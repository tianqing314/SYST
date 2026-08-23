using SYST.Core.Abstractions;

namespace SYST.Devices.Abstractions.Dut;

/// <summary>
/// ConST560 手持校验仪（设备族 ConST560）被检设备接口。
/// 设备调用统一走 <see cref="IDutDevice"/> 的通用派发入口（QueryBooleanAsync/QueryTextAsync/CommandAsync，
/// 内部按方法名路由到具体指令）；本接口仅补充设备族专属的连接类能力。
/// 通讯/执行失败由真机驱动抛 <see cref="DeviceCommException"/>（由引擎按异常收尾并落盘）。
/// </summary>
public interface IConST560Dut : IDutDevice
{
    /// <summary>
    /// 补充连接（重连）。
    /// </summary>
    Task<bool> ReplenishLinkAsync(CancellationToken ct = default);
}