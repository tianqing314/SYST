using System.Globalization;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;

namespace SYST.TestSteps.ConST811A;

/// <summary>
/// ConST811A 公共操作基类。封装 BP（气象版）和 MP（表绝压版）共用的逻辑：
/// - GZP21 工装操作
/// - 被检设备操作
/// - 步骤日志输出
/// - 常用辅助方法
/// 
/// DP 和 LLP 变体使用 <see cref="ConST811AOpsWithP06"/>（继承此类，增加 P06 标准模块支持）。
/// </summary>
internal class ConST811AOpsBase
{
    protected readonly ITestContext _ctx;
    protected readonly CancellationToken _ct;

    /// <summary>GZP21 共享工装（继电器输出）。</summary>
    public readonly IMachineTestTool Gzp21;

    /// <summary>被检 ConST811A 专属驱动。</summary>
    public readonly IConST811ADut Dut;

    public ConST811AOpsBase(ITestContext ctx, CancellationToken ct)
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
    {
        var msg = string.IsNullOrEmpty(unit) ? $"  {label} = {F(value)}" : $"  {label} = {F(value)} {unit}";
        Report(msg);
    }

    /// <summary>报告文本信息。</summary>
    public void Text(string label, string? value) => Report($"  {label} = {value ?? "(空)"}");

    /// <summary>报告判定结果。</summary>
    public void Verdict(string name, bool pass)
    {
        if (pass)
            Report($"  {name} = PASS ✓", RealtimeLevel.Success);
        else
            Report($"  {name} = FAIL ✗", RealtimeLevel.Error);
    }

    /// <summary>
    /// 执行设备指令并自动重试（替代旧脚本 goto tryagain + OpenInfoConfirmWindow 模式）。
    /// 失败时仅记录日志，不弹窗。最多重试 maxRetries 次（含首次），每次间隔 1 秒。
    /// </summary>
    /// <param name="action">返回 true=成功。</param>
    /// <param name="desc">操作描述（用于日志）。</param>
    /// <param name="maxRetries">最大尝试次数（含首次）。</param>
    /// <returns>true=某次成功；false=全部失败。</returns>
    public async Task<bool> TryCommand(Func<Task<bool>> action, string desc, int maxRetries = 3)
    {
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            _ct.ThrowIfCancellationRequested();
            if (await action())
            {
                Report($"✓ {desc}");
                return true;
            }
            Fail($"{desc}失败(第{attempt}次)");
            if (attempt < maxRetries) await Task.Delay(1000, _ct);
        }
        return false;
    }

    /// <summary>
    /// 执行设备查询并读取返回值（自动重试）。失败时返回 null。
    /// </summary>
    /// <param name="query">查询动作（返回文本值，null/空=失败）。</param>
    /// <param name="desc">操作描述。</param>
    /// <param name="maxRetries">最大尝试次数。</param>
    /// <returns>成功返回值；失败返回 null。</returns>
    public async Task<string?> TryQueryValue(Func<Task<string>> query, string desc, int maxRetries = 3)
    {
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            _ct.ThrowIfCancellationRequested();
            var val = await query();
            if (!string.IsNullOrWhiteSpace(val))
            {
                Report($"✓ {desc}: {val}");
                return val;
            }
            Fail($"{desc}失败(第{attempt}次)");
            if (attempt < maxRetries) await Task.Delay(1000, _ct);
        }
        return null;
    }

    /// <summary>按名取条件（找不到返回 null）。</summary>
    public ConditionDescriptor? Cond(string name)
    {
        foreach (var c in _ctx.Conditions)
            if (c.Name == name) return c;
        return null;
    }

    /// <summary>对某测量值按指定条件名判定并返回是否通过。</summary>
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

    /// <summary>延迟（子类可重写以支持仿真模式跳过）。</summary>
    public virtual async Task Sleep(int ms)
    {
        await Task.Delay(ms, _ct);
    }

    /// <summary>
    /// 执行遗留测试逻辑（子类可重写以支持 P06 设备）。
    /// </summary>
    public virtual async Task<LegacyTestResult> ExecuteLegacyAsync(string testName, Func<ConST811AOpsBase, Task<LegacyTestResult>> action)
    {
        return await action(this);
    }

    /// <summary>
    /// 获取校准数据（子类可重写以支持 P06 设备）。
    /// </summary>
    public virtual async Task<CalibrationResult> GetCalibrationDataAsync(CalibrationMode mode, string password, int channel, int function, int range)
    {
        // 基类不支持 P06，返回空结果
        await Task.CompletedTask;
        return new CalibrationResult(0, 0, 0, "本机型未配置 P06 标准模块");
    }

    /// <summary>
    /// 读取电压（子类可重写以支持 P06 设备）。
    /// </summary>
    public virtual async Task<double> ReadVolt(int channel)
    {
        await Task.CompletedTask;
        throw new NotSupportedException("本机型未配置 P06 标准模块，无法读取电压");
    }

    /// <summary>
    /// 读取电流（子类可重写以支持 P06 设备）。
    /// </summary>
    public virtual async Task<double> ReadCurrent(int channel)
    {
        await Task.CompletedTask;
        throw new NotSupportedException("本机型未配置 P06 标准模块，无法读取电流");
    }
}

/// <summary>
/// 遗留测试结果。
/// </summary>
internal sealed record LegacyTestResult(bool Pass, string Summary, double? Value = null);

/// <summary>
/// 校准结果。
/// </summary>
internal sealed record CalibrationResult(double RawValue, double FinalValue, double StdValue, string? Message = null);

/// <summary>
/// 校准模式。
/// </summary>
internal enum CalibrationMode
{
    Voltage,
    Current,
    Pressure
}
