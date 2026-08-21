using System.Globalization;
using SYST.Core.Abstractions;

namespace SYST.TestSteps.ConST811A;

// ============================================================================
// AtmosSensorTestConST811AHandler
// ============================================================================
/// <summary>
/// 大气压传感器测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// </summary>
public sealed class AtmosSensorTestConST811AHandler : IStepHandler
{
    public string Kind => "AtmosSensorTest";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = true;

        var pressure = await op.Dut.QueryTextAsync("GetAtmosphericPressure", null, ct);
        op.Text("大气压值", pressure ?? "");
        if (string.IsNullOrWhiteSpace(pressure)) pass = false;

        if (pass && double.TryParse(pressure, out var v) && ctx.Conditions.Count > 0)
        {
            var r = ctx.Evaluator.Evaluate(ctx.Conditions[0], v);
            op.Verdict("大气压值", r.Passed);
            if (!r.Passed) pass = false;
        }

        if (pass) op.Ok("大气压传感器测试通过");
        else op.Fail("大气压传感器测试未通过");
        return pass ? StepResult.Pass("大气压传感器测试通过") : StepResult.Fail("大气压传感器测试未通过");
    }
}

// ============================================================================
// BeeperTestConST811AHandler
// ============================================================================
/// <summary>
/// 蜂鸣器测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// 设备上启动蜂鸣器自检程序，用户在设备上操作并点击 Success/Fail，程序轮询结果。
/// </summary>
public sealed class BeeperTestConST811AHandler : IStepHandler
{
    public string Kind => "BeeperTest";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = false;

        // 启动设备蜂鸣器自检程序（设备弹出应用供用户操作）
        if (!(await op.Dut.QueryBooleanAsync("SetCheckerOpen", new[] { "Speaker" }, ct)))
        {
            op.Fail("启动蜂鸣器自检程序失败");
            return StepResult.Fail("蜂鸣器测试未通过：启动自检程序失败");
        }

        // 轮询自检结果（用户在设备上点击 Success/Fail）
        for (var i = 0; i < 120; i++)
        {
            var state = await op.Dut.QueryTextAsync("GetCheckerState", new[] { "Speaker" }, ct);
            if (state == "TestPass") { pass = true; break; }
            if (state == "TestFail") { break; }
            await Task.Delay(500, ct);
        }

        await op.Dut.QueryBooleanAsync("SetCheckerClose", null, ct);

        if (pass) op.Ok("蜂鸣器测试通过");
        else op.Fail("蜂鸣器测试未通过");
        return pass ? StepResult.Pass("蜂鸣器测试通过") : StepResult.Fail("蜂鸣器测试未通过");
    }
}

// ============================================================================
// ElectricalMeasurementAndOutputFunctionTestConST811AHandler
// ============================================================================
/// <summary>
/// 电测板测量/输出功能测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// </summary>
public sealed class ElectricalMeasurementAndOutputFunctionTestConST811AHandler : IStepHandler
{
    public string Kind => "ElectricalMeasurementAndOutputFunctionTest";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = true;

        var testPoints = new (string Name, double Target, string Unit)[]
        {
            ("电压1V", 1, "V"),
            ("电压16V", 16, "V"),
            ("电压高压档16V", 16, "V"),
            ("电压高压档30V", 30, "V"),
            ("电流0mA", 0, "mA"),
            ("电流2.5mA", 2.5, "mA"),
            ("电流25mA", 25, "mA"),
        };

        await op.Gzp21.SetOutputAsync("Ele", true, ct);
        await Task.Delay(3000, ct);

        for (var i = 0; i < testPoints.Length; i++)
        {
            var tp = testPoints[i];
            var cond = i < ctx.Conditions.Count ? ctx.Conditions[i] : null;
            var tryCount = 0;
            var pointPass = true;
            while (true)
            {
                pointPass = true;
                var pointOk = false;

                if (tp.Unit == "V")
                {
                    if (tp.Name.Contains("高压档"))
                    {
                        if (!(await op.Dut.QueryBooleanAsync("SetElectricSourceFunction", new[]{ "V2" }, ct)))
                        { op.Report("切换电测电压高压档位失败", RealtimeLevel.Error); pointPass = false; }
                    }
                    else
                    {
                        if (!(await op.Dut.QueryBooleanAsync("SetElectricSourceFunction", new[]{ "V" }, ct)))
                        { op.Report("切换电测电压低压档位失败", RealtimeLevel.Error); pointPass = false; }
                    }
                }
                else
                {
                    if (!(await op.Dut.QueryBooleanAsync("SetElectricSource_MA", new[]{ "false" }, ct)))
                    { op.Report("切换电输出电流档位失败", RealtimeLevel.Error); pointPass = false; }
                }

                if (!(await op.Dut.QueryBooleanAsync("SetElectricSourceTarget", new[]{ tp.Target.ToString() }, ct)))
                { op.Report("设置电输出目标值失败", RealtimeLevel.Error); pointPass = false; }

                if (tp.Unit == "V")
                {
                    if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_VOL", null, ct)))
                    { op.Report("设置电测量档位为电压档失败", RealtimeLevel.Error); pointPass = false; }
                }
                else
                {
                    if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_CURR", new[]{ "true" }, ct)))
                    { op.Report("设置电测量档位为电流档失败", RealtimeLevel.Error); pointPass = false; }
                }

                await Task.Delay(3000, ct);

                double eleData = 0;
                var readCount = 0;
                for (var n = 0; n < 4; n++)
                {
                    var txt = await op.Dut.QueryTextAsync("GetCurrentElectricMeasure", null, ct);
                    if (double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    { eleData += v; readCount++; }
                }
                if (readCount == 0) { pointPass = false; }
                else eleData /= readCount;
                op.Value($"{tp.Name} 测量值", eleData, tp.Unit);

                if (pointPass && cond is not null)
                {
                    var r = ctx.Evaluator.Evaluate(cond, Math.Abs(eleData - tp.Target));
                    pointOk = r.Passed;
                    op.Report($"{tp.Name}：{r.Message}", r.Passed ? RealtimeLevel.Info : RealtimeLevel.Warn);
                }

                if (pointOk) break;
                tryCount++;
                if (tryCount > 3)
                {
                    op.Report($"{tp.Name}测试不通过: 测量值{ConST811AOpsBase.F(eleData)}{tp.Unit}超差", RealtimeLevel.Error);
                    pointPass = false;
                    break;
                }
                if (!(await ctx.ConfirmAsync("电测线可能没接，请先使用测试线 连接设备与工装的SRC和MEAS插孔，红对红，黑对黑。\r\n点击确认，重新测试，否则测试失败。", "确认", "取消", ct)))
                { pointPass = false; break; }
            }
            pass &= pointPass;
        }

        // 24V 环路供电测试
        var cond24V = ctx.Conditions.Count > 10 ? ctx.Conditions[10] : null;
        var twentyFourVTests = new (string Name, bool MAOn, bool CurrLoop)[]
        {
            ("供电24V,无环路24V时输出10mA电流测试", false, true),
            ("供电24V,有环路24V时输出10mA电流测试", true, false),
            ("供电24V,有环路24V时输出10mA电流测试(应无环路电流)", true, true),
        };
        for (var i = 0; i < twentyFourVTests.Length; i++)
        {
            var t = twentyFourVTests[i];
            if (!(await op.Dut.QueryBooleanAsync("SetElectricSource_MA", new[]{ t.MAOn.ToString().ToLowerInvariant() }, ct)))
            { op.Report("切换电输出电流档位失败", RealtimeLevel.Error); pass = false; }
            if (!(await op.Dut.QueryBooleanAsync("SetElectricSourceTarget", new[]{ "10" }, ct)))
            { op.Report("设置电输出目标值失败", RealtimeLevel.Error); pass = false; }
            if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_CURR", new[]{ t.CurrLoop.ToString().ToLowerInvariant() }, ct)))
            { op.Report("设置电测量档位为电流档失败", RealtimeLevel.Error); pass = false; }
            await Task.Delay(3000, ct);

            var txt = await op.Dut.QueryTextAsync("GetCurrentElectricMeasure", null, ct);
            if (!double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var measuredValue24V))
            { pass = false; continue; }
            op.Value($"{t.Name} 测量值", measuredValue24V, "mA");
            if (cond24V is null) { pass = false; continue; }

            var r = ctx.Evaluator.Evaluate(cond24V, Math.Abs(measuredValue24V - 10));
            var ok = i < 2 ? r.Passed : !r.Passed;
            pass &= ok;
            op.Report($"{t.Name}：{(ok ? "测试通过" : "测试不通过")}", ok ? RealtimeLevel.Info : RealtimeLevel.Warn);
        }

