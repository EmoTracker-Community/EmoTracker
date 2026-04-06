using EmoTracker.Data.AutoTracking;
using EmoTracker.Data.Packages;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EmoTracker.Providers.NWA
{
    [AutoTrackingProvider]
    public class NwaProvider : AutoTrackingProviderBase
    {
        const int DefaultBasePort = 0xBEEF; // 48879
        const int DefaultPortCount = 10;
        const string DefaultHost = "localhost";

        readonly List<IAutoTrackingDevice> mAvailableDevices = new List<IAutoTrackingDevice>();
        readonly List<IProviderOption> mOptions;
        IAutoTrackingDevice mDefaultDevice;

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
        public override string DisplayName => "NWA (Bizhawk)";
        public override IReadOnlyList<GamePlatform> SupportedPlatforms => sSupportedPlatforms;
        public override IReadOnlyList<IAutoTrackingDevice> AvailableDevices => mAvailableDevices;

        public override IAutoTrackingDevice DefaultDevice
        {
            get => mDefaultDevice;
            set => mDefaultDevice = value;
        }

        public override IReadOnlyList<IProviderOption> Options => mOptions;
        public override IReadOnlyList<IProviderOperation> Operations => EmptyOperations;

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
            Log.Debug("[NWA] Refreshing device list...");
            mAvailableDevices.Clear();

            var (basePort, portCount) = GetPortRange();
            var probeTasks = new List<Task<NwaEmulatorInfo>>();

            for (int i = 0; i < portCount; i++)
            {
                int port = basePort + i;
                probeTasks.Add(NwaDevice.ProbeAsync(DefaultHost, port));
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

            Log.Debug("[NWA] Found {Count} device(s)", mAvailableDevices.Count);
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
            base.Dispose();
        }
    }
}
