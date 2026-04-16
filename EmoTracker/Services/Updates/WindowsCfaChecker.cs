using Microsoft.Win32;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EmoTracker.Services.Updates
{
    /// <summary>
    /// Detects whether Windows Defender Controlled Folder Access (CFA) is enabled
    /// AND the given path sits inside one of the default protected folders (Desktop,
    /// Documents, Pictures, Videos, Music).  When both conditions are true, xcopy
    /// run by the swap script will be silently blocked by Windows Security.
    /// </summary>
    internal static class WindowsCfaChecker
    {
        private const string CfaKeyPath =
            @"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access";
        private const string CfaValueName = "EnableControlledFolderAccess";

        // CFA value: 0 = disabled, 1 = enabled (blocks writes), 2 = audit only (logs but does not block)
        private const int CfaEnabled = 1;

        /// <summary>
        /// Returns true if CFA would block writes to <paramref name="directoryPath"/>.
        /// Always returns false on non-Windows platforms.
        /// </summary>
        public static bool IsBlockingPath(string directoryPath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            return IsBlockingPathOnWindows(directoryPath);
        }

        [SupportedOSPlatform("windows")]
        private static bool IsBlockingPathOnWindows(string directoryPath)
        {
            return IsCfaEnabled() && IsInDefaultProtectedFolder(directoryPath);
        }

        [SupportedOSPlatform("windows")]
        private static bool IsCfaEnabled()
        {
            try
            {
                using RegistryKey key = Registry.LocalMachine.OpenSubKey(CfaKeyPath);
                if (key?.GetValue(CfaValueName) is int value)
                    return value == CfaEnabled;
            }
            catch
            {
                // Registry access can fail on locked-down machines; treat as not blocking.
            }
            return false;
        }

        /// <summary>
        /// Checks whether <paramref name="path"/> is rooted inside one of the five
        /// folders that Windows protects by default when CFA is enabled.
        /// </summary>
        private static bool IsInDefaultProtectedFolder(string path)
        {
            string normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

            Environment.SpecialFolder[] defaultProtected =
            [
                Environment.SpecialFolder.Desktop,
                Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolder.MyPictures,
                Environment.SpecialFolder.MyVideos,
                Environment.SpecialFolder.MyMusic,
            ];

            foreach (Environment.SpecialFolder folder in defaultProtected)
            {
                string folderPath = Environment.GetFolderPath(folder);
                if (string.IsNullOrEmpty(folderPath)) continue;

                string normalizedFolder = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
                if (normalized.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
