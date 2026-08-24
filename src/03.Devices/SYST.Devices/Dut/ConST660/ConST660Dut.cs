using System.Globalization;
using System.IO.Ports;
using Microsoft.Extensions.Logging;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions.Dut;
using Xmas11.Comm.Core;
using Xmas11.Comm.Data.Common;
using Xmas11.Comm.Devices;
using ATCData = Xmas11.Comm.Devices.ATC.Data;

namespace SYST.Devices.Dut.ConST660;

/// <summary>
/// ConST660 温度检定炉（设备族 ConST660）被检**真机驱动**：走 Xmas11 <see cref="ATCBase"/> 通讯库。
/// 专用命令走 ATCBase 强类型方法，温控等复杂类型参数走 <see cref="ATCBase.ExecuteAnyCommand"/> 通用入口。
/// </summary>
[DutDriver("ConST660")]
public sealed class ConST660Dut : IConST660Dut
{
    private readonly ILogger _logger;
    private readonly CommEndpoint? _comm;
    private ATCBase? _dev;

    public string Key { get; }
    public string Model { get; }
    public bool IsConnected { get; private set; }

    private ATCBase Dev => _dev ?? throw new DeviceCommException("ConST660 未连接", TestResultStatus.CommunicationError);

    public ConST660Dut(DeviceDescriptor descriptor, ILogger logger)
    {
        _logger = logger;
        Key = descriptor.Model;
        Model = descriptor.Model;
        _comm = descriptor.Comm;
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try { _dev?.Close(); } catch { }
            _dev = Build(_comm);
            IsConnected = _dev.Open();
            _logger.LogInformation(IsConnected ? "ConST660 真机连接成功" : "ConST660 连接失败");
        }, ct);
    }

    public Task<bool> ReplenishLinkAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            if (IsConnected && _dev is not null) return true;
            try { _dev?.Close(); } catch { }
            _dev = Build(_comm);
            IsConnected = _dev.Open();
            return IsConnected;
        }, ct);
    }

    private void EnsureConnected()
    {
        if (_dev is not null && IsConnected) return;
        try { _dev?.Close(); } catch { }
        _dev = Build(_comm);
        IsConnected = _dev.Open();
        if (!IsConnected)
            throw new DeviceCommException("ConST660 自动连接失败", TestResultStatus.CommunicationError);
    }

    private static ATCBase Build(CommEndpoint? ep)
    {
        if (ep is null || ep.Link == LinkType.Serial)
        {
            var sp = ep?.Serial ?? new SerialParams();
            var port = string.IsNullOrWhiteSpace(ep?.PhysicalLink) ? "COM1" : ep!.PhysicalLink!;
            var sb = Enum.TryParse<StopBits>(sp.StopBits, out var s) ? s : StopBits.One;
            var pa = Enum.TryParse<Parity>(sp.Parity, out var p) ? p : Parity.None;
            return new ATCBase(port, sp.Baud, sp.DataBits, sb, pa);
        }
        throw new DeviceCommException($"ConST660 不支持通讯方式: {ep.Link}", TestResultStatus.CommunicationError);
    }

    // ===== IDutDevice =====

    public Task<string> ReadSerialNumberAsync(CancellationToken ct = default)
        => Task.Run(() => { EnsureConnected(); return Dev.GetSN() ?? ""; }, ct);

    public Task<string> ReadFirmwareVersionAsync(CancellationToken ct = default)
        => Task.Run(() => { EnsureConnected(); return Result(Dev.GetVersion(), "固件版本"); }, ct);

    public Task WriteInitInfoAsync(string boardType, CancellationToken ct = default) => Task.CompletedTask;

    public Task<double> MeasureAsync(string point, CancellationToken ct = default) => Task.FromResult(0d);

    public Task<bool> SetSerialNumberAsync(string sn, CancellationToken ct = default)
        => Task.Run(() => { EnsureConnected(); return Ok(Dev.SetSerialNumber(sn), "设置序列号"); }, ct);

    public Task<bool> SetPrimaryDeviceTypeAsync(string dt, CancellationToken ct = default)
        => Task.Run(() => { EnsureConnected(); return Ok(Dev.SetDevType(dt), "设置型号"); }, ct);

    public Task<bool> QueryBooleanAsync(string method, object? arg, CancellationToken ct = default)
        => Task.Run(() =>
        {
            if (method is not ("Open" or "Close")) EnsureConnected();
            return method switch
            {
                "Open" => OpenAndReport(),
                "Close" => CloseAndReport(),
                // 自检
                "SetCheckerOpen" => Ok(Dev.SetCheckerOpen(ParseEnum<ATCData.ProgramFunction>(Arg0(arg))), method),
                "SetCheckerClose" => Ok(Dev.SetCheckerClose(), method),
                "SetCheckerSelect" => Ok(Dev.SetCheckerSelect(ParseEnum<ATCData.ProgramFunction>(Arg0(arg))), method),
                // WiFi / BT / 电源（用 OpenCloseState）
                "SetWifiState" => Ok(Dev.SetWifiState(ParseOpenClose(Arg0(arg))), method),
                "ConnectWifiToHotspot" => Ok(Dev.ConnectWifiToHotspot(Arg0(arg) ?? "", Arg(arg, 1, ""), Arg(arg, 2, "")), method),
                "SetBluetoothState" => Ok(Dev.SetBluetoothState(ParseOpenClose(Arg0(arg))), method),
                "SetControllerPower" => Ok(Dev.SetControllerPower(ParseOpenClose(Arg0(arg))), method),
                "SetEleCtricityPower" => Ok(Dev.SetEleCtricityPower(ParseOpenClose(Arg0(arg))), method),
                "SetControllerSoftClose" => Ok(Dev.SetControllerSoftClose(ParseOpenClose(Arg0(arg))), method),
                "SetKeyboardState" => Ok(Dev.SetKeyboardState(ParseOpenClose(Arg0(arg))), method),
                "SetPrompt" => Ok(Dev.SetPrompt(ParseOpenClose(Arg0(arg))), method),
                "SetKeyTone" => Ok(Dev.SetKeyTone(ParseOpenClose(Arg0(arg))), method),
                "SetAntiWindState" => Ok(Dev.SetAntiWindState(ParseOpenClose(Arg0(arg))), method),
                // 功能开关
                "SetFunctionState" => Ok(Dev.SetFunctionState(ParseEnum<ATCData.FunctionType>(Arg0(arg)), ParseBool(Arg(arg, 1))), method),
                // 温控（类型复杂，走通用入口）
                _ => ExecOk(method, arg),
            };
        }, ct);

    public Task<string> QueryTextAsync(string method, object? arg, CancellationToken ct = default)
        => Task.Run(() =>
        {
            if (method is not ("Open" or "Close")) EnsureConnected();
            return method switch
            {
                "读取序列号" => Dev.GetSN() ?? "",
                "读取型号" => Result(Dev.GetDevType(), method),
                "读取固件版本" => Result(Dev.GetVersion(), method),
                "读取版本信息" => Result(Dev.GetHostSoftwareVersionNumberAll(), method),
                "读取电测版本" => Result(Dev.GetVersion_Electricity(), method),
                "读取控制版本" => Result(Dev.GetVersion_Controller(), method),
                "读取DD版本_P" => Result(Dev.GetDDLibVersion_P(), method),
                "读取DD版本_T" => Result(Dev.GetDDLibVersion_T(), method),
                // 自检结果
                "GetCheckerState" => Result(Dev.GetCheckerState(), method),
                // 温控
                "GetCurrentSetTemperature" => Result(Dev.GetCurrentSetTemperature(), method),
                "GetPvValues" => Result(Dev.GetPvValues(), method),
                "GetTemperatureRuningState" => Result(Dev.GetTemperatureRuningState(), method),
                "GetStability" => Result(Dev.GetStability(), method),
                "GetControllingTemperatureStatus" => Result(Dev.GetControllingTemperatureStatus(), method),
                "GetRoomTemp" => Result(Dev.GetRoomTemp(), method),
                // 电压自检
                "GetSystemVoltage" => Result(Dev.GetSystemVoltage(), method),
                "GetSystem5V" => Result(Dev.GetSystem5V(), method),
                "GetSystem12V" => Result(Dev.GetSystem12V(), method),
                "GetSystem3_3V" => Result(Dev.GetSystem3_3V(), method),
                // 电源状态
                "GetElectricalPowerSupplyState" => Result(Dev.GetElectricalPowerSupplyState(), method),
                // WiFi / BT
                "GetWifiState" => Result(Dev.GetWifiState(), method),
                "GetBluetoothState" => Result(Dev.GetBluetoothState(), method),
                // 存储
                "USBdriveState" => Result(Dev.USBdriveState(), method),
                "StorageCardState" => Result(Dev.StorageCardState(), method),
                // 通用
                _ => ExecText(method, arg),
            };
        }, ct);

    public Task CommandAsync(string method, object? arg, CancellationToken ct = default)
        => Task.Run(() =>
        {
            if (method is not ("Open" or "Close" or "SetCommConfigEmpty")) EnsureConnected();
            switch (method)
            {
                case "Open": OpenAndReport(); return;
                case "Close": CloseAndReport(); return;
                case "SetCommConfigEmpty": return;
                default: ExecOk(method, arg); return;
            }
        }, ct);

    public ValueTask DisposeAsync()
    {
        try { _dev?.Close(); } catch { }
        _dev = null;
        IsConnected = false;
        return ValueTask.CompletedTask;
    }

    // ===== ExecuteAnyCommand 通用入口 =====

    private string ExecText(string cmd, object? arg)
    {
        var full = arg is string[] arr ? cmd + " " + string.Join(" ", arr) : cmd;
        var r = Dev.ExecuteAnyCommand(full);
        if (!r.IsCorrect) throw new DeviceCommException($"{cmd}失败", TestResultStatus.CommunicationError);
        return r.Result ?? "OK";
    }

    private bool ExecOk(string cmd, object? arg)
    {
        var full = arg is string[] arr ? cmd + " " + string.Join(" ", arr) : cmd;
        var r = Dev.ExecuteAnyCommand(full);
        if (!r.IsCorrect) throw new DeviceCommException($"{cmd}失败", TestResultStatus.CommunicationError);
        return true;
    }

    // ===== Helpers =====

    private bool OpenAndReport() { EnsureConnected(); return IsConnected; }
    private bool CloseAndReport() { try { _dev?.Close(); } catch { } IsConnected = false; return true; }

    private static string Result(iResponse<string> r, string what)
        => r.IsCorrect ? (r.Result ?? "") : throw new DeviceCommException($"{what}失败", TestResultStatus.CommunicationError);

    private static string Result(iResponse r, string what)
        => r.IsCorrect ? "1" : throw new DeviceCommException($"{what}失败", TestResultStatus.CommunicationError);

    private static bool Ok(iResponse r, string what)
    {
        if (!r.IsCorrect) throw new DeviceCommException($"{what}失败", TestResultStatus.CommunicationError);
        return true;
    }

    private static string? Arg0(object? arg) => arg switch
    {
        string[] arr when arr.Length > 0 => arr[0],
        string s => s,
        _ => null,
    };

    private static string Arg(object? arg, int index, string fallback = "")
    {
        if (arg is string[] arr && arr.Length > index) return arr[index];
        return fallback;
    }

    private static bool ParseBool(string? s) => s == "1" || s?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

    private static OpenCloseState ParseOpenClose(string? s)
        => s == "1" || s?.Equals("open", StringComparison.OrdinalIgnoreCase) == true
            ? OpenCloseState.Open : OpenCloseState.Close;

    private static T ParseEnum<T>(string? s) where T : struct, Enum
        => Enum.TryParse<T>(s, true, out var v) ? v : default;
}
