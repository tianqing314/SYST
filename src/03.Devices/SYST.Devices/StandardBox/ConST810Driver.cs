using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;
using SYST.Devices.Dut;
using Xmas11.Comm.Devices;

namespace SYST.Devices.StandardBox;

/// <summary>
/// ConST810 标准模块驱动（P06）。
/// 通过 USB HID 与 HPC 设备通讯，提供电压/电流测量功能。
/// 仅适用于差压版和微差压版 ConST811A；表绝压版和气象版不使用此设备。
/// </summary>
[DutDriver("ConST810")]
internal sealed class ConST810Driver : IMachineTestTool
{
    private readonly DeviceDescriptor _descriptor;
    private readonly ILogger _logger;
    private HPCBase? _hpc;
    private bool _connected;

    public ConST810Driver(DeviceDescriptor descriptor, ILogger logger)
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
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_connected) return;

        try
        {
            // ConST810 通过 USB HID 连接，Vid=11801, Pid=760
            _hpc = new HPCBase(11801, 760, string.Empty);
            _hpc.Open();

            if (!_hpc.IsExist())
            {
                throw new InvalidOperationException("ConST810 设备不存在或无法连接");
            }

            _connected = true;
            _logger.LogInformation("已连接到 ConST810 设备 {Key}", Key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接 ConST810 设备 {Key} 失败", Key);
            throw;
        }
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_hpc != null)
        {
            _hpc.Close();
            _hpc = null;
        }
        _connected = false;
        _logger.LogInformation("已断开 ConST810 设备 {Key}", Key);
        await Task.CompletedTask;
    }
    #endregion

    #region IMachineTestTool 实现
    public bool IsRealHardware => true;

    /// <summary>
    /// 设置输出通道状态（ConST810 不支持继电器输出）。
    /// </summary>
    public async Task<bool> SetOutputAsync(string output, bool open, CancellationToken ct = default)
    {
        _logger.LogWarning("ConST810 不支持继电器输出操作: {Output}", output);
        return await Task.FromResult(false);
    }

    /// <summary>
    /// 获取输出通道状态（ConST810 不支持继电器输出）。
    /// </summary>
    public async Task<bool> GetOutputStateAsync(string output, CancellationToken ct = default)
    {
        _logger.LogWarning("ConST810 不支持继电器输出状态查询: {Output}", output);
        return await Task.FromResult(false);
    }

    /// <summary>
    /// 读取电压值。切换到电压测量模式后读取。
    /// </summary>
    /// <param name="channel">通道号（ConST810 不区分通道，忽略此参数）。</param>
    public async Task<double> ReadVoltageAsync(int channel, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);

        // 切换到电压测量模式
        var switchResult = _hpc!.ChangeToMeasure_V();
        if (!switchResult.IsCorrect)
        {
            throw new InvalidOperationException("切换到电压测量模式失败");
        }

        // 读取测量值
        var result = _hpc.GetMeasureValue();
        if (!result.IsCorrect)
        {
            throw new InvalidOperationException("读取电压值失败");
        }

        var value = result.Result.Value;
        _logger.LogDebug("读取电压值: {Value} {Unit}", value, result.Result.Unit);
        await Task.CompletedTask;
        return value;
    }

    /// <summary>
    /// 读取电流值。切换到电流测量模式后读取。
    /// </summary>
    /// <param name="channel">通道号（ConST810 不区分通道，忽略此参数）。</param>
    public async Task<double> ReadCurrentAsync(int channel, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);

        // 切换到电流测量模式
        var switchResult = _hpc!.ChangeToMeasure_mA();
        if (!switchResult.IsCorrect)
        {
            throw new InvalidOperationException("切换到电流测量模式失败");
        }

        // 读取测量值
        var result = _hpc.GetMeasureValue();
        if (!result.IsCorrect)
        {
            throw new InvalidOperationException("读取电流值失败");
        }

        var value = result.Result.Value;
        _logger.LogDebug("读取电流值: {Value} {Unit}", value, result.Result.Unit);
        await Task.CompletedTask;
        return value;
    }
    #endregion

    #region IStandardModule 其他方法
    public async Task<string> GetSerialNumberAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("ConST810 不支持读取序列号");
        return await Task.FromResult(string.Empty);
    }

    public async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("ConST810 不支持读取版本号");
        return await Task.FromResult(string.Empty);
    }

    public async Task<bool> SetPressureTypeAsync(string pressureType, CancellationToken ct = default)
    {
        _logger.LogWarning("ConST810 不支持设置压力类型");
        return await Task.FromResult(false);
    }

    public async Task<double> GetPressureKpaAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("ConST810 不支持读取压力");
        return await Task.FromResult(0d);
    }

    public async Task<double> GetTemperatureAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("ConST810 不支持读取温度");
        return await Task.FromResult(0d);
    }

    public async Task<bool> ResetAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("ConST810 不支持复位");
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
        if (!_connected || _hpc == null)
        {
            await ConnectAsync(ct);
        }
    }
    #endregion
}