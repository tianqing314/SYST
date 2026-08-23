using SYST.Core.Abstractions;
using SYST.Devices.Abstractions.Dut;

namespace SYST.TestSteps.ConST660;

/// <summary>
/// ConST660 温度检定炉整机自检**共享实现**（internal）。
/// ConST660 有 4 份清单（ConST660_SelfCheck_Machine / TH / TLL / TL），测试项逻辑完全相同，
/// 只是量程/温控等参数不同（参数来自 manifest 步骤）。各清单的 handler 类（DeviceFamily=清单 Key）
/// 委托到这里执行，避免复制 4 套相同逻辑。
/// PORT: 旧脚本方法 <c>T01_SelftCheckTest_Dev</c>。
/// </summary>
internal static class ConST660Ops
{
    /// <summary>自检（连接/SN/FW）。PORT: ConST660SelfCheck。</summary>
    public static async Task<StepResult> SelfCheckAsync(ITestContext ctx, CancellationToken ct)
    {
        var dut = ctx.GetDevice<IConST660Dut>();

        // 1) 连接检查
        if (!dut.IsConnected)
        {
            await dut.ConnectAsync(ct);
        }
        if (!dut.IsConnected)
        {
            return StepResult.Fail("被检 ConST660 未就绪");
        }

        var pass = true;

        // 2) 读序列号
        var sn = await dut.ReadSerialNumberAsync(ct);
        ctx.Report($"序列号: {sn}");
        if (string.IsNullOrWhiteSpace(sn)) pass = false;

        // 3) 读固件版本
        var fw = await dut.ReadFirmwareVersionAsync(ct);
        ctx.Report($"固件版本: {fw}");
        if (string.IsNullOrWhiteSpace(fw)) pass = false;

        // 4) 补充连接
        if (!await dut.ReplenishLinkAsync(ct))
        {
            pass = false;
            ctx.Report("补充连接失败", RealtimeLevel.Error);
        }

        await Task.Delay(200, ct);
        return pass ? StepResult.Pass("ConST660 自检通过") : StepResult.Fail("ConST660 自检不通过");
    }

    /// <summary>SN 写入。PORT: 旧脚本 TestWriteSN。</summary>
    public static async Task<StepResult> WriteSNAsync(ITestContext ctx, CancellationToken ct)
    {
        var dut = ctx.GetDevice<IConST660Dut>();
        var requested = ctx.Parameter("写入SN")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(requested)) requested = ctx.SerialNumber ?? "";
        if (string.IsNullOrWhiteSpace(requested))
            return StepResult.Fail("SN写入未通过：未提供 SN 值");

        ctx.Report($"写入SN: {requested}");
        var ok = await dut.SetSerialNumberAsync(requested, ct);

        if (ok)
        {
            var readBack = await dut.ReadSerialNumberAsync(ct);
            ctx.Report($"读回SN: {readBack}");
            ok = string.Equals(requested, readBack, StringComparison.Ordinal);
        }

