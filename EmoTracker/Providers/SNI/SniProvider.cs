using EmoTracker.Data.AutoTracking;
using EmoTracker.Data.Packages;
using Grpc.Net.Client;
using Serilog;
using SNI;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EmoTracker.Providers.SNI
{
    [AutoTrackingProvider]
    public class SniProvider : AutoTrackingProviderBase
    {
        readonly List<IAutoTrackingDevice> mAvailableDevices = new List<IAutoTrackingDevice>();
        readonly List<IProviderOption> mOptions;
        IAutoTrackingDevice mDefaultDevice;
        GrpcChannel mChannel;

        static readonly IReadOnlyList<GamePlatform> sSupportedPlatforms = new[] { GamePlatform.SNES };
        static readonly IReadOnlyList<IProviderOperation> EmptyOperations = Array.Empty<IProviderOperation>();

        public SniProvider()
        {
            mOptions = new List<IProviderOption>
            {
                new SniProviderOption("address_space", "Address Space", ProviderOptionKind.Dropdown,
                    "FxPakPro", new List<object> { "FxPakPro", "SnesABus", "Raw" }),
                new SniProviderOption("memory_mapping", "Memory Mapping", ProviderOptionKind.Dropdown,
                    "Auto", new List<object> { "Auto", "LoROM", "HiROM", "ExHiROM", "SA1" })
            };
        }

        public override string UID => "sni";
        public override string DisplayName => "SNI";
        public override IReadOnlyList<GamePlatform> SupportedPlatforms => sSupportedPlatforms;
        public override IReadOnlyList<IAutoTrackingDevice> AvailableDevices => mAvailableDevices;

        public override IAutoTrackingDevice DefaultDevice
        {
            get => mDefaultDevice;
            set => mDefaultDevice = value;
        }

        public override IReadOnlyList<IProviderOption> Options => mOptions;
        public override IReadOnlyList<IProviderOperation> Operations => EmptyOperations;

        internal GrpcChannel Channel => mChannel;

        GrpcChannel EnsureChannel()
        {
            if (mChannel == null)
            {
                Log.Debug("[SNI] Creating gRPC channel to http://localhost:8191");
                mChannel = GrpcChannel.ForAddress("http://localhost:8191");
            }
            return mChannel;
        }

        public override async Task RefreshDevicesAsync()
        {
            Log.Debug("[SNI] Refreshing device list...");
            mAvailableDevices.Clear();

            try
            {
                var channel = EnsureChannel();
                var devicesClient = new Devices.DevicesClient(channel);
                var response = await devicesClient.ListDevicesAsync(new DevicesRequest()).ConfigureAwait(false);

                Log.Debug("[SNI] Found {Count} device(s)", response.Devices.Count);
                foreach (var device in response.Devices)
                {
                    Log.Debug("[SNI] Device: {DisplayName} ({Uri})", device.DisplayName, device.Uri);
                    mAvailableDevices.Add(new SniDevice(device.Uri, device.DisplayName, this));
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[SNI] Failed to refresh devices: {Message}", ex.Message);
            }
        }

        public override void Dispose()
        {
            Log.Debug("[SNI] Disposing provider");
            base.Dispose();

            if (mChannel != null)
            {
                mChannel.Dispose();
                mChannel = null;
            }
        }
    }
}
