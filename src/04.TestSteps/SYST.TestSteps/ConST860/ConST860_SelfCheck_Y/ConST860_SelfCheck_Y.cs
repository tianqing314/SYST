using System.Globalization;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;
using SYST.Devices.Abstractions.Dut;

namespace SYST.TestSteps.ConST860.ConST860_SelfCheck_Y;

// ============================================================================
// ConST860_SelfCheck_Y 处理器集合（清单 Key=ConST860_SelfCheck_Y）。逻辑见 Shared/ConST860Ops。
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
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

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
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

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
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

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
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

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
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

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
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

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
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

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
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

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
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

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
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

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
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

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
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

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
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

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
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

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
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

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
/// 内置模块通讯测试（Y）。PORT: TestInnerModule。
/// </summary>
public sealed class ConST860InnerModuleYHandler : IStepHandler
{
    public string Kind => "TestInnerModule";
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST860Dut>();
        var info = await dut.QueryTextAsync("GetInnerModuleInfo", null, ct);
        ctx.Report($"内置模块信息: {info}");
        var ok = !string.IsNullOrWhiteSpace(info) && info != "Error";
        return ok ? StepResult.Pass("内置模块通讯测试通过") : StepResult.Fail("内置模块通讯测试未通过");
    }
}

/// <summary>
/// 液压外循环测试（Y）。PORT: ExternalLoopTest。
/// </summary>

/// <summary>
/// 液压外循环测试（Y）。PORT: ExternalLoopTest。
/// </summary>
public sealed class ConST860ExternalLoopYHandler : IStepHandler
{
    public string Kind => "ExternalLoopTest";
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        if (op.DutY is null)
        {
            return StepResult.Error("当前驱动不支持液压扩展接口（IConST860PressureYGbk）");
        }

        // 启动外循环 → 等待 → 回读状态
        if (!await op.TryCommand(() => op.DutY.SetPumpSpeedAsync(80, ct), "液泵调速至80%"))
            return StepResult.Fail("液压外循环测试未通过：泵调速失败");

        await op.Sleep(3000, "外循环运行等待");

        var active = await op.DutY.GetExternalLoopStateAsync(ct);
        op.Value("外循环状态", active ? 1 : 0);

        // 关闭泵
        await op.DutY.SetPumpSpeedAsync(0, ct);

        return active
            ? StepResult.Pass("液压外循环测试通过")
            : StepResult.Fail("液压外循环测试未通过：外循环未激活");
    }
}

/// <summary>
/// 高压泄露测试（Y）。PORT: HeightTest。
/// </summary>

/// <summary>
/// 高压泄露测试（Y）。PORT: HeightTest。
/// </summary>
public sealed class ConST860HeightLeakYHandler : IStepHandler
{
    public string Kind => "HeightTest";
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        var pass = true;

        // 充压到高压
        var charged = await op.Dut.ChargeAsync('H', ct);
        op.Value("充压至", charged, "kPa");

        await op.Sleep(2000, "稳压等待");

        // 测量泄漏
        var leak = await op.Dut.MeasureLeakAsync(ct);
        op.Value("高压泄露量", leak, "kPa/min");

        if (!op.Judge("高压泄露量", leak, "高压泄露", "kPa/min"))
        {
            pass = false;
        }

        // 泄压
        await op.Dut.CloseRepairVentAsync(ct);

        return pass ? StepResult.Pass("高压泄露测试通过") : StepResult.Fail("高压泄露测试未通过");
    }
}

/// <summary>
/// 控压测试（Y，带载）。PORT: TestPressureControl_CompleteTest。
/// </summary>

/// <summary>
/// 控压测试（Y，带载）。PORT: TestPressureControl_CompleteTest。
/// </summary>
public sealed class ConST860PressureControlCompleteYHandler : IStepHandler
{
    public string Kind => "TestPressureControl_CompleteTest";
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        if (op.DutQ is null && op.Dut is not IConST860PressureQBase q)
        {
            // Y 变体的控压走基础 SetTargetPressure + MeasureLeak 组合
        }

        var targetStr = ctx.Step.Settings.TryGetValue("TargetKpa", out var t) ? t : "2500";
        if (!double.TryParse(targetStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var target))
            target = 2500;

        ctx.Report($"设定目标压力: {target}kPa");
        var okSet = await op.TryCommand(
            () => ctx.GetDevice<IConST860Dut>().QueryBooleanAsync("SetTargetPressure", new object[] { target.ToString() }, ct),
            "设定目标压力");
        if (!okSet) return StepResult.Fail("控压测试未通过：设定目标压力失败");

        await op.Sleep(3000, "控压稳定等待");

        var actual = await ctx.GetDevice<IConST860Dut>().MeasureAsync("OutputPressure", ct);
        op.Value("实际输出压力", actual, "kPa");

