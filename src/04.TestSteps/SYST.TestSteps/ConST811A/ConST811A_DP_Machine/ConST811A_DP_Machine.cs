using System.Globalization;
using System.IO.Ports;
using System.Text.RegularExpressions;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;

namespace SYST.TestSteps.ConST811A.ConST811A_DP_Machine;

/// <summary>
/// ConST811A 主板（设备族 ConST811A）测试处理器集合。
/// 继电器指令序列（GZP21/P06 共享设备）、电压/电流读数、被检指令与 Range 判定。
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

    /// <summary>更新最后一条实时消息（用于倒计时等场景，原地更新而非追加新行）。</summary>
    public void UpdateLastReport(string m, RealtimeLevel l = RealtimeLevel.Info) => _ctx.UpdateLastReport(m, l);

    /// <summary>步骤成功：报告操作完成（用 ✓ 标记）。</summary>
    public void Ok(string desc) => Report($"✓ {desc}", RealtimeLevel.Success);

    /// <summary>步骤失败：报告操作失败（用 ✗ 标记）。</summary>
    public void Fail(string desc) => Report($"✗ {desc}", RealtimeLevel.Error);

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
}

/// <summary>
/// 低压量程压力泄露测试和排空测试。PORT: 旧脚本方法 LeakTestComposition_Low_DP（JSON Entry: LeakTestComposition_Low_DP）。
/// </summary>
public sealed class LeakTestComposition_Low_DPConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "LeakTestComposition_Low_DP";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "P21";

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

        // 提示操作员更换低压模块为10kPa量程（旧脚本为Confirm弹窗，现改为日志提示，不阻塞流程）
        op.Report("如果当前测试的设备为DP/LP类型，请将低压模块更换为10kPa量程进行测试", RealtimeLevel.Warn);
        await op.Sleep(500);
        //获取条件

        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Open" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Open" }, ct);
        Pressure AtmosSensor = new Pressure(0, "kPa");
        await op.Dut.CommandAsync("GetAtmosSensor", null, ct);
        {
            var atmTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetAtmosSensor", null, ct), "读取大气压传感器");
            if (atmTxt != null && double.TryParse(atmTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var atmVal))
                AtmosSensor = new Pressure(atmVal, "kPa");
        }
        // 切换低压量程
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetControlPressureModel", new[]{ "Low" }, ct), "切换低压量程"))) pass = false;
        await op.Sleep(5000);

        // 设定内部模块压力单位
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetPressureUnit_IPM", null, ct), "设定内部模块压力单位"))) pass = false;

        // 获取压力控制量程范围
        // 获取压力控制量程范围（Lower~Upper）
        var rangeTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetSetPointLimitPressureRange", null, ct), "获取压力控制量程范围");
        if (rangeTxt is null) pass = false;
        else op.Report("压力控制量程范围 = {rangeTxt} kPa");

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

        // 获取内部模块量程下限
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressureControlRange_LowerLimit", null, ct), "获取内部模块量程下限"))) pass = false;

        // 设置压力目标（下限）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ InnerModulePressureLowerer.Value.ToString(CultureInfo.InvariantCulture) }, ct), "设置压力目标"))) pass = false;
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
            await op.Sleep(500);
        }

        await op.Sleep(2000);

        // 设置控制器测量模式
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTestMode", null, ct), "设置控制器测量模式"))) pass = false;

        await op.Sleep(50);

        // 触发并读取内部模块压力（负压段第一次，旧脚本成功分支已省略）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        // 读取负压气源压力（第一次）
        var vpFirstRaw = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetVacuumPressure", null, ct), "获取负压气源压力");
        if (vpFirstRaw != null && double.TryParse(vpFirstRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var vpFirstVal))
            getSourcePressure30SFirst = new Pressure(vpFirstVal, "kPa");
        else
            pass = false;

        // 连续读取内部模块压力和设备温度（第一次）
        var pfFirstTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetPressure_IPM", null, ct), "获取内部模块压力值");
        var tFirstTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetDev_T", null, ct), "获取设备温度");
        if (pfFirstTxt != null && double.TryParse(pfFirstTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var pfFirstVal))
            getInternalModulePressure30SFirst = new Pressure(pfFirstVal, "kPa");
        else
            pass = false;
        tstr = tFirstTxt ?? "";
        tvalue += $"{getInternalModulePressure30SFirst.Value},{tstr};";
        op.Report(tvalue);

        await op.Sleep(50);

        // 触发并读取内部模块压力（负压段第二次）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        // 读取负压气源压力（第二次）
        var vpSecondRaw = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetVacuumPressure", null, ct), "获取负压气源压力");
        if (vpSecondRaw != null && double.TryParse(vpSecondRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var vpSecondVal))
            getSourcePressure30SSecond = new Pressure(vpSecondVal, "kPa");
        else
            pass = false;

        op.Report($"负压30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.NegativeExport, Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        negativeinternalPressureRate = Math.Abs((Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value)) / getInternalModulePressure30SFirst.Value);
        op.Report($"负压30秒泄露率: {(negativeinternalPressureRate*100).ToString("F4") + "%"}");

        op.Report($"负压气源压力30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.NegativeSource, Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        negativeSupplyPressureRate = Math.Abs((Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value)) / getSourcePressure30SFirst.Value);
        op.Report($"负压气源压力30秒泄露率: {(negativeSupplyPressureRate*100).ToString("F4") + "%"}");

        // 读取当前压力
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "读取当前压力"))) pass = false;
        // 排空
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetVentMode", null, ct), "排空"))) pass = false;
        await op.Sleep(3000);

        // 获取内部模块（排空后压力）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        // 获取内部模块量程上限
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressureControlRange_UpperLimit", null, ct), "获取内部模块量程上限"))) pass = false;

        // 设置压力目标（上限）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ InnerModulePressureUpper.Value.ToString(CultureInfo.InvariantCulture) }, ct), "设置压力目标"))) pass = false;
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
            await op.Sleep(500);
        }

        await op.Sleep(2000);

        // 设置控制器测量模式
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTestMode", null, ct), "设置控制器测量模式"))) pass = false;

        await op.Sleep(50);

        // 触发并读取内部模块压力（正压段第一次）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        // 读取正压气源压力（第一次）
        var spFirstRaw = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetSupplyPressure", null, ct), "获取正压气源压力");
        if (spFirstRaw != null && double.TryParse(spFirstRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var spFirstVal))
            getSourcePressure30SFirst = new Pressure(spFirstVal, "kPa");
        else
            pass = false;

        // 连续读取内部模块压力和设备温度（正压段第一次）
        var pfSecondTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetPressure_IPM", null, ct), "获取内部模块压力值");
        var tSecondTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetDev_T", null, ct), "获取设备温度");
        if (pfSecondTxt != null && double.TryParse(pfSecondTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var pfSecondVal))
            getInternalModulePressure30SFirst = new Pressure(pfSecondVal, "kPa");
        else
            pass = false;
        tstr = tSecondTxt ?? "";
        tvalue += $"{getInternalModulePressure30SFirst.Value},{tstr};";
        op.Report(tvalue);

        await op.Sleep(50);

        // 触发并读取内部模块压力（正压段第二次）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        // 读取正压气源压力（第二次）
        var spSecondRaw = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetSupplyPressure", null, ct), "获取正压气源压力");
        if (spSecondRaw != null && double.TryParse(spSecondRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var spSecondVal))
            getSourcePressure30SSecond = new Pressure(spSecondVal, "kPa");
        else
            pass = false;

        op.Report($"正压30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.PositiveExport, Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        positiveinternalPressureRate = Math.Abs((Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value)) / getInternalModulePressure30SFirst.Value);
        double positiveinternalRate = positiveinternalPressureRate * 100;
        op.Report($"正压30秒泄露率: {$"{positiveinternalRate.ToString("F4")}%"}");

        op.Report($"正压气源压力30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.PositiveSource, Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        positiveSupplyPressureRate = Math.Abs((Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value)) / getSourcePressure30SFirst.Value);
        op.Report($"正压气源压力30秒泄露率: {(positiveSupplyPressureRate*100).ToString("F4") + "%"}");

        // 读取当前压力
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "读取当前压力"))) pass = false;

        // 排空
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetVentMode", null, ct), "排空"))) pass = false;
        await op.Sleep(3000);

        // 获取内部模块（排空后压力）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        await op.Sleep(2000);

        await op.Dut.CommandAsync("SetVentMode", null, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Close" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Close" }, ct);

        // 提示操作员更换低压模块为±2.5kPa（旧脚本为Confirm弹窗，现改为日志提示，不阻塞流程）
        op.Report("低压泄露测试完成，需要将低压模块更换为±2.5kPa", RealtimeLevel.Warn);

        op.Report(tvalue);
        
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
    public string? DeviceFamily => "P21";

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

        await op.Sleep(500);
        //获取条件

        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Open" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Open" }, ct);
        Pressure AtmosSensor = new Pressure(0, "kPa");
        await op.Dut.CommandAsync("GetAtmosSensor", null, ct);
        {
            var atmTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetAtmosSensor", null, ct), "读取大气压传感器");
            if (atmTxt != null && double.TryParse(atmTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var atmVal))
                AtmosSensor = new Pressure(atmVal, "kPa");
        }
        // 切换高压量程
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetControlPressureModel", new[]{ "High" }, ct), "切换高压量程"))) pass = false;
        await op.Sleep(5000);

        // 设定内部模块压力单位
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetPressureUnit_IPM", null, ct), "设定内部模块压力单位"))) pass = false;

        // 获取压力控制量程范围
        // 获取压力控制量程范围（Lower~Upper）
        var rangeTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetSetPointLimitPressureRange", null, ct), "获取压力控制量程范围");
        if (rangeTxt is null) pass = false;
        else op.Report("压力控制量程范围 = {rangeTxt} kPa");

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

        // 获取内部模块量程下限
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressureControlRange_LowerLimit", null, ct), "获取内部模块量程下限"))) pass = false;

        // 设置压力目标（下限）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ InnerModulePressureLowerer.Value.ToString(CultureInfo.InvariantCulture) }, ct), "设置压力目标"))) pass = false;
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
            await op.Sleep(500);
        }

        await op.Sleep(2000);

        // 设置控制器测量模式
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTestMode", null, ct), "设置控制器测量模式"))) pass = false;

        await op.Sleep(50);

        // 触发并读取内部模块压力（负压段第一次）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        // 读取负压气源压力（第一次）
        var vpFirstRaw = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetVacuumPressure", null, ct), "获取负压气源压力");
        if (vpFirstRaw != null && double.TryParse(vpFirstRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var vpFirstVal))
            getSourcePressure30SFirst = new Pressure(vpFirstVal, "kPa");
        else
            pass = false;

        var P1s = new List<double>();

        // 连续读取内部模块压力和设备温度（负压段第一次）
        var pfFirstTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetPressure_IPM", null, ct), "获取内部模块压力值");
        var tFirstTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetDev_T", null, ct), "获取设备温度");
        if (pfFirstTxt != null && double.TryParse(pfFirstTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var pfFirstVal))
            getInternalModulePressure30SFirst = new Pressure(pfFirstVal, "kPa");
        else
            pass = false;
        tstr = tFirstTxt ?? "";
        tvalue += $"{getInternalModulePressure30SFirst.Value},{tstr};";
        op.Report(tvalue);
        P1s.Add(getInternalModulePressure30SFirst.Value);

        await op.Sleep(50);

        // 触发并读取内部模块压力（负压段第二次）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        // 读取负压气源压力（第二次）
        var vpSecondRaw = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetVacuumPressure", null, ct), "获取负压气源压力");
        if (vpSecondRaw != null && double.TryParse(vpSecondRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var vpSecondVal))
            getSourcePressure30SSecond = new Pressure(vpSecondVal, "kPa");
        else
            pass = false;

        op.Report($"负压30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.NegativeExport, Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        negativeinternalPressureRate = Math.Abs((Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value)) / getInternalModulePressure30SFirst.Value);
        op.Report($"负压30秒泄露率: {(negativeinternalPressureRate*100).ToString("F4") + "%"}");

        op.Report($"负压气源压力30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.NegativeSource, Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        negativeSupplyPressureRate = Math.Abs((Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value)) / getSourcePressure30SFirst.Value);
        op.Report($"负压气源压力30秒泄露量(新): {(negativeSupplyPressureRate * 100).ToString("F4") + "%"}");

        // 读取当前压力
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "读取当前压力"))) pass = false;
        // 排空
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetVentMode", null, ct), "排空"))) pass = false;
        await op.Sleep(3000);

        // 获取内部模块（排空后压力）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        // 获取内部模块量程上限
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressureControlRange_UpperLimit", null, ct), "获取内部模块量程上限"))) pass = false;

        // 设置压力目标（上限）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ InnerModulePressureUpper.Value.ToString(CultureInfo.InvariantCulture) }, ct), "设置压力目标"))) pass = false;
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
            await op.Sleep(500);
        }

        await op.Sleep(2000);

        // 设置控制器测量模式
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTestMode", null, ct), "设置控制器测量模式"))) pass = false;

        await op.Sleep(50);

        // 触发并读取内部模块压力（正压段第一次）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        // 读取正压气源压力（第一次）
        var spFirstRaw = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetSupplyPressure", null, ct), "获取正压气源压力");
        if (spFirstRaw != null && double.TryParse(spFirstRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var spFirstVal))
            getSourcePressure30SFirst = new Pressure(spFirstVal, "kPa");
        else
            pass = false;

        var P2s = new List<double>();

        // 连续读取内部模块压力和设备温度（正压段第一次）
        var pfSecondTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetPressure_IPM", null, ct), "获取内部模块压力值");
        var tSecondTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetDev_T", null, ct), "获取设备温度");
        if (pfSecondTxt != null && double.TryParse(pfSecondTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var pfSecondVal))
            getInternalModulePressure30SFirst = new Pressure(pfSecondVal, "kPa");
        else
            pass = false;
        tstr = tSecondTxt ?? "";
        tvalue += $"{getInternalModulePressure30SFirst.Value},{tstr};";
        op.Report(tvalue);
        P2s.Add(getInternalModulePressure30SFirst.Value);

        await op.Sleep(50);

        // 触发并读取内部模块压力（正压段第二次）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        // 读取正压气源压力（第二次）
        var spSecondRaw = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetSupplyPressure", null, ct), "获取正压气源压力");
        if (spSecondRaw != null && double.TryParse(spSecondRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var spSecondVal))
            getSourcePressure30SSecond = new Pressure(spSecondVal, "kPa");
        else
            pass = false;

        op.Report($"正压30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.PositiveExport, Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        positiveinternalPressureRate = Math.Abs((Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value)) / getInternalModulePressure30SFirst.Value);
        op.Report($"正压30秒泄露率: {(positiveinternalPressureRate*100).ToString("F4") + "%"}");

        op.Report($"正压气源压力30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.MpDpLlp, LeakPosition.PositiveSource, Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        positiveSupplyPressureRate = Math.Abs((Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value)) / getSourcePressure30SFirst.Value);
        op.Report($"正压气源压力30秒泄露率: {(positiveSupplyPressureRate * 100).ToString("F4") + "%"}");

        // 读取当前压力
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "读取当前压力"))) pass = false;

        // 排空
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetVentMode", null, ct), "排空"))) pass = false;
        await op.Sleep(3000);

        // 获取内部模块（排空后压力）
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct), "获取内部模块压力"))) pass = false;

        await op.Sleep(2000);
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
        
        return pass ? StepResult.Pass("高压量程压力泄露测试和排空测试通过") : StepResult.Fail("高压量程压力泄露测试和排空测试未通过");
    }
}
