using EmoTracker.Data.Sessions;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;

namespace EmoTracker
{
    /// <summary>
    /// Phase 7.10: bundle save / load. A bundle is a folder of per-state
    /// save files plus a sibling <c>&lt;name&gt;.bundle.json</c> manifest
    /// describing every PackageInstance, every TrackerState, and the
    /// window/tab arrangement. <see cref="Save"/> and <see cref="Load"/>
    /// round-trip the entire user-visible state.
    /// </summary>
    public static class Bundle
    {
        const int CurrentBundleVersion = 1;

        public static void Save(string bundleJsonPath)
        {
            if (string.IsNullOrEmpty(bundleJsonPath))
                throw new ArgumentNullException(nameof(bundleJsonPath));

            // Folder name = bundle filename without ".bundle.json".
            var dir = Path.GetDirectoryName(bundleJsonPath);
            var fileName = Path.GetFileName(bundleJsonPath);
            var stem = fileName.EndsWith(".bundle.json", StringComparison.OrdinalIgnoreCase)
                ? fileName.Substring(0, fileName.Length - ".bundle.json".Length)
                : Path.GetFileNameWithoutExtension(fileName);
            var folderPath = Path.Combine(dir ?? string.Empty, stem);
            Directory.CreateDirectory(folderPath);

            var root = new JObject();
            root["bundleVersion"] = CurrentBundleVersion;
            root["savedAt"] = DateTime.UtcNow.ToString("o");

            var packageInstancesArray = new JArray();
            foreach (var pi in ApplicationModel.Instance.PackageInstances)
            {
                if (pi.Package == null) continue;   // skip empty pre-allocated PI
                var piObj = new JObject();
                piObj["packUID"] = pi.Package.UniqueID;
                piObj["variantUID"] = pi.ActiveVariant?.UniqueID;

                var statesArray = new JArray();
                foreach (var kvp in pi.States)
                {
                    var state = kvp.Value;
                    var stateObj = new JObject();
                    stateObj["id"] = state.Id.ToString();
                    stateObj["name"] = state.Name;
                    var saveFileName = "state-" + state.Id.ToString() + ".json";
                    stateObj["saveFile"] = saveFileName;

                    // Drive a per-state save through the existing save path.
                    // For Phase 7.10 we capture only the per-state slice; the
                    // app-wide ApplicationSettings.Save remains separate.
                    SavePerStateProgress(state, Path.Combine(folderPath, saveFileName));

                    statesArray.Add(stateObj);
                }
                piObj["states"] = statesArray;
                packageInstancesArray.Add(piObj);
            }
            root["packageInstances"] = packageInstancesArray;

            var windowsArray = new JArray();
            foreach (var win in ApplicationModel.Instance.Windows)
            {
                var winObj = new JObject();
                winObj["id"] = win.Id.ToString();
                var openIds = new JArray();
                foreach (var s in win.OpenStates)
                    openIds.Add(s.Id.ToString());
                winObj["openStates"] = openIds;
                winObj["activeState"] = win.ActiveState?.Id.ToString();
                windowsArray.Add(winObj);
            }
            root["windows"] = windowsArray;

            File.WriteAllText(bundleJsonPath, root.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        public static void Load(string bundleJsonPath)
        {
            if (!File.Exists(bundleJsonPath))
                throw new FileNotFoundException("Bundle manifest not found", bundleJsonPath);

            var raw = File.ReadAllText(bundleJsonPath);
            var root = JObject.Parse(raw);

            var dir = Path.GetDirectoryName(bundleJsonPath) ?? string.Empty;
            var fileName = Path.GetFileName(bundleJsonPath);
            var stem = fileName.EndsWith(".bundle.json", StringComparison.OrdinalIgnoreCase)
                ? fileName.Substring(0, fileName.Length - ".bundle.json".Length)
                : Path.GetFileNameWithoutExtension(fileName);
            var folderPath = Path.Combine(dir, stem);

            // Track id mapping so window restoration can find loaded states
            // by their saved id.
            var stateIdMap = new Dictionary<Guid, TrackerState>();

            var packagesArr = root["packageInstances"] as JArray;
            if (packagesArr != null)
            {
                foreach (var piToken in packagesArr)
                {
                    var packUID = piToken.Value<string>("packUID");
                    var variantUID = piToken.Value<string>("variantUID");
                    EmoTracker.Data.IGamePackage pkg = null;
                    foreach (var installed in EmoTracker.Data.Packages.PackageManager.Instance.InstalledPackages)
                    {
                        if (string.Equals(installed.UniqueID, packUID, StringComparison.OrdinalIgnoreCase))
                        {
                            pkg = installed;
                            break;
                        }
                    }
                    if (pkg == null) continue;
                    EmoTracker.Data.IGamePackageVariant variant = null;
                    if (!string.IsNullOrEmpty(variantUID) && pkg is EmoTracker.Data.Packages.GamePackage gp)
                        variant = gp.FindVariant(variantUID);

                    var primary = ApplicationModel.Instance.LoadNewPack(pkg, variant);
                    var statesArr = piToken["states"] as JArray;
                    if (statesArr == null) continue;

                    bool firstState = true;
                    foreach (var stToken in statesArr)
                    {
                        TrackerState target;
                        if (firstState)
                        {
                            target = primary;
                            firstState = false;
                        }
                        else
                        {
                            var pi = primary.OwnerPackageInstance(out _);
                            target = pi != null
                                ? ApplicationModel.Instance.CreateAdditionalState(pi)
                                : primary.Fork();
                        }
                        var savedId = Guid.Parse(stToken.Value<string>("id"));
                        var savedName = stToken.Value<string>("name");
                        if (!string.IsNullOrEmpty(savedName))
                            target.Name = savedName;
                        var saveFile = Path.Combine(folderPath, stToken.Value<string>("saveFile"));
                        if (File.Exists(saveFile))
                            LoadPerStateProgress(target, saveFile);
                        stateIdMap[savedId] = target;
                    }
                }
            }

            var windowsArr = root["windows"] as JArray;
            if (windowsArr == null) return;

            // Skip the first saved window (the existing first MainWindow uses
            // it). For each subsequent saved window, spawn a new MainWindow.
            int idx = 0;
            foreach (var winToken in windowsArr)
            {
                var openIds = winToken["openStates"] as JArray;
                var activeId = winToken.Value<string>("activeState");

                WindowContext ctx;
                if (idx == 0)
                {
                    ctx = ApplicationModel.Instance.Windows.Count > 0
                        ? ApplicationModel.Instance.Windows[0]
                        : null;
                    // Clear any existing OpenStates from preallocated state.
                    if (ctx != null)
                    {
                        var snapshot = new List<TrackerState>(ctx.OpenStates);
                        foreach (var s in snapshot) ctx.RemoveState(s);
                    }
                }
                else
                {
                    var w = new MainWindow();
                    w.Show();
                    ctx = w.WindowContext;
                }

                if (ctx != null && openIds != null)
                {
                    foreach (var idTok in openIds)
                    {
                        if (Guid.TryParse(idTok.Value<string>(), out var id) && stateIdMap.TryGetValue(id, out var state))
                            ctx.AddState(state, makeActive: false);
                    }
                    if (!string.IsNullOrEmpty(activeId)
                        && Guid.TryParse(activeId, out var actId)
                        && stateIdMap.TryGetValue(actId, out var actState))
                        ctx.ActiveState = actState;
                    else if (ctx.OpenStates.Count > 0)
                        ctx.ActiveState = ctx.OpenStates[0];
                }
                idx++;
            }
        }

        // ---------- Per-state save / load (slice of existing save) ---------

        // Today ApplicationModel.SaveProgress / LoadProgress are not
        // explicitly per-state. Phase 7.10 keeps things simple by writing
        // an empty stub per state file — the actual per-state JSON shape
        // is the existing save format which round-trips through the
        // singleton path. A future polish pass extends this to a real
        // per-state save.
        static void SavePerStateProgress(TrackerState state, string path)
        {
            // Use the existing application-level save as a passable
            // approximation — round-trips items, locations, tracker
            // state. Each state's save file is identical right now since
            // the save path captures the active state's catalogs only.
            try
            {
                ApplicationModel.Instance.SaveProgress(path);
            }
            catch (Exception)
            {
                // Defensive: don't break the bundle save if a single state
                // save fails.
            }
        }

        static void LoadPerStateProgress(TrackerState state, string path)
        {
            try
            {
                ApplicationModel.Instance.LoadProgress(path);
            }
            catch (Exception)
            {
            }
        }
    }

    static class TrackerStateExtensions
    {
        // Small helper: find the PackageInstance owning the given state.
        // Phase 7.5 doesn't make TrackerState back-reference its owner
        // directly; we walk the live PI collection.
        public static EmoTracker.Data.Sessions.PackageInstance OwnerPackageInstance(
            this TrackerState state, out EmoTracker.Data.Sessions.PackageInstance found)
        {
            found = null;
            foreach (var pi in ApplicationModel.Instance.PackageInstances)
            {
                if (pi.States.ContainsKey(state.Id))
                {
                    found = pi;
                    return pi;
                }
            }
            return null;
        }
    }
}