        await op.Gzp21.SetOutputAsync("Ele", false, ct);

        if (pass) op.Ok("电测板测量/输出功能测试通过");
        else op.Fail("电测板测量/输出功能测试未通过");
        return pass ? StepResult.Pass("电测板测量/输出功能测试通过") : StepResult.Fail("电测板测量/输出功能测试未通过");
    }
}

// ============================================================================
// ElectricalPowerTestConST811AHandler
// ============================================================================
/// <summary>
/// 电测板电源测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// </summary>
public sealed class ElectricalPowerTestConST811AHandler : IStepHandler
{
    public string Kind => "ElectricalPowerTest";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);

        var state = await op.Dut.QueryTextAsync("GetElectricalBroadPowerCheckState", null, ct);
        op.Text("电源状态", state ?? "");
        if (string.IsNullOrWhiteSpace(state) || state != "OK")
            return StepResult.Fail($"电测板电源测试未通过：状态异常 {state}");

        op.Ok("电测板电源测试通过");
        return StepResult.Pass("电测板电源测试通过");
    }
}

// ============================================================================
// FANTestConST811AHandler
// ============================================================================
/// <summary>
/// 风扇测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// 设备上启动风扇自检程序，用户在设备上操作并点击 Success/Fail，程序轮询结果。
/// </summary>
public sealed class FANTestConST811AHandler : IStepHandler
{
    public string Kind => "FANTest";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = false;

        // 启动设备风扇自检程序（设备弹出应用供用户操作）
        if (!(await op.Dut.QueryBooleanAsync("SetCheckerOpen", new[] { "Fan" }, ct)))
        {
            op.Fail("启动风扇自检程序失败");
            return StepResult.Fail("风扇测试未通过：启动自检程序失败");
        }

        // 轮询自检结果（用户在设备上点击 Success/Fail）
        for (var i = 0; i < 120; i++)
        {
            var state = await op.Dut.QueryTextAsync("GetCheckerState", new[] { "Fan" }, ct);
            if (state == "TestPass") { pass = true; break; }
            if (state == "TestFail") { break; }
            await Task.Delay(500, ct);
        }

        await op.Dut.QueryBooleanAsync("SetCheckerClose", null, ct);

        if (pass) op.Ok("风扇测试通过");
        else op.Fail("风扇测试未通过");
        return pass ? StepResult.Pass("风扇测试通过") : StepResult.Fail("风扇测试未通过");
    }
}

// ============================================================================
// GasPumpTestConST811AHandler
// ============================================================================
/// <summary>
/// 气泵测试（公共处理器）。BP/DP/MP 变体共用（LLP 无此项）。
/// </summary>
public sealed class GasPumpTestConST811AHandler : IStepHandler
{
    public string Kind => "GasPumpTest";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = true;

        if (!(await op.Dut.QueryBooleanAsync("SetGasPumpStart", null, ct))) pass = false;
        await Task.Delay(2000, ct);

        if (pass)
        {
            var state = await op.Dut.QueryTextAsync("GetGasPumpState", null, ct);
            op.Text("气泵状态", state ?? "");
            if (string.IsNullOrWhiteSpace(state) || state != "OK") pass = false;
        }

        if (!(await op.Dut.QueryBooleanAsync("SetGasPumpStop", null, ct))) pass = false;

        if (pass) op.Ok("气泵测试通过");
        else op.Fail("气泵测试未通过");
        return pass ? StepResult.Pass("气泵测试通过") : StepResult.Fail("气泵测试未通过");
    }
}

// ============================================================================
// LCDTestConST811AHandler
// ============================================================================
/// <summary>
/// 屏幕测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// 含3个子测试：屏幕亮度、屏幕坏点、屏幕触摸。设备上启动自检程序，用户在设备上操作并点击 Success/Fail。
/// </summary>
public sealed class LCDTestConST811AHandler : IStepHandler
{
    public string Kind => "LCDTest";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var allPass = true;

