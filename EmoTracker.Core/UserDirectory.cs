using EmoTracker.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Core
{
    public class UserDirectory : Singleton<UserDirectory>
    {
        bool mbDev = false;
        string mPath;

        public static string Path
        {
            get { return Instance.mPath; }
        }

        public static bool IsDevMode
        {
            get { return Instance.mbDev; }
        }

        private bool CreateFolder(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(mPath);

                return true;
            }
            catch
            {
                //  TODO: Log exception
            }

            return false;
        }

        public UserDirectory()
        {
            string userDirectoryLocalPath = "EmoTracker";

            if (Environment.CommandLine.Contains("-dev"))
            {
                userDirectoryLocalPath = System.IO.Path.Combine(userDirectoryLocalPath, "dev");
                mbDev = true;
            }

            string docsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), userDirectoryLocalPath);
            if (Directory.Exists(docsPath))
            {
                mPath = docsPath;
                return;
            }

            string appDataPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), userDirectoryLocalPath);
            if (Directory.Exists(appDataPath))
            {
                mPath = appDataPath;
                return;
            }

            string backupDataPath = appDataPath;
            if (!Directory.Exists(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)))
            {
                Log.Warn("User Documents directory does not exist");

                mPath = appDataPath;
                backupDataPath = docsPath;
            }
            else
            {
                mPath = docsPath;
                backupDataPath = appDataPath;
            }

            if (!CreateFolder(mPath))
            {
                Log.Warn("Failed to create/find directory {0}", mPath);

                mPath = backupDataPath;
                if (!CreateFolder(mPath))
                {
                    Log.Warn("Failed to create/find directory {0}", mPath);

                    throw new DirectoryNotFoundException(string.Format("Failed to create or locate user folder at either `{0}` or `{1}`", docsPath, appDataPath));
                }
            }
        }
    }
}
