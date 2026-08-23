using System.Globalization;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;
using SYST.Devices.Abstractions.Dut;

namespace SYST.TestSteps.ConST860.ConST860_SelfCheck_Q_GW2;

// ============================================================================
// ConST860_SelfCheck_Q_GW2 处理器集合（清单 Key=ConST860_SelfCheck_Q_GW2）。逻辑见 Shared/ConST860Ops。
// 本文件 DeviceFamily=清单 Key，供引擎按 manifest.Key 解析。
// ============================================================================

/// <summary>
/// ConST860 整机自检（公共基础）。PORT: 旧 <c>ConST860_SelfCheckTest_Y_Task</c> / <c>ConST860_SelfCheck_Q_GW2_Task</c>
/// 公共部分：连接、序列号读取、自检异常查询。
/// </summary>
public sealed class ConST860SelfCheckHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "ConST860SelfCheck";

    /// <summary>限定设备家族。</summary>
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    /// <summary>执行本测试项。</summary>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        var pass = true;

        await op.Sleep(500);

        // 1) 连接/重连被检
        if (!await op.TryCommand(() => Task.FromResult(op.Dut.IsConnected), "被检连接检查"))
        {
            op.Fail("被检 ConST860 未就绪");
            return StepResult.Fail("被检未就绪");
        }

        // 2) 读序列号
        var sn = await op.Dut.ReadSerialNumberAsync(ct);
        op.Text("序列号", sn);
        if (string.IsNullOrWhiteSpace(sn)) { pass = false; op.Fail("读取序列号失败"); }

        // 3) 读固件版本
        var fw = await op.Dut.ReadFirmwareVersionAsync(ct);
        op.Text("固件版本", fw);
        if (string.IsNullOrWhiteSpace(fw)) { pass = false; op.Fail("读取固件版本失败"); }

        // 4) 设备自检异常查询。PORT: TestSelfCheckEXCeption
        var exception = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetSelfCheckException", null, ct), "设备自检异常");
        if (!string.IsNullOrEmpty(exception))
        {
            pass = false;
            op.Fail($"设备自检存在异常：{exception}");
        }

        return pass
            ? StepResult.Pass("ConST860 整机自检通过")
            : StepResult.Fail("ConST860 整机自检不通过");
    }
}

// ============================================================================
// 公共步骤（Q/Y 共用）
// ============================================================================

/// <summary>
/// SN 号写入。PORT: SN_Write。
/// </summary>

/// <summary>
/// SN 号写入。PORT: SN_Write。
/// </summary>
public sealed class ConST860WriteSNHandler : IStepHandler
{
    public string Kind => "SN_Write";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST860Dut>();
        var requested = ctx.Parameter("写入SN")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(requested)) requested = ctx.SerialNumber ?? "";
        if (string.IsNullOrWhiteSpace(requested)) return StepResult.Fail("SN写入未通过：未提供 SN 值");

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
/// 型号写入（含压力量程）。PORT: Type_WriteWithPressureRange。
/// </summary>

/// <summary>
/// 型号写入（含压力量程）。PORT: Type_WriteWithPressureRange。
/// </summary>
public sealed class ConST860WriteTypeWithRangeHandler : IStepHandler
{
    public string Kind => "Type_WriteWithPressureRange";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST860Dut>();
        var model = ctx.Parameter("写入型号")?.Value?.Trim() ?? "ConST860";
        var lower = ctx.Parameter("量程下限")?.Value ?? "-100";
        var upper = ctx.Parameter("量程上限")?.Value ?? "6000";

        ctx.Report($"写入型号: {model}，量程: {lower}~{upper} kPa");
        var okType = await dut.SetPrimaryDeviceTypeAsync(model, ct);

        // 压力量程经通用指令下发（Q/Y 驱动内部各自路由）
        await dut.CommandAsync("SetMakePressureRange", new object[] { lower, upper }, ct);

        return okType ? StepResult.Pass("型号与量程写入通过") : StepResult.Fail("型号与量程写入未通过");
    }
}

/// <summary>
/// 软件版本验证。PORT: TestSoftVersions / TestSoftVersions_Q。
/// </summary>

