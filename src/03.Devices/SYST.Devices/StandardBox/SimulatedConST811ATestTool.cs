using Microsoft.Extensions.Logging;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;
using SYST.Devices.Dut;

namespace SYST.Devices.StandardBox;

/// <summary>
/// ConST811A 工装设备仿真驱动（GZP21）。真机开关关时经
/// <see cref="StandardModuleRegistry"/> 自动选用本变体。实现 <see cref="IMachineTestTool"/>，
/// 使仿真模式也能完整跑通整机自检全流程（无需真实 GZP21 工装硬件）。
/// 继电器输出/状态返回成功，电压/电流返回标称值。
/// </summary>
[DutDriver("ConST811ATestTool", IsSimulation = true)]
internal sealed class SimulatedConST811ATestTool : IMachineTestTool
{
    private readonly DeviceDescriptor _descriptor;
    private readonly ILogger _logger;
    private readonly Dictionary<string, bool> _outputStates = new(StringComparer.OrdinalIgnoreCase);
    private bool _connected;

    /// <summary>
    /// 构造函数（自动注册要求：DeviceDescriptor, ILogger）。
    /// </summary>
    public SimulatedConST811ATestTool(DeviceDescriptor descriptor, ILogger logger)
    {
        _descriptor = descriptor;
        _logger = logger;
    }

    #region IStandardModule 基本属性
    public string Key => _descriptor.Name;
    public string Model => _descriptor.Model;
    public bool IsConnected => _connected;
    #endregion

    #region 连接管理
    /// <summary>
    /// 仿真连接：直接标记为已连接。
    /// </summary>
    public Task ConnectAsync(CancellationToken ct = default)
    {
        _connected = true;
        _logger.LogInformation("仿真工装 {Key} 已连接", Key);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 仿真释放。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        _connected = false;
        _logger.LogInformation("仿真工装 {Key} 已断开", Key);
        return ValueTask.CompletedTask;
    }
    #endregion

    #region IMachineTestTool 实现
    /// <summary>
    /// 是否为真实硬件（否，仿真）。
    /// </summary>
    public bool IsRealHardware => false;

    /// <summary>
    /// 仿真设置输出通道状态：记录到内存并返回成功。
    /// </summary>
    public Task<bool> SetOutputAsync(string output, bool open, CancellationToken ct = default)
    {
        _outputStates[output] = open;
        _logger.LogDebug("仿真工装 {Key} 设置输出通道 {Output} 为 {State}", Key, output, open ? "打开" : "关闭");
        return Task.FromResult(true);
    }

    /// <summary>
    /// 仿真获取输出通道状态：返回内存中的记录。
    /// </summary>
    public Task<bool> GetOutputStateAsync(string output, CancellationToken ct = default)
    {
        var state = _outputStates.TryGetValue(output, out var v) && v;
        return Task.FromResult(state);
    }

    /// <summary>
    /// 仿真读取电压：返回标称值 3.3V。
    /// </summary>
    public Task<double> ReadVoltageAsync(int channel = 0, CancellationToken ct = default)
    {
        return Task.FromResult(3.3);
    }

    /// <summary>
    /// 仿真读取电流：返回标称值 0.01A。
    /// </summary>
    public Task<double> ReadCurrentAsync(int channel = 0, CancellationToken ct = default)
    {
        return Task.FromResult(0.01);
    }
    #endregion

    #region IStandardModule 其他方法
    /// <summary>
    /// 仿真读取序列号。
    /// </summary>
    public Task<string> GetSerialNumberAsync(CancellationToken ct = default)
    {
        return Task.FromResult("SIM-GZP21-0001");
    }

    /// <summary>
    /// 仿真读取版本号。
    /// </summary>
    public Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        return Task.FromResult("SIM-1.0.0");
    }

    /// <summary>
    /// 仿真设置压力类型。
    /// </summary>
    public Task<bool> SetPressureTypeAsync(string pressureType, CancellationToken ct = default)
    {
        return Task.FromResult(true);
    }

    /// <summary>
    /// 仿真读取标准压力：返回标称值 101.325 kPa。
    /// </summary>
    public Task<double> GetPressureKpaAsync(CancellationToken ct = default)
    {
        return Task.FromResult(101.325);
    }

    /// <summary>
    /// 仿真读取模块温度：返回标称值 25.0 ℃。
    /// </summary>
    public Task<double> GetTemperatureAsync(CancellationToken ct = default)
    {
        return Task.FromResult(25.0);
    }

    /// <summary>
    /// 仿真复位。
    /// </summary>
    public Task<bool> ResetAsync(CancellationToken ct = default)
    {
        _outputStates.Clear();
        return Task.FromResult(true);
    }
    #endregion
}
