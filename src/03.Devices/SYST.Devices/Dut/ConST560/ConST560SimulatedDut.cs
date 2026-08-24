using Microsoft.Extensions.Logging;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions.Dut;

namespace SYST.Devices.Dut.ConST560;

/// <summary>
/// ConST560 手持校验仪（设备族 ConST560）被检**仿真驱动**（真机开关关时经
/// <see cref="DutDriverRegistry"/> 自动选用本变体）。实现 <see cref="IConST560Dut"/>，
/// 使仿真模式也能完整跑通整机自检全流程（无需真实 ConST560 硬件）。
/// 各指令按名返回让自检项"通过"的标称值。
/// </summary>
[DutDriver("ConST560", IsSimulation = true)]
public sealed class ConST560SimulatedDut : IConST560Dut
{
    /// <summary>日志。</summary>
    private readonly ILogger _logger;

    /// <summary>设备键。</summary>
    public string Key { get; }

    /// <summary>设备型号名。</summary>
    public string Model { get; }

    /// <summary>是否已连接。</summary>
    public bool IsConnected { get; private set; }

    public ConST560SimulatedDut(DeviceDescriptor descriptor, ILogger logger)
    {
        Key = descriptor.Model;
        Model = descriptor.Model;
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await Task.Delay(30, ct);
        IsConnected = true;
        _logger.LogInformation("ConST560 仿真连接成功");
    }

    public Task<bool> ReplenishLinkAsync(CancellationToken ct = default)
    {
        IsConnected = true;
        return Task.FromResult(true);
    }

    public async Task<string> ReadSerialNumberAsync(CancellationToken ct = default)
    {
        await Task.Delay(20, ct);
        return $"{Model}{DateTime.Now:yyMMddHHmmss}";
    }

    public Task<string> ReadFirmwareVersionAsync(CancellationToken ct = default)
        => Task.FromResult("HFC V2.0.0.33");

    public Task WriteInitInfoAsync(string boardType, CancellationToken ct = default)
    {
        _logger.LogInformation("ConST560 写入初始信息：{Type}", boardType);
        return Task.CompletedTask;
    }

    public Task<double> MeasureAsync(string point, CancellationToken ct = default)
        => Task.FromResult(point switch
        {
            "Battery" => 7.2 + new Random().NextDouble() * 0.2,
            _ => new Random().NextDouble(),
        });

    public Task<bool> SetSerialNumberAsync(string serialNumber, CancellationToken ct = default)
    {
        _logger.LogInformation("ConST560 设置序列号：{SN}", serialNumber);
        return Task.FromResult(true);
    }

    public Task<bool> SetPrimaryDeviceTypeAsync(string deviceType, CancellationToken ct = default)
    {
        _logger.LogInformation("ConST560 设置产品型号：{Type}", deviceType);
        return Task.FromResult(true);
    }

    public Task<bool> QueryBooleanAsync(string method, object? arg, CancellationToken ct = default)
    {
        _logger.LogDebug("ConST560 QueryBoolean: {Method}", method);
        return Task.FromResult(method switch
        {
            "设置检测状态" => true,
            "设置检测功能" => true,
            "读取检测结果" => true,
            "设置是否支持模块" => true,
            "设置WLAN当前状态" => true,
            "设置蓝牙开关状态" => true,
            "设置当前系统亮度" => true,
            "设置当前系统音量值" => true,
            "设置电池节能模式信息" => true,
            "设置自动设置时间状态" => true,
            "设置当前日期" => true,
            "设置当前时间" => true,
            "设置序列号" => true,
            "设置型号" => true,
            "设置通道功能" => true,
            "设置输出值" => true,
            "设置检测结果" => true,
            "SetSelfCheck" => true,
            "GetSelfCheck" => true,
            "SetCheckerOpen" => true,
            "SetCheckerClose" => true,
            "SetCheckerSelect" => true,
            "GetCheckerState" => true,
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
            "GetPowerSupplyCheck" => true,
            "GetEnergyCheckStata" => true,
            "GetRS1" => true,
            "GetRS2" => true,
            "GetAtmos" => true,
            "GetAtmosSensor" => true,
            "GetStorageCardState" => true,
            "GetControllerBroadPowerCheckState" => true,
            "GetMotor_Temperature" => true,
            "GetVersion_Controller" => true,
            "GetSystemTime" => true,
            "GetDevSysDate" => true,
            "GetPAMassage" => true,
            _ => true,
        });
    }

    public Task<string> QueryTextAsync(string method, object? arg, CancellationToken ct = default)
    {
        _logger.LogDebug("ConST560 QueryText: {Method}", method);
        return Task.FromResult(method switch
        {
            "读取序列号" => $"{Model}{DateTime.Now:yyMMddHHmmss}",
            "读取型号" => "ConST560",
            "读取设备版本信息" => "HOST V1.0.0",
            "读取HART搜索到的设备列表" => "1",
            "读取FF搜索到的设备列表" => "1",
            "读取蓝牙开关状态" => "1",
            "读取当前蓝牙名称" => "ConST560",
            "读取当前蓝牙MAC地址" => "AA:BB:CC:DD:EE:FF",
            "读取是否支持模块" => "1",
            "读取WLAN当前状态" => "1",
            "读取WLAN当前所有信息" => "Connected RDTEST IP=192.168.1.100",
            "读取诊断信息" => "1:5.05&3:1&4:16.5",
            "获取组件连接状态" => "1",
            "读取过流状态" => "OK",
            "读取过压状态" => "OK",
            "读取测量值" => "12.000",
            "查询电池电量信息" => "65",
            "GetRS1" => "",
            "GetRS2" => "系统版本：V1.0.5\r\n电测版本：V1.0.5\r\n控制版本：APC-V1.0.5\r\n",
            "GetDUTSN" => $"{Model}{DateTime.Now:yyMMddHHmmss}",
            "GetAtmos" => "101.325",
            "GetAtmosSensor" => "101.325",
            "GetStaticETHemetIPAddress" => "192.168.40.109",
            "GetPressureModelOnlineState" => "Open,Open",
            "GetStorageCardState" => "1",
            "GetControllerBroadPowerCheckState" => "0",
            "GetPowerSupplyCheck" => "0",
            "GetMotor_Temperature" => "25.0",
            "GetVersion_Controller" => "APC V1.0.5",
            "GetSystemTime" => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            "GetDevSysDate" => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            "GetCheckerState" => "TestPass",
            "GetCurrentElectricMeasure" => "4.000",
            "GetPAMassage" => "1",
            "GetDiagnosisInfo" => "1:5.05&3:1&4:16.5",
            _ => "OK",
        });
    }

    public Task CommandAsync(string method, object? arg, CancellationToken ct = default)
    {
        _logger.LogDebug("ConST560 Command: {Method}", method);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}