/// <summary>
/// 软件版本验证。PORT: TestSoftVersions / TestSoftVersions_Q。
/// </summary>
public sealed class ConST860SoftVersionsHandler : IStepHandler
{
    public string Kind => "TestSoftVersions";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST860Dut>();
        var pass = true;

        var mainVersion = await dut.QueryTextAsync("GetVersion", null, ct);
        ctx.Report($"主程序版本: {mainVersion}");
        if (string.IsNullOrWhiteSpace(mainVersion)) pass = false;

        var osVersion = await dut.QueryTextAsync("GetVersion_OS", null, ct);
        ctx.Report($"系统版本: {osVersion}");

        var controller = await dut.QueryTextAsync("GetVersion_Controller", null, ct);
        ctx.Report($"控制板版本: {controller}");

        return pass ? StepResult.Pass("软件版本验证通过") : StepResult.Fail("软件版本验证未通过");
    }
}

/// <summary>
/// 介质写入。PORT: TestMedium。
/// </summary>

/// <summary>
/// 介质写入。PORT: TestMedium。
/// </summary>
public sealed class ConST860MediumHandler : IStepHandler
{
    public string Kind => "TestMedium";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST860Dut>();
        var medium = ctx.Parameter("介质")?.Value ?? "空气";
        var ok = await dut.SetMediumAsync(medium, ct);
        ctx.Report($"写入介质: {medium}");
        return ok ? StepResult.Pass("介质写入通过") : StepResult.Fail("介质写入未通过");
    }
}

/// <summary>
/// 屏幕坏点测试。PORT: ScreenDefectsTest。
/// </summary>

/// <summary>
/// 屏幕坏点测试。PORT: ScreenDefectsTest。
/// </summary>
public sealed class ConST860ScreenDefectsHandler : IStepHandler
{
    public string Kind => "ScreenDefectsTest";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST860Dut>();
        if (!await dut.QueryBooleanAsync("SetCheckerOpen", new object[] { "BadPixel" }, ct))
            return StepResult.Fail("启动屏幕坏点自检程序失败");

        var state = await PollChecker(dut, ct);
        await dut.QueryBooleanAsync("SetCheckerClose", null, ct);
        return state ? StepResult.Pass("屏幕坏点测试通过") : StepResult.Fail("屏幕坏点测试未通过");
    }

    private static async Task<bool> PollChecker(IConST860Dut dut, CancellationToken ct)
    {
        for (var i = 0; i < 120; i++)
        {
            var s = await dut.QueryTextAsync("GetCheckerState", new object[] { "BadPixel" }, ct);
            if (s == "TestPass") return true;
            if (s == "TestFail") return false;
            await Task.Delay(500, ct);
        }
        return false;
    }
}

/// <summary>
/// 屏幕触摸测试。PORT: ScreenTouchTest。
/// </summary>

/// <summary>
/// 屏幕触摸测试。PORT: ScreenTouchTest。
/// </summary>
public sealed class ConST860ScreenTouchHandler : IStepHandler
{
    public string Kind => "ScreenTouchTest";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST860Dut>();
        if (!await dut.QueryBooleanAsync("SetCheckerOpen", new object[] { "Touch" }, ct))
            return StepResult.Fail("启动屏幕触摸自检程序失败");

        var state = await PollChecker(dut, ct);
        await dut.QueryBooleanAsync("SetCheckerClose", null, ct);
        return state ? StepResult.Pass("屏幕触摸测试通过") : StepResult.Fail("屏幕触摸测试未通过");
    }

    private static async Task<bool> PollChecker(IConST860Dut dut, CancellationToken ct)
    {
        for (var i = 0; i < 120; i++)
        {
            var s = await dut.QueryTextAsync("GetCheckerState", new object[] { "Touch" }, ct);
            if (s == "TestPass") return true;
            if (s == "TestFail") return false;
            await Task.Delay(500, ct);
        }
        return false;
    }
}

/// <summary>
/// 屏幕亮度测试。PORT: ScreenBrightnessTest。
/// </summary>

