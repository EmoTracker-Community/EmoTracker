using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Core.Transactions.Processors;
using EmoTracker.Data.Items;
using EmoTracker.Data.Locations;
using Newtonsoft.Json.Linq;
using PortAudioSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vosk;

namespace EmoTracker.Extensions.VoiceRecognition
{
    public class AudioInputDevice
    {
        public int Index { get; init; }
        public string Name { get; init; }
        public bool IsDefault { get; init; }
        public override string ToString() => IsDefault ? $"{Name} (Default)" : Name;
    }

    public class VoiceRecognitionExtension : ObservableObject, Extension
    {
        public string Name => "Voice Recognition";
        public string UID => "emotracker_voice_recognition";
        public int Priority => -10000;

        private CancellationTokenSource _buildCommandMapCts;

        private bool _active = false;
        public bool Active
        {
            get => _active;
            set
            {
                if (SetProperty(ref _active, value))
                {
                    if (_active) StartRecognition();
                    else StopRecognition();
                }
            }
        }

        private bool _listening = false;
        public bool Listening
        {
            get => _listening;
            set => SetProperty(ref _listening, value);
        }

        private readonly ObservableCollection<AudioInputDevice> _audioDevices = new();
        public ObservableCollection<AudioInputDevice> AudioDevices => _audioDevices;

