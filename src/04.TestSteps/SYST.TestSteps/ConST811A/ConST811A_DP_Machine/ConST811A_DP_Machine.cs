using System.Globalization;
using System.IO.Ports;
using System.Text.RegularExpressions;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;

namespace SYST.TestSteps.ConST811A.ConST811A_DP_Machine;

/// <summary>
/// ConST811A 主板（设备族 ConST811A）测试**设备特有**处理器集合。**自动转换**自旧
/// <c>ConST811A_MainBoard_Auto.cs</c> 的测试方法与 <c>.distributed.json</c> 任务配置：继电器指令序列
/// （GZP21/P06 共享设备）、电压/电流读数、被检指令与 Range 判定。
/// 工装用 <see cref="IMachineTestTool"/>，被检用 <see cref="IConST811ADut"/>。
/// </summary>
internal sealed class ConST811AOps
{
    private readonly ITestContext _ctx;
    private readonly CancellationToken _ct;

    /// <summary>GZP21 共享工装（继电器输出）。</summary>
    public readonly IMachineTestTool Gzp21;
    /// <summary>P06/ConST810 共享设备（电压/电流采样）。</summary>
    public readonly IMachineTestTool P06;

    /// <summary>被检 ConST811A 专属驱动。</summary>
    public readonly IConST811ADut Dut;

    public ConST811AOps(ITestContext ctx, CancellationToken ct)
    {
        _ctx = ctx;
        _ct = ct;
        Gzp21 = ctx.GetDevice<IMachineTestTool>("GZP21");
        P06 = ctx.GetDevice<IMachineTestTool>("P06");
        Dut = ctx.GetDevice<IConST811ADut>();
    }

    /// <summary>数值格式化（保留三位有效小数）。</summary>
    public static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>推送实时消息。</summary>
    public void Report(string m, RealtimeLevel l = RealtimeLevel.Info) => _ctx.Report(m, l);

    /// <summary>真机稳定延时（继电器切档/设值后需等待）。PORT: 旧 Thread.Sleep / ScriptHelper.Thread_Sleep。</summary>
    public Task Sleep(int ms)
    {
        Report(P06.IsRealHardware ? $"等待 {ms}ms" : $"等待 {ms}ms（仿真跳过）");
        return P06.IsRealHardware ? Task.Delay(ms, _ct) : Task.CompletedTask;
    }

    /// <summary>发共享工装输出指令（按名称映射到 GZP21 通道）。</summary>
    public Task Relay(string cmd)
    {
        Report($"工装输出指令：{cmd}");
        return Gzp21.SetOutputAsync(cmd, true, _ct);
    }

    /// <summary>读 DAM6803D 某通道电压。PORT: DSTB.GetVoltageMeasureValue。</summary>
    public Task<double> ReadVolt(int channel) => P06.ReadVoltageAsync(channel, _ct);
    public Task<double> ReadCurrent(int channel) => P06.ReadCurrentAsync(channel, _ct);
    /// <summary>回放旧平台中可直接映射的 P21/GZP21/P06 调用；复杂上下文参数不在此层猜测。</summary>
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
                if (method.Contains("Voltage", StringComparison.OrdinalIgnoreCase)) _ = await P06.ReadVoltageAsync(0, ct);
                else if (method.Contains("Current", StringComparison.OrdinalIgnoreCase)) _ = await P06.ReadCurrentAsync(0, ct);
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
/// 低压量程压力泄露测试和排空测试。PORT: 旧脚本方法 LeakTestComposition_Low_DP（JSON Entry: LeakTestComposition_Low_DP）。
/// </summary>
public sealed class LeakTestComposition_Low_DPConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "LeakTestComposition_Low_DP";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_DP_Machine";

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