/// <summary>
/// 屏幕亮度测试。PORT: ScreenBrightnessTest。
/// </summary>
public sealed class ConST860ScreenBrightnessHandler : IStepHandler
{
    public string Kind => "ScreenBrightnessTest";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST860Dut>();
        var levelStr = ctx.Step.Settings.TryGetValue("Level", out var l) ? l : "100";
        var ok = await dut.QueryBooleanAsync("SetScreenBRIG", levelStr, ct);
        ctx.Report($"设置屏幕亮度: {levelStr}%");
        return ok ? StepResult.Pass("屏幕亮度测试通过") : StepResult.Fail("屏幕亮度测试未通过");
    }
}

/// <summary>
/// 蜂鸣器测试。PORT: BeeperTest。
/// </summary>

/// <summary>
/// 蜂鸣器测试。PORT: BeeperTest。
/// </summary>
public sealed class ConST860BeeperHandler : IStepHandler
{
    public string Kind => "BeeperTest";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST860Dut>();
        if (!await dut.QueryBooleanAsync("SetCheckerOpen", new object[] { "Speaker" }, ct))
            return StepResult.Fail("启动蜂鸣器自检程序失败");

        var state = await PollChecker(dut, ct);
        await dut.QueryBooleanAsync("SetCheckerClose", null, ct);
        return state ? StepResult.Pass("蜂鸣器测试通过") : StepResult.Fail("蜂鸣器测试未通过");
    }

    private static async Task<bool> PollChecker(IConST860Dut dut, CancellationToken ct)
    {
        for (var i = 0; i < 120; i++)
        {
            var s = await dut.QueryTextAsync("GetCheckerState", new object[] { "Speaker" }, ct);
            if (s == "TestPass") return true;
            if (s == "TestFail") return false;
            await Task.Delay(500, ct);
        }
        return false;
    }
}

/// <summary>
/// 主机风扇测试。PORT: FANTest。
/// </summary>

/// <summary>
/// 主机风扇测试。PORT: FANTest。
/// </summary>
public sealed class ConST860FanHandler : IStepHandler
{
    public string Kind => "FANTest";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST860Dut>();
        if (!await dut.QueryBooleanAsync("SetCheckerOpen", new object[] { "Fan" }, ct))
            return StepResult.Fail("启动风扇自检程序失败");

        var state = await PollChecker(dut, ct);
        await dut.QueryBooleanAsync("SetCheckerClose", null, ct);
        return state ? StepResult.Pass("主机风扇测试通过") : StepResult.Fail("主机风扇测试未通过");
    }

    private static async Task<bool> PollChecker(IConST860Dut dut, CancellationToken ct)
    {
        for (var i = 0; i < 120; i++)
        {
            var s = await dut.QueryTextAsync("GetCheckerState", new object[] { "Fan" }, ct);
            if (s == "TestPass") return true;
            if (s == "TestFail") return false;
            await Task.Delay(500, ct);
        }
        return false;
    }
}

/// <summary>
/// 网口通讯测试。PORT: TestLAN。
/// </summary>

/// <summary>
/// 网口通讯测试。PORT: TestLAN。
/// </summary>
public sealed class ConST860LANHandler : IStepHandler
{
    public string Kind => "TestLAN";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST860Dut>();
        var ip = await dut.QueryTextAsync("GetStaticETHemetIPAddress", null, ct);
        ctx.Report($"静态以太网 IP: {ip}");
        var ok = !string.IsNullOrWhiteSpace(ip) && (ip.StartsWith("192") || ip.StartsWith("169"));
        return ok ? StepResult.Pass("网口通讯测试通过") : StepResult.Fail("网口通讯测试未通过");
    }
}

/// <summary>
/// 外接压力模块通讯测试。PORT: ModuleConnectStateTest。
/// </summary>

/// <summary>
/// 外接压力模块通讯测试。PORT: ModuleConnectStateTest。
/// </summary>
public sealed class ConST860ModuleConnectStateHandler : IStepHandler
{
    public string Kind => "ModuleConnectStateTest";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST860Dut>();
        var state = await dut.QueryTextAsync("GetPressureModelOnlineState", null, ct);
        ctx.Report($"外接模块状态: {state}");
        var ok = !string.IsNullOrWhiteSpace(state) && state != "Error";
        return ok ? StepResult.Pass("外接压力模块通讯测试通过") : StepResult.Fail("外接压力模块通讯测试未通过");
    }
}

