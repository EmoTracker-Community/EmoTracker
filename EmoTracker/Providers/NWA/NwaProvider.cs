using EmoTracker.Data;
using EmoTracker.Data.AutoTracking;
using EmoTracker.Data.Packages;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmoTracker.Data.Session;

namespace EmoTracker.Providers.NWA
{
    [AutoTrackingProvider]
    public class NwaProvider : AutoTrackingProviderBase
    {
        const int DefaultBasePort = 0xBEEF; // 48879
        const int DefaultPortCount = 10;
        const string DefaultHost = "localhost";
        const string SettingKey = "nwa_host";
        const int ScanIntervalMs = 5000;

        readonly List<IAutoTrackingDevice> mAvailableDevices = new List<IAutoTrackingDevice>();
        readonly List<IProviderOption> mOptions;
        IAutoTrackingDevice mDefaultDevice;
        Timer mScanTimer;
        bool mScanning;

        static readonly IReadOnlyList<GamePlatform> sSupportedPlatforms = new[]
        {
            GamePlatform.NES,
            GamePlatform.SNES,
            GamePlatform.N64,
            GamePlatform.Gameboy,
            GamePlatform.GBA,
            GamePlatform.Gamecube,
            GamePlatform.Genesis
        };

        static readonly IReadOnlyList<IProviderOperation> EmptyOperations = Array.Empty<IProviderOperation>();

        static readonly Dictionary<string, GamePlatform> sPlatformMap = new Dictionary<string, GamePlatform>(StringComparer.OrdinalIgnoreCase)
        {
            { "nes", GamePlatform.NES },
            { "famicom", GamePlatform.NES },
            { "snes", GamePlatform.SNES },
            { "superfamicom", GamePlatform.SNES },
            { "super famicom", GamePlatform.SNES },
            { "super nintendo", GamePlatform.SNES },
            { "n64", GamePlatform.N64 },
            { "nintendo 64", GamePlatform.N64 },
            { "gb", GamePlatform.Gameboy },
            { "gameboy", GamePlatform.Gameboy },
            { "game boy", GamePlatform.Gameboy },
            { "gbc", GamePlatform.Gameboy },
            { "sgb", GamePlatform.Gameboy },
            { "gba", GamePlatform.GBA },
            { "game boy advance", GamePlatform.GBA },
            { "gamecube", GamePlatform.Gamecube },
            { "gc", GamePlatform.Gamecube },
            { "ngc", GamePlatform.Gamecube },
            { "gen", GamePlatform.Genesis },
            { "genesis", GamePlatform.Genesis },
            { "megadrive", GamePlatform.Genesis },
            { "mega drive", GamePlatform.Genesis },
            { "sega genesis", GamePlatform.Genesis },
        };

        public NwaProvider()
        {
            mOptions = new List<IProviderOption>();
        }

        public override string UID => "nwa";
        public override string DisplayName => "NWA";
        public override IReadOnlyList<GamePlatform> SupportedPlatforms => sSupportedPlatforms;
        public override IReadOnlyList<IAutoTrackingDevice> AvailableDevices => mAvailableDevices;

        public override IAutoTrackingDevice DefaultDevice
        {
            get => mDefaultDevice;
            set => mDefaultDevice = value;
        }

        public override IReadOnlyList<IProviderOption> Options => mOptions;
        public override IReadOnlyList<IProviderOperation> Operations => EmptyOperations;

        static string GetHost()
        {
            return TrackerSession.Current.Global.GetProviderSetting(SettingKey, DefaultHost);
        }

        static (int basePort, int count) GetPortRange()
        {
            string envValue = Environment.GetEnvironmentVariable("NWA_PORT_RANGE");
            if (!string.IsNullOrEmpty(envValue))
            {
                // NWA_PORT_RANGE can be "port" or "port-port"
                string[] parts = envValue.Split('-');
                if (parts.Length == 2 && int.TryParse(parts[0], out int start) && int.TryParse(parts[1], out int end))
                {
                    int count = end - start + 1;
                    if (count > 0 && count <= 100)
                    {
                        Log.Debug("[NWA] Using NWA_PORT_RANGE={Start}-{End} ({Count} ports)", start, end, count);
                        return (start, count);
                    }
                }
                else if (parts.Length == 1 && int.TryParse(parts[0], out int single))
                {
                    Log.Debug("[NWA] Using NWA_PORT_RANGE={Port} (single base port)", single);
                    return (single, DefaultPortCount);
                }

                Log.Warning("[NWA] Invalid NWA_PORT_RANGE value: {Value}, using defaults", envValue);
            }

            return (DefaultBasePort, DefaultPortCount);
        }

