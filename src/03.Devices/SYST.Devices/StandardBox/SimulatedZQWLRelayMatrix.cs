using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;
using SYST.Devices.Dut;

namespace SYST.Devices.StandardBox;

/// <summary>
/// ZQWL 继电器矩阵（GearBox 工装）**仿真驱动**。实现 <see cref="IZQWLRelayMatrix"/>，
/// 按「地址 × 通道」位图记录输出状态，供 ConST560 整机测试在无硬件环境下完整跑通。
/// 真机驱动（Modbus/485 DLL）后续实现同接口即可无缝替换。
/// </summary>
[DutDriver("ZQWLRelayMatrix", IsSimulation = true)]
internal sealed class SimulatedZQWLRelayMatrix : IZQWLRelayMatrix
{
    /// <summary>日志。</summary>
    private readonly ILogger _logger;

    /// <summary>继电器状态位图：(地址, 通道) → 是否吸合。</summary>
    private readonly ConcurrentDictionary<(int Address, int Channel), bool> _states = new();

    /// <summary>设备描述符。</summary>
    private readonly DeviceDescriptor _descriptor;

    /// <summary>是否已连接。</summary>
    private bool _connected;

    /// <summary>构造仿真 ZQWL 继电器矩阵。</summary>
    /// <param name="descriptor">设备描述符。</param>
    /// <param name="logger">日志。</param>
    public SimulatedZQWLRelayMatrix(DeviceDescriptor descriptor, ILogger logger)
    {
        _descriptor = descriptor;
        _logger = logger;
    }

    /// <summary>设备键。</summary>
    public string Key => _descriptor.Name;

    /// <summary>设备型号名。</summary>
    public string Model => _descriptor.Model;

    /// <summary>是否已连接。</summary>
    public bool IsConnected => _connected;

    /// <summary>仿真连接。</summary>
    public Task ConnectAsync(CancellationToken ct = default)
    {
        _connected = true;
        _logger.LogInformation("仿真 ZQWL 继电器矩阵 {Key} 已连接", Key);
        return Task.CompletedTask;
    }

    /// <summary>读序列号（仿真返回固定值）。</summary>
    public Task<string> GetSerialNumberAsync(CancellationToken ct = default)
        => Task.FromResult("ZQWL-SIM-0001");

    /// <summary>读版本号（仿真返回固定值）。</summary>
    public Task<string> GetVersionAsync(CancellationToken ct = default)
        => Task.FromResult("SIM V1.0");

    /// <summary>设置压力类型（继电器矩阵不支持，直接成功）。</summary>
    public Task<bool> SetPressureTypeAsync(string pressureType, CancellationToken ct = default)
        => Task.FromResult(true);

    /// <summary>读标准压力（继电器矩阵不支持，返回 0）。</summary>
    public Task<double> GetPressureKpaAsync(CancellationToken ct = default)
        => Task.FromResult(0d);

    /// <summary>读模块温度（仿真返回室温）。</summary>
    public Task<double> GetTemperatureAsync(CancellationToken ct = default)
        => Task.FromResult(25d);

    /// <summary>复位（清空全部状态位图）。</summary>
    public Task<bool> ResetAsync(CancellationToken ct = default)
    {
        _states.Clear();
        _logger.LogInformation("仿真 ZQWL 继电器矩阵已复位");
        return Task.FromResult(true);
    }

    /// <summary>设置单路继电器输出（记录到状态位图）。</summary>
    public Task<bool> SetChannelAsync(int address, int channel, bool on, CancellationToken ct = default)
    {
        ValidateRange(address, channel);
        _states[(address, channel)] = on;
        _logger.LogDebug("仿真 ZQWL 设置 地址={Address} 通道={Channel} → {State}", address, channel, on ? "吸合" : "断开");
        return Task.FromResult(true);
    }

    /// <summary>断开某地址板的全部通道。</summary>
    public async Task<bool> CloseAllChannelsAsync(int address, CancellationToken ct = default)
    {
        if (address is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(address), address, "板地址范围 1-3");
        }

        for (var ch = 1; ch <= 16; ch++)
        {
            await SetChannelAsync(address, ch, false, ct);
        }

        return true;
    }

    /// <summary>读某路继电器当前状态。</summary>
    public Task<bool> GetChannelStateAsync(int address, int channel, CancellationToken ct = default)
    {
        ValidateRange(address, channel);
        return Task.FromResult(_states.TryGetValue((address, channel), out var on) && on);
    }

    /// <summary>校验地址/通道范围。</summary>
    private static void ValidateRange(int address, int channel)
    {
        if (address is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(address), address, "板地址范围 1-3");
        }

        if (channel is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "通道号范围 1-16");
        }
    }

    /// <summary>释放。</summary>
    public ValueTask DisposeAsync()
    {
        _connected = false;
        return ValueTask.CompletedTask;
    }
}