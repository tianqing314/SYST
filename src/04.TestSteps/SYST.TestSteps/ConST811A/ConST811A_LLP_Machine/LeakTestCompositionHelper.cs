using System.Globalization;
using System.Text;
using SYST.Core.Abstractions;

namespace SYST.TestSteps.ConST811A.ConST811A_LLP_Machine;

/// <summary>
/// 量程压力泄露测试共享逻辑（低压/高压量程泄露测试和排空测试）。
/// 移植旧平台 <c>p21.bots.autotest.cs</c> 的 LeakTestComposition_Low_LLP / LeakTestComposition_High_LLP：
/// 控压到位（差值率≤5%且 Stable）、控稳时间条件判定、稳定等待、30 秒采样、泄露速率与 ml/min 计算、排空判定。
/// </summary>
internal static class LeakTestCompositionHelper
{
    /// <summary>控压完成判定阈值（与旧脚本 rate<=0.05 一致）。</summary>
    private const double ControlRate = 0.05;

    /// <summary>采样间隔（毫秒，与旧脚本 150ms 一致）。</summary>
    private const int SampleIntervalMs = 150;

    /// <summary>控压轮询超时（600s 兜底，正常由控稳时间条件截断）。</summary>
    private const int ControlPollGuard = 1200;

    /// <summary>排空后等待稳定超时（300s 兜底）。</summary>
    private const int VentStableGuard = 600;

    /// <summary>按条件名取 Range 条件的 Min 值（无此条件或 Min<=0 返回默认值）。</summary>
    public static double CondMin(ConST811AOps op, string name, double def)
    {
        var c = op.Cond(name);
        if (c?.Min is { } min && min > 0) return min;
        return def;
    }

