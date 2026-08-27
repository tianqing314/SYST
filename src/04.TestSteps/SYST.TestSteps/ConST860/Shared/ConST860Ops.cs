using System.Globalization;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;
using SYST.Devices.Abstractions.Dut;

namespace SYST.TestSteps.ConST860;

/// <summary>
/// ConST860 液压整机公共 Ops。封装被检（<see cref="IConST860Dut"/>）指令调用与日志/判定辅助。
/// P25 整机测试不依赖额外标准模块。
/// </summary>
internal sealed class ConST860Ops
{
    private readonly ITestContext _ctx;
    private readonly CancellationToken _ct;

    /// <summary>被检 ConST860 基础驱动。</summary>
    public readonly IConST860Dut Dut;

    /// <summary>被检 ConST860 气压扩展驱动（Q 变体；仿真/清单配置了 Q 能力时非 null）。</summary>
    public readonly IConST860PressureQBase? DutQ;

    /// <summary>被检 ConST860 液压扩展驱动（Y 变体；仿真/清单配置了 Y 能力时非 null）。</summary>
    public readonly IConST860PressureYGbk? DutY;

    /// <summary>电测工装（可选；仿真清单未配置时为 null，相关调用按跳过处理）。</summary>
    public readonly IMachineTestTool? Tool;

    public ConST860Ops(ITestContext ctx, CancellationToken ct)
    {
        _ctx = ctx;
        _ct = ct;
        Dut = ctx.GetDevice<IConST860Dut>();
        // 扩展接口按能力探测：仿真驱动同时实现两套，真机驱动只实现对应变体的那套
        DutQ = Dut as IConST860PressureQBase;
        DutY = Dut as IConST860PressureYGbk;
        // 电测工装（ZQWL_AIRead）为可选设备：清单未配置时 GetDevice 抛错，这里容错处理
        try
        {
            Tool = ctx.GetDevice<IMachineTestTool>("ZQWL");
        }
        catch
        {
            Tool = null;
        }
    }

    /// <summary>数值格式化（保留三位小数）。</summary>
    public static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>推送实时消息。</summary>
    public void Report(string m, RealtimeLevel l = RealtimeLevel.Info) => _ctx.Report(m, l);

    /// <summary>步骤成功：报告操作完成（✓ 标记）。</summary>
    public void Ok(string desc) => Report($"✓ {desc}", RealtimeLevel.Success);

    /// <summary>步骤失败：报告操作失败（✗ 标记）。</summary>
    public void Fail(string desc) => Report($"✗ {desc}", RealtimeLevel.Error);

    /// <summary>报告读取到的值。</summary>
    public void Value(string label, double value, string unit = "")
        => Report(unit.Length == 0 ? $"  {label}: {F(value)}" : $"  {label}: {F(value)}{unit}");

    /// <summary>报告读取到的文本值。</summary>
    public void Text(string label, string? value) => Report($"  {label}: {value ?? "(空)"}");

    /// <summary>真机稳定延时。PORT: 旧 Thread.Sleep / ScriptHelper.Thread_Sleep。</summary>
    public Task Sleep(int ms)
    {
        Report($"  等待稳定 {ms}ms ...");
        return Task.Delay(ms, _ct);
    }

    /// <summary>带原因的等待。</summary>
    public Task Sleep(int ms, string reason)
    {
        Report($"  {reason}，等待 {ms}ms ...");
        return Task.Delay(ms, _ct);
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

    /// <summary>
    /// 执行设备指令并自动重试（替代旧脚本 goto tryagain + OpenInfoConfirmWindow 模式）。
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
