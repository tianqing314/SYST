using Microsoft.Extensions.Logging;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;

namespace SYST.Devices.Dut.ConST811A;

/// <summary>
/// ConST811A 整机（设备族 ConST811A）被检**仿真驱动**（真机开关关时经
/// <see cref="DutDriverRegistry"/> 自动选用本变体）。实现 <see cref="IConST811ADut"/>，
/// 使仿真模式也能完整跑通整机自检全流程（无需真实 ConST811A 硬件）。
/// 各指令按名返回让自检项"通过"的标称值：通用布尔查询返回 true，文本查询按方法名返回合理文本。
/// </summary>
[DutDriver("ConST811A", IsSimulation = true)]
public sealed class ConST811ASimulatedDut : IConST811ADut
{
    /// <summary>日志。</summary>
    private readonly ILogger _logger;

    /// <summary>仿真随机源。</summary>
    private readonly Random _rng = new();

    /// <summary>设备键。</summary>
    public string Key { get; }

    /// <summary>设备型号名。</summary>
    public string Model { get; }

    /// <summary>是否已连接。</summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// 用设备描述符构造仿真被检。
    /// </summary>
    /// <param name="descriptor">设备描述符。</param>
    /// <param name="logger">日志。</param>
    public ConST811ASimulatedDut(DeviceDescriptor descriptor, ILogger logger)
    {
        Key = descriptor.Model;
        Model = descriptor.Model;
        _logger = logger;
    }

