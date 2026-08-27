using SYST.Core.Abstractions;

namespace SYST.Devices.Abstractions;

/// <summary>
/// ConST811A 整机（设备族 ConST811A）被检设备接口。
/// 设备调用统一走 <see cref="IDutDevice"/> 的通用派发入口；本接口仅补充设备族专属的连接类能力。
/// </summary>
public interface IConST811ADut : IDutDevice
{
    /// <summary>
    /// 补充连接（重连）。PORT: 旧 ConST811A.ReplenishLink。
    /// </summary>
    Task<bool> ReplenishLinkAsync(CancellationToken ct = default);
}
