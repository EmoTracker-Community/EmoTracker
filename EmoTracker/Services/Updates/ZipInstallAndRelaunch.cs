using Avalonia.Threading;
using EmoTracker.UI;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace EmoTracker.Services.Updates
{
    /// <summary>
    /// Handles the apply-and-relaunch step for zip/tar archive update packages.
    ///
    /// Strategy (all platforms):
    ///   1. Extract the downloaded archive into a staging directory under
    ///      LocalApplicationData (e.g. %LOCALAPPDATA%\EmoTracker\.update-staging on
    ///      Windows). Keeping staging outside the install directory avoids
    ///      Controlled Folder Access blocks and OneDrive sync lock contention when
    ///      the app is installed in Desktop or Documents.
    ///   2. Write a platform-specific swap script that waits for this process to
    ///      exit, copies the staging files over the install directory, deletes
    ///      staging, and relaunches EmoTracker.
    ///   3. Launch the script detached and call Environment.Exit(0).
    ///
    /// Windows: a .bat file that polls tasklist until our PID disappears, then
    ///          uses xcopy to overwrite the install directory in-place.
    /// macOS:   a .sh file that replaces the entire .app bundle and relaunches via
    ///          `open`, which is required for proper bundle context and Gatekeeper.
    /// Linux:   a .sh file that copies flat staging files over the install directory
    ///          and relaunches the binary directly.
    /// </summary>
    public static class ZipInstallAndRelaunch
    {
        public static async Task RunDownloadedInstallerAsync(string downloadFilePath)
        {
            try
            {
                string installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

                // Pre-flight: if Windows Defender Controlled Folder Access is enabled and
                // the install directory is inside a default protected folder (Desktop,
                // Documents, etc.), xcopy in the swap script will be silently blocked.
                // Show an actionable warning and abort before touching anything.
                if (WindowsCfaChecker.IsBlockingPath(installDir))
                {
                    Log.Warning("[Update] Controlled Folder Access will block the update in: {Dir}", installDir);
                    var tcs = new TaskCompletionSource<bool>();
                    Dispatcher.UIThread.Post(() =>
                    {
                        var warn = new CfaWarningWindow(installDir);
                        warn.Show();
                        warn.Closed += (_, _) => tcs.TrySetResult(true);
                    });
                    await tcs.Task;
                    return;
                }

                // Pre-flight: if the app is running under macOS App Translocation (a
                // randomized read-only mount Gatekeeper uses for quarantined apps
                // launched directly from Downloads), our rm/cp would target the
                // translocated clone and fail silently — the real bundle never gets
                // updated. Warn the user and abort.
                if (OperatingSystem.IsMacOS() && IsAppTranslocated(installDir))
                {
                    Log.Warning("[Update] App is running under macOS App Translocation: {Dir}", installDir);
                    var tcs = new TaskCompletionSource<bool>();
                    Dispatcher.UIThread.Post(() =>
                    {
                        var warn = new AppTranslocationWarningWindow(installDir);
                        warn.Show();
                        warn.Closed += (_, _) => tcs.TrySetResult(true);
                    });
                    await tcs.Task;
                    return;
                }

                // Stage outside the install directory so that:
                //   - Windows Defender Controlled Folder Access cannot block writes
                //     (Desktop and Documents are protected; LocalApplicationData is not)
                //   - OneDrive / known-folder-move sync cannot lock files mid-copy
                string stagingBase = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "EmoTracker");
                string stagingDir = Path.Combine(stagingBase, ".update-staging");

                Log.Information("[Update] Extracting update archive to staging dir: {Dir}", stagingDir);

                Directory.CreateDirectory(stagingBase);
                if (Directory.Exists(stagingDir))
                    Directory.Delete(stagingDir, recursive: true);
                Directory.CreateDirectory(stagingDir);

                await ExtractArchiveAsync(downloadFilePath, stagingDir, CancellationToken.None);

                string exeName = OperatingSystem.IsWindows()
                    ? "EmoTracker.exe" : "EmoTracker";

                if (OperatingSystem.IsWindows())
                    LaunchWindowsSwapScript(installDir, stagingDir, exeName);
                else if (OperatingSystem.IsMacOS())
                    LaunchMacOSSwapScript(installDir, stagingDir);
                else if (OperatingSystem.IsLinux())
                    LaunchLinuxSwapScript(installDir, stagingDir, exeName);

                Log.Information("[Update] Swap script launched — exiting for update.");
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Update] Failed to apply update: {Msg}", ex.Message);
                throw;
            }
        }

        // -----------------------------------------------------------------------
        // Archive extraction
        // -----------------------------------------------------------------------

        private static async Task ExtractArchiveAsync(
            string archivePath, string destDir, CancellationToken ct)
        {
            string lower = archivePath.ToLowerInvariant();

            //  Windows platform uses zip; other platforms use various tar formats
            //  TODO: Make this safer and more resilient
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, destDir, overwriteFiles: true), ct);
            }
            else
            {
                // Use the system tar — available on macOS, Linux, and Windows 10+.
                await RunProcessAsync("tar", $"xf \"{archivePath}\" -C \"{destDir}\"", ct);
            }
        }

        private static async Task RunProcessAsync(string exe, string arguments, CancellationToken ct)
        {
            var psi = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start {exe}");

            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode != 0)
            {
                string err = await proc.StandardError.ReadToEndAsync(ct);
                throw new InvalidOperationException(
                    $"{exe} exited with code {proc.ExitCode}: {err}");
            }
        }

        // -----------------------------------------------------------------------
        // Windows batch swap script
        // -----------------------------------------------------------------------

        private static void LaunchWindowsSwapScript(
            string installDir, string stagingDir, string exeName)
        {
            int pid = Environment.ProcessId;

            string script = $"""
                @echo off
                setlocal

                set INSTALL_DIR={installDir}
                set STAGING_DIR={stagingDir}
                set EXE_PATH={Path.Combine(installDir, exeName)}
                set PID={pid}

                :wait_loop
                tasklist /fi "pid eq %PID%" 2>nul | find "%PID%" >nul
                if %errorlevel%==0 (
                    timeout /t 1 /nobreak >nul
                    goto wait_loop
                )

                xcopy /Y /E /I /Q "%STAGING_DIR%\*" "%INSTALL_DIR%\"
                rmdir /S /Q "%STAGING_DIR%"
                start "" "%EXE_PATH%"
                """;

            string scriptPath = Path.Combine(Path.GetTempPath(), "EmoTracker_update.bat");
            File.WriteAllText(scriptPath, script);

            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
            {
                UseShellExecute  = true,
                CreateNoWindow   = true,
                WindowStyle      = ProcessWindowStyle.Hidden,
            });
        }

        // -----------------------------------------------------------------------
        // macOS shell swap script
        // -----------------------------------------------------------------------

        [System.Runtime.Versioning.SupportedOSPlatform("macos")]
        private static void LaunchMacOSSwapScript(string installDir, string stagingDir)
        {
            // The release archive contains EmoTracker.app/ at its root, so staging looks like:
            //   stagingDir/EmoTracker.app/Contents/MacOS/...
            //
            // installDir = .../EmoTracker.app/Contents/MacOS  (AppContext.BaseDirectory)
            // appBundle  = .../EmoTracker.app
            // appParent  = .../ (directory that contains the .app)
            string appBundle = Path.GetDirectoryName(Path.GetDirectoryName(installDir)!)!;
            string appParent = Path.GetDirectoryName(appBundle)!;
            string appName   = Path.GetFileName(appBundle)!; // "EmoTracker.app"
            int    pid       = Environment.ProcessId;

            string logPath    = Path.Combine(Path.GetTempPath(), "emotracker_update.log");

            Log.Information("[Update] macOS swap — installDir={InstallDir} appBundle={AppBundle} appParent={AppParent} appName={AppName} stagingDir={StagingDir}",
                installDir, appBundle, appParent, appName, stagingDir);

            // Poll for the parent PID to exit (mirrors the Windows tasklist loop) so that
            // the rm/cp never races a still-exiting EmoTracker instance. Without this,
            // 'open' can activate the dying original process instead of launching the new one.
            string script = $"""
                #!/bin/sh
                set -x
                exec >"{logPath}" 2>&1
                echo "Waiting for PID {pid} to exit..."
                while kill -0 {pid} 2>/dev/null; do
                    sleep 0.5
                done
                echo "PID gone, performing swap..."
                rm -rf "{appBundle}"
                cp -R "{stagingDir}/{appName}" "{appParent}/"
                rm -rf "{stagingDir}"
                xattr -r -d com.apple.quarantine "{appBundle}" 2>/dev/null || true
                echo "Launching app..."
                open "{appBundle}"
                """;

            string scriptPath = Path.Combine(Path.GetTempPath(), "emotracker_update.sh");
            File.WriteAllText(scriptPath, script);

            MakeExecutable(scriptPath);

            // Launch via a detached subshell (nohup + &) so the script survives this
            // process exiting. Without nohup the script can receive SIGHUP and be killed
            // when the parent App bundle terminates.
            Process.Start(new ProcessStartInfo(
                "/bin/sh", $"-c \"nohup /bin/sh '{scriptPath}' </dev/null 2>/dev/null &\"")
            {
                UseShellExecute = false,
            });
        }

        // -----------------------------------------------------------------------
        // Linux shell swap script
        // -----------------------------------------------------------------------

        [System.Runtime.Versioning.SupportedOSPlatform("linux")]
        private static void LaunchLinuxSwapScript(
            string installDir, string stagingDir, string exeName)
        {
            // The Linux archive contains flat files, so staging mirrors installDir.
            string exePath = Path.Combine(installDir, exeName);

            string script = $"""
                #!/bin/sh
                sleep 1
                cp -R "{stagingDir}/." "{installDir}/"
                rm -rf "{stagingDir}"
                chmod +x "{exePath}"
                "{exePath}" &
                """;

            string scriptPath = Path.Combine(Path.GetTempPath(), "emotracker_update.sh");
            File.WriteAllText(scriptPath, script);

            MakeExecutable(scriptPath);

            Process.Start(new ProcessStartInfo("/bin/sh", $"\"{scriptPath}\"")
            {
                UseShellExecute = false,
            });
        }

        // Gatekeeper App Translocation mounts quarantined apps at a randomized
        // read-only location so they can't modify themselves or neighboring files.
        // Detected via the path prefix macOS uses for the translocation mount.
        private static bool IsAppTranslocated(string path)
            => path.Contains("/AppTranslocation/", StringComparison.Ordinal);

        [System.Runtime.Versioning.SupportedOSPlatform("macos")]
        [System.Runtime.Versioning.SupportedOSPlatform("linux")]
        private static void MakeExecutable(string path)
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead  | UnixFileMode.UserWrite  | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }
}
