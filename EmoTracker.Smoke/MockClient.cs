using System.Net.Http.Json;
using System.Text.Json;

namespace EmoTracker.Smoke;

/// <summary>
/// Client for the mock LttP randomizer's HTTP control API (port 9090),
/// used to inject/read memory scenarios cross-process.
/// </summary>
public sealed class MockClient : IDisposable
{
    readonly HttpClient mHttp;
    readonly string mBase;

    public MockClient(string baseUrl = "http://localhost:9090")
    {
        mBase = baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/";
        mHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task<bool> PingAsync()
    {
        try
        {
            var resp = await mHttp.GetAsync(mBase + "ping");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task ResetAsync()
    {
        var resp = await mHttp.PostAsync(mBase + "reset", null);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>Write hex bytes to a bus address, e.g. (0x7EF38C, "04").</summary>
    public async Task WriteAsync(ulong bus, string hexBytes)
    {
        // spacing not required; normalize by joining pairs
        string normalized = hexBytes.Replace(" ", "").Replace(",", "");
        var resp = await mHttp.PostAsJsonAsync(mBase + "write", new { bus = bus.ToString("X"), bytes = HexPairs(normalized) });
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>Select the cartridge profile ("alttp" | "smz3").</summary>
    public async Task SetProfileAsync(string profile)
    {
        var resp = await mHttp.PostAsJsonAsync(mBase + "profile", new { profile });
        resp.EnsureSuccessStatusCode();
    }

    public async Task<string> GetProfileAsync()
    {
        var resp = await mHttp.GetAsync(mBase + "profile");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("profile").GetString() ?? "";
    }

    /// <summary>Read hex bytes from a bus address; returns raw byte array.</summary>
    public async Task<byte[]> ReadBusAsync(ulong bus, int length)
    {
        var resp = await mHttp.GetAsync($"{mBase}read?bus={bus:X}&len={length}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        string bytes = doc.RootElement.GetProperty("bytes").GetString() ?? "";
        return bytes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(h => Convert.ToByte(h, 16)).ToArray();
    }

    /// <summary>In SMZ3 mode, switch the simulated current game (sm=true → Super Metroid).</summary>
    public async Task SwitchGameAsync(bool sm)
    {
        var resp = await mHttp.PostAsJsonAsync(mBase + "game", new { sm });
        resp.EnsureSuccessStatusCode();
    }

    static string HexPairs(string hex)
    {
        var parts = new List<string>();
        for (int i = 0; i + 1 < hex.Length; i += 2)
            parts.Add(hex.Substring(i, 2));
        return string.Join(" ", parts);
    }

    public void Dispose() => mHttp?.Dispose();
}