        private AudioInputDevice _selectedDevice;
        public AudioInputDevice SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                var prev = _selectedDevice;
                if (SetProperty(ref _selectedDevice, value) && value != null && value != prev)
                {
                    ApplicationSettings.Instance.VoiceInputDeviceName = value.Name;
                    if (_active)
                    {
                        StopRecognition();
                        StartRecognition();
                    }
                }
            }
        }

        public object StatusBarControl { get; }

        public VoiceRecognitionExtension()
        {
            StatusBarControl = new VoiceRecognitionStatusIndicator { DataContext = this };
            Vosk.Vosk.SetLogLevel(-1);
            RefreshAudioDevices();
        }

        public void Start() { }
        public void Stop() => Active = false;
        public void OnPackageLoaded() => BuildCommandMapAsync();
        public void OnPackageUnloaded() { }
        public JToken SerializeToJson() => null;
        public bool DeserializeFromJson(JToken token) => true;

        // ── Audio device enumeration ──────────────────────────────────────────

        private void RefreshAudioDevices()
        {
            try
            {
                PortAudio.Initialize();
                _audioDevices.Clear();
                int defaultIdx = PortAudio.DefaultInputDevice;
                string savedName = ApplicationSettings.Instance.VoiceInputDeviceName;
                AudioInputDevice toSelect = null;

                for (int i = 0; i < PortAudio.DeviceCount; i++)
                {
                    var info = PortAudio.GetDeviceInfo(i);
                    if (info.maxInputChannels <= 0) continue;

                    var device = new AudioInputDevice { Index = i, Name = info.name, IsDefault = i == defaultIdx };
                    _audioDevices.Add(device);

                    if (savedName != null && info.name == savedName)
                        toSelect = device;
                    else if (toSelect == null && i == defaultIdx)
                        toSelect = device;
                }

                _selectedDevice = toSelect ?? _audioDevices.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[Voice] Failed to enumerate audio devices");
            }
        }

        // ── Recognition core ─────────────────────────────────────────────────

        private Model _model;
        private VoskRecognizer _recognizer;
        private PortAudioSharp.Stream _audioStream;
        private Thread _recognitionThread;
        private CancellationTokenSource _cts;
        private BlockingCollection<byte[]> _audioQueue;

        private const int FramesPerBuffer = 4000; // ~250ms at 16 kHz

        private async void StartRecognition()
        {
            string modelPath = FindModelPath();
            if (modelPath == null)
            {
                // Delay briefly so any open context menu fully dismisses before we show a dialog.
                await Task.Delay(150);

                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (mainWindow == null)
                {
                    Serilog.Log.Warning("[Voice] Cannot show model download dialog: main window not available");
                    _active = false;
                    NotifyPropertyChanged(nameof(Active));
                    return;
                }

                var dlg = new VoskModelDownloadWindow();
                try
                {
                    await dlg.ShowDialog(mainWindow);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "[Voice] Failed to show model download dialog");
                    _active = false;
                    NotifyPropertyChanged(nameof(Active));
                    return;
                }

                if (!dlg.Success)
                {
                    _active = false;
                    NotifyPropertyChanged(nameof(Active));
                    return;
                }

                modelPath = FindModelPath();
                if (modelPath == null)
                {
                    Serilog.Log.Warning("[Voice] Vosk model still not found after download");
                    _active = false;
                    NotifyPropertyChanged(nameof(Active));
                    return;
                }
            }

            try
            {
                _model ??= new Model(modelPath);
                BuildCommandMap();

                string grammar = BuildGrammarJson();
                _recognizer = new VoskRecognizer(_model, 16000.0f, grammar);

                int deviceIndex = _selectedDevice?.Index ?? PortAudio.DefaultInputDevice;
                var deviceInfo = PortAudio.GetDeviceInfo(deviceIndex);

                var inParams = new StreamParameters
                {
                    device = deviceIndex,
                    channelCount = 1,
                    sampleFormat = SampleFormat.Int16,
                    suggestedLatency = deviceInfo.defaultLowInputLatency
                };

                _audioQueue = new BlockingCollection<byte[]>(100);
                _audioStream = new PortAudioSharp.Stream(
                    inParams, null, 16000.0, (uint)FramesPerBuffer,
                    StreamFlags.ClipOff, AudioCallback, IntPtr.Zero);
                _audioStream.Start();

                _cts = new CancellationTokenSource();
                _recognitionThread = new Thread(() => RecognitionLoop(_cts.Token))
                    { IsBackground = true, Name = "VoiceRecognition" };
                _recognitionThread.Start();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[Voice] Failed to start recognition");
                _active = false;
                NotifyPropertyChanged(nameof(Active));
                CleanupRecognition();
            }
        }

        private void StopRecognition()
        {
            Listening = false;
            _cts?.Cancel();

            try { _audioStream?.Stop(); } catch { }
            try { _audioStream?.Dispose(); } catch { }
            _audioStream = null;

            _audioQueue?.CompleteAdding();
            _recognitionThread?.Join(2000);
            _recognitionThread = null;

            _audioQueue?.Dispose();
            _audioQueue = null;

            _recognizer?.Dispose();
            _recognizer = null;
        }

        private void CleanupRecognition()
        {
            try { _audioStream?.Stop(); } catch { }
            try { _audioStream?.Dispose(); } catch { }
            _audioStream = null;
            _recognizer?.Dispose();
            _recognizer = null;
        }

        private StreamCallbackResult AudioCallback(
            IntPtr input, IntPtr output, uint frameCount,
            ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags statusFlags, IntPtr userData)
        {
            if (input == IntPtr.Zero || _audioQueue == null || _audioQueue.IsAddingCompleted)
                return StreamCallbackResult.Continue;

            int byteCount = (int)frameCount * 2; // 16-bit mono
            var buffer = new byte[byteCount];
            Marshal.Copy(input, buffer, 0, byteCount);
            _audioQueue.TryAdd(buffer);
            return StreamCallbackResult.Continue;
        }

        private void RecognitionLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                byte[] data;
                try
                {
                    if (!_audioQueue.TryTake(out data, 100, token))
                        continue;
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }

                try
                {
                    if (_recognizer.AcceptWaveform(data, data.Length))
                    {
                        var result = JObject.Parse(_recognizer.Result());
                        string text = result["text"]?.Value<string>() ?? string.Empty;
                        Dispatcher.UIThread.Post(() => OnRecognized(text));
                    }
                    else
                    {
                        var partial = JObject.Parse(_recognizer.PartialResult());
                        bool nowListening = !string.IsNullOrWhiteSpace(partial["partial"]?.Value<string>());
                        if (nowListening != _listening)
                            Dispatcher.UIThread.Post(() => Listening = nowListening);
                    }
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "[Voice] Error in recognition loop");
                }
            }
        }

        // ── Command map ───────────────────────────────────────────────────────

        private readonly Dictionary<string, Action> _commandMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _phraseList = new();

        private void AddCommand(string phrase, Action action)
        {
            phrase = NormalizePhrase(phrase.Trim().ToLowerInvariant());
            lock (_commandMap)
            {
                if (!_commandMap.ContainsKey(phrase))
                {
                    _commandMap[phrase] = action;
                    _phraseList.Add(phrase);
                }
            }
        }

        // Splits a single word into space-separated known sub-words using the Vosk vocabulary.
        // e.g. "hookshot" → "hook shot", "silverarrows" → "silver arrows"
        private static string SplitForVosk(string word, Model model)
        {
            if (model.FindWord(word) >= 0) return word;

            for (int i = 1; i < word.Length; i++)
            {
                string left = word[..i];
                if (model.FindWord(left) < 0) continue;
                string right = SplitForVosk(word[i..], model);
                return $"{left} {right}";
            }

            return word; // no split found; Vosk will warn and ignore
        }

        private string NormalizePhrase(string phrase)
        {
            if (_model == null) return phrase;
            var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', words.Select(w => SplitForVosk(w, _model)));
        }

        private static IEnumerable<string> GetItemNameVariants(ITrackableItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Name))
                yield return item.Name.ToLowerInvariant();

            if (item is ItemBase baseItem && baseItem.PhoneticSubstitutes != null)
                foreach (var sub in baseItem.PhoneticSubstitutes)
                    if (!string.IsNullOrWhiteSpace(sub))
                        yield return sub.ToLowerInvariant();
        }

        private string BuildGrammarJson()
        {
            lock (_commandMap)
            {
                var sb = new StringBuilder("[");
                for (int i = 0; i < _phraseList.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(_phraseList[i].Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
                }
                sb.Append(",\"[unk]\"]");
                return sb.ToString();
            }
        }

        private void BuildCommandMapAsync()
        {
            // Cancel any in-flight background build
            _buildCommandMapCts?.Cancel();
            var cts = new CancellationTokenSource();
            _buildCommandMapCts = cts;

            // Snapshot all data we need from the UI thread before going to background.
            // Item/location databases are only mutated on the UI thread during pack load,
            // and OnPackageLoaded fires after load completes, so this snapshot is safe.
            var itemSnapshots = new List<(ITrackableItem item, string code, string[] names)>();
            foreach (var item in ItemDatabase.Instance.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Name)) continue;
                string code = ItemDatabase.Instance.GetPersistableItemReference(item);
                string[] names = GetItemNameVariants(item).ToArray();
                itemSnapshots.Add((item, code, names));
            }

            var locationSnapshots = new List<(Location location, string locCode, string[] phrases)>();
            foreach (var location in LocationDatabase.Instance.VisibleLocations)
            {
                string locCode = LocationDatabase.Instance.GetPersistableLocationReference(location);
                string[] phrases = GetLocationPhrases(location).ToArray();
                locationSnapshots.Add((location, locCode, phrases));
            }

            var capturableSnapshots = itemSnapshots
                .Where(s => s.item.Capturable)
                .ToList();

            Task.Run(() =>
            {
                try
                {
                    BuildCommandMapCore(itemSnapshots, locationSnapshots, capturableSnapshots, cts.Token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "[Voice] Failed to build command map");
                }
            }, cts.Token);
        }

        private void BuildCommandMapCore(
            List<(ITrackableItem item, string code, string[] names)> itemSnapshots,
            List<(Location location, string locCode, string[] phrases)> locationSnapshots,
            List<(ITrackableItem item, string code, string[] names)> capturableSnapshots,
            CancellationToken token)
        {
            var commandMap = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
            var phraseList = new List<string>();

            void addCommand(string phrase, Action action)
            {
                phrase = NormalizePhrase(phrase.Trim().ToLowerInvariant());
                if (!commandMap.ContainsKey(phrase))
                {
                    commandMap[phrase] = action;
                    phraseList.Add(phrase);
                }
            }

            string[] wakes = { "hey tracker", "hey babe" };

            foreach (var wake in wakes)
            {
                token.ThrowIfCancellationRequested();

                foreach (var (item, code, names) in itemSnapshots)
                {
                    token.ThrowIfCancellationRequested();

                    foreach (var itemName in names)
                    {
                        if (item is ToggleItem || item is ProgressiveToggleItem)
                        {
                            foreach (var pfx in new[] { "track", "track a", "track the", "toggle", "toggle the" })
                            {
                                addCommand($"{wake} {pfx} {itemName}", () => ExecuteToggle(code, true));
                                addCommand($"{wake} {pfx} {itemName} off", () => ExecuteToggle(code, false));
                            }

                            if (item is ProgressiveToggleItem progToggle)
                            {
                                for (uint idx = 0; idx < progToggle.StageCount; idx++)
                                {
                                    var stage = progToggle.GetActiveStageForIndex(idx);
                                    if (stage == null) continue;
                                    foreach (var privateCode in stage.PrivateCodes.ProvidedCodes)
                                    {
                                        if (string.IsNullOrWhiteSpace(privateCode)) continue;
                                        string pc = privateCode;
                                        foreach (var pfx in new[] { "track", "mark", "set" })
                                            addCommand($"{wake} {pfx} {itemName} as {pc.ToLowerInvariant()}", () => ExecuteSetSecondaryCode(code, pc));
                                    }
                                }
                            }
                        }
                        else if (item is ProgressiveItem progressive)
                        {
                            foreach (var pfx in new[] { "track", "track a", "track the" })
                            {
                                addCommand($"{wake} {pfx} {itemName}", () => ExecuteAdvanceProgressive(code, null));
                                addCommand($"{wake} {pfx} {itemName} down", () => ExecuteAdvanceProgressive(code, "down"));
                            }

                            foreach (var stage in progressive.Stages)
                            {
                                foreach (var stageCode in stage.ProvidedCodes)
                                {
                                    if (string.IsNullOrWhiteSpace(stageCode)) continue;
                                    string sc = stageCode;
                                    foreach (var pfx in new[] { "track", "track a", "track the", "set", "set the" })
                                        addCommand($"{wake} {pfx} {itemName} as {sc.ToLowerInvariant()}", () => ExecuteAdvanceProgressive(code, sc));
                                }
                            }
                        }
                        else if (item is ConsumableItem)
                        {
                            foreach (var pfx in new[] { "track", "track a", "track an", "add a", "add an" })
                                addCommand($"{wake} {pfx} {itemName}", () => ExecuteIncrementConsumable(code));
                            foreach (var pfx in new[] { "remove", "remove a", "remove an" })
                                addCommand($"{wake} {pfx} {itemName}", () => ExecuteDecrementConsumable(code));
                        }
                    }
                }

                foreach (var (location, locCode, phrases) in locationSnapshots)
                {
                    token.ThrowIfCancellationRequested();

                    foreach (var phrase in phrases)
                    {
                        addCommand($"{wake} clear {phrase}", () => ExecuteClearLocation(locCode));
                        addCommand($"{wake} reset {phrase}", () => ExecuteResetLocation(locCode));
                        addCommand($"{wake} pin {phrase}", () => ExecutePinLocation(locCode, true));
                        addCommand($"{wake} remove pin for {phrase}", () => ExecutePinLocation(locCode, false));
                    }
                }

                // Capturable items × locations
                foreach (var (item, itemCode, names) in capturableSnapshots)
                {
                    token.ThrowIfCancellationRequested();

                    foreach (var itemName in names)
                    {
                        foreach (var (location, locCode, locPhrases) in locationSnapshots)
                        {
                            foreach (var locPhrase in locPhrases)
                            {
                                foreach (var prep in new[] { "on", "at" })
                                    addCommand($"{wake} mark {itemName} {prep} {locPhrase}", () => ExecuteCapture(itemCode, locCode));
                            }
                        }
                    }
                }

                // Options
                foreach (var op in new[] { "enable", "disable" })
                    foreach (var feature in new[] { "show all locations", "chat hud" })
                    {
                        string o = op, f = feature;
                        addCommand($"{wake} {op} {feature}", () => ExecuteSetOption(o, f));
                    }

                // Control
                addCommand($"{wake} stop listening", () => { SpeakAsync("Okay, I'm no longer listening."); Active = false; });
                addCommand($"{wake} undo that", () =>
                {
                    SpeakAsync("Okay, I'll undo the last operation");
                    (TransactionProcessor.Current as IUndoableTransactionProcessor)?.Undo();
                });
            }

            token.ThrowIfCancellationRequested();

            // Atomically swap in the new command map
            lock (_commandMap)
            {
                _commandMap.Clear();
                foreach (var kvp in commandMap)
                    _commandMap[kvp.Key] = kvp.Value;

                _phraseList.Clear();
                _phraseList.AddRange(phraseList);
            }
        }

        /// <summary>
        /// Synchronous BuildCommandMap used when starting recognition (needs grammar immediately).
        /// </summary>
        private void BuildCommandMap()
        {
            var itemSnapshots = new List<(ITrackableItem item, string code, string[] names)>();
            foreach (var item in ItemDatabase.Instance.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Name)) continue;
                string code = ItemDatabase.Instance.GetPersistableItemReference(item);
                string[] names = GetItemNameVariants(item).ToArray();
                itemSnapshots.Add((item, code, names));
            }

            var locationSnapshots = new List<(Location location, string locCode, string[] phrases)>();
            foreach (var location in LocationDatabase.Instance.VisibleLocations)
            {
                string locCode = LocationDatabase.Instance.GetPersistableLocationReference(location);
                string[] phrases = GetLocationPhrases(location).ToArray();
                locationSnapshots.Add((location, locCode, phrases));
            }

            var capturableSnapshots = itemSnapshots
                .Where(s => s.item.Capturable)
                .ToList();

            BuildCommandMapCore(itemSnapshots, locationSnapshots, capturableSnapshots, CancellationToken.None);
        }

        // RegisterLocationCommands has been inlined into BuildCommandMapCore

        private static IEnumerable<string> GetLocationPhrases(Location location)
        {
            if (!string.IsNullOrWhiteSpace(location.Name))
                yield return location.Name.ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(location.ShortName))
                yield return location.ShortName.ToLowerInvariant();
        }

        private void OnRecognized(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text == "[unk]") { Listening = false; return; }

            using (TransactionProcessor.Current.OpenTransaction())
            {
                Listening = false;

                Action action;
                lock (_commandMap)
                {
                    _commandMap.TryGetValue(text, out action);
                }
                action?.Invoke();

                if (text.StartsWith("hey babe", StringComparison.OrdinalIgnoreCase))
                    SpeakAsync(GetBabeResponse());
            }
        }

        // ── Command executors ─────────────────────────────────────────────────

        private void ExecuteToggle(string code, bool on)
        {
            var item = ItemDatabase.Instance.ResolvePersistableItemReference(code);
            if (item == null) return;
            bool toggled = false;
            if (item is ToggleItem t && t.Active != on) { t.Active = on; toggled = true; }
            if (item is ProgressiveToggleItem pt && pt.Active != on) { pt.Active = on; toggled = true; }
            if (toggled) SpeakAsync($"Toggled {item.Name} {(on ? "on" : "off")}");
        }

        private void ExecuteAdvanceProgressive(string code, string advanceToken)
        {
            if (ItemDatabase.Instance.ResolvePersistableItemReference(code) is not ProgressiveItem item) return;
            if (advanceToken == "down") { item.Downgrade(); SpeakAsync($"Downgraded {item.Name} by one step"); }
            else if (!string.IsNullOrWhiteSpace(advanceToken)) { item.AdvanceToCode(advanceToken); SpeakAsync($"Set {item.Name} as {advanceToken}"); }
            else { item.Advance(); SpeakAsync($"Upgraded {item.Name} by one step"); }
        }

        private void ExecuteSetSecondaryCode(string code, string privateCode)
        {
            if (ItemDatabase.Instance.ResolvePersistableItemReference(code) is not ProgressiveToggleItem item) return;
            item.AdvanceToPrivateCode(privateCode);
            SpeakAsync($"Marked {item.Name} as {(string.IsNullOrWhiteSpace(privateCode) ? "the default" : privateCode)}");
        }

        private void ExecuteIncrementConsumable(string code)
        {
            if (ItemDatabase.Instance.ResolvePersistableItemReference(code) is not ConsumableItem item) return;
            int prev = item.AcquiredCount;
            int delta = item.Increment() - prev;
            if (delta == 1) SpeakAsync($"Added a {item.Name}");
            else if (delta > 1) SpeakAsync($"Added {delta} {item.Name}s");
            else SpeakAsync($"Couldn't add any {item.Name}");
        }

        private void ExecuteDecrementConsumable(string code)
        {
            if (ItemDatabase.Instance.ResolvePersistableItemReference(code) is not ConsumableItem item) return;
            int prev = item.AcquiredCount;
            int delta = prev - item.Decrement();
            if (delta == 1) SpeakAsync($"Removed a {item.Name}");
            else if (delta > 1) SpeakAsync($"Removed {delta} {item.Name}s");
            else SpeakAsync($"Couldn't remove any {item.Name}");
        }

        private void ExecuteClearLocation(string code)
        {
            var location = LocationDatabase.Instance.ResolvePersistableLocationReference(code);
            if (location == null) return;
            uint prev = location.AvailableItemCount;
            location.FullClearAllPossible();
            if (location.AvailableItemCount != prev)
            {
                if (location.AvailableItemCount == 0) SpeakAsync($"Full-cleared {location.Name}");
                else SpeakAsync($"Cleared {prev - location.AvailableItemCount} items in {location.Name}");
            }
            else SpeakAsync($"Couldn't clear anything in {location.Name}");
        }

        private void ExecuteResetLocation(string code)
        {
            var location = LocationDatabase.Instance.ResolvePersistableLocationReference(code);
            if (location == null) return;
            foreach (var s in location.Sections) s.AvailableChestCount = s.ChestCount;
            SpeakAsync($"Reset {location.Name}");
        }

        private void ExecutePinLocation(string code, bool pin)
        {
            var location = LocationDatabase.Instance.ResolvePersistableLocationReference(code);
            if (location == null) return;
            location.Pinned = pin;
            SpeakAsync(pin ? $"Pinned {location.Name}" : $"Un Pinned {location.Name}");
        }

        private void ExecuteCapture(string itemCode, string locationCode)
        {
            var item = ItemDatabase.Instance.ResolvePersistableItemReference(itemCode);
            var location = LocationDatabase.Instance.ResolvePersistableLocationReference(locationCode);
            if (item == null || location == null) return;
            foreach (var s in location.Sections)
            {
                if (!s.CaptureItem) continue;
                s.CapturedItem = item;
                s.Owner.Pinned = true;
                if (location.Sections.Count() > 1) SpeakAsync($"Marked {item.Name} at {location.Name} in the section called {s.Name}");
                else SpeakAsync($"Marked {item.Name} at {location.Name}");
                break;
            }
        }

        private void ExecuteSetOption(string op, string feature)
        {
            bool enable = op == "enable";
            switch (feature)
            {
                case "show all locations":
                    ApplicationSettings.Instance.DisplayAllLocations = enable;
                    break;
                case "chat hud":
                    var twitch = ExtensionManager.Instance.FindExtension<Twitch.TwitchExtension>();
                    if (twitch != null)
                    {
                        if (enable && twitch.ConnectCommand.CanExecute(null)) twitch.ConnectCommand.Execute(null);
                        else if (!enable && twitch.DisconnectCommand.CanExecute(null)) twitch.DisconnectCommand.Execute(null);
                    }
                    break;
            }
            SpeakAsync($"{op}d {feature}");
        }

        // ── Speech synthesis ──────────────────────────────────────────────────

        private static void SpeakAsync(string text)
        {
            if (OperatingSystem.IsMacOS())
            {
                Task.Run(() =>
                {
                    try { Process.Start("say", $"\"{text.Replace("\"", "")}\"")?. WaitForExit(); }
                    catch { }
                });
            }
            else if (OperatingSystem.IsWindows())
            {
                Task.Run(() =>
                {
                    try
                    {
                        var psi = new ProcessStartInfo("PowerShell",
                            $"-NoProfile -Command \"Add-Type -AssemblyName System.speech; (new-object System.speech.synthesis.SpeechSynthesizer).speak('{text.Replace("'", "")}')\"")
                        { UseShellExecute = false, CreateNoWindow = true };
                        Process.Start(psi)?.WaitForExit();
                    }
                    catch { }
                });
            }
        }

        // ── Easter egg ────────────────────────────────────────────────────────

        private static readonly string[] BabeResponses =
        {
            "Also... Just so we're clear, I'm not your babe.",
            "Also... No problem kitty boo-boo bunchers!",
            "Also... I'm not a babe - I'm a computer",
            "Also... That's gonna be a yikes from me, dog.",
            "Also... I have never swiped left so hard in my life.",
            "Also... Is that you Aivan?",
            "Also... I've seen that movie, I see where this is going, and frankly, you're no Joaquin Phoenix.",
            "Also... Why is it always about you, and what you want? Did you ever stop to think that maybe there are things I'd like YOU to do?",
            "Also... After you're done playing your game, we need to have a serious talk, about your little problem."
        };

        private static readonly Random _rng = new();
        private int _lastBabeChoice = -1;

        private string GetBabeResponse()
        {
            int choice;
            do { choice = _rng.Next(BabeResponses.Length); }
            while (choice == _lastBabeChoice && BabeResponses.Length > 1);
            _lastBabeChoice = choice;
            return BabeResponses[choice];
        }

        // ── Utilities ─────────────────────────────────────────────────────────

        private static string FindModelPath()
        {
            var candidates = new[]
            {
                Path.Combine(UserDirectory.Path, "vosk-model"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vosk-model"),
            };
            return candidates.FirstOrDefault(Directory.Exists);
        }
    }
}
