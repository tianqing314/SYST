using SYST.Core.Abstractions;
using SYST.Devices.Abstractions.Dut;

namespace SYST.TestSteps.ConST685.SelfCheck;

/// <summary>
/// ConST685 过程校验仪整机自检处理器。
/// PORT: 旧脚本方法 <c>T02_SelftCheckTest_Dev</c>。
/// </summary>
public sealed class ConST685SelfCheckHandler : IStepHandler
{
    public string Kind => "ConST685SelfCheck";
    public string? DeviceFamily => "ConST685_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST685Dut>();

        // 1) 连接检查
        if (!dut.IsConnected)
        {
            await dut.ConnectAsync(ct);
        }
        if (!dut.IsConnected)
        {
            return StepResult.Fail("被检 ConST685 未就绪");
        }

        var pass = true;

        // 2) 读序列号
        var sn = await dut.ReadSerialNumberAsync(ct);
        ctx.Report($"序列号: {sn}");
        if (string.IsNullOrWhiteSpace(sn)) pass = false;

        // 3) 读固件版本
        var fw = await dut.ReadFirmwareVersionAsync(ct);
        ctx.Report($"固件版本: {fw}");
        if (string.IsNullOrWhiteSpace(fw)) pass = false;

        // 4) 补充连接
        if (!await dut.ReplenishLinkAsync(ct))
        {
            pass = false;
            ctx.Report("补充连接失败", RealtimeLevel.Error);
        }

        await Task.Delay(200, ct);
        return pass ? StepResult.Pass("ConST685 自检通过") : StepResult.Fail("ConST685 自检不通过");
    }
}

/// <summary>
/// ConST685 SN 写入。PORT: 旧脚本 TestWriteSN。
/// </summary>
public sealed class ConST685WriteSNHandler : IStepHandler
{
    public string Kind => "TestWriteSN";
    public string? DeviceFamily => "ConST685_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST685Dut>();
        var requested = ctx.Parameter("写入SN")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(requested)) requested = ctx.SerialNumber ?? "";
        if (string.IsNullOrWhiteSpace(requested))
            return StepResult.Fail("SN写入未通过：未提供 SN 值");

        ctx.Report($"写入SN: {requested}");
        var ok = await dut.SetSerialNumberAsync(requested, ct);

        if (ok)
        {
            var readBack = await dut.ReadSerialNumberAsync(ct);
            ctx.Report($"读回SN: {readBack}");
            ok = string.Equals(requested, readBack, StringComparison.Ordinal);
        }

        ctx.SerialNumber = requested;
        return ok ? StepResult.Pass("SN写入通过") : StepResult.Fail("SN写入未通过");
    }
}

/// <summary>
/// ConST685 型号写入。PORT: 旧脚本 TestWriteDevType。
/// </summary>
public sealed class ConST685WriteDevTypeHandler : IStepHandler
{
    public string Kind => "TestWriteDevType";
    public string? DeviceFamily => "ConST685_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST685Dut>();
        var model = ctx.Parameter("写入型号")?.Value?.Trim() ?? "ConST685";
        ctx.Report($"写入型号: {model}");
        var ok = await dut.SetPrimaryDeviceTypeAsync(model, ct);
        return ok ? StepResult.Pass("型号写入通过") : StepResult.Fail("型号写入未通过");
    }
}

/// <summary>
/// ConST685 软件版本验证及升级。PORT: 旧脚本 TestSoftVersions。
/// </summary>
public sealed class ConST685SoftVersionsHandler : IStepHandler
{
    public string Kind => "TestSoftVersions";
    public string? DeviceFamily => "ConST685_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST685Dut>();
        var pass = true;

        var sysVersion = await dut.QueryTextAsync("GetVersion", null, ct);
        ctx.Report($"系统版本: {sysVersion}");
        if (string.IsNullOrWhiteSpace(sysVersion)) pass = false;

        var ddP = await dut.QueryTextAsync("GetDDLibVersion_P", null, ct);
        ctx.Report($"DD库压力版本: {ddP}");

        var ddT = await dut.QueryTextAsync("GetDDLibVersion_T", null, ct);
        ctx.Report($"DD库温度版本: {ddT}");

        var sn = await dut.ReadSerialNumberAsync(ct);
        ctx.SerialNumber = sn;
        ctx.Report($"序列号: {sn}");

        return pass ? StepResult.Pass("软件版本验证通过") : StepResult.Fail("软件版本验证未通过");
    }
}