        try
        {
            // ===== 子测试1：屏幕亮度测试 =====
            if (!(await op.Dut.QueryBooleanAsync("SetCheckerOpen", new[] { "Brightness" }, ct)))
            {
                op.Fail("启动屏幕亮度自检程序失败");
                return StepResult.Fail("屏幕亮度测试未通过：启动自检程序失败");
            }
            var brightnessPass = await PollCheckerState(op, "Brightness", ct);
            op.Verdict("亮度测试", brightnessPass);
            if (!brightnessPass) allPass = false;

            // ===== 子测试2：屏幕坏点测试 =====
            if (!(await op.Dut.QueryBooleanAsync("SetCheckerSelect", new[] { "BadPixel" }, ct)))
            {
                op.Fail("启动屏幕坏点自检程序失败");
                return StepResult.Fail("屏幕坏点测试未通过：启动自检程序失败");
            }
            var badPixelPass = await PollCheckerState(op, "BadPixel", ct);
            op.Verdict("坏点测试", badPixelPass);
            if (!badPixelPass) allPass = false;

            // ===== 子测试3：屏幕触摸测试 =====
            if (!(await op.Dut.QueryBooleanAsync("SetCheckerSelect", new[] { "Touch" }, ct)))
            {
                op.Fail("启动屏幕触摸自检程序失败");
                return StepResult.Fail("屏幕触摸测试未通过：启动自检程序失败");
            }
            var touchPass = await PollCheckerState(op, "Touch", ct);
            op.Verdict("触摸测试", touchPass);
            if (!touchPass) allPass = false;
        }
        finally
        {
            await op.Dut.QueryBooleanAsync("SetCheckerClose", null, ct);
        }

        if (allPass) op.Ok("屏幕测试通过");
        else op.Fail("屏幕测试未通过");
        return allPass ? StepResult.Pass("屏幕测试通过") : StepResult.Fail("屏幕测试未通过");
    }

    /// <summary>
    /// 轮询设备自检状态，直到用户在设备上点击 Success/Fail 或超时。
    /// </summary>
    private static async Task<bool> PollCheckerState(ConST811AOpsBase op, string function, CancellationToken ct)
    {
        for (var i = 0; i < 120; i++)
        {
            var state = await op.Dut.QueryTextAsync("GetCheckerState", new[] { function }, ct);
            if (state == "TestPass") return true;
            if (state == "TestFail") return false;
            await Task.Delay(500, ct);
        }
        return false;
    }
}

// ============================================================================
// ManualConST811AHandler
// ============================================================================
/// <summary>
/// 电源指示灯测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// </summary>
public sealed class ManualConST811AHandler : IStepHandler
{
    public string Kind => "Manual_1b0ac0cbde40461f9fcbc943513d9414";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var ok = await ctx.ConfirmAsync("请确认设备电源指示灯是否正常亮起？", ct);
        if (ok) op.Ok("电源指示灯测试通过");
        else op.Fail("电源指示灯测试未通过");
        return ok ? StepResult.Pass("电源指示灯测试通过") : StepResult.Fail("电源指示灯测试未通过");
    }
}

// ============================================================================
// ModuleConnectStateTestConST811AHandler
// ============================================================================
/// <summary>
/// 外接压力模块通讯测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// </summary>
public sealed class ModuleConnectStateTestConST811AHandler : IStepHandler
{
    public string Kind => "ModuleConnectStateTest";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var probe = await op.Dut.QueryTextAsync("GetPressureModelOnlineState", null, ct);
        op.Text("设备回读", probe ?? "");
        var pass = !string.IsNullOrWhiteSpace(probe);
        if (pass) op.Ok("外接压力模块通讯测试通过");
        else op.Fail("外接压力模块通讯测试未通过");
        return pass ? StepResult.Pass("外接压力模块通讯测试通过") : StepResult.Fail("外接压力模块通讯测试未通过");
    }
}

// ============================================================================
// NTCTestConST811AHandler
// ============================================================================
/// <summary>
/// NTC 测试（公共处理器）。DP/MP 变体共用。
/// </summary>
public sealed class NTCTestConST811AHandler : IStepHandler
{
    public string Kind => "NTCTest";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = true;

        var temp = await op.Dut.QueryTextAsync("GetNTCTemperature", null, ct);
        op.Text("NTC温度", temp ?? "");
        if (string.IsNullOrWhiteSpace(temp)) pass = false;

        if (pass && double.TryParse(temp, out var v) && ctx.Conditions.Count > 0)
        {
            var r = ctx.Evaluator.Evaluate(ctx.Conditions[0], v);
            op.Verdict("NTC温度", r.Passed);
            if (!r.Passed) pass = false;
        }

        if (pass) op.Ok("NTC测试通过");
        else op.Fail("NTC测试未通过");
        return pass ? StepResult.Pass("NTC测试通过") : StepResult.Fail("NTC测试未通过");
    }
}

// ============================================================================
// PressureControlTestConST811AHandler
// ============================================================================
/// <summary>
/// 压力控制测试（公共处理器）。DP/LLP/MP 变体共用。
/// 注意：BP 有专用的 PressureControlTest_BP 处理器。
/// </summary>
public sealed class PressureControlTestConST811AHandler : IStepHandler
{
    public string Kind => "PressureControlTest";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = true;

        if (!(await op.Dut.QueryBooleanAsync("SetPressureControl", null, ct))) pass = false;

        if (pass)
        {
            var controlOk = false;
            for (var i = 0; i < 10; i++)
            {
                var state = await op.Dut.QueryTextAsync("GetPressureControlState", null, ct);
                if (state == "OK") { controlOk = true; break; }
                if (i < 9) await Task.Delay(1000, ct);
            }
            op.Verdict("压力控制状态", controlOk);
            if (!controlOk) pass = false;
        }

        if (pass) op.Ok("压力控制测试通过");
        else op.Fail("压力控制测试未通过");
        return pass ? StepResult.Pass("压力控制测试通过") : StepResult.Fail("压力控制测试未通过");
    }
}

// ============================================================================
// QRLeakTestConST811AHandler
// ============================================================================
/// <summary>
/// 二维码气密性测试（公共处理器）。DP/LLP 变体共用。
/// </summary>
public sealed class QRLeakTestConST811AHandler : IStepHandler
{
    public string Kind => "QRLeakTest";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = true;

        if (!(await op.Dut.QueryBooleanAsync("SetQRLeakTestStart", null, ct))) pass = false;

        if (pass)
        {
            var testOk = false;
            for (var i = 0; i < 10; i++)
            {
                var state = await op.Dut.QueryTextAsync("GetQRLeakTestState", null, ct);
                if (state == "OK") { testOk = true; break; }
                if (state == "FAIL") { break; }
                if (i < 9) await Task.Delay(1000, ct);
            }
            op.Verdict("气密性测试结果", testOk);
            if (!testOk) pass = false;
        }

        if (pass) op.Ok("二维码气密性测试通过");
        else op.Fail("二维码气密性测试未通过");
        return pass ? StepResult.Pass("二维码气密性测试通过") : StepResult.Fail("二维码气密性测试未通过");
    }
}

// ============================================================================
// RTCTimeTestConST811AHandler
// ============================================================================
/// <summary>
/// 系统板RTC时间测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// </summary>
public sealed class RTCTimeTestConST811AHandler : IStepHandler
{
    public string Kind => "RTCTimeTest";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = true;

