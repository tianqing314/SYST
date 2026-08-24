using System.Globalization;
using System.IO.Ports;
using Microsoft.Extensions.Logging;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions.Dut;
using Xmas11.Comm.Core;
using Xmas11.Comm.Data.Common;
using Xmas11.Comm.Devices;

namespace SYST.Devices.Dut.ConST685;

/// <summary>
/// ConST685 过程校验仪（设备族 ConST685）被检**真机驱动**：走 Xmas11 <see cref="TAUBase"/> 通讯库。
/// 专用命令走 TAUBase 强类型方法，通用命令走 <see cref="TAUBase.OnExecute(string[])"/>。
/// </summary>
[DutDriver("ConST685")]
public sealed class ConST685Dut : IConST685Dut
{
    private readonly ILogger _logger;
    private readonly CommEndpoint? _comm;
    private TAUBase? _dev;

    public string Key { get; }
    public string Model { get; }
    public bool IsConnected { get; private set; }

    private TAUBase Dev => _dev ?? throw new DeviceCommException("ConST685 未连接", TestResultStatus.CommunicationError);

    public ConST685Dut(DeviceDescriptor descriptor, ILogger logger)
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
            _logger.LogInformation(IsConnected ? "ConST685 真机连接成功" : "ConST685 连接失败");
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
            throw new DeviceCommException("ConST685 自动连接失败", TestResultStatus.CommunicationError);
    }

    private static TAUBase Build(CommEndpoint? ep)
    {
        if (ep is null || ep.Link == LinkType.Serial)
        {
            var sp = ep?.Serial ?? new SerialParams();
            var port = string.IsNullOrWhiteSpace(ep?.PhysicalLink) ? "COM1" : ep!.PhysicalLink!;
            var sb = Enum.TryParse<StopBits>(sp.StopBits, out var s) ? s : StopBits.One;
            var pa = Enum.TryParse<Parity>(sp.Parity, out var p) ? p : Parity.None;
            return new TAUBase(port, sp.Baud, sp.DataBits, sb, pa);
        }
        throw new DeviceCommException($"ConST685 不支持通讯方式: {ep.Link}", TestResultStatus.CommunicationError);
    }

    // ===== IDutDevice =====

    public Task<string> ReadSerialNumberAsync(CancellationToken ct = default)
        => Task.Run(() => { EnsureConnected(); return Dev.GetSN() ?? ""; }, ct);

    public Task<string> ReadFirmwareVersionAsync(CancellationToken ct = default)
        => Task.Run(() =>
        {
            EnsureConnected();
            var info = Dev.GetModuleInfo();
            return info.IsCorrect ? info.Result?.ToString() ?? "" : "";
        }, ct);

    public Task WriteInitInfoAsync(string boardType, CancellationToken ct = default) => Task.CompletedTask;

    public Task<double> MeasureAsync(string point, CancellationToken ct = default) => Task.FromResult(0d);

    public Task<bool> SetSerialNumberAsync(string sn, CancellationToken ct = default)
        => Task.Run(() => { EnsureConnected(); ExecOk("SetSerialNumber", new[] { sn }); return true; }, ct);

    public Task<bool> SetPrimaryDeviceTypeAsync(string dt, CancellationToken ct = default)
        => Task.Run(() => { EnsureConnected(); ExecOk("SetDevType", new[] { dt }); return true; }, ct);

    public Task<bool> QueryBooleanAsync(string method, object? arg, CancellationToken ct = default)
        => Task.Run(() =>
        {
            if (method is not ("Open" or "Close")) EnsureConnected();
            return method switch
            {
                "Open" => OpenAndReport(),
                "Close" => CloseAndReport(),
                "SetDAQScanConfig" => true, // 通过通用指令
                "StartScanChannel" => true,
                "StopScan" => ExecOk("StopScan", arg),
                "StopDAQScan" => ExecOk("StopDAQScan", arg),
                "OpenCurrentChannelZero" => ExecOk("OpenCurrentChannelZero", arg),
                "CloseCurrentChannelZero" => ExecOk("CloseCurrentChannelZero", arg),
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
                "读取型号" => Dev.GetName() ?? "",
                "读取模块信息" => FormatModuleInfo(Dev.GetModuleInfo()),
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

    // ===== OnExecute 通用入口 =====

    private string ExecText(string cmd, object? arg)
    {
        var cmds = arg is string[] arr
            ? new[] { cmd }.Concat(arr).ToArray()
            : new[] { cmd };
        var r = Dev.OnExecute(cmds);
        if (!r.IsCorrect) throw new DeviceCommException($"{cmd}失败", TestResultStatus.CommunicationError);
        return r.Result ?? "OK";
    }

    private bool ExecOk(string cmd, object? arg)
    {
        var cmds = arg is string[] arr
            ? new[] { cmd }.Concat(arr).ToArray()
            : new[] { cmd };
        var r = Dev.OnExecute(cmds);
        if (!r.IsCorrect) throw new DeviceCommException($"{cmd}失败", TestResultStatus.CommunicationError);
        return true;
    }

    private static string FormatModuleInfo(iResponse r)
        => r.IsCorrect ? "OK" : "FAIL";

    // ===== Helpers =====

    private bool OpenAndReport() { EnsureConnected(); return IsConnected; }
    private bool CloseAndReport() { try { _dev?.Close(); } catch { } IsConnected = false; return true; }

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