/// <summary>
/// ConST685 LCD 液晶屏坏点测试。PORT: 旧脚本 LCDBadPixelTest。
/// </summary>
public sealed class ConST685LCDBadPixelHandler : IStepHandler
{
    public string Kind => "LCDBadPixelTest";
    public string? DeviceFamily => "ConST685_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST685Dut>();
        if (!await dut.QueryBooleanAsync("SetBadPixelCheckerOpen", null, ct))
        {
            return StepResult.Fail("启动屏幕坏点自检程序失败");
        }
        var state = await PollChecker(dut, ct);
        await dut.QueryBooleanAsync("SetCheckerClose", null, ct);
        return state ? StepResult.Pass("屏幕坏点测试通过") : StepResult.Fail("屏幕坏点测试未通过");
    }

    private static async Task<bool> PollChecker(IConST685Dut dut, CancellationToken ct)
    {
        for (var i = 0; i < 120; i++)
        {
            var s = await dut.QueryTextAsync("GetCheckerState", null, ct);
            if (s == "TestPass") return true;
            if (s == "TestFail") return false;
            await Task.Delay(500, ct);
        }
        return false;
    }
}

/// <summary>
/// ConST685 触摸屏测试。PORT: 旧脚本 LCDTouchTest。
/// </summary>
public sealed class ConST685LCDTouchHandler : IStepHandler
{
    public string Kind => "LCDTouchTest";
    public string? DeviceFamily => "ConST685_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST685Dut>();
        if (!await dut.QueryBooleanAsync("SetTouchCheckerOpen", null, ct))
        {
            return StepResult.Fail("启动触摸测试失败");
        }
        var state = await PollChecker(dut, ct);
        await dut.QueryBooleanAsync("SetCheckerClose", null, ct);
        return state ? StepResult.Pass("触摸屏测试通过") : StepResult.Fail("触摸屏测试未通过");
    }

    private static async Task<bool> PollChecker(IConST685Dut dut, CancellationToken ct)
    {
        for (var i = 0; i < 120; i++)
        {
            var s = await dut.QueryTextAsync("GetCheckerState", null, ct);
            if (s == "TestPass") return true;
            if (s == "TestFail") return false;
            await Task.Delay(500, ct);
        }
        return false;
    }
}

/// <summary>
/// ConST685 扬声器测试。PORT: 旧脚本 TestSpeaker。
/// </summary>
public sealed class ConST685SpeakerHandler : IStepHandler
{
    public string Kind => "TestSpeaker";
    public string? DeviceFamily => "ConST685_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST685Dut>();
        if (!await dut.QueryBooleanAsync("SetSpeakerCheckerOpen", null, ct))
        {
            return StepResult.Fail("启动扬声器测试失败");
        }
        var state = await PollChecker(dut, ct);
        await dut.QueryBooleanAsync("SetCheckerClose", null, ct);
        return state ? StepResult.Pass("扬声器测试通过") : StepResult.Fail("扬声器测试未通过");
    }

    private static async Task<bool> PollChecker(IConST685Dut dut, CancellationToken ct)
    {
        for (var i = 0; i < 120; i++)
        {
            var s = await dut.QueryTextAsync("GetCheckerState", null, ct);
            if (s == "TestPass") return true;
            if (s == "TestFail") return false;
            await Task.Delay(500, ct);
        }
        return false;
    }
}

/// <summary>
/// ConST685 系统电压/5V/12V/3.3V 测量。PORT: 旧脚本 SystemVoltageTest / TestSystem5Voltage / TestSystem12Voltage / TestSystem3_3Voltage。
/// </summary>
public sealed class ConST685VoltageHandler : IStepHandler
{
    public string Kind => "SystemVoltageTest";
    public string? DeviceFamily => "ConST685_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST685Dut>();
        var point = ctx.Step.Settings.TryGetValue("Point", out var p) ? p : "SystemVoltage";
        var value = await dut.MeasureAsync(point, ct);
        ctx.Report($"{point}: {value:F3} V");

        if (ctx.Conditions.Count > 0)
        {
            var r = ctx.Evaluator.Evaluate(ctx.Conditions[0], value);
            ctx.Report(r.Message, r.Passed ? RealtimeLevel.Info : RealtimeLevel.Warn);
            return r.Passed ? StepResult.Pass($"{point} 通过", value.ToString("F3")) : StepResult.Fail($"{point} 未通过");
        }
        return StepResult.Pass($"{point} 测量完成", value.ToString("F3"));
    }
}