/// <summary>
/// U 盘测试。PORT: TestUSBPrincipal。
/// </summary>

/// <summary>
/// U 盘测试。PORT: TestUSBPrincipal。
/// </summary>
public sealed class ConST860USBPrincipalHandler : IStepHandler
{
    public string Kind => "TestUSBPrincipal";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST860Dut>();
        var writeData = ctx.Parameter("写入数据")?.Value ?? "testData010101-usb";
        await dut.CommandAsync("Writedatatofile", new object[] { "/Hard Disk/test.txt", writeData }, ct);
        await Task.Delay(200, ct);
        var readBack = await dut.QueryTextAsync("Getfileinfo", new object[] { "/Hard Disk/test.txt" }, ct);
        var ok = string.Equals(writeData, readBack, StringComparison.Ordinal);
        return ok ? StepResult.Pass("U盘测试通过") : StepResult.Fail("U盘测试未通过");
    }
}

/// <summary>
/// 时间测试。PORT: TimeTest。
/// </summary>

/// <summary>
/// 时间测试。PORT: TimeTest。
/// </summary>
public sealed class ConST860TimeHandler : IStepHandler
{
    public string Kind => "TimeTest";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST860Dut>();
        var now = DateTime.Now;
        await dut.CommandAsync("SetSystemTime", now.ToString("yyyy-MM-dd HH:mm:ss"), ct);
        await Task.Delay(500, ct);
        var readBack = await dut.QueryTextAsync("GetRtc", null, ct);
        ctx.Report($"设置时间: {now:yyyy-MM-dd HH:mm:ss}，回读时间: {readBack}");
        var ok = DateTime.TryParse(readBack, out var parsed) && Math.Abs((parsed - now).TotalSeconds) < 5;
        return ok ? StepResult.Pass("时间测试通过") : StepResult.Fail("时间测试未通过");
    }
}

/// <summary>
/// 电源开关测试。PORT: PowerTest。
/// </summary>

/// <summary>
/// 电源开关测试。PORT: PowerTest。
/// </summary>
public sealed class ConST860PowerHandler : IStepHandler
{
    public string Kind => "PowerTest";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST860Dut>();
        var ok = await dut.QueryBooleanAsync("GetPowerSwitchState", null, ct);
        ctx.Report($"电源开关状态: {(ok ? "正常" : "异常")}");
        return ok ? StepResult.Pass("电源开关测试通过") : StepResult.Fail("电源开关测试未通过");
    }
}

// ============================================================================
// 液压（Y）专属步骤
// ============================================================================

/// <summary>
/// 内置模块通讯测试（Y）。PORT: TestInnerModule。
/// </summary>

/// <summary>
/// 内置模块功能测试（Q）。PORT: TestInnerModule_Q。
/// </summary>
public sealed class ConST860InnerModuleQHandler : IStepHandler
{
    public string Kind => "TestInnerModule_Q";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        var info = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetInnerModuleInfo", null, ct), "气压内置模块信息");
        if (string.IsNullOrWhiteSpace(info)) return StepResult.Fail("内置模块功能测试未通过");
        return StepResult.Pass("内置模块功能测试通过");
    }
}

/// <summary>
/// 泵效率测试（Q，气泵转速+造压能力）。PORT: TestGaspump。
/// </summary>

/// <summary>
/// 泵效率测试（Q，气泵转速+造压能力）。PORT: TestGaspump。
/// </summary>
public sealed class ConST860GasPumpQHandler : IStepHandler
{
    public string Kind => "TestGaspump";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        var pass = true;

        // 启动泵并测转速
        var rpm = await op.Dut.GetPumpRpmAsync(ct);
        op.Value("泵转速", rpm, "rpm");
        if (!op.Judge("占空比50%电机转速", rpm, "泵转速", "rpm"))
        {
            pass = false;
        }

