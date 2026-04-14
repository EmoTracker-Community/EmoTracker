#pragma warning disable SYSLIB0014 // WebClient is obsolete
using EmoTracker.Core;
using EmoTracker.Data.JSON;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using EmoTracker.Data.Session;

namespace EmoTracker.Data.Packages
{
    [Flags]
    public enum PackageFlags
    {
        None = 0,
        Official = 1,
        Featured = 2,
        Map = 4,
        Pins = 8,
        ChatHUD = 16,
        AutoTracker = 32,
        Unsafe = 64
    }

    public enum DownloadStatus
    {
        Ready,
        InProgress,
        Complete,
        Error
    }

    public enum UninstallResult
    {
        Success,
        FailedUninstall,
        FailedOverrides
    }

    public enum BackupOverrideResult
    {
        Success,
        Failed
    }

    public class VariantEntry : ObservableObject
    {
        string mName;
        PackageFlags mFlags = PackageFlags.None;

        public PackageFlags Flags
        {
            get { return mFlags; }
            set { SetProperty(ref mFlags, value); }
        }

        public string Name
        {
            get { return mName; }
            set { SetProperty(ref mName, value); }
        }
    }

    public class PackageRepositoryEntry : ObservableObject
    {
        public enum PackageStatus
        {
            Development,
            AppUpdateRequired,
            Available,
            Installed,
            UpdateAvailable,
            DownloadError
        }

        PackageRepository mOwner;

        string mName;
        string mGame;
        string mAuthor;
        string mUID;
        string mURL;
        string mDocumentationURL;
        Version mVersion;
        Version mRequiredAppVersion;
        GamePackage mExistingPackage;
        PackageStatus mStatus = PackageStatus.Available;
        PackageFlags mFlags = PackageFlags.None;
        WebClient mWebClient;
        DownloadStatus mDownloadStatus;
        int mDownloadProgress = 0;
        ObservableCollection<VariantEntry> mVariants = new ObservableCollection<VariantEntry>();

        public PackageRepositoryEntry(PackageRepository owner)
        {
            mOwner = owner;

            //InstallPackageCommand = new DelegateCommand(InstallPackageHandler);
            //UninstallPackageCommand = new DelegateCommand(UninstallPackageHandler);

            mWebClient = new WebClient();
            mWebClient.Headers.Add("User-Agent", string.Format("EmoTracker/{0} (Windows)", ApplicationVersion.Current));
            mWebClient.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.BypassCache);
            mWebClient.DownloadFileCompleted += MWebClient_DownloadFileCompleted;
            mWebClient.DownloadProgressChanged += MWebClient_DownloadProgressChanged;

        }

        public PackageStatus Status
        {
            get { return mStatus; }
            set
            {
                if (SetProperty(ref mStatus, value))
                {
                    PackageManager.Instance.ForceRefreshProperty("UpdatesAvailable");
                    PackageManager.Instance.ForceRefreshProperty("CurrentPackageHasUpdateAvailable");
                }
            }
        }

        public PackageFlags Flags
        {
            get { return mFlags; }
            set { SetProperty(ref mFlags, value); }
        }

        public DownloadStatus DownloadStatus
        {
            get { return mDownloadStatus; }
            set { SetProperty(ref mDownloadStatus, value); }
        }

        public string Name
        {
            get { return mName; }
            set { SetProperty(ref mName, value); }
        }

        public string Game
        {
            get { return mGame; }
            set { SetProperty(ref mGame, value); }
        }


        public string Author
        {
            get { return mAuthor; }
            set { SetProperty(ref mAuthor, value); }
        }

        public string UID
        {
            get { return mUID; }
            set { SetProperty(ref mUID, value); }
        }

        public string URL
        {
            get { return mURL; }
            set { SetProperty(ref mURL, value); }
        }

        public string DocumentationURL
        {
            get { return mDocumentationURL; }
            set { SetProperty(ref mDocumentationURL, value); }
        }

        public Version Version
        {
            get { return mVersion; }
            set { SetProperty(ref mVersion, value); }
        }

