using EmoTracker.Core;
using EmoTracker.Data.JSON;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using EmoTracker.Data.Session;

namespace EmoTracker.Data.Packages
{
    public class GamePackage : ObservableObject, IGamePackage, IMenuItemsProvider
    {
        class Variant : ObservableObject, IGamePackageVariant
        {
            IGamePackage mOwner;
            string mUniqueID;
            string mOverridePath;
            string mDisplayName;

            public Variant(IGamePackage owner)
            {
                mOwner = owner;
            }

            public string BasePath
            {
                get { return mUniqueID; }
            }

            public string OverridePath
            {
                get { return mOverridePath; }
                set { SetProperty(ref mOverridePath, value); }
            }

            public string DisplayName
            {
                get { return mDisplayName; }
                set { SetProperty(ref mDisplayName, value); }
            }

            public bool IsActive
            {
                get { return TrackerSession.Current.Tracker.ActiveGamePackageVariant == this; }
            }

            public string UniqueID
            {
                get { return mUniqueID; }
                set { SetProperty(ref mUniqueID, value); }
            }

            public IGamePackage Package
            {
                get { return mOwner; }
            }
        }

        IGamePackageSource mSource;

        string mName;
        string mGameName;
        string mGameVariant;
        string mAuthor;
        string mPackageUID;
        Version mVersion;
        Version mLayoutEngineVersion;
        GamePlatform mPlatform;
        bool mUnsafe = true;
        List<string> mAutoTrackerProviders = new List<string>();

        ObservableCollection<IGamePackageVariant> mAvailableVariants = new ObservableCollection<IGamePackageVariant>();
        Variant mActiveVariant;

        string mOverridePath;

        public string OverridePath
        {
            get
            {
                if (!string.IsNullOrEmpty(mOverridePath))
                    return mOverridePath;

                if (!string.IsNullOrWhiteSpace(mPackageUID))
                    mOverridePath = Path.Combine(UserDirectory.Path, "user_overrides", mPackageUID);

                return mOverridePath;
            }
        }

        public string VariantOverridePath
        {
            get
            {
                if (!string.IsNullOrEmpty(mOverridePath))
                    return mOverridePath;

                if (!string.IsNullOrWhiteSpace(mPackageUID))
                    mOverridePath = Path.Combine(UserDirectory.Path, "user_overrides", mPackageUID);

                return mOverridePath;
            }
        }

        public GamePackage(IGamePackageSource source)
        {
            mSource = source;
            LoadManifest();
        }

        public IGamePackageSource Source
        {
            get { return mSource; }
        }

        public bool IsActive
        {
            get { return TrackerSession.Current.Tracker.ActiveGamePackage == this; }
        }

        public string Name { get { return mName; } }

        public string DisplayName { get { return mName ?? mPackageUID; } }

        public GamePlatform Platform { get { return mPlatform; } }

        public string Game { get { return mGameName; } }

        public string GameVariant { get { return mGameVariant; } }

        public string Author { get { return mAuthor; } }

        public string UniqueID { get { return mPackageUID; } }

        public Version Version { get { return mVersion; } }

        public Version LayoutEngineVersion { get { return mLayoutEngineVersion; } }

        public bool FlaggedAsUnsafe { get { return mUnsafe; } }

        public IReadOnlyList<string> AutoTrackerProviders { get { return mAutoTrackerProviders; } }

        string IGamePackage.OverridePath { get { return OverridePath; } }

        [DependentProperty("VariantOverridePath")]
        Variant ActiveVariant
        {
            get { return mActiveVariant; }
            set { SetProperty(ref mActiveVariant, value); }
        }

        public IEnumerable<IGamePackageVariant> AvailableVariants
        {
            get { return mAvailableVariants; }
        }

        public IGamePackageVariant FindVariant(string uid)
        {
            foreach (IGamePackageVariant variant in AvailableVariants)
            {
                if (string.Equals(uid, variant.UniqueID, StringComparison.OrdinalIgnoreCase))
                    return variant;
            }

            return null;
        }

        IGamePackageVariant IGamePackage.ActiveVariant
        {
            get { return mActiveVariant; }
            set { ActiveVariant = value as Variant; }
        }

        public bool IsValid
        {
            get
            {
                return !string.IsNullOrWhiteSpace(mPackageUID) && Version != null;
            }
        }

        IEnumerable<object> IMenuItemsProvider.Items
        {
            get { return mAvailableVariants; }
        }

        public void ResetUserOverrides()
        {
            if (Directory.Exists(OverridePath))
            {
                try
                {
                    Directory.Delete(OverridePath, true);
                }
                catch
                {
                }                
            }
        }

        public void ExportUserOverride(string filename)
        {
            DeploySourceFileToOverride(filename, true);
        }

        string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            while (path.StartsWith("/"))
            {
                path = path.Substring(1);
            }

