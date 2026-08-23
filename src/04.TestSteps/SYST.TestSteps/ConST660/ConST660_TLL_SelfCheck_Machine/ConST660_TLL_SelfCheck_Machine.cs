using SYST.Core.Abstractions;

namespace SYST.TestSteps.ConST660.ConST660_TLL_SelfCheck_Machine;

/// <summary>
/// ConST660 低温炉-160整机自检处理器集合（清单 Key=ConST660_TLL_SelfCheck_Machine）。测试项逻辑共享 <see cref="ConST660Ops"/>，
/// 本文件仅提供 DeviceFamily=清单 Key 的处理器注册（引擎按 manifest.Key 解析）。
/// PORT: 旧脚本方法 <c>T01_SelftCheckTest_Dev</c>。
/// </summary>
/// <summary>
/// ConST660 低温炉-160整机自检：ConST660SelfCheck 测试项。
/// </summary>
public sealed class ConST660SelfCheckHandler : IStepHandler
{
    public string Kind => "ConST660SelfCheck";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.SelfCheckAsync(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：TestWriteSN 测试项。
/// </summary>
public sealed class ConST660WriteSNHandler : IStepHandler
{
    public string Kind => "TestWriteSN";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.WriteSNAsync(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：TestWriteDevType 测试项。
/// </summary>
public sealed class ConST660WriteDevTypeHandler : IStepHandler
{
    public string Kind => "TestWriteDevType";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.WriteDevTypeAsync(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：TestWriteRange 测试项。
/// </summary>
public sealed class ConST660WriteRangeHandler : IStepHandler
{
    public string Kind => "TestWriteRange";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.WriteRangeAsync(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：TestSoftVersions 测试项。
/// </summary>
public sealed class ConST660SoftVersionsHandler : IStepHandler
{
    public string Kind => "TestSoftVersions";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.SoftVersionsAsync(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：LCDBadPixelTest 测试项。
/// </summary>
public sealed class ConST660LCDBadPixelHandler : IStepHandler
{
    public string Kind => "LCDBadPixelTest";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.LcdBadPixelAsync(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：LCDTouchTest 测试项。
/// </summary>
public sealed class ConST660LCDTouchHandler : IStepHandler
{
    public string Kind => "LCDTouchTest";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.LcdTouchAsync(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：TestSpeaker 测试项。
/// </summary>
public sealed class ConST660SpeakerHandler : IStepHandler
{
    public string Kind => "TestSpeaker";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.SpeakerAsync(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：SystemVoltageTest 测试项。
/// </summary>
public sealed class ConST660VoltageHandler : IStepHandler
{
    public string Kind => "SystemVoltageTest";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.VoltageAsync(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：TestUSBPrincipal 测试项。
/// </summary>
public sealed class ConST660USBPrincipalHandler : IStepHandler
{
    public string Kind => "TestUSBPrincipal";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.UsbPrincipalAsync(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：TestUSBSubordinate 测试项。
/// </summary>
public sealed class ConST660USBSubordinateHandler : IStepHandler
{
    public string Kind => "TestUSBSubordinate";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.UsbSubordinateAsync(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：TestStorageCardPrincipal 测试项。
/// </summary>
public sealed class ConST660StorageCardHandler : IStepHandler
{
    public string Kind => "TestStorageCardPrincipal";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.StorageCardAsync(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：TestClock 测试项。
/// </summary>
public sealed class ConST660ClockHandler : IStepHandler
{
    public string Kind => "TestClock";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.ClockAsync(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：TestWifi 测试项。
/// </summary>
public sealed class ConST660WifiHandler : IStepHandler
{
    public string Kind => "TestWifi";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.WifiAsync(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：TestBluetooth 测试项。
/// </summary>
public sealed class ConST660BluetoothHandler : IStepHandler
{
    public string Kind => "TestBluetooth";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.BluetoothAsync(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：TestControlTemperature 测试项。
/// </summary>
public sealed class ConST660ControlTemperatureHandler : IStepHandler
{
    public string Kind => "TestControlTemperature";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.ControlTemperatureAsync(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：TestControlTemperature_TLL_LTC 测试项。
/// </summary>
public sealed class ConST660ControlTemperatureTLLHandler : IStepHandler
{
    public string Kind => "TestControlTemperature_TLL_LTC";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.ControlTemperatureTllAsync(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：TestControlTemperature2 测试项。
/// </summary>
public sealed class ConST660ControlTemperature2Handler : IStepHandler
{
    public string Kind => "TestControlTemperature2";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.ControlTemperature2Async(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：TestElectricalCom0 测试项。
/// </summary>
public sealed class ConST660Com0Handler : IStepHandler
{
    public string Kind => "TestElectricalCom0";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.Com0Async(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：TestElectricalIO 测试项。
/// </summary>
public sealed class ConST660ElectricalIOHandler : IStepHandler
{
    public string Kind => "TestElectricalIO";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.ElectricalIOAsync(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：TestControllerCom3 测试项。
/// </summary>
public sealed class ConST660ControllerCom3Handler : IStepHandler
{
    public string Kind => "TestControllerCom3";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.ControllerCom3Async(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：TestControllerIO 测试项。
/// </summary>
public sealed class ConST660ControllerIOHandler : IStepHandler
{
    public string Kind => "TestControllerIO";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.ControllerIOAsync(ctx, ct);
}

/// <summary>
/// ConST660 低温炉-160整机自检：PowerOnOffTest 测试项。
/// </summary>
public sealed class ConST660PowerOnOffHandler : IStepHandler
{
    public string Kind => "PowerOnOffTest";
    public string? DeviceFamily => "ConST660_TLL_SelfCheck_Machine";

    public Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
        => ConST660Ops.PowerOnOffAsync(ctx, ct);
}
