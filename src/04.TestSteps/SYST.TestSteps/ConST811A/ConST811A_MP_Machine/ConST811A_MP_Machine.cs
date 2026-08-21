using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;

namespace SYST.TestSteps.ConST811A.ConST811A_MP_Machine;

/// <summary>
/// ConST811A 主板（设备族 ConST811A）测试**设备特有**处理器集合。**自动转换**自旧
/// <c>ConST811A_MainBoard_Auto.cs</c> 的测试方法与 <c>.distributed.json</c> 任务配置：继电器指令序列
/// （GZP21 共享工装）、被检指令与 Range 判定。表绝压版不接 P06/ConST810 标准模块（电压/电流采样）。
/// 工装用 <see cref="IMachineTestTool"/>，被检用 <see cref="IConST811ADut"/>。
/// </summary>
internal sealed class ConST811AOps
{
    private readonly ITestContext _ctx;
    private readonly CancellationToken _ct;

    /// <summary>GZP21 共享工装（继电器输出）。</summary>
    public readonly IMachineTestTool Gzp21;

    /// <summary>被检 ConST811A 专属驱动。</summary>
    public readonly IConST811ADut Dut;

    public ConST811AOps(ITestContext ctx, CancellationToken ct)
    {
        _ctx = ctx;
        _ct = ct;
        Gzp21 = ctx.GetDevice<IMachineTestTool>("GZP21");
        Dut = ctx.GetDevice<IConST811ADut>();
    }

    /// <summary>数值格式化（保留三位有效小数）。</summary>
    public static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>推送实时消息。</summary>
    public void Report(string m, RealtimeLevel l = RealtimeLevel.Info) => _ctx.Report(m, l);

    /// <summary>真机稳定延时（继电器切档/设值后需等待）。PORT: 旧 Thread.Sleep / ScriptHelper.Thread_Sleep。</summary>
    public Task Sleep(int ms)
    {
        Report($"等待 {ms}ms");
        return Task.Delay(ms, _ct);
    }

    /// <summary>发共享工装输出指令（按名称映射到 GZP21 通道）。</summary>
    public Task Relay(string cmd)
    {
        Report($"工装输出指令：{cmd}");
        return Gzp21.SetOutputAsync(cmd, true, _ct);
    }

    /// <summary>回放旧平台中可直接映射的 P21/GZP21 调用；复杂上下文参数不在此层猜测。</summary>
    public async Task ExecuteLegacyAsync(IReadOnlyList<string> calls, CancellationToken ct)
    {
        foreach (var call in calls)
        {
            var p = call.Split('|', 3);
            if (p.Length < 2) continue;
            var device = p[0];
            var method = p[1];
            var arg = p.Length == 3 ? p[2] : "";
            IReadOnlyList<string>? args = string.IsNullOrWhiteSpace(arg) ? null : new[] { arg.Trim() };
            if (device == "GZP21")
            {
                var open = !arg.Contains("Close", StringComparison.OrdinalIgnoreCase);
                var outputName = method.Replace("Set", "").Replace("State", "");
                await Gzp21.SetOutputAsync(outputName, open, ct);
                continue;
            }
            if (device == "P21")
            {
                if (method.StartsWith("Get", StringComparison.OrdinalIgnoreCase) || method.StartsWith("Is", StringComparison.OrdinalIgnoreCase))
                    _ = await Dut.QueryTextAsync(method, args, ct);
                else
                    await Dut.CommandAsync(method, args, ct);
            }
            else if (device == "P06")
            {
                // 表绝压版不接 P06 标准模块；旧脚本遗留的 P06 电压/电流调用按跳过处理（无采样设备）
                Report($"跳过 P06 调用：{method}（本机型未配置 P06 标准模块）", RealtimeLevel.Warn);
            }
        }
    }

    /// <summary>按名取条件（找不到返回 null）。</summary>
    public ConditionDescriptor? Cond(string name)
    {
        foreach (var c in _ctx.Conditions)
            if (c.Name == name) return c;
        return null;
    }

    /// <summary>对某测量值按指定条件名判定，报「读回+区间+结论」并返回是否通过（条件缺失记为不通过）。</summary>
    public bool Judge(string condName, double value, string label, string unit)
    {
        var cond = Cond(condName);
        if (cond is null)
        {
            Report($"{label} {F(value)}{unit}：缺少判定条件 {condName}", RealtimeLevel.Warn);
            return false;
        }
        var r = _ctx.Evaluator.Evaluate(cond, value);
        Report($"{label} {F(value)}{unit}：{r.Message}", r.Passed ? RealtimeLevel.Info : RealtimeLevel.Warn);
        return r.Passed;
    }

    /// <summary>掐头去尾各 5 点（旧 ScriptHelperKVP.TrimCurrents 语义）。</summary>
    public static List<double> TrimCurrents(List<double> values)
    {
        if (values.Count <= 10) return values;
        return values.Skip(5).Take(values.Count - 10).ToList();
    }

    /// <summary>
    /// 执行设备指令并自动重试（替代旧脚本 goto tryagain + OpenInfoConfirmWindow 模式）。
    /// 失败时仅记录日志，不弹窗。最多重试 maxRetries 次（含首次），每次间隔 1 秒。
    /// </summary>
    public async Task<bool> TryCommand(Func<Task<bool>> action, string desc, int maxRetries = 3)
    {
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            _ct.ThrowIfCancellationRequested();
            if (await action()) { Report($"✓ {desc}"); return true; }
            Fail($"{desc}失败(第{attempt}次)");
            if (attempt < maxRetries) await Task.Delay(1000, _ct);
        }
        return false;
    }

    /// <summary>
    /// 执行设备查询并读取返回值（自动重试）。失败时返回 null。
    /// </summary>
    public async Task<string?> TryQueryValue(Func<Task<string>> query, string desc, int maxRetries = 3)
    {
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            _ct.ThrowIfCancellationRequested();
            var val = await query();
            if (!string.IsNullOrWhiteSpace(val)) { Report($"✓ {desc}: {val}"); return val; }
            Fail($"{desc}失败(第{attempt}次)");
            if (attempt < maxRetries) await Task.Delay(1000, _ct);
        }
        return null;
    }

    /// <summary>步骤失败：报告操作失败（用 ✗ 标记）。</summary>
    public void Fail(string desc) => Report($"✗ {desc}", RealtimeLevel.Error);

    /// <summary>步骤成功：报告操作完成（用 ✓ 标记）。</summary>
    public void Ok(string desc) => Report($"✓ {desc}", RealtimeLevel.Success);
}

