using System.Globalization;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;
using SYST.Devices.Abstractions.Dut;

namespace SYST.TestSteps.ConST560;

/// <summary>
/// ConST560 手持校验仪公共 Ops。封装被检（<see cref="IConST560Dut"/>）与 ZQWL 继电器矩阵指令调用。
/// **自动转换**自旧 <c>ConST560_SelfCheckTest_Y_Task</c> 脚本逻辑；P21 整机测试使用 ZQWL 继电器矩阵控制齿轮箱工装。
/// </summary>
internal sealed class ConST560Ops
{
    private readonly ITestContext _ctx;
    private readonly CancellationToken _ct;

    /// <summary>被检 ConST560 专属驱动。</summary>
    public readonly IConST560Dut Dut;

    /// <summary>ZQWL 继电器矩阵（齿轮箱工装）。</summary>
    public readonly IZQWLRelayMatrix ZQWL;

    public ConST560Ops(ITestContext ctx, CancellationToken ct)
    {
        _ctx = ctx;
        _ct = ct;
        Dut = ctx.GetDevice<IConST560Dut>();
        ZQWL = ctx.GetDevice<IZQWLRelayMatrix>("ZQWL");
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

    /// <summary>真机稳定延时。</summary>
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
    /// 执行设备指令并自动重试。
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

    // ZQWL 继电器矩阵辅助方法

    /// <summary>设置 ZQWL 单路继电器（地址 1-3，通道 1-16）。</summary>
    public Task<bool> SetZQWL(int address, int channel, bool on) => ZQWL.SetChannelAsync(address, channel, on, _ct);

    /// <summary>断开 ZQWL 某地址板全部通道。</summary>
    public Task<bool> CloseAllZQWL(int address) => ZQWL.CloseAllChannelsAsync(address, _ct);

    /// <summary>读 ZQWL 某路状态。</summary>
    public Task<bool> GetZQWLState(int address, int channel) => ZQWL.GetChannelStateAsync(address, channel, _ct);

    /// <summary>
    /// 执行 ZQWL 组合动作：夹爪/探针推杆/TYPE-C 推杆/航插推杆/充电回路等。
    /// 旧脚本使用固定地址+通道组合，这里按动作名封装。
    /// </summary>
    public async Task<bool> ZQWL_Action(string action, bool on)
    {
        // 旧脚本地址/通道映射（按 ConST560 工装实际接线调整）
        // 这里使用占位映射，真实工装需按实际接线表修改
        var (addr, ch) = action switch
        {
            "Jaw" => (1, 1),           // 夹爪
            "ProbePush" => (1, 2),     // 探针推杆
            "TypeCPush" => (1, 3),     // TYPE-C 推杆
            "AviationPlugPush" => (1, 4), // 航插推杆
            "ChargeCircuit" => (1, 5), // 充电回路
            _ => throw new ArgumentException($"未知的 ZQWL 动作: {action}")
        };
        return await SetZQWL(addr, ch, on);
    }
}