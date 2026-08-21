using System.Globalization;
using System.IO.Ports;
using System.Text.RegularExpressions;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;

namespace SYST.TestSteps.ConST811A.ConST811A_BP_Machine;

/// <summary>
/// ConST811A 主板（设备族 ConST811A）测试**设备特有**处理器集合。**自动转换**自旧
/// <c>ConST811A_MainBoard_Auto.cs</c> 的测试方法与 <c>.distributed.json</c> 任务配置：继电器指令序列
/// （GZP21 共享工装）、被检指令与 Range 判定。气象版不接 P06/ConST810 标准模块（电压/电流采样）。
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

    /// <summary>步骤开始：报告正在执行的操作（用 ○ 标记）。</summary>
    public void Step(string desc) => Report($"○ {desc}");

    /// <summary>步骤成功：报告操作完成（用 ✓ 标记）。</summary>
    public void Ok(string desc) => Report($"✓ {desc}", RealtimeLevel.Success);

    /// <summary>步骤失败：报告操作失败（用 ✗ 标记）。</summary>
    public void Fail(string desc) => Report($"✗ {desc}", RealtimeLevel.Error);

    /// <summary>报告读取到的值。</summary>
    public void Value(string label, double value, string unit = "")
        => Report($"  {label}: {F(value)}{unit}");

    /// <summary>报告读取到的文本值。</summary>
    public void Text(string label, string value)
        => Report($"  {label}: {value}");

    /// <summary>报告条件判定结果。</summary>
    public void Verdict(string label, bool passed, string detail)
        => Report($"  {label}: {(passed ? "合格" : "不合格")} - {detail}", passed ? RealtimeLevel.Info : RealtimeLevel.Warn);

    /// <summary>真机稳定延时（继电器切档/设值后需等待）。PORT: 旧 Thread.Sleep / ScriptHelper.Thread_Sleep。</summary>
    public Task Sleep(int ms)
    {
        Report($"  等待稳定 {ms}ms ...");
        return Task.Delay(ms, _ct);
    }

    /// <summary>带日志的等待，显示原因。</summary>
    public Task Sleep(int ms, string reason)
    {
        Report($"  {reason}，等待 {ms}ms ...");
        return Task.Delay(ms, _ct);
    }

    /// <summary>发共享工装输出指令（按名称映射到 GZP21 通道）。</summary>
    public Task Relay(string cmd)
    {
        Report($"  工装输出指令：{cmd}");
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
                // 气象版不接 P06 标准模块；旧脚本遗留的 P06 电压/电流调用按跳过处理（无采样设备）
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
}