/// <summary>
/// 低压量程压力泄露测试和排空测试。PORT: 旧脚本方法 LeakTestComposition_Low_MP（JSON Entry: LeakTestComposition_Low_MP）。
/// </summary>
public sealed class LeakTestComposition_Low_MPConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "LeakTestComposition_Low_MP";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_MP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        // G10: 旧 List<DataBase>/StringBuilder tvalue 改为 StringBuilder，最终 op.Report(tvalue)
        var tvalue = new StringBuilder();
        tvalue.Append("压力值,高压温度,低压温度,泵温度,电测板温度^");
        var tstr = "";
        Pressure getInternalModulePressureOrg = new Pressure(0, "kPa");
        double rate = 0;
        DateTime starTime = DateTime.Now;

        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Open" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Open" }, ct);

        Pressure AtmosSensor = new Pressure(0, "kPa");

        await op.Dut.CommandAsync("GetAtmosSensor", null, ct);

        // 切换低压量程（规则1：空成功分支+ConfirmAsync → TryCommand）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetControlPressureModel", new[]{ "Low" }, ct), "切换低压量程"))) pass = false;
        await op.Sleep(5000);

        // 设定内部模块压力单位（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetPressureUnit_IPM", null, ct), "设定内部模块压力单位"))) pass = false;

        // 获取压力控制量程范围（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetSetPointLimitPressureRange", null, ct), "获取压力控制量程范围"))) pass = false;
        // DevicePressureRange 仅用于旧脚本日志，本平台不再使用（dead variable，已删除）

        Pressure InnerModulePressureUpper = new Pressure(0, "kPa");//量程上限
        Pressure getInternalModulePressure30SFirst = new Pressure(0, "kPa");
        Pressure getInternalModulePressure30SSecond = new Pressure(0, "kPa");
        Pressure getSourcePressure30SFirst = new Pressure(0, "kPa");
        Pressure getSourcePressure30SSecond = new Pressure(0, "kPa");
        double positiveinternalPressureRate = double.MaxValue;
        double positiveSupplyPressureRate = double.MaxValue;
        // state/state4 在新平台用字符串 stateTxt.Contains("Stable") 判断（见 BP 文件），无需声明枚举

        await op.Dut.CommandAsync("GetPressureLowerer_IPM", null, ct);

        Pressure InnerModulePressureLowerer = new Pressure(0, "kPa");//量程上限
        // 30s First/Second 已在上面声明，此处为旧脚本 reset（赋 0 即可）
        getInternalModulePressure30SFirst = new Pressure(0, "kPa");
        getInternalModulePressure30SSecond = new Pressure(0, "kPa");
        getSourcePressure30SFirst = new Pressure(0, "kPa");
        getSourcePressure30SSecond = new Pressure(0, "kPa");
        double negativeinternalPressureRate = double.MaxValue;
        double negativeSupplyPressureRate = double.MaxValue;

        // 获取内部模块量程下限（规则4：QueryBoolean+QueryText → TryQueryValue）
        var lowTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetPressureControlRange_LowerLimit", null, ct), "获取内部模块量程下限");
        if (lowTxt is not null && double.TryParse(lowTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var lowVal))
            InnerModulePressureLowerer = new Pressure(lowVal, "kPa");
        else
            pass = false;

        // 设置压力目标（规则2：失败日志+ConfirmAsync → TryCommand）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ InnerModulePressureLowerer.ToString() }, ct), "设置压力目标"))) pass = false;

        // state 在新平台用字符串 stateTxt.Contains("Stable") 判断（见 BP 文件），无需声明枚举
        var VP1s = new List<double>();

        if (!(await op.Dut.QueryBooleanAsync("GetControllerModuleConfig", null, ct))) { op.Report("GetControllerModuleConfig 调用失败", RealtimeLevel.Error); pass = false; }
        starTime = DateTime.Now;
        // 旧 while(true) 轮询 GetPressure_IPM 直到 rate>=0.95（控压到位）或超时（规则5：保留轮询循环）
        var lowPollGuard = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await op.Dut.CommandAsync("GetPressureStableState", null, ct);
            var ipmTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipmVal))
                getInternalModulePressureOrg = new Pressure(ipmVal, "kPa");
            rate = Math.Abs(getInternalModulePressureOrg.Value / InnerModulePressureLowerer.Value);
            if (rate >= 0.95) { op.Report($"打压完成,耗时{(DateTime.Now - starTime).TotalSeconds} s"); break; }
            if (++lowPollGuard > 600) { op.Report($"打压失败,耗时{(DateTime.Now - starTime).TotalSeconds} s，时间超过了指标，性能达不到要求。当前压力{getInternalModulePressureOrg}，没有达到目标点{InnerModulePressureLowerer}。", RealtimeLevel.Warn); pass = false; break; }
            op.Report($"当前压力{getInternalModulePressureOrg}，没有达到目标点{InnerModulePressureLowerer}。{(DateTime.Now - starTime).TotalSeconds} s");
            VP1s.Add(getInternalModulePressureOrg.Value);
            await op.Sleep(500);
        }

        await op.Sleep(2000);

        // 设置控制器测量模式（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTestMode", null, ct), "设置控制器测量模式"))) pass = false;

        await op.Sleep(50);

        // 获取内部模块压力（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        // 30秒前负压气源值（规则4：QueryBoolean+QueryText → TryQueryValue）
        var vpFirstTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetVacuumPressure", null, ct), "获取负压气源压力");
        if (vpFirstTxt is not null && double.TryParse(vpFirstTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var vpFirstVal))
            getSourcePressure30SFirst = new Pressure(vpFirstVal, "kPa");
        else
            pass = false;

        var P1s = new List<double>();
        // 旧 while(true) 30 秒轮询：每 150ms 读 IPM + Dev_T，append tvalue + P1s（规则5：保留轮询循环）
        {
            var neg30SStart = DateTime.Now;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                // 规则4：QueryBoolean+QueryText → TryQueryValue 分别读取
                var infoTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetPressure_IPM", null, ct), "读取内部模块压力");
                if (infoTxt is null) { pass = false; break; }
                double infoVal = 0;
                if (double.TryParse(infoTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    infoVal = v;
                tstr = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetDev_T", null, ct), "读取Dev_T温度");
                if (tstr is null) { pass = false; break; }
                tvalue.Append($"{infoVal},{tstr};");
                P1s.Add(infoVal);
                if ((DateTime.Now - neg30SStart).TotalSeconds > 30) break;
                await op.Sleep(150);
            }
            op.Report(tvalue.ToString());
        }

        // 获取内部模块压力（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        // 30秒后负压气源值（规则4）
        var vpSecondTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetVacuumPressure", null, ct), "获取负压气源压力");
        if (vpSecondTxt is not null && double.TryParse(vpSecondTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var vpSecondVal))
            getSourcePressure30SSecond = new Pressure(vpSecondVal, "kPa");
        else
            pass = false;

        op.Report($"负压30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.NegativeExport, Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        negativeinternalPressureRate = Math.Abs((Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value)) / getInternalModulePressure30SFirst.Value);
        op.Report($"负压30秒泄露量(新): {(negativeinternalPressureRate * 100).ToString("F5") + "%"}");

        op.Report($"负压气源压力30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.NegativeSource, Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        negativeSupplyPressureRate = Math.Abs((Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value)) / getSourcePressure30SFirst.Value);
        op.Report($"负压气源压力30秒泄露率: {(negativeSupplyPressureRate * 100).ToString("F5") + "%"}");

        await op.Dut.CommandAsync("SetVentMode", null, ct);
        await op.Sleep(10000);
        // G8: 旧 goto tryagain 已由 TryCommand 自动重试替代（无弹窗）

        // 读取当前压力（规则2）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "读取当前压力"))) pass = false;
        // 排空（规则2）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetVentMode", null, ct), "排空"))) pass = false;
        await op.Sleep(3000);
        // 旧 while(true) 轮询 GetPressureStableState 直到 Stable（bounded 防死等）（规则5）
        var stableGuard = 0;
        while (true)
        {
            var stateTxt = await op.Dut.QueryTextAsync("GetPressureStableState", null, ct);
            if (stateTxt.Contains("Stable", StringComparison.OrdinalIgnoreCase)) break;
            if (++stableGuard > 600) { op.Report("等待压力稳定超时(300s)", RealtimeLevel.Warn); pass = false; break; }
            await op.Sleep(500);
        }
        // 获取内部模块压力（规则2）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        // 获取内部模块量程上限（规则4）
        var upTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetPressureControlRange_UpperLimit", null, ct), "获取内部模块量程上限");
        if (upTxt is not null && double.TryParse(upTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var upVal))
            InnerModulePressureUpper = new Pressure(upVal, "kPa");
        else
            pass = false;

        // 设置压力目标（规则2）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ InnerModulePressureUpper.ToString() }, ct), "设置压力目标"))) pass = false;

        var VP2s = new List<double>();

        if (!(await op.Dut.QueryBooleanAsync("GetControllerModuleConfig", null, ct))) { op.Report("GetControllerModuleConfig 调用失败", RealtimeLevel.Error); pass = false; }
        starTime = DateTime.Now;
        // 旧 while(true) 轮询 GetPressure_IPM 直到 rate>=0.95（控压到位）或超时（规则5）
        var upperPollGuard = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await op.Dut.CommandAsync("GetPressureStableState", null, ct);
            var ipmTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipmVal))
                getInternalModulePressureOrg = new Pressure(ipmVal, "kPa");
            rate = Math.Abs(getInternalModulePressureOrg.Value / InnerModulePressureUpper.Value);
            if (rate >= 0.95) { op.Report($"打压完成,耗时{(DateTime.Now - starTime).TotalSeconds} s"); break; }
            if (++upperPollGuard > 600) { op.Report($"打压失败,耗时{(DateTime.Now - starTime).TotalSeconds} s，时间超过了指标，性能达不到要求。当前压力{getInternalModulePressureOrg}，没有达到目标点{InnerModulePressureUpper}。", RealtimeLevel.Warn); pass = false; break; }
            op.Report($"当前压力{getInternalModulePressureOrg}，没有达到目标点{InnerModulePressureUpper}。{(DateTime.Now - starTime).TotalSeconds} s");
            VP2s.Add(getInternalModulePressureOrg.Value);
            await op.Sleep(500);
        }

        await op.Sleep(2000);

        // 设置控制器测量模式（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTestMode", null, ct), "设置控制器测量模式"))) pass = false;

        await op.Sleep(50);

        // 获取内部模块压力（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        // 30秒前正压气源值（规则4）
        var spFirstTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetSupplyPressure", null, ct), "获取正压气源压力");
        if (spFirstTxt is not null && double.TryParse(spFirstTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var spFirstVal))
            getSourcePressure30SFirst = new Pressure(spFirstVal, "kPa");
        else
            pass = false;

        var P2s = new List<double>();

        // 旧 while(true) 30 秒轮询：每 150ms 读 IPM + Dev_T，append tvalue + P2s（规则5）
        {
            var pos30SStart = DateTime.Now;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                // 规则4：QueryBoolean+QueryText → TryQueryValue 分别读取
                var infoTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetPressure_IPM", null, ct), "读取内部模块压力");
                if (infoTxt is null) { pass = false; break; }
                double infoVal = 0;
                if (double.TryParse(infoTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    infoVal = v;
                tstr = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetDev_T", null, ct), "读取Dev_T温度");
                if (tstr is null) { pass = false; break; }
                tvalue.Append($"{infoVal},{tstr};");
                P2s.Add(infoVal);
                if ((DateTime.Now - pos30SStart).TotalSeconds > 30) break;
                await op.Sleep(150);
            }
            op.Report(tvalue.ToString());
        }

        // 获取内部模块压力（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        // 30秒后正压气源值（规则4）
        var spSecondTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetSupplyPressure", null, ct), "获取正压气源压力");
        if (spSecondTxt is not null && double.TryParse(spSecondTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var spSecondVal))
            getSourcePressure30SSecond = new Pressure(spSecondVal, "kPa");
        else
            pass = false;

        op.Report($"正压30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.PositiveExport, Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        positiveinternalPressureRate = Math.Abs((Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value)) / getInternalModulePressure30SFirst.Value);
        op.Report($"正压30秒泄露率: {(positiveinternalPressureRate * 100).ToString("F5") + "%"}");

        op.Report($"正压气源压力30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.PositiveSource, Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        positiveSupplyPressureRate = Math.Abs((Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value)) / getSourcePressure30SFirst.Value);
        op.Report($"正压气源压力30秒泄露率: {(positiveSupplyPressureRate * 100).ToString("F5") + "%"}");

        await op.Dut.CommandAsync("SetVentMode", null, ct);
        await op.Sleep(10000);
        // G8: 旧 goto tryPagain 已由 TryCommand 自动重试替代（无弹窗）

        // 读取当前压力（规则2）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "读取当前压力"))) pass = false;

        // 排空（规则2）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetVentMode", null, ct), "排空"))) pass = false;
        await op.Sleep(3000);

        // 旧 while(true) 轮询 GetPressureStableState 直到 Stable（bounded 防死等）（规则5）
        {
            var stableGuard2 = 0;
            while (true)
            {
                var stateTxt = await op.Dut.QueryTextAsync("GetPressureStableState", null, ct);
                if (stateTxt.Contains("Stable", StringComparison.OrdinalIgnoreCase)) break;
                if (++stableGuard2 > 600) { op.Report("等待压力稳定超时(300s)", RealtimeLevel.Warn); pass = false; break; }
                await op.Sleep(500);
            }
        }
        // 获取内部模块压力（规则2）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        await op.Sleep(2000);
        op.Report($"压力值与温度值: {tvalue.ToString()}");
        await op.Dut.CommandAsync("SetVentMode", null, ct);
        // state4 用字符串 stateTxt.Contains("Stable") 判断（见 BP 文件），无需声明枚举

        // 旧 while(true) 轮询 GetPressureStableState 直到 Stable（bounded 防死等）（规则5）
        {
            var stableGuard2 = 0;
            while (true)
            {
                var stateTxt = await op.Dut.QueryTextAsync("GetPressureStableState", null, ct);
                if (stateTxt.Contains("Stable", StringComparison.OrdinalIgnoreCase)) break;
                if (++stableGuard2 > 600) { op.Report("等待压力稳定超时(300s)", RealtimeLevel.Warn); pass = false; break; }
                await op.Sleep(500);
            }
        }
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Close" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Close" }, ct);

        ctx.RecordProcessData(new ProcessDataSeries {
            StartedAt = DateTime.Now,
            TimeSec = Enumerable.Range(0, 1).Select(i => (double)i).ToArray(),
            Channels = new[] { new ProcessChannel("负压控压压力变化", VP1s.ToArray()) }
        });
        ctx.RecordProcessData(new ProcessDataSeries {
            StartedAt = DateTime.Now,
            TimeSec = Enumerable.Range(0, 1).Select(i => (double)i).ToArray(),
            Channels = new[] { new ProcessChannel("负压泄漏压力变化", P1s.ToArray()) }
        });
        ctx.RecordProcessData(new ProcessDataSeries {
            StartedAt = DateTime.Now,
            TimeSec = Enumerable.Range(0, 1).Select(i => (double)i).ToArray(),
            Channels = new[] { new ProcessChannel("正压控压压力变化", VP2s.ToArray()) }
        });
        ctx.RecordProcessData(new ProcessDataSeries {
            StartedAt = DateTime.Now,
            TimeSec = Enumerable.Range(0, 1).Select(i => (double)i).ToArray(),
            Channels = new[] { new ProcessChannel("正压压力变化", P2s.ToArray()) }
        });
        op.Report(pass ? "✓ 低压量程压力泄露测试和排空测试通过" : "✗ 低压量程压力泄露测试和排空测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("低压量程压力泄露测试和排空测试通过") : StepResult.Fail("低压量程压力泄露测试和排空测试未通过");
    }
}

