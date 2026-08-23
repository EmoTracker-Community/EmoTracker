using EmoTracker.MockRandomizer.Memory;
using Google.Protobuf;
using Grpc.Core;
using SNI;

namespace EmoTracker.MockRandomizer.Sni;

/// <summary>
/// Shared state for the SNI mock services: the memory session, the device
/// flavor (FX Pak Pro vs emulator), and address translation.
/// </summary>
public sealed class SniCore
{
    public enum BackendKind { FxPakPro, Emulator }

    public MemorySession Session { get; }
    public SnivAddressTranslator Translator { get; }
    public BackendKind Kind { get; }

    public SniCore(MemorySession session, BackendKind kind)
    {
        Session = session;
        Kind = kind;
        Translator = new SnivAddressTranslator(session);
    }

    public string DeviceUri => Kind == BackendKind.FxPakPro
        ? "fxpakpro://./mocktty0"
        : "ra://127.0.0.1:55355";

    public string KindName => Kind == BackendKind.FxPakPro ? "fxpakpro" : "retroarch";
    public string DisplayName => Kind == BackendKind.FxPakPro ? "Mock FXPakPro" : "Mock RetroArch";
    public AddressSpace DefaultAddressSpace =>
        Kind == BackendKind.FxPakPro ? AddressSpace.FxPakPro : AddressSpace.SnesAbus;
}

public sealed class DevicesMockService : Devices.DevicesBase
{
    readonly SniCore mCore;
    public DevicesMockService(SniCore core) => mCore = core;

    public override Task<DevicesResponse> ListDevices(DevicesRequest request, ServerCallContext context)
    {
        var device = new DevicesResponse.Types.Device
        {
            Uri = mCore.DeviceUri,
            DisplayName = mCore.DisplayName,
            Kind = mCore.KindName,
            DefaultAddressSpace = mCore.DefaultAddressSpace
        };
        device.Capabilities.Add(DeviceCapability.ReadMemory);
        device.Capabilities.Add(DeviceCapability.WriteMemory);
        device.Capabilities.Add(DeviceCapability.ResetSystem);
        device.Capabilities.Add(DeviceCapability.ResetToMenu);

        var resp = new DevicesResponse();
        if (request.Kinds.Count == 0 || request.Kinds.Contains(mCore.KindName))
            resp.Devices.Add(device);
        return Task.FromResult(resp);
    }
}

public sealed class DeviceMemoryMockService : DeviceMemory.DeviceMemoryBase
{
    readonly SniCore mCore;
    public DeviceMemoryMockService(SniCore core) => mCore = core;

    public override Task<DetectMemoryMappingResponse> MappingDetect(DetectMemoryMappingRequest request, ServerCallContext context)
    {
        bool exhirom = mCore.Session.ActiveProfile == Memory.CartridgeProfile.Smz3ExHiRom;
        byte[] header;
        if (request.HasRomHeader00FFB0 && request.RomHeader00FFB0.Length > 0)
            header = request.RomHeader00FFB0.ToByteArray();
        else
            header = mCore.Session.ReadBus(exhirom ? 0x40FFB0UL : 0x00FFB0UL, 0x50);

        var resp = new DetectMemoryMappingResponse
        {
            Uri = request.Uri,
            MemoryMapping = exhirom ? MemoryMapping.ExHiRom : MemoryMapping.LoRom,
            Confidence = true
        };
        resp.RomHeader00FFB0 = ByteString.CopyFrom(header);
        return Task.FromResult(resp);
    }

