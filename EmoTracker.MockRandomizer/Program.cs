using EmoTracker.MockRandomizer.Control;
using EmoTracker.MockRandomizer.Memory;
using EmoTracker.MockRandomizer.Nwa;
using EmoTracker.MockRandomizer.Scripting;
using EmoTracker.MockRandomizer.Sni;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var (sniEnabled, nwaEnabled, sniPort, nwaPort, kindStr, systemBus, runForever) = ParseArgs(args);

SniCore.BackendKind kind = kindStr.Equals("emulator", StringComparison.OrdinalIgnoreCase)
    ? SniCore.BackendKind.Emulator
    : SniCore.BackendKind.FxPakPro;

Console.WriteLine("=== EmoTracker MockLttP Randomizer ===");
Console.WriteLine($"  SNI gRPC : {(sniEnabled ? "on" : "off")}, port {sniPort}, flavor {kind}");
Console.WriteLine($"  NWA TCP  : {(nwaEnabled ? "on" : "off")}, port {nwaPort}, system-bus {(systemBus ? "on" : "off")}");

var session = new MemorySession();
var core = new SniCore(session, kind);

// ---------- SNI gRPC host ----------
WebApplication? app = null;
if (sniEnabled)
{
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.ConfigureKestrel(o =>
        o.ListenLocalhost(sniPort, lo => lo.Protocols = HttpProtocols.Http2));
    builder.Logging.ClearProviders();
    builder.Services.AddSingleton(core);
    builder.Services.AddGrpc();
    app = builder.Build();
    app.MapGrpcService<DevicesMockService>();
    app.MapGrpcService<DeviceMemoryMockService>();
    app.MapGrpcService<DeviceControlMockService>();
    app.MapGrpcService<DeviceInfoMockService>();
    await app.StartAsync();
    Console.WriteLine($"[SNI] gRPC listening on http://localhost:{sniPort}");
}

// ---------- HTTP control API (independent Kestrel host, always on) ----------
const int ControlPort = 9090;
var controlBuilder = WebApplication.CreateBuilder();
controlBuilder.WebHost.ConfigureKestrel(o => o.ListenLocalhost(ControlPort));
controlBuilder.Logging.ClearProviders();
var control = controlBuilder.Build();
ControlHttp.Map(control, session);
await control.StartAsync();
Console.WriteLine($"[CTRL] HTTP control API on http://localhost:{ControlPort}");

// ---------- NWA TCP host ----------
NwaServer? nwa = null;
if (nwaEnabled)
{
    nwa = new NwaServer(session, nwaPort, systemBus);
    nwa.Start();
    Console.WriteLine($"[NWA] TCP listening on port {nwaPort}");
}

// The SNI and NWA backends share the same MemorySession, so writes from either
// are visible to the other.

if (runForever)
{
    Console.WriteLine("Running until interrupted.");
    await Task.Delay(Timeout.Infinite);
}

// ---------- REPL / script processing ----------
Console.WriteLine("Commands: set/get/reset. Ctrl+C to exit.");
string? line;
while ((line = Console.ReadLine()) != null)
{
    if (string.IsNullOrWhiteSpace(line)) continue;
    Commands.Execute(line, session, ex => Console.WriteLine($"shell error: {ex.Message}"));
}

if (app != null) await app.StopAsync();
await control.StopAsync();
nwa?.Stop();
return 0;

static (bool sni, bool nwa, int sniPort, int nwaPort, string kind, bool systemBus, bool runForever) ParseArgs(string[] args)
{
    bool sni = false, nwa = false, systemBus = true, runForever = false;
    int sniPort = 8191, nwaPort = 0xBEEF;
    string kind = "fxpakpro";

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--sni":
                sni = true;
                if (args.Length > i + 1 && !args[i + 1].StartsWith("-"))
                    kind = args[++i];
                break;
            case "--nwa": nwa = true; break;
            case "--sni-port": sniPort = int.Parse(args[++i]); break;
            case "--nwa-port": nwaPort = int.Parse(args[++i]); break;
            case "--no-system-bus": systemBus = false; break;
            case "--run": runForever = true; break;
        }
    }

    // Default: if neither backend requested, start SNI (fxpakpro) so the tool
    // is useful out of the box.
    if (!sni && !nwa) sni = true;
    return (sni, nwa, sniPort, nwaPort, kind, systemBus, runForever);
}