/// <summary>
/// 高压量程压力泄露测试和排空测试。PORT: 旧脚本方法 LeakTestComposition_High_MP（JSON Entry: LeakTestComposition_High_MP）。
/// </summary>
public sealed class LeakTestComposition_High_MPConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "LeakTestComposition_High_MP";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_MP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        // G10: 旧 List<DataBase>/StringBuilder tvalue 改为 StringBuilder，最终 op.Report(tvalue)
        var tvalue = new StringBuilder();
        tvalue.Append("压力值,高压温度,低压温度,泵温度,电测板温度^");
        var tstr = "";
        Pressure getInternalModulePressureOrg = new Pressure(0, "kPa");
        double rate = 0;
        DateTime starTime = DateTime.Now;

        await op.Sleep(500);
        //获取条件

        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Open" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Open" }, ct);
        Pressure AtmosSensor = new Pressure(0, "kPa");

        await op.Dut.CommandAsync("GetBatteryValue", null, ct);

        await op.Dut.CommandAsync("GetAtmosSensor", null, ct);
        // 切换高压量程（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetControlPressureModel", new[]{ "High" }, ct), "切换高压量程"))) pass = false;
        await op.Sleep(5000);

        // 设定内部模块压力单位（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetPressureUnit_IPM", null, ct), "设定内部模块压力单位"))) pass = false;

        // 获取压力控制量程范围（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetSetPointLimitPressureRange", null, ct), "获取压力控制量程范围"))) pass = false;
        // DevicePressureRange 仅用于旧脚本日志，本平台不再使用（dead variable，已删除）

        Pressure InnerModulePressureUpper = new Pressure(0, "kPa");//量程上限
        Pressure getInternalModulePressure30SFirst = new Pressure(0, "kPa");
        Pressure getInternalModulePressure30SSecond = new Pressure(0, "kPa");
        Pressure getSourcePressure30SFirst = new Pressure(0, "kPa");
        Pressure getSourcePressure30SSecond = new Pressure(0, "kPa");
        double positiveinternalPressureRate = double.MaxValue;
        double positiveSupplyPressureRate = double.MaxValue;
        // state/state4 在新平台用字符串 stateTxt.Contains("Stable") 判断（见 BP 文件），无需声明枚举

        await op.Dut.CommandAsync("GetPressureLowerer_IPM", null, ct);

        Pressure InnerModulePressureLowerer = new Pressure(0, "kPa");//量程下限
        // 30s First/Second 已在上面声明，此处为旧脚本 reset（赋 0 即可）
        getInternalModulePressure30SFirst = new Pressure(0, "kPa");
        getInternalModulePressure30SSecond = new Pressure(0, "kPa");
        getSourcePressure30SFirst = new Pressure(0, "kPa");
        getSourcePressure30SSecond = new Pressure(0, "kPa");
        double negativeinternalPressureRate = double.MaxValue;
        double negativeSupplyPressureRate = double.MaxValue;

        // 获取内部模块量程下限（规则4）
        var lowTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetPressureControlRange_LowerLimit", null, ct), "获取内部模块量程下限");
        if (lowTxt is not null && double.TryParse(lowTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var lowVal))
            InnerModulePressureLowerer = new Pressure(lowVal, "kPa");
        else
            pass = false;

        // 设置压力目标（规则2）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ InnerModulePressureLowerer.ToString() }, ct), "设置压力目标"))) pass = false;
        // state 在新平台用字符串 stateTxt.Contains("Stable") 判断（见 BP 文件），无需声明枚举

        var VP1s = new List<double>();

        if (!(await op.Dut.QueryBooleanAsync("GetControllerModuleConfig", null, ct))) { op.Report("GetControllerModuleConfig 调用失败", RealtimeLevel.Error); pass = false; }
        starTime = DateTime.Now;
        // 旧 while(true) 轮询 GetPressure_IPM 直到 rate>=0.95（控压到位）或超时（规则5）
        var lowPollGuard = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await op.Dut.CommandAsync("GetPressureStableState", null, ct);
            var ipmTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipmVal))
                getInternalModulePressureOrg = new Pressure(ipmVal, "kPa");
            rate = Math.Abs(getInternalModulePressureOrg.Value / InnerModulePressureLowerer.Value);
            if (rate >= 0.95) { op.Report($"打压完成,耗时{(DateTime.Now - starTime).TotalSeconds} s"); break; }
            if (++lowPollGuard > 600) { op.Report($"打压失败,耗时{(DateTime.Now - starTime).TotalSeconds} s，时间超过了指标，性能达不到要求。当前压力{getInternalModulePressureOrg}，没有达到目标点{InnerModulePressureLowerer}。", RealtimeLevel.Warn); pass = false; break; }
            op.Report($"当前压力{getInternalModulePressureOrg}，没有达到目标点{InnerModulePressureLowerer}。{(DateTime.Now - starTime).TotalSeconds} s");
            VP1s.Add(getInternalModulePressureOrg.Value);
            await op.Sleep(500);
        }

        await op.Sleep(2000);

        // 设置控制器测量模式（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTestMode", null, ct), "设置控制器测量模式"))) pass = false;

        await op.Sleep(50);

        // 获取内部模块压力（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        // 30秒前负压气源值（规则4）
        var vpFirstTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetVacuumPressure", null, ct), "获取负压气源压力");
        if (vpFirstTxt is not null && double.TryParse(vpFirstTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var vpFirstVal))
            getSourcePressure30SFirst = new Pressure(vpFirstVal, "kPa");
        else
            pass = false;

        var P1s = new List<double>();

        // 旧 while(true) 30 秒轮询：每 150ms 读 IPM + Dev_T，append tvalue + P1s（规则5）
        {
            var neg30SStart = DateTime.Now;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                // 规则4：QueryBoolean+QueryText → TryQueryValue 分别读取
                var infoTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetPressure_IPM", null, ct), "读取内部模块压力");
                if (infoTxt is null) { pass = false; break; }
                double infoVal = 0;
                if (double.TryParse(infoTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    infoVal = v;
                tstr = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetDev_T", null, ct), "读取Dev_T温度");
                if (tstr is null) { pass = false; break; }
                tvalue.Append($"{infoVal},{tstr};");
                P1s.Add(infoVal);
                if ((DateTime.Now - neg30SStart).TotalSeconds > 30) break;
                await op.Sleep(150);
            }
            op.Report(tvalue.ToString());
        }

        // 获取内部模块压力（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        // 30秒后负压气源值（规则4）
        var vpSecondTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetVacuumPressure", null, ct), "获取负压气源压力");
        if (vpSecondTxt is not null && double.TryParse(vpSecondTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var vpSecondVal))
            getSourcePressure30SSecond = new Pressure(vpSecondVal, "kPa");
        else
            pass = false;

        op.Report($"负压30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.NegativeExport, Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        negativeinternalPressureRate = Math.Abs((Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value)) / getInternalModulePressure30SFirst.Value);
        op.Report($"负压30秒泄露率: {(negativeinternalPressureRate * 100).ToString("F5") + " %"}");

        op.Report($"负压气源压力30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.NegativeSource, Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        negativeSupplyPressureRate = Math.Abs((Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value)) / getSourcePressure30SFirst.Value);
        op.Report($"负压气源压力30秒泄露率: {(negativeSupplyPressureRate * 100).ToString("F5") + " %"}");

        await op.Dut.CommandAsync("SetVentMode", null, ct);
        await op.Sleep(10000);
        // G8: 旧 goto tryagain 已由 TryCommand 自动重试替代（无弹窗）

        // 读取当前压力（规则2）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "读取当前压力"))) pass = false;
        // 排空（规则2）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetVentMode", null, ct), "排空"))) pass = false;
        await op.Sleep(3000);
        // 旧 while(true) 轮询 GetPressureStableState 直到 Stable（bounded 防死等）（规则5）
        var stableGuard = 0;
        while (true)
        {
            var stateTxt = await op.Dut.QueryTextAsync("GetPressureStableState", null, ct);
            if (stateTxt.Contains("Stable", StringComparison.OrdinalIgnoreCase)) break;
            if (++stableGuard > 600) { op.Report("等待压力稳定超时(300s)", RealtimeLevel.Warn); pass = false; break; }
            await op.Sleep(500);
        }
        // 获取内部模块压力（规则2）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        // 获取内部模块量程上限（规则4）
        var upTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetPressureControlRange_UpperLimit", null, ct), "获取内部模块量程上限");
        if (upTxt is not null && double.TryParse(upTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var upVal))
            InnerModulePressureUpper = new Pressure(upVal, "kPa");
        else
            pass = false;

        // 设置压力目标（规则2）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ InnerModulePressureUpper.ToString() }, ct), "设置压力目标"))) pass = false;

        var VP2s = new List<double>();

        if (!(await op.Dut.QueryBooleanAsync("GetControllerModuleConfig", null, ct))) { op.Report("GetControllerModuleConfig 调用失败", RealtimeLevel.Error); pass = false; }
        starTime = DateTime.Now;
        // 旧 while(true) 轮询 GetPressure_IPM 直到 rate>=0.95（控压到位）或超时（规则5）
        var upperPollGuard = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await op.Dut.CommandAsync("GetPressureStableState", null, ct);
            var ipmTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipmVal))
                getInternalModulePressureOrg = new Pressure(ipmVal, "kPa");
            rate = Math.Abs(getInternalModulePressureOrg.Value / InnerModulePressureUpper.Value);
            if (rate >= 0.95) { op.Report($"打压完成,耗时{(DateTime.Now - starTime).TotalSeconds} s"); break; }
            if (++upperPollGuard > 600) { op.Report($"打压失败,耗时{(DateTime.Now - starTime).TotalSeconds} s，时间超过了指标，性能达不到要求。当前压力{getInternalModulePressureOrg}，没有达到目标点{InnerModulePressureUpper}。", RealtimeLevel.Warn); pass = false; break; }
            op.Report($"当前压力{getInternalModulePressureOrg}，没有达到目标点{InnerModulePressureUpper}。{(DateTime.Now - starTime).TotalSeconds} s");
            VP2s.Add(getInternalModulePressureOrg.Value);
            await op.Sleep(500);
        }

        await op.Sleep(2000);

        // 设置控制器测量模式（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTestMode", null, ct), "设置控制器测量模式"))) pass = false;

        var P2s = new List<double>();

        // 旧 while(true) 30 秒轮询：每 150ms 读 IPM + Dev_T，append tvalue + P2s（规则5）
        {
            var pos30SStart = DateTime.Now;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                // 规则4：QueryBoolean+QueryText → TryQueryValue 分别读取
                var infoTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetPressure_IPM", null, ct), "读取内部模块压力");
                if (infoTxt is null) { pass = false; break; }
                double infoVal = 0;
                if (double.TryParse(infoTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    infoVal = v;
                tstr = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetDev_T", null, ct), "读取Dev_T温度");
                if (tstr is null) { pass = false; break; }
                tvalue.Append($"{infoVal},{tstr};");
                P2s.Add(infoVal);
                if ((DateTime.Now - pos30SStart).TotalSeconds > 30) break;
                await op.Sleep(150);
            }
            op.Report(tvalue.ToString());
        }

        // 获取内部模块压力（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        // 30秒前正压气源值（规则4）
        var spFirstTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetSupplyPressure", null, ct), "获取正压气源压力");
        if (spFirstTxt is not null && double.TryParse(spFirstTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var spFirstVal))
            getSourcePressure30SFirst = new Pressure(spFirstVal, "kPa");
        else
            pass = false;

        await op.Sleep(50);

        // 获取内部模块压力（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        // 30秒后正压气源值（规则4）
        var spSecondTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetSupplyPressure", null, ct), "获取正压气源压力");
        if (spSecondTxt is not null && double.TryParse(spSecondTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var spSecondVal))
            getSourcePressure30SSecond = new Pressure(spSecondVal, "kPa");
        else
            pass = false;

        op.Report($"正压30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.PositiveExport, Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        positiveinternalPressureRate = Math.Abs((Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value)) / getInternalModulePressure30SFirst.Value);
        op.Report($"正压30秒泄露率: {(positiveinternalPressureRate * 100).ToString("F5") + " %"}");

        op.Report($"正压气源压力30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.PositiveSource, Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        positiveSupplyPressureRate = Math.Abs((Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value)) / getSourcePressure30SFirst.Value);
        op.Report($"正压气源压力30秒泄露率: {(positiveSupplyPressureRate * 100).ToString("F5") + "%"}");

        await op.Dut.CommandAsync("SetVentMode", null, ct);
        await op.Sleep(10000);
        // G8: 旧 goto tryPagain 已由 TryCommand 自动重试替代（无弹窗）

        // 读取当前压力（规则2）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "读取当前压力"))) pass = false;

        // 排空（规则2）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetVentMode", null, ct), "排空"))) pass = false;
        await op.Sleep(3000);

        // 旧 while(true) 轮询 GetPressureStableState 直到 Stable（bounded 防死等）（规则5）
        {
            var stableGuard2 = 0;
            while (true)
            {
                var stateTxt = await op.Dut.QueryTextAsync("GetPressureStableState", null, ct);
                if (stateTxt.Contains("Stable", StringComparison.OrdinalIgnoreCase)) break;
                if (++stableGuard2 > 600) { op.Report("等待压力稳定超时(300s)", RealtimeLevel.Warn); pass = false; break; }
                await op.Sleep(500);
            }
        }
        // 获取内部模块压力（规则2）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        await op.Sleep(2000);
        await op.Dut.CommandAsync("GetBatteryValue", null, ct);

        op.Report($"压力值与温度值: {tvalue.ToString()}");

        await op.Dut.CommandAsync("SetVentMode", null, ct);
        // state4 用字符串 stateTxt.Contains("Stable") 判断（见 BP 文件），无需声明枚举

        // 旧 while(true) 轮询 GetPressureStableState 直到 Stable（bounded 防死等）（规则5）
        {
            var stableGuard2 = 0;
            while (true)
            {
                var stateTxt = await op.Dut.QueryTextAsync("GetPressureStableState", null, ct);
                if (stateTxt.Contains("Stable", StringComparison.OrdinalIgnoreCase)) break;
                if (++stableGuard2 > 600) { op.Report("等待压力稳定超时(300s)", RealtimeLevel.Warn); pass = false; break; }
                await op.Sleep(500);
            }
        }
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Close" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Close" }, ct);

        ctx.RecordProcessData(new ProcessDataSeries {
            StartedAt = DateTime.Now,
            TimeSec = Enumerable.Range(0, 1).Select(i => (double)i).ToArray(),
            Channels = new[] { new ProcessChannel("负压控压压力变化", VP1s.ToArray()) }
        });
        ctx.RecordProcessData(new ProcessDataSeries {
            StartedAt = DateTime.Now,
            TimeSec = Enumerable.Range(0, 1).Select(i => (double)i).ToArray(),
            Channels = new[] { new ProcessChannel("负压压力变化", P1s.ToArray()) }
        });
        ctx.RecordProcessData(new ProcessDataSeries {
            StartedAt = DateTime.Now,
            TimeSec = Enumerable.Range(0, 1).Select(i => (double)i).ToArray(),
            Channels = new[] { new ProcessChannel("正压控压压力变化", VP2s.ToArray()) }
        });
        ctx.RecordProcessData(new ProcessDataSeries {
            StartedAt = DateTime.Now,
            TimeSec = Enumerable.Range(0, 1).Select(i => (double)i).ToArray(),
            Channels = new[] { new ProcessChannel("正压压力变化", P2s.ToArray()) }
        });
        op.Report(pass ? "✓ 高压量程压力泄露测试和排空测试通过" : "✗ 高压量程压力泄露测试和排空测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("高压量程压力泄露测试和排空测试通过") : StepResult.Fail("高压量程压力泄露测试和排空测试未通过");
    }
}

