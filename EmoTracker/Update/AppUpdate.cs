using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Diagnostics;
using EmoTracker.Core;
using EmoTracker.Data.JSON;
using System.Windows;
using EmoTracker.Data.Packages;
using EmoTracker.Data;

namespace EmoTracker.Update
{
    public class AppUpdate : ObservableSingleton<AppUpdate>
    {
        static Uri AppUpdateURI = PackageManager.BuildServiceUri("application_version.json");

        public enum UpdateStatus
        {
            Idle,
            RunningLegacyApp,
            CheckingForUpdate,
            UpdateAvailable,
            NoUpdateAvailable,
            DownloadingUpdate,
            InstallingUpdate,
            Error
        }

        UpdateStatus mStatus = UpdateStatus.Idle;

        public UpdateStatus Status
        {
            get { return mStatus; }
            set { SetProperty(ref mStatus, value); }
        }

        int mDownloadPercentage = 0;

        public int DownloadPercentage
        {
            get { return mDownloadPercentage; }
            set { SetProperty(ref mDownloadPercentage, value); }
        }       

        WebClient mWebClient;

        Version mCurrentAppVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        Version mAvailableAppVersion;
        string mAvailableVersionURL;

        public Version CurrentVersion
        {
            get { return mCurrentAppVersion; }
        }

        public Version AvailableVersion
        {
            get { return mAvailableAppVersion; }
            protected set { SetProperty(ref mAvailableAppVersion, value); }
        }

        public bool UpdateAvailable
        {
            get { return AvailableVersion != null && AvailableVersion > CurrentVersion; }
        }

        DelegateCommand mDownloadAndInstallUpdateCommand;

        public DelegateCommand DownloadAndInstallUpdateCommand
        {
            get { return mDownloadAndInstallUpdateCommand; }
            private set { SetProperty(ref mDownloadAndInstallUpdateCommand, value); }
        }


        public AppUpdate()
        {
            mWebClient = new WebClient();
            mWebClient.Headers.Add("User-Agent", string.Format("EmoTracker/{0} (Windows)", CurrentVersion));
            mWebClient.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            mWebClient.DownloadDataCompleted += MWebClient_DownloadDataCompleted;
            mWebClient.DownloadFileCompleted += MWebClient_DownloadFileCompleted;
            mWebClient.DownloadProgressChanged += MWebClient_DownloadProgressChanged;

            DownloadAndInstallUpdateCommand = new DelegateCommand(DownloadAndInstallUpdate);
        }

        private void DownloadAndInstallUpdate(object obj)
        {
            if (!string.IsNullOrWhiteSpace(mAvailableVersionURL))
            {
                Status = UpdateStatus.DownloadingUpdate;
                string temp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EmoTracker", "emotracker_setup.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(temp));
                mWebClient.DownloadFileAsync(new Uri(mAvailableVersionURL), temp, temp);
            }
        }

        private void MWebClient_DownloadDataCompleted(object sender, DownloadDataCompletedEventArgs e)
        {
            if (e.Error != null || e.Result == null || e.Result.Length <= 0)
            {
                Status = UpdateStatus.Error;
                return;
            }

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
                            string serviceURL = root.GetValue<string>("service_url", null);
                            if (!string.IsNullOrWhiteSpace(serviceURL))
                            {
                                ApplicationSettings.Instance.ServiceBaseURL = serviceURL;
                            }

                            string version = root.GetValue<string>("version", null);
                            if (!string.IsNullOrWhiteSpace(version))
                            {
                                Version temp = new Version();
                                if (Version.TryParse(version, out temp))
                                {
                                    AvailableVersion = temp;
                                    mAvailableVersionURL = root.GetValue<string>("link");
                                }
                            }                            
                        }
                    }
                }

                if (UpdateAvailable)
                {
                    if (CurrentVersion.Major == 0)
                        Status = UpdateStatus.RunningLegacyApp;
                    else
                        Status = UpdateStatus.UpdateAvailable;
                }
                else
                    Status = UpdateStatus.NoUpdateAvailable;
            }
            catch
            {
                Status = UpdateStatus.Error;
            }
        }

        private void MWebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            DownloadPercentage = (int)(((double)e.BytesReceived / (double)e.TotalBytesToReceive) * 100.0 + 0.5);
        }

        private void MWebClient_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                Status = UpdateStatus.Error;
                return;
            }

            try
            {
                Status = UpdateStatus.InstallingUpdate;

                string path = (string)e.UserState;
                Process.Start(path);
                Application.Current.Shutdown();
            }
            catch
            {
                Status = UpdateStatus.Error;
            }
        }

        public void CheckForUpdates()
        {
            //  TEMPORARILY DISABLE UPDATES
            return;

            try
            {
                Status = UpdateStatus.CheckingForUpdate;
                mWebClient.DownloadDataAsync(AppUpdateURI);
            }
            catch
            {
                Status = UpdateStatus.Error;
            }
        }
    }
}