    public override Task<SingleReadMemoryResponse> SingleRead(SingleReadMemoryRequest request, ServerCallContext context)
    {
        var req = request.Request;
        int len = (int)Math.Min(req.Size, 0x10000);
        byte[] data = mCore.Translator.Read(req.RequestAddress, (int)req.RequestAddressSpace, (int)req.RequestMemoryMapping, len);
        return Task.FromResult(new SingleReadMemoryResponse
        {
            Uri = request.Uri,
            Response = new ReadMemoryResponse
            {
                RequestAddress = req.RequestAddress,
                RequestAddressSpace = req.RequestAddressSpace,
                RequestMemoryMapping = req.RequestMemoryMapping,
                DeviceAddress = req.RequestAddress,
                DeviceAddressSpace = req.RequestAddressSpace,
                Data = ByteString.CopyFrom(data)
            }
        });
    }

    public override Task<SingleWriteMemoryResponse> SingleWrite(SingleWriteMemoryRequest request, ServerCallContext context)
    {
        var req = request.Request;
        mCore.Translator.Write(req.RequestAddress, (int)req.RequestAddressSpace, (int)req.RequestMemoryMapping, req.Data.ToByteArray());
        return Task.FromResult(new SingleWriteMemoryResponse
        {
            Uri = request.Uri,
            Response = new WriteMemoryResponse
            {
                RequestAddress = req.RequestAddress,
                RequestAddressSpace = req.RequestAddressSpace,
                RequestMemoryMapping = req.RequestMemoryMapping,
                DeviceAddress = req.RequestAddress,
                DeviceAddressSpace = req.RequestAddressSpace,
                Size = (uint)req.Data.Length
            }
        });
    }

    public override Task<MultiReadMemoryResponse> MultiRead(MultiReadMemoryRequest request, ServerCallContext context)
    {
        var resp = new MultiReadMemoryResponse { Uri = request.Uri };
        foreach (var r in request.Requests)
        {
            int len = (int)Math.Min(r.Size, 0x10000);
            byte[] data = mCore.Translator.Read(r.RequestAddress, (int)r.RequestAddressSpace, (int)r.RequestMemoryMapping, len);
            resp.Responses.Add(new ReadMemoryResponse
            {
                RequestAddress = r.RequestAddress,
                RequestAddressSpace = r.RequestAddressSpace,
                RequestMemoryMapping = r.RequestMemoryMapping,
                DeviceAddress = r.RequestAddress,
                DeviceAddressSpace = r.RequestAddressSpace,
                Data = ByteString.CopyFrom(data)
            });
        }
        return Task.FromResult(resp);
    }
}

public sealed class DeviceControlMockService : DeviceControl.DeviceControlBase
{
    readonly SniCore mCore;
    public DeviceControlMockService(SniCore core) => mCore = core;

    public override Task<ResetSystemResponse> ResetSystem(ResetSystemRequest request, ServerCallContext context)
    {
        mCore.Session.Reset();
        return Task.FromResult(new ResetSystemResponse { Uri = request.Uri });
    }

    public override Task<ResetToMenuResponse> ResetToMenu(ResetToMenuRequest request, ServerCallContext context)
    {
        mCore.Session.Reset();
        return Task.FromResult(new ResetToMenuResponse { Uri = request.Uri });
    }
}

public sealed class DeviceInfoMockService : DeviceInfo.DeviceInfoBase
{
    readonly SniCore mCore;
    public DeviceInfoMockService(SniCore core) => mCore = core;

    public override Task<FieldsResponse> FetchFields(FieldsRequest request, ServerCallContext context)
    {
        var resp = new FieldsResponse { Uri = request.Uri };
        foreach (var f in request.Fields)
        {
            resp.Fields.Add(f);
            resp.Values.Add(f switch
            {
                Field.DeviceName => mCore.DisplayName,
                Field.DeviceVersion => "1.0.0",
                Field.CoreName => mCore.Kind == SniCore.BackendKind.Emulator ? "Snes9x" : "FXPakPro",
                Field.CorePlatform => "SNES",
                Field.RomFileName => mCore.Session.ActiveProfile == Memory.CartridgeProfile.Smz3ExHiRom ? "mock-smz3.sfc" : "mock-alttp.sfc",
                _ => ""
            });
        }
        return Task.FromResult(resp);
    }
}
