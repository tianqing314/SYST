using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;

namespace SYST.TestSteps.ConST811A.ConST811A_LLP_Machine;

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

    /// <summary>真机稳定延时（继电器切档/设值后需等待）。短延时（≤2s）不输出日志，长延时（>3s）带倒计时。</summary>
    public async Task Sleep(int ms)
    {
        if (!P06.IsRealHardware)
        {
            if (ms > 2000) Report($"等待 {ms / 1000.0:F1}s（仿真跳过）");
            return;
        }
        if (ms <= 2000)
        {
            await Task.Delay(ms, _ct);
            return;
        }
        var sec = ms / 1000.0;
        Report($"等待中... 剩余{sec:F1}s");
        if (ms > 3000)
        {
            var t0 = DateTime.Now;
            var lastReported = -1;
            while ((DateTime.Now - t0).TotalMilliseconds < ms)
            {
                var remaining = (int)((ms - (DateTime.Now - t0).TotalMilliseconds) / 1000);
                if (remaining != lastReported && remaining > 0)
                {
                    UpdateLastReport($"等待中... 剩余{remaining}s");
                    lastReported = remaining;
                }
                await Task.Delay(500, _ct);
            }
        }
        else
        {
            await Task.Delay(ms, _ct);
        }
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
/// 低压量程压力泄露测试和排空测试。PORT: 旧脚本方法 LeakTestComposition_Low_LLP（JSON Entry: LeakTestComposition_Low_LLP）。
/// </summary>
public sealed class LeakTestComposition_Low_LLPConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "LeakTestComposition_Low_LLP";
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
        var failures = new List<string>();
        // 记录压力值与温度值，最终统一 op.Report
        var tvalue = new StringBuilder();
        tvalue.Append("压力值,高压温度,低压温度,泵温度,电测板温度^");

        await op.Sleep(500);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[] { "InnerModule_H", "Open" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[] { "InnerModule_L", "Open" }, ct);
        // 读取大气压（GetAtmosSensor 用 QueryTextAsync 读回），参与 ml/min 计算
        var atmos = 101.325;
        {
            var atmosTxt = await op.Dut.QueryTextAsync("GetAtmosSensor", null, ct);
            if (double.TryParse(atmosTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                atmos = v;
        }

        // 1. 切换低压量程
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetControlPressureModel", new[] { "Low" }, ct), "切换低压量程"))) { failures.Add("切换低压量程失败"); pass = false; }
        await op.Sleep(5000);

        // 2. 设定内部模块压力单位
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetPressureUnit_IPM", null, ct), "设定内部模块压力单位"))) { failures.Add("设定内部模块压力单位失败"); pass = false; }

        // 3. 获取压力控制量程范围（Lower~Upper）
        var rangeTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetSetPointLimitPressureRange", null, ct), "获取压力控制量程范围");
        if (rangeTxt is null) { failures.Add("获取压力控制量程范围失败"); pass = false; }

        // 负压段
        double? lower = await LeakTestCompositionHelper.ReadPressureAsync(op, ct, "GetPressureControlRange_LowerLimit", "获取内部模块量程下限");
        if (lower is null) { failures.Add("获取内部模块量程下限失败"); pass = false; }

        if (lower is { } lowerVal)
        {
            // 设置压力目标（下限）并控压：差值率≤5% 且 Stable；负压控稳时间条件判定
            if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTargetPressure", new[] { lowerVal.ToString(CultureInfo.InvariantCulture) }, ct), "设置压力目标(下限)"))) { failures.Add("设置负压目标压力失败"); pass = false; }
            var vp1s = new List<double>();
            var negControlOk = await LeakTestCompositionHelper.ControlPressureAsync(op, ct, lowerVal, "负压控稳时间", "负压");
            if (!negControlOk) { failures.Add("负压控压失败"); pass = false; }

            await op.Sleep(2000);
            // 设置控制器测量模式
            if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTestMode", null, ct), "设置控制器测量模式(负压)"))) { failures.Add("设置控制器测量模式(负压)失败"); pass = false; }
            await op.Sleep(50);

            // 负压段稳定等待（旧脚本计时45秒；Low_LLP 无负压稳定时间条件则默认 45）
            var negStableSec = LeakTestCompositionHelper.CondMin(op, "负压稳定时间", 45);
            await LeakTestCompositionHelper.WaitStableAsync(op, ct, negStableSec, "负压");

            // 读负压模块压力 45 秒第一个值 + 负压气源第一个值
            double? negFirst = await LeakTestCompositionHelper.ReadPressureAsync(op, ct, "GetPressure_IPM", "负压模块压力(第一个值)");
            double? negSrcFirst = await LeakTestCompositionHelper.ReadPressureAsync(op, ct, "GetVacuumPressure", "负压气源压力(第一个值)");
            if (negFirst is null) { failures.Add("读取负压模块压力(第一个值)失败"); pass = false; }
            if (negSrcFirst is null) { failures.Add("读取负压气源压力(第一个值)失败"); pass = false; }

            // 30 秒采样（模块压力 + 温度），供曲线与 tvalue
            var p1s = await LeakTestCompositionHelper.SamplePressureAsync(op, ctx, ct, tvalue, 30);

            // 读负压模块压力第二个值 + 负压气源第二个值
            double? negSecond = await LeakTestCompositionHelper.ReadPressureAsync(op, ct, "GetPressure_IPM", "负压模块压力(第二个值)");
            double? negSrcSecond = await LeakTestCompositionHelper.ReadPressureAsync(op, ct, "GetVacuumPressure", "负压气源压力(第二个值)");
            if (negSecond is null) { failures.Add("读取负压模块压力(第二个值)失败"); pass = false; }
            if (negSrcSecond is null) { failures.Add("读取负压气源压力(第二个值)失败"); pass = false; }

            // 负压模块/气源泄露判定（速率% + ml/min + 条件）
            if (negFirst is { } nf && negSecond is { } ns)
                if (!LeakTestCompositionHelper.JudgeLeak(op, nf, ns, "负压45秒泄露率", "负压模块30秒泄露", LeakPosition.NegativeExport, atmos, 30)) { failures.Add("负压模块泄露率超标"); pass = false; }
            if (negSrcFirst is { } nsf && negSrcSecond is { } nss)
                if (!LeakTestCompositionHelper.JudgeLeak(op, nsf, nss, "负压气源30秒泄露率", "负压气源30秒泄露", LeakPosition.NegativeSource, atmos, 30)) { failures.Add("负压气源泄露率超标"); pass = false; }
            op.Report("负压泄露测试 = 完成");

            // 负压排空
            if (!(await LeakTestCompositionHelper.VentAsync(op, ct, "排空后压力上限", "负压"))) { failures.Add("负压排空失败"); pass = false; }
            // 负压采样数据入曲线
            ctx.RecordProcessData(new ProcessDataSeries {
                StartedAt = DateTime.Now,
                TimeSec = Enumerable.Range(0, p1s.Count).Select(i => i * 0.15).ToArray(),
                Channels = new[] { new ProcessChannel("负压泄漏压力变化", p1s.ToArray()) }
            });
        }

        // 正压段
        double? upper = await LeakTestCompositionHelper.ReadPressureAsync(op, ct, "GetPressureControlRange_UpperLimit", "获取内部模块量程上限");
        if (upper is null) { failures.Add("获取内部模块量程上限失败"); pass = false; }

        if (upper is { } upperVal)
        {
            // 设置压力目标（上限）并控压
            if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTargetPressure", new[] { upperVal.ToString(CultureInfo.InvariantCulture) }, ct), "设置压力目标(上限)"))) { failures.Add("设置正压目标压力失败"); pass = false; }
            var vp2s = new List<double>();
            var posControlOk = await LeakTestCompositionHelper.ControlPressureAsync(op, ct, upperVal, "正压控稳时间", "正压");
            if (!posControlOk) { failures.Add("正压控压失败"); pass = false; }

            await op.Sleep(2000);
            if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTestMode", null, ct), "设置控制器测量模式(正压)"))) { failures.Add("设置控制器测量模式(正压)失败"); pass = false; }
            await op.Sleep(50);

            // 正压段稳定等待（旧脚本计时30秒；无正压稳定时间条件则默认 30）
            var posStableSec = LeakTestCompositionHelper.CondMin(op, "正压稳定时间", 30);
            await LeakTestCompositionHelper.WaitStableAsync(op, ct, posStableSec, "正压");

            double? posFirst = await LeakTestCompositionHelper.ReadPressureAsync(op, ct, "GetPressure_IPM", "正压模块压力(第一个值)");
            double? posSrcFirst = await LeakTestCompositionHelper.ReadPressureAsync(op, ct, "GetSupplyPressure", "正压气源压力(第一个值)");
            if (posFirst is null) { failures.Add("读取正压模块压力(第一个值)失败"); pass = false; }
            if (posSrcFirst is null) { failures.Add("读取正压气源压力(第一个值)失败"); pass = false; }

            var p2s = await LeakTestCompositionHelper.SamplePressureAsync(op, ctx, ct, tvalue, 30);

            double? posSecond = await LeakTestCompositionHelper.ReadPressureAsync(op, ct, "GetPressure_IPM", "正压模块压力(第二个值)");
            double? posSrcSecond = await LeakTestCompositionHelper.ReadPressureAsync(op, ct, "GetSupplyPressure", "正压气源压力(第二个值)");
            if (posSecond is null) { failures.Add("读取正压模块压力(第二个值)失败"); pass = false; }
            if (posSrcSecond is null) { failures.Add("读取正压气源压力(第二个值)失败"); pass = false; }

            if (posFirst is { } pf && posSecond is { } ps)
                if (!LeakTestCompositionHelper.JudgeLeak(op, pf, ps, "正压30秒泄露率", "正压模块30秒泄露", LeakPosition.PositiveExport, atmos, 30)) { failures.Add("正压模块泄露率超标"); pass = false; }
            if (posSrcFirst is { } psf && posSrcSecond is { } pss)
                if (!LeakTestCompositionHelper.JudgeLeak(op, psf, pss, "正压气源30秒泄露率", "正压气源30秒泄露", LeakPosition.PositiveSource, atmos, 30)) { failures.Add("正压气源泄露率超标"); pass = false; }
            op.Report("正压泄露测试 = 完成");

            // 正压排空
            if (!(await LeakTestCompositionHelper.VentAsync(op, ct, "排空后压力上限", "正压"))) { failures.Add("正压排空失败"); pass = false; }
            ctx.RecordProcessData(new ProcessDataSeries {
                StartedAt = DateTime.Now,
                TimeSec = Enumerable.Range(0, p2s.Count).Select(i => i * 0.15).ToArray(),
                Channels = new[] { new ProcessChannel("正压泄漏压力变化", p2s.ToArray()) }
            });
        }

        op.Report($"压力值与温度值: {tvalue}");

        // 收尾：排空并关闭模块稳定
        await op.Dut.CommandAsync("SetVentMode", null, ct);
        var guard = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var stateTxt = await op.Dut.QueryTextAsync("GetPressureStableState", null, ct);
            if (stateTxt.Contains("Stable", StringComparison.OrdinalIgnoreCase)) break;
            if (++guard > 600) { op.Report("等待压力稳定超时(300s)", RealtimeLevel.Warn); break; }
            await op.Sleep(500);
        }
        await op.Dut.CommandAsync("SetModuleStableEnable", new[] { "InnerModule_H", "Close" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[] { "InnerModule_L", "Close" }, ct);

        // 低压泄露测试完成，提示操作员更换低压模块为±500Pa
        op.Report("低压泄露测试完成，需要将低压模块更换为±500Pa，更换完成后继续测试", RealtimeLevel.Info);

        if (pass) return StepResult.Pass("低压量程压力泄露测试和排空测试通过");
        var msg = "低压量程压力泄露测试和排空测试未通过" + (failures.Count > 0 ? "（" + string.Join("，", failures) + "）" : "");
        return StepResult.Fail(msg);
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
    public string? DeviceFamily => "P21";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        var failures = new List<string>();
        // 记录压力值与温度值，最终统一 op.Report
        var tvalue = new StringBuilder();
        tvalue.Append("压力值,高压温度,低压温度,泵温度,电测板温度^");

        await op.Sleep(500);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[] { "InnerModule_H", "Open" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[] { "InnerModule_L", "Open" }, ct);
        // 读取大气压（GetAtmosSensor 用 QueryTextAsync 读回），参与 ml/min 计算
        var atmos = 101.325;
        {
            var atmosTxt = await op.Dut.QueryTextAsync("GetAtmosSensor", null, ct);
            if (double.TryParse(atmosTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                atmos = v;
        }

        // 1. 切换高压量程
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetControlPressureModel", new[] { "High" }, ct), "切换高压量程"))) { failures.Add("切换高压量程失败"); pass = false; }
        await op.Sleep(5000);

        // 2. 设定内部模块压力单位
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetPressureUnit_IPM", null, ct), "设定内部模块压力单位"))) { failures.Add("设定内部模块压力单位失败"); pass = false; }

        // 3. 获取压力控制量程范围（Lower~Upper）
        var rangeTxt = await op.TryQueryValue(() => op.Dut.QueryTextAsync("GetSetPointLimitPressureRange", null, ct), "获取压力控制量程范围");
        if (rangeTxt is null) { failures.Add("获取压力控制量程范围失败"); pass = false; }

        // 负压段
        double? lower = await LeakTestCompositionHelper.ReadPressureAsync(op, ct, "GetPressureControlRange_LowerLimit", "获取内部模块量程下限");
        if (lower is null) { failures.Add("获取内部模块量程下限失败"); pass = false; }

        if (lower is { } lowerVal)
        {
            // 设置压力目标（下限）并控压
            if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTargetPressure", new[] { lowerVal.ToString(CultureInfo.InvariantCulture) }, ct), "设置压力目标(下限)"))) { failures.Add("设置负压目标压力失败"); pass = false; }
            var vp1s = new List<double>();
            var negControlOk = await LeakTestCompositionHelper.ControlPressureAsync(op, ct, lowerVal, "负压控稳时间", "负压");
            if (!negControlOk) { failures.Add("负压控压失败"); pass = false; }

            await op.Sleep(2000);
            if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTestMode", null, ct), "设置控制器测量模式(负压)"))) { failures.Add("设置控制器测量模式(负压)失败"); pass = false; }
            await op.Sleep(50);

            // 负压段稳定等待（High_LLP 用"负压稳定时间"条件，缺省 45）
            var negStableSec = LeakTestCompositionHelper.CondMin(op, "负压稳定时间", 45);
            await LeakTestCompositionHelper.WaitStableAsync(op, ct, negStableSec, "负压");

            double? negFirst = await LeakTestCompositionHelper.ReadPressureAsync(op, ct, "GetPressure_IPM", "负压模块压力(第一个值)");
            double? negSrcFirst = await LeakTestCompositionHelper.ReadPressureAsync(op, ct, "GetVacuumPressure", "负压气源压力(第一个值)");
            if (negFirst is null) { failures.Add("读取负压模块压力(第一个值)失败"); pass = false; }
            if (negSrcFirst is null) { failures.Add("读取负压气源压力(第一个值)失败"); pass = false; }

            var p1s = await LeakTestCompositionHelper.SamplePressureAsync(op, ctx, ct, tvalue, 30);

            double? negSecond = await LeakTestCompositionHelper.ReadPressureAsync(op, ct, "GetPressure_IPM", "负压模块压力(第二个值)");
            double? negSrcSecond = await LeakTestCompositionHelper.ReadPressureAsync(op, ct, "GetVacuumPressure", "负压气源压力(第二个值)");
            if (negSecond is null) { failures.Add("读取负压模块压力(第二个值)失败"); pass = false; }
            if (negSrcSecond is null) { failures.Add("读取负压气源压力(第二个值)失败"); pass = false; }

            if (negFirst is { } nf && negSecond is { } ns)
                if (!LeakTestCompositionHelper.JudgeLeak(op, nf, ns, "负压45秒泄露率", "负压模块30秒泄露", LeakPosition.NegativeExport, atmos, 30)) { failures.Add("负压模块泄露率超标"); pass = false; }
            if (negSrcFirst is { } nsf && negSrcSecond is { } nss)
                if (!LeakTestCompositionHelper.JudgeLeak(op, nsf, nss, "负压气源30秒泄露率", "负压气源30秒泄露", LeakPosition.NegativeSource, atmos, 30)) { failures.Add("负压气源泄露率超标"); pass = false; }
            op.Report("负压泄露测试 = 完成");

            if (!(await LeakTestCompositionHelper.VentAsync(op, ct, "排空后压力上限", "负压"))) { failures.Add("负压排空失败"); pass = false; }
            ctx.RecordProcessData(new ProcessDataSeries {
                StartedAt = DateTime.Now,
                TimeSec = Enumerable.Range(0, p1s.Count).Select(i => i * 0.15).ToArray(),
                Channels = new[] { new ProcessChannel("负压泄漏压力变化", p1s.ToArray()) }
            });
        }

        // 正压段
        double? upper = await LeakTestCompositionHelper.ReadPressureAsync(op, ct, "GetPressureControlRange_UpperLimit", "获取内部模块量程上限");
        if (upper is null) { failures.Add("获取内部模块量程上限失败"); pass = false; }

        if (upper is { } upperVal)
        {
            if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTargetPressure", new[] { upperVal.ToString(CultureInfo.InvariantCulture) }, ct), "设置压力目标(上限)"))) { failures.Add("设置正压目标压力失败"); pass = false; }
            var vp2s = new List<double>();
            var posControlOk = await LeakTestCompositionHelper.ControlPressureAsync(op, ct, upperVal, "正压控稳时间", "正压");
            if (!posControlOk) { failures.Add("正压控压失败"); pass = false; }

            await op.Sleep(2000);
            if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetTestMode", null, ct), "设置控制器测量模式(正压)"))) { failures.Add("设置控制器测量模式(正压)失败"); pass = false; }
            await op.Sleep(50);

            // 正压段稳定等待（High_LLP 用"正压稳定时间"条件，缺省 30）
            var posStableSec = LeakTestCompositionHelper.CondMin(op, "正压稳定时间", 30);
            await LeakTestCompositionHelper.WaitStableAsync(op, ct, posStableSec, "正压");

            double? posFirst = await LeakTestCompositionHelper.ReadPressureAsync(op, ct, "GetPressure_IPM", "正压模块压力(第一个值)");
            double? posSrcFirst = await LeakTestCompositionHelper.ReadPressureAsync(op, ct, "GetSupplyPressure", "正压气源压力(第一个值)");
            if (posFirst is null) { failures.Add("读取正压模块压力(第一个值)失败"); pass = false; }
            if (posSrcFirst is null) { failures.Add("读取正压气源压力(第一个值)失败"); pass = false; }

            var p2s = await LeakTestCompositionHelper.SamplePressureAsync(op, ctx, ct, tvalue, 30);

            double? posSecond = await LeakTestCompositionHelper.ReadPressureAsync(op, ct, "GetPressure_IPM", "正压模块压力(第二个值)");
            double? posSrcSecond = await LeakTestCompositionHelper.ReadPressureAsync(op, ct, "GetSupplyPressure", "正压气源压力(第二个值)");
            if (posSecond is null) { failures.Add("读取正压模块压力(第二个值)失败"); pass = false; }
            if (posSrcSecond is null) { failures.Add("读取正压气源压力(第二个值)失败"); pass = false; }

            if (posFirst is { } pf && posSecond is { } ps)
                if (!LeakTestCompositionHelper.JudgeLeak(op, pf, ps, "正压30秒泄露率", "正压模块30秒泄露", LeakPosition.PositiveExport, atmos, 30)) { failures.Add("正压模块泄露率超标"); pass = false; }
            if (posSrcFirst is { } psf && posSrcSecond is { } pss)
                if (!LeakTestCompositionHelper.JudgeLeak(op, psf, pss, "正压气源30秒泄露率", "正压气源30秒泄露", LeakPosition.PositiveSource, atmos, 30)) { failures.Add("正压气源泄露率超标"); pass = false; }
            op.Report("正压泄露测试 = 完成");

            if (!(await LeakTestCompositionHelper.VentAsync(op, ct, "排空后压力上限", "正压"))) { failures.Add("正压排空失败"); pass = false; }
            ctx.RecordProcessData(new ProcessDataSeries {
                StartedAt = DateTime.Now,
                TimeSec = Enumerable.Range(0, p2s.Count).Select(i => i * 0.15).ToArray(),
                Channels = new[] { new ProcessChannel("正压泄漏压力变化", p2s.ToArray()) }
            });
        }

        op.Report($"压力值与温度值: {tvalue}");

        // 收尾：排空并关闭模块稳定
        await op.Dut.CommandAsync("SetVentMode", null, ct);
        var guard = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var stateTxt = await op.Dut.QueryTextAsync("GetPressureStableState", null, ct);
            if (stateTxt.Contains("Stable", StringComparison.OrdinalIgnoreCase)) break;
            if (++guard > 600) { op.Report("等待压力稳定超时(300s)", RealtimeLevel.Warn); break; }
            await op.Sleep(500);
        }
        await op.Dut.CommandAsync("SetModuleStableEnable", new[] { "InnerModule_H", "Close" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[] { "InnerModule_L", "Close" }, ct);

        if (pass) return StepResult.Pass("高压量程压力泄露测试和排空测试通过");
        var msg = "高压量程压力泄露测试和排空测试未通过" + (failures.Count > 0 ? "（" + string.Join("，", failures) + "）" : "");
        return StepResult.Fail(msg);
    }
}
