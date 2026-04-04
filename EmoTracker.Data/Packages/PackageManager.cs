using EmoTracker.Core;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;

namespace EmoTracker.Data.Packages
{
    public class PackageManager : ObservableSingleton<PackageManager>
    {
        static PackageManager()
        {
        }

        public static Uri BuildServiceUri(string path)
        {
            return new Uri(string.Format("{0}{1}", ApplicationSettings.Instance.ServiceBaseURL, path.TrimStart('/')));
        }

        WebClient mGameListWebClient;
        WebClient mRepositoryListWebClient;

        public event EventHandler OnGameListDownloaded;

        #region -- Game Organization --

        ObservableCollection<PackageRepositoryEntry> mAvailablePackages = new ObservableCollection<PackageRepositoryEntry>();
        TrivialObservableCollectionAggregatorSynchronizer<PackageRepositoryEntry> mAvailablePackagesSync;

        public IEnumerable<PackageRepositoryEntry> AvailablePackages
        {
            get { return mAvailablePackages; }
        }

        public class Game : ObservableObject
        {
            public class MemoryRange
            {
                public ulong Begin;
                public ulong End;

                public static bool TryParse(string value, out MemoryRange result)
                {
                    result = null;

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        string[] parts = value.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts != null && parts.Length == 2)
                        {
                            try
                            {
                                ulong begin = Convert.ToUInt64(parts[0], 16);
                                ulong end = Convert.ToUInt64(parts[1], 16);

                                if (end >= begin)
                                {
                                    MemoryRange range = new MemoryRange();
                                    range.Begin = begin;
                                    range.End = end;

                                    result = range;
                                    return true;
                                }
                            }
                            catch
                            {
                            }
                        }
                    }

                    return false;
                }

                public override string ToString()
                {
                    return string.Format("0x{0:x}:0x{1:x}", Begin, End);
                }
            }

            ObservableCollection<IGamePackage> mInstalledPackages = new ObservableCollection<IGamePackage>();
            ObservableCollection<PackageRepositoryEntry> mAvailablePackages = new ObservableCollection<PackageRepositoryEntry>();

            ObservableCollection<MemoryRange> mMemoryRangeWhitelist = new ObservableCollection<MemoryRange>();
            ObservableCollection<MemoryRange> mMemoryRangeBlacklist = new ObservableCollection<MemoryRange>();

            string mKey;
            string mName;
            string mSeries;
            string mImageURL;
            int mPriority = 10000;
            int mSeriesPriority = 10000;

            ImageReference mImage = ImageReference.FromExternalURI(new Uri("pack://application:,,,/EmoTracker;component/Resources/PackageManager/default_game_banner.png"));

            public string Key
            {
                get { return mKey; }
                set { SetProperty(ref mKey, value); }
            }

            public string Name
            {
                get { return mName; }
                set { SetProperty(ref mName, value); }
            }

            public string Series
            {
                get { return mSeries; }
                set { SetProperty(ref mSeries, value); }
            }

            public string ImageURL
            {
                get { return mImageURL; }
                set
                {
                    if (SetProperty(ref mImageURL, value))
                    {
                        Image = ImageReference.FromExternalURI(new Uri(value));
                    }
                }
            }

            public ImageReference Image
            {
                get { return mImage; }
                private set { SetProperty(ref mImage, value); }
            }

            public int Priority
            {
                get { return mPriority; }
                set { SetProperty(ref mPriority, value); }
            }

            public int SeriesPriority
            {
                get { return mSeriesPriority; }
                set { SetProperty(ref mSeriesPriority, value); }
            }

            public ObservableCollection<IGamePackage> InstalledPackages
            {
                get { return mInstalledPackages; }
                private set { SetProperty(ref mInstalledPackages, value); }
            }

            public ObservableCollection<PackageRepositoryEntry> AvailablePackages
            {
                get { return mAvailablePackages; }
                private set { SetProperty(ref mAvailablePackages, value); }
            }

            public IList<MemoryRange> MemoryRangeWhitelist
            {
                get { return mMemoryRangeWhitelist; }
            }

            public IList<MemoryRange> MemoryRangeBlacklist
            {
                get { return mMemoryRangeBlacklist; }
            }

            private bool MemoryRangeContains(ulong rangeStart, ulong rangeEnd, ulong address)
            {
                return address >= rangeStart && address <= rangeEnd;
            }