        // 正压造压目标
        var posTarget = double.TryParse(ctx.Step.Settings.TryGetValue("PositiveTargetKpa", out var pt) ? pt : "200",
            NumberStyles.Float, CultureInfo.InvariantCulture, out var ptv) ? ptv : 200;
        await op.Dut.ChargeAsync((char)Math.Min(posTarget / 1000.0, 'H'), ct);
        var outputPos = await op.Dut.MeasureAsync("OutputPressure", ct);
        op.Value("正压输出压力", outputPos, "kPa");
        if (!op.Judge("正压造压目标", outputPos, "正压造压", "kPa")) pass = false;

        // 正压泄漏
        await op.Sleep(2000, "稳压等待");
        var leakPos = await op.Dut.MeasureLeakAsync(ct);
        op.Value("正压泄漏量", leakPos, "kPa/min");
        if (!op.Judge("正压泄漏量", leakPos, "正压泄漏", "kPa/min")) pass = false;

        // 泄压收尾
        await op.Dut.CloseRepairVentAsync(ct);
        return pass ? StepResult.Pass("气泵效率测试通过") : StepResult.Fail("气泵效率测试未通过");
    }
}

/// <summary>
/// V6/V2 阀检测（Q）。PORT: TestV6andV2。
/// </summary>

/// <summary>
/// V6/V2 阀检测（Q）。PORT: TestV6andV2。
/// </summary>
public sealed class ConST860V6V2ValveQHandler : IStepHandler
{
    public string Kind => "TestV6andV2";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        foreach (var valve in new[] { "V6", "V2" })
        {
            var state = await op.TryQueryValue(
                () => op.Dut.QueryTextAsync("GetValveState", new object[] { valve }, ct), $"{valve} 阀状态");
            if (string.IsNullOrWhiteSpace(state) || state == "Error")
            {
                op.Fail($"{valve} 阀状态异常");
                return StepResult.Fail("V6/V2阀检测未通过");
            }
        }
        return StepResult.Pass("V6/V2阀检测通过");
    }
}

/// <summary>
/// 进气管路与 IN 阀检测（Q）。PORT: TesInletandIn。
/// </summary>

/// <summary>
/// 进气管路与 IN 阀检测（Q）。PORT: TesInletandIn。
/// </summary>
public sealed class ConST860InletAndInValveQHandler : IStepHandler
{
    public string Kind => "TesInletandIn";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        var inletState = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetInletState", null, ct), "进气管路状态");
        var inValveState = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetValveState", new object[] { "IN" }, ct), "IN 阀状态");
        var pass = !string.IsNullOrWhiteSpace(inletState) && inletState != "Error"
                && !string.IsNullOrWhiteSpace(inValveState) && inValveState != "Error";
        return pass ? StepResult.Pass("进气管路与IN阀检测通过") : StepResult.Fail("进气管路与IN阀检测未通过");
    }
}

/// <summary>
/// 自整定（Q）。PORT: TestSelfTuning_Q。
/// </summary>

/// <summary>
/// 自整定（Q）。PORT: TestSelfTuning_Q。
/// </summary>
public sealed class ConST860SelfTuningQHandler : IStepHandler
{
    public string Kind => "TestSelfTuning_Q";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        if (op.DutQ is null) return StepResult.Error("当前驱动不支持气压扩展接口（IConST860PressureQBase）");

        var okStart = await op.TryCommand(() => op.DutQ.SelfTuningAsync(true, ct), "启动气压自整定");
        await op.Sleep(3000, "自整定运行等待");
        var result = await op.DutQ.ReadSelfTuningResultAsync(ct);
        op.Text("自整定结果", result);
        await op.TryCommand(() => op.DutQ.SelfTuningAsync(false, ct), "停止气压自整定");
        return okStart ? StepResult.Pass("气压自整定通过") : StepResult.Fail("气压自整定未通过");
    }
}

/// <summary>
/// 测量管路检测（Q）。PORT: TestMeassureLeak。
/// </summary>

