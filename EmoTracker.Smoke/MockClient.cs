using System.Net.Http.Json;

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

    static string HexPairs(string hex)
    {
        var parts = new List<string>();
        for (int i = 0; i + 1 < hex.Length; i += 2)
            parts.Add(hex.Substring(i, 2));
        return string.Join(" ", parts);
    }

    public void Dispose() => mHttp?.Dispose();
}
