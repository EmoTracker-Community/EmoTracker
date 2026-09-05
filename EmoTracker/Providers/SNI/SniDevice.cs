using EmoTracker.Data.AutoTracking;
using Google.Protobuf;
using Grpc.Net.Client;
using Serilog;
using SNI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EmoTracker.Providers.SNI
{
    public class SniDevice : AutoTrackingDeviceBase
    {
        readonly string mUri;
        readonly string mDisplayName;
        readonly SniProvider mParentProvider;
        readonly List<IProviderOperation> mOperations;

        bool mConnected;
        MemoryMapping mDetectedMapping = MemoryMapping.Unknown;
        DeviceMemory.DeviceMemoryClient mMemoryClient;
        DeviceControl.DeviceControlClient mControlClient;

        // Bounded timeout for every SNI gRPC call. Without a deadline a
        // stalled server (e.g. a black-holed socket after an emulator
        // reset) blocks the sync wrappers forever, which freezes the
        // autotracker poll loop (issue #98) and, when reached from a Lua
        // watch callback on the UI dispatcher, hangs the whole app
        // (issue #105).
        static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);

        static Grpc.Core.CallOptions CreateCallOptions()
            => new Grpc.Core.CallOptions(deadline: DateTime.UtcNow.Add(ReadTimeout));

        // Serializes connection attempts so concurrent reconnect calls (the
        // extension's retry and the provider's scan timer) never race.
        static readonly System.Threading.SemaphoreSlim sConnectLock = new System.Threading.SemaphoreSlim(1, 1);

        // Distinguishes transport/connection-level failures (which mean
        // the device is no longer reachable) from per-request errors that a
        // reconnect would not fix (bad address space, mapping mismatch,
        // invalid argument). Only the former demote the connection.
        static bool IsConnectionLevelFailure(Exception ex)
        {
            if (ex is Grpc.Core.RpcException rpc)
            {
                switch (rpc.StatusCode)
                {
                    case Grpc.Core.StatusCode.Cancelled:
                    case Grpc.Core.StatusCode.Unknown:
                    case Grpc.Core.StatusCode.DeadlineExceeded:
                    case Grpc.Core.StatusCode.Internal:
                    case Grpc.Core.StatusCode.Aborted:
                    case Grpc.Core.StatusCode.ResourceExhausted:
                    case Grpc.Core.StatusCode.DataLoss:
                    case Grpc.Core.StatusCode.Unavailable:
                        return true;
                    default:
                        return false;
                }
            }

            return ex is System.Net.Http.HttpRequestException
                || ex is System.IO.IOException
                || ex is ObjectDisposedException;
        }

        public SniDevice(string uri, string displayName, SniProvider parentProvider)
        {
            mUri = uri;
            mDisplayName = displayName;
            mParentProvider = parentProvider;

            mOperations = new List<IProviderOperation>
            {
                new SniProviderOperation("reset_system", "Reset System", () => mConnected, async () =>
                {
                    if (mControlClient != null)
                        await mControlClient.ResetSystemAsync(new ResetSystemRequest { Uri = mUri }).ConfigureAwait(false);
                }),
                new SniProviderOperation("reset_to_menu", "Reset to Menu", () => mConnected, async () =>
                {
                    if (mControlClient != null)
                        await mControlClient.ResetToMenuAsync(new ResetToMenuRequest { Uri = mUri }).ConfigureAwait(false);
                })
            };
        }

        public override string Id => mUri;
        public override string DisplayName => mDisplayName;
        public override bool IsConnected => mConnected;

        static readonly IReadOnlyList<IProviderOption> EmptyOptions = Array.Empty<IProviderOption>();
        public override IReadOnlyList<IProviderOption> Options => EmptyOptions;
        public override IReadOnlyList<IProviderOperation> Operations => mOperations;

        public override event EventHandler<bool> ConnectionStatusChanged;

        public override async Task ConnectAsync()
        {
            if (mConnected)
                return;

            if (!await sConnectLock.WaitAsync(5000).ConfigureAwait(false))
            {
                Log.Warning("[SNI] Timed out waiting to connect to {DisplayName}", mDisplayName);
                return;
            }

            try
            {
                if (mConnected)
                    return;

                Log.Debug("[SNI] Connecting to device {DisplayName} ({Uri})...", mDisplayName, mUri);

                try
                {
                    GrpcChannel channel = mParentProvider.Channel;
                    if (channel == null)
                    {
                        Log.Warning("[SNI] Cannot connect — gRPC channel is null");
                        return;
                    }

                    mMemoryClient = new DeviceMemory.DeviceMemoryClient(channel);
                    mControlClient = new DeviceControl.DeviceControlClient(channel);

                    // Verify device is reachable by attempting a mapping detect
                    var detectResponse = await mMemoryClient.MappingDetectAsync(new DetectMemoryMappingRequest
                    {
                        Uri = mUri,
                        FallbackMemoryMapping = MemoryMapping.Unknown
                    }, CreateCallOptions()).ConfigureAwait(false);

                    mDetectedMapping = detectResponse.MemoryMapping;
                    Log.Debug("[SNI] Connected to {DisplayName}, detected mapping: {Mapping}", mDisplayName, mDetectedMapping);
                    mConnected = true;
                    ConnectionStatusChanged?.Invoke(this, true);
                }
                catch (Exception ex)
                {
                    Log.Warning("[SNI] Failed to connect to {DisplayName}: {Message}", mDisplayName, ex.Message);
                    mConnected = false;
                    ConnectionStatusChanged?.Invoke(this, false);
                }
            }
            finally
            {
                sConnectLock.Release();
            }
        }

        public override Task DisconnectAsync()
        {
            Log.Debug("[SNI] Disconnecting from device {DisplayName}", mDisplayName);
            mMemoryClient = null;
            mControlClient = null;
            mConnected = false;
            mDetectedMapping = MemoryMapping.Unknown;
            ConnectionStatusChanged?.Invoke(this, false);
            return Task.CompletedTask;
        }

        AddressSpace GetAddressSpace()
        {
            var option = mParentProvider.Options.FirstOrDefault(o => o.Key == "address_space");
            if (option?.Value is string val)
            {
                if (val == "SnesABus") return AddressSpace.SnesAbus;
                if (val == "Raw") return AddressSpace.Raw;
            }
            return AddressSpace.FxPakPro;
        }

        MemoryMapping GetMemoryMapping()
        {
            var option = mParentProvider.Options.FirstOrDefault(o => o.Key == "memory_mapping");
            if (option?.Value is string val)
            {
                switch (val)
                {
                    case "HiROM": return MemoryMapping.HiRom;
                    case "LoROM": return MemoryMapping.LoRom;
                    case "ExHiROM": return MemoryMapping.ExHiRom;
                    case "SA1": return MemoryMapping.Sa1;
                }
            }
            // "Auto" — use the mapping detected during connect
            return mDetectedMapping;
        }

        async Task DetectMappingAsync()
        {
            if (mMemoryClient == null)
                return;

            try
            {
                var response = await mMemoryClient.MappingDetectAsync(new DetectMemoryMappingRequest
                {
                    Uri = mUri,
                    FallbackMemoryMapping = MemoryMapping.Unknown
                }, CreateCallOptions()).ConfigureAwait(false);

                mDetectedMapping = response.MemoryMapping;
                Log.Debug("[SNI] Re-detected mapping: {Mapping}", mDetectedMapping);
            }
            catch (Exception ex)
            {
                Log.Warning("[SNI] MappingDetect failed: {Message}", ex.Message);
            }
        }

        // Async read — native for gRPC
        // All awaits use ConfigureAwait(false) to prevent deadlocks when sync
        // wrappers call .GetAwaiter().GetResult() from the UI thread.
        public override async Task<(bool success, byte[] data)> ReadAsync(ulong startAddress, int length)
        {
            if (mMemoryClient == null || !mConnected)
                return (false, null);

            try
            {
                var addressSpace = GetAddressSpace();
                var memoryMapping = GetMemoryMapping();

                try
                {
                    var response = await mMemoryClient.SingleReadAsync(new SingleReadMemoryRequest
                    {
                        Uri = mUri,
                        Request = new ReadMemoryRequest
                        {
                            RequestAddress = (uint)startAddress,
                            RequestAddressSpace = addressSpace,
                            RequestMemoryMapping = memoryMapping,
                            Size = (uint)length
                        }
                    }, CreateCallOptions()).ConfigureAwait(false);

                    byte[] data = response.Response.Data.ToByteArray();
                    Log.Debug("[SNI] Read {Length} bytes from 0x{Address:X6} (space={Space}, mapping={Mapping})", length, startAddress, addressSpace, memoryMapping);
                    return (true, data);
                }
                catch (Grpc.Core.RpcException ex) when (ex.Status.Detail != null && ex.Status.Detail.Contains("Unknown memory mapping"))
                {
                    Log.Debug("[SNI] Unknown mapping during read, running MappingDetect...");
                    await DetectMappingAsync().ConfigureAwait(false);
                    memoryMapping = GetMemoryMapping();

                    var response = await mMemoryClient.SingleReadAsync(new SingleReadMemoryRequest
                    {
                        Uri = mUri,
                        Request = new ReadMemoryRequest
                        {
                            RequestAddress = (uint)startAddress,
                            RequestAddressSpace = addressSpace,
                            RequestMemoryMapping = memoryMapping,
                            Size = (uint)length
                        }
                    }, CreateCallOptions()).ConfigureAwait(false);

                    byte[] data = response.Response.Data.ToByteArray();
                    Log.Debug("[SNI] Read {Length} bytes from 0x{Address:X6} after re-detect (mapping={Mapping})", length, startAddress, memoryMapping);
                    return (true, data);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[SNI] Read failed at 0x{Address:X6} ({Length} bytes): {Message}", startAddress, length, ex.Message);

                // A connection-level failure means the device is no longer
                // reachable. Demote IsConnected so the tracker surfaces a
                // disconnect (warning state) instead of silently returning
                // stale data while appearing active, and so the provider's
                // reconnect timer re-establishes the connection (issue #98).
                if (IsConnectionLevelFailure(ex) && mConnected)
                {
                    Log.Warning("[SNI] Read failure indicates connection loss; marking device disconnected");
                    mConnected = false;
                    ConnectionStatusChanged?.Invoke(this, false);
                }

                return (false, null);
            }
        }

        public override async Task<(bool success, byte value)> Read8Async(ulong address)
        {
            var result = await ReadAsync(address, 1).ConfigureAwait(false);
            if (result.success && result.data != null && result.data.Length >= 1)
                return (true, result.data[0]);
            return (false, 0);
        }

        public override async Task<(bool success, ushort value)> Read16Async(ulong address)
        {
            var result = await ReadAsync(address, 2).ConfigureAwait(false);
            if (result.success && result.data != null && result.data.Length >= 2)
                return (true, BitConverter.ToUInt16(result.data, 0));
            return (false, 0);
        }

        public override async Task<(bool success, uint value)> Read32Async(ulong address)
        {
            var result = await ReadAsync(address, 4).ConfigureAwait(false);
            if (result.success && result.data != null && result.data.Length >= 4)
                return (true, BitConverter.ToUInt32(result.data, 0));
            return (false, 0);
        }

        public override async Task<(bool success, ulong value)> Read64Async(ulong address)
        {
            var result = await ReadAsync(address, 8).ConfigureAwait(false);
            if (result.success && result.data != null && result.data.Length >= 8)
                return (true, BitConverter.ToUInt64(result.data, 0));
            return (false, 0);
        }

        // Async write — native for gRPC
        public override async Task<bool> WriteAsync(ulong startAddress, byte[] buffer)
        {
            if (mMemoryClient == null || !mConnected)
                return false;

            try
            {
                var addressSpace = GetAddressSpace();
                var memoryMapping = GetMemoryMapping();

                try
                {
                    await mMemoryClient.SingleWriteAsync(new SingleWriteMemoryRequest
                    {
                        Uri = mUri,
                        Request = new WriteMemoryRequest
                        {
                            RequestAddress = (uint)startAddress,
                            RequestAddressSpace = addressSpace,
                            RequestMemoryMapping = memoryMapping,
                            Data = ByteString.CopyFrom(buffer)
                        }
                    }, CreateCallOptions()).ConfigureAwait(false);
                    Log.Debug("[SNI] Wrote {Length} bytes to 0x{Address:X6} (space={Space}, mapping={Mapping})", buffer.Length, startAddress, addressSpace, memoryMapping);
                    return true;
                }
                catch (Grpc.Core.RpcException ex) when (ex.Status.Detail != null && ex.Status.Detail.Contains("Unknown memory mapping"))
                {
                    Log.Debug("[SNI] Unknown mapping during write, running MappingDetect...");
                    await DetectMappingAsync().ConfigureAwait(false);
                    memoryMapping = GetMemoryMapping();

                    await mMemoryClient.SingleWriteAsync(new SingleWriteMemoryRequest
                    {
                        Uri = mUri,
                        Request = new WriteMemoryRequest
                        {
                            RequestAddress = (uint)startAddress,
                            RequestAddressSpace = addressSpace,
                            RequestMemoryMapping = memoryMapping,
                            Data = ByteString.CopyFrom(buffer)
                        }
                    }, CreateCallOptions()).ConfigureAwait(false);
                    Log.Debug("[SNI] Wrote {Length} bytes to 0x{Address:X6} after re-detect (mapping={Mapping})", buffer.Length, startAddress, memoryMapping);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[SNI] Write failed at 0x{Address:X6} ({Length} bytes): {Message}", startAddress, buffer.Length, ex.Message);

                if (IsConnectionLevelFailure(ex) && mConnected)
                {
                    Log.Warning("[SNI] Write failure indicates connection loss; marking device disconnected");
                    mConnected = false;
                    ConnectionStatusChanged?.Invoke(this, false);
                }

                return false;
            }
        }

        public override async Task<bool> Write8Async(ulong address, byte value)
        {
            return await WriteAsync(address, new[] { value }).ConfigureAwait(false);
        }

        public override async Task<bool> Write16Async(ulong address, ushort value)
        {
            return await WriteAsync(address, BitConverter.GetBytes(value)).ConfigureAwait(false);
        }

        public override async Task<bool> Write32Async(ulong address, uint value)
        {
            return await WriteAsync(address, BitConverter.GetBytes(value)).ConfigureAwait(false);
        }

        public override async Task<bool> Write64Async(ulong address, ulong value)
        {
            return await WriteAsync(address, BitConverter.GetBytes(value)).ConfigureAwait(false);
        }

        // Sync — blocks on async (called from 30ms timer thread via Task.Run)
        public override bool Read(ulong startAddress, byte[] buffer)
        {
            var result = ReadAsync(startAddress, buffer.Length).GetAwaiter().GetResult();
            if (result.success && result.data != null)
            {
                Buffer.BlockCopy(result.data, 0, buffer, 0, Math.Min(result.data.Length, buffer.Length));
                return true;
            }
            return false;
        }

        public override bool Read8(ulong address, out byte value)
        {
            var result = Read8Async(address).GetAwaiter().GetResult();
            value = result.value;
            return result.success;
        }

        public override bool Read16(ulong address, out ushort value)
        {
            var result = Read16Async(address).GetAwaiter().GetResult();
            value = result.value;
            return result.success;
        }

        public override bool Read32(ulong address, out uint value)
        {
            var result = Read32Async(address).GetAwaiter().GetResult();
            value = result.value;
            return result.success;
        }

        public override bool Read64(ulong address, out ulong value)
        {
            var result = Read64Async(address).GetAwaiter().GetResult();
            value = result.value;
            return result.success;
        }

        public override bool Write(ulong startAddress, byte[] buffer)
        {
            return WriteAsync(startAddress, buffer).GetAwaiter().GetResult();
        }

        public override bool Write8(ulong address, byte value)
        {
            return Write8Async(address, value).GetAwaiter().GetResult();
        }

        public override bool Write16(ulong address, ushort value)
        {
            return Write16Async(address, value).GetAwaiter().GetResult();
        }

        public override bool Write32(ulong address, uint value)
        {
            return Write32Async(address, value).GetAwaiter().GetResult();
        }

        public override bool Write64(ulong address, ulong value)
        {
            return Write64Async(address, value).GetAwaiter().GetResult();
        }

        public override void Dispose()
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }
    }
}