    /// <summary>仿真连接（延时后置连接标志）。</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await Task.Delay(30, ct);
        IsConnected = true;
        _logger.LogInformation("ConST811A 仿真连接成功");
    }

    /// <summary>补充连接（仿真直接成功）。</summary>
    public Task<bool> ReplenishLinkAsync(CancellationToken ct = default)
    {
        IsConnected = true;
        return Task.FromResult(true);
    }

    /// <summary>仿真读序列号（型号 + 时间戳）。</summary>
    public async Task<string> ReadSerialNumberAsync(CancellationToken ct = default)
    {
        await Task.Delay(20, ct);
        return $"{Model}{DateTime.Now:yyMMddHHmmss}";
    }

    /// <summary>仿真读固件版本。</summary>
    public Task<string> ReadFirmwareVersionAsync(CancellationToken ct = default)
        => Task.FromResult("V1.0.5");

    /// <summary>仿真写初始信息（仅记录日志）。</summary>
    public Task WriteInitInfoAsync(string boardType, CancellationToken ct = default)
    {
        _logger.LogInformation("ConST811A 写入初始信息：{Type}", boardType);
        return Task.CompletedTask;
    }

    /// <summary>仿真读某测量点。</summary>
    public Task<double> MeasureAsync(string point, CancellationToken ct = default)
        => Task.FromResult(point switch
        {
            "Battery" => 3.6 + _rng.NextDouble() * 0.1,
            _ => _rng.NextDouble(),
        });

    /// <summary>设置被检序列号（仿真直接成功）。</summary>
    public Task<bool> SetSerialNumberAsync(string serialNumber, CancellationToken ct = default)
    {
        _logger.LogInformation("ConST811A 设置序列号：{SN}", serialNumber);
        return Task.FromResult(true);
    }

    /// <summary>设置产品型号/主设备类型（仿真直接成功）。</summary>
    public Task<bool> SetPrimaryDeviceTypeAsync(string deviceType, CancellationToken ct = default)
    {
        _logger.LogInformation("ConST811A 设置产品型号：{Type}", deviceType);
        return Task.FromResult(true);
    }

    /// <summary>
    /// 通用布尔查询（仿真按方法名返回让自检通过的标称结果）。
    /// </summary>
    public Task<bool> QueryBooleanAsync(string method, object? arg, CancellationToken ct = default)
    {
        _logger.LogDebug("ConST811A QueryBoolean: {Method}", method);
        return Task.FromResult(method switch
        {
            // 自检相关：默认均视为启动/读取成功
            "SetSelfCheck" => true,
            "GetSelfCheck" => true,
            "SetCheckerOpen" => true,
            "SetCheckerClose" => true,
            "SetCheckerSelect" => true,
            "GetCheckerState" => true,
            // 电源/电池
            "GetPowerSupplyCheck" => true,
            "GetEnergyCheckStata" => true,
            "SetElectricSourceFunction" => true,
            "SetElectricSourceTarget" => true,
            "SetEleChannelItem_VOL" => true,
            "SetEleChannelItem_CURR" => true,
            "SetEleChannelItem_PA" => true,
            "SetEleChannelItem_HART" => true,
            "SetEleChannelItem_HARTClose" => true,
            "GetCurrentElectricMeasure" => true,
            "SetPressureUnit_IPM" => true,
            "SetVentMode" => true,
            "SetTestMode" => true,
            "SetTargetPressure" => true,
            "SetPressureStability" => true,
            "SetControlPressureModel" => true,
            "GetPressureStableState" => true,
            "SetOpenMaxControlPressureSpeed" => true,
            "SetModuleStableEnable" => true,
            "TestPositivePump" => true,
            "TestNegativePump" => true,
            "TestPumpStop" => true,
            "GetPumpTestState" => true,
            "SetSystemTime" => true,
            "SetSystemDate" => true,
            "SetReboot" => true,
            "SelfTuning" => true,
            "StopSelfTuning" => true,
            "GetSelfTuningState" => true,
            "CalibrationSensor" => true,
            "StopCalibrationSensor" => true,
            "GetCalibrationSensorState" => true,
            "SetCalibrationSensorDate" => true,
            "SetCalibrationAutoDate" => true,
            "GetBlueToothState" => true,
            "CloseBlueTooth" => true,
            "SetWifiClose" => true,
            "SetFANOn" => true,
            "SetFANClose" => true,
            "SetBrightness" => true,
            "SetValveStata" => true,
            "SearchPA" => true,
            "ConnectPA" => true,
            "StartSearchHart" => true,
            "StopSearchHart" => true,
            "ConnectHart" => true,
            "SetSwitchMode_IPIR" => true,
            "Open" => true,
            "Close" => true,
            "IsDoubleRange" => true,
            _ => true,
        });
    }

    /// <summary>
    /// 通用文本查询（仿真按方法名返回合理文本）。
    /// </summary>
    public Task<string> QueryTextAsync(string method, object? arg, CancellationToken ct = default)
    {
        _logger.LogDebug("ConST811A QueryText: {Method}", method);
        return Task.FromResult(method switch
        {
            "GetRS1" => "",                       // 无警告消息 = 通过
            "GetRS2" => "系统版本：V1.0.5\r\n电测版本：V1.0.5\r\n控制版本：APC-BP V1.0.5\r\n",
            "GetDUTSN" => $"{Model}{DateTime.Now:yyMMddHHmmss}",
            "GetAtmos" => "101.325",
            "GetAtmosSensor" => "101.325",
            "GetStaticETHemetIPAddress" => "192.168.40.107",
            "GetPressureModelOnlineState" => "Open,Open",
            "GetStorageCardState" => "1",
            "GetControllerBroadPowerCheckState" => "OK",
            "GetElectricalBroadPowerCheckState" => "OK",
            "GetPowerSupplyCheck" => "0",
            "GetMotor_Temperature" => "25.0",
            "GetDevType" => Model,
            "GetVersion_Controller" => "APC-BP V1.0.5",
            "GetVersion_OS" => "OS V1.0.0.1",
            "GetSystemTime" => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            "GetDevSysDate" => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            "GetCheckerState" => "TestPass",
            "GetCurrentElectricMeasure" => "4.000",
            "GetPAMassage" => "1",
            _ => "0",
        });
    }

    /// <summary>
    /// 通用指令执行（仿真仅记录日志）。
    /// </summary>
    public Task CommandAsync(string method, object? arg, CancellationToken ct = default)
    {
        _logger.LogDebug("ConST811A Command: {Method}", method);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 释放（置未连接）。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}
