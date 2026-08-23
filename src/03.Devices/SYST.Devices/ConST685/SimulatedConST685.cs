using Microsoft.Extensions.Logging;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;
using SYST.Devices.Abstractions.Dut;
using SYST.Devices.Dut;

namespace SYST.Devices.ConST685;

/// <summary>
/// ConST685 过程校验仪整机（设备族 ConST685）被检**仿真驱动**（真机开关关时经
/// <see cref="DutDriverRegistry"/> 自动选用本变体）。实现 <see cref="IConST685Dut"/>，
/// 使仿真模式也能完整跑通整机自检全流程（无需真实 ConST685 硬件）。
/// </summary>
[DutDriver("ConST685", IsSimulation = true)]
public sealed class SimulatedConST685 : IConST685Dut
{
    private readonly ILogger _logger;
    private bool _connected;
    private string _serialNumber = "685000010001";
    private string _firmwareVersion = "V1.0.0";
    private string _deviceType = "ConST685";
    private bool _wifiEnabled;
    private string? _wifiSsid;
    private DateTime _rtc = DateTime.Now;
    private double _ref1Resistance = 0.0;
    private double _ref2Resistance = 0.0;
    private double _ref1Voltage = 0.0;
    private double _ref2Voltage = 0.0;

    public SimulatedConST685(DeviceDescriptor descriptor, ILogger logger)
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
        _logger.LogInformation("ConST685 仿真连接成功");
    }

    /// <summary>补充连接（重连）。PORT: 旧 ConST685.ReplenishLink。</summary>
    public Task<bool> ReplenishLinkAsync(CancellationToken ct = default) => Task.FromResult(true);

    /// <summary>仿真读序列号。</summary>
    public Task<string> ReadSerialNumberAsync(CancellationToken ct = default) => Task.FromResult(_serialNumber);

    /// <summary>仿真读固件版本。</summary>
    public Task<string> ReadFirmwareVersionAsync(CancellationToken ct = default) => Task.FromResult(_firmwareVersion);

    /// <summary>仿真写初始信息（仅记录日志）。</summary>
    public Task WriteInitInfoAsync(string boardType, CancellationToken ct = default)
    {
        _logger.LogInformation("ConST685 写入初始信息：{Type}", boardType);
        return Task.CompletedTask;
    }

    /// <summary>仿真读某测量点。</summary>
    public Task<double> MeasureAsync(string point, CancellationToken ct = default) => Task.FromResult(new Random().NextDouble());

    /// <summary>设置被检序列号（仿真直接成功）。</summary>
    public Task<bool> SetSerialNumberAsync(string serialNumber, CancellationToken ct = default)
    {
        _serialNumber = serialNumber;
        _logger.LogInformation("ConST685 设置序列号：{SN}", serialNumber);
        return Task.FromResult(true);
    }

    /// <summary>设置产品型号/主设备类型（仿真直接成功）。</summary>
    public Task<bool> SetPrimaryDeviceTypeAsync(string deviceType, CancellationToken ct = default)
    {
        _deviceType = deviceType;
        _logger.LogInformation("ConST685 设置产品型号：{Type}", deviceType);
        return Task.FromResult(true);
    }

    /// <summary>通用布尔查询。</summary>
    public Task<bool> QueryBooleanAsync(string method, object? arg, CancellationToken ct = default) => Task.FromResult(true);

    /// <summary>通用文本查询。</summary>
    public Task<string> QueryTextAsync(string method, object? arg, CancellationToken ct = default) => Task.FromResult(method switch
    {
        "GetDevType" => _deviceType,
        "GetWifiSsid" => _wifiSsid ?? "",
        "GetRtc" => _rtc.ToString("yyyy-MM-dd HH:mm:ss"),
        "ReadDataFromUSB" => "testData010101-usb",
        "ReadDataFromSD" => "testData010101-sd",
        _ => "OK",
    });

    /// <summary>通用指令执行（仿真仅记录日志）。</summary>
    public Task CommandAsync(string method, object? arg, CancellationToken ct = default)
    {
        _logger.LogDebug("ConST685 Command: {Method}", method);
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
    public bool SetBadPixelCheckerOpen() => true;
    public bool SetTouchCheckerOpen() => true;
    public bool SetSpeakerCheckerOpen() => true;
    public bool SetCheckerClose() => true;
    public double MeasurePoint(string point) => new Random().NextDouble();

    // REF1/REF2 零点测试
    public bool TestDeviceChanelZero(out double ref1Resistance, out double ref2Resistance, out double ref1Voltage, out double ref2Voltage)
    {
        ref1Resistance = _ref1Resistance;
        ref2Resistance = _ref2Resistance;
        ref1Voltage = _ref1Voltage;
        ref2Voltage = _ref2Voltage;
        return true;
    }
    public bool SetRef1Resistance(double value) { _ref1Resistance = value; return true; }
    public bool SetRef2Resistance(double value) { _ref2Resistance = value; return true; }
    public bool SetRef1Voltage(double value) { _ref1Voltage = value; return true; }
    public bool SetRef2Voltage(double value) { _ref2Voltage = value; return true; }

    // 接线盒测试
    public bool TestConnectorsInternal(out string info) { info = "内嵌接线盒连接正常"; return true; }
    public bool TestConnectorsExternal(out string info) { info = "外接接线盒连接正常"; return true; }

    // LOGO/启动设置
    public bool TestSetStartLogo() => true;

    // 外部电阻关闭
    public bool TestCloseExternalResistance() => true;

    // WiFi
    public bool GetWifiState(out bool state) { state = _wifiEnabled; return true; }
    public bool SetWifiOpen() { _wifiEnabled = true; return true; }
    public bool SetWifiClose() { _wifiEnabled = false; return true; }
    public bool ConnectWifiToHotspot(string ssid, string encryptionMode, string password)
    {
        _wifiEnabled = true;
        _wifiSsid = ssid;
        return true;
    }
    public bool GetBluetoothState(out bool state) { state = false; return true; }

    // RTC
    public bool GetSystemDateTime(out DateTime dateTime) { dateTime = _rtc; return true; }
    public bool SetSystemDateTime(DateTime dateTime) { _rtc = dateTime; return true; }
    public string? GetRtc() => _rtc.ToString("yyyy-MM-dd HH:mm:ss");
}