/// <summary>
/// 加压流程测试。PORT: 旧脚本方法 TestFlow（JSON Entry: TestFlow）。
/// </summary>
public sealed class TestFlowConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestFlow";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_MP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;

        //获取条件
        //控压最大用时
        var PressureFirst = op.Cond("第一个压力值");
        var PressureSecond = op.Cond("第二个压力值");

        double firstVal = 0, secondVal = 0;
        if (PressureFirst is not null && double.TryParse(PressureFirst.Expected ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture, out var pf)) firstVal = pf;
        if (PressureSecond is not null && double.TryParse(PressureSecond.Expected ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture, out var ps)) secondVal = ps;
        Pressure PressureFirstValue = new Pressure(firstVal, "kPa");
        Pressure PressureSecondValue = new Pressure(secondVal, "kPa");
        Pressure pressure = new Pressure(0, "kPa");
        DateTime StarTimePressUp = DateTime.Now;

        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Open" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Open" }, ct);

        // 切换高压量程（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetControlPressureModel", new[]{ "High" }, ct), "切换高压量程"))) pass = false;
        await op.Sleep(2000);

        // 设定内部模块压力单位（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetPressureUnit_IPM", null, ct), "设定内部模块压力单位"))) pass = false;

        // 设定第一个压力值（规则2）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ PressureFirstValue.ToString() }, ct), $"设定{PressureFirstValue}压力值"))) pass = false;

        var VP1s = new List<double>();

        if (!(await op.Dut.QueryBooleanAsync("GetControllerModuleConfig", null, ct))) { op.Report("GetControllerModuleConfig 调用失败", RealtimeLevel.Error); pass = false; }
        StarTimePressUp = DateTime.Now;
        {
            var pTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(pTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var pVal))
                pressure = new Pressure(pVal, "kPa");
            VP1s.Add(pressure.Value);
        }
        await op.Sleep(500);
        op.Report(pressure.ToString() + $"   √ 耗时{(DateTime.Now - StarTimePressUp).TotalSeconds} s");

        await op.Sleep(2000);

        // 设置控制器测量模式（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTestMode", null, ct), "设置控制器测量模式"))) pass = false;

        await op.Dut.CommandAsync("GetPressure_IPM", null, ct);

        // 设定第二个压力值（规则2）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ PressureSecondValue.ToString() }, ct), $"设定{PressureSecondValue}压力值"))) pass = false;

        StarTimePressUp = DateTime.Now;
        {
            var p2Txt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(p2Txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var p2Val))
                pressure = new Pressure(p2Val, "kPa");
            VP1s.Add(pressure.Value);
        }
        await op.Sleep(500);
        op.Report(pressure.ToString() + $"   √ 耗时{(DateTime.Now - StarTimePressUp).TotalSeconds} s");

        await op.Sleep(2000);

        // 设置控制器测量模式（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTestMode", null, ct), "设置控制器测量模式"))) pass = false;

        await op.Dut.CommandAsync("GetPressure_IPM", null, ct);

        await op.Dut.CommandAsync("SetVentMode", null, ct);
        // 旧 PressureStableState state = UnKnown; GetPressureStableState(out state) && state == Stable
        var ventStateTxt = await op.Dut.QueryTextAsync("GetPressureStableState", null, ct);
        var ventStable = ventStateTxt.Contains("Stable", StringComparison.OrdinalIgnoreCase);
        op.Report($"排空稳定: {ventStable}");
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Close" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Close" }, ct);

        ctx.RecordProcessData(new ProcessDataSeries {
            StartedAt = DateTime.Now,
            TimeSec = Enumerable.Range(0, VP1s.Count).Select(idx => (double)idx).ToArray(),
            Channels = new[] { new ProcessChannel("控压压力变化", VP1s.ToArray()) }
        });
        op.Report(pass ? "✓ 加压流程测试通过" : "✗ 加压流程测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("加压流程测试通过") : StepResult.Fail("加压流程测试未通过");
    }
}

