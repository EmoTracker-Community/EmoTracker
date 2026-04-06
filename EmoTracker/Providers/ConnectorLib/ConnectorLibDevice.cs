using ConnectorLib;
using EmoTracker.Data.AutoTracking;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EmoTracker.Providers.ConnectorLib
{
    public class ConnectorLibDevice : AutoTrackingDeviceBase
    {
        readonly string mName;
        readonly Type mInstanceType;
        IAddressableConnector mConnector;
        bool mConnected;

        static readonly IReadOnlyList<IProviderOption> EmptyOptions = Array.Empty<IProviderOption>();
        static readonly IReadOnlyList<IProviderOperation> EmptyOperations = Array.Empty<IProviderOperation>();

        public ConnectorLibDevice(string name, Type instanceType)
        {
            mName = name;
            mInstanceType = instanceType;
        }

        public override string Id => mInstanceType.FullName;
        public override string DisplayName => mName;

        public override bool IsConnected => mConnected;

        public override event EventHandler<bool> ConnectionStatusChanged;

        public override IReadOnlyList<IProviderOption> Options => EmptyOptions;
        public override IReadOnlyList<IProviderOperation> Operations => EmptyOperations;

        public override Task ConnectAsync()
        {
            if (mConnector != null)
                return Task.CompletedTask;

            try
            {
                mConnector = Activator.CreateInstance(mInstanceType) as IAddressableConnector;
                if (mConnector != null)
                {
                    IGameConnector gc = mConnector as IGameConnector;
                    if (gc != null)
                    {
                        mConnected = gc.Connected;
                        gc.ConnectionStatusChanged += OnConnectorConnectionStatusChanged;
                    }
                    else
                    {
                        mConnected = true;
                    }
                    ConnectionStatusChanged?.Invoke(this, mConnected);
                }
            }
            catch
            {
            }

            return Task.CompletedTask;
        }

        public override Task DisconnectAsync()
        {
            if (mConnector != null)
            {
                IGameConnector gc = mConnector as IGameConnector;
                if (gc != null)
                {
                    gc.ConnectionStatusChanged -= OnConnectorConnectionStatusChanged;
                    gc.Dispose();
                }
                mConnector = null;
                mConnected = false;
                ConnectionStatusChanged?.Invoke(this, false);
            }

            return Task.CompletedTask;
        }

        void OnConnectorConnectionStatusChanged(object sender, (ConnectionStatus status, string) e)
        {
            IGameConnector gc = mConnector as IGameConnector;
            if (gc != null)
            {
                mConnected = gc.Connected || e.status == ConnectionStatus.Open;
                ConnectionStatusChanged?.Invoke(this, mConnected);
            }
        }

        // Sync read — native for ConnectorLib
        public override bool Read(ulong startAddress, byte[] buffer)
        {
            if (mConnector == null || !mConnected)
                return false;
            try { return mConnector.Read(startAddress, buffer); }
            catch { return false; }
        }

        public override bool Read8(ulong address, out byte value)
        {
            value = 0;
            if (mConnector == null || !mConnected)
                return false;
            I8BitConnector c8 = mConnector as I8BitConnector;
            if (c8 == null) return false;
            try { return c8.Read8(address, out value); }
            catch { return false; }
        }

        public override bool Read16(ulong address, out ushort value)
        {
            value = 0;
            if (mConnector == null || !mConnected)
                return false;
            I16BitConnector c16 = mConnector as I16BitConnector;
            if (c16 == null) return false;
            try { return c16.Read16(address, out value); }
            catch { return false; }
        }

        public override bool Read32(ulong address, out uint value)
        {
            value = 0;
            if (mConnector == null || !mConnected)
                return false;
            // Read as two 16-bit values
            byte[] buf = new byte[4];
            if (!Read(address, buf)) return false;
            value = BitConverter.ToUInt32(buf, 0);
            return true;
        }

        public override bool Read64(ulong address, out ulong value)
        {
            value = 0;
            if (mConnector == null || !mConnected)
                return false;
            byte[] buf = new byte[8];
            if (!Read(address, buf)) return false;
            value = BitConverter.ToUInt64(buf, 0);
            return true;
        }

        // Sync write — native for ConnectorLib
        public override bool Write(ulong startAddress, byte[] buffer)
        {
            if (mConnector == null || !mConnected)
                return false;
            // IAddressableConnector doesn't have a bulk write in the base interface,
            // but I16BitConnector typically does. Fall back to byte-by-byte.
            I16BitConnector c16 = mConnector as I16BitConnector;
            if (c16 != null)
            {
                try
                {
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        c16.Write8(startAddress + (ulong)i, buffer[i]);
                    }
                    return true;
                }
                catch { return false; }
            }
            return false;
        }

        public override bool Write8(ulong address, byte value)
        {
            if (mConnector == null || !mConnected)
                return false;
            I16BitConnector c16 = mConnector as I16BitConnector;
            if (c16 == null) return false;
            try { c16.Write8(address, value); return true; }
            catch { return false; }
        }

        public override bool Write16(ulong address, ushort value)
        {
            if (mConnector == null || !mConnected)
                return false;
            I16BitConnector c16 = mConnector as I16BitConnector;
            if (c16 == null) return false;
            try { c16.Write16(address, value); return true; }
            catch { return false; }
        }

        public override bool Write32(ulong address, uint value)
        {
            if (mConnector == null || !mConnected)
                return false;
            byte[] buf = BitConverter.GetBytes(value);
            return Write(address, buf);
        }

        public override bool Write64(ulong address, ulong value)
        {
            if (mConnector == null || !mConnected)
                return false;
            byte[] buf = BitConverter.GetBytes(value);
            return Write(address, buf);
        }

        // Async — wrap sync (ConnectorLib is synchronous)
        public override Task<(bool success, byte[] data)> ReadAsync(ulong startAddress, int length)
        {
            byte[] buffer = new byte[length];
            bool success = Read(startAddress, buffer);
            return Task.FromResult((success, success ? buffer : (byte[])null));
        }

        public override Task<(bool success, byte value)> Read8Async(ulong address)
        {
            bool success = Read8(address, out byte val);
            return Task.FromResult((success, val));
        }

        public override Task<(bool success, ushort value)> Read16Async(ulong address)
        {
            bool success = Read16(address, out ushort val);
            return Task.FromResult((success, val));
        }

        public override Task<(bool success, uint value)> Read32Async(ulong address)
        {
            bool success = Read32(address, out uint val);
            return Task.FromResult((success, val));
        }

        public override Task<(bool success, ulong value)> Read64Async(ulong address)
        {
            bool success = Read64(address, out ulong val);
            return Task.FromResult((success, val));
        }

        public override Task<bool> WriteAsync(ulong startAddress, byte[] buffer)
        {
            return Task.FromResult(Write(startAddress, buffer));
        }

        public override Task<bool> Write8Async(ulong address, byte value)
        {
            return Task.FromResult(Write8(address, value));
        }

        public override Task<bool> Write16Async(ulong address, ushort value)
        {
            return Task.FromResult(Write16(address, value));
        }

        public override Task<bool> Write32Async(ulong address, uint value)
        {
            return Task.FromResult(Write32(address, value));
        }

        public override Task<bool> Write64Async(ulong address, ulong value)
        {
            return Task.FromResult(Write64(address, value));
        }

        public override void Dispose()
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }
    }
}
