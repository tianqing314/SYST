using System.Globalization;
using System.IO.Ports;
using Microsoft.Extensions.Logging;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions.Dut;
using Xmas11.Comm.Core;
using Xmas11.Comm.Data.Common;
using Xmas11.Comm.Devices;

namespace SYST.Devices.Dut.ConST560;

/// <summary>
/// ConST560 手持校验仪（设备族 ConST560）被检**真机驱动**：走 Xmas11 <see cref="E05Base"/> 通讯库。
/// 命令层按旧平台 <c>ConST575_SelfCheck.cs</c> 方法名路由：
/// - 专用命令走 E05Base 强类型方法（Version/SetCheckState/SetCheckFunction 等）
/// - 通用命令走 <see cref="E05Base.ExecuteCommand"/>
/// </summary>
[DutDriver("ConST560")]
public sealed class ConST560Dut : IConST560Dut
{
    private readonly ILogger _logger;
    private readonly CommEndpoint? _comm;
    private E05Base? _dev;

    public string Key { get; }
    public string Model { get; }
    public bool IsConnected { get; private set; }

    private E05Base Dev => _dev ?? throw new DeviceCommException("ConST560 未连接", TestResultStatus.CommunicationError);

    public ConST560Dut(DeviceDescriptor descriptor, ILogger logger)
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
            _logger.LogInformation(IsConnected ? "ConST560 真机连接成功" : "ConST560 连接失败");
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
            throw new DeviceCommException("ConST560 自动连接失败", TestResultStatus.CommunicationError);
    }

    private static E05Base Build(CommEndpoint? ep)
    {
        if (ep is null || ep.Link == LinkType.Serial)
        {
            var sp = ep?.Serial ?? new SerialParams();
            var port = string.IsNullOrWhiteSpace(ep?.PhysicalLink) ? "COM1" : ep!.PhysicalLink!;
            var sb = Enum.TryParse<StopBits>(sp.StopBits, out var s) ? s : StopBits.Two;
            var pa = Enum.TryParse<Parity>(sp.Parity, out var p) ? p : Parity.None;
            return new E05Base(port, sp.Baud, sp.DataBits, sb, pa);
        }
        throw new DeviceCommException($"ConST560 不支持通讯方式: {ep.Link}", TestResultStatus.CommunicationError);
    }

    // ===== IDutDevice =====

    public Task<string> ReadSerialNumberAsync(CancellationToken ct = default)
        => Task.Run(() => { EnsureConnected(); return Result(Dev.GetSerialNumber(), "读取序列号"); }, ct);

    public Task<string> ReadFirmwareVersionAsync(CancellationToken ct = default)
        => Task.Run(() => { EnsureConnected(); return Result(Dev.Version(E05Base.VersionModule.Host), "固件版本"); }, ct);

    public Task WriteInitInfoAsync(string boardType, CancellationToken ct = default) => Task.CompletedTask;

    public Task<double> MeasureAsync(string point, CancellationToken ct = default) => Task.FromResult(0d);

    public Task<bool> SetSerialNumberAsync(string sn, CancellationToken ct = default)
        => Task.Run(() => { EnsureConnected(); return Ok(Dev.SetSerialNumber(sn, E05Base.SerialNumberParam.APP), "设置序列号"); }, ct);

    public Task<bool> SetPrimaryDeviceTypeAsync(string dt, CancellationToken ct = default)
        => Task.Run(() => { EnsureConnected(); return Ok(Dev.SetModel(dt, E05Base.ModelParam.APP), "设置型号"); }, ct);

    public Task<bool> QueryBooleanAsync(string method, object? arg, CancellationToken ct = default)
        => Task.Run(() =>
        {
            if (method is not ("Open" or "Close")) EnsureConnected();
            return method switch
            {
                "Open" => OpenAndReport(),
                "Close" => CloseAndReport(),
                "设置检测状态" => Ok(Dev.SetCheckState(ParseEnum<E05Base.EnableState>(Arg0(arg))), method),
                "设置检测功能" => Ok(Dev.SetCheckFunction(ParseEnum<E05Base.CheckFunction>(Arg0(arg))), method),
                "读取检测结果" => Dev.GetCheckResult().IsCorrect,
                _ => ExecOk(method, arg),
            };
        }, ct);

    public Task<string> QueryTextAsync(string method, object? arg, CancellationToken ct = default)
        => Task.Run(() =>
        {
            if (method is not ("Open" or "Close")) EnsureConnected();
            return method switch
            {
                "读取序列号" => Result(Dev.GetSerialNumber(), method),
                "读取型号" => Result(Dev.GetModel(), method),
                "读取设备版本信息" => Result(Dev.Version(ParseEnum<E05Base.VersionModule>(Arg0(arg))), method),
                "读取HART_DD版本" => Result(Dev.GetHARTDDVersion(), method),
                "读取FF_DD版本" => Result(Dev.GetFFDDVersion(), method),
                "读取PA_DD版本" => Result(Dev.GetPADDVersion(), method),
                "读取检测结果" => FormatCheckResult(Dev.GetCheckResult()),
                "读取诊断信息" => Result(Dev.GetDiagnosisValues(ParseEnum<E05Base.DiagonosisModule>(Arg0(arg)), ParseEnum<E05Base.DiagonosisModule_E_H_FieldbusType>(Arg(arg, 1, "HART"))), method),
                "读取WLAN当前状态" => Result(Dev.GetWLANState(), method),
                "读取WLAN当前所有信息" => Result(Dev.GetWLANInfo(), method),
                "读取蓝牙开关状态" => Result(Dev.GetBlueToothState(), method),
                "读取当前蓝牙名称" => Result(Dev.GetBlueToothName(), method),
                "读取当前蓝牙MAC地址" => Result(Dev.GetBlueToothMAC(), method),
                "获取组件连接状态" => Result(Dev.GetConnectionStatus(ParseEnum<E05Base.ConsultConnectionModule>(Arg0(arg))), method),
                "读取测量值" => Result(Dev.GetOriginalMeasureValue(), method),
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

    // ===== ExecuteCommand 通用入口 =====

    private List<E05Base.IncomingArgumentClass> BuildArgs(object? arg)
    {
        var list = new List<E05Base.IncomingArgumentClass>();
        if (arg is string[] arr)
        {
            foreach (var s in arr)
            {
                var idx = s.IndexOf('=');
                if (idx > 0)
                    list.Add(new E05Base.IncomingArgumentClass { Name = s[..idx].Trim(), Value = s[(idx + 1)..].Trim() });
                else
                    list.Add(new E05Base.IncomingArgumentClass { Name = "PARA", Value = s });
            }
        }
        return list;
    }

    private string ExecText(string cmd, object? arg)
    {
        var inps = BuildArgs(arg);
        var ok = Dev.ExecuteCommand(cmd, inps, out var outps, out var err);
        if (!ok) throw new DeviceCommException($"{cmd}失败: {err}", TestResultStatus.CommunicationError);
        return outps is { Count: > 0 } ? string.Join(" ", outps.Select(o => $"{o.NameChn}:{o.Value}")) : "OK";
    }

    private bool ExecOk(string cmd, object? arg)
    {
        var inps = BuildArgs(arg);
        var ok = Dev.ExecuteCommand(cmd, inps, out var outps, out var err);
        if (!ok) throw new DeviceCommException($"{cmd}失败: {err}", TestResultStatus.CommunicationError);
        return true;
    }

    // ===== Helpers =====

    private bool OpenAndReport() { EnsureConnected(); return IsConnected; }
    private bool CloseAndReport() { try { _dev?.Close(); } catch { } IsConnected = false; return true; }

    private static string Result(iResponse<string> r, string what)
        => r.IsCorrect ? (r.Result ?? "") : throw new DeviceCommException($"{what}失败", TestResultStatus.CommunicationError);

    private static string Result(iResponse<E05Base.VersionData> r, string what)
    {
        if (!r.IsCorrect) throw new DeviceCommException($"{what}失败", TestResultStatus.CommunicationError);
        return $"固件={r.Result?.FirmwareVersion} 硬件={r.Result?.HardwareVersion}";
    }

    private static string Result(iResponse<E05Base.CheckResultData> r, string what)
        => FormatCheckResult(r);

    private static string FormatCheckResult(iResponse<E05Base.CheckResultData> r)
    {
        if (!r.IsCorrect) return "FAIL";
        var d = r.Result;
        return $"BAD:{(int?)d?.ScreenBadPoint} TOUCH:{(int?)d?.ScreenTouch} KEY:{(int?)d?.EntityKey} SPK:{(int?)d?.Speaker} BRI:{(int?)d?.ScreenBrightness}";
    }

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

    private static T ParseEnum<T>(string? s) where T : struct, Enum
        => Enum.TryParse<T>(s, true, out var v) ? v : default;
}
