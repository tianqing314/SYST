using Microsoft.Extensions.Logging;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;
using SYST.Devices.Comm;
using System.Globalization;
using System.IO.Ports;
using System.Net;
using Xmas11.Comm.Data.Common;
using Xmas11.Comm.Devices;
using Xmas11.Comm.Devices.APC2.Data;
using Xmas11.Domain.Mechanics;
using Xmas11.IO.USB;
// SYST.Core.Abstractions 与 APC2.Data 都有 ElectricMeasureFunction，设备层取 APC2 语义（别名消歧）
using EleMeasureFunction = Xmas11.Comm.Devices.APC2.Data.ElectricMeasureFunction;

namespace SYST.Devices.Dut.ConST811A;

/// <summary>
/// ConST811A 整机（设备族 ConST811A）被检**真机驱动**：走 Xmas11 <see cref="APC2Device"/> 通讯库（替代原 DPG2SCPI），
/// 命令层**自动转换**自旧 <c>Bots.TestBench.Device.ConST811A</c>（内部转调 <c>APC2Device.*</c>，返回 <c>iResponse</c>）。
/// 被检调用统一走 <see cref="IDutDevice"/> 通用派发入口（QueryBooleanAsync/QueryTextAsync/CommandAsync），
/// 内部按方法名路由到具体 APC2 调用（见 <see cref="Execute"/> 派发表）。
/// 连接按 manifest 号位 <see cref="CommEndpoint"/> 的串口/网络参数直接建连；
/// 针床被检在工装准备上电后才连接（工装准备前不连接，见工装准备处理器 ReplenishLinkAsync）。
/// 每条命令 <c>iResponse.IsCorrect=false</c> 即抛 <see cref="DeviceCommException"/>，交引擎按异常收尾。
/// </summary>
[DutDriver("ConST811A")]
public sealed class ConST811ADut : IConST811ADut
{
    /// <summary>日志。</summary>
    private readonly ILogger _logger;

    /// <summary>连接端点（号位 Comm）。</summary>
    private readonly CommEndpoint? _comm;

    /// <summary>ConST811A 通讯实例（连接后有值）。</summary>
    private APC2Device? _dev;

    /// <summary>设备键。</summary>
    public string Key { get; }

    /// <summary>设备型号名。</summary>
    public string Model { get; }

    /// <summary>是否已连接。</summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// 取 ConST811A 实例，未连接抛 <see cref="DeviceCommException"/>（CommunicationError）。
    /// </summary>
    private APC2Device Dev => _dev ?? throw new DeviceCommException("ConST811A 未连接", TestResultStatus.CommunicationError);

    /// <summary>
    /// 用设备描述符构造真机被检（端点取号位 Comm）。
    /// </summary>
    /// <param name="descriptor">设备描述符（含号位 Comm）。</param>
    /// <param name="logger">日志。</param>
    public ConST811ADut(DeviceDescriptor descriptor, ILogger logger)
    {
        _logger = logger;
        Key = descriptor.Model;
        Model = descriptor.Model;
        _comm = descriptor.Comm;
    }

