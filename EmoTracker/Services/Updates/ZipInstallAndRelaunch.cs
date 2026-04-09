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
    ///   1. Extract the downloaded archive into a .staging directory next to the
    ///      current install directory.
    ///   2. Write a platform-specific swap script that waits for this process to
    ///      exit, copies the staging files over the install directory, deletes
    ///      staging, and relaunches EmoTracker.
    ///   3. Launch the script detached and call Environment.Exit(0).
    ///
    /// Windows: a .bat file that polls tasklist until our PID disappears, then
    ///          uses xcopy to overwrite the install directory in-place.
    /// macOS / Linux: a .sh file; no file-locking concern so a brief sleep suffices.
    /// </summary>
    public static class ZipInstallAndRelaunch
    {
        public static async Task RunDownloadedInstallerAsync(string downloadFilePath)
        {
            try
            {
                string installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                string stagingDir = Path.Combine(installDir, ".update-staging");

                Log.Information("[Update] Extracting update archive to staging dir: {Dir}", stagingDir);

                if (Directory.Exists(stagingDir))
                    Directory.Delete(stagingDir, recursive: true);
                Directory.CreateDirectory(stagingDir);

                await ExtractArchiveAsync(downloadFilePath, stagingDir, CancellationToken.None);

                string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "EmoTracker.exe" : "EmoTracker";

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    LaunchWindowsSwapScript(installDir, stagingDir, exeName);
                else
                    LaunchUnixSwapScript(installDir, stagingDir, exeName);

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

            if (lower.EndsWith(".zip"))
            {
                await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, destDir, overwriteFiles: true), ct);
            }
            else if (lower.EndsWith(".tar.gz") || lower.EndsWith(".tar.xz") || lower.EndsWith(".tgz"))
            {
                // Use the system tar — available on macOS, Linux, and Windows 10+.
                await RunProcessAsync("tar", $"xf \"{archivePath}\" -C \"{destDir}\"", ct);
            }
            else
            {
                throw new NotSupportedException($"Unsupported archive format: {Path.GetFileName(archivePath)}");
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
        // macOS / Linux shell swap script
        // -----------------------------------------------------------------------

        private static void LaunchUnixSwapScript(
            string installDir, string stagingDir, string exeName)
        {
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

            // Make executable
            RunProcessAsync("chmod", $"+x \"{scriptPath}\"", CancellationToken.None).GetAwaiter().GetResult();

            Process.Start(new ProcessStartInfo("/bin/sh", $"\"{scriptPath}\"")
            {
                UseShellExecute = true,
            });
        }
    }
}
