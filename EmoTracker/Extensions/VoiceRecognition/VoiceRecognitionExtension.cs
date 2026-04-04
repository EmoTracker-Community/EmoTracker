using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.Core.Transactions;
using EmoTracker.Data.Core.Transactions.Processors;
using EmoTracker.Data.Items;
using EmoTracker.Data.Locations;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Extensions.VoiceRecognition
{
    public class VoiceRecognitionExtension : ObservableObject, Extension
    {
        public string Name
        {
            get { return "Voice Recognition"; }
        }

        public string UID
        {
            get { return "emotracker_voice_recognition"; }
        }

        public int Priority { get { return -10000; } }

        bool mbActive = false;
        public bool Active
        {
            get { return mbActive; }
            set
            {
                if (SetProperty(ref mbActive, value))
                {
                    if (mbActive)
                    {
                        synth = new SpeechSynthesizer();
                        synth.SetOutputToDefaultAudioDevice();
                        synth.SelectVoiceByHints(VoiceGender.Female, VoiceAge.Adult, 0, new System.Globalization.CultureInfo("en-us"));

                        sre = new SpeechRecognitionEngine();
                        sre.SetInputToDefaultAudioDevice();
                        sre.SpeechDetected += Sre_SpeechDetected;
                        sre.SpeechRecognitionRejected += Sre_SpeechRecognitionRejected;
                        sre.SpeechRecognized += Sre_SpeechRecognized;

                        ConfigureSpeechRecognition();
                        sre.RecognizeAsync(RecognizeMode.Multiple);
                    }
                    else
                    {
                        if (sre != null)
                        {
                            sre.SpeechDetected -= Sre_SpeechDetected;
                            sre.SpeechRecognitionRejected -= Sre_SpeechRecognitionRejected;
                            sre.SpeechRecognized -= Sre_SpeechRecognized;

                            sre.RecognizeAsyncCancel();
                        }

                        DisposeObjectAndDefault(ref sre);
                        DisposeObjectAndDefault(ref synth);

                        Listening = false;
                    }
                }
            }
        }

        bool mbListening = false;
        public bool Listening
        {
            get { return mbListening; }
            set { SetProperty(ref mbListening, value); }
        }

        public object StatusBarControl
        {
            get; set;
        }

        public JToken SerializeToJson()
        {
            return null;
        }

        public bool DeserializeFromJson()
        {
            return true;
        }

        public VoiceRecognitionExtension()
        {
            StatusBarControl = new VoiceRecognitionStatusIndicator() { DataContext = this };
        }

        SpeechRecognitionEngine sre;
        SpeechSynthesizer synth;
        

        public void Start()
        {
        }

        private void Sre_SpeechRecognitionRejected(object sender, SpeechRecognitionRejectedEventArgs e)
        {
            Listening = false;
        }

        private void Sre_SpeechDetected(object sender, SpeechDetectedEventArgs e)
        {
            Listening = true;
        }

        private void Sre_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            using (TransactionProcessor.Current.OpenTransaction())
            {
                Listening = false;

                if (e.Result.Confidence < 0.9f)
                    return;

                if (e.Result.Grammar.Name == "Chatter")
                {
                    Console.Out.WriteLine("Chatter: {0}", e.Result.Text);
                    return;
                }

                Console.Out.WriteLine(e.Result.Text);

                if (e.Result.Grammar.Name == "Toggle")
                {
                    ITrackableItem item = ItemDatabase.Instance.ResolvePersistableItemReference(e.Result.Semantics["item"].Value.ToString());

                    bool bToggled = false;
                    bool bNewState = e.Result.Semantics["toggle_mode"].Value.ToString() != "off";

                    ToggleItem toggle = item as ToggleItem;
                    if (toggle != null)
                    {
                        if (toggle.Active != bNewState)
                        {
                            toggle.Active = bNewState;
                            bToggled = true;
                        }
                    }

                    ProgressiveToggleItem progToggle = item as ProgressiveToggleItem;
                    if (progToggle != null)
                    {
                        if (progToggle.Active != bNewState)
                        {
                            progToggle.Active = bNewState;
                            bToggled = true;
                        }
                    }

                    if (bToggled)
                    {
                        synth.SpeakAsync(string.Format("Toggled {0} {1}", item.Name, bNewState ? "on" : "off"));
                    }
                }

                if (e.Result.Grammar.Name == "Advance Progressive")
                {
                    ITrackableItem item = ItemDatabase.Instance.ResolvePersistableItemReference(e.Result.Semantics["item"].Value.ToString());

                    ProgressiveItem progressive = item as ProgressiveItem;
                    if (progressive != null)
                    {
                        string advanceToken = "";
                        if (e.Result.Semantics.ContainsKey("advance_mode"))
                            advanceToken = e.Result.Semantics["advance_mode"].Value.ToString();

                        if (e.Result.Semantics.ContainsKey("code"))
                            advanceToken = e.Result.Semantics["code"].Value.ToString();

                        if (advanceToken == "down")
                        {
                            progressive.Downgrade();
                            synth.SpeakAsync(string.Format("Downgraded {0} by one step", item.Name));
                        }
                        else if (!string.IsNullOrWhiteSpace(advanceToken))
                        {
                            progressive.AdvanceToCode(advanceToken);
                            synth.SpeakAsync(string.Format("Set {0} as {1}", item.Name, advanceToken));
                        }
                        else
                        {
                            progressive.Advance();
                            synth.SpeakAsync(string.Format("Upgraded {0} by one step", item.Name));
                        }
                    }
                }

                if (e.Result.Grammar.Name == "Increment Consumable")
                {
                    ITrackableItem item = ItemDatabase.Instance.ResolvePersistableItemReference(e.Result.Semantics["item"].Value.ToString());

                    ConsumableItem consumable = item as ConsumableItem;
                    if (consumable != null)
                    {
                        int acquired = consumable.AcquiredCount;
                        int delta = consumable.Increment() - acquired;

                        if (delta == 1)
                            synth.SpeakAsync(string.Format("Added a {0}", item.Name));
                        else if (delta > 1)
                            synth.SpeakAsync(string.Format("Added {0} {1}s", delta, item.Name));
                        else
                            synth.SpeakAsync(string.Format("Couldn't add any {0}", item.Name));
                    }
                }

                if (e.Result.Grammar.Name == "Decrement Consumable")
                {
                    ITrackableItem item = ItemDatabase.Instance.ResolvePersistableItemReference(e.Result.Semantics["item"].Value.ToString());

                    ConsumableItem consumable = item as ConsumableItem;
                    if (consumable != null)
                    {
                        int acquired = consumable.AcquiredCount;
                        int delta = acquired - consumable.Decrement();

                        if (delta == 1)
                            synth.SpeakAsync(string.Format("Removed a {0}", item.Name));
                        else if (delta > 1)
                            synth.SpeakAsync(string.Format("Removed {0} {1}s", delta, item.Name));
                        else
                            synth.SpeakAsync(string.Format("Couldn't remove any {0}", item.Name));
                    }
                }

                if (e.Result.Grammar.Name == "Clear")
                {
                    Location location = LocationDatabase.Instance.ResolvePersistableLocationReference(e.Result.Semantics["location"].Value.ToString());
                    if (location != null)
                    {
                        uint prevCount = location.AvailableItemCount;
                        location.FullClearAllPossible();
                        if (location.AvailableItemCount != prevCount)
                        {
                            if (location.AvailableItemCount == 0)
                                synth.SpeakAsync(string.Format("Full-cleared {0}", location.Name));
                            else if (prevCount > location.AvailableItemCount)
                                synth.SpeakAsync(string.Format("Cleared {0} items in {1}", prevCount - location.AvailableItemCount, location.Name));
                        }
                        else
                        {
                            synth.SpeakAsync(string.Format("Couldn't clear anything in {0}", location.Name));
                        }
                    }
                }

                if (e.Result.Grammar.Name == "Reset Location")
                {
                    Location location = LocationDatabase.Instance.ResolvePersistableLocationReference(e.Result.Semantics["location"].Value.ToString());
                    if (location != null)
                    {
                        foreach (Section s in location.Sections)
                        {
                            s.AvailableChestCount = s.ChestCount;
                        }

                        synth.SpeakAsync(string.Format("Reset {0}", location.Name));
                    }
                }

                if (e.Result.Grammar.Name == "Pin")
                {
                    Location location = LocationDatabase.Instance.ResolvePersistableLocationReference(e.Result.Semantics["location"].Value.ToString());
                    if (location != null)
                    {
                        location.Pinned = true;
                        synth.SpeakAsync(string.Format("Pinned {0}", location.Name));
                    }
                }

                if (e.Result.Grammar.Name == "Unpin")
                {
                    Location location = LocationDatabase.Instance.ResolvePersistableLocationReference(e.Result.Semantics["location"].Value.ToString());
                    if (location != null && location.Pinned)
                    {
                        location.Pinned = false;
                        synth.SpeakAsync(string.Format("Un Pinned {0}", location.Name));
                    }
                }

                if (e.Result.Grammar.Name == "Capture")
                {
                    ITrackableItem item = ItemDatabase.Instance.ResolvePersistableItemReference(e.Result.Semantics["item"].Value.ToString());
                    Location location = LocationDatabase.Instance.ResolvePersistableLocationReference(e.Result.Semantics["location"].Value.ToString());

                    if (item != null && location != null)
                    {
                        foreach (Section s in location.Sections)
                        {
                            if (s.CaptureItem)
                            {
                                s.CapturedItem = item;
                                s.Owner.Pinned = true;

                                if (location.Sections.Count() > 1)
                                    synth.SpeakAsync(string.Format("Marked {0} at {1} in the section called {2}", item.Name, location.Name, s.Name));
                                else
                                    synth.SpeakAsync(string.Format("Marked {0} at {1}", item.Name, location.Name));

                                break;
                            }
                        }
                    }
                }

                if (e.Result.Grammar.Name == "Set Secondary Code")
                {
                    ITrackableItem item = ItemDatabase.Instance.ResolvePersistableItemReference(e.Result.Semantics["item"].Value.ToString());
                    string secondaryCode = e.Result.Semantics["secondary_code"].Value.ToString();

                    ProgressiveToggleItem progToggle = item as ProgressiveToggleItem;
                    if (progToggle != null)
                    {
                        uint currStage = progToggle.CurrentStage;
                        progToggle.AdvanceToPrivateCode(secondaryCode);
                        synth.SpeakAsync(string.Format("Marked {0} as {1}", progToggle.Name, !string.IsNullOrWhiteSpace(secondaryCode) ? secondaryCode : "the default"));
                    }
                }

                if (e.Result.Grammar.Name == "Set Option")
                {
                    string op = e.Result.Semantics["op"].Value.ToString();
                    string feature = e.Result.Semantics["feature"].Value.ToString();

                    bool bEnable = op == "enable";

                    switch (feature)
                    {
                        case "show all locations":
                            ApplicationSettings.Instance.DisplayAllLocations = bEnable;
                            break;

                        case "chat hud":
                            Extensions.Twitch.TwitchExtension twitch = ExtensionManager.Instance.FindExtension<Twitch.TwitchExtension>();
                            if (twitch != null)
                            {
                                if (bEnable && twitch.ConnectCommand.CanExecute(null))
                                {
                                    twitch.ConnectCommand.Execute(null);
                                }
                                else if (twitch.DisconnectCommand.CanExecute(null))
                                {
                                    twitch.DisconnectCommand.Execute(null);
                                }
                            }
                            break;
                    }

                    synth.SpeakAsync(string.Format("{0}d {1}", op, feature));
                }

                if (e.Result.Grammar.Name == "Stop Listening")
                {
                    synth.Speak("Okay, I'm no longer listening.");
                    Active = false;
                }
                else if (e.Result.Grammar.Name == "Undo")
                {
                    synth.SpeakAsync("Okay, I'll undo the last operation");

                    IUndoableTransactionProcessor undo = TransactionProcessor.Current as IUndoableTransactionProcessor;
                    if (undo != null)
                        undo.Undo();
                }
                else if (e.Result.Text.StartsWith("Hey Babe"))
                {
                    string[] babeResponses = new string[]
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

                    int choice = RNG.Next(babeResponses.Length);
                    while (choice == mLastBabeChoice)
                        choice = RNG.Next(babeResponses.Length);
                    mLastBabeChoice = choice;

                    synth.SpeakAsync(babeResponses[choice]);
                }
            }
        }

        Random RNG = new Random();
        int mLastBabeChoice = -1;

        public void Stop()
        {
        }

        public void OnPackageLoaded()
        {
            ConfigureSpeechRecognition();
        }

        private void AddLocationToChoices(Location location, Choices choices)
        {
            if (location != null)
            {
                choices.Add(new SemanticResultValue(location.Name, LocationDatabase.Instance.GetPersistableLocationReference(location)));

                if (!string.IsNullOrEmpty(location.ShortName))
                    choices.Add(new SemanticResultValue(location.ShortName, LocationDatabase.Instance.GetPersistableLocationReference(location)));
            }
        }

        private void ConfigureSpeechRecognition()
        {
            if (!Active || sre == null || synth == null)
                return;

            try
            {
                sre.UnloadAllGrammars();
            }
            catch
            {
            }

            var culture = new System.Globalization.CultureInfo("en-us");

            GrammarBuilder baseGrammar = new GrammarBuilder();
            baseGrammar.Culture = culture;
            baseGrammar.Append(new Choices("Hey Tracker", "Hey Babe"));

            //  toggleable items
            try
            {
                GrammarBuilder grammar = new GrammarBuilder();
                grammar.Culture = culture;
                grammar.Append(baseGrammar);
                grammar.Append(new Choices("track", "track a", "track the", "toggle", "toggle the"));

                Choices toggleItemChoices = new Choices();
                foreach (ITrackableItem item in ItemDatabase.Instance.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.Name))
                        continue;

                    ToggleItem toggle = item as ToggleItem;
                    if (toggle != null)
                        toggleItemChoices.Add(new SemanticResultValue(toggle.Name, ItemDatabase.Instance.GetPersistableItemReference(toggle)));

                    ProgressiveToggleItem progToggle = item as ProgressiveToggleItem;
                    if (progToggle != null)
                        toggleItemChoices.Add(new SemanticResultValue(progToggle.Name, ItemDatabase.Instance.GetPersistableItemReference(progToggle)));
                }
                grammar.Append(new SemanticResultKey("item", toggleItemChoices));

                Choices toggleOpChoices = new Choices();
                toggleOpChoices.Add(new GrammarBuilder(), new GrammarBuilder("off"));
                grammar.Append(new SemanticResultKey("toggle_mode", toggleOpChoices));

                sre.LoadGrammarAsync(new Grammar(grammar) { Name = "Toggle" });
            }
            catch
            {
            }

            //  progressive items
            try
            {
                //  Basic progress
                {
                    GrammarBuilder grammar = new GrammarBuilder();
                    grammar.Culture = culture;
                    grammar.Append(baseGrammar);
                    grammar.Append(new Choices("track", "track a", "track the"));

                    Choices itemChoices = new Choices();
                    foreach (ITrackableItem item in ItemDatabase.Instance.Items)
                    {
                        if (string.IsNullOrWhiteSpace(item.Name))
                            continue;

                        ProgressiveItem progressive = item as ProgressiveItem;
                        if (progressive != null)
                            itemChoices.Add(new SemanticResultValue(progressive.Name, ItemDatabase.Instance.GetPersistableItemReference(progressive)));
                    }
                    grammar.Append(new SemanticResultKey("item", itemChoices));

                    Choices advanceOpChoices = new Choices();
                    advanceOpChoices.Add(new GrammarBuilder(), new GrammarBuilder("down"));
                    grammar.Append(new SemanticResultKey("advance_mode", advanceOpChoices));

                    sre.LoadGrammarAsync(new Grammar(grammar) { Name = "Advance Progressive" });
                }

                //  Stage Progression
                {

                    foreach (ITrackableItem item in ItemDatabase.Instance.Items)
                    {
                        if (string.IsNullOrWhiteSpace(item.Name))
                            continue;

                        ProgressiveItem progressive = item as ProgressiveItem;
                        if (progressive != null)
                        {
                            GrammarBuilder grammar = new GrammarBuilder();
                            grammar.Culture = culture;
                            grammar.Append(baseGrammar);
                            grammar.Append(new Choices("track", "track a", "track the", "set", "set the"));

                            Choices itemChoices = new Choices(new SemanticResultValue(progressive.Name, ItemDatabase.Instance.GetPersistableItemReference(progressive)));
                            grammar.Append(new SemanticResultKey("item", itemChoices));
                            grammar.Append("as");

                            Choices advanceOpChoices = new Choices();
                            int choiceCount = 0;
                            foreach (ProgressiveItem.Stage s in progressive.Stages)
                            {
                                foreach (string code in s.ProvidedCodes)
                                {
                                    if (!string.IsNullOrWhiteSpace(code))
                                    {
                                        advanceOpChoices.Add(code);
                                        ++choiceCount;
                                    }
                                }
                            }

                            if (choiceCount > 0)
                            {
                                grammar.Append(new SemanticResultKey("code", advanceOpChoices));
                                sre.LoadGrammarAsync(new Grammar(grammar) { Name = "Advance Progressive" });
                            }
                        }
                    }
                }
            }
            catch
            {
            }


            //  progressive toggle marking
            try
            {
                foreach (ITrackableItem item in ItemDatabase.Instance.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.Name))
                        continue;

                    ProgressiveToggleItem progToggle = item as ProgressiveToggleItem;
                    if (progToggle != null)
                    {
                        GrammarBuilder grammar = new GrammarBuilder();
                        grammar.Culture = culture;
                        grammar.Append(baseGrammar);
                        grammar.Append(new Choices("track", "mark", "set"));

                        Choices itemChoices = new Choices(new SemanticResultValue(progToggle.Name, ItemDatabase.Instance.GetPersistableItemReference(progToggle)));
                        grammar.Append(new SemanticResultKey("item", itemChoices));
                        grammar.Append("as");

                        Choices secondaryCodeChoices = new Choices();
                        int secondaryChoiceCount = 0;
                        for (uint i = 0; i < progToggle.StageCount; ++i)
                        {
                            ProgressiveToggleItem.Stage s = progToggle.GetActiveStageForIndex(i);
                            if (s != null)
                            {
                                foreach (string privateCode in s.PrivateCodes.ProvidedCodes)
                                {
                                    secondaryCodeChoices.Add(privateCode);
                                    ++secondaryChoiceCount;
                                }
                            }
                        }

                        if (secondaryChoiceCount > 0)
                        {
                            grammar.Append(new SemanticResultKey("secondary_code", secondaryCodeChoices));
                            sre.LoadGrammarAsync(new Grammar(grammar) { Name = "Set Secondary Code" });
                        }
                    }
                }
            }
            catch
            {
            }

            //  Consumable item increment
            try
            {
                GrammarBuilder grammar = new GrammarBuilder();
                grammar.Culture = culture;
                grammar.Append(baseGrammar);
                grammar.Append(new Choices("track", "track a", "track an", "add a", "add an"));

                Choices itemChoices = new Choices();
                foreach (ITrackableItem item in ItemDatabase.Instance.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.Name))
                        continue;

                    ConsumableItem consumable = item as ConsumableItem;
                    if (consumable != null)
                        itemChoices.Add(new SemanticResultValue(consumable.Name, ItemDatabase.Instance.GetPersistableItemReference(consumable)));
                }

                grammar.Append(new SemanticResultKey("item", itemChoices));
                sre.LoadGrammarAsync(new Grammar(grammar) { Name = "Increment Consumable" });
            }
            catch
            {
            }

            //  Consumable item Decrement
            try
            {
                GrammarBuilder grammar = new GrammarBuilder();
                grammar.Culture = culture;
                grammar.Append(baseGrammar);
                grammar.Append(new Choices("remove", "remove a", "remove an"));

                Choices itemChoices = new Choices();
                foreach (ITrackableItem item in ItemDatabase.Instance.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.Name))
                        continue;

                    ConsumableItem consumable = item as ConsumableItem;
                    if (consumable != null && !string.IsNullOrWhiteSpace(consumable.Name))
                        itemChoices.Add(new SemanticResultValue(consumable.Name, ItemDatabase.Instance.GetPersistableItemReference(consumable)));
                }

                grammar.Append(new SemanticResultKey("item", itemChoices));
                sre.LoadGrammarAsync(new Grammar(grammar) { Name = "Decrement Consumable" });
            }
            catch
            {
            }

            //  location clearing
            try
            {
                GrammarBuilder grammar = new GrammarBuilder();
                grammar.Culture = culture;
                grammar.Append(baseGrammar);
                grammar.Append(new Choices("clear"));

                Choices locationChoices = new Choices();
                foreach (Location location in LocationDatabase.Instance.VisibleLocations)
                {
                    AddLocationToChoices(location, locationChoices);
                }
                grammar.Append(new SemanticResultKey("location", locationChoices));

                sre.LoadGrammarAsync(new Grammar(grammar) { Name = "Clear" });
            }
            catch
            {
            }

            //  location reset
            try
            {
                GrammarBuilder grammar = new GrammarBuilder();
                grammar.Culture = culture;
                grammar.Append(baseGrammar);
                grammar.Append(new Choices("reset"));

                Choices locationChoices = new Choices();
                foreach (Location location in LocationDatabase.Instance.VisibleLocations)
                {
                    AddLocationToChoices(location, locationChoices);
                }
                grammar.Append(new SemanticResultKey("location", locationChoices));

                sre.LoadGrammarAsync(new Grammar(grammar) { Name = "Reset Location" });
            }
            catch
            {
            }

            //  location pinning
            try
            {
                GrammarBuilder grammar = new GrammarBuilder();
                grammar.Culture = culture;
                grammar.Append(baseGrammar);
                grammar.Append(new Choices("pin"));

                Choices locationChoices = new Choices();
                foreach (Location location in LocationDatabase.Instance.VisibleLocations)
                {
                    AddLocationToChoices(location, locationChoices);
                }
                grammar.Append(new SemanticResultKey("location", locationChoices));

                sre.LoadGrammarAsync(new Grammar(grammar) { Name = "Pin" });
            }
            catch
            {
            }

            //  location pinning
            try
            {
                GrammarBuilder grammar = new GrammarBuilder();
                grammar.Culture = culture;
                grammar.Append(baseGrammar);
                grammar.Append(new Choices("remove pin for"));

                Choices locationChoices = new Choices();
                foreach (Location location in LocationDatabase.Instance.VisibleLocations)
                {
                    AddLocationToChoices(location, locationChoices);
                }
                grammar.Append(new SemanticResultKey("location", locationChoices));

                sre.LoadGrammarAsync(new Grammar(grammar) { Name = "Unpin" });
            }
            catch
            {
            }

            //  location notes
            try
            {
                GrammarBuilder grammar = new GrammarBuilder();
                grammar.Culture = culture;
                grammar.Append(baseGrammar);
                grammar.Append("make a note on");

                Choices locationChoices = new Choices();
                foreach (Location location in LocationDatabase.Instance.VisibleLocations)
                {
                    AddLocationToChoices(location, locationChoices);
                }
                grammar.Append(new SemanticResultKey("location", locationChoices));

                sre.LoadGrammarAsync(new Grammar(grammar) { Name = "Note" });
            }
            catch
            {
            }

            //  location marking
            try
            {
                GrammarBuilder grammar = new GrammarBuilder();
                grammar.Culture = culture;
                grammar.Append(baseGrammar);
                grammar.Append(new Choices("mark"));

                Choices itemChoices = new Choices();
                foreach (ITrackableItem item in ItemDatabase.Instance.Items)
                {
                    if (item.Capturable && !string.IsNullOrWhiteSpace(item.Name))
                        itemChoices.Add(new SemanticResultValue(item.Name, ItemDatabase.Instance.GetPersistableItemReference(item)));
                }

                Choices locationChoices = new Choices();
                foreach (Location location in LocationDatabase.Instance.VisibleLocations)
                {
                    AddLocationToChoices(location, locationChoices);
                }

                grammar.Append(new SemanticResultKey("item", itemChoices));
                grammar.Append(new Choices("on", "at"));
                grammar.Append(new SemanticResultKey("location", locationChoices));

                sre.LoadGrammarAsync(new Grammar(grammar) { Name = "Capture" });
            }
            catch
            {
            }

            try
            {
                //  options setting
                {
                    GrammarBuilder grammar = new GrammarBuilder();
                    grammar.Culture = culture;
                    grammar.Append(baseGrammar);
                    grammar.Append(new SemanticResultKey("op", new Choices("enable", "disable")));
                    grammar.Append(new SemanticResultKey("feature", new Choices("show all locations", "chat hud")));

                    sre.LoadGrammarAsync(new Grammar(grammar) { Name = "Set Option" });
                }

                //  options setting
                {
                    GrammarBuilder grammar = new GrammarBuilder();
                    grammar.Culture = culture;
                    grammar.Append(baseGrammar);
                    grammar.Append("stop listening");

                    sre.LoadGrammarAsync(new Grammar(grammar) { Name = "Stop Listening" });
                }

                //  options setting
                {
                    GrammarBuilder grammar = new GrammarBuilder();
                    grammar.Culture = culture;
                    grammar.Append(baseGrammar);
                    grammar.Append("undo that");

                    sre.LoadGrammarAsync(new Grammar(grammar) { Name = "Undo" });
                }

                //  chatter
                {
                    sre.LoadGrammarAsync(new DictationGrammar() { Name = "Chatter" });
                }
            }
            catch
            {
            }
        }

        public void OnPackageUnloaded()
        {
        }

        public bool DeserializeFromJson(JToken token)
        {
            return true;
        }
    }
}
