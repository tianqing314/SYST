using SYST.Core.Abstractions;

namespace SYST.Devices.Abstractions;

/// <summary>
/// ZQWL 继电器矩阵（GearBox 工装）契约：ConST560 手持校验仪整机测试工装的多路继电器控制板。
/// 旧平台为 <c>ZQWLDevice</c>（Modbus/485），按「地址(1-3) × 通道(1-16)」寻址共 48 路输出，
/// 通过不同通断组合实现工装动作切换（夹爪、探针推杆、TYPE-C 推杆、航插推杆、充电回路等）。
/// 真机驱动后续引入 DLL 实现本接口；当前仿真版按位记录状态即可跑通整机流程。
/// </summary>
public interface IZQWLRelayMatrix : IStandardModule
{
    /// <summary>
    /// 按地址与通道号设置单路继电器输出。
    /// </summary>
    /// <param name="address">板地址（1-3）。</param>
    /// <param name="channel">通道号（1-16）。</param>
    /// <param name="on">true=吸合；false=断开。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>是否成功。</returns>
    Task<bool> SetChannelAsync(int address, int channel, bool on, CancellationToken ct = default);

    /// <summary>
    /// 断开某地址板的全部通道（旧 CloseAllChannelsByAddressKVP 语义）。
    /// </summary>
    /// <param name="address">板地址（1-3）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>是否成功。</returns>
    Task<bool> CloseAllChannelsAsync(int address, CancellationToken ct = default);

    /// <summary>
    /// 读某路继电器当前状态。
    /// </summary>
    /// <param name="address">板地址（1-3）。</param>
    /// <param name="channel">通道号（1-16）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>true=吸合。</returns>
    Task<bool> GetChannelStateAsync(int address, int channel, CancellationToken ct = default);
}