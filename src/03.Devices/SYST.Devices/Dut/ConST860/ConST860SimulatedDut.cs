using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;
using SYST.Devices.Abstractions.Dut;

namespace SYST.Devices.Dut.ConST860;

/// <summary>
/// ConST860（P25 气压/液压整机）被检**仿真驱动**（真机开关关时经
/// <see cref="DutDriverRegistry"/> 自动选用本变体）。实现 <see cref="IConST860PressureQBase"/>（含全部基础 +
/// 气压扩展），并额外实现 <see cref="IConST860PressureYGbk"/> 的液压扩展——仿真模式下两种变体都能完整跑通。
/// 真机驱动可按变体分别实现：Q 驱动实现 IConST860PressureQBase、Y 驱动实现 IConST860PressureYGbk。
/// </summary>
[DutDriver("ConST860", IsSimulation = true)]
public sealed class ConST860SimulatedDut : IConST860PressureQBase, IConST860PressureYGbk
{
    /// <summary>日志。</summary>
    private readonly ILogger _logger;

    /// <summary>仿真随机源。</summary>
    private readonly Random _rng = new();

    private double _pumpRpm;
    private double _outputPressureKpa = 101.3;   // 默认一个大气压附近
    private double _targetPressureKpa;
    private int _currentRangeIndex = 1;
    private string _medium = "空气";
    private bool _externalLoopActive;
    private double _pumpSpeedPercent = 50;

    /// <summary>量程列表模拟数据（索引:下限~上限 kPa）。</summary>
    private static readonly IReadOnlyList<string> SimulatedRanges = new List<string>
    {
        "1:-100~100",
        "2:-250~250",
        "3:-600~600",
        "4:-1000~1000",
        "5:-2500~2500",
        "21:-100~100",
        "22:-250~250",
    };

    /// <summary>
    /// 用设备描述符构造仿真被检。
    /// </summary>
    /// <param name="descriptor">设备描述符。</param>
    /// <param name="logger">日志。</param>
    public ConST860SimulatedDut(DeviceDescriptor descriptor, ILogger logger)
    {
        Key = descriptor.Model;
        Model = descriptor.Model;
        _logger = logger;
    }

    /// <summary>设备键。</summary>
    public string Key { get; }

    /// <summary>设备型号名。</summary>
    public string Model { get; }

    /// <summary>是否已连接。</summary>
    public bool IsConnected { get; private set; }

    // ==================== IDutDevice 基础成员 ====================