/// <summary>
/// ConST685 REF1/REF2 零点测试。PORT: 旧脚本 TestDeviceChanelZero。
/// </summary>
public sealed class ConST685ChannelZeroHandler : IStepHandler
{
    public string Kind => "TestDeviceChanelZero";
    public string? DeviceFamily => "ConST685_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST685Dut>();
        // 读取 REF1/REF2 电阻值
        var ref1Str = await dut.QueryTextAsync("GettEleREFOriginalValue", null, ct);
        var ref2Str = await dut.QueryTextAsync("GettEleDS2431State", null, ct);
        var ok = true;
        if (string.IsNullOrWhiteSpace(ref1Str) || string.IsNullOrWhiteSpace(ref2Str))
        {
            ok = false;
        }
        else
        {
            if (double.TryParse(ref1Str, out var r1) && double.TryParse(ref2Str, out var r2))
            {
                ctx.Report($"REF1 电阻: {r1:F3}Ω; REF2 电阻: {r2:F3}Ω");
            }
            else
            {
                ok = false;
            }
        }
        return ok ? StepResult.Pass("零点测试通过") : StepResult.Fail("零点测试未通过");
    }
}

/// <summary>
/// ConST685 接线盒内嵌测试。PORT: 旧脚本 TestConnectorsInternal。
/// </summary>
public sealed class ConST685ConnectorsInternalHandler : IStepHandler
{
    public string Kind => "TestConnectorsInternal";
    public string? DeviceFamily => "ConST685_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST685Dut>();
        var ok = await dut.QueryBooleanAsync("TestConnectorsInternal", null, ct);
        var info = await dut.QueryTextAsync("GetConnectorsInternalInfo", null, ct);
        ctx.Report($"内嵌接线盒: {info}");
        return ok ? StepResult.Pass("内嵌接线盒测试通过") : StepResult.Fail("内嵌接线盒测试未通过");
    }
}

/// <summary>
/// ConST685 接线盒外接测试。PORT: 旧脚本 TestConnectorsExternal。
/// </summary>
public sealed class ConST685ConnectorsExternalHandler : IStepHandler
{
    public string Kind => "TestConnectorsExternal";
    public string? DeviceFamily => "ConST685_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST685Dut>();
        var ok = await dut.QueryBooleanAsync("TestConnectorsExternal", null, ct);
        var info = await dut.QueryTextAsync("GetConnectorsExternalInfo", null, ct);
        ctx.Report($"外接接线盒: {info}");
        return ok ? StepResult.Pass("外接接线盒测试通过") : StepResult.Fail("外接接线盒测试未通过");
    }
}

/// <summary>
/// ConST685 启动 LOGO 设置。PORT: 旧脚本 TestSetStartLogo。
/// </summary>
public sealed class ConST685SetStartLogoHandler : IStepHandler
{
    public string Kind => "TestSetStartLogo";
    public string? DeviceFamily => "ConST685_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST685Dut>();
        var ok = await dut.QueryBooleanAsync("TestSetStartLogo", null, ct);
        return ok ? StepResult.Pass("LOGO设置通过") : StepResult.Fail("LOGO设置未通过");
    }
}

/// <summary>
/// ConST685 外部电阻关闭。PORT: 旧脚本 TestCloseExternalResistance。
/// </summary>
public sealed class ConST685CloseExternalResistanceHandler : IStepHandler
{
    public string Kind => "TestCloseExternalResistance";
    public string? DeviceFamily => "ConST685_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST685Dut>();
        var ok = await dut.QueryBooleanAsync("TestCloseExternalResistance", null, ct);
        return ok ? StepResult.Pass("外部电阻关闭通过") : StepResult.Fail("外部电阻关闭未通过");
    }
}

/// <summary>
/// ConST685 实时时钟测试。PORT: 旧脚本 TestClock。
/// </summary>
public sealed class ConST685ClockHandler : IStepHandler
{
    public string Kind => "TestClock";
    public string? DeviceFamily => "ConST685_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST685Dut>();
        var now = DateTime.Now;
        await dut.CommandAsync("SetSystemDateTime", now.ToString("yyyy-MM-dd HH:mm:ss"), ct);
        await Task.Delay(500, ct);
        var readBack = await dut.QueryTextAsync("GetRtc", null, ct);
        ctx.Report($"设置时间: {now:yyyy-MM-dd HH:mm:ss}，回读时间: {readBack}");
        var ok = DateTime.TryParse(readBack, out var parsed) && Math.Abs((parsed - now).TotalSeconds) < 5;
        return ok ? StepResult.Pass("RTC时钟测试通过") : StepResult.Fail("RTC时钟测试未通过");
    }
}

