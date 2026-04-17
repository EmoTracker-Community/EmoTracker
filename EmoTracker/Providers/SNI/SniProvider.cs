using EmoTracker.Data;
using EmoTracker.Data.AutoTracking;
using EmoTracker.Data.Packages;
using Grpc.Net.Client;
using Serilog;
using SNI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EmoTracker.Providers.SNI
{
    [AutoTrackingProvider]
    public class SniProvider : AutoTrackingProviderBase
    {
        const string DefaultAddress = "http://localhost:8191";
        const string SettingKey = "sni_grpc_address";
        const int ScanIntervalMs = 5000;

        readonly List<IAutoTrackingDevice> mAvailableDevices = new List<IAutoTrackingDevice>();
        readonly List<IProviderOption> mOptions;
        IAutoTrackingDevice mDefaultDevice;
        GrpcChannel mChannel;
        Timer mScanTimer;
        bool mScanning;
        bool mReconnectEnabled;
        EventHandler<bool> mConnectionStatusChanged;
        EventHandler mAvailableDevicesChanged;

        static readonly IReadOnlyList<GamePlatform> sSupportedPlatforms = new[] { GamePlatform.SNES };
        static readonly IReadOnlyList<IProviderOperation> EmptyOperations = Array.Empty<IProviderOperation>();

        public SniProvider()
        {
            mOptions = new List<IProviderOption>
            {
                new SniProviderOption("address_space", "Address Space", ProviderOptionKind.Dropdown,
                    "SnesABus", new List<object> { "SnesABus", "FxPakPro", "Raw" }),
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
            set
            {
                if (mDefaultDevice != null)
                    mDefaultDevice.ConnectionStatusChanged -= OnDeviceConnectionStatusChanged;
                mDefaultDevice = value;
                if (mDefaultDevice != null)
                    mDefaultDevice.ConnectionStatusChanged += OnDeviceConnectionStatusChanged;
            }
        }

        void OnDeviceConnectionStatusChanged(object sender, bool connected)
        {
            mConnectionStatusChanged?.Invoke(this, connected);
        }

        public override bool IsConnected => mDefaultDevice?.IsConnected ?? false;

        public override event EventHandler<bool> ConnectionStatusChanged
        {
            add { mConnectionStatusChanged += value; }
            remove { mConnectionStatusChanged -= value; }
        }

        public override event EventHandler AvailableDevicesChanged
        {
            add { mAvailableDevicesChanged += value; }
            remove { mAvailableDevicesChanged -= value; }
        }

        public override IReadOnlyList<IProviderOption> Options => mOptions;
        public override IReadOnlyList<IProviderOperation> Operations => EmptyOperations;

        internal GrpcChannel Channel => mChannel;

        static string GetUserAgent()
        {
            return $"EmoTracker/{Core.ApplicationVersion.Current}";
        }

        string GetAddress()
        {
            return ApplicationSettings.Instance.GetProviderSetting(SettingKey, DefaultAddress);
        }

        GrpcChannel EnsureChannel()
        {
            if (mChannel == null)
            {
                var address = GetAddress();
                var userAgent = GetUserAgent();
                Log.Debug("[SNI] Creating gRPC channel to {Address} (User-Agent: {UserAgent})", address, userAgent);
                mChannel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
                {
                    HttpHandler = new UserAgentHandler(userAgent)
                });
            }
            return mChannel;
        }

        class UserAgentHandler : DelegatingHandler
        {
            readonly string mUserAgent;

            public UserAgentHandler(string userAgent) : base(new HttpClientHandler())
            {
                mUserAgent = userAgent;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                request.Headers.UserAgent.Clear();
                request.Headers.UserAgent.ParseAdd(mUserAgent);
                return base.SendAsync(request, cancellationToken);
            }
        }

        public override async Task ConnectAsync()
        {
            mReconnectEnabled = true;

            if (mDefaultDevice != null)
                await mDefaultDevice.ConnectAsync().ConfigureAwait(false);

            // Ensure the scan timer is running so reconnect attempts fire every 5 seconds
            if (mScanTimer == null)
                mScanTimer = new Timer(OnScanTimerElapsed, null, ScanIntervalMs, ScanIntervalMs);
        }

        public override async Task DisconnectAsync()
        {
            mReconnectEnabled = false;

            if (mDefaultDevice != null)
                await mDefaultDevice.DisconnectAsync().ConfigureAwait(false);
        }

        void OnScanTimerElapsed(object state)
        {
            // Fire-and-forget; ScanAndReconnectAsync guards against re-entrancy
            _ = ScanAndReconnectAsync();
        }

        async Task ScanAndReconnectAsync()
        {
            if (mScanning)
                return;

            mScanning = true;
            try
            {
                await RefreshDevicesAsync().ConfigureAwait(false);

                // If the active device was removed from the list, clear it and signal disconnected
                if (mDefaultDevice != null && !mAvailableDevices.Contains(mDefaultDevice))
                {
                    await mDefaultDevice.DisconnectAsync().ConfigureAwait(false);
                    DefaultDevice = null;
                    mConnectionStatusChanged?.Invoke(this, false);
                }

                // Try to reconnect if we have a device but are not currently connected
                if (mReconnectEnabled && mDefaultDevice != null && !IsConnected)
                    await mDefaultDevice.ConnectAsync().ConfigureAwait(false);
            }
            finally
            {
                mScanning = false;
            }
        }

        public override async Task RefreshDevicesAsync()
        {
            Log.Debug("[SNI] Refreshing device list...");

            try
            {
                var channel = EnsureChannel();
                var devicesClient = new Devices.DevicesClient(channel);
                var response = await devicesClient.ListDevicesAsync(new DevicesRequest()).ConfigureAwait(false);

                Log.Debug("[SNI] Found {Count} device(s)", response.Devices.Count);

                var responseUris = response.Devices.Select(d => d.Uri).ToHashSet();

                // Remove devices that are no longer present
                for (int i = mAvailableDevices.Count - 1; i >= 0; i--)
                {
                    if (!responseUris.Contains(mAvailableDevices[i].Id))
                        mAvailableDevices.RemoveAt(i);
                }

                // Add newly discovered devices, reusing existing instances where possible
                var existingUris = mAvailableDevices.Select(d => d.Id).ToHashSet();
                foreach (var device in response.Devices)
                {
                    if (!existingUris.Contains(device.Uri))
                    {
                        Log.Debug("[SNI] Device: {DisplayName} ({Uri})", device.DisplayName, device.Uri);
                        mAvailableDevices.Add(new SniDevice(device.Uri, device.DisplayName, this));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[SNI] Failed to refresh devices: {Message}", ex.Message);
                mAvailableDevices.Clear();
            }

            // Start the scan timer if not already running so the device list stays fresh
            if (mScanTimer == null)
                mScanTimer = new Timer(OnScanTimerElapsed, null, ScanIntervalMs, ScanIntervalMs);

            mAvailableDevicesChanged?.Invoke(this, EventArgs.Empty);
        }

        public override void Dispose()
        {
            Log.Debug("[SNI] Disposing provider");

            mReconnectEnabled = false;

            if (mScanTimer != null)
            {
                mScanTimer.Dispose();
                mScanTimer = null;
            }

            // Unwire device event before base disposes devices
            if (mDefaultDevice != null)
            {
                mDefaultDevice.ConnectionStatusChanged -= OnDeviceConnectionStatusChanged;
                mDefaultDevice = null;
            }

            base.Dispose();

            if (mChannel != null)
            {
                mChannel.Dispose();
                mChannel = null;
            }
        }
    }
}
