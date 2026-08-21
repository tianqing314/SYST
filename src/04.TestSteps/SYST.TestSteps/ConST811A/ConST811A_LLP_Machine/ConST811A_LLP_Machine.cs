using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;

namespace SYST.TestSteps.ConST811A.ConST811A_LLP_Machine;

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
/// 低压量程压力泄露测试和排空测试。PORT: 旧脚本方法 LeakTestComposition_Low_LLP（JSON Entry: LeakTestComposition_Low_LLP）。
/// </summary>
public sealed class LeakTestComposition_Low_LLPConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "LeakTestComposition_Low_LLP";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_LLP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        // 记录压力值与温度值，最终统一 op.Report
        var tvalue = new StringBuilder();
        tvalue.Append("压力值,高压温度,低压温度,泵温度,电测板温度^");
        var tstr = "";
        Pressure getInternalModulePressureOrg = new Pressure(0, "kPa");
        double rate = 0;
        DateTime starTime = DateTime.Now;

        await Task.Delay(500, ct);
        //获取条件
        
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Open" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Open" }, ct);
        // 读取大气压（规则9：GetAtmosSensor 用 QueryTextAsync 读回）
        Pressure AtmosSensor = new Pressure(0, "kPa");
        {
            var atmosTxt = await op.Dut.QueryTextAsync("GetAtmosSensor", null, ct);
            if (double.TryParse(atmosTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var atmosVal))
                AtmosSensor = new Pressure(atmosVal, "kPa");
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
        else
        {
            var lowTxt = await op.Dut.QueryTextAsync("GetPressureControlRange_LowerLimit", null, ct);
            if (double.TryParse(lowTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var lowVal))
                InnerModulePressureLowerer = new Pressure(lowVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取内部模块量程下限失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ InnerModulePressureLowerer.ToString() }, ct))) { op.Report("SetTargetPressure 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("设置压力目标失败,重试？", ct))) pass = false;

        var VP1s = new List<double>();

        if (!(await op.Dut.QueryBooleanAsync("GetControllerModuleConfig", null, ct))) { op.Report("GetControllerModuleConfig 调用失败", RealtimeLevel.Error); pass = false; }
        starTime = DateTime.Now;
        // 轮询 GetPressure_IPM 直到 rate>=0.95（控压到位）或超时
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
            await Task.Delay(500, ct);
        }
        
        await Task.Delay(2000, ct);
        
        if ((await op.Dut.QueryBooleanAsync("SetTestMode", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("设置控制器测量模式失败,重试？", ct))) pass = false;
        
        await Task.Delay(50, ct);
        
        // 30秒前内部模块压力值（规则1：GetPressure_IPM 用 QueryTextAsync 读回）
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var ipmFirstTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmFirstTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipmFirstVal))
                getInternalModulePressure30SFirst = new Pressure(ipmFirstVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取内部模块压力失败,重试？", ct))) pass = false;

        // 30秒前负压气源值（规则2：GetVacuumPressure 用 QueryTextAsync 读回）
        if (!(await op.Dut.QueryBooleanAsync("GetVacuumPressure", null, ct))) { op.Report("GetVacuumPressure 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var vpFirstTxt = await op.Dut.QueryTextAsync("GetVacuumPressure", null, ct);
            if (double.TryParse(vpFirstTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var vpFirstVal))
                getSourcePressure30SFirst = new Pressure(vpFirstVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取负压气源压力失败,重试？", ct))) pass = false;

        var P1s = new List<double>();
        // 30 秒轮询：每 150ms 读 IPM + Dev_T，append tvalue + P1s
        {
            var neg30SStart = DateTime.Now;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; break; }
                if (!(await op.Dut.QueryBooleanAsync("GetDev_T", null, ct))) { op.Report("GetDev_T 调用失败", RealtimeLevel.Error); pass = false; break; }
                var infoTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
                double infoVal = 0;
                if (double.TryParse(infoTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    infoVal = v;
                tstr = await op.Dut.QueryTextAsync("GetDev_T", null, ct);
                tvalue.Append($"{infoVal},{tstr};");
                P1s.Add(infoVal);
                if ((DateTime.Now - neg30SStart).TotalSeconds > 30) break;
                await Task.Delay(150, ct);
            }
            op.Report(tvalue.ToString());
        }

        // 30秒后内部模块压力值
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var ipmSecondTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmSecondTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipmSecondVal))
                getInternalModulePressure30SSecond = new Pressure(ipmSecondVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取内部模块压力失败,重试？", ct))) pass = false;

        // 30秒后负压气源值
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
        op.Report($"负压30秒泄露率: {(negativeinternalPressureRate * 100).ToString("F5") + "%"}");

        op.Report($"负压气源压力30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.NegativeSource, Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        negativeSupplyPressureRate = Math.Abs((Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value)) / getSourcePressure30SFirst.Value);
        op.Report($"负压气源压力30秒泄露率: {(negativeSupplyPressureRate * 100).ToString("F5") + "%"}");
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("读取当前压力失败,重试？", ct))) pass = false;
        if (!(await op.Dut.QueryBooleanAsync("SetVentMode", null, ct))) { op.Report("SetVentMode 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("排空失败,重试？", ct))) pass = false;
        await Task.Delay(3000, ct);
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("获取内部模块失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressureControlRange_UpperLimit", null, ct))) { op.Report("GetPressureControlRange_UpperLimit 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var upTxt = await op.Dut.QueryTextAsync("GetPressureControlRange_UpperLimit", null, ct);
            if (double.TryParse(upTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var upVal))
                InnerModulePressureUpper = new Pressure(upVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取内部模块量程上限失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ InnerModulePressureUpper.ToString() }, ct))) { op.Report("SetTargetPressure 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("设置压力目标失败,重试？", ct))) pass = false;

        var VP2s = new List<double>();

        if (!(await op.Dut.QueryBooleanAsync("GetControllerModuleConfig", null, ct))) { op.Report("GetControllerModuleConfig 调用失败", RealtimeLevel.Error); pass = false; }
        starTime = DateTime.Now;
        // 轮询 GetPressure_IPM 直到 rate>=0.95（控压到位）或超时
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
            await Task.Delay(500, ct);
        }
        
        await Task.Delay(2000, ct);
        
        if ((await op.Dut.QueryBooleanAsync("SetTestMode", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("设置控制器测量模式失败,重试？", ct))) pass = false;
        
        await Task.Delay(50, ct);
        
        // 30秒前内部模块压力值（规则1：GetPressure_IPM 用 QueryTextAsync 读回）
        getInternalModulePressure30SFirst = new Pressure(0, "kPa");
        getInternalModulePressure30SSecond = new Pressure(0, "kPa");
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var ipmFirstTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmFirstTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipmFirstVal))
                getInternalModulePressure30SFirst = new Pressure(ipmFirstVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取内部模块压力失败,重试？", ct))) pass = false;

        // 30秒前正压气源值（规则2：GetSupplyPressure 用 QueryTextAsync 读回）
        if (!(await op.Dut.QueryBooleanAsync("GetSupplyPressure", null, ct))) { op.Report("GetSupplyPressure 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var spFirstTxt = await op.Dut.QueryTextAsync("GetSupplyPressure", null, ct);
            if (double.TryParse(spFirstTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var spFirstVal))
                getSourcePressure30SFirst = new Pressure(spFirstVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取正压气源压力失败,重试？", ct))) pass = false;

        var P2s = new List<double>();
        // 30 秒轮询：每 150ms 读 IPM + Dev_T，append tvalue + P2s
        {
            var pos30SStart = DateTime.Now;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; break; }
                if (!(await op.Dut.QueryBooleanAsync("GetDev_T", null, ct))) { op.Report("GetDev_T 调用失败", RealtimeLevel.Error); pass = false; break; }
                var infoTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
                double infoVal = 0;
                if (double.TryParse(infoTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    infoVal = v;
                tstr = await op.Dut.QueryTextAsync("GetDev_T", null, ct);
                tvalue.Append($"{infoVal},{tstr};");
                P2s.Add(infoVal);
                if ((DateTime.Now - pos30SStart).TotalSeconds > 30) break;
                await Task.Delay(150, ct);
            }
            op.Report(tvalue.ToString());
        }

        // 30秒后内部模块压力值
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var ipmSecondTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmSecondTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipmSecondVal))
                getInternalModulePressure30SSecond = new Pressure(ipmSecondVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取内部模块压力失败,重试？", ct))) pass = false;

        // 30秒后正压气源值
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
        op.Report($"正压30秒泄露率: {(positiveinternalPressureRate * 100).ToString("F5") + "%"}");

        op.Report($"正压气源压力30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.PositiveSource, Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        positiveSupplyPressureRate = Math.Abs((Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value)) / getSourcePressure30SFirst.Value);
        op.Report($"正压气源压力30秒泄露率: {(positiveSupplyPressureRate * 100).ToString("F5") + "%"}");
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("读取当前压力失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("SetVentMode", null, ct))) { op.Report("SetVentMode 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("排空失败,重试？", ct))) pass = false;
        await Task.Delay(3000, ct);
        // 等待压力稳定
        {
            var stableGuard = 0;
            while (true)
            {
                var stateTxt = await op.Dut.QueryTextAsync("GetPressureStableState", null, ct);
                if (stateTxt.Contains("Stable", StringComparison.OrdinalIgnoreCase)) break;
                if (++stableGuard > 600) { op.Report("等待压力稳定超时(300s)", RealtimeLevel.Warn); pass = false; break; }
                await Task.Delay(500, ct);
            }
        }
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("获取内部模块失败,重试？", ct))) pass = false;

        await Task.Delay(2000, ct);
        op.Report($"压力值与温度值: {tvalue.ToString()}");
        await op.Dut.CommandAsync("SetVentMode", null, ct);
        // 等待压力稳定
        {
            var stableGuard2 = 0;
            while (true)
            {
                var stateTxt = await op.Dut.QueryTextAsync("GetPressureStableState", null, ct);
                if (stateTxt.Contains("Stable", StringComparison.OrdinalIgnoreCase)) break;
                if (++stableGuard2 > 600) { op.Report("等待压力稳定超时(300s)", RealtimeLevel.Warn); pass = false; break; }
                await Task.Delay(500, ct);
            }
        }
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Close" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Close" }, ct);

        if (!(await ctx.ConfirmAsync("低压泄露测试完成，需要将低压模块更换为±500Pa, 确认：更换完成，继续测试，取消：终止测试", ct))) pass = false;

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
/// 高压量程压力泄露测试和排空测试。PORT: 旧脚本方法 LeakTestComposition_High_LLP（JSON Entry: LeakTestComposition_High_LLP）。
/// </summary>
public sealed class LeakTestComposition_High_LLPConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "LeakTestComposition_High_LLP";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_LLP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        // 记录压力值与温度值，最终统一 op.Report
        var tvalue = new StringBuilder();
        tvalue.Append("压力值,高压温度,低压温度,泵温度,电测板温度^");
        var tstr = "";
        Pressure getInternalModulePressureOrg = new Pressure(0, "kPa");
        double rate = 0;
        DateTime starTime = DateTime.Now;
        
        await Task.Delay(500, ct);
        //获取条件
        
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Open" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Open" }, ct);
        // 读取大气压（规则9：GetAtmosSensor 用 QueryTextAsync 读回）
        Pressure AtmosSensor = new Pressure(0, "kPa");
        {
            var atmosTxt = await op.Dut.QueryTextAsync("GetAtmosSensor", null, ct);
            if (double.TryParse(atmosTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var atmosVal))
                AtmosSensor = new Pressure(atmosVal, "kPa");
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
        else
        {
            var lowTxt = await op.Dut.QueryTextAsync("GetPressureControlRange_LowerLimit", null, ct);
            if (double.TryParse(lowTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var lowVal))
                InnerModulePressureLowerer = new Pressure(lowVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取内部模块量程下限失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ InnerModulePressureLowerer.ToString() }, ct))) { op.Report("SetTargetPressure 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("设置压力目标失败,重试？", ct))) pass = false;

        var VP1s = new List<double>();

        if (!(await op.Dut.QueryBooleanAsync("GetControllerModuleConfig", null, ct))) { op.Report("GetControllerModuleConfig 调用失败", RealtimeLevel.Error); pass = false; }
        starTime = DateTime.Now;
        // 轮询 GetPressure_IPM 直到 rate>=0.95（控压到位）或超时
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
            await Task.Delay(500, ct);
        }
        
        await Task.Delay(2000, ct);
        
        if ((await op.Dut.QueryBooleanAsync("SetTestMode", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("设置控制器测量模式失败,重试？", ct))) pass = false;
        
        await Task.Delay(50, ct);
        
        // 30秒前内部模块压力值（规则1：GetPressure_IPM 用 QueryTextAsync 读回）
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var ipmFirstTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmFirstTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipmFirstVal))
                getInternalModulePressure30SFirst = new Pressure(ipmFirstVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取内部模块压力失败,重试？", ct))) pass = false;

        // 30秒前负压气源值（规则2：GetVacuumPressure 用 QueryTextAsync 读回）
        if (!(await op.Dut.QueryBooleanAsync("GetVacuumPressure", null, ct))) { op.Report("GetVacuumPressure 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var vpFirstTxt = await op.Dut.QueryTextAsync("GetVacuumPressure", null, ct);
            if (double.TryParse(vpFirstTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var vpFirstVal))
                getSourcePressure30SFirst = new Pressure(vpFirstVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取负压气源压力失败,重试？", ct))) pass = false;

        var P1s = new List<double>();
        // 30 秒轮询：每 150ms 读 IPM + Dev_T，append tvalue + P1s
        {
            var neg30SStart = DateTime.Now;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; break; }
                if (!(await op.Dut.QueryBooleanAsync("GetDev_T", null, ct))) { op.Report("GetDev_T 调用失败", RealtimeLevel.Error); pass = false; break; }
                var infoTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
                double infoVal = 0;
                if (double.TryParse(infoTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    infoVal = v;
                tstr = await op.Dut.QueryTextAsync("GetDev_T", null, ct);
                tvalue.Append($"{infoVal},{tstr};");
                P1s.Add(infoVal);
                if ((DateTime.Now - neg30SStart).TotalSeconds > 30) break;
                await Task.Delay(150, ct);
            }
            op.Report(tvalue.ToString());
        }

        // 30秒后内部模块压力值
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var ipmSecondTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmSecondTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipmSecondVal))
                getInternalModulePressure30SSecond = new Pressure(ipmSecondVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取内部模块压力失败,重试？", ct))) pass = false;

        // 30秒后负压气源值
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
        op.Report($"负压30秒泄露率: {(negativeinternalPressureRate * 100).ToString("F5") + "%"}");

        op.Report($"负压气源压力30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.NegativeSource, Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        negativeSupplyPressureRate = Math.Abs((Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value)) / getSourcePressure30SFirst.Value);
        op.Report($"负压气源压力30秒泄露率: {(negativeSupplyPressureRate * 100).ToString("F5") + "%"}");
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("读取当前压力失败,重试？", ct))) pass = false;
        if (!(await op.Dut.QueryBooleanAsync("SetVentMode", null, ct))) { op.Report("SetVentMode 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("排空失败,重试？", ct))) pass = false;
        await Task.Delay(3000, ct);
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("获取内部模块失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressureControlRange_UpperLimit", null, ct))) { op.Report("GetPressureControlRange_UpperLimit 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var upTxt = await op.Dut.QueryTextAsync("GetPressureControlRange_UpperLimit", null, ct);
            if (double.TryParse(upTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var upVal))
                InnerModulePressureUpper = new Pressure(upVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取内部模块量程上限失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ InnerModulePressureUpper.ToString() }, ct))) { op.Report("SetTargetPressure 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("设置压力目标失败,重试？", ct))) pass = false;

        var VP2s = new List<double>();

        if (!(await op.Dut.QueryBooleanAsync("GetControllerModuleConfig", null, ct))) { op.Report("GetControllerModuleConfig 调用失败", RealtimeLevel.Error); pass = false; }
        starTime = DateTime.Now;
        // 轮询 GetPressure_IPM 直到 rate>=0.95（控压到位）或超时
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
            await Task.Delay(500, ct);
        }
        
        await Task.Delay(2000, ct);
        
        if ((await op.Dut.QueryBooleanAsync("SetTestMode", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("设置控制器测量模式失败,重试？", ct))) pass = false;
        
        await Task.Delay(50, ct);
        
        // 30秒前内部模块压力值（规则1：GetPressure_IPM 用 QueryTextAsync 读回）
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var ipmFirstTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmFirstTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipmFirstVal))
                getInternalModulePressure30SFirst = new Pressure(ipmFirstVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取内部模块压力失败,重试？", ct))) pass = false;

        // 30秒前正压气源值（规则2：GetSupplyPressure 用 QueryTextAsync 读回）
        if (!(await op.Dut.QueryBooleanAsync("GetSupplyPressure", null, ct))) { op.Report("GetSupplyPressure 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var spFirstTxt = await op.Dut.QueryTextAsync("GetSupplyPressure", null, ct);
            if (double.TryParse(spFirstTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var spFirstVal))
                getSourcePressure30SFirst = new Pressure(spFirstVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取正压气源压力失败,重试？", ct))) pass = false;

        var P2s = new List<double>();
        
        // 30 秒轮询：每 150ms 读 IPM + Dev_T，append tvalue + P2s
        {
            var pos30SStart = DateTime.Now;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; break; }
                if (!(await op.Dut.QueryBooleanAsync("GetDev_T", null, ct))) { op.Report("GetDev_T 调用失败", RealtimeLevel.Error); pass = false; break; }
                var infoTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
                double infoVal = 0;
                if (double.TryParse(infoTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    infoVal = v;
                tstr = await op.Dut.QueryTextAsync("GetDev_T", null, ct);
                tvalue.Append($"{infoVal},{tstr};");
                P2s.Add(infoVal);
                if ((DateTime.Now - pos30SStart).TotalSeconds > 30) break;
                await Task.Delay(150, ct);
            }
            op.Report(tvalue.ToString());
        }
        
        // 30秒后内部模块压力值
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var ipmSecondTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmSecondTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipmSecondVal))
                getInternalModulePressure30SSecond = new Pressure(ipmSecondVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取内部模块压力失败,重试？", ct))) pass = false;

        // 30秒后正压气源值
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
        op.Report($"正压30秒泄露率: {(positiveinternalPressureRate * 100).ToString("F5") + "%"}");

        op.Report($"正压气源压力30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.PositiveSource, Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        positiveSupplyPressureRate = Math.Abs((Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value)) / getSourcePressure30SFirst.Value);
        op.Report($"正压气源压力30秒泄露率: {(positiveSupplyPressureRate * 100).ToString("F5") + "%"}");
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("读取当前压力失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("SetVentMode", null, ct))) { op.Report("SetVentMode 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("排空失败,重试？", ct))) pass = false;
        await Task.Delay(3000, ct);
        // 等待压力稳定
        {
            var stableGuard = 0;
            while (true)
            {
                var stateTxt = await op.Dut.QueryTextAsync("GetPressureStableState", null, ct);
                if (stateTxt.Contains("Stable", StringComparison.OrdinalIgnoreCase)) break;
                if (++stableGuard > 600) { op.Report("等待压力稳定超时(300s)", RealtimeLevel.Warn); pass = false; break; }
                await Task.Delay(500, ct);
            }
        }
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("获取内部模块失败,重试？", ct))) pass = false;
        
        await Task.Delay(2000, ct);
        op.Report($"压力值与温度值: {tvalue.ToString()}");

        await op.Dut.CommandAsync("SetVentMode", null, ct);
        // 等待压力稳定
        {
            var stableGuard2 = 0;
            while (true)
            {
                var stateTxt = await op.Dut.QueryTextAsync("GetPressureStableState", null, ct);
                if (stateTxt.Contains("Stable", StringComparison.OrdinalIgnoreCase)) break;
                if (++stableGuard2 > 600) { op.Report("等待压力稳定超时(300s)", RealtimeLevel.Warn); pass = false; break; }
                await Task.Delay(500, ct);
            }
        }
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