            path = path.Replace(Path.AltDirectorySeparatorChar, '/');
            path = path.Replace(Path.DirectorySeparatorChar, '/');

            return path;
        }

        Stream OpenOverrideUsingRoot(string path, string root)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                if (!string.IsNullOrWhiteSpace(OverridePath) && Directory.Exists(OverridePath))
                {
                    string overrideFile = Path.Combine(root, path);
                    if (File.Exists(overrideFile))
                        return File.OpenRead(overrideFile);
                }
            }

            return null;
        }

        public Stream Open(string path, bool ignoreVariants = false, bool ignoreOverrides = false)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            path = NormalizePath(path);

            Stream s = null;

            if (!ignoreOverrides && !ignoreVariants && s == null && ActiveVariant != null)
                s = OpenOverrideUsingRoot(path, ActiveVariant.OverridePath);

            if (!ignoreVariants && s == null && ActiveVariant != null)
                s = mSource.Open(NormalizePath(Path.Combine(ActiveVariant.BasePath, path)));

            if (!ignoreOverrides && s == null)
                s = OpenOverrideUsingRoot(path, OverridePath);

            if (s == null)
                s = mSource.Open(path);

            return s;
        }

        public bool Exists(string path, bool ignoreVariants = false, bool ignoreOverrides = false)
        {
            using (Stream s = Open(path, ignoreVariants, ignoreOverrides))
            {
                return s != null;
            }
        }

        void LoadManifest()
        {
            mAvailableVariants.Clear();

            if (mSource == null)
                return;

            try
            {
                using (Stream s = mSource.Open("manifest.json"))
                {
                    if (s != null)
                    {
                        using (StreamReader reader = new StreamReader(s))
                        {
                            JObject manifest = (JObject)JToken.ReadFrom(new JsonTextReader(reader));
                            if (manifest != null)
                            {
                                mName = manifest.GetValue<string>("name");
                                mPlatform = manifest.GetEnumValue<GamePlatform>("platform");
                                mGameName = manifest.GetValue<string>("game_name");
                                mGameVariant = manifest.GetValue<string>("game_variant");
                                mAuthor = manifest.GetValue<string>("author");
                                mUnsafe = manifest.GetValue<bool>("enable_unsafe_scripting", false);
                                
                                mPackageUID = manifest.GetValue<string>("uid");
                                if (string.IsNullOrWhiteSpace(mPackageUID))
                                    mPackageUID = manifest.GetValue<string>("package_uid");

                                if (!Version.TryParse(manifest.GetValue<string>("version"), out mVersion))
                                    Version.TryParse(manifest.GetValue<string>("package_version"), out mVersion);

                                Version.TryParse(manifest.GetValue<string>("layout_engine_version"), out mLayoutEngineVersion);

                                JArray autoTrackerProviders = manifest.GetValue<JArray>("auto_tracker_providers");
                                if (autoTrackerProviders != null)
                                {
                                    foreach (var item in autoTrackerProviders)
                                    {
                                        string providerUid = item.Value<string>();
                                        if (!string.IsNullOrWhiteSpace(providerUid))
                                            mAutoTrackerProviders.Add(providerUid);
                                    }
                                }

                                JObject variantDefs = manifest.GetValue<JObject>("variants");
                                if (variantDefs != null)
                                {
                                    foreach (var key in variantDefs.Properties())
                                    {
                                        string name = key.Name;

                                        JObject def = variantDefs.GetValue<JObject>(key.Name);
                                        if (def != null)
                                        {
                                            string displayName = def.GetValue<string>("display_name");
                                            if (!string.IsNullOrWhiteSpace(displayName))
                                            {
                                                mAvailableVariants.Add(new Variant(this)
                                                {
                                                    UniqueID = name,
                                                    DisplayName = displayName,
                                                    OverridePath = Path.Combine(OverridePath, name)
                                                });
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private void DeploySourceFileToOverride(string path, bool bReplaceExisting = false)
        {
            if (!Directory.Exists(OverridePath))
                Directory.CreateDirectory(OverridePath);

            path = NormalizePath(path);

            string overrideFilename = Path.Combine(OverridePath, path);
            {
                string overrideFolder = Path.GetDirectoryName(overrideFilename);

                if (!Directory.Exists(overrideFolder))
                    Directory.CreateDirectory(overrideFolder);
            }

            if (bReplaceExisting || !File.Exists(overrideFilename))
            {
                if (mSource != null)
                {
                    using (Stream src = mSource.Open(path))
                    {
                        if (src != null)
                        {
                            if (File.Exists(overrideFilename))
                                File.Delete(overrideFilename);

                            using (Stream dst = File.OpenWrite(overrideFilename))
                            {
                                src.CopyTo(dst);
                            }
                        }
                    }
                }
            }
        }
    }
}