        if (!op.Judge("控压允差", Math.Abs(actual - target), "控压偏差", "kPa"))
        {
            return StepResult.Fail("控压测试未通过");
        }

        // 泄压收尾
        await ctx.GetDevice<IConST860Dut>().CloseRepairVentAsync(ct);
        return StepResult.Pass("控压测试通过");
    }
}

/// <summary>
/// 自整定后阀参数回读（Y）。PORT: ReadSelfTuningPar。
/// </summary>

/// <summary>
/// 自整定后阀参数回读（Y）。PORT: ReadSelfTuningPar。
/// </summary>
public sealed class ConST860ReadSelfTuningParYHandler : IStepHandler
{
    public string Kind => "ReadSelfTuningPar";
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        var par = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetControlValveParams", null, ct), "自整定阀参数");
        if (string.IsNullOrWhiteSpace(par)) return StepResult.Fail("自整定后阀参数读取失败");
        return StepResult.Pass("自整定后阀参数读取通过");
    }
}

/// <summary>
/// 系统泄压（Y）。PORT: TestRepairVent。
/// </summary>

/// <summary>
/// 系统泄压（Y）。PORT: TestRepairVent。
/// </summary>
public sealed class ConST860RepairVentYHandler : IStepHandler
{
    public string Kind => "TestRepairVent";
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        await op.Dut.CloseRepairVentAsync(ct);
        await op.Sleep(2000, "泄压等待");
        var pressure = await op.Dut.MeasureAsync("OutputPressure", ct);
        op.Value("泄压后压力", pressure, "kPa");
        if (!op.Judge("泄压后压力上限", pressure, "泄压后压力", "kPa"))
        {
            return StepResult.Fail("系统泄压未通过");
        }
        return StepResult.Pass("系统泄压通过");
    }
}

/// <summary>
/// 舱门泄压测试（Y）。PORT: FrontDoorTest。
/// </summary>

/// <summary>
/// 舱门泄压测试（Y）。PORT: FrontDoorTest。
/// </summary>
public sealed class ConST860FrontDoorYHandler : IStepHandler
{
    public string Kind => "FrontDoorTest";
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var dut = ctx.GetDevice<IConST860Dut>();
        var doorState = await dut.QueryTextAsync("GetDoorState", null, ct);
        ctx.Report($"舱门状态: {doorState}");
        var ok = doorState == "Closed" || doorState == "OK";
        return ok ? StepResult.Pass("舱门泄压测试通过") : StepResult.Fail("舱门泄压测试未通过");
    }
}

/// <summary>
/// 蓄能器充压检测（Y）。PORT: TestEnergy。
/// </summary>

/// <summary>
/// 蓄能器充压检测（Y）。PORT: TestEnergy。
/// </summary>
public sealed class ConST860EnergyAccumulatorYHandler : IStepHandler
{
    public string Kind => "TestEnergy";
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        // 快速充满蓄能器
        var charged = await op.Dut.ChargeAsync('#', ct);
        op.Value("蓄能器充压", charged, "kPa");
        if (!op.Judge("蓄能器压力下限", charged, "蓄能器压力", "kPa"))
        {
            return StepResult.Fail("蓄能器充压检测未通过");
        }
        await op.Dut.CloseRepairVentAsync(ct);
        return StepResult.Pass("蓄能器充压检测通过");
    }
}

/// <summary>
/// 模块清零测试（Y）。PORT: TestClearPressure。
/// </summary>

/// <summary>
/// 模块清零测试（Y）。PORT: TestClearPressure。
/// </summary>
public sealed class ConST860ClearPressureYHandler : IStepHandler
{
    public string Kind => "TestClearPressure";
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        var ok = await op.TryCommand(() =>
            op.Dut.QueryBooleanAsync("ClearPressure", new object[] { "InternalHigh" }, ct), "S1 模块清零");
        var ok2 = await op.TryCommand(() =>
            op.Dut.QueryBooleanAsync("ClearPressure", new object[] { "InternalLow" }, ct), "S2 模块清零");
        return ok && ok2 ? StepResult.Pass("模块清零测试通过") : StepResult.Fail("模块清零测试未通过");
    }
}

/// <summary>
/// 液源模块自校准（Y）。PORT: TestCalibrationSensor。
/// </summary>

/// <summary>
/// 液源模块自校准（Y）。PORT: TestCalibrationSensor。
/// </summary>
public sealed class ConST860CalibrationSensorYHandler : IStepHandler
{
    public string Kind => "TestCalibrationSensor";
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        if (op.DutY is null)
        {
            return StepResult.Error("当前驱动不支持液压扩展接口（IConST860PressureYGbk）");
        }
        var okStart = await op.TryCommand(() => op.DutY.RunCalibrationAsync(true, ct), "启动液源校准");
        await op.Sleep(2000, "校准运行等待");
        var okStop = await op.TryCommand(() => op.DutY.RunCalibrationAsync(false, ct), "停止液源校准");
        return okStart && okStop
            ? StepResult.Pass("液源模块自校准通过")
            : StepResult.Fail("液源模块自校准未通过");
    }
}

