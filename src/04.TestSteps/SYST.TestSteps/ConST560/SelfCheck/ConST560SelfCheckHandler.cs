using SYST.Core.Abstractions;
using SYST.Devices.Abstractions.Dut;

namespace SYST.TestSteps.ConST560.SelfCheck;

/// <summary>
/// ConST560 手持校验仪整机自检处理器（按步骤 Key 逐项分发）。
/// PORT: 旧平台 E05 ConST560_SelfCheck.json 37 步业务。
/// 清单中每个步骤（ConST560_BenchPreparation ~ ConST560_TestComplete）经 <see cref="ExecuteAsync"/>
/// 分发到对应的私有方法独立执行、独立报告，使用 ZQWL 继电器矩阵控制齿轮箱工装完成整机自检。
/// </summary>
public sealed class ConST560SelfCheckHandler : IStepHandler
{
    public string Kind => "ConST560SelfCheck";
    public string? DeviceFamily => "ConST560_SelfCheck_Machine";

    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST560Ops(ctx, ct);
        return ctx.Step.Key switch
        {
            "ConST560_BenchPreparation" => await BenchPreparationAsync(op, ct),
            "ConST560_PowerOnTime" => await PowerOnTimeAsync(op, ct),
            "ConST560_TimeDate" => await TimeDateAsync(op, ct),
            "ConST560_SNNumberWrite" => await SnNumberWriteAsync(op, ctx, ct),
            "ConST560_TypeWrite" => await TypeWriteAsync(op, ct),
            "ConST560_VersionThisVerify" => await VersionThisVerifyAsync(op, ct),
            "ConST560_ScreenBadPoint" => await ScreenBadPointAsync(op, ct),
            "ConST560_ScreenTouch" => await ScreenTouchAsync(op, ct),
            "ConST560_EntityKey" => await EntityKeyAsync(op, ct),
            "ConST560_SpeakerMachine" => await SpeakerMachineAsync(op, ct),
            "ConST560_ScreenBrightDegreeConfirmNeedTest_IfInternalRangeOrderNeedRedoCancel" => await ScreenBrightnessAsync(op, ct),
            "ConST560_WIFI" => await WifiAsync(op, ct),
            "ConST560_Bluetooth" => await BluetoothAsync(op, ct),
            "ConST560_OuterPressureModuleComm" => await OuterPressureModuleAsync(op, ct),
            "ConST560_MainBoardSelfCheckInfo" => await MainBoardSelfCheckInfoAsync(op, ct),
            "ConST560_CircuitVoltage" => await CircuitVoltageAsync(op, ct),
            "ConST560_HartInnerSourceInnerResistanceComm" => await ResistanceCommAsync(op, "ConST560_HartInnerSourceInnerResistanceComm", "Hart内源内阻通讯", ct),
            "ConST560_HartOuterSourceOuterResistanceComm" => await ResistanceCommAsync(op, "ConST560_HartOuterSourceOuterResistanceComm", "Hart外源外阻通讯", ct),
            "ConST560_HartOuterSourceInnerResistanceComm" => await ResistanceCommAsync(op, "ConST560_HartOuterSourceInnerResistanceComm", "Hart外源内阻通讯", ct),
            "ConST560_FFOuterSourceOuterResistanceComm" => await ResistanceCommAsync(op, "ConST560_FFOuterSourceOuterResistanceComm", "FF外源外阻通讯", ct),
            "ConST560_FFInnerSourceInnerResistanceComm" => await ResistanceCommAsync(op, "ConST560_FFInnerSourceInnerResistanceComm", "FF内源内阻通讯", ct),
            "ConST560_PAOuterSourceOuterResistanceComm" => await ResistanceCommAsync(op, "ConST560_PAOuterSourceOuterResistanceComm", "PA外源外阻通讯", ct),
            "ConST560_mAOutputOutFunction_SRC" => await MAOutputSrcAsync(op, ct),
            "ConST560_mAOutputOutFunction_SINK" => await MAOutputSinkAsync(op, ct),
            "ConST560_mAMeasureFunction" => await MAMeasureAsync(op, ct),
            "ConST560_OverCurrentTest" => await OverCurrentAsync(op, ct),
            "ConST560_VMeasureFunction" => await VMeasureAsync(op, ct),
            "ConST560_OverPressureTest" => await OverPressureAsync(op, ct),
            "ConST560_ChargeElectricityStatusTest" => await ChargeStatusAsync(op, ct),
            "ConST560_WholeMachineConsumeTest" => await WholeMachineConsumeAsync(op, ct),
            "ConST560_ChargeElectricityTest" => await ChargeElectricityAsync(op, ct),
            "ConST560_TestComplete" => await TestCompleteAsync(op, ct),
            _ => StepResult.Error($"未知的 ConST560 测试步骤：{ctx.Step.Key}"),
        };
    }

    /// <summary>1. 工装准备：检查被检连接。</summary>
    private static async Task<StepResult> BenchPreparationAsync(ConST560Ops op, CancellationToken ct)
    {
        ctx_Report(op, "开始 ConST560 整机自检...");
        if (!await op.TryCommand(() => Task.FromResult(op.Dut.IsConnected), "工装准备检查"))
        {
            op.Fail("工装准备失败");
            return StepResult.Fail("工装准备未通过：被检未就绪");
        }
        op.Ok("工装准备通过");
        return StepResult.Pass("工装准备通过");
    }

    /// <summary>2. 开机时间测试：等待设备开机稳定并确认连接。</summary>
    private static async Task<StepResult> PowerOnTimeAsync(ConST560Ops op, CancellationToken ct)
    {
        await op.Sleep(300, "等待设备开机稳定");
        if (!op.Dut.IsConnected)
        {
            op.Fail("设备开机后未连接");
            return StepResult.Fail("开机时间测试未通过：设备开机后未连接");
        }
        op.Ok("开机时间测试通过");
        return StepResult.Pass("开机时间测试通过");
    }

    /// <summary>3. 日期时间测试。</summary>
    private static async Task<StepResult> TimeDateAsync(ConST560Ops op, CancellationToken ct)
    {
        var dateTime = await op.Dut.QueryTextAsync("GetDateTime", null, ct);
        op.Text("日期时间", dateTime);
        if (string.IsNullOrWhiteSpace(dateTime))
        {
            op.Fail("读取日期时间失败");
            return StepResult.Fail("日期时间测试未通过");
        }
        op.Ok("日期时间测试通过");
        return StepResult.Pass("日期时间测试通过", dateTime);
    }

    /// <summary>4. 序列号写入。</summary>
    private static async Task<StepResult> SnNumberWriteAsync(ConST560Ops op, ITestContext ctx, CancellationToken ct)
    {
        var sn = ctx.SerialNumber ?? "";
        if (string.IsNullOrWhiteSpace(sn))
        {
            sn = await op.Dut.ReadSerialNumberAsync(ct);
        }
        if (string.IsNullOrWhiteSpace(sn))
        {
            op.Fail("序列号读取失败");
            return StepResult.Fail("序列号写入未通过");
        }
        op.Text("序列号", sn);
        ctx.SerialNumber = sn;
        op.Ok("序列号写入通过");
        return StepResult.Pass("序列号写入通过", sn);
    }

    /// <summary>5. 设备类型写入。</summary>
    private static async Task<StepResult> TypeWriteAsync(ConST560Ops op, CancellationToken ct)
    {
        var deviceType = await op.Dut.QueryTextAsync("GetDeviceType", null, ct);
        op.Text("设备类型", deviceType);
        if (string.IsNullOrWhiteSpace(deviceType))
        {
            op.Fail("设备类型读取失败");
            return StepResult.Fail("设备类型写入未通过");
        }
        op.Ok("设备类型写入通过");
        return StepResult.Pass("设备类型写入通过", deviceType);
    }

    /// <summary>6. 版本验证。</summary>
    private static async Task<StepResult> VersionThisVerifyAsync(ConST560Ops op, CancellationToken ct)
    {
        var fw = await op.Dut.ReadFirmwareVersionAsync(ct);
        op.Text("固件版本", fw);
        if (string.IsNullOrWhiteSpace(fw))
        {
            op.Fail("固件版本读取失败");
            return StepResult.Fail("版本验证未通过");
        }
        op.Ok("版本验证通过");
        return StepResult.Pass("版本验证通过", fw);
    }

    /// <summary>7. 屏幕坏点测试。</summary>
    private static async Task<StepResult> ScreenBadPointAsync(ConST560Ops op, CancellationToken ct)
    {
        if (!await op.TryCommand(async () => { await op.Dut.CommandAsync("StartLCDBadPixelTest", null, ct); return true; }, "屏幕坏点测试"))
        {
            op.Fail("屏幕坏点测试失败");
            return StepResult.Fail("屏幕坏点测试未通过");
        }
        await op.Sleep(2000, "等待屏幕测试完成");
        op.Ok("屏幕坏点测试通过");
        return StepResult.Pass("屏幕坏点测试通过");
    }

    /// <summary>8. 触摸屏测试。</summary>
    private static async Task<StepResult> ScreenTouchAsync(ConST560Ops op, CancellationToken ct)
    {
        if (!await op.TryCommand(() => op.Dut.QueryBooleanAsync("TestTouchScreen", null, ct), "触摸屏测试"))
        {
            op.Fail("触摸屏测试失败");
            return StepResult.Fail("触摸屏测试未通过");
        }
        op.Ok("触摸屏测试通过");
        return StepResult.Pass("触摸屏测试通过");
    }

    /// <summary>9. 实体按键测试。</summary>
    private static async Task<StepResult> EntityKeyAsync(ConST560Ops op, CancellationToken ct)
    {
        if (!await op.TryCommand(() => op.Dut.QueryBooleanAsync("TestEntityKeys", null, ct), "实体按键测试"))
        {
            op.Fail("实体按键测试失败");
            return StepResult.Fail("实体按键测试未通过");
        }
        op.Ok("实体按键测试通过");
        return StepResult.Pass("实体按键测试通过");
    }

    /// <summary>10. 扬声器测试。</summary>
    private static async Task<StepResult> SpeakerMachineAsync(ConST560Ops op, CancellationToken ct)
    {
        if (!await op.TryCommand(async () => { await op.Dut.CommandAsync("TestSpeaker", null, ct); return true; }, "扬声器测试"))
        {
            op.Fail("扬声器测试失败");
            return StepResult.Fail("扬声器测试未通过");
        }
        op.Ok("扬声器测试通过");
        return StepResult.Pass("扬声器测试通过");
    }

    /// <summary>11. 屏幕亮度确认。</summary>
    private static async Task<StepResult> ScreenBrightnessAsync(ConST560Ops op, CancellationToken ct)
    {
        if (!await op.TryCommand(() => op.Dut.QueryBooleanAsync("TestScreenBrightness", null, ct), "屏幕亮度测试"))
        {
            op.Fail("屏幕亮度测试失败");
            return StepResult.Fail("屏幕亮度测试未通过");
        }
        op.Ok("屏幕亮度测试通过");
        return StepResult.Pass("屏幕亮度测试通过");
    }

    /// <summary>12. WIFI 测试。</summary>
    private static async Task<StepResult> WifiAsync(ConST560Ops op, CancellationToken ct)
    {
        var wifiOk = await op.Dut.QueryBooleanAsync("TestWifi", new[] { "CONSTSC", "WPA_PSK_AES", "4001131199" }, ct);
        op.Text("WIFI", wifiOk ? "通过" : "不通过");
        if (!wifiOk)
        {
            op.Fail("WIFI 测试失败");
            return StepResult.Fail("WIFI测试未通过");
        }
        op.Ok("WIFI 测试通过");
        return StepResult.Pass("WIFI测试通过");
    }

    /// <summary>13. 蓝牙测试。</summary>
    private static async Task<StepResult> BluetoothAsync(ConST560Ops op, CancellationToken ct)
    {
        var btOk = await op.Dut.QueryBooleanAsync("TestBluetooth", null, ct);
        op.Text("蓝牙", btOk ? "通过" : "不通过");
        if (!btOk)
        {
            op.Fail("蓝牙测试失败");
            return StepResult.Fail("蓝牙测试未通过");
        }
        op.Ok("蓝牙测试通过");
        return StepResult.Pass("蓝牙测试通过");
    }

    /// <summary>14. 外部压力模块通讯。</summary>
    private static async Task<StepResult> OuterPressureModuleAsync(ConST560Ops op, CancellationToken ct)
    {
        if (!await op.TryCommand(() => op.Dut.QueryBooleanAsync("TestOuterPressureModule", null, ct), "外部压力模块通讯"))
        {
            op.Fail("外部压力模块通讯失败");
            return StepResult.Fail("外部压力模块通讯未通过");
        }
        op.Ok("外部压力模块通讯通过");
        return StepResult.Pass("外部压力模块通讯通过");
    }

    /// <summary>15. 主板自检信息。</summary>
    private static async Task<StepResult> MainBoardSelfCheckInfoAsync(ConST560Ops op, CancellationToken ct)
    {
        var mainBoardInfo = await op.Dut.QueryTextAsync("GetMainBoardSelfCheckInfo", null, ct);
        op.Text("主板自检", mainBoardInfo);
        if (string.IsNullOrWhiteSpace(mainBoardInfo))
        {
            op.Fail("主板自检信息读取失败");
            return StepResult.Fail("主板自检信息未通过");
        }
        op.Ok("主板自检信息通过");
        return StepResult.Pass("主板自检信息通过", mainBoardInfo);
    }

    /// <summary>16. 电路电压测试。</summary>
    private static async Task<StepResult> CircuitVoltageAsync(ConST560Ops op, CancellationToken ct)
    {
        var voltageOk = await op.Dut.QueryBooleanAsync("TestCircuitVoltage", null, ct);
        op.Text("电路电压", voltageOk ? "通过" : "不通过");
        if (!voltageOk)
        {
            op.Fail("电路电压测试失败");
            return StepResult.Fail("电路电压测试未通过");
        }
        op.Ok("电路电压测试通过");
        return StepResult.Pass("电路电压测试通过");
    }

    /// <summary>17-22. Hart/FF/PA 内外源内外阻通讯测试（方法名与步骤 Key 一致）。</summary>
    private static async Task<StepResult> ResistanceCommAsync(ConST560Ops op, string entry, string name, CancellationToken ct)
    {
        if (!await op.TryCommand(() => op.Dut.QueryBooleanAsync(entry, null, ct), name))
        {
            op.Fail($"{name}失败");
            return StepResult.Fail($"{name}未通过");
        }
        op.Ok($"{name}通过");
        return StepResult.Pass($"{name}通过");
    }

    /// <summary>23. mA 输出源功能测试。</summary>
    private static async Task<StepResult> MAOutputSrcAsync(ConST560Ops op, CancellationToken ct)
    {
        if (!await op.TryCommand(() => op.Dut.QueryBooleanAsync("TestMAOutputSrc", null, ct), "mA输出源功能测试"))
        {
            op.Fail("mA输出源功能测试失败");
            return StepResult.Fail("mA输出源功能测试未通过");
        }
        op.Ok("mA输出源功能测试通过");
        return StepResult.Pass("mA输出源功能测试通过");
    }

    /// <summary>24. mA 输出沉功能测试。</summary>
    private static async Task<StepResult> MAOutputSinkAsync(ConST560Ops op, CancellationToken ct)
    {
        if (!await op.TryCommand(() => op.Dut.QueryBooleanAsync("TestMAOutputSink", null, ct), "mA输出沉功能测试"))
        {
            op.Fail("mA输出沉功能测试失败");
            return StepResult.Fail("mA输出沉功能测试未通过");
        }
        op.Ok("mA输出沉功能测试通过");
        return StepResult.Pass("mA输出沉功能测试通过");
    }

    /// <summary>25. mA 测量功能。</summary>
    private static async Task<StepResult> MAMeasureAsync(ConST560Ops op, CancellationToken ct)
    {
        if (!await op.TryCommand(() => op.Dut.QueryBooleanAsync("TestMAMeasure", null, ct), "mA测量功能测试"))
        {
            op.Fail("mA测量功能测试失败");
            return StepResult.Fail("mA测量功能测试未通过");
        }
        op.Ok("mA测量功能测试通过");
        return StepResult.Pass("mA测量功能测试通过");
    }

    /// <summary>26. 过流测试。</summary>
    private static async Task<StepResult> OverCurrentAsync(ConST560Ops op, CancellationToken ct)
    {
        if (!await op.TryCommand(() => op.Dut.QueryBooleanAsync("TestOverCurrent", null, ct), "过流测试"))
        {
            op.Fail("过流测试失败");
            return StepResult.Fail("过流测试未通过");
        }
        op.Ok("过流测试通过");
        return StepResult.Pass("过流测试通过");
    }

    /// <summary>27. 电压测量功能。</summary>
    private static async Task<StepResult> VMeasureAsync(ConST560Ops op, CancellationToken ct)
    {
        if (!await op.TryCommand(() => op.Dut.QueryBooleanAsync("TestVMeasure", null, ct), "电压测量功能测试"))
        {
            op.Fail("电压测量功能测试失败");
            return StepResult.Fail("电压测量功能测试未通过");
        }
        op.Ok("电压测量功能测试通过");
        return StepResult.Pass("电压测量功能测试通过");
    }

    /// <summary>28. 过压测试。</summary>
    private static async Task<StepResult> OverPressureAsync(ConST560Ops op, CancellationToken ct)
    {
        if (!await op.TryCommand(() => op.Dut.QueryBooleanAsync("TestOverPressure", null, ct), "过压测试"))
        {
            op.Fail("过压测试失败");
            return StepResult.Fail("过压测试未通过");
        }
        op.Ok("过压测试通过");
        return StepResult.Pass("过压测试通过");
    }

    /// <summary>29. 充电状态测试。</summary>
    private static async Task<StepResult> ChargeStatusAsync(ConST560Ops op, CancellationToken ct)
    {
        if (!await op.TryCommand(() => op.Dut.QueryBooleanAsync("TestChargeStatus", null, ct), "充电状态测试"))
        {
            op.Fail("充电状态测试失败");
            return StepResult.Fail("充电状态测试未通过");
        }
        op.Ok("充电状态测试通过");
        return StepResult.Pass("充电状态测试通过");
    }

    /// <summary>30. 整机耗电测试。</summary>
    private static async Task<StepResult> WholeMachineConsumeAsync(ConST560Ops op, CancellationToken ct)
    {
        if (!await op.TryCommand(() => op.Dut.QueryBooleanAsync("TestWholeMachineConsume", null, ct), "整机耗电测试"))
        {
            op.Fail("整机耗电测试失败");
            return StepResult.Fail("整机耗电测试未通过");
        }
        op.Ok("整机耗电测试通过");
        return StepResult.Pass("整机耗电测试通过");
    }

    /// <summary>31. 充电测试。</summary>
    private static async Task<StepResult> ChargeElectricityAsync(ConST560Ops op, CancellationToken ct)
    {
        if (!await op.TryCommand(() => op.Dut.QueryBooleanAsync("TestChargeElectricity", null, ct), "充电测试"))
        {
            op.Fail("充电测试失败");
            return StepResult.Fail("充电测试未通过");
        }
        op.Ok("充电测试通过");
        return StepResult.Pass("充电测试通过");
    }

    /// <summary>32. 测试完成确认：完成指令 + ZQWL 继电器矩阵自检 + 被检自检指令 + 补充连接。</summary>
    private static async Task<StepResult> TestCompleteAsync(ConST560Ops op, CancellationToken ct)
    {
        var pass = true;

        // 测试完成确认
        if (!await op.TryCommand(async () => { await op.Dut.CommandAsync("TestComplete", null, ct); return true; }, "测试完成确认"))
        {
            pass = false;
            op.Fail("测试完成确认失败");
        }

        // ZQWL 继电器矩阵自检（地址 1-3 × 通道 1-16 吸合/断开回读）
        var zqwlPass = true;
        for (var a = 1; a <= 3; a++)
        {
            for (var ch = 1; ch <= 16; ch++)
            {
                if (!await op.TryCommand(() => op.ZQWL.SetChannelAsync(a, ch, true, ct), $"ZQWL 地址{a}通道{ch}吸合"))
                {
                    zqwlPass = false;
                }
                await Task.Delay(50, ct);
                if (!await op.ZQWL.GetChannelStateAsync(a, ch, ct))
                {
                    op.Fail($"ZQWL 地址{a}通道{ch}状态读回不一致");
                    zqwlPass = false;
                }
                if (!await op.TryCommand(() => op.ZQWL.SetChannelAsync(a, ch, false, ct), $"ZQWL 地址{a}通道{ch}断开"))
                {
                    zqwlPass = false;
                }
                await Task.Delay(50, ct);
                if (await op.ZQWL.GetChannelStateAsync(a, ch, ct))
                {
                    op.Fail($"ZQWL 地址{a}通道{ch}断开后状态仍为吸合");
                    zqwlPass = false;
                }
            }
        }
        if (zqwlPass)
        {
            op.Ok("ZQWL 继电器矩阵自检通过");
        }
        else
        {
            pass = false;
            op.Fail("ZQWL 继电器矩阵自检不通过");
        }

        // 被检自检指令
        if (!await op.TryCommand(() => op.Dut.QueryBooleanAsync("SetSelfCheck", null, ct), "执行自检指令"))
        {
            pass = false;
        }
        var selfCheckOk = await op.Dut.QueryBooleanAsync("GetSelfCheck", null, ct);
        op.Text("自检状态", selfCheckOk ? "通过" : "不通过");
        if (!selfCheckOk)
        {
            pass = false;
            op.Fail("自检指令返回不通过");
        }

        // 补充连接
        if (!await op.Dut.ReplenishLinkAsync(ct))
        {
            pass = false;
            op.Fail("补充连接失败");
        }

        return pass ? StepResult.Pass("ConST560 整机自检通过") : StepResult.Fail("ConST560 整机自检不通过");
    }

    private static void ctx_Report(ConST560Ops op, string message)
        => op.Report(message);
}