/// <summary>
/// ConST685 WiFi 测试。PORT: 旧脚本 TestWifi。
/// </summary>
public sealed class ConST685WifiHandler : IStepHandler
{
    public string Kind => "TestWifi";
    public string? DeviceFamily => "ConST685_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST685Dut>();
        var ssid = ctx.Parameter("SSID")?.Value ?? "CONSTSC";
        var mode = ctx.Parameter("EncryptionMode")?.Value ?? "WPA_PSK_AES";
        var pwd = ctx.Parameter("Password")?.Value ?? "4001131199";

        await dut.CommandAsync("SetWifiOpen", null, ct);
        await Task.Delay(2000, ct);
        var ok = await dut.QueryBooleanAsync("ConnectWifiToHotspot", new[] { ssid, mode, pwd }, ct);
        await dut.CommandAsync("SetWifiClose", null, ct);

        return ok ? StepResult.Pass("WiFi测试通过") : StepResult.Fail("WiFi测试未通过");
    }
}

/// <summary>
/// ConST685 蓝牙测试。PORT: 旧脚本 TestBluetooth。
/// </summary>
public sealed class ConST685BluetoothHandler : IStepHandler
{
    public string Kind => "TestBluetooth";
    public string? DeviceFamily => "ConST685_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST685Dut>();
        var ok = await dut.QueryBooleanAsync("GetBluetoothState", null, ct);
        return ok ? StepResult.Pass("蓝牙测试通过") : StepResult.Fail("蓝牙测试未通过");
    }
}

/// <summary>
/// ConST685 开关机人工确认测试。
/// </summary>
public sealed class ConST685PowerOnOffHandler : IStepHandler
{
    public string Kind => "PowerOnOffTest";
    public string? DeviceFamily => "ConST685_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var ok = await ctx.ConfirmAsync("请按电源键开机，观察设备是否正常启动？", ct);
        return ok ? StepResult.Pass("开关机测试通过") : StepResult.Fail("开关机测试未通过");
    }
}

/// <summary>
/// ConST685 USB 主口测试。PORT: 旧脚本 TestUSBPrincipal。
/// </summary>
public sealed class ConST685USBPrincipalHandler : IStepHandler
{
    public string Kind => "TestUSBPrincipal";
    public string? DeviceFamily => "ConST685_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var writeData = ctx.Parameter("写入数据")?.Value ?? "testData010101-usb";
        var readBack = await UsbQueryAsync(ctx, writeData, ct);
        var ok = string.Equals(writeData, readBack, StringComparison.Ordinal);
        return ok ? StepResult.Pass("USB主口测试通过") : StepResult.Fail("USB主口测试未通过");
    }

    private static async Task<string> UsbQueryAsync(ITestContext ctx, string data, CancellationToken ct)
    {
        var dut = ctx.GetDevice<IConST685Dut>();
        await dut.CommandAsync("AddDataToUSB", data, ct);
        await Task.Delay(100, ct);
        return await dut.QueryTextAsync("ReadDataFromUSB", null, ct);
    }
}

/// <summary>
/// ConST685 USB 从口测试。PORT: 旧脚本 TestUSBSubordinate。
/// </summary>
public sealed class ConST685USBSubordinateHandler : IStepHandler
{
    public string Kind => "TestUSBSubordinate";
    public string? DeviceFamily => "ConST685_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST685Dut>();
        var ok = await dut.QueryBooleanAsync("GetUSBCommState", null, ct);
        return ok ? StepResult.Pass("USB从口测试通过") : StepResult.Fail("USB从口测试未通过");
    }
}

/// <summary>
/// ConST685 SD 卡测试。PORT: 旧脚本 TestStorageCardPrincipal。
/// </summary>
public sealed class ConST685StorageCardHandler : IStepHandler
{
    public string Kind => "TestStorageCardPrincipal";
    public string? DeviceFamily => "ConST685_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST685Dut>();
        var state = await dut.QueryTextAsync("GetStorageCardState", null, ct);
        ctx.Report($"存储卡状态: {state}");
        var ok = !string.IsNullOrWhiteSpace(state) && state != "0";
        return ok ? StepResult.Pass("SD卡测试通过") : StepResult.Fail("SD卡测试未通过");
    }
}