/// <summary>
/// 测量管路检测（Q）。PORT: TestMeassureLeak。
/// </summary>
public sealed class ConST860MeasureLeakQHandler : IStepHandler
{
    public string Kind => "TestMeassureLeak";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        var leak = await op.Dut.MeasureLeakAsync(ct);
        op.Value("测量管路泄漏率", leak, "kPa/min");
        var pass = op.Judge("测量管路泄漏上限", leak, "测量管路泄漏", "kPa/min");
        return pass ? StepResult.Pass("测量管路检测通过") : StepResult.Fail("测量管路检测未通过");
    }
}

/// <summary>
/// 大气压检漏（Q）。PORT: TestATMLeak。
/// </summary>

/// <summary>
/// 大气压检漏（Q）。PORT: TestATMLeak。
/// </summary>
public sealed class ConST860ATMLeakQHandler : IStepHandler
{
    public string Kind => "TestATMLeak";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        var atm = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetATMPressureValue", null, ct), "大气压值");
        if (string.IsNullOrWhiteSpace(atm)) return StepResult.Fail("大气压检漏未通过：读取失败");

        if (double.TryParse(atm, NumberStyles.Float, CultureInfo.InvariantCulture, out var atmVal))
        {
            op.Value("大气压", atmVal, "kPa");
            var pass = op.Judge("大气压范围", atmVal, "大气压", "kPa");
            return pass ? StepResult.Pass("大气压检漏通过") : StepResult.Fail("大气压检漏未通过");
        }
        return StepResult.Fail("大气压检漏未通过：解析失败");
    }
}

/// <summary>
/// 板载大气压传感器检测（Q）。PORT: OnboardaAtmoTest。
/// </summary>

/// <summary>
/// 板载大气压传感器检测（Q）。PORT: OnboardaAtmoTest。
/// </summary>
public sealed class ConST860OnboardAtmoQHandler : IStepHandler
{
    public string Kind => "OnboardaAtmoTest";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        var onboard = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetOnboardAtmo", null, ct), "板载大气压传感器");
        if (string.IsNullOrWhiteSpace(onboard)) return StepResult.Fail("板载大气压传感器检测未通过");
        return StepResult.Pass("板载大气压传感器检测通过");
    }
}

/// <summary>
/// 参考气容检漏（Q）。PORT: TestREFAirCapacitor。
/// </summary>

/// <summary>
/// 参考气容检漏（Q）。PORT: TestREFAirCapacitor。
/// </summary>
public sealed class ConST860REFAirCapacitorQHandler : IStepHandler
{
    public string Kind => "TestREFAirCapacitor";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        var leak = await op.Dut.MeasureLeakAsync(ct);
        op.Value("参考气容泄漏率", leak, "kPa/min");
        var pass = op.Judge("参考气容泄漏上限", leak, "参考气容泄漏", "kPa/min");
        return pass ? StepResult.Pass("参考气容检漏通过") : StepResult.Fail("参考气容检漏未通过");
    }
}

/// <summary>
/// NTC 阀岛温度测试（Q）。PORT: TestValveTerminal。
/// </summary>

/// <summary>
/// NTC 阀岛温度测试（Q）。PORT: TestValveTerminal。
/// </summary>
public sealed class ConST860ValveTerminalQHandler : IStepHandler
{
    public string Kind => "TestValveTerminal";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        var temp = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetValveTerminalTemperature", null, ct), "NTC 阀岛温度");
        if (string.IsNullOrWhiteSpace(temp)) return StepResult.Fail("NTC阀岛温度测试未通过");
        if (double.TryParse(temp, NumberStyles.Float, CultureInfo.InvariantCulture, out var tv))
        {
            op.Value("NTC 温度", tv, "℃");
            var pass = op.Judge("阀岛温度范围", tv, "NTC 温度", "℃");
            return pass ? StepResult.Pass("NTC阀岛温度测试通过") : StepResult.Fail("NTC阀岛温度测试未通过");
        }
        return StepResult.Fail("NTC阀岛温度测试未通过：解析失败");
    }
}

/// <summary>
/// 控压单元风扇测试（Q）。PORT: FANTest_Q_KYU。
/// </summary>