    /// <summary>
    /// 控压轮询：设目标后循环读 GetPressure_IPM + GetPressureStableState，
    /// 差值率 |(当前-目标)/目标| ≤ 5% 且 Stable 判定完成；超过控稳时间条件（Range.Max 秒）判失败。
    /// </summary>
    /// <param name="op">Ops。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="target">目标压力（kPa）。</param>
    /// <param name="controlCondName">控稳时间条件名（正压控稳时间/负压控稳时间）。</param>
    /// <param name="label">报告标签（正压/负压）。</param>
    /// <returns>是否控压成功。</returns>
    public static async Task<bool> ControlPressureAsync(ConST811AOps op, CancellationToken ct,
        double target, string controlCondName, string label)
    {
        if (target == 0)
        {
            op.Report($"{label}控压目标为 0，跳过", RealtimeLevel.Warn);
            return false;
        }

        var t0 = DateTime.Now;
        var guard = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var stateTxt = await op.Dut.QueryTextAsync("GetPressureStableState", null, ct);
            var ipmTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipm))
            {
                var rate = Math.Abs((ipm - target) / target);
                var stable = stateTxt.Contains("Stable", StringComparison.OrdinalIgnoreCase);
                if (stable && rate <= ControlRate)
                {
                    op.Report($"{label}控压完成，当前 {ipm:F4} kPa 目标 {target:F4} kPa，耗时{(DateTime.Now - t0).TotalSeconds:F1}s");
                    return true;
                }
            }

            var elapsed = (DateTime.Now - t0).TotalSeconds;
            // 控稳时间条件：超过 Range.Max 秒判失败（旧脚本 PositiveControlTime/NegativeControlTime.IsTrue）
            if (op.Cond(controlCondName)?.Max is { } maxSec && maxSec > 0 && elapsed > maxSec)
            {
                op.Report($"{label}控压超时（> {maxSec:0}s），当前 {ipmTxt} kPa", RealtimeLevel.Warn);
                return false;
            }

            if (++guard > ControlPollGuard)
            {
                op.Report($"{label}控压超时（600s 兜底）", RealtimeLevel.Warn);
                return false;
            }
            await Task.Delay(500, ct);
        }
    }

    /// <summary>稳定等待（读采样第一个值前；旧脚本计时45秒/30秒 或 WaitTimeN/WaitTimeP 条件）。带倒计时显示。</summary>
    public static async Task WaitStableAsync(ConST811AOps op, CancellationToken ct, double seconds, string label)
    {
        var t0 = DateTime.Now;
        var lastReported = -1;
        op.Report($"{label}稳定等待中... 剩余{seconds:0}s");
        while ((DateTime.Now - t0).TotalSeconds < seconds)
        {
            ct.ThrowIfCancellationRequested();
            var remaining = (int)(seconds - (DateTime.Now - t0).TotalSeconds);
            if (remaining != lastReported && remaining % 5 == 0 && remaining > 0)
            {
                op.UpdateLastReport($"{label}稳定等待中... 剩余{remaining}s");
                lastReported = remaining;
            }
            await Task.Delay(500, ct);
        }
        op.Report($"{label}稳定等待完成（{seconds:0}s）");
    }

    /// <summary>按条件名读取压力值（失败重试，返回 null 表示失败）。</summary>
    public static async Task<double?> ReadPressureAsync(ConST811AOps op, CancellationToken ct,
        string query, string desc)
    {
        var txt = await op.TryQueryValue(() => op.Dut.QueryTextAsync(query, null, ct), desc);
        if (txt != null && double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            return v;
        }
        return null;
    }

    /// <summary>
    /// 30 秒采样段：循环读模块压力 + 温度，追加到 tvalue，返回采样值列表（用于曲线）。带倒计时显示。
    /// </summary>
    public static async Task<List<double>> SamplePressureAsync(ConST811AOps op, CancellationToken ct,
        StringBuilder tvalue, double seconds)
    {
        var samples = new List<double>();
        var t0 = DateTime.Now;
        var lastReported = -1;
        op.Report($"采样中... 剩余{seconds:0}s");
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            double v = 0;
            var infoTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(infoTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) v = parsed;
            var tstr = await op.Dut.QueryTextAsync("GetDev_T", null, ct);
            tvalue.Append($"{v},{tstr};");
            samples.Add(v);
            var remaining = (int)(seconds - (DateTime.Now - t0).TotalSeconds);
            if (remaining != lastReported && remaining % 5 == 0 && remaining > 0)
            {
                op.UpdateLastReport($"采样中... 剩余{remaining}s");
                lastReported = remaining;
            }
            if ((DateTime.Now - t0).TotalSeconds > seconds) break;
            await Task.Delay(SampleIntervalMs, ct);
        }
        op.Report($"采样完成（{seconds:0}s）");
        return samples;
    }

    /// <summary>
    /// 计算并判定泄露：速率%（前后差值/首值）与 ml/min（LeakFormula），按条件名判定合格。
    /// </summary>
    /// <param name="op">Ops。</param>
    /// <param name="first">采样首值（kPa）。</param>
    /// <param name="second">采样末值（kPa）。</param>
    /// <param name="condName">泄露率条件名（正压30秒泄露率等）。</param>
    /// <param name="label">报告标签。</param>
    /// <param name="pos">泄露位置（决定容积）。</param>
    /// <param name="atmos">大气压（kPa）。</param>
    /// <param name="timeSec">采样时长（秒，30）。</param>
    /// <returns>是否合格。</returns>
    public static bool JudgeLeak(ConST811AOps op, double first, double second, string condName,
        string label, LeakPosition pos, double atmos, double timeSec)
    {
        var rate = first == 0 ? double.MaxValue : Math.Abs((second - first) / first);
        var pct = rate * 100;
        var mlMin = LeakFormula.Compute(LeakDeviceModel.MpDpLlp, pos, Math.Abs(second - first), timeSec, atmos);
        op.Report($"{label}：速率 {pct:F4}%，泄漏量 {mlMin:0.000} ml/min");
        return op.Judge(condName, pct, label, "%");
    }

    /// <summary>
    /// 排空段：读当前压力 → SetVentMode → 等 Stable → 读排空后压力 → 按"排空后压力上限"条件判定。
    /// </summary>
    public static async Task<bool> VentAsync(ConST811AOps op, CancellationToken ct,
        string ventCondName, string label)
    {
        if (!(await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetVentMode", null, ct), $"{label}排空")))
            return false;

        await op.Sleep(3000);
        // 等待压力稳定
        var guard = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var stateTxt = await op.Dut.QueryTextAsync("GetPressureStableState", null, ct);
            if (stateTxt.Contains("Stable", StringComparison.OrdinalIgnoreCase)) break;
            if (++guard > VentStableGuard)
            {
                op.Report($"{label}排空后等待压力稳定超时(300s)", RealtimeLevel.Warn);
                return false;
            }
            await op.Sleep(500);
        }

        var finalTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
        if (double.TryParse(finalTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var finalVal))
        {
            op.Report($"{label}排空后压力 = {finalVal} kPa");
            return op.Judge(ventCondName, Math.Abs(finalVal), $"{label}排空后压力", "kPa");
        }
        op.Report($"{label}排空后读取压力失败", RealtimeLevel.Warn);
        return false;
    }
}