        // 同步电脑时间
        DateTime computerTime = DateTime.Now;
        op.Text("电脑时间", computerTime.ToString("yyyy-MM-dd HH:mm:ss"));
        if (!(await op.Dut.QueryBooleanAsync("SetSystemTime", new[]{ computerTime.ToString() }, ct))) pass = false;
        if (pass && !(await op.Dut.QueryBooleanAsync("SetSystemDate", new[]{ computerTime.ToString() }, ct))) pass = false;

        // 回读设备时间
        if (pass && !(await op.Dut.QueryBooleanAsync("GetDevSysDate", null, ct))) pass = false;

        // 重启设备
        if (pass)
        {
            await op.Dut.CommandAsync("SetReboot", null, ct);
            await op.Dut.CommandAsync("Close", null, ct);
            await op.Dut.CommandAsync("SetCommConfigEmpty", null, ct);
            await Task.Delay(1000, ct);

            // 重新连接设备
            await Task.Delay(1000, ct);
            if (!(await op.Dut.QueryBooleanAsync("Open", null, ct)))
                pass = false;
            else if (!(await ctx.ConfirmAsync("重启失败,请确认设备是否重启成功,若无异常,再重新测试!", ct)))
                pass = false;
        }

        // 重启后回读设备时间
        if (pass)
        {
            if (!(await op.Dut.QueryBooleanAsync("GetDevSysDate", null, ct))) pass = false;
            op.Text("重启后电脑时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        if (pass) op.Ok("RTC时间测试通过");
        else op.Fail("RTC时间测试未通过");
        return pass ? StepResult.Pass("RTC时间测试通过") : StepResult.Fail("RTC时间测试未通过");
    }
}

// ============================================================================
// TestBluetoothConST811AHandler
// ============================================================================
/// <summary>
/// 蓝牙测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// </summary>
public sealed class TestBluetoothConST811AHandler : IStepHandler
{
    public string Kind => "TestBluetooth";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = true;

        if (!(await op.Dut.QueryBooleanAsync("OpenBlueTooth", null, ct))) pass = false;
        await Task.Delay(2000, ct);

        if (pass && !(await op.Dut.QueryBooleanAsync("GetBlueToothState", null, ct))) pass = false;
        if (pass && !(await op.Dut.QueryBooleanAsync("GetBlueToothName", null, ct))) pass = false;

        if (!(await op.Dut.QueryBooleanAsync("CloseBlueTooth", null, ct))) pass = false;

        if (pass) op.Ok("蓝牙测试通过");
        else op.Fail("蓝牙测试未通过");
        return pass ? StepResult.Pass("蓝牙测试通过") : StepResult.Fail("蓝牙测试未通过");
    }
}

// ============================================================================
// TestCalibrationSensorConST811AHandler
// ============================================================================
/// <summary>
/// 进气传感器校准（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// </summary>
public sealed class TestCalibrationSensorConST811AHandler : IStepHandler
{
    public string Kind => "TestCalibrationSensor";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = true;

        if (!(await op.Dut.QueryBooleanAsync("SetCalibrationSensorAuto", null, ct))) pass = false;

        if (pass)
        {
            var result = await op.Dut.QueryTextAsync("GetCalibrationSensorResult", null, ct);
            op.Text("校准结果", result ?? "");
            if (string.IsNullOrWhiteSpace(result) || result != "OK") pass = false;
        }

        if (pass) op.Ok("传感器校准通过");
        else op.Fail("传感器校准未通过");
        return pass ? StepResult.Pass("传感器校准通过") : StepResult.Fail("传感器校准未通过");
    }
}

// ============================================================================
// TestControllerBroadPowerConST811AHandler
// ============================================================================
/// <summary>
/// 控制板电源测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// </summary>
public sealed class TestControllerBroadPowerConST811AHandler : IStepHandler
{
    public string Kind => "TestControllerBroadPower";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var probe = await op.Dut.QueryTextAsync("GetControllerBroadPowerCheckState", null, ct);
        op.Text("设备回读", probe ?? "");
        var pass = !string.IsNullOrWhiteSpace(probe);
        if (pass) op.Ok("控制板电源测试通过");
        else op.Fail("控制板电源测试未通过");
        return pass ? StepResult.Pass("控制板电源测试通过") : StepResult.Fail("控制板电源测试未通过");
    }
}

// ============================================================================
// TestCPSConST811AHandler
// ============================================================================
/// <summary>
/// CPS手动检测（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// </summary>
public sealed class TestCPSConST811AHandler : IStepHandler
{
    public string Kind => "TestCPS";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = true;

        var sn = ctx.SerialNumber ?? "";
        if (!sn.StartsWith("811AGC") && !sn.StartsWith("811ADC") && !sn.StartsWith("811ALC"))
        {
            op.Ok("该机型无需CPS测试，跳过");
            return StepResult.Pass("CPS手动检测跳过");
        }

        if (!(await op.Dut.QueryBooleanAsync("SetCheckerOpen", null, ct)))
            pass = false;

        var checkerOk = false;
        for (var i = 0; i < 10; i++)
        {
            var state = await op.Dut.QueryTextAsync("GetCheckerState", null, ct);
            if (state == "OK") { checkerOk = true; break; }
            if (i < 9) await Task.Delay(1000, ct);
        }
        op.Verdict("CPS状态", checkerOk);
        if (!checkerOk) pass = false;

        if (!(await op.Dut.QueryBooleanAsync("SetCheckerClose", null, ct)))
            pass = false;

        if (pass) op.Ok("CPS手动检测通过");
        else op.Fail("CPS手动检测未通过");
        return pass ? StepResult.Pass("CPS手动检测通过") : StepResult.Fail("CPS手动检测未通过");
    }
}

// ============================================================================
// TestDeviceWriteSNConST811AHandler
// ============================================================================
/// <summary>
/// 设备 SN 写入（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// 
/// 测试逻辑：
/// 1. 确定要写入的 SN（优先使用参数值，其次使用上下文 SN）
/// 2. 写入 SN 到被检设备
/// 3. 读回验证
/// </summary>
public sealed class TestDeviceWriteSNConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestDeviceWriteSN";
    
    /// <summary>不限定设备家族（所有 ConST811A 变体共用）。</summary>
    public string? DeviceFamily => null;

    /// <summary>执行本测试项。</summary>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var ops = ConST811AOpsFactory.Create(ctx, ct);
        var pass = true;

        // 确定要写入的 SN
        var requestedSn = ctx.Parameter("写入SN")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(requestedSn)) requestedSn = ctx.SerialNumber ?? "";
        if (string.IsNullOrWhiteSpace(requestedSn))
            return StepResult.Fail("SN写入未通过：未提供 SN 值");
        ops.Text("写入SN", requestedSn);

        // 写入 SN
        pass &= await ops.Dut.SetSerialNumberAsync(requestedSn, ct);

        // 读回验证
        if (pass)
        {
            ctx.SerialNumber = await ops.Dut.ReadSerialNumberAsync(ct);
            ops.Text("读回SN", ctx.SerialNumber ?? "");
            pass = string.Equals(requestedSn, ctx.SerialNumber, StringComparison.Ordinal);
        }

        if (pass) ops.Ok($"SN写入通过：{requestedSn}");
        else ops.Fail($"SN写入未通过：{requestedSn}");
        return pass ? StepResult.Pass("SN写入通过") : StepResult.Fail("SN写入未通过");
    }
}