/// <summary>
/// 控压单元风扇测试（Q）。PORT: FANTest_Q_KYU。
/// </summary>
public sealed class ConST860FanKYUQHandler : IStepHandler
{
    public string Kind => "FANTest_Q_KYU";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        var fanMode = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetFANConfig", null, ct), "控压单元风扇配置");
        if (string.IsNullOrWhiteSpace(fanMode)) return StepResult.Fail("控压单元风扇测试未通过");
        return StepResult.Pass("控压单元风扇测试通过");
    }
}

/// <summary>
/// 空载/带载控压测试（Q，四象限：低压负压/低压正压/高压负压/高压正压）。
/// PORT: PressureControl_KLN/KLP/KHN/KHP、TestPressureControl_LN/LP/HN/HP。
/// 通过 Settings 的 Load（NoLoad/Loaded）、Side（High/Low）、Sign（Positive/Negative）组合路由。
/// </summary>

/// <summary>
/// 空载/带载控压测试（Q，四象限：低压负压/低压正压/高压负压/高压正压）。
/// PORT: PressureControl_KLN/KLP/KHN/KHP、TestPressureControl_LN/LP/HN/HP。
/// 通过 Settings 的 Load（NoLoad/Loaded）、Side（High/Low）、Sign（Positive/Negative）组合路由。
/// </summary>
public sealed class ConST860PressureControlQHandler : IStepHandler
{
    public string Kind => "PressureControl";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);

        // 从 Settings 读取四象限配置；默认低压正压空载
        var load = ctx.Step.Settings.TryGetValue("Load", out var ld) ? ld : "NoLoad";       // NoLoad / Loaded
        var side = ctx.Step.Settings.TryGetValue("Side", out var sd) ? sd : "Low";          // High / Low
        var sign = ctx.Step.Settings.TryGetValue("Sign", out var sg) ? sg : "Positive";     // Positive / Negative

        // 默认目标压力表（kPa）
        var defaultTargets = new Dictionary<(string Side, string Sign), double>
        {
            { ("Low",  "Negative"), -80 },
            { ("Low",  "Positive"), 80 },
            { ("High", "Negative"), -2500 },
            { ("High", "Positive"), 2500 },
        };
        var targetKey = $"Target_{side}_{sign}";
        var target = double.TryParse(ctx.Step.Settings.TryGetValue(targetKey, out var tv)
            ? tv
            : defaultTargets[(side, sign)].ToString(NumberFormatInfo.InvariantInfo),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedTarget)
            ? parsedTarget
            : defaultTargets[(side, sign)];

        ctx.Report($"[{load}/{side}/{sign}] 设定目标: {target}kPa");

        // 设置目标压力（仿真驱动会跟随）
        var okSet = await op.TryCommand(
            () => op.Dut.QueryBooleanAsync("SetTargetPressure", new object[] { target.ToString(CultureInfo.InvariantCulture) }, ct),
            "设定目标压力");
        if (!okSet) return StepResult.Fail("控压未通过：设定目标失败");

        await op.Sleep(3000, "控压稳定等待");

        // 回读 PV/SV
        double pv, sv;
        if (op.DutQ is not null)
        {
            (pv, sv) = await op.DutQ.ReadPvSvAsync(ct);
        }
        else
        {
            pv = await op.Dut.MeasureAsync("OutputPressure", ct);
            sv = target;
        }
        op.Value("PV", pv, "kPa");
        op.Value("SV", sv, "kPa");

        // 判定偏差
        var deviation = Math.Abs(pv - sv);
        if (!op.Judge("控压允差", deviation, "PV-SV 偏差", "kPa"))
        {
            return StepResult.Fail("控压未通过");
        }

        // 泄压收尾
        await op.Dut.CloseRepairVentAsync(ct);
        return StepResult.Pass($"[{load}/{side}/{sign}] 控压通过");
    }
}

// ============================================================================
// Q 清单缺失补充（旧平台 Q/GW2 脚本中存在但此前遗漏的处理器）
// ============================================================================

/// <summary>
/// 24V 输出测试（Q）。PORT: Test24V。
/// </summary>

