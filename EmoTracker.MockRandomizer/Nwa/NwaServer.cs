using System.Net;
using System.Net.Sockets;
using System.Text;

namespace EmoTracker.MockRandomizer.Nwa;

/// <summary>
/// Emulates the BizHawk emu-nwaccess (NWA) server: a line-based TCP protocol.
/// Implements the subset EmoTracker's <c>NwaDevice</c> uses — discovery
/// (EMULATOR_INFO), identity (MY_NAME_IS), core info (CORE_CURRENT_INFO),
/// memory regions (CORE_MEMORIES), reads (CORE_READ), writes (bCORE_WRITE),
/// and emulation state (EMULATION_STATUS).
/// </summary>
public sealed class NwaServer
{
    readonly Memory.MemorySession mSession;
    readonly int mPort;
    TcpListener? _listener;
    CancellationTokenSource? mCts;

    bool mEnableSystemBus = true;

    public NwaServer(Memory.MemorySession session, int port, bool enableSystemBus = true)
    {
        mSession = session;
        mPort = port;
        mEnableSystemBus = enableSystemBus;
    }

    public void Start()
    {
        mCts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, mPort);
        _listener.Start();
        _ = Task.Run(async () =>
        {
            while (!mCts.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(mCts.Token);
                    _ = HandleClient(client);
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        });
    }

    public void Stop()
    {
        mCts?.Cancel();
        _listener?.Stop();
    }