        [DependentProperty("RequiresNewerAppVersion")]
        public Version RequiredAppVersion
        {
            get { return mRequiredAppVersion; }
            set
            {
                if (SetProperty(ref mRequiredAppVersion, value) && RequiresNewerAppVersion)
                    Status = PackageStatus.AppUpdateRequired;
            }
        }

        public bool RequiresNewerAppVersion
        {
            get { return RequiredAppVersion > ApplicationVersion.Current; }
        }

        public int DownloadProgress
        {
            get { return mDownloadProgress; }
            set { SetProperty(ref mDownloadProgress, value); }
        }

        public GamePackage ExistingPackage
        {
            get { return mExistingPackage; }
            set
            {
                if (SetProperty(ref mExistingPackage, value))
                {
                    if (RequiresNewerAppVersion)
                    {
                        Status = PackageStatus.AppUpdateRequired;
                    }
                    else
                    {
                        if (mExistingPackage == null)
                        {
                            Status = PackageStatus.Available;
                        }
                        else if (mExistingPackage != null && Version > mExistingPackage.Version)
                        {
                            Status = PackageStatus.UpdateAvailable;
                        }
                        else
                        {
                            if (mExistingPackage != null && mExistingPackage.Source as DirectoryPackageSource != null)
                                Status = PackageStatus.Development;
                            else
                                Status = PackageStatus.Installed;
                        }
                    }
                }
            }
        }

        public ObservableCollection<VariantEntry> Variants
        {
            get { return mVariants; }
        }

        #region -- Download Support --
        public void Install()
        {
            PackageManager.Instance.DownloadNotInProgress = false;
            string temp = Path.GetTempFileName();

            mWebClient.DownloadFileAsync(new Uri(mURL), temp, temp);
        }

        public UninstallResult Uninstall()
        {
            string fileName = ExistingPackage.Source.PackPath;

           
            FileAttributes fatt = File.GetAttributes(fileName);
            FileSystemInfo packinfo;

            if (fatt.HasFlag(FileAttributes.Directory))
            {
                packinfo = new DirectoryInfo(fileName);
            }
            else 
            {
                packinfo = new FileInfo(fileName);
            }
        

            DirectoryInfo overideFolder = new DirectoryInfo(ExistingPackage.OverridePath);

            try
            {
                packinfo.Delete();
            }
            catch
            {
                return UninstallResult.FailedUninstall;
            }

            try
            {
                if (overideFolder.Exists)
                {
                    overideFolder.Delete(true);
                }
            }
            catch
            {
                return UninstallResult.FailedOverrides;
            }
            finally
            {

                //duck: this maybe should be moved to the application model fucntion?
                if (TrackerSession.Current.Tracker.IsActivePackage(mUID))
                {
                    TrackerSession.Current.Tracker.ActiveGamePackage = null;
                }

                PackageManager.Instance.Rescan();
                mOwner.Reload();

            }
            return UninstallResult.Success;


        }

