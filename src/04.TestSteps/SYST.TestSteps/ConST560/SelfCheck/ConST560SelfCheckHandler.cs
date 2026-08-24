using SYST.Core.Abstractions;
using SYST.Devices.Abstractions.Dut;
using SYST.Devices.Abstractions.Test;
using SYST.TestSteps.ConST560;

namespace SYST.TestSteps.ConST560.SelfCheck;

/// <summary>
/// ConST560 手持校验仪整机自检处理器（真实场景版）。
/// 依据旧平台 E05 ConST575_SelfCheck 的37步顺序，按真实接线与人机确认逐步执行。
/// 总线版本分支通过 Step.Settings["BusCategory"] 控制（E05_PAFF / E05_NoPAFF）。
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
            "BenchPreparation" => await BenchPreparationAsync(op, ctx, ct),
            "PowerOnTime" => await PowerOnTimeAsync(op, ctx, ct),
            "TimeDate" => await TimeDateAsync(op, ct),
            "SNNumberWrite" => await SnNumberWriteAsync(op, ctx, ct),
            "TypeWrite" => await TypeWriteAsync(op, ctx, ct),
            "VersionThisVerify" => await VersionVerifyAsync(op, ctx, ct),
            "ScreenBadPoint" => await CheckerTestAsync(op, ctx, "屏幕坏点", "1", "屏幕坏点测试结果", ct),
            "ScreenTouch" => await CheckerTestAsync(op, ctx, "屏幕触摸", "2", "屏幕触摸测试结果", ct),
            "EntityKey" => await CheckerTestAsync(op, ctx, "实体按键", "3", "实体按键测试结果", ct),
            "SpeakerMachine" => await SpeakerTestAsync(op, ctx, ct),
            "ScreenBrightDegreeConfirmNeedTest_IfInternalRangeOrderNeedRedoCancel" => await CheckerTestAsync(op, ctx, "屏幕亮度", "5", "屏幕亮度测试结果", ct),
            "WIFI" => await WifiTestAsync(op, ctx, ct),
            "Bluetooth" => await BluetoothTestAsync(op, ctx, ct),
            "OuterPressureModuleComm" => await OuterPressureModuleTestAsync(op, ct),
            "MainBoardSelfCheckInfo" => await MainBoardDiagAsync(op, ct),
            "CircuitVoltage" => await CircuitVoltageAsync(op, ct),
            "HartInnerSourceInnerResistanceComm" => await BusCommAsync(op, ctx, "HART", "IPIR_Transmitter", ConST560Gears.HartInnerSourceInnerResistor(), ct),
            "HartOuterSourceOuterResistanceComm" => await BusCommAsync(op, ctx, "HART", "EPER", ConST560Gears.HartOuterSourceOuterResistor(), ct),
            "HartOuterSourceInnerResistanceComm" => await BusCommAsync(op, ctx, "HART", "EPIR", ConST560Gears.HartOuterSourceInnerResistor(), ct),
            "FFOuterSourceOuterResistanceComm" => await BusCommAsync(op, ctx, "PAFF", "EPER", ConST560Gears.FFOuterSourceOuterResistor(), ct),
            "FFInnerSourceInnerResistanceComm" => await BusCommAsync(op, ctx, "PAFF", "IPIR", ConST560Gears.FFInnerSourceInnerResistor(), ct),
            "PAOuterSourceOuterResistanceComm" => await BusCommAsync(op, ctx, "PAFF", "IPIR", ConST560Gears.PAOuterSourceOuterResistor(), ct),
            "mAOutputOutFunction_SRC" => await MaOutputSrcAsync(op, ct),
            "mAOutputOutFunction_SINK" => await MaOutputSinkAsync(op, ct),
            "mAMeasureFunction" => await MaMeasureAsync(op, ct),
            "OverCurrentTest" => await OverCurrentAsync(op, ct),
            "VMeasureFunction" => await VMeasureAsync(op, ct),
            "OverPressureTest" => await OverPressureAsync(op, ct),
            "ChargeElectricityStatusTest" => await ChargeStatusAsync(op, ctx, ct),
            "WholeMachineConsumeTest" => await WholeMachineConsumeAsync(op, ct),
            "ChargeElectricityTest" => await ChargeTestAsync(op, ct),
            "TestComplete" => await TestCompleteAsync(op, ct),
            _ => StepResult.Error($"未知的 ConST560 测试步骤：{ctx.Step.Key}"),
        };
    }

    private static string BusCategory(ITestContext ctx)
        => ctx.Step.Settings.TryGetValue("BusCategory", out var v) && !string.IsNullOrWhiteSpace(v) ? v : "E05";

    private static async Task<StepResult> BenchPreparationAsync(ConST560Ops op, ITestContext ctx, CancellationToken ct)
    {
        if (!await ctx.ConfirmAsync("开始测试前，请保证被检已开过机并将USB默认通讯方式设置为串口，并保证被检右侧Type-C和航插保护口已打开并正确嵌在工装固定槽内", ct))
        {
            op.Fail("工装准备未确认");
            return StepResult.Fail("工装准备未通过：人工未确认");
        }

        await op.SwitchGearAsync(ConST560Gears.FullReset(), true);
        await op.SwitchGearAsync(ConST560Gears.ChargeInsert(), true);
        await op.SwitchGearAsync(ConST560Gears.ClampClose(), true);
        await op.SwitchGearAsync(ConST560Gears.ProbePush(), true);
        await op.Sleep(800, "探针推进");
        await op.SwitchGearAsync(ConST560Gears.ProbeRetract(), false);
        await op.SwitchGearAsync(ConST560Gears.ClampOpen(), false);
        await op.Sleep(300);
        await op.SwitchGearAsync(ConST560Gears.ClampClose(), true);
        await op.Sleep(300);
        await op.SwitchGearAsync(ConST560Gears.ProbePush(), true);
        await op.Sleep(800);
        await op.SwitchGearAsync(ConST560Gears.TypeCInsert(), true);
        await op.Sleep(800);
        await op.SwitchGearAsync(ConST560Gears.AviationPlugInsert(), true);
        await op.Sleep(800);

        if (!op.Dut.IsConnected)
        {
            op.Fail("工装准备后被检未就绪");
            return StepResult.Fail("工装准备未通过：被检未连接");
        }
        op.Ok("工装准备通过");
        return StepResult.Pass("工装准备通过");
    }

    private static async Task<StepResult> PowerOnTimeAsync(ConST560Ops op, ITestContext ctx, CancellationToken ct)
    {
        if (!await ctx.ConfirmAsync("请长按电源键3s开机，在看到屏幕点亮后点击确认", ct))
        {
            op.Fail("人工未确认开机");
            return StepResult.Fail("开机时间测试未通过：人工未确认");
        }

        var t0 = DateTime.Now;
        var ok = await op.Dut.ReplenishLinkAsync(ct);
        var sec = (DateTime.Now - t0).TotalSeconds;
        op.Text("开机连接耗时", $"{sec:0.0}s");
        if (!ok)
        {
            op.Fail("开机后连接失败");
            return StepResult.Fail("开机时间测试未通过");
        }
        await op.Sleep(2000, "稳定连接");
        op.Ok("开机时间测试通过");
        return StepResult.Pass($"开机时间测试通过 {sec:0.0}s");
    }

    private static async Task<StepResult> TimeDateAsync(ConST560Ops op, CancellationToken ct)
    {
        var sysTime = await op.Dut.QueryTextAsync("GetSystemTime", null, ct);
        var devTime = await op.Dut.QueryTextAsync("GetDevSysDate", null, ct);
        op.Text("系统时间", sysTime);
        op.Text("被检时间", devTime);
        if (string.IsNullOrWhiteSpace(sysTime) || string.IsNullOrWhiteSpace(devTime))
        {
            op.Fail("读取时间失败");
            return StepResult.Fail("时间日期测试未通过");
        }
        var ok = DateTime.TryParse(sysTime, out var t1) && DateTime.TryParse(devTime, out var t2) && Math.Abs((t1 - t2).TotalSeconds) <= 5;
        op.Ok(ok ? "时间日期测试通过" : "时间日期差异超限");
        return ok ? StepResult.Pass("时间日期测试通过") : StepResult.Fail("时间日期测试未通过");
    }

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
            return StepResult.Fail("SN号写入未通过");
        }
        await op.Dut.SetSerialNumberAsync(sn, ct);
        var readSn = await op.Dut.ReadSerialNumberAsync(ct);
        op.Text("写入SN", sn);
        op.Text("回读SN", readSn);
        if (!string.Equals(sn, readSn, StringComparison.OrdinalIgnoreCase))
        {
            op.Fail("序列号回读不一致");
            return StepResult.Fail("SN号写入未通过");
        }
        ctx.SerialNumber = sn;
        op.Ok("SN号写入通过");
        return StepResult.Pass("SN号写入通过", sn);
    }

    private static async Task<StepResult> TypeWriteAsync(ConST560Ops op, ITestContext ctx, CancellationToken ct)
    {
        var deviceType = ctx.Step.Settings.TryGetValue("DeviceType", out var dt) && !string.IsNullOrWhiteSpace(dt) ? dt : "ConST560";
        await op.Dut.SetPrimaryDeviceTypeAsync(deviceType, ct);
        var readType = await op.Dut.QueryTextAsync("读取型号", new[] { "PARA=APP" }, ct);
        op.Text("写入型号", deviceType);
        op.Text("回读型号", readType);
        if (!string.Equals(deviceType, readType, StringComparison.OrdinalIgnoreCase))
        {
            op.Fail("型号写入回读不一致");
            return StepResult.Fail("型号写入未通过");
        }
        op.Ok("型号写入通过");
        return StepResult.Pass("型号写入通过", readType);
    }

    private static async Task<StepResult> VersionVerifyAsync(ConST560Ops op, ITestContext ctx, CancellationToken ct)
    {
        var expectedFirmware = ctx.Step.Settings.TryGetValue("ExpectedFirmware", out var ef) ? ef : "";
        var moduleVersionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (ctx.Step.Settings.TryGetValue("ModuleVersions", out var mv) && !string.IsNullOrWhiteSpace(mv))
        {
            foreach (var part in mv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var idx = part.IndexOf('=');
                if (idx > 0)
                {
                    moduleVersionMap[part[..idx].Trim()] = part[(idx + 1)..].Trim();
                }
            }
        }

        var pass = true;
        var msg = new System.Text.StringBuilder();

        var fw = await op.Dut.ReadFirmwareVersionAsync(ct);
        op.Text("固件版本", fw);
        if (string.IsNullOrWhiteSpace(fw))
        {
            op.Fail("版本读取失败");
            return StepResult.Fail("版本验证未通过");
        }
        if (!string.IsNullOrWhiteSpace(expectedFirmware) && !string.Equals(fw, expectedFirmware, StringComparison.OrdinalIgnoreCase))
        {
            msg.AppendLine($"固件版本不一致：设备={fw}，期望={expectedFirmware}");
            pass = false;
        }

        var moduleVersions = await op.Dut.QueryTextAsync("读取设备版本信息", new[] { "module=HOST,E-H,PA,FF" }, ct);
        op.Text("版本信息", moduleVersions);
        var ddHart = await op.Dut.QueryTextAsync("读取HART_DD版本", null, ct);
        var ddFF = await op.Dut.QueryTextAsync("读取FF_DD版本", null, ct);
        var ddPA = await op.Dut.QueryTextAsync("读取PA_DD版本", null, ct);
        op.Text("HART_DD版本", ddHart);
        op.Text("FF_DD版本", ddFF);
        op.Text("PA_DD版本", ddPA);

        var kvPairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(moduleVersions))
        {
            foreach (var line in moduleVersions.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = line.IndexOf(':');
                if (idx > 0)
                {
                    kvPairs[line[..idx].Trim()] = line[(idx + 1)..].Trim();
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(ddHart)) kvPairs["HART_DD库版本"] = ddHart;
        if (!string.IsNullOrWhiteSpace(ddFF)) kvPairs["FF_DD库版本"] = ddFF;
        if (!string.IsNullOrWhiteSpace(ddPA)) kvPairs["PA_DD库版本"] = ddPA;

        foreach (var kv in moduleVersionMap)
        {
            var actual = kvPairs.TryGetValue(kv.Key, out var v) ? v : "";
            if (!string.Equals(actual, kv.Value, StringComparison.OrdinalIgnoreCase))
            {
                msg.AppendLine($"{kv.Key} 不一致：设备={actual}，期望={kv.Value}");
                pass = false;
            }
        }

        if (!pass)
        {
            if (!await ctx.ConfirmAsync($"版本校验未通过：\n{msg}\n是否手动通过？", ct))
            {
                op.Fail("版本校验未通过");
                return StepResult.Fail("版本验证未通过");
            }
        }

        op.Ok("版本验证通过");
        return StepResult.Pass("版本验证通过", $"固件={fw}");
    }

    private static async Task<StepResult> CheckerTestAsync(ConST560Ops op, ITestContext ctx, string testName, string functionCode, string resultField, CancellationToken ct)
    {
        await op.SwitchGearAsync(ConST560Gears.FullReset(), true);
        await op.Dut.CommandAsync("设置检测状态", new[] { "State=1" }, ct);
        await op.Dut.CommandAsync("设置检测功能", new[] { $"Function={functionCode}" }, ct);

        if (!await ctx.ConfirmAsync($"开始{testName}测试，完成后点击确认", ct))
        {
            await op.Dut.CommandAsync("设置检测状态", new[] { "State=0" }, ct);
            op.Fail($"{testName}测试取消");
            return StepResult.Fail($"{testName}测试未通过");
        }

        var pass = false;
        for (var i = 0; i < 60; i++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await op.Dut.QueryTextAsync("读取检测结果", null, ct);
            op.Text(testName, result);
            if (!string.IsNullOrWhiteSpace(result) && result != "0")
            {
                pass = result == "2";
                break;
            }
            await op.Sleep(1000, $"等待{testName}结果");
        }

        await op.Dut.CommandAsync("设置检测状态", new[] { "State=0" }, ct);
        op.Ok(pass ? $"{testName}测试通过" : $"{testName}测试未通过");
        return pass ? StepResult.Pass($"{testName}测试通过") : StepResult.Fail($"{testName}测试未通过");
    }

    private static async Task<StepResult> SpeakerTestAsync(ConST560Ops op, ITestContext ctx, CancellationToken ct)
    {
        await op.SwitchGearAsync(ConST560Gears.FullReset(), true);
        await op.Dut.CommandAsync("设置检测状态", new[] { "State=1" }, ct);
        await op.Dut.CommandAsync("设置检测功能", new[] { "Function=4" }, ct);

        if (!await ctx.ConfirmAsync("开始扬声器测试，完成后点击确认", ct))
        {
            await op.Dut.CommandAsync("设置检测状态", new[] { "State=0" }, ct);
            op.Fail("扬声器测试取消");
            return StepResult.Fail("扬声器测试未通过");
        }

        var pass = false;
        for (var i = 0; i < 60; i++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await op.Dut.QueryTextAsync("读取检测结果", null, ct);
            op.Text("扬声器", result);
            if (!string.IsNullOrWhiteSpace(result) && result != "0")
            {
                pass = result == "2";
                break;
            }
            await op.Sleep(1000, "等待扬声器结果");
        }
        await op.Dut.CommandAsync("设置当前系统音量值", new[] { "Volume=10" }, ct);
        await op.Dut.CommandAsync("设置检测状态", new[] { "State=0" }, ct);
        op.Ok(pass ? "扬声器测试通过" : "扬声器测试未通过");
        return pass ? StepResult.Pass("扬声器测试通过") : StepResult.Fail("扬声器测试未通过");
    }

    private static async Task<StepResult> WifiTestAsync(ConST560Ops op, ITestContext ctx, CancellationToken ct)
    {
        var ssid = ctx.Step.Settings.TryGetValue("WifiSSID", out var s) ? s : "RDTEST";
        var pwd = ctx.Step.Settings.TryGetValue("WifiPassword", out var p) ? p : "Consttest";

        await op.SwitchGearAsync(ConST560Gears.FullReset(), true);
        var support = await op.Dut.QueryTextAsync("读取是否支持模块", new[] { "PARA=WIFI" }, ct);
        op.Text("WIFI模块支持", support);
        if (support == "0")
        {
            await op.Dut.CommandAsync("设置是否支持模块", new[] { "PARA=WIFI", "Value=1" }, ct);
            await op.Sleep(2000);
        }
        var wlanState = await op.Dut.QueryTextAsync("读取WLAN当前状态", null, ct);
        if (wlanState == "0")
        {
            await op.Dut.CommandAsync("设置WLAN当前状态", new[] { "State=1" }, ct);
            await op.Sleep(2000);
        }

        var connected = false;
        for (var tryTimes = 0; tryTimes < 5 && !connected; tryTimes++)
        {
            await op.Dut.CommandAsync("连接热点", new[] { $"Name={ssid}", $"Pswd={pwd}" }, ct);
            await op.Sleep(12000, "等待WIFI连接");
            var info = await op.Dut.QueryTextAsync("读取WLAN当前所有信息", null, ct);
            connected = !string.IsNullOrWhiteSpace(info) && info.Contains("Connected", StringComparison.OrdinalIgnoreCase);
        }
        if (!connected)
        {
            await op.Dut.CommandAsync("断开热点连接", null, ct);
            await op.Dut.CommandAsync("设置WLAN当前状态", new[] { "State=0" }, ct);
            op.Fail("WIFI测试失败");
            return StepResult.Fail("WIFI测试未通过");
        }

        await op.Dut.CommandAsync("断开热点连接", null, ct);
        await op.Dut.CommandAsync("设置WLAN当前状态", new[] { "State=0" }, ct);
        op.Ok("WIFI测试通过");
        return StepResult.Pass("WIFI测试通过");
    }

    private static async Task<StepResult> BluetoothTestAsync(ConST560Ops op, ITestContext ctx, CancellationToken ct)
    {
        await op.SwitchGearAsync(ConST560Gears.FullReset(), true);
        var support = await op.Dut.QueryTextAsync("读取是否支持模块", new[] { "PARA=BLE" }, ct);
        op.Text("蓝牙模块支持", support);
        if (support == "0")
        {
            await op.Dut.CommandAsync("设置是否支持模块", new[] { "PARA=BLE", "Value=1" }, ct);
            await op.Sleep(2000);
        }
        var btState = await op.Dut.QueryTextAsync("读取蓝牙开关状态", null, ct);
        if (btState == "0")
        {
            await op.Dut.CommandAsync("设置蓝牙开关状态", new[] { "state=1" }, ct);
            await op.Sleep(2000);
        }
        if (!await ctx.ConfirmAsync("蓝牙测试：请在被检上完成蓝牙自检后确认", ct))
        {
            await op.Dut.CommandAsync("设置蓝牙开关状态", new[] { "state=0" }, ct);
            op.Fail("蓝牙测试取消");
            return StepResult.Fail("蓝牙测试未通过");
        }
        await op.Dut.CommandAsync("设置蓝牙开关状态", new[] { "state=0" }, ct);
        op.Ok("蓝牙测试通过");
        return StepResult.Pass("蓝牙测试通过");
    }

    private static async Task<StepResult> OuterPressureModuleTestAsync(ConST560Ops op, CancellationToken ct)
    {
        await op.SwitchGearAsync(ConST560Gears.FullReset(), true);
        var conn = await op.Dut.QueryTextAsync("获取组件连接状态", new[] { "PARA=PM" }, ct);
        op.Text("压力模块状态", conn);
        if (string.IsNullOrWhiteSpace(conn) || conn == "0")
        {
            op.Fail("外部压力模块未连接");
            return StepResult.Fail("外部压力模块通讯未通过");
        }
        var ver = await op.Dut.QueryTextAsync("读取设备版本信息", new[] { "module=PM" }, ct);
        op.Text("压力模块版本", ver);
        op.Ok("外部压力模块通讯通过");
        return StepResult.Pass("外部压力模块通讯通过");
    }

    private static async Task<StepResult> MainBoardDiagAsync(ConST560Ops op, CancellationToken ct)
    {
        await op.SwitchGearAsync(ConST560Gears.FullReset(), true);
        var diag = await op.Dut.QueryTextAsync("读取诊断信息", new[] { "PARA=APP" }, ct);
        op.Text("主板诊断", diag);
        if (string.IsNullOrWhiteSpace(diag))
        {
            op.Fail("主板自检信息读取失败");
            return StepResult.Fail("主板自检信息未通过");
        }
        op.Ok("主板自检信息通过");
        return StepResult.Pass("主板自检信息通过", diag);
    }

    private static async Task<StepResult> CircuitVoltageAsync(ConST560Ops op, CancellationToken ct)
    {
        await op.SwitchGearAsync(ConST560Gears.FullReset(), true);
        await op.ConST326.SetMeasureModeAsync("V", ct);
        await op.ConST326.SetOutputModeAsync("mA24V", ct);
        await op.Sleep(2000, "等待环路电压稳定");
        var v = await op.MeasureVoltageWithConST326Async("V");
        op.Value("环路电压", v, "V");
        if (!op.Judge("LoopVoltage", v, "环路电压", "V"))
        {
            op.Fail("环路电压测试失败");
            return StepResult.Fail("环路电压测试未通过");
        }
        op.Ok("环路电压测试通过");
        return StepResult.Pass("环路电压测试通过", ConST560Ops.F(v));
    }

    private static async Task<StepResult> BusCommAsync(ConST560Ops op, ITestContext ctx, string function, string busMode, GearMap gear, CancellationToken ct)
    {
        var busCategory = BusCategory(ctx);
        if (busCategory == "E05_NoPAFF" && function == "PAFF")
        {
            return StepResult.Pass("该机型不测试总线项");
        }

        await op.SwitchGearAsync(gear, true);
        await op.Dut.CommandAsync("设置通道功能", new[] { $"Function={function}", $"BusPowerMode={busMode}" }, ct);
        await op.Sleep(3000, $"等待{function}通讯稳定");
        var searchResult = await op.Dut.QueryTextAsync(function == "HART" ? "读取HART搜索到的设备列表" : "读取FF搜索到的设备列表", null, ct);
        op.Text($"{function}搜索结果", searchResult);
        var pass = !string.IsNullOrWhiteSpace(searchResult) && (searchResult.Contains("1", StringComparison.OrdinalIgnoreCase) || searchResult.Contains("设备数量", StringComparison.OrdinalIgnoreCase));
        op.Ok(pass ? $"{function}通讯通过" : $"{function}通讯失败");
        return pass ? StepResult.Pass($"{function}通讯通过") : StepResult.Fail($"{function}通讯未通过");
    }

    private static async Task<StepResult> MaOutputSrcAsync(ConST560Ops op, CancellationToken ct)
    {
        await op.SwitchGearAsync(ConST560Gears.FullReset(), true);
        await op.ConST326.SetPower24VAsync(true, ct);
        await op.ConST326.SetMeasureModeAsync("mA", ct);
        await op.Dut.CommandAsync("设置通道功能", new[] { "Function=s_mA&24V" }, ct);
        await op.Sleep(2000);
        await op.Dut.CommandAsync("设置输出值", new[] { "Value=12" }, ct);
        await op.Sleep(2000);
        var val = await op.MeasureCurrentWithZCZHAsync("mA", 20, 200);
        op.Value("SRC输出", val, "mA");
        var pass = Math.Abs(val - 12) <= 0.5;
        op.Ok(pass ? "mA输出源测试通过" : "mA输出源测试未通过");
        return pass ? StepResult.Pass("mA输出源测试通过") : StepResult.Fail("mA输出源测试未通过");
    }

    private static async Task<StepResult> MaOutputSinkAsync(ConST560Ops op, CancellationToken ct)
    {
        await op.SwitchGearAsync(ConST560Gears.FullReset(), true);
        await op.ConST326.SetPower24VAsync(true, ct);
        await op.ConST326.SetMeasureModeAsync("mA", ct);
        await op.Dut.CommandAsync("设置通道功能", new[] { "Function=m_mA" }, ct);
        await op.Sleep(2000);
        await op.Dut.CommandAsync("设置输出值", new[] { "Value=12" }, ct);
        await op.Sleep(2000);
        var val = await op.MeasureCurrentWithZCZHAsync("mA", 20, 200);
        op.Value("SINK输出", val, "mA");
        var pass = Math.Abs(val - 12) <= 0.5;
        op.Ok(pass ? "mA输出沉测试通过" : "mA输出沉测试未通过");
        return pass ? StepResult.Pass("mA输出沉测试通过") : StepResult.Fail("mA输出沉测试未通过");
    }

    private static async Task<StepResult> MaMeasureAsync(ConST560Ops op, CancellationToken ct)
    {
        await op.SwitchGearAsync(ConST560Gears.FullReset(), true);
        await op.ConST326.SetOutputModeAsync("mA", ct);
        await op.Dut.CommandAsync("设置通道功能", new[] { "Function=m_mA" }, ct);
        await op.Sleep(2000);
        await op.ConST326.SetOutputAsync("mA", 12, "mA", ct);
        await op.Sleep(2000);
        var val = await op.Dut.QueryTextAsync("读取测量值", new[] { "Unit=mA" }, ct);
        op.Text("被检mA测量", val);
        var pass = double.TryParse(val, out var d) && Math.Abs(d - 12) <= 0.5;
        op.Ok(pass ? "mA测量功能测试通过" : "mA测量功能测试未通过");
        return pass ? StepResult.Pass("mA测量功能测试通过") : StepResult.Fail("mA测量功能测试未通过");
    }

    private static async Task<StepResult> OverCurrentAsync(ConST560Ops op, CancellationToken ct)
    {
        await op.SwitchGearAsync(ConST560Gears.FullReset(), true);
        await op.Dut.CommandAsync("设置通道功能", new[] { "Function=m_mA" }, ct);
        await op.Sleep(2000);
        var status = await op.Dut.QueryTextAsync("读取过流状态", null, ct);
        op.Text("过流状态", status);
        var pass = !string.IsNullOrWhiteSpace(status) && status != "ERROR";
        op.Ok(pass ? "过流测试通过" : "过流测试未通过");
        return pass ? StepResult.Pass("过流测试通过") : StepResult.Fail("过流测试未通过");
    }

    private static async Task<StepResult> VMeasureAsync(ConST560Ops op, CancellationToken ct)
    {
        await op.SwitchGearAsync(ConST560Gears.FullReset(), true);
        await op.ConST326.SetOutputModeAsync("V", ct);
        await op.ConST326.SetOutputAsync("V", 1, "V", ct);
        await op.Sleep(2000);
        var val = await op.Dut.QueryTextAsync("读取测量值", new[] { "Unit=V" }, ct);
        op.Text("被检V测量", val);
        var pass = double.TryParse(val, out var d) && Math.Abs(d - 1) <= 0.2;
        op.Ok(pass ? "电压测量功能测试通过" : "电压测量功能测试未通过");
        return pass ? StepResult.Pass("电压测量功能测试通过") : StepResult.Fail("电压测量功能测试未通过");
    }

    private static async Task<StepResult> OverPressureAsync(ConST560Ops op, CancellationToken ct)
    {
        await op.SwitchGearAsync(ConST560Gears.FullReset(), true);
        await op.ConST326.SetOutputModeAsync("V", ct);
        await op.ConST326.SetOutputAsync("V", 24, "V", ct);
        await op.Sleep(2000);
        var status = await op.Dut.QueryTextAsync("读取过压状态", null, ct);
        op.Text("过压状态", status);
        var pass = !string.IsNullOrWhiteSpace(status) && status != "ERROR";
        op.Ok(pass ? "过压测试通过" : "过压测试未通过");
        return pass ? StepResult.Pass("过压测试通过") : StepResult.Fail("过压测试未通过");
    }

    private static async Task<StepResult> ChargeStatusAsync(ConST560Ops op, ITestContext ctx, CancellationToken ct)
    {
        await op.SwitchGearAsync(ConST560Gears.FullReset(), true);
        await op.SwitchGearAsync(ConST560Gears.ChargeInsert(), true);
        await op.Sleep(2000);
        var current = await op.MeasureCurrentWithZCZHAsync("mA", 20, 200);
        op.Value("充电电流", current, "mA");
        var charging = current > 0.3;
        if (!charging)
        {
            if (!await ctx.ConfirmAsync("充电电流偏低，是否继续？", ct))
            {
                await op.SwitchGearAsync(ConST560Gears.ChargeDisconnect(), false);
                op.Fail("充电状态测试取消");
                return StepResult.Fail("充电状态测试未通过");
            }
        }
        await op.SwitchGearAsync(ConST560Gears.ChargeDisconnect(), false);
        op.Ok("充电状态测试通过");
        return StepResult.Pass("充电状态测试通过", ConST560Ops.F(current));
    }

    private static async Task<StepResult> WholeMachineConsumeAsync(ConST560Ops op, CancellationToken ct)
    {
        await op.SwitchGearAsync(ConST560Gears.FullReset(), true);
        await op.ConST326.SetOutputModeAsync("mA24V", ct);
        await op.ConST326.SetOutputAsync("mA", 22, "mA", ct);
        await op.Sleep(2000, "等待满功率稳定");
        var val = await op.MeasureCurrentWithZCZHAsync("mA", 20, 200);
        op.Value("整机功耗", val, "mA");
        var pass = val >= 5 && val <= 100;
        op.Ok(pass ? "整机耗电测试通过" : "整机耗电测试未通过");
        return pass ? StepResult.Pass("整机耗电测试通过") : StepResult.Fail("整机耗电测试未通过");
    }

    private static async Task<StepResult> ChargeTestAsync(ConST560Ops op, CancellationToken ct)
    {
        await op.SwitchGearAsync(ConST560Gears.FullReset(), true);
        await op.SwitchGearAsync(ConST560Gears.ChargeInsert(), true);
        await op.Sleep(60000, "等待充电一分钟");
        var current = await op.MeasureCurrentWithZCZHAsync("mA", 20, 200);
        op.Value("充电电流", current, "mA");
        await op.SwitchGearAsync(ConST560Gears.ChargeDisconnect(), false);
        var pass = current > 0.3;
        op.Ok(pass ? "充电检测通过" : "充电检测未通过");
        return pass ? StepResult.Pass("充电检测通过") : StepResult.Fail("充电检测未通过");
    }

    private static async Task<StepResult> TestCompleteAsync(ConST560Ops op, CancellationToken ct)
    {
        try { await op.Dut.CommandAsync("Close", null, ct); } catch { }
        await op.SwitchGearAsync(ConST560Gears.AviationPlugRetract(), false);
        await op.Sleep(800);
        await op.SwitchGearAsync(ConST560Gears.TypeCRetract(), false);
        await op.Sleep(800);
        await op.SwitchGearAsync(ConST560Gears.ProbeRetract(), false);
        await op.Sleep(800);
        await op.SwitchGearAsync(ConST560Gears.ClampOpen(), false);
        await op.Sleep(400);
        await op.SwitchGearAsync(ConST560Gears.FullReset(), true);
        await op.Sleep(300);
        await op.Dut.ReplenishLinkAsync(ct);
        op.Ok("测试完成");
        return StepResult.Pass("测试完成");
    }
}
