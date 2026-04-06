using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EmoTracker.Data.Packages;

namespace EmoTracker.Data.AutoTracking
{
    public abstract class AutoTrackingProviderBase : IAutoTrackingProvider
    {
        public abstract string UID { get; }
        public abstract string DisplayName { get; }
        public abstract IReadOnlyList<GamePlatform> SupportedPlatforms { get; }

        public abstract Task RefreshDevicesAsync();
        public abstract IReadOnlyList<IAutoTrackingDevice> AvailableDevices { get; }

        public abstract IAutoTrackingDevice DefaultDevice { get; set; }

        public abstract IReadOnlyList<IProviderOption> Options { get; }
        public abstract IReadOnlyList<IProviderOperation> Operations { get; }

        // Connection lifecycle — delegates to DefaultDevice
        public virtual Task ConnectAsync()
        {
            return DefaultDevice?.ConnectAsync() ?? Task.CompletedTask;
        }

        public virtual Task DisconnectAsync()
        {
            return DefaultDevice?.DisconnectAsync() ?? Task.CompletedTask;
        }

        public virtual bool IsConnected => DefaultDevice?.IsConnected ?? false;

        public virtual event EventHandler<bool> ConnectionStatusChanged
        {
            add { if (DefaultDevice != null) DefaultDevice.ConnectionStatusChanged += value; }
            remove { if (DefaultDevice != null) DefaultDevice.ConnectionStatusChanged -= value; }
        }

        // Sync read — delegates to DefaultDevice
        public virtual bool Read(ulong startAddress, byte[] buffer)
        {
            return DefaultDevice?.Read(startAddress, buffer) ?? false;
        }

        public virtual bool Read8(ulong address, out byte value)
        {
            if (DefaultDevice != null)
                return DefaultDevice.Read8(address, out value);
            value = 0;
            return false;
        }

        public virtual bool Read16(ulong address, out ushort value)
        {
            if (DefaultDevice != null)
                return DefaultDevice.Read16(address, out value);
            value = 0;
            return false;
        }

        public virtual bool Read32(ulong address, out uint value)
        {
            if (DefaultDevice != null)
                return DefaultDevice.Read32(address, out value);
            value = 0;
            return false;
        }

        public virtual bool Read64(ulong address, out ulong value)
        {
            if (DefaultDevice != null)
                return DefaultDevice.Read64(address, out value);
            value = 0;
            return false;
        }

        // Sync write — delegates to DefaultDevice
        public virtual bool Write(ulong startAddress, byte[] buffer)
        {
            return DefaultDevice?.Write(startAddress, buffer) ?? false;
        }

        public virtual bool Write8(ulong address, byte value)
        {
            return DefaultDevice?.Write8(address, value) ?? false;
        }

        public virtual bool Write16(ulong address, ushort value)
        {
            return DefaultDevice?.Write16(address, value) ?? false;
        }

        public virtual bool Write32(ulong address, uint value)
        {
            return DefaultDevice?.Write32(address, value) ?? false;
        }

        public virtual bool Write64(ulong address, ulong value)
        {
            return DefaultDevice?.Write64(address, value) ?? false;
        }

        // Async read — delegates to DefaultDevice
        public virtual Task<(bool success, byte[] data)> ReadAsync(ulong startAddress, int length)
        {
            return DefaultDevice?.ReadAsync(startAddress, length) ?? Task.FromResult<(bool, byte[])>((false, null));
        }

        public virtual Task<(bool success, byte value)> Read8Async(ulong address)
        {
            return DefaultDevice?.Read8Async(address) ?? Task.FromResult<(bool, byte)>((false, 0));
        }

        public virtual Task<(bool success, ushort value)> Read16Async(ulong address)
        {
            return DefaultDevice?.Read16Async(address) ?? Task.FromResult<(bool, ushort)>((false, 0));
        }

        public virtual Task<(bool success, uint value)> Read32Async(ulong address)
        {
            return DefaultDevice?.Read32Async(address) ?? Task.FromResult<(bool, uint)>((false, 0));
        }

        public virtual Task<(bool success, ulong value)> Read64Async(ulong address)
        {
            return DefaultDevice?.Read64Async(address) ?? Task.FromResult<(bool, ulong)>((false, 0));
        }

        // Async write — delegates to DefaultDevice
        public virtual Task<bool> WriteAsync(ulong startAddress, byte[] buffer)
        {
            return DefaultDevice?.WriteAsync(startAddress, buffer) ?? Task.FromResult(false);
        }

        public virtual Task<bool> Write8Async(ulong address, byte value)
        {
            return DefaultDevice?.Write8Async(address, value) ?? Task.FromResult(false);
        }

        public virtual Task<bool> Write16Async(ulong address, ushort value)
        {
            return DefaultDevice?.Write16Async(address, value) ?? Task.FromResult(false);
        }

        public virtual Task<bool> Write32Async(ulong address, uint value)
        {
            return DefaultDevice?.Write32Async(address, value) ?? Task.FromResult(false);
        }

        public virtual Task<bool> Write64Async(ulong address, ulong value)
        {
            return DefaultDevice?.Write64Async(address, value) ?? Task.FromResult(false);
        }

        public virtual void Dispose()
        {
            foreach (var device in AvailableDevices)
            {
                device.Dispose();
            }
        }
    }
}
