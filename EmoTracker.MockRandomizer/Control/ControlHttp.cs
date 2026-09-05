using System.Text.Json;
using EmoTracker.MockRandomizer.Memory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace EmoTracker.MockRandomizer.Control;

/// <summary>
/// A tiny HTTP control API on the mock so the smoke runner (a separate process)
/// can inject and read memory cross-process without knowing the wire protocol of
/// any specific backend. Served in-process by Kestrel alongside the SNI gRPC host.
///
/// Endpoints (JSON):
///   GET  /ping                                        -> {"ok":true}
///   GET  /read?bus=<hex>&len=<n>                      -> {"bytes":"AA BB CC"}
///   POST /write   {"bus":"7EF38C","bytes":"04"}        -> {"ok":true,"written":1}
///   POST /reset                                        -> {"ok":true}
///   GET  /status                                       -> {"devices":...}
///   GET  /profile                                      -> {"profile":"alttp"|"smz3"}
///   POST /profile   {"profile":"smz3"}                 -> {"ok":true}
///   GET  /game                                         -> {"game":"lttp"|"sm"}
///   POST /game      {"sm":true}                        -> {"ok":true}
/// </summary>
public static class ControlHttp
{
    public static void Map(WebApplication app, MemorySession session)
    {
        app.MapGet("/ping", () => Results.Json(new { ok = true }));

        app.MapGet("/read", (string? bus, int? len) =>
        {
            ulong addr = Convert.ToUInt64(bus ?? "0", 16);
            int n = len ?? 16;
            var data = session.ReadBus(addr, n);
            return Results.Json(new { bytes = string.Join(" ", data.Select(b => b.ToString("X2"))) });
        });

        app.MapPost("/write", (WriteRequest req) =>
        {
            if (req?.bytes == null) return Results.BadRequest(new { ok = false, error = "bytes required" });
            var parts = req.bytes.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var data = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                data[i] = Convert.ToByte(parts[i], 16);
            session.WriteBus(Convert.ToUInt64(req.bus ?? "0", 16), data);
            return Results.Json(new { ok = true, written = data.Length });
        });

        app.MapPost("/reset", () =>
        {
            session.Reset();
            return Results.Json(new { ok = true });
        });

        app.MapGet("/status", () => Results.Json(new
        {
            ok = true,
            profile = ProfileName(session.ActiveProfile),
            wram = session.Wram.Length,
            cartrom = session.CartRom.Length,
            sram = session.Sram.Length
        }));

        app.MapGet("/profile", () => Results.Json(new { profile = ProfileName(session.ActiveProfile) }));

        app.MapPost("/profile", (ProfileRequest req) =>
        {
            session.SetProfile(ParseProfile(req?.profile));
            return Results.Json(new { ok = true, profile = ProfileName(session.ActiveProfile) });
        });

        app.MapGet("/game", () => Results.Json(new
        {
            game = session.ActiveProfile == Memory.CartridgeProfile.Smz3ExHiRom
                ? (session.IsSmz3GameSm() ? "sm" : "lttp")
                : "n/a"
        }));

        app.MapPost("/game", (GameRequest req) =>
        {
            session.SwitchSmz3Game(req?.sm ?? false);
            return Results.Json(new { ok = true });
        });
    }

    static string ProfileName(Memory.CartridgeProfile profile)
        => profile == Memory.CartridgeProfile.Smz3ExHiRom ? "smz3" : "alttp";

    static Memory.CartridgeProfile ParseProfile(string? profile)
        => string.Equals(profile, "smz3", StringComparison.OrdinalIgnoreCase)
            ? Memory.CartridgeProfile.Smz3ExHiRom
            : Memory.CartridgeProfile.AlttpLoRom;

    public class WriteRequest { public string? bus { get; set; } public string? bytes { get; set; } }
    public class ProfileRequest { public string? profile { get; set; } }
    public class GameRequest { public bool? sm { get; set; } }
}