    async Task HandleClient(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            while (true)
            {
                string? line = await ReadLineAsync(stream);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(new[] { ' ' }, 2);
                string cmd = parts[0].Trim();
                string arg = parts.Length > 1 ? parts[1].Trim() : "";

                switch (cmd.ToUpperInvariant())
                {
                    case "EMULATOR_INFO":
                        await WriteReplyAsync(stream, new Dictionary<string, string>
                        {
                            ["name"] = "EmuNwaccess",
                            ["version"] = "1.0.0",
                            ["id"] = "bizhawk-mock",
                            ["nwa_version"] = "0.3.0"
                        });
                        break;

                    case "MY_NAME_IS":
                        await WriteReplyAsync(stream, new Dictionary<string, string> { ["name"] = arg });
                        break;

                    case "CORE_CURRENT_INFO":
                        await WriteReplyAsync(stream, new Dictionary<string, string>
                        {
                            ["name"] = "Snes9x",
                            ["game"] = "mock-alttp.sfc",
                            ["platform"] = "snes"
                        });
                        break;

                    case "EMULATION_STATUS":
                        await WriteReplyAsync(stream, new Dictionary<string, string>
                        {
                            ["state"] = "running",
                            ["game"] = "mock-alttp.sfc"
                        });
                        break;

                    case "CORE_MEMORIES":
                        await WriteMemoriesAsync(stream);
                        break;

                    case "CORE_READ":
                        await HandleRead(stream, arg);
                        break;

                    case "BCORE_WRITE": // matches bCORE_WRITE after ToUpperInvariant
                        await HandleWrite(stream, arg);
                        break;

                    default:
                        await WriteReplyAsync(stream, new Dictionary<string, string> { ["error"] = "unknown_command" });
                        break;
                }
            }
        }
    }

    async Task<int> ReadByteAsync(NetworkStream stream)
    {
        byte[] buf = new byte[1];
        int n = await stream.ReadAsync(buf, 0, 1);
        return n == 0 ? -1 : buf[0];
    }

    async Task<string?> ReadLineAsync(NetworkStream stream)
    {
        int prev = -1;
        using var ms = new MemoryStream();
        while (true)
        {
            int b = await ReadByteAsync(stream);
            if (b < 0)
            {
                // EOF: if we've accumulated command bytes it's a trailing
                // unterminated line; otherwise the connection closed.
                if (ms.Length == 0)
                    return null;
                return Encoding.ASCII.GetString(ms.ToArray());
            }
            if (b == '\n')
            {
                if (prev == '\r')
                    ms.SetLength(ms.Length - 1);
                return Encoding.ASCII.GetString(ms.ToArray());
            }
            ms.WriteByte((byte)b);
            prev = b;
        }
    }

    async Task WriteMemoriesAsync(NetworkStream stream)
    {
        var names = new List<string> { "WRAM", "CARTROM", "SRAM", "APURAM", "VRAM" };
        if (mEnableSystemBus) names.Insert(0, "System Bus");

        var sb = new StringBuilder("\n");
        foreach (var name in names)
        {
            sb.Append($"name:{name}\n");
            sb.Append("access:r\n");
            sb.Append($"size:{SizeFor(name)}\n");
        }
        sb.Append('\n');
        await WriteRawAsync(stream, Encoding.ASCII.GetBytes(sb.ToString()));
    }

    ulong SizeFor(string name) => name switch
    {
        "System Bus" => 0x800000,
        "WRAM" => 0x20000,
        "CARTROM" => Memory.SnesBus.CartRomSize,
        "SRAM" => Memory.SnesBus.SramSize,
        "APURAM" => 0x10000,
        "VRAM" => 0x10000,
        _ => 0
    };

    async Task HandleRead(NetworkStream stream, string arg)
    {
        var (ok, domain, offset, length) = ParseReadArg(arg);
        if (!ok)
        {
            await WriteReplyAsync(stream, new Dictionary<string, string> { ["error"] = "invalid_arg", ["reason"] = arg });
            return;
        }

        byte[] data;
        if (domain.Equals("System Bus", StringComparison.OrdinalIgnoreCase))
            data = mSession.ReadSystemBus(offset, (int)length);
        else if (domain.Equals("BUS", StringComparison.OrdinalIgnoreCase))
            data = mSession.ReadSystemBus(offset, (int)length);
        else
            data = mSession.ReadDomain(domain, offset, (int)length);

        await WriteBinaryAsync(stream, data);
    }

    async Task HandleWrite(NetworkStream stream, string arg)
    {
        var (ok, domain, offset, length) = ParseReadArg(arg);
        if (!ok)
        {
            await WriteReplyAsync(stream, new Dictionary<string, string> { ["error"] = "invalid_arg", ["reason"] = arg });
            return;
        }

        // Read the binary payload: 0x00 + 4-byte BE size + data.
        byte[] marker = new byte[5];
        if (!await ReadExactAsync(stream, marker, 5))
        {
            await WriteReplyAsync(stream, new Dictionary<string, string> { ["error"] = "read_payload_failed" });
            return;
        }
        if (marker[0] != 0x00)
        {
            await WriteReplyAsync(stream, new Dictionary<string, string> { ["error"] = "expected_binary_marker" });
            return;
        }
        int size = (marker[1] << 24) | (marker[2] << 16) | (marker[3] << 8) | marker[4];
        byte[] data = new byte[size];
        if (size > 0 && !await ReadExactAsync(stream, data, size))
        {
            await WriteReplyAsync(stream, new Dictionary<string, string> { ["error"] = "read_payload_data_failed" });
            return;
        }

        bool wrote;
        if (domain.Equals("System Bus", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("BUS", StringComparison.OrdinalIgnoreCase))
        {
            mSession.WriteSystemBus(offset, data);
            wrote = true;
        }
        else
        {
            wrote = mSession.WriteDomain(domain, offset, data);
        }

        await WriteReplyAsync(stream, wrote
            ? new Dictionary<string, string> { ["name"] = "ok", ["size"] = data.Length.ToString() }
            : new Dictionary<string, string> { ["error"] = "write_failed", ["reason"] = domain });
    }

    static (bool ok, string domain, ulong offset, ulong length) ParseReadArg(string arg)
    {
        var parts = arg.Split(';');
        if (parts.Length != 3) return (false, "", 0, 0);
        if (!ulong.TryParse(parts[1].TrimStart('$'), System.Globalization.NumberStyles.HexNumber, null, out ulong off))
            return (false, "", 0, 0);
        if (!ulong.TryParse(parts[2].TrimStart('$'), System.Globalization.NumberStyles.HexNumber, null, out ulong len))
            return (false, "", 0, 0);
        return (true, parts[0].Trim(), off, len);
    }

    static async Task WriteReplyAsync(NetworkStream stream, Dictionary<string, string> kv)
    {
        // NWA ASCII replies begin with a leading '\n' (0x0A), then key:value
        // lines, then a blank line. NwaDevice.ReadAsciiReplyAsync reads the
        // first byte and expects 0x0A.
        var sb = new StringBuilder("\n");
        foreach (var entry in kv)
            sb.Append($"{entry.Key}:{entry.Value}\n");
        sb.Append('\n');
        await WriteRawAsync(stream, Encoding.ASCII.GetBytes(sb.ToString()));
    }

    static async Task WriteBinaryAsync(NetworkStream stream, byte[] data)
    {
        var header = new byte[5];
        header[0] = 0x00;
        header[1] = (byte)((data.Length >> 24) & 0xFF);
        header[2] = (byte)((data.Length >> 16) & 0xFF);
        header[3] = (byte)((data.Length >> 8) & 0xFF);
        header[4] = (byte)(data.Length & 0xFF);
        await stream.WriteAsync(header, 0, 5);
        await stream.WriteAsync(data, 0, data.Length);
        await stream.FlushAsync();
    }

    static async Task WriteRawAsync(NetworkStream stream, byte[] data)
    {
        await stream.WriteAsync(data, 0, data.Length);
        await stream.FlushAsync();
    }

    static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int count)
    {
        int got = 0;
        while (got < count)
        {
            int r = await stream.ReadAsync(buffer, got, count - got);
            if (r == 0) return false;
            got += r;
        }
        return true;
    }
}
