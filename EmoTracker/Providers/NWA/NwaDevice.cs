using EmoTracker.Data.AutoTracking;
using EmoTracker.Data.Packages;
using EmoTracker.Providers.NWA.AddressMaps;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EmoTracker.Providers.NWA
{
    public class NwaDevice : AutoTrackingDeviceBase
    {
        readonly string mHost;
        readonly int mPort;
        readonly string mDisplayName;
        readonly NwaProvider mParentProvider;
        readonly List<IProviderOperation> mOperations;
        readonly SemaphoreSlim mLock = new SemaphoreSlim(1, 1);

        TcpClient mClient;
        NetworkStream mStream;
        bool mConnected;
        string mPlatform;
        Dictionary<string, NwaMemoryRegion> mMemoryRegions;
        INwaAddressMap mAddressMap;

        public NwaDevice(string host, int port, string displayName, string platform, NwaProvider parentProvider)
        {
            mHost = host;
            mPort = port;
            mDisplayName = displayName;
            mParentProvider = parentProvider;
            mPlatform = platform;

            mOperations = new List<IProviderOperation>
            {
                new NwaProviderOperation("emulation_reset", "Reset", () => mConnected, async () =>
                {
                    await SendCommandAsync("EMULATION_RESET").ConfigureAwait(false);
                }),
                new NwaProviderOperation("emulation_pause", "Pause", () => mConnected, async () =>
                {
                    await SendCommandAsync("EMULATION_PAUSE").ConfigureAwait(false);
                }),
                new NwaProviderOperation("emulation_resume", "Resume", () => mConnected, async () =>
                {
                    await SendCommandAsync("EMULATION_RESUME").ConfigureAwait(false);
                })
            };
        }

        public override string Id => $"nwa:{mHost}:{mPort}";
        public override string DisplayName => mDisplayName;
        public override bool IsConnected => mConnected;
        public string Platform => mPlatform;

        static readonly IReadOnlyList<IProviderOption> EmptyOptions = Array.Empty<IProviderOption>();
        public override IReadOnlyList<IProviderOption> Options => EmptyOptions;
        public override IReadOnlyList<IProviderOperation> Operations => mOperations;

        public override event EventHandler<bool> ConnectionStatusChanged;

        public override async Task ConnectAsync()
        {
            if (mConnected)
                return;

            Log.Debug("[NWA] Connecting to {Host}:{Port}...", mHost, mPort);

            try
            {
                mClient = new TcpClient();
                mClient.ReceiveTimeout = 5000;
                mClient.SendTimeout = 5000;
                await mClient.ConnectAsync(mHost, mPort).ConfigureAwait(false);
                mStream = mClient.GetStream();

                // Identify ourselves
                string clientName = $"EmoTracker {Core.ApplicationVersion.Current}";
                var nameReply = await SendCommandAsync($"MY_NAME_IS {clientName}").ConfigureAwait(false);
                Log.Debug("[NWA] Identified as: {Name}", nameReply.GetValueOrDefault("name", clientName));

                // Refresh memory regions
                await RefreshMemoryRegionsAsync().ConfigureAwait(false);

                // Verify core and initialize address map for the platform
                await InitializeAddressMapAsync().ConfigureAwait(false);

                mConnected = true;
                Log.Debug("[NWA] Connected to {DisplayName} at {Host}:{Port}", mDisplayName, mHost, mPort);
                ConnectionStatusChanged?.Invoke(this, true);
            }
            catch (Exception ex)
            {
                Log.Warning("[NWA] Failed to connect to {Host}:{Port}: {Message}", mHost, mPort, ex.Message);
                CleanupConnection();
                ConnectionStatusChanged?.Invoke(this, false);
            }
        }

        const string RequiredSnesCore = "BSNESv115+";
        const int CoreSwitchPollIntervalMs = 500;
        const int CoreSwitchTimeoutMs = 15000;

        async Task InitializeAddressMapAsync()
        {
            var gamePlatform = NwaProvider.MapPlatform(mPlatform);

            if (gamePlatform == GamePlatform.SNES)
            {
                await EnsureBsnesCoreAsync().ConfigureAwait(false);

                // Re-enumerate memory regions after a potential core switch
                await RefreshMemoryRegionsAsync().ConfigureAwait(false);
            }

            // Verify that the core exposes a "System Bus" domain, which allows
            // bus addresses to be used directly without address translation.
            if (mMemoryRegions == null || !mMemoryRegions.ContainsKey("System Bus"))
            {
                var domainNames = mMemoryRegions != null
                    ? string.Join(", ", mMemoryRegions.Keys)
                    : "(none)";
                Log.Warning("[NWA] \"System Bus\" memory domain not available. Reported domains: {Domains}", domainNames);
                throw new InvalidOperationException(
                    $"Core does not expose a \"System Bus\" memory domain (available: {domainNames})");
            }

            mAddressMap = new DefaultAddressMap("System Bus");
            Log.Debug("[NWA] Using address map with domain: System Bus");
        }

        async Task EnsureBsnesCoreAsync()
        {
            var coreReply = await SendCommandAsync("CORE_CURRENT_INFO").ConfigureAwait(false);
            string coreName = coreReply.GetValueOrDefault("name", "");
            Log.Debug("[NWA] Current core: {Core}", coreName);

            if (string.Equals(coreName, RequiredSnesCore, StringComparison.OrdinalIgnoreCase))
                return;

            Log.Information("[NWA] Switching core from {Current} to {Required}...", coreName, RequiredSnesCore);

            // Save state before switching cores so we can restore it afterwards
            Log.Debug("[NWA] Saving state before core switch...");
            await SendCommandAsync("SAVE_STATE emotracker_core_switch.state").ConfigureAwait(false);

            await SendCommandAsync($"LOAD_CORE {RequiredSnesCore}").ConfigureAwait(false);
            await SendCommandAsync("CORE_RESET").ConfigureAwait(false);

            // Wait for the game to be running on the new core
            var deadline = DateTime.UtcNow.AddMilliseconds(CoreSwitchTimeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(CoreSwitchPollIntervalMs).ConfigureAwait(false);

                var statusReply = await SendCommandAsync("EMULATION_STATUS").ConfigureAwait(false);
                string state = statusReply.GetValueOrDefault("state", "");
                if (state == "running" || state == "paused")
                    break;
            }

            // Restore the save state on the new core
            Log.Debug("[NWA] Restoring state after core switch...");
            try
            {
                await SendCommandAsync("LOAD_STATE emotracker_core_switch.state").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning("[NWA] Failed to restore state after core switch: {Message}", ex.Message);
            }

            // Verify the switch succeeded
            coreReply = await SendCommandAsync("CORE_CURRENT_INFO").ConfigureAwait(false);
            coreName = coreReply.GetValueOrDefault("name", "");
            Log.Debug("[NWA] Core after switch: {Core}", coreName);

            if (!string.Equals(coreName, RequiredSnesCore, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Failed to switch to {RequiredSnesCore} core (got \"{coreName}\")");
            }

            Log.Information("[NWA] Successfully switched to {Core}", coreName);
        }

        /// <summary>
        /// Read raw bytes from a named NWA memory domain, bypassing address mapping.
        /// Used by address maps during initialization (e.g., reading ROM headers).
        /// </summary>
        async Task<byte[]> ReadRawDomainAsync(string domain, ulong offset, int length)
        {
            string command = $"CORE_READ {domain} ${offset:X};${length:X}";
            byte[] data = await SendReadCommandAsync(command).ConfigureAwait(false);
            return data ?? Array.Empty<byte>();
        }

        public override Task DisconnectAsync()
        {
            Log.Debug("[NWA] Disconnecting from {DisplayName}", mDisplayName);
            CleanupConnection();
            ConnectionStatusChanged?.Invoke(this, false);
            return Task.CompletedTask;
        }

        void CleanupConnection()
        {
            mConnected = false;
            mMemoryRegions = null;
            mAddressMap = null;

            if (mStream != null)
            {
                try { mStream.Dispose(); } catch { }
                mStream = null;
            }

            if (mClient != null)
            {
                try { mClient.Dispose(); } catch { }
                mClient = null;
            }
        }

        async Task RefreshMemoryRegionsAsync()
        {
            mMemoryRegions = new Dictionary<string, NwaMemoryRegion>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var reply = await SendCommandAsync("CORE_MEMORIES").ConfigureAwait(false);
                // CORE_MEMORIES returns repeated name/access/size groups
                var names = reply.GetValues("name");
                var accesses = reply.GetValues("access");
                var sizes = reply.GetValues("size");

                for (int i = 0; i < names.Count; i++)
                {
                    string name = names[i];
                    string access = i < accesses.Count ? accesses[i] : "r";
                    uint size = 0;
                    if (i < sizes.Count)
                        uint.TryParse(sizes[i], out size);

                    mMemoryRegions[name] = new NwaMemoryRegion(name, access, size);
                    Log.Debug("[NWA] Memory region: {Name} ({Access}, {Size} bytes)", name, access, size);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[NWA] Failed to enumerate memory regions: {Message}", ex.Message);
            }
        }

        // --- NWA Protocol I/O ---

        async Task<NwaReply> SendCommandAsync(string command)
        {
            await mLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (mStream == null || !mClient.Connected)
                    throw new InvalidOperationException("Not connected");

                byte[] commandBytes = Encoding.ASCII.GetBytes(command + "\n");
                await mStream.WriteAsync(commandBytes, 0, commandBytes.Length).ConfigureAwait(false);
                await mStream.FlushAsync().ConfigureAwait(false);

                return await ReadAsciiReplyAsync().ConfigureAwait(false);
            }
            finally
            {
                mLock.Release();
            }
        }

        async Task<byte[]> SendReadCommandAsync(string command)
        {
            await mLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (mStream == null || !mClient.Connected)
                    throw new InvalidOperationException("Not connected");

                byte[] commandBytes = Encoding.ASCII.GetBytes(command + "\n");
                await mStream.WriteAsync(commandBytes, 0, commandBytes.Length).ConfigureAwait(false);
                await mStream.FlushAsync().ConfigureAwait(false);

                // Response can be binary (starts with 0x00) or ASCII error (starts with \n)
                int firstByte = await ReadByteAsync().ConfigureAwait(false);
                if (firstByte == 0x00)
                {
                    return await ReadBinaryPayloadAsync().ConfigureAwait(false);
                }
                else if (firstByte == '\n')
                {
                    // ASCII reply — could be an error
                    var reply = await ReadAsciiReplyBodyAsync().ConfigureAwait(false);
                    if (reply.ContainsKey("error"))
                        throw new NwaProtocolException(reply.GetValueOrDefault("error", "unknown"), reply.GetValueOrDefault("reason", ""));
                    return null;
                }
                else
                {
                    throw new NwaProtocolException("protocol_error", $"Unexpected response byte: 0x{firstByte:X2}");
                }
            }
            finally
            {
                mLock.Release();
            }
        }

        async Task SendWriteCommandAsync(string command, byte[] data)
        {
            await mLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (mStream == null || !mClient.Connected)
                    throw new InvalidOperationException("Not connected");

                // Send the command
                byte[] commandBytes = Encoding.ASCII.GetBytes(command + "\n");
                await mStream.WriteAsync(commandBytes, 0, commandBytes.Length).ConfigureAwait(false);

                // Send binary payload: <0x00><4-byte big-endian size><data>
                byte[] header = new byte[5];
                header[0] = 0x00;
                header[1] = (byte)((data.Length >> 24) & 0xFF);
                header[2] = (byte)((data.Length >> 16) & 0xFF);
                header[3] = (byte)((data.Length >> 8) & 0xFF);
                header[4] = (byte)(data.Length & 0xFF);
                await mStream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
                await mStream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
                await mStream.FlushAsync().ConfigureAwait(false);

                // Read the ASCII acknowledgement
                var reply = await ReadAsciiReplyAsync().ConfigureAwait(false);
                if (reply.ContainsKey("error"))
                    throw new NwaProtocolException(reply.GetValueOrDefault("error", "unknown"), reply.GetValueOrDefault("reason", ""));
            }
            finally
            {
                mLock.Release();
            }
        }

        async Task<int> ReadByteAsync()
        {
            byte[] buf = new byte[1];
            int read = await mStream.ReadAsync(buf, 0, 1).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Connection closed");
            return buf[0];
        }

        async Task<byte[]> ReadBinaryPayloadAsync()
        {
            // Read 4-byte big-endian size
            byte[] sizeBytes = await ReadExactAsync(4).ConfigureAwait(false);
            int size = (sizeBytes[0] << 24) | (sizeBytes[1] << 16) | (sizeBytes[2] << 8) | sizeBytes[3];

            if (size <= 0)
                return Array.Empty<byte>();

            return await ReadExactAsync(size).ConfigureAwait(false);
        }

        async Task<byte[]> ReadExactAsync(int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = await mStream.ReadAsync(buffer, offset, count - offset).ConfigureAwait(false);
                if (read == 0)
                    throw new EndOfStreamException("Connection closed while reading");
                offset += read;
            }
            return buffer;
        }

        async Task<NwaReply> ReadAsciiReplyAsync()
        {
            // ASCII replies start with \n, then key:value\n pairs, ending with \n\n
            int firstByte = await ReadByteAsync().ConfigureAwait(false);
            if (firstByte != '\n')
                throw new NwaProtocolException("protocol_error", $"Expected ASCII reply (0x0A), got 0x{firstByte:X2}");

            return await ReadAsciiReplyBodyAsync().ConfigureAwait(false);
        }

        async Task<NwaReply> ReadAsciiReplyBodyAsync()
        {
            var reply = new NwaReply();
            var lineBuilder = new StringBuilder();

            while (true)
            {
                int b = await ReadByteAsync().ConfigureAwait(false);
                if (b == '\n')
                {
                    string line = lineBuilder.ToString();
                    lineBuilder.Clear();

                    if (string.IsNullOrEmpty(line))
                    {
                        // Empty line = end of reply
                        break;
                    }

                    int colonIdx = line.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        string key = line.Substring(0, colonIdx);
                        string value = line.Substring(colonIdx + 1);
                        reply.Add(key, value);
                    }
                }
                else
                {
                    lineBuilder.Append((char)b);
                }
            }

            return reply;
        }

        // --- Memory Read/Write via NWA CORE_READ / bCORE_WRITE ---

        public override async Task<(bool success, byte[] data)> ReadAsync(ulong startAddress, int length)
        {
            if (!mConnected || mStream == null || mAddressMap == null)
                return (false, null);

            try
            {
                var mapping = mAddressMap.MapAddress(startAddress);
                if (mapping == null)
                {
                    Log.Warning("[NWA] Cannot map address 0x{Address:X6} to a memory domain", startAddress);
                    return (false, null);
                }

                string domain = mapping.Value.Domain;
                ulong offset = mapping.Value.Offset;
                string command = $"CORE_READ {domain};${offset:X};${length:X}";
                byte[] data = await SendReadCommandAsync(command).ConfigureAwait(false);

                if (data != null)
                {
                    Log.Debug("[NWA] Read {Length} bytes from {Domain}+0x{Offset:X6} (bus 0x{Address:X6})", data.Length, domain, offset, startAddress);
                    return (true, data);
                }
                return (false, null);
            }
            catch (NwaProtocolException ex)
            {
                Log.Warning("[NWA] Read error at 0x{Address:X6}: [{Error}] {Reason}", startAddress, ex.ErrorType, ex.Reason);
                return (false, null);
            }
            catch (Exception ex)
            {
                Log.Warning("[NWA] Read failed at 0x{Address:X6} ({Length} bytes): {Message}", startAddress, length, ex.Message);
                HandleConnectionFailure();
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

        public override async Task<bool> WriteAsync(ulong startAddress, byte[] buffer)
        {
            if (!mConnected || mStream == null || mAddressMap == null)
                return false;

            try
            {
                var mapping = mAddressMap.MapAddress(startAddress);
                if (mapping == null)
                {
                    Log.Warning("[NWA] Cannot map address 0x{Address:X6} to a memory domain for write", startAddress);
                    return false;
                }

                string domain = mapping.Value.Domain;
                ulong offset = mapping.Value.Offset;
                string command = $"bCORE_WRITE {domain};${offset:X};${buffer.Length:X}";
                await SendWriteCommandAsync(command, buffer).ConfigureAwait(false);

                Log.Debug("[NWA] Wrote {Length} bytes to {Domain}+0x{Offset:X6} (bus 0x{Address:X6})", buffer.Length, domain, offset, startAddress);
                return true;
            }
            catch (NwaProtocolException ex)
            {
                Log.Warning("[NWA] Write error at 0x{Address:X6}: [{Error}] {Reason}", startAddress, ex.ErrorType, ex.Reason);
                return false;
            }
            catch (Exception ex)
            {
                Log.Warning("[NWA] Write failed at 0x{Address:X6} ({Length} bytes): {Message}", startAddress, buffer.Length, ex.Message);
                HandleConnectionFailure();
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

        void HandleConnectionFailure()
        {
            if (!mConnected)
                return;

            Log.Warning("[NWA] Connection to {DisplayName} lost", mDisplayName);
            mConnected = false;
            ConnectionStatusChanged?.Invoke(this, false);
        }

        public override void Dispose()
        {
            CleanupConnection();
        }

        // --- Static helpers for port scanning during discovery ---

        internal static async Task<NwaEmulatorInfo> ProbeAsync(string host, int port, int timeoutMs = 2000)
        {
            using (var client = new TcpClient())
            {
                var connectTask = client.ConnectAsync(host, port);
                if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)).ConfigureAwait(false) != connectTask)
                    return null;

                await connectTask.ConfigureAwait(false); // propagate exceptions
                client.ReceiveTimeout = timeoutMs;
                client.SendTimeout = timeoutMs;

                using (var stream = client.GetStream())
                {
                    // Send EMULATOR_INFO
                    byte[] cmd = Encoding.ASCII.GetBytes("EMULATOR_INFO\n");
                    await stream.WriteAsync(cmd, 0, cmd.Length).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);

                    var reply = await ReadAsciiReplyFromStreamAsync(stream, timeoutMs).ConfigureAwait(false);
                    if (reply == null || !reply.ContainsKey("name"))
                        return null;

                    string name = reply.GetValueOrDefault("name", "Unknown");
                    string version = reply.GetValueOrDefault("version", "");
                    string id = reply.GetValueOrDefault("id", "");
                    string nwaVersion = reply.GetValueOrDefault("nwa_version", "");

                    // Get current core info for platform
                    string platform = null;
                    try
                    {
                        byte[] coreCmd = Encoding.ASCII.GetBytes("CORE_CURRENT_INFO\n");
                        await stream.WriteAsync(coreCmd, 0, coreCmd.Length).ConfigureAwait(false);
                        await stream.FlushAsync().ConfigureAwait(false);

                        var coreReply = await ReadAsciiReplyFromStreamAsync(stream, timeoutMs).ConfigureAwait(false);
                        if (coreReply != null)
                            platform = coreReply.GetValueOrDefault("platform", null);
                    }
                    catch
                    {
                        // CORE_CURRENT_INFO may not be supported; not critical
                    }

                    // Get current game name
                    string game = null;
                    try
                    {
                        byte[] statusCmd = Encoding.ASCII.GetBytes("EMULATION_STATUS\n");
                        await stream.WriteAsync(statusCmd, 0, statusCmd.Length).ConfigureAwait(false);
                        await stream.FlushAsync().ConfigureAwait(false);

                        var statusReply = await ReadAsciiReplyFromStreamAsync(stream, timeoutMs).ConfigureAwait(false);
                        if (statusReply != null)
                        {
                            string state = statusReply.GetValueOrDefault("state", "");
                            if (state == "running" || state == "paused")
                                game = statusReply.GetValueOrDefault("game", null);
                        }
                    }
                    catch
                    {
                        // EMULATION_STATUS may not be supported; not critical
                    }

                    return new NwaEmulatorInfo
                    {
                        Host = host,
                        Port = port,
                        Name = name,
                        Version = version,
                        Id = id,
                        NwaVersion = nwaVersion,
                        Platform = platform,
                        Game = game
                    };
                }
            }
        }

        static async Task<NwaReply> ReadAsciiReplyFromStreamAsync(NetworkStream stream, int timeoutMs)
        {
            var cts = new CancellationTokenSource(timeoutMs);
            var reply = new NwaReply();
            var lineBuilder = new StringBuilder();

            // Read first byte (should be \n)
            byte[] buf = new byte[1];
            int read = await stream.ReadAsync(buf, 0, 1, cts.Token).ConfigureAwait(false);
            if (read == 0 || buf[0] != '\n')
                return null;

            while (!cts.IsCancellationRequested)
            {
                read = await stream.ReadAsync(buf, 0, 1, cts.Token).ConfigureAwait(false);
                if (read == 0)
                    break;

                if (buf[0] == '\n')
                {
                    string line = lineBuilder.ToString();
                    lineBuilder.Clear();

                    if (string.IsNullOrEmpty(line))
                        break;

                    int colonIdx = line.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        string key = line.Substring(0, colonIdx);
                        string value = line.Substring(colonIdx + 1);
                        reply.Add(key, value);
                    }
                }
                else
                {
                    lineBuilder.Append((char)buf[0]);
                }
            }

            return reply;
        }
    }

    internal class NwaEmulatorInfo
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Id { get; set; }
        public string NwaVersion { get; set; }
        public string Platform { get; set; }
        public string Game { get; set; }
    }

    internal class NwaMemoryRegion
    {
        public string Name { get; }
        public string Access { get; }
        public uint Size { get; }

        public NwaMemoryRegion(string name, string access, uint size)
        {
            Name = name;
            Access = access;
            Size = size;
        }
    }

    internal class NwaReply
    {
        readonly List<KeyValuePair<string, string>> mEntries = new List<KeyValuePair<string, string>>();

        public void Add(string key, string value)
        {
            mEntries.Add(new KeyValuePair<string, string>(key, value));
        }

        public bool ContainsKey(string key)
        {
            foreach (var entry in mEntries)
            {
                if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public string GetValueOrDefault(string key, string defaultValue)
        {
            for (int i = mEntries.Count - 1; i >= 0; i--)
            {
                if (string.Equals(mEntries[i].Key, key, StringComparison.OrdinalIgnoreCase))
                    return mEntries[i].Value;
            }
            return defaultValue;
        }

        // For array-style responses (repeated keys like CORE_MEMORIES)
        public List<string> GetValues(string key)
        {
            var values = new List<string>();
            foreach (var entry in mEntries)
            {
                if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                    values.Add(entry.Value);
            }
            return values;
        }
    }

    internal class NwaProtocolException : Exception
    {
        public string ErrorType { get; }
        public string Reason { get; }

        public NwaProtocolException(string errorType, string reason)
            : base($"NWA error [{errorType}]: {reason}")
        {
            ErrorType = errorType;
            Reason = reason;
        }
    }
}
