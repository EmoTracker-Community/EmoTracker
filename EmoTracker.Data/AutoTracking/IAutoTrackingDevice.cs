using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EmoTracker.Data.AutoTracking
{
    public interface IAutoTrackingDevice : IDisposable
    {
        string Id { get; }
        string DisplayName { get; }

        // Connection lifecycle
        Task ConnectAsync();
        Task DisconnectAsync();
        bool IsConnected { get; }
        event EventHandler<bool> ConnectionStatusChanged;

        // Synchronous memory read
        bool Read(ulong startAddress, byte[] buffer);
        bool Read8(ulong address, out byte value);
        bool Read16(ulong address, out ushort value);
        bool Read32(ulong address, out uint value);
        bool Read64(ulong address, out ulong value);

        // Synchronous memory write
        bool Write(ulong startAddress, byte[] buffer);
        bool Write8(ulong address, byte value);
        bool Write16(ulong address, ushort value);
        bool Write32(ulong address, uint value);
        bool Write64(ulong address, ulong value);

        // Async memory read
        Task<(bool success, byte[] data)> ReadAsync(ulong startAddress, int length);
        Task<(bool success, byte value)> Read8Async(ulong address);
        Task<(bool success, ushort value)> Read16Async(ulong address);
        Task<(bool success, uint value)> Read32Async(ulong address);
        Task<(bool success, ulong value)> Read64Async(ulong address);

        // Async memory write
        Task<bool> WriteAsync(ulong startAddress, byte[] buffer);
        Task<bool> Write8Async(ulong address, byte value);
        Task<bool> Write16Async(ulong address, ushort value);
        Task<bool> Write32Async(ulong address, uint value);
        Task<bool> Write64Async(ulong address, ulong value);

        // Per-device options and operations
        IReadOnlyList<IProviderOption> Options { get; }
        IReadOnlyList<IProviderOperation> Operations { get; }
    }
}