        if (!(await ctx.ConfirmAsync("如果当前测试的设备为DP/LP类型\r\n请将低压模块更换为10kPa量程进行测试； 确认：更换完成，继续测试，取消：终止测试", ct))) pass = false;
        await Task.Delay(500, ct);
        //获取条件
        
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Open" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Open" }, ct);
        Pressure AtmosSensor = new Pressure(0, "kPa");
        await op.Dut.CommandAsync("GetAtmosSensor", null, ct);
        {
            var atmTxt = await op.Dut.QueryTextAsync("GetAtmosSensor", null, ct);
            if (double.TryParse(atmTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var atmVal))
                AtmosSensor = new Pressure(atmVal, "kPa");
        }
        if ((await op.Dut.QueryBooleanAsync("SetControlPressureModel", new[]{ "Low" }, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("切换低压量程失败,重试？", ct))) pass = false;
        await Task.Delay(5000, ct);
        
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
        
        await op.Dut.CommandAsync("GetPressureLowerer_IPM", null, ct);
        
        Pressure InnerModulePressureLowerer = new Pressure(0, "kPa");//量程下限
        double negativeinternalPressureRate = double.MaxValue;
        double negativeSupplyPressureRate = double.MaxValue;
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressureControlRange_LowerLimit", null, ct))) { op.Report("GetPressureControlRange_LowerLimit 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("获取内部模块量程下限失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ InnerModulePressureLowerer.ToString() }, ct))) { op.Report("SetTargetPressure 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("设置压力目标失败,重试？", ct))) pass = false;
        await op.Dut.CommandAsync("GetPressureStableState", null, ct);
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
        
        if (!(await op.Dut.QueryBooleanAsync("GetVacuumPressure", null, ct))) { op.Report("GetVacuumPressure 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var vpSecondTxt = await op.Dut.QueryTextAsync("GetVacuumPressure", null, ct);
            if (double.TryParse(vpSecondTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var vpSecondVal))
                getSourcePressure30SSecond = new Pressure(vpSecondVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取负压气源压力失败,重试？", ct))) pass = false;
        
        op.Report($"负压30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.NegativeExport, Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        negativeinternalPressureRate = Math.Abs((Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value)) / getInternalModulePressure30SFirst.Value);
        op.Report($"负压30秒泄露率: {(negativeinternalPressureRate*100).ToString("F4") + "%"}");
        
        op.Report($"负压气源压力30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.NegativeSource, Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        negativeSupplyPressureRate = Math.Abs((Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value)) / getSourcePressure30SFirst.Value);
        op.Report($"负压气源压力30秒泄露率: {(negativeSupplyPressureRate*100).ToString("F4") + "%"}");
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("读取当前压力失败,重试？", ct))) pass = false;
        if (!(await op.Dut.QueryBooleanAsync("SetVentMode", null, ct))) { op.Report("SetVentMode 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("排空失败,重试？", ct))) pass = false;
        await Task.Delay(3000, ct);
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("获取内部模块失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressureControlRange_UpperLimit", null, ct))) { op.Report("GetPressureControlRange_UpperLimit 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("获取内部模块量程上限失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ InnerModulePressureUpper.ToString() }, ct))) { op.Report("SetTargetPressure 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("设置压力目标失败,重试？", ct))) pass = false;
        await op.Dut.CommandAsync("GetPressureStableState", null, ct);
        // 旧 while(true) 轮询 GetPressure_IPM 直到 rate<=0.05（控压到位）或超时
        var upperPollGuard = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var ipmUpTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmUpTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipmUpVal))
                getInternalModulePressureOrg = new Pressure(ipmUpVal, "kPa");
            rate = Math.Abs((getInternalModulePressureOrg.Value - InnerModulePressureUpper.Value) / InnerModulePressureUpper.Value);
            if (rate <= 0.05) { op.Report("上限打压完成"); break; }
            if (++upperPollGuard > 600) { op.Report("上限打压超时(300s)", RealtimeLevel.Warn); pass = false; break; }
            await Task.Delay(500, ct);
        }
        
        await Task.Delay(2000, ct);
        
        if ((await op.Dut.QueryBooleanAsync("SetTestMode", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("设置控制器测量模式失败,重试？", ct))) pass = false;
        
        await Task.Delay(50, ct);
        
        if ((await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("获取内部模块压力失败,重试？", ct))) pass = false;
        
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
        
        if (!(await op.Dut.QueryBooleanAsync("GetSupplyPressure", null, ct))) { op.Report("GetSupplyPressure 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var spSecondTxt = await op.Dut.QueryTextAsync("GetSupplyPressure", null, ct);
            if (double.TryParse(spSecondTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var spSecondVal))
                getSourcePressure30SSecond = new Pressure(spSecondVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取正压气源压力失败,重试？", ct))) pass = false;
        
        op.Report($"正压30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.PositiveExport, Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        positiveinternalPressureRate = Math.Abs((Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value)) / getInternalModulePressure30SFirst.Value);
        double positiveinternalRate = positiveinternalPressureRate * 100;
        op.Report($"正压30秒泄露率: {$"{positiveinternalRate.ToString("F4")}%"}");
        
        op.Report($"正压气源压力30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.PositiveSource, Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        positiveSupplyPressureRate = Math.Abs((Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value)) / getSourcePressure30SFirst.Value);
        op.Report($"正压气源压力30秒泄露率: {(positiveSupplyPressureRate*100).ToString("F4") + "%"}");
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("读取当前压力失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("SetVentMode", null, ct))) { op.Report("SetVentMode 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("排空失败,重试？", ct))) pass = false;
        await Task.Delay(3000, ct);
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("获取内部模块失败,重试？", ct))) pass = false;
        
        await Task.Delay(2000, ct);
        
        await op.Dut.CommandAsync("SetVentMode", null, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Close" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Close" }, ct);
        
        if (!(await ctx.ConfirmAsync("低压泄露测试完成，需要将低压模块更换为±2.5kPa, 确认：更换完成，继续测试，取消：终止测试", ct))) pass = false;
        
        op.Report(tvalue);
        op.Report(pass ? "✓ 低压量程压力泄露测试和排空测试通过" : "✗ 低压量程压力泄露测试和排空测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("低压量程压力泄露测试和排空测试通过") : StepResult.Fail("低压量程压力泄露测试和排空测试未通过");
    }
}

/// <summary>
/// 高压量程压力泄露测试和排空测试。PORT: 旧脚本方法 LeakTestComposition_High_DP（JSON Entry: LeakTestComposition_High_DP）。
/// </summary>
public sealed class LeakTestComposition_High_DPConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "LeakTestComposition_High_DP";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_DP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        // G10: 旧 List<DataBase>/StringBuilder tvalue 改为 string 拼接，最终 op.Report
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
        {
            var atmTxt = await op.Dut.QueryTextAsync("GetAtmosSensor", null, ct);
            if (double.TryParse(atmTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var atmVal))
                AtmosSensor = new Pressure(atmVal, "kPa");
        }
        if ((await op.Dut.QueryBooleanAsync("SetControlPressureModel", new[]{ "High" }, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("切换高压量程失败,重试？", ct))) pass = false;
        await Task.Delay(5000, ct);
        
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
        
        await op.Dut.CommandAsync("GetPressureLowerer_IPM", null, ct);
        
        Pressure InnerModulePressureLowerer = new Pressure(0, "kPa");//量程下限
        double negativeinternalPressureRate = double.MaxValue;
        double negativeSupplyPressureRate = double.MaxValue;
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressureControlRange_LowerLimit", null, ct))) { op.Report("GetPressureControlRange_LowerLimit 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("获取内部模块量程下限失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ InnerModulePressureLowerer.ToString() }, ct))) { op.Report("SetTargetPressure 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("设置压力目标失败,重试？", ct))) pass = false;
        await op.Dut.CommandAsync("GetPressureStableState", null, ct);
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
        
        if (!(await op.Dut.QueryBooleanAsync("GetVacuumPressure", null, ct))) { op.Report("GetVacuumPressure 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var vpFirstTxt = await op.Dut.QueryTextAsync("GetVacuumPressure", null, ct);
            if (double.TryParse(vpFirstTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var vpFirstVal))
                getSourcePressure30SFirst = new Pressure(vpFirstVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取负压气源压力失败,重试？", ct))) pass = false; { }
        
        var P1s = new List<double>();
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("读取设备压力值失败", RealtimeLevel.Error); pass = false; }
        //return result.AddTestErrMsgs(new ErrMsg(20001, "读取设备压力值失败"));
        if (!(await op.Dut.QueryBooleanAsync("GetDev_T", null, ct))) { op.Report("GetDev_T 调用失败", RealtimeLevel.Error); pass = false; }
        {
            var pfTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(pfTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var pfVal))
                getInternalModulePressure30SFirst = new Pressure(pfVal, "kPa");
            tstr = await op.Dut.QueryTextAsync("GetDev_T", null, ct);
            tvalue += $"{getInternalModulePressure30SFirst.Value},{tstr};";
            op.Report(tvalue);
            P1s.Add(getInternalModulePressure30SFirst.Value);
        }

        await Task.Delay(50, ct);

        if ((await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("获取内部模块压力失败,重试？", ct))) pass = false;

        if (!(await op.Dut.QueryBooleanAsync("GetVacuumPressure", null, ct))) { op.Report("GetVacuumPressure 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var vpSecondTxt = await op.Dut.QueryTextAsync("GetVacuumPressure", null, ct);
            if (double.TryParse(vpSecondTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var vpSecondVal))
                getSourcePressure30SSecond = new Pressure(vpSecondVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取负压气源压力失败,重试？", ct))) pass = false;
        
        op.Report($"负压30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.NegativeExport, Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        negativeinternalPressureRate = Math.Abs((Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value)) / getInternalModulePressure30SFirst.Value);
        op.Report($"负压30秒泄露率: {(negativeinternalPressureRate*100).ToString("F4") + "%"}");
        
        op.Report($"负压气源压力30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.NegativeSource, Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        negativeSupplyPressureRate = Math.Abs((Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value)) / getSourcePressure30SFirst.Value);
        op.Report($"负压气源压力30秒泄露量(新): {(negativeSupplyPressureRate * 100).ToString("F4") + "%"}");
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("读取当前压力失败,重试？", ct))) pass = false;
        if (!(await op.Dut.QueryBooleanAsync("SetVentMode", null, ct))) { op.Report("SetVentMode 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("排空失败,重试？", ct))) pass = false;
        await Task.Delay(3000, ct);
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("获取内部模块失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressureControlRange_UpperLimit", null, ct))) { op.Report("GetPressureControlRange_UpperLimit 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("获取内部模块量程上限失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ InnerModulePressureUpper.ToString() }, ct))) { op.Report("SetTargetPressure 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("设置压力目标失败,重试？", ct))) pass = false;
        await op.Dut.CommandAsync("GetPressureStableState", null, ct);
        // 旧 while(true) 轮询 GetPressure_IPM 直到 rate<=0.05（控压到位）或超时
        var upperPollGuard = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var ipmUpTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmUpTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipmUpVal))
                getInternalModulePressureOrg = new Pressure(ipmUpVal, "kPa");
            rate = Math.Abs((getInternalModulePressureOrg.Value - InnerModulePressureUpper.Value) / InnerModulePressureUpper.Value);
            if (rate <= 0.05) { op.Report("上限打压完成"); break; }
            if (++upperPollGuard > 600) { op.Report("上限打压超时(300s)", RealtimeLevel.Warn); pass = false; break; }
            await Task.Delay(500, ct);
        }
        
        await Task.Delay(2000, ct);
        
        if ((await op.Dut.QueryBooleanAsync("SetTestMode", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("设置控制器测量模式失败,重试？", ct))) pass = false;
        
        await Task.Delay(50, ct);
        
        if ((await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("获取内部模块压力失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("GetSupplyPressure", null, ct))) { op.Report("GetSupplyPressure 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var spFirstTxt = await op.Dut.QueryTextAsync("GetSupplyPressure", null, ct);
            if (double.TryParse(spFirstTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var spFirstVal))
                getSourcePressure30SFirst = new Pressure(spFirstVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取正压气源压力失败,重试？", ct))) pass = false; { }
        
        var P2s = new List<double>();
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("读取设备压力值失败", RealtimeLevel.Error); pass = false; }
        //return result.AddTestErrMsgs(new ErrMsg(20001, "读取设备压力值失败"));
        if (!(await op.Dut.QueryBooleanAsync("GetDev_T", null, ct))) { op.Report("GetDev_T 调用失败", RealtimeLevel.Error); pass = false; }
        {
            var pfTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(pfTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var pfVal))
                getInternalModulePressure30SFirst = new Pressure(pfVal, "kPa");
            tstr = await op.Dut.QueryTextAsync("GetDev_T", null, ct);
            tvalue += $"{getInternalModulePressure30SFirst.Value},{tstr};";
            op.Report(tvalue);
            P2s.Add(getInternalModulePressure30SFirst.Value);
        }
        
        await Task.Delay(50, ct);
        
        if ((await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("获取内部模块压力失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("GetSupplyPressure", null, ct))) { op.Report("GetSupplyPressure 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var spSecondTxt = await op.Dut.QueryTextAsync("GetSupplyPressure", null, ct);
            if (double.TryParse(spSecondTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var spSecondVal))
                getSourcePressure30SSecond = new Pressure(spSecondVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取正压气源压力失败,重试？", ct))) pass = false;
        
        op.Report($"正压30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.PositiveExport, Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        positiveinternalPressureRate = Math.Abs((Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value)) / getInternalModulePressure30SFirst.Value);
        op.Report($"正压30秒泄露率: {(positiveinternalPressureRate*100).ToString("F4") + "%"}");
        
        op.Report($"正压气源压力30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.PositiveSource, Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        positiveSupplyPressureRate = Math.Abs((Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value)) / getSourcePressure30SFirst.Value);
        op.Report($"正压气源压力30秒泄露率: {(positiveSupplyPressureRate * 100).ToString("F4") + "%"}");
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("读取当前压力失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("SetVentMode", null, ct))) { op.Report("SetVentMode 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("排空失败,重试？", ct))) pass = false;
        await Task.Delay(3000, ct);
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("获取内部模块失败,重试？", ct))) pass = false;
        
        await Task.Delay(2000, ct);
        op.Report($"压力值与温度值: {tvalue.ToString()}");
        await op.Dut.CommandAsync("SetVentMode", null, ct);
        
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Close" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Close" }, ct);
        
        ctx.RecordProcessData(new ProcessDataSeries {
            StartedAt = DateTime.Now,
            TimeSec = Enumerable.Range(0, 1).Select(i => (double)i).ToArray(),
            Channels = new[] { new ProcessChannel("负压压力变化", P1s.ToArray()) }
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