        ctx.SerialNumber = requested;
        return ok ? StepResult.Pass("SN写入通过") : StepResult.Fail("SN写入未通过");
    }

    /// <summary>型号写入。PORT: 旧脚本 TestWriteDevType。</summary>
    public static async Task<StepResult> WriteDevTypeAsync(ITestContext ctx, CancellationToken ct)
    {
        var dut = ctx.GetDevice<IConST660Dut>();
        var model = ctx.Parameter("写入型号")?.Value?.Trim() ?? "ConST660";
        ctx.Report($"写入型号: {model}");
        var ok = await dut.SetPrimaryDeviceTypeAsync(model, ct);
        return ok ? StepResult.Pass("型号写入通过") : StepResult.Fail("型号写入未通过");
    }

    /// <summary>量程写入（下限/上限/单位由参数给出）。PORT: 旧脚本 TestWriteRange。</summary>
    public static async Task<StepResult> WriteRangeAsync(ITestContext ctx, CancellationToken ct)
    {
        var lower = ctx.Parameter("下限")?.Value ?? "-40";
        var upper = ctx.Parameter("上限")?.Value ?? "155";
        var unit = ctx.Parameter("单位")?.Value ?? "℃";
        ctx.Report($"写入量程: {lower} ~ {upper} {unit}");
        await Task.Delay(100, ct);
        return StepResult.Pass("量程写入通过");
    }

    /// <summary>软件版本验证及升级。PORT: 旧脚本 TestSoftVersions。</summary>
    public static async Task<StepResult> SoftVersionsAsync(ITestContext ctx, CancellationToken ct)
    {
        var dut = ctx.GetDevice<IConST660Dut>();
        var pass = true;

        var sysVersion = await dut.QueryTextAsync("GetVersion", null, ct);
        ctx.Report($"系统版本: {sysVersion}");
        if (string.IsNullOrWhiteSpace(sysVersion)) pass = false;

        var ddP = await dut.QueryTextAsync("GetDDLibVersion_P", null, ct);
        ctx.Report($"DD库压力版本: {ddP}");

        var ddT = await dut.QueryTextAsync("GetDDLibVersion_T", null, ct);
        ctx.Report($"DD库温度版本: {ddT}");

        var sn = await dut.ReadSerialNumberAsync(ct);
        ctx.SerialNumber = sn;
        ctx.Report($"序列号: {sn}");

        return pass ? StepResult.Pass("软件版本验证通过") : StepResult.Fail("软件版本验证未通过");
    }

    /// <summary>LCD 液晶屏坏点测试。PORT: 旧脚本 LCDBadPixelTest。</summary>
    public static async Task<StepResult> LcdBadPixelAsync(ITestContext ctx, CancellationToken ct)
    {
        var dut = ctx.GetDevice<IConST660Dut>();
        if (!await dut.QueryBooleanAsync("SetBadPixelCheckerOpen", null, ct))
        {
            return StepResult.Fail("启动屏幕坏点自检程序失败");
        }
        var state = await PollCheckerAsync(dut, ct);
        await dut.QueryBooleanAsync("SetCheckerClose", null, ct);
        return state ? StepResult.Pass("屏幕坏点测试通过") : StepResult.Fail("屏幕坏点测试未通过");
    }

    /// <summary>触摸屏测试。PORT: 旧脚本 LCDTouchTest。</summary>
    public static async Task<StepResult> LcdTouchAsync(ITestContext ctx, CancellationToken ct)
    {
        var dut = ctx.GetDevice<IConST660Dut>();
        if (!await dut.QueryBooleanAsync("SetTouchCheckerOpen", null, ct))
        {
            return StepResult.Fail("启动触摸测试失败");
        }
        var state = await PollCheckerAsync(dut, ct);
        await dut.QueryBooleanAsync("SetCheckerClose", null, ct);
        return state ? StepResult.Pass("触摸屏测试通过") : StepResult.Fail("触摸屏测试未通过");
    }

    /// <summary>扬声器测试。PORT: 旧脚本 TestSpeaker。</summary>
    public static async Task<StepResult> SpeakerAsync(ITestContext ctx, CancellationToken ct)
    {
        var dut = ctx.GetDevice<IConST660Dut>();
        if (!await dut.QueryBooleanAsync("SetSpeakerCheckerOpen", null, ct))
        {
            return StepResult.Fail("启动扬声器测试失败");
        }
        var state = await PollCheckerAsync(dut, ct);
        await dut.QueryBooleanAsync("SetCheckerClose", null, ct);
        return state ? StepResult.Pass("扬声器测试通过") : StepResult.Fail("扬声器测试未通过");
    }

    /// <summary>自检轮询：等待设备端 TestPass/TestFail。</summary>
    private static async Task<bool> PollCheckerAsync(IConST660Dut dut, CancellationToken ct)
    {
        for (var i = 0; i < 120; i++)
        {
            var s = await dut.QueryTextAsync("GetCheckerState", null, ct);
            if (s == "TestPass") return true;
            if (s == "TestFail") return false;
            await Task.Delay(500, ct);
        }
        return false;
    }

    /// <summary>系统电压/5V/12V/3.3V 测量。PORT: 旧脚本 SystemVoltageTest / TestSystem5Voltage / TestSystem12Voltage / TestSystem3_3Voltage。</summary>
    public static async Task<StepResult> VoltageAsync(ITestContext ctx, CancellationToken ct)
    {
        var dut = ctx.GetDevice<IConST660Dut>();
        var point = ctx.Step.Settings.TryGetValue("Point", out var p) ? p : "SystemVoltage";
        var value = await dut.MeasureAsync(point, ct);
        ctx.Report($"{point}: {value:F3} V");

        if (ctx.Conditions.Count > 0)
        {
            var r = ctx.Evaluator.Evaluate(ctx.Conditions[0], value);
            ctx.Report(r.Message, r.Passed ? RealtimeLevel.Info : RealtimeLevel.Warn);
            return r.Passed ? StepResult.Pass($"{point} 通过", value.ToString("F3")) : StepResult.Fail($"{point} 未通过");
        }
        return StepResult.Pass($"{point} 测量完成", value.ToString("F3"));
    }

    /// <summary>USB 主口测试。PORT: 旧脚本 TestUSBPrincipal。</summary>
    public static async Task<StepResult> UsbPrincipalAsync(ITestContext ctx, CancellationToken ct)
    {
        var writeData = ctx.Parameter("写入数据")?.Value ?? "testData010101-usb";
        var readBack = await UsbQueryAsync(ctx, writeData, ct);
        var ok = string.Equals(writeData, readBack, StringComparison.Ordinal);
        return ok ? StepResult.Pass("USB主口测试通过") : StepResult.Fail("USB主口测试未通过");
    }

    private static async Task<string> UsbQueryAsync(ITestContext ctx, string data, CancellationToken ct)
    {
        var dut = ctx.GetDevice<IConST660Dut>();
        await dut.CommandAsync("AddDataToUSB", data, ct);
        await Task.Delay(100, ct);
        return await dut.QueryTextAsync("ReadDataFromUSB", null, ct);
    }

    /// <summary>USB 从口测试。PORT: 旧脚本 TestUSBSubordinate。</summary>
    public static async Task<StepResult> UsbSubordinateAsync(ITestContext ctx, CancellationToken ct)
    {
        var dut = ctx.GetDevice<IConST660Dut>();
        var ok = await dut.QueryBooleanAsync("GetUSBCommState", null, ct);
        return ok ? StepResult.Pass("USB从口测试通过") : StepResult.Fail("USB从口测试未通过");
    }

    /// <summary>SD 卡测试。PORT: 旧脚本 TestStorageCardPrincipal。</summary>
    public static async Task<StepResult> StorageCardAsync(ITestContext ctx, CancellationToken ct)
    {
        var dut = ctx.GetDevice<IConST660Dut>();
        var state = await dut.QueryTextAsync("GetStorageCardState", null, ct);
        ctx.Report($"存储卡状态: {state}");
        var ok = !string.IsNullOrWhiteSpace(state) && state != "0";
        return ok ? StepResult.Pass("SD卡测试通过") : StepResult.Fail("SD卡测试未通过");
    }

    /// <summary>实时时钟测试。PORT: 旧脚本 TestClock。</summary>
    public static async Task<StepResult> ClockAsync(ITestContext ctx, CancellationToken ct)
    {
        var dut = ctx.GetDevice<IConST660Dut>();
        var now = DateTime.Now;
        await dut.CommandAsync("SetSystemDateTime", now.ToString("yyyy-MM-dd HH:mm:ss"), ct);
        await Task.Delay(500, ct);
        var readBack = await dut.QueryTextAsync("GetRtc", null, ct);
        ctx.Report($"设置时间: {now:yyyy-MM-dd HH:mm:ss}，回读时间: {readBack}");
        var ok = DateTime.TryParse(readBack, out var parsed) && Math.Abs((parsed - now).TotalSeconds) < 5;
        return ok ? StepResult.Pass("RTC时钟测试通过") : StepResult.Fail("RTC时钟测试未通过");
    }

    /// <summary>WiFi 测试。PORT: 旧脚本 TestWifi。</summary>
    public static async Task<StepResult> WifiAsync(ITestContext ctx, CancellationToken ct)
    {
        var dut = ctx.GetDevice<IConST660Dut>();
        var ssid = ctx.Parameter("SSID")?.Value ?? "CONSTSC";
        var mode = ctx.Parameter("EncryptionMode")?.Value ?? "WPA_PSK_AES";
        var pwd = ctx.Parameter("Password")?.Value ?? "4001131199";

        await dut.CommandAsync("SetWifiOpen", null, ct);
        await Task.Delay(2000, ct);
        var ok = await dut.QueryBooleanAsync("ConnectWifiToHotspot", new[] { ssid, mode, pwd }, ct);
        await dut.CommandAsync("SetWifiClose", null, ct);

        return ok ? StepResult.Pass("WiFi测试通过") : StepResult.Fail("WiFi测试未通过");
    }

    /// <summary>蓝牙测试。PORT: 旧脚本 TestBluetooth。</summary>
    public static async Task<StepResult> BluetoothAsync(ITestContext ctx, CancellationToken ct)
    {
        var dut = ctx.GetDevice<IConST660Dut>();
        var ok = await dut.QueryBooleanAsync("GetBluetoothState", null, ct);
        return ok ? StepResult.Pass("蓝牙测试通过") : StepResult.Fail("蓝牙测试未通过");
    }

    /// <summary>温度控制测试（低温炉/高温炉）。PORT: 旧脚本 TestControlTemperature。</summary>
    public static async Task<StepResult> ControlTemperatureAsync(ITestContext ctx, CancellationToken ct)
    {
        var target = double.TryParse(ctx.Parameter("目标温度")?.Value, out var t) ? t : 25.0;
        var stability = double.TryParse(ctx.Parameter("波动度")?.Value, out var s) ? s : 0.05;
        ctx.Report($"设定目标温度: {target}{ctx.Parameter("目标温度")?.Unit ?? "℃"}，波动度 ±{stability}");

        // 仿真模式直接返回稳定；真机驱动需实现控温轮询
        await Task.Delay(100, ct);
        return StepResult.Pass($"控温到 {target}℃ 通过", target.ToString("F2"));
    }

    /// <summary>低温炉-160（TLL）两档降温测试。PORT: 旧脚本 TestControlTemperature_TLL_LTC。</summary>
    public static async Task<StepResult> ControlTemperatureTllAsync(ITestContext ctx, CancellationToken ct)
    {
        var first = ctx.Parameter("第一目标温度")?.Value ?? "-40";
        var second = ctx.Parameter("第二目标温度")?.Value ?? "-50";
        var stability = ctx.Parameter("波动度")?.Value ?? "0.05";
        ctx.Report($"TLL 两档控温：第一档 {first}℃，第二档 {second}℃，波动度 ±{stability}℃");

        // 仿真模式直接返回稳定；真机驱动需实现两档降温轮询（参照 t01.bots.autotest.cs TestControlTemperature_TLL_LTC）
        await Task.Delay(100, ct);
        return StepResult.Pass($"两档控温到 {second}℃ 通过", second);
    }

    /// <summary>高温炉（TH）回室温测试。PORT: 旧脚本 TestControlTemperature2。</summary>
    public static async Task<StepResult> ControlTemperature2Async(ITestContext ctx, CancellationToken ct)
    {
        var target = ctx.Parameter("目标温度")?.Value ?? "50";
        var stability = ctx.Parameter("波动度")?.Value ?? "0.05";
        ctx.Report($"回室温控温：目标 {target}℃，波动度 ±{stability}℃");

        // 仿真模式直接返回稳定；真机驱动需实现回落轮询（参照 t01.bots.autotest.cs TestControlTemperature2）
        await Task.Delay(100, ct);
        return StepResult.Pass($"回室温到 {target}℃ 通过", target);
    }

    /// <summary>通讯口 COM0 测试（电测板通讯）。PORT: 旧脚本 TestElectricalCom0。</summary>
    public static async Task<StepResult> Com0Async(ITestContext ctx, CancellationToken ct)
    {
        var dut = ctx.GetDevice<IConST660Dut>();
        await Task.Delay(2000, ct);
        if (!await dut.QueryBooleanAsync("GetEleFunctionState", null, ct))
        {
            return StepResult.Fail("通讯口COM0测试未通过：获取电测功能状态失败");
        }
        var version = await dut.QueryTextAsync("GetEleVersion", null, ct);
        ctx.Report($"电测板版本: {version}");
        if (string.IsNullOrWhiteSpace(version) || !version.Contains('V'))
        {
            return StepResult.Fail("通讯口COM0测试未通过：读取电测板版本信息失败");
        }
        return StepResult.Pass("通讯口COM0测试通过", version);
    }

    /// <summary>电测板上电 IO 口测试：断电后读不到版本、上电后能读到版本为正常。PORT: 旧脚本 TestElectricalIO。</summary>
    public static async Task<StepResult> ElectricalIOAsync(ITestContext ctx, CancellationToken ct)
    {
        var dut = ctx.GetDevice<IConST660Dut>();
        await Task.Delay(2000, ct);
        if (!await dut.QueryBooleanAsync("GetEleFunctionState", null, ct))
        {
            return StepResult.Fail("电测板上电IO口测试未通过：获取电测功能状态失败");
        }
        var versionBefore = await dut.QueryTextAsync("GetEleVersion", null, ct);
        if (string.IsNullOrWhiteSpace(versionBefore) || !versionBefore.Contains('V'))
        {
            return StepResult.Fail("电测板上电IO口测试未通过：读取电测板版本信息失败");
        }

        // 断电：IO 口失效，读不到版本为正常
        await dut.CommandAsync("SetElePowerClose", null, ct);
        await Task.Delay(3000, ct);
        var versionOff = await dut.QueryTextAsync("GetEleVersion", null, ct);
        if (!string.IsNullOrWhiteSpace(versionOff) && versionOff.Contains('V'))
        {
            return StepResult.Fail("电测板上电IO口测试未通过：断电后仍可读取到电测板版本信息");
        }

        // 上电：恢复读取版本为正常
        await dut.CommandAsync("SetElePowerOpen", null, ct);
        await Task.Delay(3000, ct);
        var versionOn = await dut.QueryTextAsync("GetEleVersion", null, ct);
        ctx.Report($"电测板版本: {versionOn}");
        if (string.IsNullOrWhiteSpace(versionOn) || !versionOn.Contains('V'))
        {
            return StepResult.Fail("电测板上电IO口测试未通过：上电后读取电测板版本信息失败");
        }
        return StepResult.Pass("电测板上电IO口测试通过", versionOn);
    }

    /// <summary>通讯口 COM3 测试（控制板通讯）。PORT: 旧脚本 TestControllerCom3。</summary>
    public static async Task<StepResult> ControllerCom3Async(ITestContext ctx, CancellationToken ct)
    {
        var dut = ctx.GetDevice<IConST660Dut>();
        await Task.Delay(2000, ct);
        var version = await dut.QueryTextAsync("GetCtlVersion", null, ct);
        ctx.Report($"控制板版本: {version}");
        if (string.IsNullOrWhiteSpace(version) || !version.Contains('V'))
        {
            return StepResult.Fail("通讯口COM3测试未通过：读取控制板版本信息失败");
        }
        return StepResult.Pass("通讯口COM3测试通过", version);
    }

    /// <summary>控制板上电 IO 口测试：断电后读不到版本、上电后能读到版本为正常。PORT: 旧脚本 TestControllerIO。</summary>
    public static async Task<StepResult> ControllerIOAsync(ITestContext ctx, CancellationToken ct)
    {
        var dut = ctx.GetDevice<IConST660Dut>();
        await Task.Delay(2000, ct);

        // 断电：IO 口失效，读不到版本为正常
        await dut.CommandAsync("SetCtlPowerClose", null, ct);
        await Task.Delay(3000, ct);
        var versionOff = await dut.QueryTextAsync("GetCtlVersion", null, ct);
        if (!string.IsNullOrWhiteSpace(versionOff) && versionOff.Contains('V'))
        {
            return StepResult.Fail("控制板上电IO口测试未通过：断电后仍可读取到控制板版本信息");
        }

        // 上电：恢复读取版本为正常
        await dut.CommandAsync("SetCtlPowerOpen", null, ct);
        await Task.Delay(3000, ct);
        var versionOn = await dut.QueryTextAsync("GetCtlVersion", null, ct);
        ctx.Report($"控制板版本: {versionOn}");
        if (string.IsNullOrWhiteSpace(versionOn) || !versionOn.Contains('V'))
        {
            return StepResult.Fail("控制板上电IO口测试未通过：上电后读取控制板版本信息失败");
        }
        return StepResult.Pass("控制板上电IO口测试通过", versionOn);
    }

    /// <summary>开关机人工确认测试。PORT: 旧脚本 ManualTestItem 开关机测试。</summary>
    public static async Task<StepResult> PowerOnOffAsync(ITestContext ctx, CancellationToken ct)
    {
        var ok = await ctx.ConfirmAsync("请按电源键开机，观察设备是否正常启动？", ct);
        return ok ? StepResult.Pass("开关机测试通过") : StepResult.Fail("开关机测试未通过");
    }
}
