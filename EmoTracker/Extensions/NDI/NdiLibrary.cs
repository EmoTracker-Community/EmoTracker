using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace EmoTracker.Extensions.NDI
{
    /// <summary>
    /// Ensures the NDI native runtime library can be found at P/Invoke load time.
    ///
    /// On Windows the NDI Tools installer places the native DLL under a versioned
    /// directory (e.g. C:\Program Files\NDI\NDI 6 Runtime\v6\) and records that
    /// path in the machine-scope environment variable NDI_RUNTIME_DIR_V6 (or V5
    /// for older installs).  It does NOT add the directory to the system PATH, so
    /// NativeLibrary.TryLoad("Processing.NDI.Lib.x64.dll") would fail with the
    /// DLL's default search order.
    ///
    /// Calling EnsureRuntimeOnPath() before any NDI P/Invoke call prepends the
    /// runtime directory to the process PATH so the loader can find the DLL
    /// without requiring it to be copied next to the application binary.
    ///
    /// On macOS/Linux, NDILibDotNetCoreBase's own static constructor already
    /// registers a NativeLibrary.SetDllImportResolver for its assembly (do NOT
    /// register a second one here - .NET only allows one per assembly, and a
    /// second SetDllImportResolver call throws inside that cctor, permanently
    /// breaking NDIlib for the process). Decompiling that resolver shows:
    ///   - Linux: it only tries loading the single absolute path
    ///     "<app base directory>/libndi.so" - no system paths, wrong version
    ///     suffix (real installs ship libndi.so.5, not libndi.so).
    ///   - macOS: it does a bare NativeLibrary.TryLoad("libndi.dylib") with no
    ///     path. This reliably fails on Apple Silicon, where .NET executables
    ///     get automatic ad-hoc code signing, which macOS treats as a
    ///     "restricted" process - dyld strips DYLD_*-based fallback search
    ///     (e.g. /usr/local/lib) for restricted processes, so the system
    ///     install is never found even though it's present.
    /// When that resolver returns IntPtr.Zero, .NET's default P/Invoke
    /// resolution runs next and probes the app's own base directory for
    /// "NDILib"/"NDILib.dylib"/"libNDILib.dylib"/"libNDILib" (Linux:
    /// "NDILib"/"NDILib.so"/"libNDILib.so"). That full-path, same-directory
    /// load isn't subject to the restricted-process search restriction, which
    /// is exactly why manually copying the installed library into the app
    /// directory under one of those names (confirmed by a reporter in issue
    /// #100) works around this. EnsureRuntimeOnPath() automates that: it
    /// locates the system-installed NDI library and symlinks it into the app's
    /// base directory under the name each platform's own resolution path
    /// already expects, without touching SetDllImportResolver.
    ///
    /// Version compatibility note:
    ///   NDILibDotNetCoreBase requires NDI SDK 5.x or 6.x.  NDI 4.0/4.1 have an
    ///   incompatible source_t struct layout (p_ip_address vs p_url_address) and
    ///   are missing the v3 audio/recv APIs the wrapper imports.  NDI 4.5 fixes
    ///   those issues, but the NDI_RUNTIME_DIR_V4 env var cannot distinguish 4.5
    ///   from the broken 4.0/4.1 installs, so V4 is intentionally not probed.
    ///   NDI 3.x lacks recv_create_v3, send_send_audio_v3, and audio_frame_v3_t
    ///   entirely, making it incompatible regardless.
    /// </summary>
    internal static class NdiLibrary
    {
        private static bool _initialized;

        public static void EnsureRuntimeOnPath()
        {
            if (_initialized)
                return;
            _initialized = true;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Matches NDILibDotNetCoreBase's own default-resolution fallback
                // name (the one that actually worked for the issue #100 reporter),
                // not the vendor resolver's bare "libndi.dylib" attempt, which
                // fails under Apple Silicon's restricted-process dyld rules.
                LinkSystemLibrary(
                    linkName: "libNDILib.dylib",
                    candidateTargets: new[] { "/usr/local/lib/libndi.dylib", "/opt/homebrew/lib/libndi.dylib" });
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Matches the vendor resolver's own hardcoded expectation exactly
                // (decompiled: Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libndi.so")),
                // so this is found on the vendor resolver's first attempt.
                LinkSystemLibrary(
                    linkName: "libndi.so",
                    candidateTargets: FindLinuxNdiLibrary());
                return;
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            // Only probe versions whose runtime DLLs are ABI-compatible with
            // NDILibDotNetCoreBase (NDI 5.x and 6.x).  V4 is excluded because
            // NDI_RUNTIME_DIR_V4 cannot distinguish NDI 4.5 (compatible) from
            // NDI 4.0/4.1 (incompatible struct layout, missing v3 APIs).
            // V3 is excluded because it is missing required API entry points.
            string[] runtimeEnvVars =
            {
                "NDI_RUNTIME_DIR_V6",
                "NDI_RUNTIME_DIR_V5",
            };

            foreach (string envVar in runtimeEnvVars)
            {
                // Probe process scope first (cheapest), then fall back to
                // machine scope. The NDI Tools installer writes the runtime
                // dir to MACHINE scope; processes that started before the
                // installer ran (or were launched from a context that didn't
                // refresh its environment block) will have the value visible
                // ONLY at machine scope. Without this fallback, a user with
                // NDI 6 properly installed gets a "Unable to load DLL 'NDILib'"
                // error after running EmoTracker from such a stale-env shell.
                string dir = Environment.GetEnvironmentVariable(envVar);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    dir = Environment.GetEnvironmentVariable(envVar, EnvironmentVariableTarget.Machine);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    continue;

                string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

                // Avoid duplicating the entry if already present.
                if (currentPath.IndexOf(dir, StringComparison.OrdinalIgnoreCase) < 0)
                    Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + currentPath);

                return;
            }

            // If no runtime env var resolves, NDI Tools is not installed.
            // NDIlib.initialize() will fail and the caller should surface an
            // appropriate error to the user.
        }

        // Symlinks the first candidate that exists into the app's own base
        // directory under linkName, so the app-directory probing described
        // above finds it. Leaves things alone if linkName already exists
        // (real file or symlink, broken or not) - e.g. a user's own manual
        // workaround - and is best-effort: any failure (missing candidates,
        // unwritable app directory) is silently ignored, and NDIlib.initialize()
        // fails visibly via the existing warning/exception logging.
        private static void LinkSystemLibrary(string linkName, string[] candidateTargets)
        {
            try
            {
                string linkPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, linkName);
                var linkInfo = new FileInfo(linkPath);
                if (linkInfo.Exists || linkInfo.LinkTarget != null)
                    return;

                foreach (string candidate in candidateTargets)
                {
                    if (File.Exists(candidate))
                    {
                        File.CreateSymbolicLink(linkPath, candidate);
                        return;
                    }
                }
            }
            catch
            {
            }
        }

        // NDI's Linux installer doesn't standardize on a single directory, so
        // ask the dynamic linker cache (populated by ldconfig at install time)
        // rather than guessing distro-specific paths. Falls back to a couple of
        // common locations if ldconfig is unavailable or the cache lookup fails.
        private static string[] FindLinuxNdiLibrary()
        {
            try
            {
                var startInfo = new ProcessStartInfo("ldconfig", "-p")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                };

                using Process process = Process.Start(startInfo);
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(2000);

                foreach (string line in output.Split('\n'))
                {
                    int arrow = line.IndexOf("=>", StringComparison.Ordinal);
                    if (arrow < 0)
                        continue;

                    string name = line.Substring(0, arrow).Trim();
                    if (name.StartsWith("libndi.so", StringComparison.Ordinal))
                        return new[] { line.Substring(arrow + 2).Trim() };
                }
            }
            catch
            {
            }

            return new[]
            {
                "/usr/lib/libndi.so.5",
                "/usr/local/lib/libndi.so.5",
                "/usr/lib/x86_64-linux-gnu/libndi.so.5",
            };
        }
    }
}