            public bool IsMemoryRangeAccessAllowed(ulong start, ulong end)
            {
                foreach (MemoryRange memoryRange in MemoryRangeBlacklist)
                {
                    if (MemoryRangeContains(memoryRange.Begin, memoryRange.End, start) || MemoryRangeContains(memoryRange.Begin, memoryRange.End, end) ||
                        MemoryRangeContains(start, end, memoryRange.Begin) || MemoryRangeContains(start, end, memoryRange.End))
                    {
                        return false;
                    }
                }

                bool bFoundInWhiteList = true;
                if (MemoryRangeWhitelist.Count > 0)
                {
                    bFoundInWhiteList = false;

                    foreach (MemoryRange memoryRange in MemoryRangeWhitelist)
                    {
                        if (MemoryRangeContains(memoryRange.Begin, memoryRange.End, start) && MemoryRangeContains(memoryRange.Begin, memoryRange.End, end))
                        {
                            bFoundInWhiteList = true;
                        }
                    }
                }

                return bFoundInWhiteList;
            }

            public override string ToString()
            {
                return Name;
            }
        }

        private ObservableCollection<Game> mAvailableGames = new ObservableCollection<Game>();
        private Game mDefaultGame = new Game() { Name = "Other", Key = "Other", ImageURL = BuildServiceUri("/games/default.png").AbsoluteUri };

        public IEnumerable<Game> AvailableGames
        {
            get { return mAvailableGames; }
        }

        public Game DefaultGame
        {
            get { return mDefaultGame; }
        }

        public Game FindGame(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                foreach (Game g in AvailableGames)
                {
                    if (g.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return g;

                    if (g.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return g;

                }
            }

            return DefaultGame;
        }

#endregion

        #region -- Repository Management ---

        bool mbCommunityRepositoryListError = false;
        public bool CommunityRepositoryListError
        {
            get { return mbCommunityRepositoryListError; }
            private set { SetProperty(ref mbCommunityRepositoryListError, value); }
        }

        bool mbDownloadNotInProgress = true;

        public bool DownloadNotInProgress
        {
            get { return mbDownloadNotInProgress; }
            set { SetProperty(ref mbDownloadNotInProgress, value); }
        }

        ObservableCollection<PackageRepository> mRepositories = new ObservableCollection<PackageRepository>();

        public IEnumerable<PackageRepository> Repositories
        {
            get { return mRepositories; }
        }

        public bool UpdatesAvailable
        {
            get
            {
                foreach (PackageRepository repository in Repositories)
                {
                    foreach (PackageRepositoryEntry entry in repository.Packages)
                    {
                        if (entry.Status == PackageRepositoryEntry.PackageStatus.UpdateAvailable)
                            return true;
                    }
                }

                return false;
            }
        }

        public bool CurrentPackageHasUpdateAvailable
        {
            get
            {
                foreach (PackageRepository repository in Repositories)
                {
                    foreach (PackageRepositoryEntry entry in repository.Packages)
                    {
                        if (entry.Status == PackageRepositoryEntry.PackageStatus.UpdateAvailable)
                        {
                            if (entry.ExistingPackage == Tracker.Instance.ActiveGamePackage)
                                return true;
                        }
                    }
                }

                return false;
            }
        }

        public bool AnyPackagesInstalled
        {
            get
            {
                return mInstalledPackages.Count > 0;
            }
        }

        public PackageRepository AddRepository(PackageRepository repository)
        {
            foreach (PackageRepository existing in Repositories)
            {
                if (existing.URL.Equals(repository.URL, StringComparison.OrdinalIgnoreCase))
                {
                    existing.Reload();
                    return existing;
                }
            }

            mRepositories.Add(repository);
            mAvailablePackagesSync.AddSource(repository.Packages);
            return repository;
        }

        public void RemoveRepository(PackageRepository repository)
        {
            mRepositories.Remove(repository);

            if (repository != null && repository.Packages != null)
                mAvailablePackagesSync.RemoveSource(repository.Packages);
        }

        public event EventHandler<PackageRepository> OnRepositoryUpdated;

        #endregion

        public static string PackInstallPath = Path.Combine(UserDirectory.Path, "packs");

        ObservableCollection<GamePackage> mInstalledPackages = new ObservableCollection<GamePackage>();

        public IEnumerable<IGamePackage> InstalledPackages
        {
            get { return mInstalledPackages; }
        }

        public PackageManager()
        {
            mAvailablePackagesSync = new TrivialObservableCollectionAggregatorSynchronizer<PackageRepositoryEntry>(mAvailablePackages);
        }

        public void Initialize()
        {
            Directory.CreateDirectory(PackInstallPath);
            ScanForInstalledPackages();

            foreach (string url in ApplicationSettings.Instance.AdditionalRepositories)
            {
                AddRepository(new PackageRepository(url));
            }

            DownloadGameList();
            DownloadRepositoryList();
        }

        private void DownloadGameList()
        {
            try
            {
                mGameListWebClient = new WebClient();
                mGameListWebClient.Headers.Add("User-Agent", string.Format("EmoTracker/{0} (Windows)", ApplicationVersion.Current));
                mGameListWebClient.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                mGameListWebClient.DownloadDataCompleted += OnGameListDownloadCompleted;
                mGameListWebClient.DownloadDataAsync(BuildServiceUri("supported_games.json"));
            }
            catch
            {
            }
        }

        private void DownloadRepositoryList()
        {
            try
            {
                mRepositoryListWebClient = new WebClient();
                mRepositoryListWebClient.Headers.Add("User-Agent", string.Format("EmoTracker/{0} (Windows)", ApplicationVersion.Current));
                mRepositoryListWebClient.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                mRepositoryListWebClient.DownloadDataCompleted += OnRepositoryListDownloadCompleted;
                mRepositoryListWebClient.DownloadDataAsync(BuildServiceUri("package_repositories.json"));
            }
            catch
            {
            }
        }

        private void ParseMemoryRangeList(IList<Game.MemoryRange> ranges, JArray src)
        {
            if (src != null)
            {
                foreach (string range in src)
                {
                    Game.MemoryRange instance;

                    if (Game.MemoryRange.TryParse(range, out instance) && instance != null)
                        ranges.Add(instance);
                    else
                        throw new InvalidDataException("Invalid data was passed to MemoryRange.TryParse(...)");
                }
            }
        }

        private void OnGameListDownloadCompleted(object sender, DownloadDataCompletedEventArgs e)
        {
            if (e.Error != null || e.Result == null || e.Result.Length <= 0)
                return;

            try
            {
                using (Stream s = new MemoryStream(e.Result))
                {
                    using (StreamReader reader = new StreamReader(s))
                    {
                        JsonTextReader jsonReader = new JsonTextReader(reader);
                        JObject root = (JObject)JToken.ReadFrom(jsonReader);
                        if (root != null)
                        {
                            foreach (var property in root.Properties())
                            {
                                try
                                {
                                    JObject obj = root.GetValue<JObject>(property.Name);
                                    if (obj != null)
                                    {
                                        Game game = new Game()
                                        {
                                            Key = property.Name,
                                            Name = obj.GetValue<string>("name", property.Name),
                                            Series = obj.GetValue<string>("series"),
                                            ImageURL = BuildServiceUri(obj.GetValue<string>("image")).AbsoluteUri,
                                            Priority = obj.GetValue<int>("priority", 10000),
                                            SeriesPriority = obj.GetValue<int>("series_priority", 10000)
                                        };

                                        bool bShouldRestrictAllMemoryAccess = false;
                                        try
                                        {
                                            JObject memoryConfig = obj.GetValue<JObject>("memory_watch_config");
                                            if (memoryConfig != null)
                                            {
                                                List<Game.MemoryRange> blacklist = new List<Game.MemoryRange>();
                                                ParseMemoryRangeList(blacklist, memoryConfig.GetValue<JArray>("memory_range_blacklist"));

                                                List<Game.MemoryRange> whitelist = new List<Game.MemoryRange>();
                                                ParseMemoryRangeList(whitelist, memoryConfig.GetValue<JArray>("memory_range_whitelist"));

                                                foreach (var entry in whitelist)
                                                {
                                                    game.MemoryRangeWhitelist.Add(entry);
                                                }

                                                foreach (var entry in blacklist)
                                                {
                                                    game.MemoryRangeBlacklist.Add(entry);
                                                }
                                            }
                                        }
                                        catch
                                        {
                                            bShouldRestrictAllMemoryAccess = true;
                                        }

                                        if (bShouldRestrictAllMemoryAccess)
                                        {
                                            //  When we fail to parse this block, restrict all memory access
                                            game.MemoryRangeBlacklist.Add(new Game.MemoryRange()
                                            {
                                                Begin = 0x0000000000000000,
                                                End = 0xFFFFFFFFFFFFFFFF
                                            });
                                        }

                                        mAvailableGames.Add(game);
                                    }
                                }
                                catch
                                {
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (OnGameListDownloaded != null)
                    OnGameListDownloaded(this, EventArgs.Empty);
            }
        }

        internal void NotifyRepositoryUpdated(PackageRepository packageRepository)
        {
            EmoTracker.Core.Services.Dispatch.BeginInvoke(() =>
            {
                NotifyPropertyChanged("CurrentPackageHasUpdateAvailable");
                NotifyPropertyChanged("UpdatesAvailable");

                OnRepositoryUpdated?.Invoke(this, packageRepository);
            });
        }

        private void OnRepositoryListDownloadCompleted(object sender, DownloadDataCompletedEventArgs e)
        {
            if (e.Error != null || e.Result == null || e.Result.Length <= 0)
            {
                CommunityRepositoryListError = true;
                return;
            }

            try
            {
                HashSet<PackageRepository> repositoriesToRemove = new HashSet<PackageRepository>();
                foreach (PackageRepository repo in Repositories)
                {
                    repositoriesToRemove.Add(repo);
                }

                using (Stream s = new MemoryStream(e.Result))
                {
                    using (StreamReader reader = new StreamReader(s))
                    {
                        JsonTextReader jsonReader = new JsonTextReader(reader);
                        JObject root = (JObject)JToken.ReadFrom(jsonReader);
                        if (root != null)
                        {
                            foreach (var property in root.Properties())
                            {
                                try
                                {
                                    string url = root.GetValue<string>(property.Name);
                                    repositoriesToRemove.Remove(AddRepository(new PackageRepository(url, property.Name)));
                                }
                                catch
                                {
                                }                                
                            }
                        }
                    }
                }

                foreach (PackageRepository repository in repositoriesToRemove)
                {
                    RemoveRepository(repository);
                }

                foreach (string url in ApplicationSettings.Instance.AdditionalRepositories)
                {
                    AddRepository(new PackageRepository(url));
                }

                CommunityRepositoryListError = false;
            }
            catch
            {
            }
        }

        void ScanForInstalledPackages()
        {
            mInstalledPackages.Clear();
            NotifyPropertyChanged("AnyPackagesInstalled");

            foreach (string folder in Directory.GetDirectories(PackInstallPath))
            {
                GamePackage package = new GamePackage(new DirectoryPackageSource(folder));
                if (package.IsValid)
                {
                    AddInstalledPackage(package);
                }
            }

            foreach (string archive in Directory.GetFiles(PackInstallPath, "*.zip"))
            {
                GamePackage package = new GamePackage(new ZipPackageSource(archive));
                if (package.IsValid)
                {
                    AddInstalledPackage(package);
                }
            }
        }

        void AddInstalledPackage(GamePackage package, bool bOverride = false)
        {
            foreach (GamePackage existing in mInstalledPackages)
            {
                if (string.Equals(package.UniqueID, existing.UniqueID, StringComparison.OrdinalIgnoreCase) && !bOverride)
                {
                    return;
                }
            }

            mInstalledPackages.Add(package);
            NotifyPropertyChanged("AnyPackagesInstalled");
        }

        public void RefreshActiveState()
        {
            foreach (GamePackage package in InstalledPackages)
            {
                package.ForceRefreshProperty("IsActive");

                foreach (IGamePackageVariant abstractVariant in package.AvailableVariants)
                {
                    ObservableObject observable = abstractVariant as ObservableObject;
                    if (observable != null)
                        observable.ForceRefreshProperty("IsActive");
                }
            }

            ForceRefreshProperty("CurrentPackageHasUpdateAvailable");
        }

        public GamePackage FindInstalledPackage(string uid)
        {
            if (!string.IsNullOrWhiteSpace(uid))
            {
                foreach (GamePackage package in InstalledPackages)
                {
                    if (string.Equals(package.UniqueID, uid, StringComparison.OrdinalIgnoreCase))
                        return package;
                }
            }

            return null;
        }

        public PackageRepositoryEntry FindRepositoryEntry(string uid)
        {
            if (!string.IsNullOrWhiteSpace(uid))
            {
                foreach (PackageRepositoryEntry entry in AvailablePackages)
                {
                    if (string.Equals(entry.UID, uid, StringComparison.OrdinalIgnoreCase))
                        return entry;
                }
            }

            return null;
        }

        public void Rescan()
        {
            ScanForInstalledPackages();
        }

        public void RefreshRemoteRepositories()
        {
            DownloadRepositoryList();
        }
    }
}
