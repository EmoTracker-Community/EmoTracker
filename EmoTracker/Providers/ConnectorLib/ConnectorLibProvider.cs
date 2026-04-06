using ConnectorLib;
using EmoTracker.Core;
using EmoTracker.Data.AutoTracking;
using EmoTracker.Data.Packages;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EmoTracker.Providers.ConnectorLib
{
    [AutoTrackingProvider]
    public class ConnectorLibProvider : AutoTrackingProviderBase
    {
        readonly List<IAutoTrackingDevice> mAvailableDevices = new List<IAutoTrackingDevice>();
        IAutoTrackingDevice mDefaultDevice;
        GamePlatform mActivePlatform;

        static readonly IReadOnlyList<GamePlatform> sSupportedPlatforms = new[]
        {
            GamePlatform.NES,
            GamePlatform.SNES,
            GamePlatform.N64,
            GamePlatform.Gameboy,
            GamePlatform.GBA,
            GamePlatform.Genesis
        };

        static readonly IReadOnlyList<IProviderOption> EmptyOptions = Array.Empty<IProviderOption>();
        static readonly IReadOnlyList<IProviderOperation> EmptyOperations = Array.Empty<IProviderOperation>();

        public ConnectorLibProvider()
        {
            sd2snesConnector.Usb2SnesApplicationName = string.Format("EmoTracker {0}", ApplicationVersion.Current);
        }

        public override string UID => "connectorlib";
        public override string DisplayName => "ConnectorLib";
        public override IReadOnlyList<GamePlatform> SupportedPlatforms => sSupportedPlatforms;

        public override IReadOnlyList<IAutoTrackingDevice> AvailableDevices => mAvailableDevices;

        public override IAutoTrackingDevice DefaultDevice
        {
            get => mDefaultDevice;
            set => mDefaultDevice = value;
        }

        public override IReadOnlyList<IProviderOption> Options => EmptyOptions;
        public override IReadOnlyList<IProviderOperation> Operations => EmptyOperations;

        public GamePlatform ActivePlatform
        {
            get => mActivePlatform;
            set
            {
                if (mActivePlatform != value)
                {
                    mActivePlatform = value;
                    _ = RefreshDevicesAsync();
                }
            }
        }

        public override Task RefreshDevicesAsync()
        {
            mAvailableDevices.Clear();

            ConnectorType connectorType;
            if (ConnectorTypeForGamePlatform(mActivePlatform, out connectorType))
            {
                var availableConnectorInstanceTypes = ConnectorFactory.Available[(int)connectorType];
                if (availableConnectorInstanceTypes != null && availableConnectorInstanceTypes.Length > 0)
                {
                    foreach (var availableType in availableConnectorInstanceTypes)
                    {
                        if (availableType.Visibility != ConnectorFactory.Visibility.Production)
                            continue;

                        mAvailableDevices.Add(new ConnectorLibDevice(availableType.Name, availableType.Type));
                    }
                }
            }

            return Task.CompletedTask;
        }

        static bool ConnectorTypeForGamePlatform(GamePlatform platform, out ConnectorType connectorType)
        {
            switch (platform)
            {
                case GamePlatform.NES:
                    connectorType = ConnectorType.NESConnector;
                    return true;
                case GamePlatform.SNES:
                    connectorType = ConnectorType.SNESConnector;
                    return true;
                case GamePlatform.N64:
                    connectorType = ConnectorType.N64Connector;
                    return true;
                case GamePlatform.Gameboy:
                    connectorType = ConnectorType.GBConnector;
                    return true;
                case GamePlatform.GBA:
                    connectorType = ConnectorType.GBAConnector;
                    return true;
                case GamePlatform.Genesis:
                    connectorType = ConnectorType.GenesisConnector;
                    return true;
            }

            connectorType = ConnectorType.ExternalConnector;
            return false;
        }
    }
}
