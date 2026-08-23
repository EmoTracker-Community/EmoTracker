using EmoTracker.MockRandomizer.Memory;

namespace EmoTracker.MockRandomizer.Scripting;

/// <summary>
/// Parses the simple CLI/REPL command grammar and applies it to the shared
/// <see cref="MemorySession"/>. Supports <c>set</c>, <c>get</c>, <c>state</c>,
/// <c>play</c>, <c>status</c>, and <c>log</c>.
/// </summary>
public static class Commands
{
    public static bool Execute(string line, MemorySession session, Action<Exception>? log)
    {
        if (string.IsNullOrWhiteSpace(line)) return true;
        var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        string cmd = parts[0].ToLowerInvariant();

        try
        {
            switch (cmd)
            {
                case "set":
                    DoSet(session, parts);
                    break;
                case "get":
                    return DoGet(session, parts);
                case "reset":
                    session.Reset();
                    Console.WriteLine("ok: reset to default image");
                    break;
                default:
                    Console.WriteLine($"error: unknown command '{cmd}'");
                    return false;
            }
        }
        catch (Exception ex)
        {
            log?.Invoke(ex);
            Console.WriteLine($"error: {ex.Message}");
            return false;
        }
        return true;
    }

    static (string region, ulong baseOffset, bool isBus) ResolveRegion(string token)
    {
        string upper = token.ToUpperInvariant();
        // Accept the FxPakPro/WRAM forms explicitly.
        switch (upper)
        {
            case "WRAM": return ("WRAM", 0, false);
            case "CARTROM": return ("CARTROM", 0, false);
            case "SRAM": return ("SRAM", 0, false);
            case "BUS": return ("System Bus", 0, true);
            default: throw new ArgumentException($"unknown region '{token}'");
        }
    }

    static void DoSet(MemorySession session, string[] parts)
    {
        // set <REGION>:<hexAddr> <byte> [<byte> ...]
        if (parts.Length < 3) throw new ArgumentException("usage: set <REGION>:<hexAddr> <hex bytes...>");
        var (region, _, isBus) = ResolveRegion(parts[1].Substring(0, parts[1].IndexOf(':')));
        ulong addr = Convert.ToUInt64(parts[1].Substring(parts[1].IndexOf(':') + 1), 16);

        var data = new byte[parts.Length - 2];
        for (int i = 0; i < data.Length; i++)
            data[i] = Convert.ToByte(parts[i + 2], 16);

        if (isBus)
            session.WriteSystemBus(addr, data);
        else
            session.WriteDomain(region, addr, data);

        Console.WriteLine($"ok: wrote {data.Length} byte(s) to {parts[1]}");
    }

    static bool DoGet(MemorySession session, string[] parts)
    {
        // get <REGION>:<hexAddr> [length]
        if (parts.Length < 2) throw new ArgumentException("usage: get <REGION>:<hexAddr> [len]");
        int colon = parts[1].IndexOf(':');
        var (region, _, isBus) = ResolveRegion(colon > 0 ? parts[1].Substring(0, colon) : "BUS");
        ulong addr = Convert.ToUInt64(colon > 0 ? parts[1].Substring(colon + 1) : parts[1], 16);
        int len = parts.Length > 2 ? int.Parse(parts[2]) : 16;

        byte[] data = isBus ? session.ReadSystemBus(addr, len) : session.ReadDomain(region, addr, len);
        Console.WriteLine(string.Join(" ", data.Select(b => b.ToString("X2"))));
        return true;
    }
}
