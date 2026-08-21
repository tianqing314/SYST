using SYST.Core.Abstractions;

namespace SYST.Devices.Abstractions;

/// <summary>
/// ConST811A 整机（设备族 ConST811A）被检设备接口。**自动转换**自旧 <c>Bots.TestBench.Device.ConST811A</c>
/// （旧平台驱动，内部转调 Xmas11 <c>APC2Device</c>，返回 <c>iResponse</c>）。
/// 设备调用统一走 <see cref="IDutDevice"/> 的通用派发入口（QueryBooleanAsync/QueryTextAsync/CommandAsync，
/// 内部按方法名路由到具体 APC2 调用）；本接口仅补充设备族专属的连接类能力。
/// 通讯/执行失败由真机驱动抛 <see cref="DeviceCommException"/>（由引擎按异常收尾并落盘）。
/// </summary>
public interface IConST811ADut : IDutDevice
{
    /// <summary>
    /// 补充连接（重连）。PORT: 旧 ConST811A.ReplenishLink。
    /// </summary>
    Task<bool> ReplenishLinkAsync(CancellationToken ct = default);
}