        public BackupOverrideResult BackupOverride()
        {
            string opath = ExistingPackage.OverridePath;
            string bopath = $"{opath}_backup";
            DirectoryInfo oDi = new DirectoryInfo(ExistingPackage.OverridePath);

            try
            {
                oDi.MoveTo(bopath);
            }
            catch
            {
                return BackupOverrideResult.Failed;
            }


            return BackupOverrideResult.Success;
        }

       
        private void MWebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            if (System.Environment.OSVersion.Version.Major >= 10)
                DownloadProgress = e.ProgressPercentage;
        }
        private void MWebClient_DownloadFileCompleted(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
        {
            try
            {
                if (e.Error == null)
                {
                    string path = e.UserState as string;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length > 0)
                    {
                        bool bIsFirstInstalledPackage = PackageManager.Instance.InstalledPackages.Count() == 0;
                        bool bExistingIsActive = ExistingPackage == TrackerSession.Current.Tracker.ActiveGamePackage;

                        if (ExistingPackage != null)
                            ExistingPackage.Source.ReleaseStorage();

                        int retryCount = 0;
                        while (retryCount < 10)
                        {
                            try
                            {
                                File.Copy(path, Path.Combine(PackageManager.PackInstallPath, string.Format("{0}.zip", UID)), true);
                                break;
                            }
                            catch (Exception innerException)
                            {
                                TrackerSession.Current.Scripts.OutputException(innerException);
                                System.Threading.Thread.Sleep(500);
                            }

                            ++retryCount;
                        }
                        

                        if (ExistingPackage != null)
                            ExistingPackage.Source.AcquireStorage();

                        PackageManager.Instance.Rescan();
                        mOwner.Reload();


                        //duck: this maybe should be moved to the application model fucntion?
                        // Refresh our installed package
                        ExistingPackage = PackageManager.Instance.FindInstalledPackage(UID);

                        if (bIsFirstInstalledPackage)
                        {
                            if (ExistingPackage.AvailableVariants.Count() > 0)
                                TrackerSession.Current.Tracker.ActiveGamePackageVariant = ExistingPackage.AvailableVariants.First();
                            else
                                TrackerSession.Current.Tracker.ActiveGamePackage = ExistingPackage;
                        }
                        else if (bExistingIsActive && ExistingPackage != null)
                        {
                            IGamePackageVariant variant = ExistingPackage.FindVariant(TrackerSession.Current.Global.LastActivePackageVariant) ?? ExistingPackage.AvailableVariants.FirstOrDefault();

                            if (variant != null)
                                TrackerSession.Current.Tracker.ActiveGamePackageVariant = variant;
                            else
                                TrackerSession.Current.Tracker.ActiveGamePackage = ExistingPackage;

                            TrackerSession.Current.Tracker.Reload();
                        }

                        return;
                    }
                }

                Status = PackageStatus.DownloadError;
            }
            catch (Exception outerException)
            {
                TrackerSession.Current.Scripts.OutputException(outerException);
            }
            finally
            {
                PackageManager.Instance.DownloadNotInProgress = true;
            }
        }

        #endregion
    }

    public class PackageRepository : ObservableObject
    {
        DownloadStatus mStatus = DownloadStatus.Ready;
        WebClient mWebClient;
        string mURL;
        string mName;

        ObservableCollection<PackageRepositoryEntry> mPackages = new ObservableCollection<PackageRepositoryEntry>();

        public IReadOnlyCollection<PackageRepositoryEntry> Packages
        {
            get { return mPackages; }
        }

        public DownloadStatus DownloadStatus
        {
            get { return mStatus; }
            protected set { SetProperty(ref mStatus, value); }
        }

        public string Name
        {
            get { return mName; }
            private set { SetProperty(ref mName, value); }
        }

        public string URL
        {
            get { return mURL; }
            private set { SetProperty(ref mURL, value); }
        }

        public PackageRepository(string url, string defaultName = null)
        {
            Name = defaultName;
            URL = url;

            mWebClient = new WebClient();
            mWebClient.Headers.Add(HttpRequestHeader.UserAgent, string.Format("EmoTracker/{0} (Windows)", ApplicationVersion.Current));
            mWebClient.Headers.Add(HttpRequestHeader.CacheControl, "no-cache");
            mWebClient.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            mWebClient.DownloadDataCompleted += MWebClient_DownloadDataCompleted;
            mWebClient.DownloadProgressChanged += MWebClient_DownloadProgressChanged;

            Reload();
        }

        public void Reload()
        {
            if (DownloadStatus != DownloadStatus.InProgress)
            {
                DownloadStatus = DownloadStatus.InProgress;
                try
                {
                    mWebClient.DownloadDataAsync(new Uri(URL));
                }
                catch
                {
                    DownloadStatus = DownloadStatus.Error;
                }
            }
        }

        private void MWebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
        }

        PackageFlags ParsePackageFlagsArray(JArray attributes, bool bAllowPrivilegedFlags)
        {
            if (attributes == null)
                return PackageFlags.None;

            PackageFlags flags = PackageFlags.None;
            foreach (string flag in attributes)
            {
                PackageFlags flagVal;
                if (Enum.TryParse<PackageFlags>(flag, true, out flagVal))
                {
                    if (!bAllowPrivilegedFlags && flags.HasFlag(PackageFlags.Official | PackageFlags.Featured))
                        flags = PackageFlags.None;

                    flags |= flagVal;
                }
            }

            return flags;
        }

        private void MWebClient_DownloadDataCompleted(object sender, DownloadDataCompletedEventArgs e)
        {
            if (e.Error != null || e.Result == null || e.Result.Length <= 0)
            {
                EmoTracker.Core.Services.Dispatch.BeginInvoke(() => DownloadStatus = DownloadStatus.Error);
                return;
            }

            // Parse on the background thread so the UI thread isn't blocked.
            List<PackageRepositoryEntry> parsed = null;
            string parsedName = null;
            bool parseSucceeded = false;

            try
            {
                parsed = new List<PackageRepositoryEntry>();

                using (Stream s = new MemoryStream(e.Result))
                {
                    using (StreamReader reader = new StreamReader(s))
                    {
                        JsonTextReader jsonReader = new JsonTextReader(reader);
                        JObject root = (JObject)JToken.ReadFrom(jsonReader);
                        if (root != null)
                        {
                            parsedName = root.GetValue<string>("name", Name);

                            JArray packages = root.GetValue<JArray>("packages");
                            if (packages != null)
                            {
                                foreach (JObject entry in packages)
                                {
                                    PackageRepositoryEntry instance = new PackageRepositoryEntry(this);

                                    string versionValue = entry.GetValue<string>("version");
                                    Version version;

                                    if (!string.IsNullOrWhiteSpace(versionValue) && Version.TryParse(versionValue, out version))
                                    {
                                        instance.Version = version;

                                        string requiredAppVersionValue = entry.GetValue<string>("required_app_version");
                                        Version requiredAppVersion;

                                        if (!string.IsNullOrWhiteSpace(requiredAppVersionValue) && Version.TryParse(requiredAppVersionValue, out requiredAppVersion))
                                            instance.RequiredAppVersion = requiredAppVersion;

                                        instance.Name = entry.GetValue<string>("name");
                                        instance.Game = entry.GetValue<string>("game_name");
                                        instance.Author = entry.GetValue<string>("author", "Unknown");
                                        instance.UID = entry.GetValue<string>("uid");
                                        instance.URL = entry.GetValue<string>("link");
                                        instance.DocumentationURL = entry.GetValue<string>("documentation_url");
                                        instance.ExistingPackage = PackageManager.Instance.FindInstalledPackage(instance.UID);
                                        instance.Flags = ParsePackageFlagsArray(entry.GetValue<JArray>("flags"), instance.URL.ToLower().Contains(TrackerSession.Current.Global.ServiceBaseURL));

                                        JArray variants = entry.GetValue<JArray>("variants");
                                        if (variants != null)
                                        {
                                            foreach (JObject variantDef in variants)
                                            {
                                                instance.Variants.Add(new VariantEntry()
                                                {
                                                    Name = variantDef.GetValue<string>("name"),
                                                    Flags = ParsePackageFlagsArray(variantDef.GetValue<JArray>("flags"), instance.URL.ToLower().Contains(TrackerSession.Current.Global.ServiceBaseURL))
                                                });
                                            }
                                        }

                                        if (!string.IsNullOrWhiteSpace(instance.Name) &&
                                            !string.IsNullOrWhiteSpace(instance.UID) &&
                                            !string.IsNullOrWhiteSpace(instance.URL))
                                        {
                                            parsed.Add(instance);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                parseSucceeded = true;
            }
            catch
            {
                // Fall through; parseSucceeded remains false.
            }

            // Commit results on the UI thread so ObservableCollection mutations and their
            // CollectionChanged notifications always fire on the correct thread.
            var capturedParsed = parsed;
            var capturedName = parsedName;
            EmoTracker.Core.Services.Dispatch.BeginInvoke(() =>
            {
                if (!parseSucceeded)
                {
                    DownloadStatus = DownloadStatus.Error;
                    PackageManager.Instance.NotifyRepositoryUpdated(this);
                    return;
                }

                if (capturedName != null)
                    Name = capturedName;

                mPackages.Clear();
                foreach (var entry in capturedParsed)
                    mPackages.Add(entry);

                DownloadStatus = DownloadStatus.Complete;
                PackageManager.Instance.ForceRefreshProperty("UpdatesAvailable");
                PackageManager.Instance.ForceRefreshProperty("CurrentPackageHasUpdateAvailable");
                PackageManager.Instance.NotifyRepositoryUpdated(this);
            });
        }
    }
}