/// <summary>
/// 高压量程压力泄露测试和排空测试。PORT: 旧脚本方法 LeakTestSingle_BP（JSON Entry: LeakTestSingle_BP）。
/// </summary>
public sealed class LeakTestSingle_BPConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "LeakTestSingle_BP";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        // G10: 旧 List<TextData>/StringBuilder tvalue 改为 string 拼接，最终 op.Report
        var tvalue = "压力值,高压温度,低压温度,泵温度,电测板温度^";
        var tstr = "";
        Pressure getInternalModulePressureOrg = new Pressure(0, "kPa");
        var rate = 0.0;

        await Task.Delay(500, ct);
        //获取条件
        
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Open" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Open" }, ct);
        
        Pressure AtmosSensor = new Pressure(0, "kPa");
        await op.Dut.CommandAsync("GetAtmosSensor", null, ct);
        
        if ((await op.Dut.QueryBooleanAsync("SetPressureUnit_IPM", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("设定内部模块压力单位失败,重试？", ct))) pass = false;
        
        if ((await op.Dut.QueryBooleanAsync("GetSetPointLimitPressureRange", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("获取压力控制量程范围失败,重试？", ct))) pass = false;
        
        Pressure InnerModulePressureUpper = new Pressure(0, "kPa");//量程上限
        Pressure getInternalModulePressure30SFirst = new Pressure(0, "kPa");
        Pressure getInternalModulePressure30SSecond = new Pressure(0, "kPa");
        Pressure getSourcePressure30SFirst = new Pressure(0, "kPa");
        Pressure getSourcePressure30SSecond = new Pressure(0, "kPa");
        double positiveinternalPressureRate = double.MaxValue;
        double positiveSupplyPressureRate = double.MaxValue;
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressureControlRange_UpperLimit", null, ct))) { op.Report("GetPressureControlRange_UpperLimit 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var upTxt = await op.Dut.QueryTextAsync("GetPressureControlRange_UpperLimit", null, ct);
            if (double.TryParse(upTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var upVal))
                InnerModulePressureUpper = new Pressure(upVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取内部模块量程上限失败,重试？", ct))) pass = false;

        if (!(await op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ InnerModulePressureUpper.ToString() }, ct))) { op.Report("SetTargetPressure 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("设置压力目标失败,重试？", ct))) pass = false; { }
        // 旧 while(true) 轮询 GetPressure_IPM 直到 rate<0.05（控压到位）或超时
        var upperPollGuard = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var ipmTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipmVal))
                getInternalModulePressureOrg = new Pressure(ipmVal, "kPa");
            rate = Math.Abs((getInternalModulePressureOrg.Value - InnerModulePressureUpper.Value) / InnerModulePressureUpper.Value);
            if (rate < 0.05) { op.Report("上限打压完成"); break; }
            if (++upperPollGuard > 600) { op.Report("上限打压超时(300s)", RealtimeLevel.Warn); pass = false; break; }
            await Task.Delay(500, ct);
        }
        
        await Task.Delay(2000, ct);
        
        if ((await op.Dut.QueryBooleanAsync("SetTestMode", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("设置控制器测量模式失败,重试？", ct))) pass = false;
        
        await Task.Delay(50, ct);
        
        if ((await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("获取内部模块压力失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await op.Dut.QueryBooleanAsync("GetDev_T", null, ct))) { op.Report("GetDev_T 调用失败", RealtimeLevel.Error); pass = false; }
        {
            var pfTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(pfTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var pfVal))
                getInternalModulePressure30SFirst = new Pressure(pfVal, "kPa");
            tstr = await op.Dut.QueryTextAsync("GetDev_T", null, ct);
            tvalue += $"{getInternalModulePressure30SFirst.Value},{tstr};";
            op.Report(tvalue);
        }

        await Task.Delay(50, ct);

        if ((await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("获取内部模块压力失败,重试？", ct))) pass = false;
        
        op.Report($"上限30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.Hmp, LeakPosition.PositiveExport, Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        positiveinternalPressureRate = Math.Abs((Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value)) / getInternalModulePressure30SFirst.Value);
        op.Report($"上限30秒泄露率: {positiveinternalPressureRate.ToString("F4")}");
        
        // 30秒前正压气源值
        if (!(await op.Dut.QueryBooleanAsync("GetSupplyPressure", null, ct))) { op.Report("GetSupplyPressure 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var spFirstTxt = await op.Dut.QueryTextAsync("GetSupplyPressure", null, ct);
            if (double.TryParse(spFirstTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var spFirstVal))
                getSourcePressure30SFirst = new Pressure(spFirstVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取正压气源压力失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await op.Dut.QueryBooleanAsync("GetDev_T", null, ct))) { op.Report("GetDev_T 调用失败", RealtimeLevel.Error); pass = false; }
        {
            var pf2Txt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(pf2Txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var pf2Val))
                getInternalModulePressure30SFirst = new Pressure(pf2Val, "kPa");
            tstr = await op.Dut.QueryTextAsync("GetDev_T", null, ct);
            tvalue += $"{getInternalModulePressure30SFirst.Value},{tstr};";
            op.Report(tvalue);
        }

        await Task.Delay(50, ct);

        // 30秒后正压气源值
        if (!(await op.Dut.QueryBooleanAsync("GetSupplyPressure", null, ct))) { op.Report("GetSupplyPressure 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var spSecondTxt = await op.Dut.QueryTextAsync("GetSupplyPressure", null, ct);
            if (double.TryParse(spSecondTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var spSecondVal))
                getSourcePressure30SSecond = new Pressure(spSecondVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取正压气源压力失败,重试？", ct))) pass = false;
        
        op.Report($"正压气源压力30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.Hmp, LeakPosition.PositiveSource, Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value), 30, AtmosSensor.Value))}");

        positiveSupplyPressureRate = Math.Abs((Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value)) / getSourcePressure30SFirst.Value);
        op.Report($"正压气源压力30秒泄露率: {positiveSupplyPressureRate.ToString("F4")}");
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("读取当前压力失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("SetVentMode", null, ct))) { op.Report("SetVentMode 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("排空失败,重试？", ct))) pass = false;
        await Task.Delay(3000, ct);
        await Task.Delay(2000, ct);
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("获取内部模块失败,重试？", ct))) pass = false;
        
        Pressure InnerModulePressureLowerer = new Pressure(0, "kPa");//量程上限
        getInternalModulePressure30SFirst = new Pressure(0, "kPa");
        getInternalModulePressure30SSecond = new Pressure(0, "kPa");
        getSourcePressure30SFirst = new Pressure(0, "kPa");
        getSourcePressure30SSecond = new Pressure(0, "kPa");
        double negativeinternalPressureRate = double.MaxValue;
        double negativeSupplyPressureRate = double.MaxValue;
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressureControlRange_LowerLimit", null, ct))) { op.Report("GetPressureControlRange_LowerLimit 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var lowTxt = await op.Dut.QueryTextAsync("GetPressureControlRange_LowerLimit", null, ct);
            if (double.TryParse(lowTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var lowVal))
                InnerModulePressureLowerer = new Pressure(lowVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取内部模块量程下限失败,重试？", ct))) pass = false;

        if (!(await op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ InnerModulePressureLowerer.ToString() }, ct))) { op.Report("SetTargetPressure 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("设置压力目标失败,重试？", ct))) pass = false;
        // 旧 while(true) 轮询 GetPressure_IPM 直到 rate<=0.05（控压到位）或超时
        var lowerPollGuard = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var ipmLowTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmLowTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipmLowVal))
                getInternalModulePressureOrg = new Pressure(ipmLowVal, "kPa");
            rate = Math.Abs((getInternalModulePressureOrg.Value - InnerModulePressureLowerer.Value) / InnerModulePressureLowerer.Value);
            if (rate <= 0.05) { op.Report("下限打压完成"); break; }
            if (++lowerPollGuard > 600) { op.Report("下限打压超时(300s)", RealtimeLevel.Warn); pass = false; break; }
            await Task.Delay(500, ct);
        }
        
        await Task.Delay(2000, ct);
        
        if ((await op.Dut.QueryBooleanAsync("SetTestMode", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("设置控制器测量模式失败,重试？", ct))) pass = false;
        
        await Task.Delay(50, ct);
        
        if ((await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("获取内部模块压力失败,重试？", ct))) pass = false;
        
        await Task.Delay(50, ct);
        
        if ((await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("获取内部模块压力失败,重试？", ct))) pass = false;
        
        op.Report($"下限30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.Hmp, LeakPosition.NegativeExport, Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value), 30, AtmosSensor.Value))}");

        negativeinternalPressureRate = Math.Abs((Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value)) / Math.Abs(100 - getInternalModulePressure30SFirst.Value));
        op.Report($"下限30秒泄露率: {negativeinternalPressureRate.ToString("F4")}");
        
        // 30秒前负压气源值
        if (!(await op.Dut.QueryBooleanAsync("GetVacuumPressure", null, ct))) { op.Report("GetVacuumPressure 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var vpFirstTxt = await op.Dut.QueryTextAsync("GetVacuumPressure", null, ct);
            if (double.TryParse(vpFirstTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var vpFirstVal))
                getSourcePressure30SFirst = new Pressure(vpFirstVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取负压气源压力失败,重试？", ct))) pass = false;

        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await op.Dut.QueryBooleanAsync("GetDev_T", null, ct))) { op.Report("GetDev_T 调用失败", RealtimeLevel.Error); pass = false; }
        {
            var pf3Txt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(pf3Txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var pf3Val))
                getInternalModulePressure30SFirst = new Pressure(pf3Val, "kPa");
            tstr = await op.Dut.QueryTextAsync("GetDev_T", null, ct);
            tvalue += $"{getInternalModulePressure30SFirst.Value},{tstr};";
            op.Report(tvalue);
        }

        await Task.Delay(50, ct);

        // 30秒后负压气源值
        if (!(await op.Dut.QueryBooleanAsync("GetVacuumPressure", null, ct))) { op.Report("GetVacuumPressure 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var vpSecondTxt = await op.Dut.QueryTextAsync("GetVacuumPressure", null, ct);
            if (double.TryParse(vpSecondTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var vpSecondVal))
                getSourcePressure30SSecond = new Pressure(vpSecondVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取负压气源压力失败,重试？", ct))) pass = false;
        
        op.Report($"负压气源压力30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.Hmp, LeakPosition.NegativeSource, Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value), 30, AtmosSensor.Value))}");

        negativeSupplyPressureRate = Math.Abs((Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value)) / Math.Abs(100 - getSourcePressure30SFirst.Value));
        op.Report($"负压气源压力30秒泄露率: {negativeSupplyPressureRate.ToString("F4")}");
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("读取当前压力失败,重试？", ct))) pass = false;
        if (!(await op.Dut.QueryBooleanAsync("SetVentMode", null, ct))) { op.Report("SetVentMode 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("排空失败,重试？", ct))) pass = false;
        await Task.Delay(5000, ct);
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("获取内部模块失败,重试？", ct))) pass = false;
        
        await Task.Delay(2000, ct);
        await op.Dut.CommandAsync("SetVentMode", null, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Close" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Close" }, ct);
        
        op.Report(pass ? "✓ 高压量程压力泄露测试和排空测试通过" : "✗ 高压量程压力泄露测试和排空测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("高压量程压力泄露测试和排空测试通过") : StepResult.Fail("高压量程压力泄露测试和排空测试未通过");
    }
}