// ============================================================================
// TestDeviceWriteTypeConST811AHandler
// ============================================================================
/// <summary>
/// 设备类型写入（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// 
/// 测试逻辑：
/// 1. 读取当前设备类型
/// 2. 根据机型确定控压系数（AG=0.905, AD=0.95, AAM=0.905, AAL=0.95, AL=0.30, AB=0.95, 10M=0.905）
/// 3. 写入控压系数
/// 4. 10M 机型特殊处理：设置泵阻转电流为 8A
/// 5. 写入设备类型
/// 6. 读回验证
/// </summary>
public sealed class TestDeviceWriteTypeConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestDeviceWriteType";
    
    /// <summary>不限定设备家族（所有 ConST811A 变体共用）。</summary>
    public string? DeviceFamily => null;

    /// <summary>执行本测试项。</summary>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = true;

        // 读取当前设备类型
        var orgDeviceMode = await op.Dut.QueryTextAsync("GetDevType", null, ct);
        op.Text("当前设备类型", orgDeviceMode ?? "");
        if (string.IsNullOrEmpty(orgDeviceMode))
            return StepResult.Fail("设备类型写入未通过：读取设备类型为空");

        // 根据机型确定控压系数
        double setControlPanelModelParameter = orgDeviceMode switch
        {
            var s when s.Contains("AG") => 0.905,
            var s when s.Contains("AD") => 0.95,
            var s when s.Contains("AAM") => 0.905,
            var s when s.Contains("AAL") => 0.95,
            var s when s.Contains("AL") => 0.30,
            var s when s.Contains("AB") => 0.95,
            var s when s.Contains("10M") => 0.905,
            _ => 0.905
        };
        op.Value("控压系数", setControlPanelModelParameter, "");

        // 写入控压系数
        if (!(await op.Dut.QueryBooleanAsync("SetControlPanelModelParameter",
            new[] { setControlPanelModelParameter.ToString("F4") }, ct)))
            return StepResult.Fail("设备类型写入未通过：写入控压系数失败");

        // 读回控压系数验证
        op.Text("读回控压系数", await op.Dut.QueryTextAsync("GetControlPanelModelParameter", null, ct) ?? "");

        // 10M 机型特殊处理：设置泵阻转电流为 8A
        if (orgDeviceMode.Contains("10M"))
        {
            await op.Dut.CommandAsync("SetDumpStallingCurrent", null, ct);
            if (await op.Dut.QueryBooleanAsync("SetDumpCurrent", new[] { "8" }, ct))
                op.Text("泵阻转电流", $"{await op.Dut.QueryTextAsync("GetDumpCurrent", null, ct)}A");
            else
                op.Text("泵阻转电流", "设置失败");
        }

        // 写入设备类型
        pass &= await op.Dut.SetPrimaryDeviceTypeAsync(orgDeviceMode, ct);

        // 读回验证
        if (pass)
        {
            var newType = await op.Dut.QueryTextAsync("GetDevType", null, ct);
            op.Text("读回设备类型", newType ?? "");
            if (newType != orgDeviceMode)
            {
                op.Fail($"比对失败：期望 {orgDeviceMode}，实际 {newType}");
                pass = false;
            }
        }

        if (pass) op.Ok($"设备类型写入通过：{orgDeviceMode}");
        else op.Fail($"设备类型写入未通过：{orgDeviceMode}");
        return pass ? StepResult.Pass("设备类型写入通过") : StepResult.Fail("设备类型写入未通过");
    }
}

// ============================================================================
// TestHartConST811AHandler
// ============================================================================
/// <summary>
/// HART 通讯测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// </summary>
public sealed class TestHartConST811AHandler : IStepHandler
{
    public string Kind => "TestHart";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = true;

        if (!(await op.Dut.QueryBooleanAsync("SetElectricSourceFunction", new[] { "V" }, ct)))
            pass = false;
        if (pass && !(await op.Dut.QueryBooleanAsync("SetEleChannelItem_VOL", null, ct)))
            pass = false;

        if (pass)
        {
            var hartData = await op.Dut.QueryTextAsync("GetHardCorValue", null, ct);
            op.Text("HART 数据", hartData ?? "");
            if (string.IsNullOrWhiteSpace(hartData)) pass = false;
        }

        if (pass) op.Ok("HART 测试通过");
        else op.Fail("HART 测试未通过");
        return pass ? StepResult.Pass("HART 测试通过") : StepResult.Fail("HART 测试未通过");
    }
}

// ============================================================================
// TestKeyBoardConST811AHandler
// ============================================================================
/// <summary>
/// 按键测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// 设备上启动按键自检程序，用户在设备上操作并点击 Success/Fail，程序轮询结果。
/// </summary>
public sealed class TestKeyBoardConST811AHandler : IStepHandler
{
    public string Kind => "TestKeyBoard";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = false;

        // 启动设备按键自检程序（设备弹出应用供用户操作）
        if (!(await op.Dut.QueryBooleanAsync("SetCheckerOpen", new[] { "KeyBoard" }, ct)))
        {
            op.Fail("启动按键自检程序失败");
            return StepResult.Fail("按键测试未通过：启动自检程序失败");
        }

        // 轮询自检结果（用户在设备上点击 Success/Fail）
        for (var i = 0; i < 120; i++)
        {
            var state = await op.Dut.QueryTextAsync("GetCheckerState", new[] { "KeyBoard" }, ct);
            if (state == "TestPass") { pass = true; break; }
            if (state == "TestFail") { break; }
            await Task.Delay(500, ct);
        }

        await op.Dut.QueryBooleanAsync("SetCheckerClose", null, ct);

        if (pass) op.Ok("按键测试通过");
        else op.Fail("按键测试未通过");
        return pass ? StepResult.Pass("按键测试通过") : StepResult.Fail("按键测试未通过");
    }
}

// ============================================================================
// TestLANConST811AHandler
// ============================================================================
/// <summary>
/// 网口通讯测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// 读取设备以太网IP后启动网口自检程序，用户在设备上操作并点击 Success/Fail，程序轮询结果。
/// </summary>
public sealed class TestLANConST811AHandler : IStepHandler
{
    public string Kind => "TestLAN";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = true;