/// <summary>
/// 液体泵效率测试（Y）。PORT: BumpTest。
/// </summary>

/// <summary>
/// 液体泵效率测试（Y）。PORT: BumpTest。
/// </summary>
public sealed class ConST860PumpEfficiencyYHandler : IStepHandler
{
    public string Kind => "BumpTest";
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        if (op.DutY is null)
        {
            return StepResult.Error("当前驱动不支持液压扩展接口（IConST860PressureYGbk）");
        }
        var eff = await op.DutY.PumpEfficiencyTestAsync(ct);
        op.Value("液体泵效率", eff, "%");
        if (!op.Judge("泵效率下限", eff, "液体泵效率", "%"))
        {
            return StepResult.Fail("液体泵效率测试未通过");
        }
        return StepResult.Pass("液体泵效率测试通过");
    }
}

/// <summary>
/// 液体泵电机调速（Y）。PORT: ETest。
/// </summary>

/// <summary>
/// 液体泵电机调速（Y）。PORT: ETest。
/// </summary>
public sealed class ConST860PumpSpeedYHandler : IStepHandler
{
    public string Kind => "ETest";
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        if (op.DutY is null)
        {
            return StepResult.Error("当前驱动不支持液压扩展接口（IConST860PressureYGbk）");
        }
        foreach (var speed in new[] { 30, 60, 90 })
        {
            if (!await op.TryCommand(() => op.DutY.SetPumpSpeedAsync(speed, ct), $"泵速调至{speed}%"))
                return StepResult.Fail("液体泵电机调速未通过");
            await op.Sleep(1500, "转速稳定等待");
            var rpm = await op.Dut.GetPumpRpmAsync(ct);
            op.Value($"{speed}% 转速", rpm, "rpm");
            if (!op.Judge("电机转速下限", rpm, $"{speed}% 转速", "rpm"))
            {
                return StepResult.Fail("液体泵电机调速未通过");
            }
        }
        // 收尾归零
        await op.DutY.SetPumpSpeedAsync(0, ct);
        return StepResult.Pass("液体泵电机调速通过");
    }
}

/// <summary>
/// 自整定（Y）。PORT: TestSelfTuning。
/// </summary>

/// <summary>
/// 自整定（Y）。PORT: TestSelfTuning。
/// </summary>
public sealed class ConST860SelfTuningYHandler : IStepHandler
{
    public string Kind => "TestSelfTuning";
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        if (op.DutQ is null)
        {
            // Y 变体自整定走通用指令
            var okCmd = await op.TryCommand(() =>
                op.Dut.QueryBooleanAsync("StartSelfTuning", new object[] { "3721" }, ct), "启动自整定(Y-通用)");
            return okCmd ? StepResult.Pass("自整定通过") : StepResult.Fail("自整定未通过");
        }

        var okStart = await op.TryCommand(() => op.DutQ.SelfTuningAsync(true, ct), "启动自整定");
        await op.Sleep(3000, "自整定运行等待");
        var result = await op.DutQ.ReadSelfTuningResultAsync(ct);
        op.Text("自整定结果", result);
        await op.TryCommand(() => op.DutQ.SelfTuningAsync(false, ct), "停止自整定");
        return okStart ? StepResult.Pass("自整定通过") : StepResult.Fail("自整定未通过");
    }
}

/// <summary>
/// 低压泄露测试（Y）。PORT: LowTest。
/// </summary>

/// <summary>
/// 低压泄露测试（Y）。PORT: LowTest。
/// </summary>
public sealed class ConST860LowLeakYHandler : IStepHandler
{
    public string Kind => "LowTest";
    public string? DeviceFamily => "ConST860_SelfCheck_Y";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST860Ops(ctx, ct);
        var charged = await op.Dut.ChargeAsync('L', ct);
        op.Value("充压至", charged, "kPa");
        await op.Sleep(2000, "稳压等待");
        var leak = await op.Dut.MeasureLeakAsync(ct);
        op.Value("低压泄露量", leak, "kPa/min");
        var pass = op.Judge("低压泄露量", leak, "低压泄露", "kPa/min");
        await op.Dut.CloseRepairVentAsync(ct);
        return pass ? StepResult.Pass("低压泄露测试通过") : StepResult.Fail("低压泄露测试未通过");
    }
}

// ============================================================================
// 气压（Q）专属步骤
// ============================================================================

/// <summary>
/// 内置模块功能测试（Q）。PORT: TestInnerModule_Q。
/// </summary>

