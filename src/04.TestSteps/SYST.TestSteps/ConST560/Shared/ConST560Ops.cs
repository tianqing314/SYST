using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;
using SYST.Devices.Abstractions.Dut;
using SYST.Devices.Abstractions.Test;

namespace SYST.TestSteps.ConST560;

/// <summary>
/// ConST560 手持校验仪公共 Ops（真实场景版）。
/// 依据旧平台 E05 ConST575_SelfCheck 接线与工装动作，组合：
/// - ConST560（被检）
/// - ConST326（标准信号源）
/// - ZCZH（VA 采集器）
/// - ZQWL（齿轮箱继电器矩阵）
/// 其中 ConST326/ZCZH 为测试桩/真机接口，可通过 manifest ToolDevices 挂载替换。
/// </summary>
internal sealed class ConST560Ops
{
    private readonly ITestContext _ctx;
    private readonly CancellationToken _ct;

    public readonly IConST560Dut Dut;
    public readonly IZQWLRelayMatrix ZQWL;
    public readonly IConST326 ConST326;
    public readonly IZCZH ZCZH;

    public ConST560Ops(ITestContext ctx, CancellationToken ct)
    {
        _ctx = ctx;
        _ct = ct;
        Dut = ctx.GetDevice<IConST560Dut>();
        ZQWL = ctx.GetDevice<IZQWLRelayMatrix>("ZQWL");
        ConST326 = ctx.GetDevice<IConST326>("ConST326");
        ZCZH = ctx.GetDevice<IZCZH>("ZCZH");
    }

    public static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public void Report(string m, RealtimeLevel l = RealtimeLevel.Info) => _ctx.Report(m, l);
    public void Ok(string desc) => Report($"✓ {desc}", RealtimeLevel.Success);
    public void Fail(string desc) => Report($"✗ {desc}", RealtimeLevel.Error);
    public void Text(string label, string? value) => Report($"  {label}: {value ?? "(空)"}");
    public void Value(string label, double value, string unit = "")
        => Report(unit.Length == 0 ? $"  {label}: {F(value)}" : $"  {label}: {F(value)}{unit}");

    public Task Sleep(int ms) { Report($"  等待稳定 {ms}ms ..."); return Task.Delay(ms, _ct); }
    public Task Sleep(int ms, string reason) { Report($"  {reason}，等待 {ms}ms ..."); return Task.Delay(ms, _ct); }

    public ConditionDescriptor? Cond(string name)
    {
        foreach (var c in _ctx.Conditions)
            if (c.Name == name) return c;
        return null;
    }

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

    /// <summary>根据旧平台工装齿轮表执行 ZQWL 动作（地址-通道掩码），可批量吸合/断开。</summary>
    public async Task<bool> SwitchGearAsync(GearMap map, bool on)
    {
        foreach (var kv in map)
        {
            if (!await ZQWL.SetChannelAsync(kv.Address, kv.Channel, on, _ct))
                return false;
        }
        return true;
    }

    public async Task<double> MeasureCurrentWithZCZHAsync(string unit, int samples = 20, int waitMs = 200)
    {
        var sum = 0.0;
        for (var i = 0; i < samples; i++)
        {
            _ct.ThrowIfCancellationRequested();
            sum += await ZCZH.ReadValueAsync(unit, _ct);
            await Task.Delay(waitMs, _ct);
        }
        return sum / samples;
    }

    public async Task<double> MeasureVoltageWithConST326Async(string unit, int samples = 3, int waitMs = 200)
    {
        var sum = 0.0;
        for (var i = 0; i < samples; i++)
        {
            _ct.ThrowIfCancellationRequested();
            sum += await ConST326.ReadMeasureValueAsync(unit, _ct);
            await Task.Delay(waitMs, _ct);
        }
        return sum / samples;
    }
}