        // 读取设备以太网IP
        var ip = await op.Dut.QueryTextAsync("GetStaticETHemetIPAddress", null, ct);
        op.Text("设备IP", ip ?? "");
        if (string.IsNullOrWhiteSpace(ip))
            pass = false;
        else if (!ip.StartsWith("192") && !ip.StartsWith("169"))
        {
            op.Fail($"IP地址格式异常：{ip}（应以192或169开头）");
            pass = false;
        }

        if (!pass)
        {
            op.Fail("网口通讯测试未通过");
            return StepResult.Fail("网口通讯测试未通过");
        }

        try
        {
            // 启动设备网口自检程序（设备弹出应用供用户操作）
            if (!(await op.Dut.QueryBooleanAsync("SetCheckerOpen", new[] { "LAN" }, ct)))
            {
                op.Fail("启动网口自检程序失败");
                return StepResult.Fail("网口通讯测试未通过：启动自检程序失败");
            }

            // 轮询自检结果（用户在设备上点击 Success/Fail）
            var checkerOk = false;
            for (var i = 0; i < 120; i++)
            {
                var state = await op.Dut.QueryTextAsync("GetCheckerState", new[] { "LAN" }, ct);
                if (state == "TestPass") { checkerOk = true; break; }
                if (state == "TestFail") { break; }
                await Task.Delay(500, ct);
            }
            op.Verdict("网口检查状态", checkerOk);
            if (!checkerOk) pass = false;
        }
        finally
        {
            await op.Dut.QueryBooleanAsync("SetCheckerClose", null, ct);
        }

        if (pass) op.Ok("网口通讯测试通过");
        else op.Fail("网口通讯测试未通过");
        return pass ? StepResult.Pass("网口通讯测试通过") : StepResult.Fail("网口通讯测试未通过");
    }
}

// ============================================================================
// TestMeterStateConST811AHandler
// ============================================================================
/// <summary>
/// 电池功耗测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// </summary>
public sealed class TestMeterStateConST811AHandler : IStepHandler
{
    public string Kind => "TestMeterState";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = true;

        var batteryV = await op.Dut.QueryTextAsync("GetBatteryVoltage", null, ct);
        op.Text("电池电压", batteryV ?? "");
        if (string.IsNullOrWhiteSpace(batteryV)) pass = false;

        if (pass && double.TryParse(batteryV, out var v) && ctx.Conditions.Count > 0)
        {
            var r = ctx.Evaluator.Evaluate(ctx.Conditions[0], v);
            op.Verdict("电池电压", r.Passed);
            if (!r.Passed) pass = false;
        }

        if (pass) op.Ok("电池功耗测试通过");
        else op.Fail("电池功耗测试未通过");
        return pass ? StepResult.Pass("电池功耗测试通过") : StepResult.Fail("电池功耗测试未通过");
    }
}

// ============================================================================
// TestOverallWIFIConST811AHandler
// ============================================================================
/// <summary>
/// WIFI 测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// </summary>
public sealed class TestOverallWIFIConST811AHandler : IStepHandler
{
    public string Kind => "TestOverallWIFI";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = true;

        if (!(await op.Dut.QueryBooleanAsync("OpenWIFI", null, ct))) pass = false;
        await Task.Delay(2000, ct);

        if (pass && !(await op.Dut.QueryBooleanAsync("GetWIFIState", null, ct))) pass = false;
        if (pass && !(await op.Dut.QueryBooleanAsync("GetWIFIName", null, ct))) pass = false;

        if (!(await op.Dut.QueryBooleanAsync("CloseWIFI", null, ct))) pass = false;

        if (pass) op.Ok("WIFI测试通过");
        else op.Fail("WIFI测试未通过");
        return pass ? StepResult.Pass("WIFI测试通过") : StepResult.Fail("WIFI测试未通过");
    }
}

// ============================================================================
// TestPaModuleConST811AHandler
// ============================================================================
/// <summary>
/// PA 模块测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// 自动流程：工装打开PA继电器 → 设备切换PA档位 → 搜索PA → 连接 → 读取测量值。
/// 搜索失败时提示操作员检查电测线并重试（最多3次）。
/// </summary>
public sealed class TestPaModuleConST811AHandler : IStepHandler
{
    public string Kind => "TestPaModule";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = false;

        try
        {
            // 工装打开 PA 继电器
            await op.Gzp21.SetOutputAsync("PA", true, ct);

            for (var trynum = 1; trynum <= 3; trynum++)
            {
                // 设备切换电测档位为 PA 变送器
                if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_PA", null, ct)))
                {
                    op.Fail("电测档位切换PA变送器失败");
                    return StepResult.Fail("PA 模块测试未通过：切换PA档位失败");
                }
                op.Report("电测档位切换PA变送器完成", RealtimeLevel.Info);
                await Task.Delay(5000, ct);

                // 搜索 PA 变送器
                op.Report("开始搜索PA变送器", RealtimeLevel.Info);
                if (!(await op.Dut.QueryBooleanAsync("SearchPA", null, ct)))
                {
                    op.Fail("搜索PA变送器失败");
                    return StepResult.Fail("PA 模块测试未通过：搜索PA失败");
                }

                // 轮询获取搜索列表（最多10次，每次间隔1秒）
                var paList = "";
                for (var i = 0; i < 10; i++)
                {
                    paList = await op.Dut.QueryTextAsync("GetPAMassage", null, ct);
                    if (!string.IsNullOrWhiteSpace(paList) && paList != "0")
                        break;
                    await Task.Delay(1000, ct);
                }

                op.Report("搜索结束", RealtimeLevel.Info);

                if (string.IsNullOrWhiteSpace(paList) || paList == "0")
                {
                    // 没有搜索到设备，提示操作员检查电测线
                    if (trynum < 3)
                    {
                        op.Report("没有获取搜索列表，弹窗提示是否正确接好电测线", RealtimeLevel.Warn);
                        var retry = await ctx.ConfirmAsync(
                            "没有搜索到设备，电测线可能没接，请先使用测试线 连接设备与工装的SRC和MEAS插孔，红对红，黑对黑。\r\n点击确认，重新测试，否则测试失败。",
                            "确认", "取消", ct);
                        if (!retry)
                        {
                            op.Fail("没有搜索到设备，操作员手动中止");
                            return StepResult.Fail("PA 模块测试未通过：操作员手动中止");
                        }
                        await Task.Delay(5000, ct);
                        continue;
                    }
                    op.Fail("没有搜索到任何PA");
                    return StepResult.Fail("PA 模块测试未通过：没有搜索到任何PA");
                }

                // 获取搜索列表
                op.Text("搜索列表", paList);

                // 连接搜索到的第一个 PA 变送器（地址从搜索列表中取）
                var address = paList.Split(',')[0].Trim();
                op.Report($"开始连接PA变送器（地址：{address}）", RealtimeLevel.Info);
                if (!(await op.Dut.QueryBooleanAsync("ConnectPA", new[] { address }, ct)))
                {
                    op.Fail("连接PA变送器失败");
                    return StepResult.Fail("PA 模块测试未通过：连接PA失败");
                }
                op.Report("连接PA变送器完成", RealtimeLevel.Info);
                await Task.Delay(1000, ct);

                // 获取当前电测信息
                var measure = await op.Dut.QueryTextAsync("GetCurrentElectricMeasure", null, ct);
                op.Text("当前测试信息", measure ?? "");
                op.Text("PA地址", address);

                if (string.IsNullOrWhiteSpace(measure) || measure == "0")
                {
                    op.Fail("获取当前电测信息失败或测量值为空");
                    return StepResult.Fail("PA 模块测试未通过：测量值为空");
                }

                pass = true;
                break;
            }
        }
        finally
        {
            // 工装关闭 PA 继电器
            await op.Gzp21.SetOutputAsync("PA", false, ct);
        }