/// <summary>
/// V6阀开启功能测试。PORT: 旧脚本方法 V6ValveOpenTest（JSON Entry: V6ValveOpenTest）。
/// </summary>
public sealed class V6ValveOpenTestConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "V6ValveOpenTest";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_MP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;

        await op.Dut.CommandAsync("GetBatteryValue", null, ct);
        await op.Sleep(500);

        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Close" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Close" }, ct);
        // 设定内部模块压力单位（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetPressureUnit_IPM", null, ct), "设定内部模块压力单位"))) pass = false;

        Pressure innerPressureUpper = new Pressure(0, "kPa");

        // 读取内部模块量程上限（规则3+9：从设备读取实际值，替换 innerPressureUpper 零值占位）
        var upLimitTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetPressureControlRange_UpperLimit", null, ct), "读取内部模块量程上限");
        if (upLimitTxt is not null && double.TryParse(upLimitTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var upVal))
            innerPressureUpper = new Pressure(upVal, "kPa");
        else
            pass = false;

        // 读取压力模块量程上下限（规则9：从设备读取实际值，替换 ModulePressure 零值占位）
        var mpUpperTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetPressureControlRange_UpperLimit", null, ct), "读取压力模块量程上限");
        var mpLowerTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetPressureControlRange_LowerLimit", null, ct), "读取压力模块量程下限");
        double mpUpper = 0, mpLower = 0;
        if (mpUpperTxt is not null && double.TryParse(mpUpperTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var uv))
            mpUpper = uv;
        else
            pass = false;
        if (mpLowerTxt is not null && double.TryParse(mpLowerTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var lv))
            mpLower = lv;
        else
            pass = false;
        PressureRange ModulePressure = new PressureRange(mpLower, mpUpper, "kPa");

        // 设定内部模块压力目标（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ innerPressureUpper.ToString() }, ct), "设定内部模块压力"))) pass = false;

        // 获取内部模块压力（规则2）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;
        await op.Sleep(100);
        await op.Sleep(1000);

        // 设定排空模式（规则2：fire+ConfirmAsync → TryCommand）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetVentMode", null, ct), "设定排空模式"))) pass = false;
        await op.Sleep(3000);

        // 设定测试模式（规则2：fire+ConfirmAsync → TryCommand）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTestMode", null, ct), "设定测试模式"))) pass = false;
        await op.Sleep(5000);

        // 开启V1和V6阀（规则1：空成功分支+ConfirmAsync → TryCommand）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetValveStata", new[]{ "33" }, ct), "开启V1和V6阀"))) pass = false;
        await op.Sleep(5000);
        await op.Dut.CommandAsync("SetValveStata", new[]{ "33" }, ct);

        // 获取正压气源（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetSupplyPressure", null, ct), "获取正压气源"))) pass = false;

        // 设定压力模块量程上限目标（规则1；ModulePressure.UpperValue 已从设备读取实际值）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ ModulePressure.UpperValue.ToString() }, ct), "设定压力模块量程上限"))) pass = false;

        await op.Sleep(2000);
        await op.Dut.CommandAsync("GetPressure_IPM", null, ct);
        await op.Sleep(1000);
        // G8: 旧 goto tryagain 已由 TryCommand 自动重试替代（无弹窗）

        // 读取压力稳定状态（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressureStableState", null, ct), "读取压力稳定状态"))) pass = false;

        await op.Dut.CommandAsync("SetVentMode", null, ct);
        await op.Sleep(5000);

        await op.Sleep(2000);
        await op.Dut.CommandAsync("GetPressure_IPM", null, ct);
        // 读取压力稳定状态（规则1）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressureStableState", null, ct), "读取压力稳定状态"))) pass = false;

        await op.Dut.CommandAsync("SetVentMode", null, ct);

        await op.Sleep(2000);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Close" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Close" }, ct);

        op.Report(pass ? "✓ V6阀开启功能测试通过" : "✗ V6阀开启功能测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("V6阀开启功能测试通过") : StepResult.Fail("V6阀开启功能测试未通过");
    }
}
