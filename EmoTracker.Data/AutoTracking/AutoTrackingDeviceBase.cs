using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EmoTracker.Data.AutoTracking
{
    public abstract class AutoTrackingDeviceBase : IAutoTrackingDevice
    {
        public abstract string Id { get; }
        public abstract string DisplayName { get; }

        public abstract Task ConnectAsync();
        public abstract Task DisconnectAsync();
        public abstract bool IsConnected { get; }
        public abstract event EventHandler<bool> ConnectionStatusChanged;

        public abstract IReadOnlyList<IProviderOption> Options { get; }
        public abstract IReadOnlyList<IProviderOperation> Operations { get; }

        // Sync read — override in providers where sync is native (e.g., ConnectorLib)
        public virtual bool Read(ulong startAddress, byte[] buffer)
        {
            var result = ReadAsync(startAddress, buffer.Length).GetAwaiter().GetResult();
            if (result.success && result.data != null)
            {
                Buffer.BlockCopy(result.data, 0, buffer, 0, Math.Min(result.data.Length, buffer.Length));
                return true;
            }
            return false;
        }

        public virtual bool Read8(ulong address, out byte value)
        {
            var result = Read8Async(address).GetAwaiter().GetResult();
            value = result.value;
            return result.success;
        }

        public virtual bool Read16(ulong address, out ushort value)
        {
            var result = Read16Async(address).GetAwaiter().GetResult();
            value = result.value;
            return result.success;
        }

        public virtual bool Read32(ulong address, out uint value)
        {
            var result = Read32Async(address).GetAwaiter().GetResult();
            value = result.value;
            return result.success;
        }

        public virtual bool Read64(ulong address, out ulong value)
        {
            var result = Read64Async(address).GetAwaiter().GetResult();
            value = result.value;
            return result.success;
        }

        // Sync write — override in providers where sync is native
        public virtual bool Write(ulong startAddress, byte[] buffer)
        {
            return WriteAsync(startAddress, buffer).GetAwaiter().GetResult();
        }

        public virtual bool Write8(ulong address, byte value)
        {
            return Write8Async(address, value).GetAwaiter().GetResult();
        }

        public virtual bool Write16(ulong address, ushort value)
        {
            return Write16Async(address, value).GetAwaiter().GetResult();
        }

        public virtual bool Write32(ulong address, uint value)
        {
            return Write32Async(address, value).GetAwaiter().GetResult();
        }

        public virtual bool Write64(ulong address, ulong value)
        {
            return Write64Async(address, value).GetAwaiter().GetResult();
        }

        // Async read — override in providers where async is native (e.g., SNI gRPC)
        public virtual Task<(bool success, byte[] data)> ReadAsync(ulong startAddress, int length)
        {
            byte[] buffer = new byte[length];
            bool success = Read(startAddress, buffer);
            return Task.FromResult((success, success ? buffer : (byte[])null));
        }

        public virtual Task<(bool success, byte value)> Read8Async(ulong address)
        {
            bool success = Read8(address, out byte value);
            return Task.FromResult((success, value));
        }

        public virtual Task<(bool success, ushort value)> Read16Async(ulong address)
        {
            bool success = Read16(address, out ushort value);
            return Task.FromResult((success, value));
        }

        public virtual Task<(bool success, uint value)> Read32Async(ulong address)
        {
            bool success = Read32(address, out uint value);
            return Task.FromResult((success, value));
        }

        public virtual Task<(bool success, ulong value)> Read64Async(ulong address)
        {
            bool success = Read64(address, out ulong value);
            return Task.FromResult((success, value));
        }

        // Async write — override in providers where async is native
        public virtual Task<bool> WriteAsync(ulong startAddress, byte[] buffer)
        {
            return Task.FromResult(Write(startAddress, buffer));
        }

        public virtual Task<bool> Write8Async(ulong address, byte value)
        {
            return Task.FromResult(Write8(address, value));
        }

        public virtual Task<bool> Write16Async(ulong address, ushort value)
        {
            return Task.FromResult(Write16(address, value));
        }

        public virtual Task<bool> Write32Async(ulong address, uint value)
        {
            return Task.FromResult(Write32(address, value));
        }

        public virtual Task<bool> Write64Async(ulong address, ulong value)
        {
            return Task.FromResult(Write64(address, value));
        }

        public virtual void Dispose()
        {
            if (IsConnected)
                DisconnectAsync().GetAwaiter().GetResult();
        }
    }
}
