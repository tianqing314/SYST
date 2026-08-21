using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;

namespace SYST.TestSteps.ConST811A;

/// <summary>
/// ConST811A 带 P06 标准模块的操作类。DP（差压）和 LLP（微差压）变体使用此类：
/// - 继承 <see cref="ConST811AOpsBase"/> 的所有功能
/// - 增加 P06 标准模块支持（电压/电流采样）
/// - 重写 Sleep 方法以支持仿真模式跳过
/// </summary>
internal class ConST811AOpsWithP06 : ConST811AOpsBase
{
    /// <summary>P06 标准模块（电压/电流采样）。</summary>
    public readonly IMachineTestTool P06;

    public ConST811AOpsWithP06(ITestContext ctx, CancellationToken ct) : base(ctx, ct)
    {
        P06 = ctx.GetDevice<IMachineTestTool>("P06");
    }

    /// <summary>延迟（仿真模式下跳过）。</summary>
    public override async Task Sleep(int ms)
    {
        if (P06.IsRealHardware)
        {
            await Task.Delay(ms, _ct);
        }
    }

    /// <summary>读取标准模块电压。</summary>
    public override async Task<double> ReadVolt(int channel)
    {
        return await P06.ReadVoltageAsync(channel, _ct);
    }

    /// <summary>读取标准模块电流。</summary>
    public override async Task<double> ReadCurrent(int channel)
    {
        return await P06.ReadCurrentAsync(channel, _ct);
    }
}
