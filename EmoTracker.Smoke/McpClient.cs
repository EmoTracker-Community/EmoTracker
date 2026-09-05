using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EmoTracker.Smoke;

/// <summary>
/// Minimal MCP (Model Context Protocol) HTTP client for the EmoTracker dev
/// server. Speaks JSON-RPC 2.0 over Streamable HTTP (SSE), establishes a
/// session, and issues tools/call requests. Enough for the smoke runner to
/// drive and inspect the tracker headlessly.
/// </summary>
    public sealed class McpClient : IAsyncDisposable
    {
        HttpClient mHttp;
        readonly string mBase;
        string mSessionId;
        readonly string mClientName;

        McpClient(HttpClient http, string baseUrl, string sessionId, string clientName)
        {
            mHttp = http;
            mBase = baseUrl;
            mSessionId = sessionId;
            mClientName = clientName;
        }

        public string SessionId => mSessionId;

        public static async Task<McpClient> ConnectAsync(string baseUrl, string clientName = "smoke", int timeoutSeconds = 10)
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            if (!baseUrl.EndsWith("/")) baseUrl += "/";

            // initialize
            var init = await PostAsync(http, baseUrl, null, """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"%NAME%","version":"1.0.0"}}}
                """.Replace("%NAME%", clientName));

            // The session id came back in a response header on initialize.
            string sid = init.Headers.TryGetValues("mcp-session-id", out var vals)
                ? vals.FirstOrDefault() ?? ""
                : "";

            // notifications/initialized
            await PostAsync(http, baseUrl, sid, """{"jsonrpc":"2.0","method":"notifications/initialized"}""").ConfigureAwait(false);

            return new McpClient(http, baseUrl, sid, clientName);
        }

        /// <summary>Issues tools/call and returns the text of the first content result as a JSON string.</summary>
        public async Task<string> CallAsync(string tool, string argsJson)
        {
            try
            {
                return await CallOnceAsync(tool, argsJson).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // The server may drop the keep-alive connection when the pack
                // reloads (e.g. load_pack / reload_pack re-parses the layout).
                // Re-establish the session and retry once.
                await ReconnectAsync().ConfigureAwait(false);
                return await CallOnceAsync(tool, argsJson).ConfigureAwait(false);
            }
        }

        async Task<string> CallOnceAsync(string tool, string argsJson)
        {
            string body = "{\"jsonrpc\":\"2.0\",\"id\":99,\"method\":\"tools/call\",\"params\":{\"name\":\""
                + tool + "\",\"arguments\":" + argsJson + "}}";
            var resp = await PostAsync(mHttp, mBase, mSessionId, body).ConfigureAwait(false);
            string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var json = ParseSseData(text);
            var result = ParseResult(json);
            return result.ToString() ?? "";
        }

        async Task ReconnectAsync()
        {
            await DisposeAsync().ConfigureAwait(false);
            var fresh = await ConnectAsync(mBase, mClientName).ConfigureAwait(false);
            mHttp = fresh.mHttp;
            mSessionId = fresh.mSessionId;
        }

    /// <summary>Polls until <paramref name="predicate"/> over the tool result is true, or timeout.</summary>
    public async Task<bool> WaitUntilAsync(Func<Task<(bool ok, string jsonRaw)>> check, int timeoutMs, int intervalMs = 150)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var (ok, raw) = await check().ConfigureAwait(false);
            if (ok) return true;
            await Task.Delay(intervalMs).ConfigureAwait(false);
        }
        return false;
    }

    static async Task<HttpResponseMessage> PostAsync(HttpClient http, string baseUrl, string? sid, string body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (!string.IsNullOrEmpty(sid))
            req.Headers.Add("mcp-session-id", sid);

        var resp = await http.SendAsync(req).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return resp;
    }

    static JsonElement ParseSseData(string raw)
    {
        // Streamable HTTP may return `event: message` + `data: <json>` lines, or
        // a plain JSON body. Extract the first `data:` line.
        foreach (var line in raw.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("data:", StringComparison.Ordinal))
            {
                var json = t.Substring(5).Trim();
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }
        }
        // Fallback: plain JSON body.
        using var doc2 = JsonDocument.Parse(raw);
        return doc2.RootElement.Clone();
    }

    static JsonElement ParseResult(JsonElement rpc)
    {
        if (rpc.TryGetProperty("result", out var result)
            && result.TryGetProperty("content", out var content))
        {
            foreach (var item in content.EnumerateArray())
            {
            if (item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                string? txt = text.GetString();
                using var doc = JsonDocument.Parse(txt ?? "null");
                return doc.RootElement.Clone();
            }
            }
        }
        return default;
    }

    public void Dispose() => mHttp?.Dispose();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
