using Microsoft.Extensions.Logging;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;
using SYST.Devices.Abstractions.Dut;
using SYST.Devices.Dut;

namespace SYST.Devices.ConST660;

/// <summary>
/// ConST660 温度检定炉整机（设备族 ConST660）被检**仿真驱动**（真机开关关时经
/// <see cref="DutDriverRegistry"/> 自动选用本变体）。实现 <see cref="IConST660Dut"/>，
/// 使仿真模式也能完整跑通整机自检全流程（无需真实 ConST660 硬件）。
/// </summary>
[DutDriver("ConST660", IsSimulation = true)]
public sealed class SimulatedConST660 : IConST660Dut
{
    private readonly ILogger _logger;
    private bool _connected;
    private string _serialNumber = "660TH00010001";
    private string _firmwareVersion = "V1.0.0";
    private string _deviceType = "ConST660";
    private bool _wifiEnabled;
    private string? _wifiSsid;
    private DateTime _rtc = DateTime.Now;
    private bool _bluetoothOn;
    private bool _elePowerOn = true;
    private bool _ctlPowerOn = true;

    public SimulatedConST660(DeviceDescriptor descriptor, ILogger logger)
    {
        Key = descriptor.Model;
        Model = descriptor.Model;
        _logger = logger;
    }

    public string Key { get; }
    public string Model { get; }
    public bool IsConnected => _connected;

    /// <summary>仿真连接。</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await Task.Delay(30, ct);
        _connected = true;
        _logger.LogInformation("ConST660 仿真连接成功");
    }

    /// <summary>补充连接（重连）。PORT: 旧 ConST660.ReplenishLink。</summary>
    public Task<bool> ReplenishLinkAsync(CancellationToken ct = default)
    {
        _connected = true;
        return Task.FromResult(true);
    }

    /// <summary>仿真读序列号。</summary>
    public Task<string> ReadSerialNumberAsync(CancellationToken ct = default) => Task.FromResult(_serialNumber);

    /// <summary>仿真读固件版本。</summary>
    public Task<string> ReadFirmwareVersionAsync(CancellationToken ct = default) => Task.FromResult(_firmwareVersion);

    /// <summary>仿真写初始信息（仅记录日志）。</summary>
    public Task WriteInitInfoAsync(string boardType, CancellationToken ct = default)
    {
        _logger.LogInformation("ConST660 写入初始信息：{Type}", boardType);
        return Task.CompletedTask;
    }

    /// <summary>仿真读某测量点。</summary>
    public Task<double> MeasureAsync(string point, CancellationToken ct = default) => Task.FromResult(point switch
    {
        "SystemVoltage" => 28.0,
        "System5V" => 5.0,
        "System12V" => 12.0,
        "System3_3V" => 3.3,
        _ => new Random().NextDouble(),
    });

    /// <summary>设置被检序列号（仿真直接成功）。</summary>
    public Task<bool> SetSerialNumberAsync(string serialNumber, CancellationToken ct = default)
    {
        _serialNumber = serialNumber;
        _logger.LogInformation("ConST660 设置序列号：{SN}", serialNumber);
        return Task.FromResult(true);
    }

    /// <summary>设置产品型号/主设备类型（仿真直接成功）。</summary>
    public Task<bool> SetPrimaryDeviceTypeAsync(string deviceType, CancellationToken ct = default)
    {
        _deviceType = deviceType;
        _logger.LogInformation("ConST660 设置产品型号：{Type}", deviceType);
        return Task.FromResult(true);
    }

    /// <summary>通用布尔查询。</summary>
    public Task<bool> QueryBooleanAsync(string method, object? arg, CancellationToken ct = default) => Task.FromResult(method switch
    {
        "GetEleFunctionState" => _elePowerOn,
        _ => true,
    });

    /// <summary>通用文本查询。</summary>
    public Task<string> QueryTextAsync(string method, object? arg, CancellationToken ct = default) => Task.FromResult(method switch
    {
        "GetDevType" => _deviceType,
        "GetWifiSsid" => _wifiSsid ?? "",
        "GetRtc" => _rtc.ToString("yyyy-MM-dd HH:mm:ss"),
        "GetEleVersion" => _elePowerOn ? "V1.0.0" : "",
        "GetCtlVersion" => _ctlPowerOn ? "V1.0.0" : "",
        "ReadDataFromUSB" => "testData010101-usb",
        "ReadDataFromSD" => "testData010101-sd",
        _ => "OK",
    });

    /// <summary>通用指令执行（仿真维护电测板/控制板电源状态）。</summary>
    public Task CommandAsync(string method, object? arg, CancellationToken ct = default)
    {
        switch (method)
        {
            case "SetElePowerClose": _elePowerOn = false; break;
            case "SetElePowerOpen": _elePowerOn = true; break;
            case "SetCtlPowerClose": _ctlPowerOn = false; break;
            case "SetCtlPowerOpen": _ctlPowerOn = true; break;
        }
        _logger.LogDebug("ConST660 Command: {Method}", method);
        return Task.CompletedTask;
    }

    /// <summary>释放。</summary>
    public ValueTask DisposeAsync()
    {
        _connected = false;
        return ValueTask.CompletedTask;
    }

    // ========== 设备专属方法（旧平台命名，供处理器调用） ==========

    public bool GetSerialNumber(out string code) { code = _serialNumber; return true; }
    public bool GetDevType(out string type) { type = _deviceType; return true; }
    public bool GetVersion(out string version) { version = _firmwareVersion; return true; }
    public bool SoftwareUpgrade(string fileName, out string version)
    {
        _firmwareVersion = Path.GetFileNameWithoutExtension(fileName);
        version = _firmwareVersion;
        return true;
    }
    public bool GetDDLibVersion_P(out string version) { version = "P-V2.0"; return true; }
    public bool GetDDLibVersion_T(out string version) { version = "T-V1.5"; return true; }
    public bool SetScreenSaverTimeToNever() => true;
    public bool SetBadPixelCheckerOpen() => true;
    public bool SetTouchCheckerOpen() => true;
    public bool SetSpeakerCheckerOpen() => true;
    public bool SetCheckerClose() => true;
    public double MeasurePoint(string point) => point switch
    {
        "SystemVoltage" => 28.0,
        "System5V" => 5.0,
        "System12V" => 12.0,
        "System3_3V" => 3.3,
        _ => 0.0,
    };
    public bool GetUSBdriveState(out bool state) { state = true; return true; }
    public bool AddDataToUSB(string file, string value) => true;
    public bool ReadDataFromUSB(string file, out string value) { value = "testData010101-usb"; return true; }
    public bool GetStorageCardState(out bool state) { state = true; return true; }
    public bool AddDataToSD(string file, string value) => true;
    public bool ReadDataFromSD(string file, out string value) { value = "testData010101-sd"; return true; }
    public bool GetSystemDateTime(out DateTime dateTime) { dateTime = _rtc; return true; }
    public bool SetSystemDateTime(DateTime dateTime) { _rtc = dateTime; return true; }
    public bool GetWifiState(out bool state) { state = _wifiEnabled; return true; }
    public bool SetWifiOpen() { _wifiEnabled = true; return true; }
    public bool SetWifiClose() { _wifiEnabled = false; return true; }
    public bool ConnectWifiToHotspot(string ssid, string encryptionMode, string password)
    {
        _wifiEnabled = true;
        _wifiSsid = ssid;
        return true;
    }
    public bool GetBluetoothState(out bool state) { state = _bluetoothOn; return true; }
}