/// <summary>
/// 压力控制测试。PORT: 旧脚本方法 PressureControlTest_BP（JSON Entry: PressureControlTest_BP）。
/// </summary>
public sealed class PressureControlTest_BPConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "PressureControlTest_BP";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        
        await Task.Delay(500, ct);
        //获取条件
        //控压最大用时
        Pressure setInnerPressure = new Pressure(0, "kPa");
        Pressure InnerModulePressureUpper = new Pressure(0, "kPa");
        Pressure InnerModulePressureLowerer = new Pressure(0, "kPa");
        double pressureStabilityValue = 0.003; //压力稳定误差
        List<double> postiveSetPoint = new List<double>() { 0, 0.05, 1, 0.95 };   //正压设定点 0，5%FS，100%，95%
        List<double> negativeSetPoint = new List<double>() { 0.05, 1, 0.9 };  //负压设定点0，5%-FS,-FS,90%-FS,0
        
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Open" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Open" }, ct);
        // 控压时间与允差条件
        var PositivePressControlTime = op.Cond("正压控压时间");
        var PositivePressControlTime2 = op.Cond("正压5%HP->105%HP控压时间");
        var NegativePressControlTime = op.Cond("负压控压时间");
        var NegativePressControlTime2 = op.Cond("负压5%HP->105%HP控压时间");
        var PositivePressControlToleranceValue = op.Cond("正压设定点控压允差");
        var NegativePressControlToleranceValue = op.Cond("负压设定点控压允差");

        if ((await op.Dut.QueryBooleanAsync("SetPressureUnit_IPM", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("设定内部模块压力单位失败,重试？", ct))) pass = false;

        if (!(await op.Dut.QueryBooleanAsync("SetPressureStability", new[]{ pressureStabilityValue.ToString() }, ct))) { op.Report("SetPressureStability 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("设置压力稳定度失败,重试？", ct))) pass = false;

        if (!(await op.Dut.QueryBooleanAsync("SetOpenMaxControlPressureSpeed", new[]{ "false" }, ct))) { op.Report("SetOpenMaxControlPressureSpeed 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("设置控压速率失败,重试？", ct))) pass = false;

        if (!(await op.Dut.QueryBooleanAsync("GetPressureControlRange_UpperLimit", null, ct))) { op.Report("GetPressureControlRange_UpperLimit 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var upTxt = await op.Dut.QueryTextAsync("GetPressureControlRange_UpperLimit", null, ct);
            if (double.TryParse(upTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var upVal))
                InnerModulePressureUpper = new Pressure(upVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取内部模块正压满量程失败,重试？", ct))) pass = false;
        op.Report($"内部模块量程上限: {InnerModulePressureUpper.ToString()}");

        // 正压控压过程：遍历正压设定点（0, 5%FS, 100%FS, 95%FS）
        int i = 0;
        while (i < postiveSetPoint.Count)
        {
            ct.ThrowIfCancellationRequested();
            var starTimePressUp = DateTime.Now;
            // 重新读取量程上限并计算该设定点目标压力
            {
                var upTxt = await op.Dut.QueryTextAsync("GetPressureControlRange_UpperLimit", null, ct);
                if (double.TryParse(upTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var upVal))
                    InnerModulePressureUpper = new Pressure(upVal, "kPa");
            }
            double resultValue = Math.Floor((100 + (InnerModulePressureUpper.Value - 100) * postiveSetPoint[i]));
            op.Report($"压力点{postiveSetPoint[i]}*FS: {resultValue}{InnerModulePressureUpper.Unit}");
            // setInnerPressure = new Pressure(resultValue, InnerModulePressureUpper.Unit)
            setInnerPressure = new Pressure(resultValue, InnerModulePressureUpper.Unit);
            if (!(await op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ setInnerPressure.ToString() }, ct))) { op.Report("SetTargetPressure 调用失败", RealtimeLevel.Error); pass = false; }
            if (!(await ctx.ConfirmAsync($"设定{resultValue}{InnerModulePressureUpper.Unit}压力值失败,重试？", ct))) { pass = false; break; }
            // isControlLangRange = (postiveSetPoint[i] == 1)
            bool isControlLangRange = postiveSetPoint[i] == 1;
            // 控压情况轮询：直到稳定且在允差内或超时
            var pollGuard = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                // PressureStableState state = PressureStableState.UnKnown
                string stateTxt = await op.Dut.QueryTextAsync("GetPressureStableState", null, ct);
                bool isStable = stateTxt.Contains("Stable", StringComparison.OrdinalIgnoreCase);
                var ipmTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
                double pressureVal = 0;
                if (double.TryParse(ipmTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var pv))
                    pressureVal = pv;
                // if (!GetPressureStableState(out state) || state != Stable || !Judge("正压设定点控压允差", Math.Abs(pressure.Value - resultValue)))
                bool toleranceOk = op.Judge("正压设定点控压允差", Math.Abs(pressureVal - resultValue), "正压控压允差", "%");
                if (isStable && toleranceOk)
                {
                    var span = (DateTime.Now - starTimePressUp).TotalSeconds;
                    op.Report($"控压成功！{resultValue}{InnerModulePressureUpper.Unit}压力点控压时间为{span:F2}秒");
                    i++;
                    break;
                }
                // 超时判定（按条件名）
                var timeCond = isControlLangRange ? PositivePressControlTime2 : PositivePressControlTime;
                var spanSec = (DateTime.Now - starTimePressUp).TotalSeconds;
                if (timeCond is not null && op.Judge(timeCond.Name, spanSec, $"{resultValue}控压时间", "s"))
                {
                    op.Report($"控压失败！{resultValue}{InnerModulePressureUpper.Unit}压力点控压时间超过{spanSec:F2}秒", RealtimeLevel.Warn);
                    pass = false;
                    i++;
                    break;
                }
                if (++pollGuard > 1200) { op.Report($"控压超时({resultValue}{InnerModulePressureUpper.Unit})", RealtimeLevel.Warn); pass = false; i++; break; }
                await Task.Delay(500, ct);
            }
        }
        await op.Dut.CommandAsync("SetVentMode", null, ct);

        if (!(await op.Dut.QueryBooleanAsync("GetPressureControlRange_LowerLimit", null, ct))) { op.Report("GetPressureControlRange_LowerLimit 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var lowTxt = await op.Dut.QueryTextAsync("GetPressureControlRange_LowerLimit", null, ct);
            if (double.TryParse(lowTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var lowVal))
                InnerModulePressureLowerer = new Pressure(lowVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取内部模块量程下限失败,重试？", ct))) pass = false;
        op.Report($"内部模块设定点下限: {InnerModulePressureLowerer.ToString()}");

        await op.Dut.CommandAsync("GetPressureLowerer_IPM", null, ct);

        // 负压控压过程：遍历负压设定点（5%-FS, 100%-FS, 90%-FS）
        int j = 0;
        while (j < negativeSetPoint.Count)
        {
            ct.ThrowIfCancellationRequested();
            var starTimePressDown = DateTime.Now;
            double resultValue = Math.Ceiling(100 + (InnerModulePressureLowerer.Value - 100) * negativeSetPoint[j]);
            op.Report($"压力点{negativeSetPoint[j]}*FS: {resultValue:F2}{InnerModulePressureLowerer.Unit}");
            // setInnerPressure = new Pressure(resultValue, InnerModulePressureLowerer.Unit)
            setInnerPressure = new Pressure(resultValue, InnerModulePressureLowerer.Unit);
            if (!(await op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ setInnerPressure.ToString() }, ct))) { op.Report("SetTargetPressure 调用失败", RealtimeLevel.Error); pass = false; }
            if (!(await ctx.ConfirmAsync($"设定{resultValue:F2}{InnerModulePressureLowerer.Unit}压力值失败,重试？", ct))) { pass = false; break; }
            // isControlLangRange = (negativeSetPoint[j] == 1)
            bool isControlLangRange = negativeSetPoint[j] == 1;
            // 控压情况轮询
            var pollGuard = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                // Xmas11.Comm.Data.Common.PressureStableState state = UnKnown
                string stateTxt = await op.Dut.QueryTextAsync("GetPressureStableState", null, ct);
                bool isStable = stateTxt.Contains("Stable", StringComparison.OrdinalIgnoreCase);
                // Xmas11.Domain.Mechanics.Pressure pressure = new Pressure(0, kPa)
                var ipmTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
                double pressureVal = 0;
                if (double.TryParse(ipmTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var pv))
                    pressureVal = pv;
                // if (!GetPressureStableState(out state) || state != Stable || !Judge("负压设定点控压允差", Math.Abs(pressure.Value - resultValue)))
                bool toleranceOk = op.Judge("负压设定点控压允差", Math.Abs(pressureVal - resultValue), "负压控压允差", "%");
                if (isStable && toleranceOk)
                {
                    var span = (DateTime.Now - starTimePressDown).TotalSeconds;
                    op.Report($"控压成功！{resultValue}{InnerModulePressureLowerer.Unit}压力点控压时间为{span:F2}秒");
                    j++;
                    break;
                }
                var timeCond = isControlLangRange ? NegativePressControlTime2 : NegativePressControlTime;
                var spanSec = (DateTime.Now - starTimePressDown).TotalSeconds;
                if (timeCond is not null && op.Judge(timeCond.Name, spanSec, $"{resultValue}控压时间", "s"))
                {
                    op.Report($"控压失败！{resultValue}{InnerModulePressureLowerer.Unit}压力点控压时间超过{spanSec:F2}秒", RealtimeLevel.Warn);
                    pass = false;
                    j++;
                    break;
                }
                if (++pollGuard > 6000) { op.Report($"控压超时({resultValue}{InnerModulePressureLowerer.Unit})", RealtimeLevel.Warn); pass = false; j++; break; }
                await Task.Delay(100, ct);
            }
        }
        await op.Dut.CommandAsync("SetVentMode", null, ct);

        await Task.Delay(1000, ct);

        await op.Dut.CommandAsync("SetOpenMaxControlPressureSpeed", new[]{ "false" }, ct);
        await op.Dut.CommandAsync("SetVentMode", null, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Close" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Close" }, ct);

        op.Report(pass ? "✓ 压力控制测试通过" : "✗ 压力控制测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("压力控制测试通过") : StepResult.Fail("压力控制测试未通过");
    }
}