        public override async Task RefreshDevicesAsync()
        {
            if (mScanning)
                return;

            mScanning = true;
            try
            {
                Log.Debug("[NWA] Refreshing device list...");

                // Index existing devices by port so we can reuse instances
                var existingByPort = new Dictionary<int, NwaDevice>();
                foreach (var device in mAvailableDevices)
                {
                    var nwaDevice = device as NwaDevice;
                    if (nwaDevice != null)
                        existingByPort[nwaDevice.Port] = nwaDevice;
                }

                // Identify connected ports — skip these during probing
                var connectedPorts = new HashSet<int>();
                foreach (var kvp in existingByPort)
                {
                    if (kvp.Value.IsConnected)
                        connectedPorts.Add(kvp.Key);
                }

                mAvailableDevices.Clear();

                // Re-add connected devices first
                foreach (var port in connectedPorts)
                    mAvailableDevices.Add(existingByPort[port]);

                var host = GetHost();
                var (basePort, portCount) = GetPortRange();
                var probeTasks = new List<Task<NwaEmulatorInfo>>();

                for (int i = 0; i < portCount; i++)
                {
                    int port = basePort + i;
                    if (connectedPorts.Contains(port))
                        continue;
                    probeTasks.Add(NwaDevice.ProbeAsync(host, port));
                }

                NwaEmulatorInfo[] results;
                try
                {
                    results = await Task.WhenAll(probeTasks).ConfigureAwait(false);
                }
                catch
                {
                    // WhenAll throws on first faulted task; handle individually below
                    results = new NwaEmulatorInfo[probeTasks.Count];
                    for (int i = 0; i < probeTasks.Count; i++)
                    {
                        try { results[i] = await probeTasks[i].ConfigureAwait(false); }
                        catch { results[i] = null; }
                    }
                }

                foreach (var info in results)
                {
                    if (info == null)
                        continue;

                    // Reuse existing device instance for this port if one exists
                    if (existingByPort.TryGetValue(info.Port, out var existing))
                    {
                        mAvailableDevices.Add(existing);
                        continue;
                    }

                    string displayName = !string.IsNullOrEmpty(info.Version)
                        ? $"{info.Name} v{info.Version} (:{info.Port})"
                        : $"{info.Name} (:{info.Port})";

                    if (!string.IsNullOrEmpty(info.Game))
                    {
                        const int MaxGameNameLength = 30;
                        string gameName = info.Game.Length > MaxGameNameLength
                            ? info.Game.Substring(0, MaxGameNameLength) + "\u2026"
                            : info.Game;
                        displayName += $" \u2014 {gameName}";
                    }

                    string platform = info.Platform;

                    Log.Debug("[NWA] Found emulator: {DisplayName}, platform={Platform}", displayName, platform ?? "(unknown)");
                    mAvailableDevices.Add(new NwaDevice(info.Host, info.Port, displayName, platform, this));
                }

                // Clear DefaultDevice if it's no longer in the available list
                if (mDefaultDevice != null && !mAvailableDevices.Contains(mDefaultDevice))
                    mDefaultDevice = null;

                Log.Debug("[NWA] Found {Count} device(s) ({ConnectedCount} connected)", mAvailableDevices.Count, connectedPorts.Count);

                // Start periodic scanning if not already running
                if (mScanTimer == null)
                {
                    mScanTimer = new Timer(OnScanTimerElapsed, null, ScanIntervalMs, ScanIntervalMs);
                }
            }
            finally
            {
                mScanning = false;
            }
        }

        void OnScanTimerElapsed(object state)
        {
            // Fire-and-forget; RefreshDevicesAsync guards against re-entrancy
            _ = RefreshDevicesAsync();
        }

        /// <summary>
        /// Called by NwaDevice when it detects a forceful disconnection.
        /// Triggers an immediate rescan to update the device list.
        /// </summary>
        internal void OnDeviceDisconnected()
        {
            Log.Debug("[NWA] Device disconnected, scheduling rescan...");
            _ = RefreshDevicesAsync();
        }

        internal static GamePlatform MapPlatform(string platform)
        {
            if (string.IsNullOrEmpty(platform))
                return GamePlatform.Unknown;

            if (sPlatformMap.TryGetValue(platform, out var mapped))
                return mapped;

            return GamePlatform.Unknown;
        }

        public override void Dispose()
        {
            Log.Debug("[NWA] Disposing provider");

            if (mScanTimer != null)
            {
                mScanTimer.Dispose();
                mScanTimer = null;
            }

            base.Dispose();
        }
    }
}
