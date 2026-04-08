using System;
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
    /// On macOS and Linux, NDI Tools installs libndi.dylib / libndi.so.5 into
    /// system library directories that are already on the default search path,
    /// so no PATH manipulation is needed.
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
                string dir = Environment.GetEnvironmentVariable(envVar);
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
    }
}
