using Microsoft.Extensions.Logging;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions.Test;
using SYST.Devices.Dut;

namespace SYST.Devices.StandardBox.Test;

/// <summary>
/// ConST326 标准信号源仿真桩。
/// 仅用于 E05 ConST560 整机自检接线与场景串行，先按成功回落；
/// 真机 DLL 实现后删除该类即可。
/// </summary>
[DutDriver("ConST326", IsSimulation = true)]
internal sealed class SimulatedConST326 : IConST326
{
    private readonly ILogger _logger;
    private bool _connected;

    public string Key { get; }
    public string Model { get; }
    public bool IsConnected => _connected;

    public SimulatedConST326(DeviceDescriptor descriptor, ILogger logger)
    {
        Key = descriptor.Name;
        Model = descriptor.Model;
        _logger = logger;
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        _connected = true;
        _logger.LogInformation("仿真 ConST326 {Key} 已连接", Key);
        return Task.CompletedTask;
    }

    public Task<string> GetSerialNumberAsync(CancellationToken ct = default)
        => Task.FromResult("CON326-SIM-0001");

    public Task<string> GetVersionAsync(CancellationToken ct = default)
        => Task.FromResult("SIM V1.0");

    public Task<bool> SetPressureTypeAsync(string pressureType, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<double> GetPressureKpaAsync(CancellationToken ct = default)
        => Task.FromResult(0d);

    public Task<double> GetTemperatureAsync(CancellationToken ct = default)
        => Task.FromResult(25d);

    public Task<bool> ResetAsync(CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<bool> SetMeasureModeAsync(string mode, CancellationToken ct = default)
    {
        _logger.LogDebug("仿真 ConST326 SetMeasureMode={Mode}", mode);
        return Task.FromResult(true);
    }

    public Task<bool> SetOutputModeAsync(string mode, CancellationToken ct = default)
    {
        _logger.LogDebug("仿真 ConST326 SetOutputMode={Mode}", mode);
        return Task.FromResult(true);
    }

    public Task<bool> SetOutputAsync(string type, double value, string unit, CancellationToken ct = default)
    {
        _logger.LogDebug("仿真 ConST326 SetOutput {Type}={Value}{Unit}", type, value, unit);
        return Task.FromResult(true);
    }

    public Task<bool> SetPower24VAsync(bool on, CancellationToken ct = default)
    {
        _logger.LogDebug("仿真 ConST326 24V={On}", on);
        return Task.FromResult(true);
    }

    public Task<double> ReadMeasureValueAsync(string unit, CancellationToken ct = default)
    {
        return Task.FromResult(unit switch
        {
            "mA" => 12.00,
            "V" => 1.00,
            _ => 0.0
        });
    }

    public ValueTask DisposeAsync()
    {
        _connected = false;
        return ValueTask.CompletedTask;
    }
}