    /// <summary>
    /// 连接被检：按端点（网络/串口）建 APC2Device，Open 成功即连接成功。
    /// 针床被检由工装准备上电后经 <see cref="ReplenishLinkAsync"/> 连接（工装准备前不连接），
    /// 串口参数取 manifest 号位配置（波特率/停止位等），不做旧体系 Board 分支的覆盖/探活指令。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    public Task ConnectAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try { _dev?.Close(); } catch { }
            _dev = Build(_comm, _logger);
            var opened = _dev.Open();
            IsConnected = opened;
            _logger.LogInformation(IsConnected ? "ConST811A 真机连接成功" : "ConST811A 连接未就绪（将重试）");
        }, ct);
    }

    /// <summary>
    /// 按端点构造 APC2Device（网络/串口/USB）。
    /// 串口参数取 manifest 号位配置（波特率/停止位/校验位），不覆盖。
    /// USB 方式先按 VID/PID 扫描 WMI 找虚拟 COM 口按串口连接；无 COM 口的
    /// WinUSB/原生 USB 设备（如 ConST811A 整机）则用 Xmas11 USB 枚举取位置直连。
    /// </summary>
    /// <param name="ep">连接端点。</param>
    /// <param name="logger">日志（USB 扫描时记录）。</param>
    /// <returns>通讯实例。</returns>
    private static APC2Device Build(CommEndpoint? ep, ILogger? logger = null)
    {
        if (ep is null || ep.Link == LinkType.Ethernet)
        {
            var ip = ep?.Ip ?? Environment.GetEnvironmentVariable("SYST_DUT_IP") ?? "192.168.40.107";
            var port = ep?.Port ?? int.Parse(Environment.GetEnvironmentVariable("SYST_DUT_PORT") ?? "1030", CultureInfo.InvariantCulture);
            return new APC2Device(IPAddress.Parse(ip), port);
        }

        if (ep.Link == LinkType.Serial)
        {
            var sp = ep.Serial ?? new SerialParams();
            var portName = string.IsNullOrWhiteSpace(ep.PhysicalLink) ? "COM1" : ep.PhysicalLink!;
            var stopBits = Enum.TryParse<StopBits>(sp.StopBits, out var sb) ? sb : StopBits.One;
            var parity = Enum.TryParse<Parity>(sp.Parity, out var pa) ? pa : Parity.None;
            return new APC2Device(portName, sp.Baud, sp.DataBits, stopBits, parity);
        }

        if (ep.Link == LinkType.Usb)
        {
            // USB 方式：优先按串口（虚拟 COM）连接；无 COM 口的 WinUSB/原生 USB 设备则用 VID/PID 枚举位置直连。
            var sp = ep.Serial ?? new SerialParams();
            var stopBits = Enum.TryParse<StopBits>(sp.StopBits, out var sb) ? sb : StopBits.One;
            var parity = Enum.TryParse<Parity>(sp.Parity, out var pa) ? pa : Parity.None;

            // PhysicalLink 可能是 COM 口名（如 "COM5"）或 USB 设备位置（如 "Port_#0002.Hub_#0002"）
            // 仅当以 "COM" 开头时才作为 COM 口使用
            if (!string.IsNullOrWhiteSpace(ep.PhysicalLink)
                && ep.PhysicalLink.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            {
                logger?.LogInformation("ConST811A USB 使用指定 COM 口: {Port}", ep.PhysicalLink);
                return new APC2Device(ep.PhysicalLink!, sp.Baud, sp.DataBits, stopBits, parity);
            }

            // 通过 VID/PID 扫描 WMI 查找虚拟 COM 口
            var vid = ep.Vid ?? 0x2E19;
            var pid = ep.Pid ?? 0x02F9;
            var scanner = new WmiSerialScanner();

            // 第一步：精确匹配 VID+PID
            var comPort = scanner.FindComPortByVidPid(vid, pid);
            if (comPort == null)
            {
                // 第二步：仅匹配 VID（PID 可能因复合接口 MI_xx 而不同）
                var vidPorts = scanner.FindComPortsByVid(vid);
                if (vidPorts.Count > 0)
                {
                    comPort = vidPorts[0];
                    logger?.LogWarning("ConST811A USB 未找到精确匹配 VID=0x{Vid:X4}&PID=0x{Pid:X4}，使用 VID 匹配到的 {Port}（共找到 {Count} 个）",
                        vid, pid, comPort, vidPorts.Count);
                }
            }

            if (comPort != null)
            {
                logger?.LogInformation("ConST811A USB 扫描到 COM 口: {Port} (VID=0x{Vid:X4} PID=0x{Pid:X4})", comPort, vid, pid);
                return new APC2Device(comPort, sp.Baud, sp.DataBits, stopBits, parity);
            }

            // 第三步：无虚拟 COM 口 → USB 直连（WinUSB/原生 USB 设备，如 ConST811A 整机）。
            // 用 Xmas11 自带 USB 枚举取 DeviceLocation（如 "Port_#0002.Hub_#0002"），按 APC2 的 (vid,pid,location) 构造直连。
            try
            {
                if (USBDevice.Find(out Dictionary<USBVidPid, List<DeviceProperties>> all) && all != null)
                {
                    var direct = FindUsbLocation(all, vid, pid);
                    if (direct != null)
                    {
                        logger?.LogInformation("ConST811A USB 直连: {Location} (VID=0x{Vid:X4} PID=0x{Pid:X4})", direct, vid, pid);
                        return new APC2Device((ushort)vid, (ushort)pid, direct);
                    }
                }
            }
            catch (Exception ex)
            {
                // USB 枚举依赖 WindowsDesktop 运行时，非 WPF/WinForms 宿主下会加载失败：记录并走最后兜底
                logger?.LogWarning(ex, "ConST811A USB 直连枚举失败");
            }

            // 第四步：显式配置的 USB 设备位置直连（PhysicalLink 非 COM 名时视为 DeviceLocation）
            if (!string.IsNullOrWhiteSpace(ep.PhysicalLink))
            {
                logger?.LogInformation("ConST811A USB 使用指定设备位置直连: {Location}", ep.PhysicalLink);
                return new APC2Device((ushort)vid, (ushort)pid, ep.PhysicalLink!);
            }

            throw new DeviceCommException(
                $"未找到 VID=0x{vid:X4} PID=0x{pid:X4} 的 USB 设备（请检查设备是否已连接、驱动是否安装）",
                TestResultStatus.CommunicationError);
        }

        throw new DeviceCommException($"ConST811A 不支持通讯方式: {ep.Link}", TestResultStatus.CommunicationError);
    }

    /// <summary>
    /// 在 Xmas11 USB 枚举结果中查找目标 VID/PID 的 DeviceLocation（如 "Port_#0002.Hub_#0002"）。
    /// 先精确匹配 VID+PID，再退而仅匹配 VID（PID 可能因复合接口 MI_xx 而不同）。
    /// </summary>
    /// <param name="all">USBDevice.Find 的枚举结果。</param>
    /// <param name="vid">厂商 ID。</param>
    /// <param name="pid">产品 ID。</param>
    /// <returns>第一个非空 DeviceLocation；未找到返回 null。</returns>
    private static string? FindUsbLocation(Dictionary<USBVidPid, List<DeviceProperties>> all, int vid, int pid)
    {
        foreach (var kv in all)
        {
            if (kv.Key.VID == (uint)vid && kv.Key.PID == (uint)pid)
            {
                foreach (var dp in kv.Value)
                {
                    if (!string.IsNullOrWhiteSpace(dp.DeviceLocation))
                    {
                        return dp.DeviceLocation;
                    }
                }
            }
        }

        foreach (var kv in all)
        {
            if (kv.Key.VID == (uint)vid)
            {
                foreach (var dp in kv.Value)
                {
                    if (!string.IsNullOrWhiteSpace(dp.DeviceLocation))
                    {
                        return dp.DeviceLocation;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 补充连接（重连）。PORT: 旧 ConST811A.ReplenishLink。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>是否连接成功。</returns>
    public async Task<bool> ReplenishLinkAsync(CancellationToken ct = default)
    {
        await ConnectAsync(ct);
        return IsConnected;
    }

    /// <summary>
    /// 惰性连接：未连接时按号位端点建连，失败抛通讯异常。
    /// 单测/整跑点击测试项即自动连接，无需先在连接配置页手动连接。PORT: 旧平台 Open。
    /// </summary>
    private void EnsureConnected()
    {
        if (_dev is not null && IsConnected)
        {
            return;
        }

        try { _dev?.Close(); } catch { }
        _dev = Build(_comm, _logger);
        var opened = _dev.Open();
        IsConnected = opened;
        _logger.LogInformation(IsConnected ? "ConST811A 自动连接成功" : "ConST811A 自动连接失败");
        if (!IsConnected)
        {
            throw new DeviceCommException("ConST811A 连接失败（请检查设备是否已连接、驱动是否安装）", TestResultStatus.CommunicationError);
        }
    }

    /// <summary>
    /// 关闭连接（静默）。
    /// </summary>
    private void CloseLink()
    {
        try { _dev?.Close(); } catch { }
        _dev = null;
        IsConnected = false;
    }

    // ===== IDutDevice 必需实现 =====

    /// <summary>读整机序列号。PORT: APC2.GetSerialNumber。</summary>
    public Task<string> ReadSerialNumberAsync(CancellationToken ct = default)
        => Str(() => Dev.GetSerialNumber(), "读取SN", ct);

    /// <summary>读固件版本。PORT: APC2.GetVersion。</summary>
    public Task<string> ReadFirmwareVersionAsync(CancellationToken ct = default)
        => Str(() => Dev.GetVersion(), "读取版本", ct);

    /// <summary>写板卡类型/初始信息（旧体系无对应命令，留空）。</summary>
    public Task WriteInitInfoAsync(string boardType, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>按测量点名测量（旧体系无通用测量入口，返回 0）。</summary>
    public Task<double> MeasureAsync(string point, CancellationToken ct = default)
        => Task.FromResult(0d);

    /// <summary>设置被检序列号。PORT: APC2.SetSerialNumber。</summary>
    public Task<bool> SetSerialNumberAsync(string serialNumber, CancellationToken ct = default)
        => Bool(() => Dev.SetSerialNumber(serialNumber), "设置SN", ct);

    /// <summary>设置产品型号/主设备类型。PORT: APC2.SetPrimaryDevType。</summary>
    public Task<bool> SetPrimaryDeviceTypeAsync(string deviceType, CancellationToken ct = default)
        => Bool(() => Dev.SetPrimaryDevType(deviceType), "设置产品型号", ct);

    // ===== 通用派发入口（遗留脚本自动转换） =====

    /// <summary>通用布尔查询（遗留脚本自动转换）。按方法名 + 参数派发到 APC2，返回是否成功。</summary>
    public Task<bool> QueryBooleanAsync(string method, object? arg, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var a = Args(arg);
            if (method is not ("Open" or "Close" or "SetCommConfigEmpty"))
            {
                EnsureConnected();
            }

            return method switch
            {
                "Open" => OpenAndReport(),
                "Close" => CloseAndReport(),
                "SetCommConfigEmpty" => NoOpOk("清空通讯配置"),
                "IsDoubleRange" => IsDoubleRangeCheck(),
                _ => Execute(method, a).IsCorrect,
            };
        }, ct);

    /// <summary>通用文本查询（遗留脚本自动转换）。按方法名 + 参数派发到 APC2，返回结果文本；失败抛异常。</summary>
    public Task<string> QueryTextAsync(string method, object? arg, CancellationToken ct = default)
        => Task.Run(() =>
        {
            EnsureConnected();
            var a = Args(arg);
            return method switch
            {
                "GetRS1" => GetRS1(Arg(a, 0)),
                "GetRS2" => GetRS2(Arg(a, 0)),
                "GetAtmos" => StrResult(() => Dev.GetAtmos(), "GetAtmos"),
                "GetDUTSN" => StrResult(() => Dev.GetSerialNumber(), "GetDUTSN"),
                "GetStaticETHemetIPAddress" => StrResult(() => Dev.GetStaticETHemetIPAddress(), "GetStaticETHemetIPAddress"),
                "GetPressureModelOnlineState" => StrResult(() => Dev.GetPressureModelOnlineState(), "GetPressureModelOnlineState"),
                "GetStorageCardState" => StrResult(() => Dev.StorageCardState(), "GetStorageCardState"),
                "GetControllerBroadPowerCheckState" => ControllerBroadPowerCheckText(),
                "GetPowerSupplyCheck" => StrResult(() => Dev.GetPowerSupplyCheck(), "GetPowerSupplyCheck"),
                "GetMotor_Temperature" => StrResult(() => Dev.GetPumpTemperature(), "GetMotor_Temperature"),
                "GetDevType" => ResultText(Dev.GetDevType(), "GetDevType").Replace(",", ""),
                "GetCurrentElectricMeasure" => ElectricMeasureText(),
                "GetElectricalBroadPowerCheckState" => ElectricalPowerCheckText(),
                "GetPAMassage" => PAMassageList(),
                "ConnectPA" => ConnectPAByAddress(a),
                // USB 文本命令
                "GetUSBdriveState" => USBdriveStateText(),
                "GetUSBdriveSize" => USBdriveSizeText(),
                "ReadDataFromUSB" => ReadDataFromUSBText(a),
                // WiFi 文本命令
                "GetCommType" => GetCommType(),
                "GetWLANFunctionState" => GetWLANFunctionState(),
                "GetWIFIState" => ResultText(Dev.GetWifiState(), "GetWIFIState"),
                "GetWIFIMacAddress" => ResultText(Dev.GetWiFiAddress(), "GetWIFIMacAddress"),
                "GetConnectWifiState" => ResultText(Dev.GetConnectWifiState(), "GetConnectWifiState"),
                "GetWifiIPAddress" => ResultText(Dev.GetWifiIPAddress(), "GetWifiIPAddress"),
                // 自整定状态
                "GetSelfTuningState" => SelfTuningStateText(),
                // 进气传感器校准状态
                "GetCalibrationSensorState" => CalibrationSensorStateText(),
                // 压力量程上下限（从 PressureRange 中提取单个值）
                "GetPressureControlRange_UpperLimit" => PressureRangeValueText("Upper"),
                "GetPressureControlRange_LowerLimit" => PressureRangeValueText("Lower"),
                // 压力量程范围（Lower~Upper，供泄露/排空测试报告量程）
                "GetSetPointLimitPressureRange" => PressureRangeText(),
                // 压力值（从 Pressure record 中提取 Value）
                "GetPressure_IPM" => PressureValueText(() => _dev!.GetPressure_IPM()),
                "GetPressureLowerer_IPM" => PressureValueText(() => _dev!.GetPressureLowerer_IPM()),
                "GetSupplyPressure" => PressureValueText(() => _dev!.GetSupplyPressure()),
                "GetVacuumPressure" => PressureValueText(() => _dev!.GetVacuumPressure()),
                "GetAtmosphericPressure" => PressureValueText(() => _dev!.GetAtmos()),
                // 内部模块温度（高压,低压；InterPressureModuleInfo 转换）
                "GetDev_T" => InterModuleTempText(),
                _ => ResultText(Execute(method, a), method),
            };
        }, ct);

    /// <summary>通用指令执行（遗留脚本自动转换）。按方法名 + 参数派发到 APC2，失败抛异常。</summary>
    public Task CommandAsync(string method, object? arg, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var a = Args(arg);
            if (method is not ("Open" or "Close" or "SetCommConfigEmpty"))
            {
                EnsureConnected();
            }

            switch (method)
            {
                case "Open":
                    OpenAndReport();
                    return;
                case "Close":
                    CloseAndReport();
                    return;
                case "SetCommConfigEmpty":
                    NoOpOk("清空通讯配置");
                    return;
            }
            var r = Execute(method, a);
            if (!r.IsCorrect)
                throw new DeviceCommException($"ConST811A {method} 失败", TestResultStatus.CommunicationError);
        }, ct);

    // ===== iResponse 包装：失败抛 DeviceCommException =====

    /// <summary>执行一条返回字符串的命令，失败抛通讯异常。</summary>
    private Task<string> Str(Func<iResponse<string>> call, string what, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            EnsureConnected();
            var r = call();
            if (!r.IsCorrect)
                throw new DeviceCommException($"{what}失败", TestResultStatus.CommunicationError);
            return r.Result;
        }, ct);
    }

    /// <summary>执行一条返回 bool 的命令，失败抛通讯异常。</summary>
    private Task<bool> Bool(Func<iResponse> call, string what, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            EnsureConnected();
            var r = call();
            if (!r.IsCorrect)
                throw new DeviceCommException($"{what}失败", TestResultStatus.CommunicationError);
            return true;
        }, ct);
    }

    /// <summary>打开连接并返回结果。设备不存在时返回 false 而非抛异常。</summary>
    private bool OpenAndReport()
    {
        try { _dev?.Close(); } catch { }
        try
        {
            _dev = Build(_comm);
            IsConnected = _dev.Open();
            _logger.LogInformation(IsConnected ? "ConST811A 打开连接成功" : "ConST811A 打开连接失败");
            return IsConnected;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ConST811A 打开连接异常（设备可能正在重启）");
            IsConnected = false;
            return false;
        }
    }

    /// <summary>关闭连接并返回成功。</summary>
    private bool CloseAndReport()
    {
        CloseLink();
        _logger.LogInformation("ConST811A 关闭连接");
        return true;
    }

    /// <summary>无操作指令（旧平台无对应 APC2 能力，仅记录日志并返回成功）。</summary>
    private bool NoOpOk(string what)
    {
        _logger.LogDebug("ConST811A {What}（本驱动无对应操作）", what);
        return true;
    }

    /// <summary>
    /// 是否复合量程（双模块在线）。PORT: 旧 ConST811A.IsDoubleRange。
    /// state[1]=高压模块在线, state[0]=低压模块在线；两者都 Open 才算复合量程。
    /// </summary>
    private bool IsDoubleRangeCheck()
    {
        var r = Dev.GetPressureModelOnlineState();
        if (!r.IsCorrect || r.Result is not List<OpenCloseState> states || states.Count < 2)
            return false;
        return states[1] == OpenCloseState.Open && states[0] == OpenCloseState.Open;
    }

    /// <summary>从 iResponse 取文本结果（通用），失败抛异常。</summary>
    private string ResultText(iResponse r, string what)
    {
        if (!r.IsCorrect)
            throw new DeviceCommException($"ConST811A {what} 失败", TestResultStatus.CommunicationError);
        return ((dynamic)r).Result?.ToString() ?? string.Empty;
    }

    /// <summary>执行返回 iResponse&lt;string&gt; 的调用并取 Result。</summary>
    private string StrResult(Func<iResponse<string>> call, string what)
    {
        var r = call();
        if (!r.IsCorrect)
            throw new DeviceCommException($"ConST811A {what} 失败", TestResultStatus.CommunicationError);
        return r.Result;
    }

    /// <summary>任意 iResponse 调用取文本（枚举/数值 ToString），失败抛异常。</summary>
    private string StrResult<T>(Func<iResponse<T>> call, string what)
    {
        var r = call();
        if (!r.IsCorrect)
            throw new DeviceCommException($"ConST811A {what} 失败", TestResultStatus.CommunicationError);
        return r.Result?.ToString() ?? string.Empty;
    }

    /// <summary>从 PressureRange 提取上限或下限的数值文本。which: "Upper" 或 "Lower"。</summary>
    private string PressureRangeValueText(string which)
    {
        var r = _dev!.GetSetPointEditPressureRange();
        if (!r.IsCorrect)
            throw new DeviceCommException($"ConST811A GetPressureControlRange_{which}Limit 失败", TestResultStatus.CommunicationError);
        dynamic range = ((dynamic)r).Result;
        double value = which == "Upper" ? (double)range.UpperValue : (double)range.LowerValue;
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>从 PressureRange 提取量程范围文本 "Lower~Upper"（kPa）。</summary>
    private string PressureRangeText()
    {
        var r = _dev!.GetSetPointEditPressureRange();
        if (!r.IsCorrect)
            throw new DeviceCommException("ConST811A 获取压力控制量程范围失败", TestResultStatus.CommunicationError);
        dynamic range = ((dynamic)r).Result;
        double lower = (double)range.LowerValue;
        double upper = (double)range.UpperValue;
        return $"{lower.ToString(CultureInfo.InvariantCulture)}~{upper.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// 从 <c>InterPressureModuleInfo</c> 提取内部模块温度文本 "高压温度,低压温度"。
    /// 与旧脚本 <c>GetDev_T</c> 语义对齐（tvalue 记录"压力值,高压温度,低压温度,泵温度,电测板温度"）。
    /// </summary>
    private string InterModuleTempText()
    {
        var r = _dev!.GetInterPressureModuleInfo();
        if (!r.IsCorrect)
            throw new DeviceCommException("ConST811A 读取内部模块温度失败", TestResultStatus.CommunicationError);
        var info = r.Result;
        if (info is null) return string.Empty;
        return $"{info.HighModuleTemperature.ToString(CultureInfo.InvariantCulture)},{info.LowModuleTemperature.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>从 iResponse&lt;Pressure&gt; 中提取 Value 并转为文本。</summary>
    private string PressureValueText(Func<iResponse> call)
    {
        var r = call();
        if (!r.IsCorrect)
            throw new DeviceCommException($"ConST811A 压力读取失败", TestResultStatus.CommunicationError);
        double value = ((dynamic)r).Result.Value;
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>获取电测板电源状态。旧代码逻辑：响应 "00-00-00-00" 或 "00-04-00-00" 视为 OK。</summary>
    private string ElectricalPowerCheckText()
    {
        var r = Dev.GetElectricalException();
        if (!r.IsCorrect)
            throw new DeviceCommException("ConST811A GetElectricalException 失败", TestResultStatus.CommunicationError);
        var result = r.Result?.ToString() ?? string.Empty;
        // 与旧代码一致：特定返回值视为 OK
        return (result == "00-00-00-00" || result == "00-04-00-00") ? "OK" : result;
    }

    /// <summary>获取控制板电源状态。响应 "00-00-00-00-00-00" 视为 OK。</summary>
    private string ControllerBroadPowerCheckText()
    {
        var r = Dev.GetControllerException();
        if (!r.IsCorrect)
            throw new DeviceCommException("ConST811A GetControllerException 失败", TestResultStatus.CommunicationError);
        var result = r.Result?.ToString() ?? string.Empty;
        // 特定返回值视为 OK
        return result.StartsWith("00-00-") ? "OK" : result;
    }

    /// <summary>获取当前电测量值（纯数值字符串，供 double.TryParse 使用）。</summary>
    private string ElectricMeasureText()
    {
        var r = Dev.GetCurrentElectricMeasure();
        if (!r.IsCorrect)
            throw new DeviceCommException("ConST811A GetCurrentElectricMeasure 失败", TestResultStatus.CommunicationError);
        var m = r.Result;
        if (m is null) return string.Empty;
        return m.MeasureValue.ToString("f4");
    }

    /// <summary>获取自整定状态，返回 ResultType 枚举名。PORT: 旧 ConST811A.GetSelfTuningState。</summary>
    private string SelfTuningStateText()
    {
        var r = Dev.GetSelfTuningState();
        if (!r.IsCorrect)
            throw new DeviceCommException("ConST811A GetSelfTuningState 失败", TestResultStatus.CommunicationError);
        // SelfTuningData 有 ResultType (SelfTuningTestType enum) 和 ProcessValue
        dynamic data = r.Result;
        if (data is null) return "Unknown";
        string resultType = data.ResultType?.ToString() ?? "Unknown";
        // 如果是 InProgress，附加进度百分比
        if (resultType == "InProgress")
        {
            int progress = (int)(data.ProcessValue ?? 0);
            return $"InProgress:{progress}";
        }
        return resultType;
    }

    /// <summary>获取进气传感器校准状态，返回 CalibrationSensorStateTest 枚举名。PORT: 旧 ConST811A.GetCalibrationSensorState。</summary>
    private string CalibrationSensorStateText()
    {
        var r = Dev.GetCalibrationSensorState();
        if (!r.IsCorrect)
            throw new DeviceCommException("ConST811A GetCalibrationSensorState 失败", TestResultStatus.CommunicationError);
        dynamic data = r.Result;
        if (data is null) return "UnKnown";
        string resultType = data.ResultType?.ToString() ?? "UnKnown";
        // 如果是 Process，附加进度百分比
        if (resultType == "Process")
        {
            int progress = (int)(data.ProcessValue ?? 0);
            return $"Process:{progress}";
        }
        return resultType;
    }

    /// <summary>获取 PA 变送器列表，转为分号分隔字符串（每个元素取 Address）。</summary>
    private string PAMassageList()
    {
        var r = Dev.GetPAMassage();
        if (!r.IsCorrect)
            throw new DeviceCommException("ConST811A GetPAMassage 失败", TestResultStatus.CommunicationError);
        var list = r.Result;
        if (list is null || list.Count == 0)
            return string.Empty;
        return string.Join(";", list.Select(m => m.Address));
    }

    /// <summary>按地址连接 PA 变送器：从设备获取列表，匹配地址后连接。</summary>
    private string ConnectPAByAddress(string[]? a)
    {
        var target = a?.FirstOrDefault() ?? string.Empty;
        var listR = Dev.GetPAMassage();
        if (!listR.IsCorrect)
            throw new DeviceCommException("ConST811A GetPAMassage 失败", TestResultStatus.CommunicationError);
        var list = listR.Result;
        if (list is null || list.Count == 0)
            return "false";
        // 匹配：地址字符串匹配 PAMassage.Address
        var match = list.FirstOrDefault(m => m.Address.Contains(target, StringComparison.OrdinalIgnoreCase));
        var addr = match?.Address ?? list[0].Address;
        var r = Dev.ConnectPA(addr);
        return r.IsCorrect ? "true" : "false";
    }

    /// <summary>
    /// 核心派发表：方法名 → APC2 调用。方法名与旧脚本 ConST811A 方法/指令一致。
    /// </summary>
    /// <param name="method">方法名。</param>
    /// <param name="a">参数字符串数组（可能为 null）。</param>
    /// <returns>iResponse（<see cref="iResponse{T}"/> 是其子类，可直接赋值）。</returns>
    private iResponse Execute(string method, string[]? a)
    {
        var dev = Dev;
        return method switch
        {
            // ===== 自检 (SelfCheck) =====
            "SetSelfCheck" => dev.SetSelfCheck(Parse<CheckType>(Arg(a, 0)), Parse<CheckDo>(Arg(a, 1))),
            "GetSelfCheck" => dev.GetSelfCheck(Parse<CheckType>(Arg(a, 0))),
            "GetSelfCheckError" => dev.GetSelfCheckError(Parse<CheckType>(Arg(a, 0))),
            "GetMainBoardCheckState" => dev.GetMainBoardCheckState(),
            "GetControllerBroadPowerCheckState" => dev.GetControllerException(),
            "GetElectricalBroadPowerCheckState" => dev.GetElectricalException(),
            "GetCheckerState" => dev.GetCheckerState(Parse<ProgramFunction>(Arg(a, 0))),
            "SetCheckerOpen" => dev.SetCheckerOpen(Parse<ProgramFunction>(Arg(a, 0))),
            "SetCheckerClose" => dev.SetCheckerClose(),
            "SetCheckerSelect" => dev.SetCheckerSelect(Parse<ProgramFunction>(Arg(a, 0))),

            // ===== 电源/电池 (Power/Battery) =====
            "GetBatteryValue" => dev.GetBatteryValue(),
            "GetBATTery2" => dev.GetBATTery(),
            "GetPowerSupplyCheck" => dev.GetPowerSupplyCheck(),
            "GetEnergyCheckStata" => dev.GetEnergyCheckStata(),
            "GetSupplyPressure" => dev.GetSupplyPressure(),
            "GetVacuumPressure" => dev.GetVacuumPressure(),

            // ===== 自整定 (SelfTuning) =====
            "SelfTuning" => dev.SelfTuning(),
            "StopSelfTuning" => dev.StopSelfTuning(),
            "GetSelfTuningState" => dev.GetSelfTuningState(),

            // ===== 进气传感器校准 (CalibrationSensor) =====
            "CalibrationSensor" => dev.CalibrationSensor(),
            "StopCalibrationSensor" => dev.StopCalibrationSensor(),
            "GetCalibrationSensorState" => dev.GetCalibrationSensorState(),
            "SetCalibrationSensorDate" => dev.SetCalibrationSensorDate(ToDate(Arg(a, 0))),
            "SetCalibrationAutoDate" => dev.SetCalibrationAutoDate(ToDate(Arg(a, 0))),

            // ===== 电测 (Electric Measurement) =====
            "GetCurrentElectricMeasure" => dev.GetCurrentElectricMeasure(),
            "SetEleChannelItem_VOL" => dev.SetElectricMeasureFunction(EleMeasureFunction.VOL, 0),
            "SetEleChannelItem_CURR" => dev.SetElectricMeasureFunction(EleMeasureFunction.CURR, ToBool(Arg(a, 0)) ? 1 : 0),
            "SetEleChannelItem_SW_Normal" => dev.SetElectricMeasureFunction(EleMeasureFunction.SW, 0),
            "SetEleChannelItem_SW_NPN" => dev.SetElectricMeasureFunction(EleMeasureFunction.SW, 1),
            "SetEleChannelItem_SW_PNP" => dev.SetElectricMeasureFunction(EleMeasureFunction.SW, 2),
            "SetEleChannelItem_PA" => dev.SetElectricMeasureFunction(EleMeasureFunction.PA, 0),
            "SetEleChannelItem_HART" => dev.SetElectricMeasureFunction(EleMeasureFunction.HART, 0),
            "SetEleChannelItem_HARTClose" => dev.SetElectricMeasureFunction(EleMeasureFunction.NONE, 0),
            "SetElectricSourceFunction" => dev.SetElectricSourceFunction(Parse<ElectricSourceFunction>(Arg(a, 0)), 0),
            "SetElectricSource_MA" => dev.SetElectricSourceFunction(ElectricSourceFunction.mA, ToBool(Arg(a, 0)) ? 1 : 0),
            "SetElectricSourceTarget" => dev.SetElectricSourceTarget(ToDouble(Arg(a, 0))),

            // ===== 压力控制 (Pressure Control) =====
            "SetTargetPressure" => dev.SetTargetPressureValue(ToDouble(Arg(a, 0))),
            "GetPressure_IPM" => dev.GetPressure_IPM(),
            "GetPressureLowerer_IPM" => dev.GetPressureLowerer_IPM(),
            "SetPressureUnit_IPM" => dev.SetPressureModelUnit(PressureModel.ControllerModule, PressureUnit.kPa),
            "SetVentMode" => dev.SetPressureControlMode(DevicePressureControlMode.VENT),
            "SetTestMode" => dev.SetPressureControlMode(DevicePressureControlMode.MEASURE),
            "SetControlPressureModel" => SetControlPressureModel(dev, Arg(a, 0)),
            "SetOpenMaxControlPressureSpeed" => dev.SetOpenMaxControlPressureSpeed(ToBool(Arg(a, 0))),
            "GetPressureStableState" => dev.GetPressureModelStableState(1),
            "SetPressureStability" => dev.SetPressureModelStableParam(1, ToDouble(Arg(a, 0)), 5),
            "GetPressureControlRange_UpperLimit" => dev.GetSetPointEditPressureRange(),
            "GetPressureControlRange_LowerLimit" => dev.GetSetPointEditPressureRange(),
            "GetSetPointLimitPressureRange" => dev.GetSetPointEditPressureRange(),
            "SetModuleStableEnable" => dev.SetModuleStableEnable(Parse<StableModuleType>(Arg(a, 0)), Parse<OpenCloseState>(Arg(a, 1))),

            // ===== 气泵 (Pump) =====
            "TestPositivePump" => dev.TestPump(PumpTestItem.Positive),
            "TestNegativePump" => dev.TestPump(PumpTestItem.Negative),
            "TestPumpStop" => dev.TestPump(PumpTestItem.Stop),
            "GetPumpTestState" => dev.GetPumpTestState(),
            "GetPumpCurrent" => dev.GetPumpCurrent(),

            // ===== 系统 (System) =====
            "GetSystemTime" => dev.GetSystemTime(),
            "SetSystemTime" => dev.SetSystemTime(ToDate(Arg(a, 0))),
            "SetSystemDate" => dev.SetSystemDate(ToDate(Arg(a, 0))),
            "GetDevSysDate" => dev.GetSystemDateTime(),
            "GetDev_T" => dev.GetInterPressureModuleInfo(),
            "GetMotor_Temperature" => dev.GetPumpTemperature(),
            "GetVersion_Controller" => dev.GetVersion_Controller(),
            "SetReboot" => dev.SetReboot(),
            "GetDUTSN" => dev.GetSerialNumber(),

            // ===== 无线/蓝牙/WiFi =====
            "GetBlueToothState" => dev.GetBlueToothState(),
            "GetBlueToothName" => dev.GetBlueToothName(),
            "OpenBlueTooth" => dev.SetBlueToothState(OpenCloseState.Open),
            "CloseBlueTooth" => dev.SetBlueToothState(OpenCloseState.Close),
            "SetWifiClose" => dev.SetWifiState(OpenCloseState.Close),
            "GetStaticETHemetIPAddress" => dev.GetStaticETHemetIPAddress(),

            // ===== 存储 (Storage) =====
            "GetStorageCardState" => dev.StorageCardState(),
            "AddDataToSD" => dev.DataAddtoStorageCard(a![0], Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(a![1])), FileWriteType.TRUNcate),
            "ReadDataFromSD" => dev.DataReadtoStorageCard(a![0]),
            "DelSDCardfile" => dev.Delfile($@"\Storage_Card\{a![0]}"),
            "QuerySDCardfileExists" => dev.QueryfileExists($@"\Storage_Card\{a![0]}"),
            "DiskSize_SD" => dev.DiskSize("Storage_Card"),

            // ===== HART/PA 变送器 =====
            "SetBrightness" => dev.SetBrightness(Parse<BrightnessType>(Arg(a, 0)), Arg(a, 1)),
            "SetFANOn" => dev.SetFANOn(),
            "SetFANClose" => dev.SetFANClose(),
            "SetValveStata" => dev.SetSwitchValveState(ToInt(Arg(a, 0))),
            "SearchPA" => dev.SearchPA(),
            "ConnectPA" => dev.ConnectPA(Arg(a, 0)),
            "GetPAMassage" => dev.GetPAMassage(),
            "StartSearchHart" => dev.SearchHart(SearchState.Start),
            "StopSearchHart" => dev.SearchHart(SearchState.Stop),
            "ConnectHart" => dev.ConnectHartDevice(ToInt(Arg(a, 0))),
            "GetEleHartMassage" => dev.GetHartMassage(),
            "GetSupplyMode" => dev.GetHartSupplyMode(),
            "SetSwitchMode_IPIR" => dev.SetHartSupplyMode(ResistancePowerSupplyMode.IPIR),
            "GetControllerModuleConfig" => dev.GetControlPressureModelInfo(4),
            "GetPressureModelOnlineState" => dev.GetPressureModelOnlineState(),
            "GetAtmosSensor" => dev.GetAtmos(),
            "GetAtmos" => dev.GetAtmos(),
            "GetDevType" => dev.GetPrimaryDevType(),

            // ===== 大气压传感器 (Atmospheric Sensor) =====
            "GetAtmosphericPressure" => dev.GetAtmos(),

            // ===== 气泵 (Gas Pump) =====
            "GetGasPumpState" => dev.GetPumpTestState(),
            "SetGasPumpStart" => dev.TestPump(PumpTestItem.Positive),
            "SetGasPumpStop" => dev.TestPump(PumpTestItem.Stop),

            // ===== NTC 温度 =====
            "GetNTCTemperature" => dev.GetPumpTemperature(),

            // ===== 压力控制 (Pressure Control Extended) =====
            "SetPressureControl" => dev.SetPressureControlMode(DevicePressureControlMode.MEASURE),
            "GetPressureControlState" => dev.GetPressureModelStableState(1),

            // ===== 气密性测试 (QR Leak Test) =====
            "SetQRLeakTestStart" => dev.SetPressureControlMode(DevicePressureControlMode.MEASURE),
            "GetQRLeakTestState" => dev.GetPressureModelStableState(1),

            // ===== 传感器校准 (Calibration Sensor Extended) =====
            "SetCalibrationSensorAuto" => dev.SetCalibrationAutoDate(DateTime.Now),
            "GetCalibrationSensorResult" => dev.GetCalibrationSensorState(),

            // ===== 控压系数 (Control Panel Model Parameter) =====
            "SetControlPanelModelParameter" => dev.SetPressureModelStableParam(1, ToDouble(Arg(a, 0)), 5),
            "GetControlPanelModelParameter" => dev.GetPressureModelStableState(1),

            // ===== 泵阻转电流 (Dump Current) =====
            "SetDumpStallingCurrent" => dev.SetPressureControlMode(DevicePressureControlMode.MEASURE),
            "GetDumpCurrent" => dev.GetPumpCurrent(),
            "SetDumpCurrent" => dev.SetPressureControlMode(DevicePressureControlMode.MEASURE),

            // ===== 序列号/版本 (Serial Number / Version) =====
            "GetSerialNumber" => dev.GetSerialNumber(),
            "GetVersion" => dev.GetVersion(),
            "GetVersion_Electricity" => dev.GetVersion(),
            "GetVersion_OS" => dev.GetVersion_OS(),
            "GetFixVersion" => dev.GetVersion(),
            "ReadHardWaveVersion" => dev.GetVersion(),
            "GetHardCorValue" => dev.GetVersion(),
            "GetBatteryVoltage" => dev.GetBatteryValue(),

            // ===== USB =====
            "GetUSBCommState" => dev.GetBlueToothState(),
            "GetUSBStorageState" => dev.StorageCardState(),
            "GetUSBdriveState" => dev.USBdriveState(),
            "GetUSBdriveSize" => dev.DiskSize("Hard Disk"),
            "AddDataToUSB" => dev.AddDatatoUSB(a![0], Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(a![1])), FileWriteType.TRUNcate),
            "ReadDataFromUSB" => dev.ReadDatatoUSB(a![0]),
            "DelUSBfile" => dev.Delfile($@"\Hard Disk\{a![0]}"),
            "QueryUSBfileExists" => dev.QueryfileExists($@"\Hard Disk\{a![0]}"),

            // ===== WiFi =====
            "GetWIFIState" => dev.GetWifiState(),
            "GetWIFIMacAddress" => dev.GetWiFiAddress(),
            "OpenWIFI" => dev.SetWifiState(OpenCloseState.Open),
            "CloseWIFI" => dev.SetWifiState(OpenCloseState.Close),
            "ConnectWifiToHotspot" => dev.ConnectWifiToHotspot(a![0], a![1], a![2]),
            "GetConnectWifiState" => dev.GetConnectWifiState(),
            "GetWifiIPAddress" => dev.GetWifiIPAddress(),
            "OpenWLANFunction" => dev.SetFunctionState(FunctionType.WLAN, OpenCloseState.Open),
            "CloseWLANFunction" => dev.SetFunctionState(FunctionType.WLAN, OpenCloseState.Close),

            // ===== 电源适配器 (Power Adapter) =====
            "GetPowerAdapterState" => dev.GetPowerSupplyCheck(),

            // ===== 设备DDT =====
            "GetDeviceDDT" => dev.GetVersion(),

            _ => throw new DeviceCommException($"ConST811A 不支持指令 {method}", TestResultStatus.CommunicationError),
        };
    }

    /// <summary>切换控制模块高/低量程。PORT: 旧 SetControlPressureModel(PressureSwitchTripType)。</summary>
    private static iResponse SetControlPressureModel(APC2Device dev, string? mode)
    {
        return mode?.Trim() switch
        {
            "High" or "高" => dev.SetControlPressureModel_Q(PressureModel.InterHighPressure),
            "Low" or "低" => dev.SetControlPressureModel_Q(PressureModel.InterLowPressure),
            _ => throw new DeviceCommException($"ConST811A SetControlPressureModel 参数无效：{mode}", TestResultStatus.CommunicationError),
        };
    }

    /// <summary>获取当前通讯类型（ETH/WLAN/USB）。PORT: 旧 ConST811A.GetCommType。</summary>
    /// <remarks>旧代码通过检查 CommInstance 类型来判断。简化版：检查 WiFi 连接状态。</remarks>
    private string GetCommType()
    {
        try
        {
            var wifiState = Dev.GetConnectWifiState();
            if (wifiState.IsCorrect && wifiState.Result.ToString() == "Open")
                return "WLAN";
            return "None";
        }
        catch
        {
            return "None";
        }
    }

    /// <summary>获取 WLAN 功能开关状态（Open/Close）。PORT: 旧 ConST811A.GetWLANFunctionState。</summary>
    private string GetWLANFunctionState()
    {
        var r = Dev.GetFunctionState(FunctionType.WLAN);
        if (!r.IsCorrect || r.Result is not List<bool> states || states.Count < 1)
            throw new DeviceCommException("ConST811A GetWLANFunctionState 失败", TestResultStatus.CommunicationError);
        return states[0] ? "Open" : "Close";
    }

    // ===== USB 辅助方法 =====

    /// <summary>获取 USB 驱动器状态。PORT: 旧 ConST811A.GetUSBdriveState。</summary>
    private string USBdriveStateText()
    {
        var r = Dev.USBdriveState();
        if (!r.IsCorrect)
            throw new DeviceCommException("ConST811A USBdriveState 失败", TestResultStatus.CommunicationError);
        return r.Result.ToString() ?? string.Empty;
    }

    /// <summary>获取 USB 驱动器大小。PORT: 旧 ConST811A.GetUSBdriveSize。</summary>
    private string USBdriveSizeText()
    {
        var r = Dev.DiskSize("Hard Disk");
        if (!r.IsCorrect)
            throw new DeviceCommException("ConST811A USBdriveSize 失败", TestResultStatus.CommunicationError);
        var arr = r.Result as long[];
        if (arr != null && arr.Length >= 2)
            return $"识别成功,总字节{arr[1]}";
        return r.Result.ToString() ?? string.Empty;
    }

    /// <summary>从 USB 读取文件内容。PORT: 旧 ConST811A.ReadDataFromUSB。</summary>
    private string ReadDataFromUSBText(string[]? a)
    {
        if (a == null || a.Length < 1)
            throw new DeviceCommException("ConST811A ReadDataFromUSB 缺少文件名参数", TestResultStatus.CommunicationError);
        var r = Dev.ReadDatatoUSB(a[0]);
        if (!r.IsCorrect)
            throw new DeviceCommException("ConST811A ReadDataFromUSB 失败", TestResultStatus.CommunicationError);
        return r.Result.ToString() ?? string.Empty;
    }

    // ===== GetRS1 / GetRS2：测试前量程与版本预检消息 =====

    /// <summary>
    /// 测试前量程预检。PORT: 旧 ConST811A.GetRS1。
    /// 返回给操作员的量程确认消息：高压/低压模块是否在线、模块量程是否与型号匹配。
    /// </summary>
    private string GetRS1(string devCode)
    {
        var msg = "";
        try
        {
            // 控制板为 APC-BP 时（BP 板）直接通过
            if (IsOk(Execute("GetVersion_Controller", null)) && ResultText(Execute("GetVersion_Controller", null), "GetVersion_Controller").Contains("APC-BP"))
                return "";

            var devType = SnType(devCode);
            // 是否复合量程
            if (!IsDoubleRangeCheck())
            {
                msg = "设备为单模块，正常测试应该安装2个工装模块，请检测是否漏装或者没拧紧。";
                return msg;
            }

            var high = PressureRange(Execute("GetPressureControlRange_UpperLimit", null));
            var low = PressureRange(Execute("GetPressureControlRange_LowerLimit", null));
            var ok = devType switch
            {
                "ConST811AD" => Math.Abs(high.LowerValue + 100) < 1e-6 && Math.Abs(high.UpperValue - 250) < 1e-6
                            && Math.Abs(low.LowerValue + 10) < 1e-6 && Math.Abs(low.UpperValue - 10) < 1e-6,
                "ConST811AG" => Math.Abs(high.LowerValue + 100) < 1e-6 && Math.Abs(high.UpperValue - 7000) < 1e-6
                            && Math.Abs(low.LowerValue + 100) < 1e-6 && Math.Abs(low.UpperValue - 250) < 1e-6,
                "ConST811AG-10M" => Math.Abs(high.LowerValue + 100) < 1e-6 && Math.Abs(high.UpperValue - 10000) < 1e-6
                            && Math.Abs(low.LowerValue + 100) < 1e-6 && Math.Abs(low.UpperValue - 250) < 1e-6,
                "ConST811AL" => Math.Abs(high.LowerValue + 10) < 1e-6 && Math.Abs(high.UpperValue - 10) < 1e-6
                            && Math.Abs(low.LowerValue + 10) < 1e-6 && Math.Abs(low.UpperValue - 10) < 1e-6,
                _ => true,
            };
            if (!ok)
                msg = $"当前设备为{devType}，标准模块量程与实际模块量程不匹配：\r\n实际：{low} -- {high}";
        }
        catch (Exception ex)
        {
            msg = ex.Message;
        }
        return msg;
    }

    /// <summary>
    /// 测试前版本预检。PORT: 旧 ConST811A.GetRS2。
    /// 读取系统/电测/控制板版本并返回提示（远程版本比对服务未接入，仅返回本地读取结果）。
    /// </summary>
    private string GetRS2(string devCode)
    {
        var result = "";
        try
        {
            var sys = ResultText(Execute("GetVersion", null), "GetVersion");
            result += $"系统版本：{sys}\r\n";
            var ele = ResultText(Execute("GetVersion_Electricity", null), "GetVersion_Electricity");
            result += $"电测版本：{ele}\r\n";
            var col = ResultText(Execute("GetVersion_Controller", null), "GetVersion_Controller");
            result += $"控制版本：{col}\r\n";
        }
        catch (Exception ex)
        {
            result += ex.Message;
        }
        return result;
    }

    /// <summary>读取 iResponse 的 Result 是否成功（不抛）。</summary>
    private static bool IsOk(iResponse r) => r.IsCorrect;

    /// <summary>从设备量程 iResponse 中取上下限（kPa）。</summary>
    private static (double LowerValue, double UpperValue) PressureRange(iResponse r)
    {
        if (!r.IsCorrect)
            return (0, 0);
        var obj = ((dynamic)r).Result;
        if (obj is null)
            return (0, 0);
        try
        {
            return ((double)obj.LowerValue, (double)obj.UpperValue);
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>由 SN 提取设备型号段（如 ConST811AD / ConST811AG / ConST811AG-10M / ConST811AL）。</summary>
    private static string SnType(string devCode)
    {
        if (string.IsNullOrWhiteSpace(devCode))
            return "";
        var s = devCode.Trim();
        var idx = s.IndexOf("ConST811A", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return s;
        var sub = s[idx..];
        // 取到型号结尾（到空格/分隔符为止）
        var m = System.Text.RegularExpressions.Regex.Match(sub, @"ConST811A[^ ,;]+");
        return m.Success ? m.Value : sub;
    }

    // ===== 参数解析辅助 =====

    /// <summary>把 object? 参数统一为字符串数组（null/标量/数组）。</summary>
    private static string[]? Args(object? arg) => arg switch
    {
        null => null,
        string s => new[] { s },
        string[] sa => sa,
        _ => new[] { arg.ToString() ?? "" },
    };

    /// <summary>取第 index 个参数（缺省返回 fallback）。</summary>
    private static string Arg(string[]? args, int index, string fallback = "")
        => args is not null && args.Length > index ? args[index] ?? "" : fallback;

    /// <summary>解析 bool（"true"/"1" 视为真）。</summary>
    private static bool ToBool(string? s)
        => !string.IsNullOrWhiteSpace(s) && (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1");

    /// <summary>解析 double（不变文化）。</summary>
    private static double ToDouble(string? s)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0d;

    /// <summary>解析 int（不变文化）。</summary>
    private static int ToInt(string? s)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    /// <summary>解析 DateTime（兼容默认 ToString / 不变文化 / 往返格式）。</summary>
    private static DateTime ToDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return DateTime.Now;
        if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d1))
            return d1;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2))
            return d2;
        return DateTime.Now;
    }

    /// <summary>解析枚举（忽略大小写）。</summary>
    private static TEnum Parse<TEnum>(string? s) where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(s, true, out var v) ? v : throw new DeviceCommException($"ConST811A 参数枚举 {typeof(TEnum).Name} 无效：{s}", TestResultStatus.CommunicationError);

    /// <summary>释放连接。</summary>
    public ValueTask DisposeAsync()
    {
        CloseLink();
        return ValueTask.CompletedTask;
    }
}
