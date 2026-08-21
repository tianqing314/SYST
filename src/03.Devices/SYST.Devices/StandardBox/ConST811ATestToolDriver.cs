using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;
using SYST.Devices.Dut;
using Xmas11.Comm.Device;
using Xmas11.Comm.Devices;
using Xmas11.Comm.Data.Common;

namespace SYST.Devices.StandardBox;

/// <summary>
/// ConST811A 工装设备驱动（GZP21）。
/// 通过以太网与 ConSTGZ811A 工装通讯，控制继电器输出（27V、HART、PA、ELE）和读取状态。
/// </summary>
[DutDriver("ConST811ATestTool")]
internal sealed class ConST811ATestToolDriver : IMachineTestTool
{
    private readonly DeviceDescriptor _descriptor;
    private readonly ILogger _logger;
    private ConSTGZ811A? _tool;
    private bool _connected;

    /// <summary>
    /// 构造函数（自动注册要求：DeviceDescriptor, ILogger）。
    /// </summary>
    public ConST811ATestToolDriver(DeviceDescriptor descriptor, ILogger logger)
    {
        _descriptor = descriptor;
        _logger = logger;
    }

    #region IStandardModule 基本属性
    public string Key => _descriptor.Name;
    public string Model => _descriptor.Model;
    public bool IsConnected => _connected;
    #endregion

    #region 连接管理
    /// <summary>
    /// 连接到工装设备。
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_connected) return;

        try
        {
            // 根据通讯端点创建 ConSTGZ811A 实例
            var comm = _descriptor.Comm;
            if (comm == null)
            {
                throw new InvalidOperationException("设备描述符缺少通讯设置");
            }

            if (comm.Link == LinkType.Ethernet)
            {
                if (string.IsNullOrEmpty(comm.Ip) || comm.Port == null)
                {
                    throw new InvalidOperationException("网络端点缺少 IP 或端口");
                }
                // 直接使用 IP 和端口创建 ConSTGZ811A 实例
                var ipAddress = System.Net.IPAddress.Parse(comm.Ip);
                _tool = new ConSTGZ811A(ipAddress, comm.Port.Value);
            }
            else
            {
                throw new NotSupportedException($"不支持的通讯方式: {comm.Link}");
            }
            
            // 打开连接并检查设备是否存在
            _tool.Open();
            if (!_tool.IsExist())
            {
                throw new InvalidOperationException("工装设备不存在或无法连接");
            }
            
            _connected = true;
            _logger.LogInformation("已连接到 ConST811A 工装设备 {Key}", Key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接 ConST811A 工装设备 {Key} 失败", Key);
            throw;
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// 释放资源。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_tool != null)
        {
            _tool.Close();
            _tool = null;
        }
        _connected = false;
        _logger.LogInformation("已断开 ConST811A 工装设备 {Key}", Key);
        await Task.CompletedTask;
    }
    #endregion

    #region IMachineTestTool 实现
    /// <summary>
    /// 是否为真实硬件（非仿真）。
    /// </summary>
    public bool IsRealHardware => true;

    /// <summary>
    /// 设置输出通道状态。
    /// </summary>
    /// <param name="output">输出通道名称（27V、HART、PA、ELE）。</param>
    /// <param name="open">true 为打开，false 为关闭。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<bool> SetOutputAsync(string output, bool open, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        var state = open ? OpenCloseState.Open : OpenCloseState.Close;
        var result = output.ToUpperInvariant() switch
        {
            "27V" => _tool!.SetY1SwitchState(state, 0),
            "HART" => _tool!.SetY2SwitchState(state, 0),
            "PA" => _tool!.SetY3SwitchState(state, 0),
            "ELE" or "ELECTRICAL" => _tool!.SetY4SwitchState(state, 0),
            _ => throw new ArgumentException($"未知输出通道: {output}")
        };
        if (!result.IsCorrect)
        {
            throw new InvalidOperationException($"设置输出通道 {output} 失败");
        }
        _logger.LogDebug("设置输出通道 {Output} 为 {State}", output, open ? "打开" : "关闭");
        await Task.CompletedTask;
        return true;
    }

    /// <summary>
    /// 获取输出通道状态。
    /// </summary>
    /// <param name="output">输出通道名称（27V、HART、PA、ELE）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>true 表示打开，false 表示关闭。</returns>
    public async Task<bool> GetOutputStateAsync(string output, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        var result = output.ToUpperInvariant() switch
        {
            "27V" => _tool!.GetY1SwitchState(),
            "HART" => _tool!.GetY2SwitchState(),
            "PA" => _tool!.GetY3SwitchState(),
            "ELE" or "ELECTRICAL" => _tool!.GetY4SwitchState(),
            _ => throw new ArgumentException($"未知输出通道: {output}")
        };
        if (!result.IsCorrect)
        {
            throw new InvalidOperationException($"获取输出通道 {output} 状态失败");
        }
        var isOpen = result.Result == OpenCloseState.Open;
        _logger.LogDebug("输出通道 {Output} 状态: {State}", output, isOpen ? "打开" : "关闭");
        await Task.CompletedTask;
        return isOpen;
    }

    /// <summary>
    /// 读取电压（工装不支持，返回 0）。
    /// </summary>
    public async Task<double> ReadVoltageAsync(int channel, CancellationToken ct = default)
    {
        _logger.LogWarning("ConST811A 工装不支持电压读取");
        return await Task.FromResult(0d);
    }

    /// <summary>
    /// 读取电流（工装不支持，返回 0）。
    /// </summary>
    public async Task<double> ReadCurrentAsync(int channel, CancellationToken ct = default)
    {
        _logger.LogWarning("ConST811A 工装不支持电流读取");
        return await Task.FromResult(0d);
    }
    #endregion

    #region IStandardModule 其他方法
    /// <summary>
    /// 读取工装序列号（不支持，返回空字符串）。
    /// </summary>
    public async Task<string> GetSerialNumberAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("ConST811A 工装不支持读取序列号");
        return await Task.FromResult(string.Empty);
    }

    /// <summary>
    /// 读取工装版本号（不支持，返回空字符串）。
    /// </summary>
    public async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("ConST811A 工装不支持读取版本号");
        return await Task.FromResult(string.Empty);
    }

    /// <summary>
    /// 设置压力类型（工装不支持，返回 false）。
    /// </summary>
    public async Task<bool> SetPressureTypeAsync(string pressureType, CancellationToken ct = default)
    {
        _logger.LogWarning("ConST811A 工装不支持设置压力类型");
        return await Task.FromResult(false);
    }

    /// <summary>
    /// 读取标准压力（工装不支持，返回 0）。
    /// </summary>
    public async Task<double> GetPressureKpaAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("ConST811A 工装不支持读取压力");
        return await Task.FromResult(0d);
    }

    /// <summary>
    /// 读取模块温度（工装不支持，返回 0）。
    /// </summary>
    public async Task<double> GetTemperatureAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("ConST811A 工装不支持读取温度");
        return await Task.FromResult(0d);
    }

    /// <summary>
    /// 复位工装（不支持，返回 false）。
    /// </summary>
    public async Task<bool> ResetAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("ConST811A 工装不支持复位");
        return await Task.FromResult(false);
    }
    #endregion

    #region 辅助方法
    /// <summary>
    /// 惰性连接：未连接时按号位端点自动连接（幂等），失败抛异常。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    private async Task EnsureConnectedAsync(CancellationToken ct = default)
    {
        if (!_connected || _tool == null)
        {
            await ConnectAsync(ct);
        }
    }
    #endregion
}