    /// <summary>仿真连接。</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await Task.Delay(30, ct);
        IsConnected = true;
        _logger.LogInformation("ConST860 仿真连接成功");
    }

    /// <summary>仿真读序列号（型号 + 时间戳）。</summary>
    public Task<string> ReadSerialNumberAsync(CancellationToken ct = default)
        => Task.FromResult($"{Model}{DateTime.Now:yyMMddHHmmss}");

    /// <summary>仿真读固件版本。</summary>
    public Task<string> ReadFirmwareVersionAsync(CancellationToken ct = default)
        => Task.FromResult("V1.0.5");

    /// <summary>仿真写初始信息（仅记录日志）。</summary>
    public Task WriteInitInfoAsync(string boardType, CancellationToken ct = default)
    {
        _logger.LogInformation("ConST860 写入初始信息：{Type}", boardType);
        return Task.CompletedTask;
    }

    /// <summary>仿真读某测量点。</summary>
    public Task<double> MeasureAsync(string point, CancellationToken ct = default)
        => Task.FromResult(point switch
        {
            "PumpRpm" => 1500 + _rng.NextDouble() * 100,
            "OutputPressure" => _outputPressureKpa + _rng.NextDouble() * 2 - 1,
            _ => _rng.NextDouble(),
        });

    /// <summary>设置被检序列号（仿真直接成功）。</summary>
    public Task<bool> SetSerialNumberAsync(string serialNumber, CancellationToken ct = default)
    {
        _logger.LogInformation("ConST860 设置序列号：{SN}", serialNumber);
        return Task.FromResult(true);
    }

    /// <summary>设置产品型号/主设备类型（仿真直接成功）。</summary>
    public Task<bool> SetPrimaryDeviceTypeAsync(string deviceType, CancellationToken ct = default)
    {
        _logger.LogInformation("ConST860 设置产品型号：{Type}", deviceType);
        return Task.FromResult(true);
    }

    /// <summary>通用布尔查询（按方法名返回让自检通过的标称结果）。</summary>
    public Task<bool> QueryBooleanAsync(string method, object? arg, CancellationToken ct = default)
    {
        _logger.LogDebug("ConST860 QueryBoolean: {Method}", method);
        return Task.FromResult(method switch
        {
            // 屏/蜂鸣器/风扇等 Checker 类自检轮询直接返回通过
            "GetCheckerState" => true,
            "SetCheckerOpen" => true,
            "SetCheckerClose" => true,
            "SetCheckerSelect" => true,
            // 自整定状态：完成
            "GetSelfTuningState" => true,
            // 泵/阀门开关类指令一律成功
            _ => true,
        });
    }

    /// <summary>通用文本查询（按方法名返回合理文本）。</summary>
    public Task<string> QueryTextAsync(string method, object? arg, CancellationToken ct = default)
    {
        _logger.LogDebug("ConST860 QueryText: {Method}", method);
        return Task.FromResult(method switch
        {
            "GetStaticETHemetIPAddress" => "192.168.40.110",
            "GetStorageCardState" => "1",
            "GetUSBStorageState" => "1",
            "GetCheckerState" => "TestPass",
            "GetSelfCheckException" => "",
            "GetMedium" => _medium,
            "GetDevType" => Model,
            _ => "OK",
        });
    }

    /// <summary>通用指令执行（仅记录日志）。</summary>
    public Task CommandAsync(string method, object? arg, CancellationToken ct = default)
    {
        _logger.LogDebug("ConST860 Command: {Method}", method);
        return Task.CompletedTask;
    }

    /// <summary>释放。</summary>
    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }

    // ==================== IConST860Dut 基础能力 ====================

    /// <summary>补充连接（重连，仿真直接成功）。</summary>
    public Task<bool> ReplenishLinkAsync(CancellationToken ct = default)
    {
        IsConnected = true;
        return Task.FromResult(true);
    }

    /// <summary>设备自检（公共抓手/电平，仿真返回正常）。</summary>
    public Task<bool> SelfCheckAsync(CancellationToken ct = default) => Task.FromResult(true);

    /// <summary>获取泵的实时转速 rpm（气泵或液泵）。</summary>
    public Task<double> GetPumpRpmAsync(CancellationToken ct = default)
    {
        _pumpRpm = 1500 + _rng.NextDouble() * 100 - 50;
        return Task.FromResult(_pumpRpm);
    }

    /// <summary>充能（蓄能器充电）。'#' 表示快速充满到 10 MPa。</summary>
    public Task<double> ChargeAsync(char target, CancellationToken ct = default)
    {
        _outputPressureKpa = target switch
        {
            '#' => 10000,
            'H' => 5000,
            'L' => 2500,
            _ => 3000,
        };
        _logger.LogInformation("ConST860 充能至 {Pressure}kPa", _outputPressureKpa);
        return Task.FromResult(_outputPressureKpa);
    }

    /// <summary>测量泄漏（返回小泄漏值 kPa/min，让判定条件通过）。</summary>
    public Task<double> MeasureLeakAsync(CancellationToken ct = default)
        => Task.FromResult(0.1 + _rng.NextDouble() * 0.05);

    /// <summary>关闭维修泄压阀（压力归零）。</summary>
    public Task CloseRepairVentAsync(CancellationToken ct = default)
    {
        _outputPressureKpa = 0;
        return Task.CompletedTask;
    }

    /// <summary>写入介质类型（气/油/水）。</summary>
    public Task<bool> SetMediumAsync(string medium, CancellationToken ct = default)
    {
        _medium = medium;
        _logger.LogInformation("ConST860 写入介质：{Medium}", medium);
        return Task.FromResult(true);
    }

    // ==================== IConST860PressureQBase 气压扩展 ====================

    /// <summary>设置计量模式（GW/GW4 等）。</summary>
    public Task<bool> SetMeasureModeAsync(string mode, CancellationToken ct = default)
    {
        _logger.LogInformation("ConST860(Q) 设置计量模式：{Mode}", mode);
        return Task.FromResult(true);
    }

    /// <summary>启动/停止自整定。</summary>
    public Task<bool> SelfTuningAsync(bool start, CancellationToken ct = default)
    {
        _logger.LogInformation("ConST860(Q) 自整定 {Action}", start ? "启动" : "停止");
        return Task.FromResult(true);
    }

    /// <summary>读取自整定结果。</summary>
    public Task<string> ReadSelfTuningResultAsync(CancellationToken ct = default)
        => Task.FromResult("OK:Kp=1.0,Ki=0.1,Kd=0.01");

    /// <summary>获取当前输出压力 kPa（带 ±1% 波动）。</summary>
    public Task<double> ReadOutputPressureAsync(CancellationToken ct = default)
    {
        var noise = _outputPressureKpa * 0.01 * (_rng.NextDouble() * 2 - 1);
        return Task.FromResult(_outputPressureKpa + noise);
    }

    /// <summary>读取 PV/SV 显示值。</summary>
    public Task<(double PV, double SV)> ReadPvSvAsync(CancellationToken ct = default)
        => Task.FromResult((_outputPressureKpa + _rng.NextDouble() * 2 - 1, _targetPressureKpa));

    /// <summary>设置目标压力值 kPa（输出跟随目标的 95%）。</summary>
    public Task<bool> SetTargetPressureAsync(double pressureKpa, CancellationToken ct = default)
    {
        _targetPressureKpa = pressureKpa;
        _outputPressureKpa = pressureKpa * 0.95;
        return Task.FromResult(true);
    }

    /// <summary>读取量程列表。</summary>
    public Task<IReadOnlyList<string>> GetRangeListAsync(CancellationToken ct = default)
        => Task.FromResult(SimulatedRanges);

    /// <summary>切换量程。</summary>
    public Task<bool> SetCurrentRangeAsync(int rangeIndex, CancellationToken ct = default)
    {
        _currentRangeIndex = rangeIndex;
        return Task.FromResult(true);
    }

    /// <summary>读取当前量程索引。</summary>
    public Task<int> GetCurrentRangeAsync(CancellationToken ct = default)
        => Task.FromResult(_currentRangeIndex);

    // ==================== IConST860PressureYGbk 液压扩展 ====================

    /// <summary>读取外循环状态。</summary>
    public Task<bool> GetExternalLoopStateAsync(CancellationToken ct = default)
        => Task.FromResult(_externalLoopActive);

    /// <summary>启动/停止液源模块校准。</summary>
    public Task<bool> RunCalibrationAsync(bool start, CancellationToken ct = default)
    {
        _logger.LogInformation("ConST860(Y) 液源校准 {Action}", start ? "启动" : "停止");
        return Task.FromResult(true);
    }

    /// <summary>执行泵效率测试（返回 85%~95%）。</summary>
    public Task<double> PumpEfficiencyTestAsync(CancellationToken ct = default)
        => Task.FromResult(85 + _rng.NextDouble() * 10);

    /// <summary>液泵电机速率控制（0~100%），转速随之变化。</summary>
    public Task<bool> SetPumpSpeedAsync(int percentage, CancellationToken ct = default)
    {
        _pumpSpeedPercent = Math.Clamp(percentage, 0, 100);
        _pumpRpm = 3000 * (_pumpSpeedPercent / 100.0);
        return Task.FromResult(true);
    }

    /// <summary>快速充满控制板（资阳 D05 专用）。</summary>
    public Task<bool> ChargeControlBoardAsync(string valve, CancellationToken ct = default)
    {
        _logger.LogInformation("ConST860(Y) 快充控制板阀：{Valve}", valve);
        return Task.FromResult(true);
    }
}