/// <summary>
/// 24V 输出测试（Q）。PORT: Test24V。
/// </summary>
public sealed class ConST860Test24VQHandler : IStepHandler
{
    public string Kind => "Test24V";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        var state = await op.TryQueryValue(() => op.Dut.QueryTextAsync("Get24VOutputState", null, ct), "24V输出状态");
        if (string.IsNullOrWhiteSpace(state)) return StepResult.Fail("24V输出测试未通过");
        op.Value("24V", state == "OK" ? 24 : 0, "V");
        return state == "OK" ? StepResult.Pass("24V输出测试通过") : StepResult.Fail("24V输出测试未通过");
    }
}

/// <summary>
/// DO 端口测试（Q）。PORT: TestDO。
/// </summary>

/// <summary>
/// DO 端口测试（Q）。PORT: TestDO。
/// </summary>
public sealed class ConST860TestDOQHandler : IStepHandler
{
    public string Kind => "TestDO";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        var ok = await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetExtendedInterfaceTest", new object[] { "Open" }, ct), "开启阀测试模式");
        if (!ok) return StepResult.Fail("DO端口测试未通过：开启测试模式失败");

        var doState = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetDOState", null, ct), "DO端口状态");
        op.Text("DO状态", doState);
        if (string.IsNullOrWhiteSpace(doState) || doState == "Error") return StepResult.Fail("DO端口测试未通过");
        return StepResult.Pass("DO端口测试通过");
    }
}

/// <summary>
/// 电磁阀接口测试（Q）。PORT: TestElectronValve。
/// </summary>

/// <summary>
/// 电磁阀接口测试（Q）。PORT: TestElectronValve。
/// </summary>
public sealed class ConST860TestElectronValveQHandler : IStepHandler
{
    public string Kind => "TestElectronValve";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        var ok = await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetExtendedInterfaceTest", new object[] { "Open" }, ct), "开启阀测试模式");
        if (!ok) return StepResult.Fail("电磁阀接口测试未通过：开启测试模式失败");

        var valveState = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetValveState", null, ct), "电磁阀状态");
        op.Text("电磁阀状态", valveState);
        if (string.IsNullOrWhiteSpace(valveState) || valveState == "Error") return StepResult.Fail("电磁阀接口测试未通过");
        return StepResult.Pass("电磁阀接口测试通过");
    }
}

/// <summary>
/// Switch 测试（Q）。PORT: TestSwitch。
/// </summary>

/// <summary>
/// Switch 测试（Q）。PORT: TestSwitch。
/// </summary>
public sealed class ConST860TestSwitchQHandler : IStepHandler
{
    public string Kind => "TestSwitch";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        var state = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetSwitchState", null, ct), "Switch状态");
        op.Text("Switch状态", state);
        if (string.IsNullOrWhiteSpace(state) || state == "Error") return StepResult.Fail("Switch测试未通过");
        return StepResult.Pass("Switch测试通过");
    }
}

/// <summary>
/// 设备自检测试（Q）。PORT: TestSelfCheckEXCeption。
/// </summary>

/// <summary>
/// 设备自检测试（Q）。PORT: TestSelfCheckEXCeption。
/// </summary>
public sealed class ConST860TestSelfCheckExceptionQHandler : IStepHandler
{
    public string Kind => "TestSelfCheckEXCeption";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        var exception = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetSelfCheckException", null, ct), "设备自检异常");
        if (!string.IsNullOrEmpty(exception))
        {
            op.Fail($"设备自检存在异常：{exception}");
            return StepResult.Fail("设备自检测试未通过");
        }
        return StepResult.Pass("设备自检测试通过");
    }
}

/// <summary>
/// 舱门泄压测试（Q）。PORT: FrontDoorTest_Q。
/// </summary>

/// <summary>
/// 舱门泄压测试（Q）。PORT: FrontDoorTest_Q。
/// </summary>
public sealed class ConST860FrontDoorQHandler : IStepHandler
{
    public string Kind => "FrontDoorTest_Q";
    public string? DeviceFamily => "ConST860_SelfCheck_Q_GW2";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        var doorState = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetDoorState", null, ct), "舱门状态");
        op.Text("舱门状态", doorState);
        if (string.IsNullOrWhiteSpace(doorState) || doorState == "Open") return StepResult.Fail("舱门泄压测试未通过");
        return StepResult.Pass("舱门泄压测试通过");
    }
}

