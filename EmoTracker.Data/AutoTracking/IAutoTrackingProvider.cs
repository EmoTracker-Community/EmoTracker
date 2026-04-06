using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EmoTracker.Data.Packages;

namespace EmoTracker.Data.AutoTracking
{
    public interface IAutoTrackingProvider : IDisposable
    {
        string UID { get; }
        string DisplayName { get; }
        IReadOnlyList<GamePlatform> SupportedPlatforms { get; }

        // Device management
        Task RefreshDevicesAsync();
        IReadOnlyList<IAutoTrackingDevice> AvailableDevices { get; }

        // Default device — all provider-level read/write/connect calls delegate to this device
        IAutoTrackingDevice DefaultDevice { get; set; }

        // Provider-level connection lifecycle (delegates to DefaultDevice)
        Task ConnectAsync();
        Task DisconnectAsync();
        bool IsConnected { get; }
        event EventHandler<bool> ConnectionStatusChanged;

        // Provider-level synchronous memory read (delegates to DefaultDevice)
        bool Read(ulong startAddress, byte[] buffer);
        bool Read8(ulong address, out byte value);
        bool Read16(ulong address, out ushort value);
        bool Read32(ulong address, out uint value);
        bool Read64(ulong address, out ulong value);

        // Provider-level synchronous memory write (delegates to DefaultDevice)
        bool Write(ulong startAddress, byte[] buffer);
        bool Write8(ulong address, byte value);
        bool Write16(ulong address, ushort value);
        bool Write32(ulong address, uint value);
        bool Write64(ulong address, ulong value);

        // Provider-level async memory read (delegates to DefaultDevice)
        Task<(bool success, byte[] data)> ReadAsync(ulong startAddress, int length);
        Task<(bool success, byte value)> Read8Async(ulong address);
        Task<(bool success, ushort value)> Read16Async(ulong address);
        Task<(bool success, uint value)> Read32Async(ulong address);
        Task<(bool success, ulong value)> Read64Async(ulong address);

        // Provider-level async memory write (delegates to DefaultDevice)
        Task<bool> WriteAsync(ulong startAddress, byte[] buffer);
        Task<bool> Write8Async(ulong address, byte value);
        Task<bool> Write16Async(ulong address, ushort value);
        Task<bool> Write32Async(ulong address, uint value);
        Task<bool> Write64Async(ulong address, ulong value);

        // Provider-specific options and operations
        IReadOnlyList<IProviderOption> Options { get; }
        IReadOnlyList<IProviderOperation> Operations { get; }
    }
}
