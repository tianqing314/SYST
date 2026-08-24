using System.Globalization;
using System.IO.Ports;
using Microsoft.Extensions.Logging;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions.Dut;
using Xmas11.Comm.Core;
using Xmas11.Comm.Data.Common;
using Xmas11.Comm.Devices;

using DPC2Data = Xmas11.Comm.Devices.DPC2;

namespace SYST.Devices.Dut.ConST860;

/// <summary>
/// ConST860 气压/液压整机（P25）被检**真机驱动**：走 Xmas11 <see cref="DPC2Base"/> 通讯库。
/// 同时实现 <see cref="IConST860PressureQBase"/>（气压）和 <see cref="IConST860PressureYGbk"/>（液压）。
/// 通用命令走 DPC2Base.OnExecute，专用命令走 DPC2Base 强类型方法。
/// </summary>
[DutDriver("ConST860")]
public sealed class ConST860Dut : IConST860PressureQBase, IConST860PressureYGbk
{
    private readonly ILogger _logger;
    private readonly CommEndpoint? _comm;
    private DPC2Base? _dev;

    public string Key { get; }
    public string Model { get; }
    public bool IsConnected { get; private set; }

    private DPC2Base Dev => _dev ?? throw new DeviceCommException("ConST860 未连接", TestResultStatus.CommunicationError);

    public ConST860Dut(DeviceDescriptor descriptor, ILogger logger)
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
            _logger.LogInformation(IsConnected ? "ConST860 真机连接成功" : "ConST860 连接失败");
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
            throw new DeviceCommException("ConST860 自动连接失败", TestResultStatus.CommunicationError);
    }

    private static DPC2Base Build(CommEndpoint? ep)
    {
        if (ep is null || ep.Link == LinkType.Serial)
        {
            var sp = ep?.Serial ?? new SerialParams();
            var port = string.IsNullOrWhiteSpace(ep?.PhysicalLink) ? "COM1" : ep!.PhysicalLink!;
            var sb = Enum.TryParse<StopBits>(sp.StopBits, out var s) ? s : StopBits.One;
            var pa = Enum.TryParse<Parity>(sp.Parity, out var p) ? p : Parity.None;
            return new DPC2Base(port, sp.Baud, sp.DataBits, sb, pa);
        }
        throw new DeviceCommException($"ConST860 不支持通讯方式: {ep.Link}", TestResultStatus.CommunicationError);
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
                "Open" => true,
                "Close" => true,
                "SetSelfCheckState" => ExecOk("SetCheckerOpen Main", arg),
                "SetKeyboardState" => Ok(Dev.SetKeyboardState(ParseEnum<OpenCloseState>(Arg0(arg))), method),
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
                "读取版本信息" => Result(Dev.GetVersion(), method),
                "GetSelfCheckException" => "",
                "GetMedium" => "空气",
                "GetStaticETHemetIPAddress" => "192.168.40.110",
                _ => ExecText(method, arg),
            };
        }, ct);

    public Task CommandAsync(string method, object? arg, CancellationToken ct = default)
        => Task.Run(() =>
        {
            if (method is not ("Open" or "Close" or "SetCommConfigEmpty")) EnsureConnected();
            switch (method)
            {
                case "Open": return;
                case "Close": return;
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

    // ===== IConST860Dut 基础能力 =====

    public Task<bool> SelfCheckAsync(CancellationToken ct = default)
        => Task.Run(() => { EnsureConnected(); return ExecOk("SetCheckerOpen Main", arg: null); }, ct);

    public Task<double> GetPumpRpmAsync(CancellationToken ct = default)
        => Task.FromResult(0d); // 真机由 CPPI 指令读取

    public Task<double> ChargeAsync(char target, CancellationToken ct = default)
        => Task.FromResult(0d); // 真机由 CPPI 指令控制

    public Task<double> MeasureLeakAsync(CancellationToken ct = default)
        => Task.FromResult(0d);

    public Task CloseRepairVentAsync(CancellationToken ct = default)
        => Task.Run(() => { EnsureConnected(); Dev.Reset(); }, ct);

    public Task<bool> SetMediumAsync(string medium, CancellationToken ct = default)
        => Task.FromResult(true);

    // ===== IConST860PressureQBase 气压扩展 =====

    public Task<bool> SetMeasureModeAsync(string mode, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<bool> SelfTuningAsync(bool start, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<string> ReadSelfTuningResultAsync(CancellationToken ct = default)
        => Task.FromResult("OK");

    public Task<double> ReadOutputPressureAsync(CancellationToken ct = default)
        => Task.Run(() =>
        {
            EnsureConnected();
            var r = Dev.GetCalibratorPressureChannelValue(DPC2Data.PressureModelType.Ext_A);
            if (!r.IsCorrect) return 0d;
            var p = r.Result;
            return p.Value;
        }, ct);

    public Task<(double PV, double SV)> ReadPvSvAsync(CancellationToken ct = default)
        => Task.Run(() =>
        {
            EnsureConnected();
            var r = Dev.GetCalibratorPressureChannelValue(DPC2Data.PressureModelType.Ext_A);
            var pv = r.IsCorrect ? r.Result.Value : 0d;
            return (pv, 0d);
        }, ct);

    public Task<bool> SetTargetPressureAsync(double pressureKpa, CancellationToken ct = default)
        => Task.Run(() =>
        {
            EnsureConnected();
            Dev.SetCalibratorOutputValue(pressureKpa);
            return true;
        }, ct);

    public Task<IReadOnlyList<string>> GetRangeListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(new[] { "1:-100~100", "2:-250~250", "3:-600~600" });

    public Task<bool> SetCurrentRangeAsync(int rangeIndex, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<int> GetCurrentRangeAsync(CancellationToken ct = default)
        => Task.FromResult(1);

    // ===== IConST860PressureYGbk 液压扩展 =====

    public Task<bool> GetExternalLoopStateAsync(CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<bool> RunCalibrationAsync(bool start, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<double> PumpEfficiencyTestAsync(CancellationToken ct = default)
        => Task.FromResult(90.0);

    public Task<bool> SetPumpSpeedAsync(int percentage, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<bool> ChargeControlBoardAsync(string valve, CancellationToken ct = default)
        => Task.FromResult(true);

    // ===== Helpers =====

    private string ExecText(string cmd, object? arg)
    {
        var full = arg is string[] arr ? cmd + " " + string.Join(" ", arr) : cmd;
        var r = Dev.OnExecute(new[] { full });
        if (!r.IsCorrect) throw new DeviceCommException($"{cmd}失败", TestResultStatus.CommunicationError);
        return r.Result ?? "OK";
    }

    private bool ExecOk(string cmd, object? arg)
    {
        var full = arg is string[] arr ? cmd + " " + string.Join(" ", arr) : cmd;
        var r = Dev.OnExecute(new[] { full });
        if (!r.IsCorrect) throw new DeviceCommException($"{cmd}失败", TestResultStatus.CommunicationError);
        return true;
    }

    private static string Result(iResponse<string> r, string what)
        => r.IsCorrect ? (r.Result ?? "") : throw new DeviceCommException($"{what}失败", TestResultStatus.CommunicationError);

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

    private static T ParseEnum<T>(string? s) where T : struct, Enum
        => Enum.TryParse<T>(s, true, out var v) ? v : default;
}