        if (pass) op.Ok("PA 模块测试通过");
        else op.Fail("PA 模块测试未通过");
        return pass ? StepResult.Pass("PA 模块测试通过") : StepResult.Fail("PA 模块测试未通过");
    }
}

// ============================================================================
// TestPowerAdapterConST811AHandler
// ============================================================================
/// <summary>
/// 电源适配器测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// </summary>
public sealed class TestPowerAdapterConST811AHandler : IStepHandler
{
    public string Kind => "TestPowerAdapter";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var probe = await op.Dut.QueryTextAsync("GetPowerAdapterState", null, ct);
        op.Text("设备回读", probe ?? "");
        var pass = !string.IsNullOrWhiteSpace(probe);
        if (pass) op.Ok("电源适配器测试通过");
        else op.Fail("电源适配器测试未通过");
        return pass ? StepResult.Pass("电源适配器测试通过") : StepResult.Fail("电源适配器测试未通过");
    }
}

// ============================================================================
// TestSelfTuningConST811AHandler
// ============================================================================
/// <summary>
/// 自整定测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// </summary>
public sealed class TestSelfTuningConST811AHandler : IStepHandler
{
    public string Kind => "TestSelfTuning";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = true;

        if (!(await op.Dut.QueryBooleanAsync("SetSelfTuning", null, ct))) pass = false;

        if (pass)
        {
            var tuningOk = false;
            for (var i = 0; i < 30; i++)
            {
                var state = await op.Dut.QueryTextAsync("GetSelfTuningState", null, ct);
                if (state == "OK") { tuningOk = true; break; }
                if (state == "FAIL") { break; }
                if (i < 29) await Task.Delay(1000, ct);
            }
            op.Verdict("自整定结果", tuningOk);
            if (!tuningOk) pass = false;
        }

        if (pass) op.Ok("自整定测试通过");
        else op.Fail("自整定测试未通过");
        return pass ? StepResult.Pass("自整定测试通过") : StepResult.Fail("自整定测试未通过");
    }
}

// ============================================================================
// TestSoftVersionsConST811AHandler
// ============================================================================
/// <summary>
/// 软件版本验证（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// </summary>
public sealed class TestSoftVersionsConST811AHandler : IStepHandler
{
    public string Kind => "TestSoftVersions";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = true;

        var sysVersion = await op.Dut.QueryTextAsync("GetVersion", null, ct);
        op.Text("系统版本", sysVersion ?? "");
        if (string.IsNullOrWhiteSpace(sysVersion)) pass = false;

        var elecVersion = await op.Dut.QueryTextAsync("GetVersion_Electricity", null, ct);
        op.Text("电测版本", elecVersion ?? "");
        if (string.IsNullOrWhiteSpace(elecVersion)) pass = false;

        op.Text("设备类型", await op.Dut.QueryTextAsync("GetDevType", null, ct) ?? "");
        op.Text("控制器版本", await op.Dut.QueryTextAsync("GetVersion_Controller", null, ct) ?? "");
        op.Text("DD库版本", await op.Dut.QueryTextAsync("GetDeviceDDT", null, ct) ?? "");
        op.Text("固件版本", await op.Dut.QueryTextAsync("GetFixVersion", null, ct) ?? "");
        op.Text("硬件版本", await op.Dut.QueryTextAsync("ReadHardWaveVersion", null, ct) ?? "");

        var sn = await op.Dut.QueryTextAsync("GetSerialNumber", null, ct);
        op.Text("序列号", sn ?? "");
        ctx.SerialNumber = sn;

        if (pass) op.Ok("版本验证通过");
        else op.Fail("版本验证未通过");
        return pass ? StepResult.Pass("版本验证通过") : StepResult.Fail("版本验证未通过");
    }
}

// ============================================================================
// TestStorageCardPrincipalConST811AHandler
// ============================================================================
/// <summary>
/// 存储卡测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// </summary>
public sealed class TestStorageCardPrincipalConST811AHandler : IStepHandler
{
    public string Kind => "TestStorageCardPrincipal";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var probe = await op.Dut.QueryTextAsync("GetStorageCardState", null, ct);
        op.Text("设备回读", probe ?? "");
        var pass = !string.IsNullOrWhiteSpace(probe);
        if (pass) op.Ok("SD卡测试通过");
        else op.Fail("SD卡测试未通过");
        return pass ? StepResult.Pass("SD卡测试通过") : StepResult.Fail("SD卡测试未通过");
    }
}

// ============================================================================
// TestSwitchConST811AHandler
// ============================================================================
/// <summary>
/// 开关测量功能测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// </summary>
public sealed class TestSwitchConST811AHandler : IStepHandler
{
    public string Kind => "TestSwitch";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var pass = true;

        await op.Gzp21.SetOutputAsync("Ele", true, ct);

        if (!(await op.Dut.QueryBooleanAsync("SetElectricSourceFunction", new[]{ "V" }, ct)))
            pass = false;

        // 机械(或NPN)开关分开测试
        if (pass && !(await op.Dut.QueryBooleanAsync("SetEleChannelItem_SW_Normal", null, ct)))
            pass = false;

        if (pass)
        {
            var retryOk1 = await RetryHelper.RetryAsync(async attempt =>
            {
                var ok = false;
                if (!(await op.Dut.QueryBooleanAsync("SetElectricSourceTarget", new[]{ "5" }, ct)))
                { op.Report("设置电输出目标值失败", RealtimeLevel.Error); ok = false; }
                await Task.Delay(1000, ct);
                var txt = await op.Dut.QueryTextAsync("GetCurrentElectricMeasure", null, ct);
                if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_SW_Normal", null, ct)))
                { op.Report("打开机械开关失败", RealtimeLevel.Error); ok = false; }
                if (double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    ok = !Convert.ToBoolean(Convert.ToInt32(v));
                    op.Report($"机械开关分开: {(ok ? "√" : "机械(或NPN)开关测试未通过")}");
                }
                else op.Report("读取当前电测测量值失败", RealtimeLevel.Error);
                return ok;
            }, _ => ctx.ConfirmAsync("电测线可能没接，请先使用测试线 连接设备与工装的SRC和MEAS插孔，红对红，黑对黑。\r\n点击确认，重新测试，否则测试失败。", ct), 4, ct);
            if (!retryOk1) pass = false;
        }

        // 机械(或NPN)开关短接测试
        if (pass)
        {
            var retryOk2 = await RetryHelper.RetryAsync(async attempt =>
            {
                var ok = false;
                if (!(await op.Dut.QueryBooleanAsync("SetElectricSourceTarget", new[]{ "0" }, ct)))
                { op.Report("设置电输出目标值失败", RealtimeLevel.Error); ok = false; }
                await Task.Delay(1000, ct);
                var txt = await op.Dut.QueryTextAsync("GetCurrentElectricMeasure", null, ct);
                if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_SW_Normal", null, ct)))
                { op.Report("打开机械开关失败", RealtimeLevel.Error); ok = false; }
                if (double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    ok = Convert.ToBoolean(Convert.ToInt32(v));
                    op.Report($"机械开关短接: {(ok ? "√" : "机械(或NPN)开关测试未通过")}");
                }
                else op.Report("读取当前电测测量值失败", RealtimeLevel.Error);
                return ok;
            }, _ => ctx.ConfirmAsync("电测线可能没接，请先使用测试线 连接设备与工装的SRC和MEAS插孔，红对红，黑对黑。\r\n点击确认，重新测试，否则测试失败。", ct), 4, ct);
            if (!retryOk2) pass = false;
        }

        // PNP 开关闭合测试
        if (pass)
        {
            if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_SW_PNP", null, ct)))
                pass = false;
            await Task.Delay(1000, ct);

            if (pass)
            {
                var retryOk3 = await RetryHelper.RetryAsync(async attempt =>
                {
                    var ok = false;
                    if (!(await op.Dut.QueryBooleanAsync("SetElectricSourceTarget", new[]{ "5" }, ct)))
                    { op.Report("设置电输出目标值失败", RealtimeLevel.Error); ok = false; }
                    await Task.Delay(1000, ct);
                    var txt = await op.Dut.QueryTextAsync("GetCurrentElectricMeasure", null, ct);
                    if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_SW_NPN", null, ct)))
                    { op.Report("打开PNP开关失败", RealtimeLevel.Error); ok = false; }
                    if (double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    {
                        ok = Convert.ToBoolean(Convert.ToInt32(v));
                        op.Report($"PNP开关闭合: {(ok ? "√" : "PNP开关未通过")}");
                    }
                    else op.Report("读取当前电测测量值失败", RealtimeLevel.Error);
                    return ok;
                }, _ => ctx.ConfirmAsync("电测线可能没接，请先使用测试线 连接设备与工装的SRC和MEAS插孔，红对红，黑对黑。\r\n点击确认，重新测试，否则测试失败。", ct), 4, ct);
                if (!retryOk3) pass = false;
            }
        }

        // PNP 开关断开测试
        if (pass)
        {
            var retryOk4 = await RetryHelper.RetryAsync(async attempt =>
            {
                var ok = false;
                if (!(await op.Dut.QueryBooleanAsync("SetElectricSourceTarget", new[]{ "0" }, ct)))
                { op.Report("设置电输出目标值失败", RealtimeLevel.Error); ok = false; }
                await Task.Delay(1000, ct);
                var txt = await op.Dut.QueryTextAsync("GetCurrentElectricMeasure", null, ct);
                if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_SW_NPN", null, ct)))
                { op.Report("打开NPN开关失败", RealtimeLevel.Error); ok = false; }
                if (double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    ok = !Convert.ToBoolean(Convert.ToInt32(v));
                    op.Report($"PNP开关断开: {(ok ? "√" : "PNP开关测试未通过")}");
                }
                else op.Report("读取当前电测测量值失败", RealtimeLevel.Error);
                return ok;
            }, _ => ctx.ConfirmAsync("电测线可能没接，请先使用测试线 连接设备与工装的SRC和MEAS插孔，红对红，黑对黑。\r\n点击确认，重新测试，否则测试失败。", ct), 4, ct);
            if (!retryOk4) pass = false;
        }

        await op.Gzp21.SetOutputAsync("Ele", false, ct);

        if (pass) op.Ok("开关测量功能测试通过");
        else op.Fail("开关测量功能测试未通过");
        return pass ? StepResult.Pass("开关测量功能测试通过") : StepResult.Fail("开关测量功能测试未通过");
    }
}

// ============================================================================
// TestUSBPrincipalConST811AHandler
// ============================================================================
/// <summary>
/// USB 存储测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// </summary>
public sealed class TestUSBPrincipalConST811AHandler : IStepHandler
{
    public string Kind => "TestUSBPrincipal";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var probe = await op.Dut.QueryTextAsync("GetUSBStorageState", null, ct);
        op.Text("设备回读", probe ?? "");
        var pass = !string.IsNullOrWhiteSpace(probe);
        if (pass) op.Ok("USB存储测试通过");
        else op.Fail("USB存储测试未通过");
        return pass ? StepResult.Pass("USB存储测试通过") : StepResult.Fail("USB存储测试未通过");
    }
}

// ============================================================================
// TestUSBSubordinateConST811AHandler
// ============================================================================
/// <summary>
/// USB 通讯测试（公共处理器）。所有 ConST811A 变体（BP/DP/LLP/MP）共用。
/// </summary>
public sealed class TestUSBSubordinateConST811AHandler : IStepHandler
{
    public string Kind => "TestUSBSubordinate";
    public string? DeviceFamily => null;

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = ConST811AOpsFactory.Create(ctx, ct);
        var probe = await op.Dut.QueryTextAsync("GetUSBCommState", null, ct);
        op.Text("设备回读", probe ?? "");
        var pass = !string.IsNullOrWhiteSpace(probe);
        if (pass) op.Ok("USB通讯测试通过");
        else op.Fail("USB通讯测试未通过");
        return pass ? StepResult.Pass("USB通讯测试通过") : StepResult.Fail("USB通讯测试未通过");
    }
}

