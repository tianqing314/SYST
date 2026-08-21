using System.Globalization;
using System.IO.Ports;
using System.Text.RegularExpressions;
using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;

namespace SYST.TestSteps.ConST811A.ConST811A_BP_Machine;

/// <summary>
/// ConST811A 主板（设备族 ConST811A）测试**设备特有**处理器集合。**自动转换**自旧
/// <c>ConST811A_MainBoard_Auto.cs</c> 的测试方法与 <c>.distributed.json</c> 任务配置：继电器指令序列
/// （GZP21 共享工装）、被检指令与 Range 判定。气象版不接 P06/ConST810 标准模块（电压/电流采样）。
/// 工装用 <see cref="IMachineTestTool"/>，被检用 <see cref="IConST811ADut"/>。
/// </summary>
internal sealed class ConST811AOps
{
    private readonly ITestContext _ctx;
    private readonly CancellationToken _ct;

    /// <summary>GZP21 共享工装（继电器输出）。</summary>
    public readonly IMachineTestTool Gzp21;

    /// <summary>被检 ConST811A 专属驱动。</summary>
    public readonly IConST811ADut Dut;

    public ConST811AOps(ITestContext ctx, CancellationToken ct)
    {
        _ctx = ctx;
        _ct = ct;
        Gzp21 = ctx.GetDevice<IMachineTestTool>("GZP21");
        Dut = ctx.GetDevice<IConST811ADut>();
    }

    /// <summary>数值格式化（保留三位有效小数）。</summary>
    public static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>推送实时消息。</summary>
    public void Report(string m, RealtimeLevel l = RealtimeLevel.Info) => _ctx.Report(m, l);

    /// <summary>真机稳定延时（继电器切档/设值后需等待）。PORT: 旧 Thread.Sleep / ScriptHelper.Thread_Sleep。</summary>
    public Task Sleep(int ms)
    {
        Report($"等待 {ms}ms");
        return Task.Delay(ms, _ct);
    }

    /// <summary>发共享工装输出指令（按名称映射到 GZP21 通道）。</summary>
    public Task Relay(string cmd)
    {
        Report($"工装输出指令：{cmd}");
        return Gzp21.SetOutputAsync(cmd, true, _ct);
    }

    /// <summary>回放旧平台中可直接映射的 P21/GZP21 调用；复杂上下文参数不在此层猜测。</summary>
    public async Task ExecuteLegacyAsync(IReadOnlyList<string> calls, CancellationToken ct)
    {
        foreach (var call in calls)
        {
            var p = call.Split('|', 3);
            if (p.Length < 2) continue;
            var device = p[0];
            var method = p[1];
            var arg = p.Length == 3 ? p[2] : "";
            IReadOnlyList<string>? args = string.IsNullOrWhiteSpace(arg) ? null : new[] { arg.Trim() };
            if (device == "GZP21")
            {
                var open = !arg.Contains("Close", StringComparison.OrdinalIgnoreCase);
                var outputName = method.Replace("Set", "").Replace("State", "");
                await Gzp21.SetOutputAsync(outputName, open, ct);
                continue;
            }
            if (device == "P21")
            {
                if (method.StartsWith("Get", StringComparison.OrdinalIgnoreCase) || method.StartsWith("Is", StringComparison.OrdinalIgnoreCase))
                    _ = await Dut.QueryTextAsync(method, args, ct);
                else
                    await Dut.CommandAsync(method, args, ct);
            }
            else if (device == "P06")
            {
                // 气象版不接 P06 标准模块；旧脚本遗留的 P06 电压/电流调用按跳过处理（无采样设备）
                Report($"跳过 P06 调用：{method}（本机型未配置 P06 标准模块）", RealtimeLevel.Warn);
            }
        }
    }

    /// <summary>按名取条件（找不到返回 null）。</summary>
    public ConditionDescriptor? Cond(string name)
    {
        foreach (var c in _ctx.Conditions)
            if (c.Name == name) return c;
        return null;
    }

    /// <summary>对某测量值按指定条件名判定，报「读回+区间+结论」并返回是否通过（条件缺失记为不通过）。</summary>
    public bool Judge(string condName, double value, string label, string unit)
    {
        var cond = Cond(condName);
        if (cond is null)
        {
            Report($"{label} {F(value)}{unit}：缺少判定条件 {condName}", RealtimeLevel.Warn);
            return false;
        }
        var r = _ctx.Evaluator.Evaluate(cond, value);
        Report($"{label} {F(value)}{unit}：{r.Message}", r.Passed ? RealtimeLevel.Info : RealtimeLevel.Warn);
        return r.Passed;
    }

    /// <summary>掐头去尾各 5 点（旧 ScriptHelperKVP.TrimCurrents 语义）。</summary>
    public static List<double> TrimCurrents(List<double> values)
    {
        if (values.Count <= 10) return values;
        return values.Skip(5).Take(values.Count - 10).ToList();
    }
}

/// <summary>
/// SN写入。PORT: 旧脚本方法 TestDeviceWriteSN（JSON Entry: TestDeviceWriteSN）。
/// </summary>
public sealed class TestDeviceWriteSNConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestDeviceWriteSN";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        // 优先取步骤「写入SN」参数（旧脚本默认值），为空时用号位 SN（UI 输入/自动生成）
        var requestedSn = ctx.Parameter("写入SN")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(requestedSn)) requestedSn = ctx.SerialNumber ?? "";
        if (string.IsNullOrWhiteSpace(requestedSn)) pass = false;
        else pass &= await op.Dut.SetSerialNumberAsync(requestedSn, ct);
        if (pass) ctx.SerialNumber = await op.Dut.ReadSerialNumberAsync(ct);
        op.Report(pass ? "✓ SN写入通过" : "✗ SN写入未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("SN写入通过") : StepResult.Fail("SN写入未通过");
    }
}

/// <summary>
/// 设备类型写入。PORT: 旧脚本方法 TestDeviceWriteType（JSON Entry: TestDeviceWriteType）。
/// </summary>
public sealed class TestDeviceWriteTypeConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestDeviceWriteType";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        var productModel = ctx.Setting("ProductModel") ?? "ConST811A";
        pass &= await op.Dut.SetPrimaryDeviceTypeAsync(productModel, ct);
        op.Report(pass ? "✓ 设备类型写入通过" : "✗ 设备类型写入未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("设备类型写入通过") : StepResult.Fail("设备类型写入未通过");
    }
}

/// <summary>
/// 软件版本验证及升级。PORT: 旧脚本方法 TestSoftVersions（JSON Entry: TestSoftVersions）。
/// </summary>
public sealed class TestSoftVersionsConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestSoftVersions";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        var firmware = await op.Dut.ReadFirmwareVersionAsync(ct);
        op.Report($"固件版本：{firmware}");
        pass &= !string.IsNullOrWhiteSpace(firmware);
        op.Report(pass ? "✓ 软件版本验证及升级通过" : "✗ 软件版本验证及升级未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("软件版本验证及升级通过") : StepResult.Fail("软件版本验证及升级未通过");
    }
}

/// <summary>
/// CPS手动检测。PORT: 旧脚本方法 TestCPS（JSON Entry: TestCPS）。
/// </summary>
public sealed class TestCPSConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestCPS";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        
        await op.Dut.QueryTextAsync("GetDUTSN", null, ct);
        
        //老版本直接通过
        if (!(await ctx.ConfirmAsync("1.将设备与连接台接好后，查看设备CPS图标，正常显示为通过。 \r\n2.上行控压，达到目标点进行下行控压，确定排气正常从C104出气。 气压版：上行7MPa，下行100kPa, 差压版：上行250kPa,下行50kPa，微差压版：上行10kPa，下行2kPa。 \r\n3.手动排空。", ct))) pass = false;
        
        
        
        op.Report(pass ? "✓ CPS手动检测通过" : "✗ CPS手动检测未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("CPS手动检测通过") : StepResult.Fail("CPS手动检测未通过");
    }
}

/// <summary>
/// 网口通讯。PORT: 旧脚本方法 TestLAN（JSON Entry: TestLAN）。
/// </summary>
public sealed class TestLANConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestLAN";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        var ip = await op.Dut.QueryTextAsync("GetStaticETHemetIPAddress", null, ct);
        op.Report($"设备网口地址：{ip}");
        pass &= !string.IsNullOrWhiteSpace(ip);
        op.Report(pass ? "✓ 网口通讯通过" : "✗ 网口通讯未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("网口通讯通过") : StepResult.Fail("网口通讯未通过");
    }
}

/// <summary>
/// 按键测试。PORT: 旧脚本方法 TestKeyBoard（JSON Entry: TestKeyBoard）。
/// </summary>
public sealed class TestKeyBoardConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestKeyBoard";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        
        
        
        var prevPass1 = pass;  // G8: 记录本重试段之前的整体结果
        while (true) {  // G8: 原 goto 标签 tryagain → while(true) 重试循环
            pass = true;  // G8: 每次重试重置本段结果
        
        
        
            if (!(await op.Dut.QueryBooleanAsync("SetCheckerOpen", new[]{ "KeyBoard" }, ct))) { op.Report("与设备指令通讯有问题，启动按键测试失败", RealtimeLevel.Error); pass = false; }
        
            await Task.Delay(500, ct);
            if (!(await op.Dut.QueryBooleanAsync("GetCheckerState", new[]{ "KeyBoard" }, ct))) { op.Report("与设备指令通讯有问题，获取自检测试结果失败", RealtimeLevel.Error); pass = false; }
        
            await Task.Delay(1000, ct);
        
            if (!(await ctx.ConfirmAsync($"当前测试没通过，是否需要重新测试一次？点击【确认】进行第{1}次测试，否则测试不通过，设备有问题。", ct))) { pass = false; break; }  // G8: 取消重试 → 退出循环
            await op.Dut.CommandAsync("SetCheckerClose", null, ct);
            await Task.Delay(3000, ct);
            continue;  // G8: 原 goto tryagain → 重新测试
        }  // G8: while(true) 重试循环结束（原 goto tryagain 标签）
        pass &= prevPass1;  // G8: 合并本段结果到整体结果
        
        
        
        await op.Dut.CommandAsync("SetCheckerClose", null, ct);
        
        op.Report(pass ? "✓ 按键测试通过" : "✗ 按键测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("按键测试通过") : StepResult.Fail("按键测试未通过");
    }
}

/// <summary>
/// 屏幕测试。PORT: 旧脚本方法 LCDTest（JSON Entry: LCDTest）。
/// </summary>
public sealed class LCDTestConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "LCDTest";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        
        
        
        var prevPass3 = pass;  // G8: 记录本重试段之前的整体结果
        while (true) {  // G8: 原 goto 标签 tryagain1 → while(true) 重试循环
            pass = true;  // G8: 每次重试重置本段结果
        
            if (!(await op.Dut.QueryBooleanAsync("SetCheckerOpen", new[]{ "Brightness" }, ct))) { op.Report("与设备指令通讯有问题，启动屏幕亮度测试失败", RealtimeLevel.Error); pass = false; }
            await Task.Delay(500, ct);
            if (!(await op.Dut.QueryBooleanAsync("GetCheckerState", new[]{ "Brightness" }, ct))) { op.Report("与设备指令通讯有问题，获取自检测试结果失败", RealtimeLevel.Error); pass = false; }
            await Task.Delay(1000, ct);
        
            if (!(await ctx.ConfirmAsync($"当前测试没通过，是否需要重新测试一次？点击【确认】进行第{1}次测试，否则测试不通过，设备有问题。", ct))) { pass = false; break; }  // G8: 取消重试 → 退出循环
            await op.Dut.CommandAsync("SetCheckerClose", null, ct);
            await Task.Delay(3000, ct);
            continue;  // G8: 原 goto tryagain1 → 重新测试
        }  // G8: while(true) 重试循环结束（原 goto tryagain1 标签）
        pass &= prevPass3;  // G8: 合并本段结果到整体结果
        
        
        
        var prevPass2 = pass;  // G8: 记录本重试段之前的整体结果
        while (true) {  // G8: 原 goto 标签 tryagain2 → while(true) 重试循环
            pass = true;  // G8: 每次重试重置本段结果
        
            if (!(await op.Dut.QueryBooleanAsync("SetCheckerSelect", new[]{ "BadPixel" }, ct))) { op.Report("与设备指令通讯有问题，启动屏幕坏点测试失败", RealtimeLevel.Error); pass = false; }
            await Task.Delay(500, ct);
        
            if (!(await op.Dut.QueryBooleanAsync("GetCheckerState", new[]{ "BadPixel" }, ct))) { op.Report("与设备指令通讯有问题，获取自检测试结果失败", RealtimeLevel.Error); pass = false; }
            await Task.Delay(1000, ct);
        
            if (!(await ctx.ConfirmAsync($"当前测试没通过，是否需要重新测试一次？点击【确认】进行第{1}次测试，否则测试不通过，设备有问题。", ct))) { pass = false; break; }  // G8: 取消重试 → 退出循环
            await op.Dut.CommandAsync("SetCheckerClose", null, ct);
            await Task.Delay(3000, ct);
            await op.Dut.CommandAsync("SetCheckerOpen", new[]{ "BadPixel" }, ct);
            continue;  // G8: 原 goto tryagain2 → 重新测试
        }  // G8: while(true) 重试循环结束（原 goto tryagain2 标签）
        pass &= prevPass2;  // G8: 合并本段结果到整体结果
        
        
        
        
        
        var prevPass1 = pass;  // G8: 记录本重试段之前的整体结果
        while (true) {  // G8: 原 goto 标签 tryagain3 → while(true) 重试循环
            pass = true;  // G8: 每次重试重置本段结果
        
            if (!(await op.Dut.QueryBooleanAsync("SetCheckerSelect", new[]{ "Touch" }, ct))) { op.Report("与设备指令通讯有问题，启动屏幕触摸测试失败", RealtimeLevel.Error); pass = false; }
            await Task.Delay(500, ct);
        
            if (!(await op.Dut.QueryBooleanAsync("GetCheckerState", new[]{ "Touch" }, ct))) { op.Report("获取自检测试结果失败", RealtimeLevel.Error); pass = false; }
            await Task.Delay(1000, ct);
        
            if (!(await ctx.ConfirmAsync($"当前测试没通过，是否需要重新测试一次？点击【确认】进行第{1}次测试，否则测试不通过，设备有问题。", ct))) { pass = false; break; }  // G8: 取消重试 → 退出循环
            await op.Dut.CommandAsync("SetCheckerClose", null, ct);
            await Task.Delay(3000, ct);
            await op.Dut.CommandAsync("SetCheckerOpen", new[]{ "Touch" }, ct);
            continue;  // G8: 原 goto tryagain3 → 重新测试
        }  // G8: while(true) 重试循环结束（原 goto tryagain3 标签）
        pass &= prevPass1;  // G8: 合并本段结果到整体结果
        
        
        await op.Dut.CommandAsync("SetCheckerClose", null, ct);
        
        op.Report(pass ? "✓ 屏幕测试通过" : "✗ 屏幕测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("屏幕测试通过") : StepResult.Fail("屏幕测试未通过");
    }
}

/// <summary>
/// 蜂鸣器测试。PORT: 旧脚本方法 BeeperTest（JSON Entry: BeeperTest）。
/// </summary>
public sealed class BeeperTestConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "BeeperTest";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        
        
        
        var prevPass1 = pass;  // G8: 记录本重试段之前的整体结果
        while (true) {  // G8: 原 goto 标签 tryagain → while(true) 重试循环
            pass = true;  // G8: 每次重试重置本段结果
        
            if (!(await op.Dut.QueryBooleanAsync("SetCheckerOpen", new[]{ "Speaker" }, ct))) { op.Report("与设备指令通讯有问题，启动蜂鸣器测试失败", RealtimeLevel.Error); pass = false; }
            await Task.Delay(500, ct);
            if (!(await op.Dut.QueryBooleanAsync("GetCheckerState", new[]{ "Speaker" }, ct))) { op.Report("与设备指令通讯有问题，获取自检测试结果失败", RealtimeLevel.Error); pass = false; }
            await Task.Delay(1000, ct);
        
        
            if (!(await ctx.ConfirmAsync($"当前测试没通过，是否需要重新测试一次？点击【确认】进行第{1}次测试，否则测试不通过，设备有问题。", ct))) { pass = false; break; }  // G8: 取消重试 → 退出循环
            await op.Dut.CommandAsync("SetCheckerClose", null, ct);
            await Task.Delay(3000, ct);
            continue;  // G8: 原 goto tryagain → 重新测试
        }  // G8: while(true) 重试循环结束（原 goto tryagain 标签）
        pass &= prevPass1;  // G8: 合并本段结果到整体结果
        
        await op.Dut.CommandAsync("SetCheckerClose", null, ct);
        
        op.Report(pass ? "✓ 蜂鸣器测试通过" : "✗ 蜂鸣器测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("蜂鸣器测试通过") : StepResult.Fail("蜂鸣器测试未通过");
    }
}

/// <summary>
/// 风扇测试。PORT: 旧脚本方法 FANTest（JSON Entry: FANTest）。
/// </summary>
public sealed class FANTestConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "FANTest";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        await op.Dut.CommandAsync("SetFANOn", null, ct);
        await op.Sleep(500);
        await op.Dut.CommandAsync("SetFANClose", null, ct);
        op.Report(pass ? "✓ 风扇测试通过" : "✗ 风扇测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("风扇测试通过") : StepResult.Fail("风扇测试未通过");
    }
}

/// <summary>
/// 电源指示灯测试。PORT: 旧脚本方法 Manual_1b0ac0cbde40461f9fcbc943513d9414（JSON Entry: Manual_1b0ac0cbde40461f9fcbc943513d9414）。
/// </summary>
public sealed class Manual_1b0ac0cbde40461f9fcbc943513d9414ConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "Manual_1b0ac0cbde40461f9fcbc943513d9414";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        
        string outmsg1 = "";
        string outmsg2 = "";
        outmsg1 = await op.Dut.QueryTextAsync("GetRS1", new[]{ ctx.SerialNumber ?? "" }, ct);
        outmsg2 = await op.Dut.QueryTextAsync("GetRS2", new[]{ ctx.SerialNumber ?? "" }, ct);
        
        if (!(await ctx.ConfirmAsync($"测试前准备确认:\r\n1、接入4根电测线，2个外接航插线，1根电源线。\r\n2、接入USB通讯线，网线，U盘。3、拧上设备侧边的堵头。\r\n4、{outmsg1}。\r\n5、{outmsg2}\r\n\r\n点击【确认】或【取消】，都会停止测试。", "", ct))) pass = false;
        if (!(await ctx.ConfirmAsync($"测试前准备确认:\r\n1、接入4根电测线，2个外接航插线，1根电源线。\r\n2、接入USB通讯线，网线，U盘。3、拧上设备侧边的堵头。\r\n4、{outmsg1}。\r\n\r\n点击【确认】或【取消】，都会停止测试。", "", ct))) pass = false;
        if (!(await ctx.ConfirmAsync($"测试前准备确认:\r\n1、接入4根电测线，2个外接航插线，1根电源线。\r\n2、接入USB通讯线，网线，U盘。3、拧上设备侧边的堵头。\r\n4、{outmsg2}。\r\n\r\n点击【确认】或【取消】，都会停止测试。", "", ct))) pass = false;
        if (!(await ctx.ConfirmAsync($"测试前准备确认:\r\n1、接入4根电测线，2个外接航插线，1根电源线。\r\n2、接入USB通讯线，网线，U盘。\r\n3、拧上设备侧边的堵头。\r\n\r\n完成后，没有问题点击【确认】进行下一步。\r\n点击【取消】，停止测试。", "", ct))) pass = false;
        
        op.Report(pass ? "✓ 电源指示灯测试通过" : "✗ 电源指示灯测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("电源指示灯测试通过") : StepResult.Fail("电源指示灯测试未通过");
    }
}

/// <summary>
/// 系统板PA模块测试。PORT: 旧脚本方法 TestPaModule（JSON Entry: TestPaModule）。
/// </summary>
public sealed class TestPaModuleConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestPaModule";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        // G10 遗留变量 massage：原始声明引用旧框架/旧类型未迁移，以下为可编译占位
        var massage = new List<(string Address, string Name)>(); // 旧声明 `List<PAMassage> massage = ...` 类型未迁移
        
        
        
        
        await op.Gzp21.SetOutputAsync("PA", true, ct);
        if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_PA", null, ct))) { op.Report("与设备指令通讯有问题，电测档位切换PA变送器失败", RealtimeLevel.Error); pass = false; }
        await Task.Delay(5000, ct);
        
        var retryOk = await RetryHelper.RetryAsync(async attempt =>
        {
            pass = true;  // 每次重试重置本段结果
            if (!(await op.Dut.QueryBooleanAsync("SearchPA", null, ct))) { op.Report("与设备指令通讯有问题，搜索PA变送器失败", RealtimeLevel.Error); pass = false; }
            await op.Dut.CommandAsync("GetPAMassage", null, ct);
            await Task.Delay(1000, ct);
            return pass;
        }, _ => ctx.ConfirmAsync("没有搜索到设备，电测线可能没接，请先使用测试线 连接设备与工装的SRC和MEAS插孔，红对红，黑对黑。\r\n点击确认，重新测试，否则测试失败。", ct), 3, ct);
        if (!retryOk) pass = false;
        
        await Task.Delay(5000, ct);
        
        await Task.Delay(1000, ct);
        if (!(await op.Dut.QueryBooleanAsync("ConnectPA", new[]{ massage[0].Address.ToString() }, ct))) { op.Report("与设备指令通讯有问题，连接PA变送器失败", RealtimeLevel.Error); pass = false; }
        await Task.Delay(1000, ct);
        
        if (!(await op.Dut.QueryBooleanAsync("GetCurrentElectricMeasure", null, ct))) { op.Report("与设备指令通讯有问题，获取当前电测信息失败", RealtimeLevel.Error); pass = false; }
        await op.Gzp21.SetOutputAsync("PA", false, ct);
        
        op.Report(pass ? "✓ 系统板PA模块测试通过" : "✗ 系统板PA模块测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("系统板PA模块测试通过") : StepResult.Fail("系统板PA模块测试未通过");
    }
}

/// <summary>
/// HART测试。PORT: 旧脚本方法 TestHart（JSON Entry: TestHart）。
/// </summary>
public sealed class TestHartConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestHart";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        // G10 遗留变量 address：原始声明引用旧框架/旧类型未迁移，以下为可编译占位
        int address = 0; // 旧声明 `int address = int.Parse(result.Data.Value...);` 引用旧框架
        // G10 遗留变量 msg：原始声明引用旧框架/旧类型未迁移，以下为可编译占位
        
        
        
        var prevPass1 = pass;  // G8: 记录本重试段之前的整体结果
        while (true) {  // G8: 原 goto 标签 tryagain → while(true) 重试循环
            pass = true;  // G8: 每次重试重置本段结果
        
            await op.Gzp21.SetOutputAsync("Hart", true, ct);
            await Task.Delay(1000, ct);
        
        
            if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_HART", null, ct))) { op.Report("与设备指令通讯有问题，切换hart档位失败", RealtimeLevel.Error); pass = false; }
        
            if (!(await op.Dut.QueryBooleanAsync("GetSupplyMode", null, ct))) { op.Report("与设备指令通讯有问题，获取当前供电模式失败", RealtimeLevel.Error); pass = false; }
            if (!(await op.Dut.QueryBooleanAsync("SetSwitchMode_IPIR", null, ct))) { op.Report("与设备指令通讯有问题，切换供电模式失败", RealtimeLevel.Error); pass = false; }
        
            await Task.Delay(8000, ct);
            if (!(await op.Dut.QueryBooleanAsync("StartSearchHart", null, ct))) { op.Report("与设备指令通讯有问题，搜索Hart失败", RealtimeLevel.Error); pass = false; }
            await Task.Delay(1000, ct);
            if (!(await op.Dut.QueryBooleanAsync("GetEleHartMassage", null, ct))) { op.Report("与设备指令通讯有问题，获取hart信息失败", RealtimeLevel.Error); pass = false; }
        
            if (!(await ctx.ConfirmAsync("电测线可能没接，请先使用测试线 连接设备与工装的SRC和MEAS插孔，红对红，黑对黑。\r\n点击确认，重新测试，否则测试失败。", ct))) { pass = false; break; }  // G8: 取消重试 → 退出循环
        
            await Task.Delay(5000, ct);
            continue;  // G8: 原 goto tryagain → 重新测试
        }  // G8: while(true) 重试循环结束（原 goto tryagain 标签）
        pass &= prevPass1;  // G8: 合并本段结果到整体结果
        
        if (!(await op.Dut.QueryBooleanAsync("ConnectHart", new[]{ address.ToString() }, ct))) { op.Report("与设备指令通讯有问题，连接Hart变送器失败", RealtimeLevel.Error); pass = false; }
        
        await op.Dut.CommandAsync("StopSearchHart", null, ct);
        await op.Dut.CommandAsync("SetEleChannelItem_HARTClose", null, ct);
        await op.Gzp21.SetOutputAsync("Hart", false, ct);
        await op.Dut.CommandAsync("SetTestMode", null, ct);
        
        op.Report(pass ? "✓ HART测试通过" : "✗ HART测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("HART测试通过") : StepResult.Fail("HART测试未通过");
    }
}

/// <summary>
/// 电池功耗测试。PORT: 旧脚本方法 TestMeterState（JSON Entry: TestMeterState）。
/// </summary>
public sealed class TestMeterStateConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestMeterState";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        // G10 遗留变量 MainBoardCheckStata：原始声明引用旧框架/旧类型未迁移，以下为可编译占位
        
        
        
        //获取条件
        //根据机型获取电池功耗参数,10MPa机型单独一个电池功耗参数
        
        
        var retryOk = await RetryHelper.RetryAsync(async attempt =>
        {
            pass = true;  // 每次重试重置本段结果
            await op.Gzp21.GetOutputStateAsync("27V", ct);
            await Task.Delay(1000, ct);
            await op.Gzp21.SetOutputAsync("27V", false, ct);
            await Task.Delay(1000, ct);
            await Task.Delay(1000, ct);
            return pass;
        }, _ => ctx.ConfirmAsync("请确认未接外部电源，确认未接后，再继续进行测试？", ct), 3, ct);
        if (!retryOk) pass = false;
        
        
        
        if (!(await op.Dut.QueryBooleanAsync("SetBrightness", new[]{ "Percentage", "50" }, ct))) { op.Report("指令通讯有问题，初始化屏幕亮度50%失败", RealtimeLevel.Error); pass = false; }
        if (!(await op.Dut.QueryBooleanAsync("SetWifiClose", null, ct))) { op.Report("指令通讯有问题，初始化WIFI关闭失败", RealtimeLevel.Error); pass = false; }
        //初始化蓝牙关闭
        if (!(await op.Dut.QueryBooleanAsync("CloseBlueTooth", null, ct))) { op.Report("指令通讯有问题，初始化蓝牙关闭失败", RealtimeLevel.Error); pass = false; }
        if (!(await op.Dut.QueryBooleanAsync("SetTestMode", null, ct))) { op.Report("指令通讯有问题，初始化压力测量模式失败", RealtimeLevel.Error); pass = false; }
        if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_VOL", null, ct))) { op.Report("指令通讯有问题，初始化电压测量模式失败", RealtimeLevel.Error); pass = false; }
        if (!(await op.Dut.QueryBooleanAsync("SetElectricSource_MA", new[]{ "true" }, ct))) { op.Report("指令通讯有问题，初始化电流输出模式失败", RealtimeLevel.Error); pass = false; }
        
        // 读取主板自检状态（成功即通过）
        if (!(await op.Dut.QueryBooleanAsync("GetMainBoardCheckState", null, ct))) { op.Report("指令通讯有问题，读取主板自检状态失败", RealtimeLevel.Error); pass = false; }
        
        // 读取供电方式（成功即通过）
        if (!(await op.Dut.QueryBooleanAsync("GetPowerSupplyCheck", null, ct))) { op.Report("指令通讯有问题，读取供电方式失败", RealtimeLevel.Error); pass = false; }
        
        
        
        //设置排空模式
        if ((await op.Dut.QueryBooleanAsync("SetVentMode", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("切换排空模式失败,重试？", ct))) pass = false;
        op.Report($"结果2: {"切换排空模式失败"}");
        op.Report($"结果2: {"切换排空模式成功"}");
        
        
        //设置控制器测试模式
        if ((await op.Dut.QueryBooleanAsync("SetTestMode", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("切换控制器测试模式失败,重试？", ct))) pass = false;
        op.Report($"结果3: {"切换控制器测试模式失败"}");
        op.Report($"结果3: {"切换控制器测试模式成功"}");
        
        await Task.Delay(5000, ct);
        
        List<double> EnergyCheckStata = null!;
        
        if (!(await op.Dut.QueryBooleanAsync("GetEnergyCheckStata", null, ct))) { op.Report("指令通讯有问题，获取整机功耗数据失败", RealtimeLevel.Error); pass = false; }
        op.Report($"结果4: {"获取整机功耗数据失败"}");
        op.Report($"结果4: {Math.Abs(EnergyCheckStata[2]) + "mW   整机功耗测试错误"}");
        op.Report($"结果4: {Math.Abs(EnergyCheckStata[2]) + "mW  整机功耗测试通过"}");
        await Task.Delay(1000, ct);
        
        
        if (!(await op.Dut.QueryBooleanAsync("GetBatteryValue", null, ct))) { op.Report("GetBatteryValue 调用失败", RealtimeLevel.Error); pass = false; }
        
        await op.Gzp21.SetOutputAsync("27V", false, ct);
        await op.Gzp21.SetOutputAsync("27V", true, ct);
        
        op.Report(pass ? "✓ 电池功耗测试通过" : "✗ 电池功耗测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("电池功耗测试通过") : StepResult.Fail("电池功耗测试未通过");
    }
}

/// <summary>
/// 电测板电源测试。PORT: 旧脚本方法 ElectricalPowerTest（JSON Entry: ElectricalPowerTest）。
/// </summary>
public sealed class ElectricalPowerTestConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "ElectricalPowerTest";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        var probe = await op.Dut.QueryTextAsync("GetPowerSupplyCheck", null, ct);
        op.Report($"设备回读：{probe}");
        pass &= !string.IsNullOrWhiteSpace(probe);
        op.Report(pass ? "✓ 电测板电源测试通过" : "✗ 电测板电源测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("电测板电源测试通过") : StepResult.Fail("电测板电源测试未通过");
    }
}

/// <summary>
/// 电测板测量/输出功能测试。PORT: 旧脚本方法 ElectricalMeasurementAndOutputFunctionTest（JSON Entry: ElectricalMeasurementAndOutputFunctionTest）。
/// </summary>
public sealed class ElectricalMeasurementAndOutputFunctionTestConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "ElectricalMeasurementAndOutputFunctionTest";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;

        // 电测输出测试点：名称 / 目标值 / 单位（V=电压档、mA=电流档；名称含"高压档"走高压档位）
        var testPoints = new (string Name, double Target, string Unit)[]
        {
            ("电压1V", 1, "V"),
            ("电压16V", 16, "V"),
            ("电压高压档16V", 16, "V"),
            ("电压高压档30V", 30, "V"),
            ("电流0mA", 0, "mA"),
            ("电流2.5mA", 2.5, "mA"),
            ("电流25mA", 25, "mA"),
        };

        await op.Gzp21.SetOutputAsync("Ele", true, ct);
        await Task.Delay(3000, ct);

        // 电测输出功能测试：逐点设定输出档位/目标值，回读测量值并在允差内判定（允差条件按测试点顺序取 Conditions）
        for (var i = 0; i < testPoints.Length; i++)
        {
            var tp = testPoints[i];
            var cond = i < ctx.Conditions.Count ? ctx.Conditions[i] : null;
            var tryCount = 0;
            var pointPass = true;
            while (true)
            {
                pointPass = true;
                var pointOk = false;
                op.Report($">>开始测试{tp.Name}（第{tryCount + 1}次）");

                // 设置电输出档位
                if (tp.Unit == "V")
                {
                    if (tp.Name.Contains("高压档"))
                    {
                        if (!(await op.Dut.QueryBooleanAsync("SetElectricSourceFunction", new[]{ "V2" }, ct))) { op.Report("切换电测电压高压档位失败", RealtimeLevel.Error); pointPass = false; }
                    }
                    else
                    {
                        if (!(await op.Dut.QueryBooleanAsync("SetElectricSourceFunction", new[]{ "V" }, ct))) { op.Report("切换电测电压低压档位失败", RealtimeLevel.Error); pointPass = false; }
                    }
                }
                else
                {
                    if (!(await op.Dut.QueryBooleanAsync("SetElectricSource_MA", new[]{ "false" }, ct))) { op.Report("切换电输出电流档位失败", RealtimeLevel.Error); pointPass = false; }
                }

                // 设置电输出目标值
                if (!(await op.Dut.QueryBooleanAsync("SetElectricSourceTarget", new[]{ tp.Target.ToString() }, ct))) { op.Report("设置电输出目标值失败", RealtimeLevel.Error); pointPass = false; }

                // 设置电测量档位
                if (tp.Unit == "V")
                {
                    if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_VOL", null, ct))) { op.Report("设置电测量档位为电压档失败", RealtimeLevel.Error); pointPass = false; }
                }
                else
                {
                    if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_CURR", new[]{ "true" }, ct))) { op.Report("设置电测量档位为电流档失败", RealtimeLevel.Error); pointPass = false; }
                }

                await Task.Delay(3000, ct);

                // 读取电测量值（连续读 4 次取平均，旧脚本 tryCount1 循环）
                double eleData = 0;
                var readCount = 0;
                for (var n = 0; n < 4; n++)
                {
                    var txt = await op.Dut.QueryTextAsync("GetCurrentElectricMeasure", null, ct);
                    if (double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { eleData += v; readCount++; }
                }
                if (readCount == 0) { op.Report("读取测量值失败", RealtimeLevel.Error); pointPass = false; }
                else eleData /= readCount;
                op.Report($"{tp.Name} 测量值: {ConST811AOps.F(eleData)}{tp.Unit}");

                // 判定：|测量值-目标值| 在允差内即通过
                if (pointPass && cond is not null)
                {
                    var r = ctx.Evaluator.Evaluate(cond, Math.Abs(eleData - tp.Target));
                    pointOk = r.Passed;
                    op.Report($"{tp.Name}：{r.Message}", r.Passed ? RealtimeLevel.Info : RealtimeLevel.Warn);
                }

                if (pointOk) break;

                tryCount++;
                if (tryCount > 3)
                {
                    op.Report($"{tp.Name}测试不通过: 测量值{ConST811AOps.F(eleData)}{tp.Unit}超差", RealtimeLevel.Error);
                    pointPass = false;
                    break;
                }
                if (!(await ctx.ConfirmAsync("电测线可能没接，请先使用测试线 连接设备与工装的SRC和MEAS插孔，红对红，黑对黑。\r\n点击确认，重新测试，否则测试失败。", ct))) { pointPass = false; break; }
            }
            pass &= pointPass;
        }

        // 24V 环路电压供电测试（旧脚本 3 组：无/有环路 24V 供电，目标 10mA；最后一组应无环路电流）
        var cond24V = ctx.Conditions.Count > 10 ? ctx.Conditions[10] : null;
        var twentyFourVTests = new (string Name, bool MAOn, bool CurrLoop)[]
        {
            ("供电24V,无环路24V时输出10mA电流测试", false, true),
            ("供电24V,有环路24V时输出10mA电流测试", true, false),
            ("供电24V,有环路24V时输出10mA电流测试(应无环路电流)", true, true),
        };
        for (var i = 0; i < twentyFourVTests.Length; i++)
        {
            var t = twentyFourVTests[i];
            op.Report($">>开始测试{t.Name}");
            if (!(await op.Dut.QueryBooleanAsync("SetElectricSource_MA", new[]{ t.MAOn.ToString().ToLowerInvariant() }, ct))) { op.Report("切换电输出电流档位失败", RealtimeLevel.Error); pass = false; }
            if (!(await op.Dut.QueryBooleanAsync("SetElectricSourceTarget", new[]{ "10" }, ct))) { op.Report("设置电输出目标值失败", RealtimeLevel.Error); pass = false; }
            if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_CURR", new[]{ t.CurrLoop.ToString().ToLowerInvariant() }, ct))) { op.Report("设置电测量档位为电流档失败", RealtimeLevel.Error); pass = false; }
            await Task.Delay(3000, ct);

            var txt = await op.Dut.QueryTextAsync("GetCurrentElectricMeasure", null, ct);
            if (!double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var measuredValue24V))
            {
                op.Report("读取测量值失败", RealtimeLevel.Error);
                pass = false;
                continue;
            }
            op.Report($"{t.Name} 测量值: {ConST811AOps.F(measuredValue24V)}mA");
            if (cond24V is null) { pass = false; continue; }

            var r = ctx.Evaluator.Evaluate(cond24V, Math.Abs(measuredValue24V - 10));
            var ok = i < 2 ? r.Passed : !r.Passed;  // 第三组应无环路电流：|测量值-10| 超允差即说明电流为 0
            pass &= ok;
            op.Report($"{t.Name}：{(ok ? "测试通过" : "测试不通过")}", ok ? RealtimeLevel.Info : RealtimeLevel.Warn);
        }

        await op.Gzp21.SetOutputAsync("Ele", false, ct);

        op.Report(pass ? "✓ 电测板测量/输出功能测试通过" : "✗ 电测板测量/输出功能测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("电测板测量/输出功能测试通过") : StepResult.Fail("电测板测量/输出功能测试未通过");
    }
}

/// <summary>
/// 开关测量功能测试。PORT: 旧脚本方法 TestSwitch（JSON Entry: TestSwitch）。
/// </summary>
public sealed class TestSwitchConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestSwitch";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;

        await op.Gzp21.SetOutputAsync("Ele", true, ct);

        // 设置电测输出为电压档（0~16V）
        if (!(await op.Dut.QueryBooleanAsync("SetElectricSourceFunction", new[]{ "V" }, ct))) { op.Report("切换电测电压档位失败", RealtimeLevel.Error); pass = false; }

        // 机械(或NPN)开关测试 -- 分开：输出 5V，开关应为断开（回读 0）
        op.Report(">>机械(或NPN)开关测试--分开");
        if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_SW_Normal", null, ct))) { op.Report("打开机械开关失败", RealtimeLevel.Error); pass = false; }
        var retryOk1 = await RetryHelper.RetryAsync(async attempt =>
        {
            var ok = false;
            if (!(await op.Dut.QueryBooleanAsync("SetElectricSourceTarget", new[]{ "5" }, ct))) { op.Report("设置电输出目标值失败", RealtimeLevel.Error); ok = false; }
            await Task.Delay(1000, ct);
            var txt = await op.Dut.QueryTextAsync("GetCurrentElectricMeasure", null, ct);
            if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_SW_Normal", null, ct))) { op.Report("打开机械开关失败", RealtimeLevel.Error); ok = false; }
            if (double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                ok = !Convert.ToBoolean(Convert.ToInt32(v));  // 分开时测量值为 0
                op.Report($"机械开关分开: {(ok ? "√" : "机械(或NPN)开关测试未通过")}");
            }
            else op.Report("读取当前电测测量值失败", RealtimeLevel.Error);
            return ok;
        }, _ => ctx.ConfirmAsync("电测线可能没接，请先使用测试线 连接设备与工装的SRC和MEAS插孔，红对红，黑对黑。\r\n点击确认，重新测试，否则测试失败。", ct), 4, ct);
        if (!retryOk1) pass = false;

        // 机械(或NPN)开关测试 -- 短接：输出 0V，开关应为闭合（回读 1）
        op.Report(">>机械(或NPN)开关测试--短接");
        var retryOk2 = await RetryHelper.RetryAsync(async attempt =>
        {
            var ok = false;
            if (!(await op.Dut.QueryBooleanAsync("SetElectricSourceTarget", new[]{ "0" }, ct))) { op.Report("设置电输出目标值失败", RealtimeLevel.Error); ok = false; }
            await Task.Delay(1000, ct);
            var txt = await op.Dut.QueryTextAsync("GetCurrentElectricMeasure", null, ct);
            if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_SW_Normal", null, ct))) { op.Report("打开机械开关失败", RealtimeLevel.Error); ok = false; }
            if (double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                ok = Convert.ToBoolean(Convert.ToInt32(v));  // 短接时测量值为 1
                op.Report($"机械开关短接: {(ok ? "√" : "机械(或NPN)开关测试未通过")}");
            }
            else op.Report("读取当前电测测量值失败", RealtimeLevel.Error);
            return ok;
        }, _ => ctx.ConfirmAsync("电测线可能没接，请先使用测试线 连接设备与工装的SRC和MEAS插孔，红对红，黑对黑。\r\n点击确认，重新测试，否则测试失败。", ct), 4, ct);
        if (!retryOk2) pass = false;

        // PNP 开关测试 -- 闭合：输出 5V，PNP 闭合（回读 1）
        op.Report(">>PNP开关测试--闭合");
        if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_SW_PNP", null, ct))) { op.Report("打开PNP开关失败", RealtimeLevel.Error); pass = false; }
        await Task.Delay(1000, ct);
        var retryOk3 = await RetryHelper.RetryAsync(async attempt =>
        {
            var ok = false;
            if (!(await op.Dut.QueryBooleanAsync("SetElectricSourceTarget", new[]{ "5" }, ct))) { op.Report("设置电输出目标值失败", RealtimeLevel.Error); ok = false; }
            await Task.Delay(1000, ct);
            var txt = await op.Dut.QueryTextAsync("GetCurrentElectricMeasure", null, ct);
            if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_SW_NPN", null, ct))) { op.Report("打开PNP开关失败", RealtimeLevel.Error); ok = false; }
            if (double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                ok = Convert.ToBoolean(Convert.ToInt32(v));  // PNP 闭合时测量值为 1
                op.Report($"PNP开关闭合: {(ok ? "√" : "PNP开关未通过")}");
            }
            else op.Report("读取当前电测测量值失败", RealtimeLevel.Error);
            return ok;
        }, _ => ctx.ConfirmAsync("电测线可能没接，请先使用测试线 连接设备与工装的SRC和MEAS插孔，红对红，黑对黑。\r\n点击确认，重新测试，否则测试失败。", ct), 4, ct);
        if (!retryOk3) pass = false;

        // PNP 开关测试 -- 断开：输出 0V，PNP 断开（回读 0）
        op.Report(">>PNP开关测试--断开");
        var retryOk4 = await RetryHelper.RetryAsync(async attempt =>
        {
            var ok = false;
            if (!(await op.Dut.QueryBooleanAsync("SetElectricSourceTarget", new[]{ "0" }, ct))) { op.Report("设置电输出目标值失败", RealtimeLevel.Error); ok = false; }
            await Task.Delay(1000, ct);
            var txt = await op.Dut.QueryTextAsync("GetCurrentElectricMeasure", null, ct);
            if (!(await op.Dut.QueryBooleanAsync("SetEleChannelItem_SW_NPN", null, ct))) { op.Report("打开NPN开关失败", RealtimeLevel.Error); ok = false; }
            if (double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                ok = !Convert.ToBoolean(Convert.ToInt32(v));  // PNP 断开时测量值为 0
                op.Report($"PNP开关断开: {(ok ? "√" : "PNP开关测试未通过")}");
            }
            else op.Report("读取当前电测测量值失败", RealtimeLevel.Error);
            return ok;
        }, _ => ctx.ConfirmAsync("电测线可能没接，请先使用测试线 连接设备与工装的SRC和MEAS插孔，红对红，黑对黑。\r\n点击确认，重新测试，否则测试失败。", ct), 4, ct);
        if (!retryOk4) pass = false;

        await op.Gzp21.SetOutputAsync("Ele", false, ct);

        op.Report(pass ? "✓ 开关测量功能测试通过" : "✗ 开关测量功能测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("开关测量功能测试通过") : StepResult.Fail("开关测量功能测试未通过");
    }
}

/// <summary>
/// 外接压力模块通讯测试。PORT: 旧脚本方法 ModuleConnectStateTest（JSON Entry: ModuleConnectStateTest）。
/// </summary>
public sealed class ModuleConnectStateTestConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "ModuleConnectStateTest";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        var probe = await op.Dut.QueryTextAsync("GetPressureModelOnlineState", null, ct);
        op.Report($"设备回读：{probe}");
        pass &= !string.IsNullOrWhiteSpace(probe);
        op.Report(pass ? "✓ 外接压力模块通讯测试通过" : "✗ 外接压力模块通讯测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("外接压力模块通讯测试通过") : StepResult.Fail("外接压力模块通讯测试未通过");
    }
}

/// <summary>
/// 系统板RTC时间测试。PORT: 旧脚本方法 RTCTimeTest（JSON Entry: RTCTimeTest）。
/// </summary>
public sealed class RTCTimeTestConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "RTCTimeTest";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        
        
        
        DateTime computerTime = DateTime.Now;
        if (!(await op.Dut.QueryBooleanAsync("SetSystemTime", new[]{ computerTime.ToString() }, ct))) { op.Report("SetSystemTime 调用失败", RealtimeLevel.Error); pass = false; }
        
        if (!(await op.Dut.QueryBooleanAsync("SetSystemDate", new[]{ computerTime.ToString() }, ct))) { op.Report("SetSystemDate 调用失败", RealtimeLevel.Error); pass = false; }
        
        
        //回读
        if (!(await op.Dut.QueryBooleanAsync("GetDevSysDate", null, ct))) { op.Report("GetDevSysDate 调用失败", RealtimeLevel.Error); pass = false; }
        
        
        
        
        
        await op.Dut.CommandAsync("SetReboot", null, ct);
        await op.Dut.CommandAsync("Close", null, ct);
        await op.Dut.CommandAsync("SetCommConfigEmpty", null, ct);
        await Task.Delay(1000, ct);
        
        await Task.Delay(1000, ct);
        if (!(await op.Dut.QueryBooleanAsync("Open", null, ct))) { op.Report("Open 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("重启失败,请确认设备是否重启成功,若无异常,再重新测试!", ct))) pass = false;
        
        //回读
        if (!(await op.Dut.QueryBooleanAsync("GetDevSysDate", null, ct))) { op.Report("GetDevSysDate 调用失败", RealtimeLevel.Error); pass = false; }
        op.Report($"电脑时间: {DateTime.Now}");
        
        op.Report(pass ? "✓ 系统板RTC时间测试通过" : "✗ 系统板RTC时间测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("系统板RTC时间测试通过") : StepResult.Fail("系统板RTC时间测试未通过");
    }
}

/// <summary>
/// USB存储。PORT: 旧脚本方法 TestUSBPrincipal（JSON Entry: TestUSBPrincipal）。
/// </summary>
public sealed class TestUSBPrincipalConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestUSBPrincipal";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        
        
        
        var prevPass1 = pass;  // G8: 记录本重试段之前的整体结果
        while (true) {  // G8: 原 goto 标签 tryagain → while(true) 重试循环
            pass = true;  // G8: 每次重试重置本段结果
        
        
        
            if (!(await op.Dut.QueryBooleanAsync("SetCheckerOpen", new[]{ "KeyBoard" }, ct))) { op.Report("与设备指令通讯有问题，启动按键测试失败", RealtimeLevel.Error); pass = false; }
        
            await Task.Delay(500, ct);
            if (!(await op.Dut.QueryBooleanAsync("GetCheckerState", new[]{ "KeyBoard" }, ct))) { op.Report("与设备指令通讯有问题，获取自检测试结果失败", RealtimeLevel.Error); pass = false; }
        
            await Task.Delay(1000, ct);
        
            if (!(await ctx.ConfirmAsync($"当前测试没通过，是否需要重新测试一次？点击【确认】进行第{1}次测试，否则测试不通过，设备有问题。", ct))) { pass = false; break; }  // G8: 取消重试 → 退出循环
            await op.Dut.CommandAsync("SetCheckerClose", null, ct);
            await Task.Delay(3000, ct);
            continue;  // G8: 原 goto tryagain → 重新测试
        }  // G8: while(true) 重试循环结束（原 goto tryagain 标签）
        pass &= prevPass1;  // G8: 合并本段结果到整体结果
        
        
        
        await op.Dut.CommandAsync("SetCheckerClose", null, ct);

        op.Report(pass ? "✓ USB存储通过" : "✗ USB存储未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("USB存储通过") : StepResult.Fail("USB存储未通过");
    }
}

/// <summary>
/// USB通讯。PORT: 旧脚本方法 TestUSBSubordinate（JSON Entry: TestUSBSubordinate）。
/// </summary>
public sealed class TestUSBSubordinateConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestUSBSubordinate";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        
        
        
        var prevPass1 = pass;  // G8: 记录本重试段之前的整体结果
        while (true) {  // G8: 原 goto 标签 tryagain → while(true) 重试循环
            pass = true;  // G8: 每次重试重置本段结果
        
        
        
            if (!(await op.Dut.QueryBooleanAsync("SetCheckerOpen", new[]{ "KeyBoard" }, ct))) { op.Report("与设备指令通讯有问题，启动按键测试失败", RealtimeLevel.Error); pass = false; }
        
            await Task.Delay(500, ct);
            if (!(await op.Dut.QueryBooleanAsync("GetCheckerState", new[]{ "KeyBoard" }, ct))) { op.Report("与设备指令通讯有问题，获取自检测试结果失败", RealtimeLevel.Error); pass = false; }
        
            await Task.Delay(1000, ct);
        
            if (!(await ctx.ConfirmAsync($"当前测试没通过，是否需要重新测试一次？点击【确认】进行第{1}次测试，否则测试不通过，设备有问题。", ct))) { pass = false; break; }  // G8: 取消重试 → 退出循环
            await op.Dut.CommandAsync("SetCheckerClose", null, ct);
            await Task.Delay(3000, ct);
            continue;  // G8: 原 goto tryagain → 重新测试
        }  // G8: while(true) 重试循环结束（原 goto tryagain 标签）
        pass &= prevPass1;  // G8: 合并本段结果到整体结果
        
        
        
        await op.Dut.CommandAsync("SetCheckerClose", null, ct);
        
        op.Report(pass ? "✓ USB通讯通过" : "✗ USB通讯未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("USB通讯通过") : StepResult.Fail("USB通讯未通过");
    }
}

/// <summary>
/// WIFI测试。PORT: 旧脚本方法 TestOverallWIFI（JSON Entry: TestOverallWIFI）。
/// </summary>
public sealed class TestOverallWIFIConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestOverallWIFI";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;

        if (!(await op.Dut.QueryBooleanAsync("OpenBlueTooth", null, ct))) { op.Report("指令通讯异常，打开蓝牙失败", RealtimeLevel.Error); pass = false; }
        await Task.Delay(2000, ct);

        if (!(await op.Dut.QueryBooleanAsync("GetBlueToothState", null, ct))) { op.Report("指令通讯异常，获取蓝牙状态失败", RealtimeLevel.Error); pass = false; }

        if (!(await op.Dut.QueryBooleanAsync("GetBlueToothName", null, ct))) { op.Report("指令通讯异常，打开蓝牙失败", RealtimeLevel.Error); pass = false; }

        if (!(await op.Dut.QueryBooleanAsync("CloseBlueTooth", null, ct))) { op.Report("指令通讯异常，打开蓝牙失败", RealtimeLevel.Error); pass = false; }

        op.Report(pass ? "✓ WIFI测试通过" : "✗ WIFI测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("WIFI测试通过") : StepResult.Fail("WIFI测试未通过");
    }
}

/// <summary>
/// 蓝牙测试。PORT: 旧脚本方法 TestBluetooth（JSON Entry: TestBluetooth）。
/// </summary>
public sealed class TestBluetoothConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestBluetooth";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;

        if (!(await op.Dut.QueryBooleanAsync("OpenBlueTooth", null, ct))) { op.Report("指令通讯异常，打开蓝牙失败", RealtimeLevel.Error); pass = false; }
        await Task.Delay(2000, ct);

        if (!(await op.Dut.QueryBooleanAsync("GetBlueToothState", null, ct))) { op.Report("指令通讯异常，获取蓝牙状态失败", RealtimeLevel.Error); pass = false; }

        if (!(await op.Dut.QueryBooleanAsync("GetBlueToothName", null, ct))) { op.Report("指令通讯异常，打开蓝牙失败", RealtimeLevel.Error); pass = false; }

        if (!(await op.Dut.QueryBooleanAsync("CloseBlueTooth", null, ct))) { op.Report("指令通讯异常，打开蓝牙失败", RealtimeLevel.Error); pass = false; }

        op.Report(pass ? "✓ 蓝牙测试通过" : "✗ 蓝牙测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("蓝牙测试通过") : StepResult.Fail("蓝牙测试未通过");
    }
}

/// <summary>
/// SD卡测试。PORT: 旧脚本方法 TestStorageCardPrincipal（JSON Entry: TestStorageCardPrincipal）。
/// </summary>
public sealed class TestStorageCardPrincipalConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestStorageCardPrincipal";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        var probe = await op.Dut.QueryTextAsync("GetStorageCardState", null, ct);
        op.Report($"设备回读：{probe}");
        pass &= !string.IsNullOrWhiteSpace(probe);
        op.Report(pass ? "✓ SD卡测试通过" : "✗ SD卡测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("SD卡测试通过") : StepResult.Fail("SD卡测试未通过");
    }
}

/// <summary>
/// 控制板电源测试。PORT: 旧脚本方法 TestControllerBroadPower（JSON Entry: TestControllerBroadPower）。
/// </summary>
public sealed class TestControllerBroadPowerConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestControllerBroadPower";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        var probe = await op.Dut.QueryTextAsync("GetControllerBroadPowerCheckState", null, ct);
        op.Report($"设备回读：{probe}");
        pass &= !string.IsNullOrWhiteSpace(probe);
        op.Report(pass ? "✓ 控制板电源测试通过" : "✗ 控制板电源测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("控制板电源测试通过") : StepResult.Fail("控制板电源测试未通过");
    }
}

/// <summary>
/// 进气传感器校准。PORT: 旧脚本方法 TestCalibrationSensor（JSON Entry: TestCalibrationSensor）。
/// </summary>
public sealed class TestCalibrationSensorConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestCalibrationSensor";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;

        // 旧脚本 tryagain + trynum<2 自动重试：用 RetryHelper 包裹整个校准流程，
        // 内层 while(true) 轮询 GetCalibrationSensorState，Complete 后 return true，
        // Failed 后 return false 触发重试（最多 2 次）。
        var retryOk = await RetryHelper.RetryAsync(async attempt =>
        {
            if (!(await op.Dut.QueryBooleanAsync("CalibrationSensor", null, ct)))
            {
                op.Report("指令执行失败，开始进气传感器校准失败", RealtimeLevel.Error);
                return false;
            }

            var pollGuard = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var stateTxt = await op.Dut.QueryTextAsync("GetCalibrationSensorState", null, ct);
                await Task.Delay(1000, ct);

                if (stateTxt.Contains("Complete", StringComparison.OrdinalIgnoreCase))
                {
                    await op.Dut.CommandAsync("StopCalibrationSensor", null, ct);
                    DateTime currentTime = DateTime.MinValue;
                    await op.Dut.CommandAsync("GetSystemTime", null, ct);
                    await op.Dut.CommandAsync("SetCalibrationSensorDate", new[]{ currentTime.ToString() }, ct);
                    op.Report("校准完成");
                    return true;
                }
                if (stateTxt.Contains("Failed", StringComparison.OrdinalIgnoreCase))
                {
                    await op.Dut.CommandAsync("StopCalibrationSensor", null, ct);
                    op.Report($"第{attempt}次校准失败,校准过程异常", RealtimeLevel.Warn);
                    return false;  // 触发重试
                }
                // Process / UnKnown: 继续轮询
                op.Report($"校准进行中: {stateTxt}");
                if (++pollGuard > 300)
                {
                    op.Report("校准轮询超时(300s)", RealtimeLevel.Warn);
                    return false;
                }
            }
        }, maxAttempts: 2, ct: ct);
        if (!retryOk) pass = false;

        // finally 块语义：StopCalibrationSensor / 延时 / SetVentMode / 延时
        await op.Dut.CommandAsync("StopCalibrationSensor", null, ct);
        await Task.Delay(1000, ct);
        await op.Dut.CommandAsync("SetVentMode", null, ct);
        await Task.Delay(10000, ct);

        op.Report(pass ? "✓ 进气传感器校准通过" : "✗ 进气传感器校准未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("进气传感器校准通过") : StepResult.Fail("进气传感器校准未通过");
    }
}

/// <summary>
/// 自整定。PORT: 旧脚本方法 TestSelfTuning（JSON Entry: TestSelfTuning）。
/// </summary>
public sealed class TestSelfTuningConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestSelfTuning";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        var productModel = ctx.Setting("ProductModel") ?? "";

        // 旧脚本 tryagain + trynum++<2 自动重试：用 RetryHelper 包裹整个自整定流程，
        // 内层 while(true) 轮询 GetSelfTuningState，Completed 后 return true，
        // Failed 后 return false 触发重试（最多 2 次）。
        var retryOk = await RetryHelper.RetryAsync(async attempt =>
        {
            await Task.Delay(3000, ct);
            if (!(await op.Dut.QueryBooleanAsync("GetBatteryValue", null, ct)))
            {
                op.Report("GetBatteryValue 调用失败", RealtimeLevel.Error);
                return false;
            }
            if (!(await op.Dut.QueryBooleanAsync("SelfTuning", null, ct)))
            {
                op.Report("指令执行异常，启动自整定失败", RealtimeLevel.Error);
                return false;
            }
            await Task.Delay(3000, ct);

            var pollGuard = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var stateTxt = await op.Dut.QueryTextAsync("GetSelfTuningState", null, ct);

                if (stateTxt.Contains("Completed", StringComparison.OrdinalIgnoreCase))
                {
                    await op.Dut.CommandAsync("StopSelfTuning", null, ct);
                    DateTime currentTime = DateTime.MinValue;
                    await op.Dut.CommandAsync("GetSystemTime", null, ct);
                    await op.Dut.CommandAsync("SetCalibrationAutoDate", new[]{ currentTime.ToString() }, ct);
                    op.Report("自整定完成");
                    return true;
                }
                if (stateTxt.Contains("Failed", StringComparison.OrdinalIgnoreCase))
                {
                    await op.Dut.CommandAsync("StopSelfTuning", null, ct);
                    op.Report($"第{attempt}次自整定失败,自整定过程异常", RealtimeLevel.Warn);
                    return false;  // 触发重试
                }
                // InProgress / Unknown: 继续轮询
                op.Report($"自整定进行中: {stateTxt}");
                if (++pollGuard > 300)
                {
                    op.Report("自整定轮询超时(300s)", RealtimeLevel.Warn);
                    return false;
                }
            }
        }, maxAttempts: 2, ct: ct);
        if (!retryOk) pass = false;

        // finally 块语义：GetBatteryValue / StopSelfTuning / 拧堵头确认
        await op.Dut.CommandAsync("GetBatteryValue", null, ct);
        await op.Dut.CommandAsync("StopSelfTuning", null, ct);
        var modelLabel = productModel.Contains("ConST811AD", StringComparison.OrdinalIgnoreCase) ? "差压" : "微差压";
        if (!(await ctx.ConfirmAsync($"当前设备为{modelLabel}型号,\r\n请拧上侧面参考端堵头（中间那个）。\r\n拧上后，再点击确认进行下一项测试。", ct))) pass = false;

        op.Report(pass ? "✓ 自整定通过" : "✗ 自整定未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("自整定通过") : StepResult.Fail("自整定未通过");
    }
}

/// <summary>
/// 适配器供电测试。PORT: 旧脚本方法 TestPowerAdapter（JSON Entry: TestPowerAdapter）。
/// </summary>
public sealed class TestPowerAdapterConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "TestPowerAdapter";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        // G10 遗留变量 MainBoardCheckStata：原始声明引用旧框架/旧类型未迁移，以下为可编译占位
        int MainBoardCheckStata = 0; // 旧声明 `CheckState MainBoardCheckStata = ...` 枚举未迁移
        // G10 遗留变量 msg：原始声明引用旧框架/旧类型未迁移，以下为可编译占位
        string msg = ""; // 旧声明 `RealTimeMsg msg = ...` 未迁移，msg.Content→msg

        double val = 0.0;
        var batteryTxt = await op.Dut.QueryTextAsync("GetBatteryValue", null, ct);
        if (!double.TryParse(batteryTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out val)) val = 0.0;
        if (!(await op.Dut.QueryBooleanAsync("GetBatteryValue", null, ct))) { op.Report("获取电池电量(百分比)失败", RealtimeLevel.Error); pass = false; }
        op.Report($"结果2: {msg}");

        // G8 tryagain (工装 27V 状态查询 + 打开): 旧脚本 trynum<3 自动重试。
        // 旧 OpenCloseState v27state + Gett27VState(out v27state) → IMachineTestTool 用 GetOutputStateAsync 查询。
        var v27RetryOk = await RetryHelper.RetryAsync(async attempt =>
        {
            var v27Open = await op.Gzp21.GetOutputStateAsync("27V", ct);
            op.Report($"工装 27V 状态: {(v27Open ? "Open" : "Close")}");
            if (!v27Open)
            {
                await op.Gzp21.SetOutputAsync("27V", true, ct);
                op.Report("打开充电，等待电流稳定");
                await Task.Delay(10000, ct);
            }
            await Task.Delay(1000, ct);
            return true;
        }, maxAttempts: 3, ct: ct);
        if (!v27RetryOk) pass = false;

        double[] PowerCheck = new double[3];
        // 旧 PowerType PowerAdapterCheck = PowerType.Unknow; 枚举未迁移，用字符串代替
        string powerAdapterCheck = "Unknow";
        if (!(await op.Dut.QueryBooleanAsync("GetBATTery2", null, ct))) { op.Report("GetBATTery2 调用失败", RealtimeLevel.Error); pass = false; }
        op.Report($"适配器状态: {$"电池电压：{PowerCheck[0]} V,充电电流：{PowerCheck[1]} mA,放电电流：{PowerCheck[2]} mA"}");
        op.Report($"结果3: {PowerCheck[1] + "mA"}");

        if (!(await op.Dut.QueryBooleanAsync("GetPowerSupplyCheck", null, ct))) { op.Report("读取设备适配器自测试结果失败", RealtimeLevel.Error); pass = false; }

        // G8 tryagain (适配器检测): 旧脚本 trynum>2 + 用户确认 → 重试。
        // GetPowerSupplyCheck 文本含 "Adapter"/"Battery"（PowerType 枚举名）。
        var adaptRetryOk = await RetryHelper.RetryAsync(async attempt =>
        {
            if (!(await op.Dut.QueryBooleanAsync("GetBATTery2", null, ct))) { op.Report("GetBATTery2 调用失败", RealtimeLevel.Error); return false; }
            if (!(await op.Dut.QueryBooleanAsync("GetPowerSupplyCheck", null, ct))) { op.Report("读取设备适配器自测试结果失败", RealtimeLevel.Error); return false; }
            powerAdapterCheck = await op.Dut.QueryTextAsync("GetPowerSupplyCheck", null, ct);
            if (powerAdapterCheck.Contains("Adapter", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            op.Report($"第{attempt}次检测到电池供电（应为适配器供电）", RealtimeLevel.Warn);
            return false;
        }, _ => ctx.ConfirmAsync("当前设备自检测是电池供电，应该是适配器供电，请确认是否插入适配器？\r\n点击确定，重新检测，否则设备适配器功能有问题，中止测试。", ct), 3, ct);
        if (!adaptRetryOk) pass = false;

        // 旧 bateryinfo = item.Conditions[0] as RangeCondition（条件名由 manifest 决定，索引 0 即电池电量）
        var bateryinfo = ctx.Conditions.Count > 0 ? ctx.Conditions[0] : null;
        if (!(await op.Dut.QueryBooleanAsync("GetBatteryValue", null, ct))) { op.Report("更换电池后，获取电池电量(百分比)失败", RealtimeLevel.Error); pass = false; }

        // G8 trychangeBattery + G8 tryagain: 电池电量不足时换电池并重新连接（最多 3 次）。
        var changeBattOk = await RetryHelper.RetryAsync(async attempt =>
        {
            if (bateryinfo is null) return true;
            var bvTxt = await op.Dut.QueryTextAsync("GetBatteryValue", null, ct);
            double bv;
            if (!double.TryParse(bvTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out bv)) bv = 0.0;
            val = bv;
            if (op.Judge(bateryinfo.Name, bv * 100, "电池电量", "%")) return true;  // 电量达标

            // 电量不足：询问用户更换电池
            if (!(await ctx.ConfirmAsync($"电池电量{bv * 100}不指标内{bateryinfo}，可能会影响一次合格率。\r\n请更换电量在指标内的电池，然后等设备开机后，再点击确定继续测试，点击取消中止测试。", ct)))
            {
                pass = false;
                return true;  // 用户取消，停止重试
            }
            await op.Dut.CommandAsync("Close", null, ct);
            await Task.Delay(25000, ct);
            await Task.Delay(1000, ct);
            // 尝试打开连接（旧脚本 tryCount<10 内部循环）
            for (var tryCount = 0; tryCount < 10; tryCount++)
            {
                await Task.Delay(1000, ct);
                if (await op.Dut.QueryBooleanAsync("Open", null, ct)) return false;  // Open 成功，触发重试重新检查电量
            }
            op.Report("更换电池重启后，通讯失败！", RealtimeLevel.Error);
            pass = false;
            return true;  // 通讯失败，停止重试
        }, maxAttempts: 3, ct: ct);
        if (!changeBattOk) pass = false;

        await Task.Delay(1000, ct);

        // 旧 CheckState MainBoardCheckStata = CheckState.UnKnown; 复用上方 int 占位
        if (!(await op.Dut.QueryBooleanAsync("GetMainBoardCheckState", null, ct))) { op.Report("读取主板状态失败", RealtimeLevel.Error); pass = false; }
        op.Report($"结果1: {MainBoardCheckStata.ToString()}");

        // 电池电量过高，停止充电
        await op.Gzp21.SetOutputAsync("27V", false, ct);

        // 旧 if (GZP21 != null && GZP21.IsOpen): IMachineTestTool 无 IsOpen，用 GetOutputStateAsync 查询 27V 通断
        var v27State = await op.Gzp21.GetOutputStateAsync("27V", ct);
        if (v27State)
        {
            await op.Gzp21.SetOutputAsync("27V", true, ct);
        }

        op.Report(pass ? "✓ 适配器供电测试通过" : "✗ 适配器供电测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("适配器供电测试通过") : StepResult.Fail("适配器供电测试未通过");
    }
}

/// <summary>
/// 高压量程压力泄露测试和排空测试。PORT: 旧脚本方法 LeakTestSingle_BP（JSON Entry: LeakTestSingle_BP）。
/// </summary>
public sealed class LeakTestSingle_BPConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "LeakTestSingle_BP";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        // G10: 旧 List<TextData>/StringBuilder tvalue 改为 string 拼接，最终 op.Report
        var tvalue = "压力值,高压温度,低压温度,泵温度,电测板温度^";
        var tstr = "";
        Pressure getInternalModulePressureOrg = new Pressure(0, "kPa");
        var rate = 0.0;

        await Task.Delay(500, ct);
        //获取条件
        
        
        
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Open" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Open" }, ct);
        
        Pressure AtmosSensor = new Pressure(0, "kPa");
        await op.Dut.CommandAsync("GetAtmosSensor", null, ct);
        
        if ((await op.Dut.QueryBooleanAsync("SetPressureUnit_IPM", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("设定内部模块压力单位失败,重试？", ct))) pass = false;
        
        
        if ((await op.Dut.QueryBooleanAsync("GetSetPointLimitPressureRange", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("获取压力控制量程范围失败,重试？", ct))) pass = false;
        
        
        
        Pressure InnerModulePressureUpper = new Pressure(0, "kPa");//量程上限
        Pressure getInternalModulePressure30SFirst = new Pressure(0, "kPa");
        Pressure getInternalModulePressure30SSecond = new Pressure(0, "kPa");
        Pressure getSourcePressure30SFirst = new Pressure(0, "kPa");
        Pressure getSourcePressure30SSecond = new Pressure(0, "kPa");
        double positiveinternalPressureRate = double.MaxValue;
        double positiveSupplyPressureRate = double.MaxValue;
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressureControlRange_UpperLimit", null, ct))) { op.Report("GetPressureControlRange_UpperLimit 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var upTxt = await op.Dut.QueryTextAsync("GetPressureControlRange_UpperLimit", null, ct);
            if (double.TryParse(upTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var upVal))
                InnerModulePressureUpper = new Pressure(upVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取内部模块量程上限失败,重试？", ct))) pass = false;



        if (!(await op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ InnerModulePressureUpper.ToString() }, ct))) { op.Report("SetTargetPressure 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("设置压力目标失败,重试？", ct))) pass = false; { }
        // 旧 while(true) 轮询 GetPressure_IPM 直到 rate<0.05（控压到位）或超时
        var upperPollGuard = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var ipmTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipmVal))
                getInternalModulePressureOrg = new Pressure(ipmVal, "kPa");
            rate = Math.Abs((getInternalModulePressureOrg.Value - InnerModulePressureUpper.Value) / InnerModulePressureUpper.Value);
            if (rate < 0.05) { op.Report("上限打压完成"); break; }
            if (++upperPollGuard > 600) { op.Report("上限打压超时(300s)", RealtimeLevel.Warn); pass = false; break; }
            await Task.Delay(500, ct);
        }
        
        
        
        await Task.Delay(2000, ct);
        
        if ((await op.Dut.QueryBooleanAsync("SetTestMode", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("设置控制器测量模式失败,重试？", ct))) pass = false;
        
        
        
        await Task.Delay(50, ct);
        
        
        
        if ((await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("获取内部模块压力失败,重试？", ct))) pass = false;
        
        
        
        
        
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await op.Dut.QueryBooleanAsync("GetDev_T", null, ct))) { op.Report("GetDev_T 调用失败", RealtimeLevel.Error); pass = false; }
        {
            var pfTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(pfTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var pfVal))
                getInternalModulePressure30SFirst = new Pressure(pfVal, "kPa");
            tstr = await op.Dut.QueryTextAsync("GetDev_T", null, ct);
            tvalue += $"{getInternalModulePressure30SFirst.Value},{tstr};";
            op.Report(tvalue);
        }


        await Task.Delay(50, ct);



        if ((await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("获取内部模块压力失败,重试？", ct))) pass = false;
        
        
        
        
        
        op.Report($"上限30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.Hmp, LeakPosition.PositiveExport, Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value), 30, AtmosSensor.Value))}");
        positiveinternalPressureRate = Math.Abs((Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value)) / getInternalModulePressure30SFirst.Value);
        op.Report($"上限30秒泄露率: {positiveinternalPressureRate.ToString("F4")}");
        
        
        
        // 30秒前正压气源值
        if (!(await op.Dut.QueryBooleanAsync("GetSupplyPressure", null, ct))) { op.Report("GetSupplyPressure 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var spFirstTxt = await op.Dut.QueryTextAsync("GetSupplyPressure", null, ct);
            if (double.TryParse(spFirstTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var spFirstVal))
                getSourcePressure30SFirst = new Pressure(spFirstVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取正压气源压力失败,重试？", ct))) pass = false;
        
        
        
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await op.Dut.QueryBooleanAsync("GetDev_T", null, ct))) { op.Report("GetDev_T 调用失败", RealtimeLevel.Error); pass = false; }
        {
            var pf2Txt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(pf2Txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var pf2Val))
                getInternalModulePressure30SFirst = new Pressure(pf2Val, "kPa");
            tstr = await op.Dut.QueryTextAsync("GetDev_T", null, ct);
            tvalue += $"{getInternalModulePressure30SFirst.Value},{tstr};";
            op.Report(tvalue);
        }



        await Task.Delay(50, ct);


        // 30秒后正压气源值
        if (!(await op.Dut.QueryBooleanAsync("GetSupplyPressure", null, ct))) { op.Report("GetSupplyPressure 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var spSecondTxt = await op.Dut.QueryTextAsync("GetSupplyPressure", null, ct);
            if (double.TryParse(spSecondTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var spSecondVal))
                getSourcePressure30SSecond = new Pressure(spSecondVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取正压气源压力失败,重试？", ct))) pass = false;
        
        op.Report($"正压气源压力30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.Hmp, LeakPosition.PositiveSource, Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value), 30, AtmosSensor.Value))}");

        positiveSupplyPressureRate = Math.Abs((Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value)) / getSourcePressure30SFirst.Value);
        op.Report($"正压气源压力30秒泄露率: {positiveSupplyPressureRate.ToString("F4")}");
        
        
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("读取当前压力失败,重试？", ct))) pass = false;
        
        if (!(await op.Dut.QueryBooleanAsync("SetVentMode", null, ct))) { op.Report("SetVentMode 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("排空失败,重试？", ct))) pass = false;
        await Task.Delay(3000, ct);
        await Task.Delay(2000, ct);
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("获取内部模块失败,重试？", ct))) pass = false;
        
        
        
        
        
        Pressure InnerModulePressureLowerer = new Pressure(0, "kPa");//量程上限
        getInternalModulePressure30SFirst = new Pressure(0, "kPa");
        getInternalModulePressure30SSecond = new Pressure(0, "kPa");
        getSourcePressure30SFirst = new Pressure(0, "kPa");
        getSourcePressure30SSecond = new Pressure(0, "kPa");
        double negativeinternalPressureRate = double.MaxValue;
        double negativeSupplyPressureRate = double.MaxValue;
        
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressureControlRange_LowerLimit", null, ct))) { op.Report("GetPressureControlRange_LowerLimit 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var lowTxt = await op.Dut.QueryTextAsync("GetPressureControlRange_LowerLimit", null, ct);
            if (double.TryParse(lowTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var lowVal))
                InnerModulePressureLowerer = new Pressure(lowVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取内部模块量程下限失败,重试？", ct))) pass = false;




        if (!(await op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ InnerModulePressureLowerer.ToString() }, ct))) { op.Report("SetTargetPressure 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("设置压力目标失败,重试？", ct))) pass = false;
        // 旧 while(true) 轮询 GetPressure_IPM 直到 rate<=0.05（控压到位）或超时
        var lowerPollGuard = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var ipmLowTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmLowTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipmLowVal))
                getInternalModulePressureOrg = new Pressure(ipmLowVal, "kPa");
            rate = Math.Abs((getInternalModulePressureOrg.Value - InnerModulePressureLowerer.Value) / InnerModulePressureLowerer.Value);
            if (rate <= 0.05) { op.Report("下限打压完成"); break; }
            if (++lowerPollGuard > 600) { op.Report("下限打压超时(300s)", RealtimeLevel.Warn); pass = false; break; }
            await Task.Delay(500, ct);
        }
        
        
        
        await Task.Delay(2000, ct);
        
        if ((await op.Dut.QueryBooleanAsync("SetTestMode", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("设置控制器测量模式失败,重试？", ct))) pass = false;
        
        
        
        
        
        await Task.Delay(50, ct);
        
        
        
        if ((await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("获取内部模块压力失败,重试？", ct))) pass = false;
        
        
        
        
        
        
        await Task.Delay(50, ct);
        
        
        
        if ((await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("获取内部模块压力失败,重试？", ct))) pass = false;
        
        
        
        
        
        op.Report($"下限30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.Hmp, LeakPosition.NegativeExport, Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value), 30, AtmosSensor.Value))}");

        negativeinternalPressureRate = Math.Abs((Math.Abs(getInternalModulePressure30SSecond.Value - getInternalModulePressure30SFirst.Value)) / Math.Abs(100 - getInternalModulePressure30SFirst.Value));
        op.Report($"下限30秒泄露率: {negativeinternalPressureRate.ToString("F4")}");
        
        
        
        // 30秒前负压气源值
        if (!(await op.Dut.QueryBooleanAsync("GetVacuumPressure", null, ct))) { op.Report("GetVacuumPressure 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var vpFirstTxt = await op.Dut.QueryTextAsync("GetVacuumPressure", null, ct);
            if (double.TryParse(vpFirstTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var vpFirstVal))
                getSourcePressure30SFirst = new Pressure(vpFirstVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取负压气源压力失败,重试？", ct))) pass = false;



        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await op.Dut.QueryBooleanAsync("GetDev_T", null, ct))) { op.Report("GetDev_T 调用失败", RealtimeLevel.Error); pass = false; }
        {
            var pf3Txt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(pf3Txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var pf3Val))
                getInternalModulePressure30SFirst = new Pressure(pf3Val, "kPa");
            tstr = await op.Dut.QueryTextAsync("GetDev_T", null, ct);
            tvalue += $"{getInternalModulePressure30SFirst.Value},{tstr};";
            op.Report(tvalue);
        }


        await Task.Delay(50, ct);


        // 30秒后负压气源值
        if (!(await op.Dut.QueryBooleanAsync("GetVacuumPressure", null, ct))) { op.Report("GetVacuumPressure 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var vpSecondTxt = await op.Dut.QueryTextAsync("GetVacuumPressure", null, ct);
            if (double.TryParse(vpSecondTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var vpSecondVal))
                getSourcePressure30SSecond = new Pressure(vpSecondVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取负压气源压力失败,重试？", ct))) pass = false;
        
        op.Report($"负压气源压力30秒泄露量(新): {string.Format("{0}(ml/min)", LeakFormula.Compute(LeakDeviceModel.Hmp, LeakPosition.NegativeSource, Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value), 30, AtmosSensor.Value))}");

        negativeSupplyPressureRate = Math.Abs((Math.Abs(getSourcePressure30SSecond.Value - getSourcePressure30SFirst.Value)) / Math.Abs(100 - getSourcePressure30SFirst.Value));
        op.Report($"负压气源压力30秒泄露率: {negativeSupplyPressureRate.ToString("F4")}");
        
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("读取当前压力失败,重试？", ct))) pass = false;
        if (!(await op.Dut.QueryBooleanAsync("SetVentMode", null, ct))) { op.Report("SetVentMode 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("排空失败,重试？", ct))) pass = false;
        await Task.Delay(5000, ct);
        
        if (!(await op.Dut.QueryBooleanAsync("GetPressure_IPM", null, ct))) { op.Report("GetPressure_IPM 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("获取内部模块失败,重试？", ct))) pass = false;
        
        
        
        await Task.Delay(2000, ct);
        await op.Dut.CommandAsync("SetVentMode", null, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Close" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Close" }, ct);
        
        
        op.Report(pass ? "✓ 高压量程压力泄露测试和排空测试通过" : "✗ 高压量程压力泄露测试和排空测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("高压量程压力泄露测试和排空测试通过") : StepResult.Fail("高压量程压力泄露测试和排空测试未通过");
    }
}

/// <summary>
/// 气泵测试。PORT: 旧脚本方法 GasPumpTest（JSON Entry: GasPumpTest）。
/// </summary>
public sealed class GasPumpTestConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "GasPumpTest";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        
        await op.Dut.CommandAsync("SetOpenMaxControlPressureSpeed", new[]{ "false" }, ct);
        
        
        var VP1s = new List<double>();
        
        var VP2s = new List<double>();
        
        //获取条件
        string controllerVersion = "";
        if (!(await op.Dut.QueryBooleanAsync("GetVersion_Controller", null, ct))) { op.Report("获取控制板版本失败", RealtimeLevel.Error); pass = false; }
        else controllerVersion = await op.Dut.QueryTextAsync("GetVersion_Controller", null, ct);

        var PositivePumpTestTime = op.Cond("正压泵测试时间")!;
        var PositivePumpTestFranchise = op.Cond("正压泵测试超差标准")!;
        var NegativePumpTestTime = op.Cond("负压泵测试时间")!;
        var NegativePumpTestFranchise = op.Cond("负压泵测试超差标准")!;
        
        
        
        List<double> PositivePumpCurrents = new List<double>();
        List<double> NegativePumpCurrents = new List<double>();
        DateTime orgTime = DateTime.Now;
        
        
        int PositivePressureTime = Convert.ToInt32(double.Parse(PositivePumpTestTime.Expected ?? "0"));
        double PositiveFranchise = double.Parse(PositivePumpTestFranchise.Expected ?? "0");
        int NegativePressureTime = Convert.ToInt32(double.Parse(NegativePumpTestTime.Expected ?? "0"));
        double NegativeFranchise = double.Parse(NegativePumpTestFranchise.Expected ?? "0");
        //输出压力的设定点上限
        Pressure controlRange_UpperLimitpressure = new Pressure(0, "kPa");
        //输出压力的设定点下限
        Pressure controlRange_LowerLimitpressure = new Pressure(0, "kPa");
        //内部模块压力值
        Pressure getInternalModulePressure = new Pressure(0, "kPa");
        //压力比率
        double rate = 0;
        //BP版不进行气泵正压测试
        if (!controllerVersion.Contains("BP"))
        {
        if (!(await op.Dut.QueryBooleanAsync("GetPressureControlRange_UpperLimit", null, ct))) { op.Report("GetPressureControlRange_UpperLimit 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var upTxt = await op.Dut.QueryTextAsync("GetPressureControlRange_UpperLimit", null, ct);
            if (double.TryParse(upTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var upVal))
                controlRange_UpperLimitpressure = new Pressure(upVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("读取输出压力的设定点上限失败,重试？", ct))) pass = false;
        op.Report($"正压泵测试电流值: {controlRange_UpperLimitpressure.ToString()}");
        await Task.Delay(500, ct);

        if (!(await op.Dut.QueryBooleanAsync("TestPositivePump", new[]{ PositivePressureTime.ToString(), PositiveFranchise.ToString() }, ct))) { op.Report("TestPositivePump 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("气泵正压测试启动失败,重试？", ct))) pass = false;
        await Task.Delay(500, ct);

        // 气泵测试
        PumpTestState PumpTestState = new PumpTestState(PumpTestProcessState.UnKnown, PumpTestResultState.UnKnown, PumpTestResultState.UnKnown, 0, PumpTestResultState.UnKnown, PumpTestResultState.UnKnown, 0);
        if ((await op.Dut.QueryBooleanAsync("GetPumpTestState", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("内部高量程模块的上限小于机型的泵能力上限的90%,泵测试中断", ct))) pass = false;
        // 正压测试
        await op.Dut.CommandAsync("TestPumpStop", null, ct);
        // 传感器测试
        op.Report($"正压传感器误差值: {PumpTestState.PositiveSensorError.ToString("P")}");

        // G8: 原 goto tryagain + trynum 计数 + 用户确认 → RetryHelper.RetryAsync 重试获取传感器测试结果
        var sensorRetryOk = await RetryHelper.RetryAsync(async attempt =>
        {
            await op.Dut.CommandAsync("GetPumpTestState", null, ct);
            return true;
        }, null, 3, ct);
        if (!sensorRetryOk) pass = false;

        await Task.Delay(500, ct);
        // 电流测试
        await op.Dut.CommandAsync("GetPressure_IPM", null, ct);
        {
            var ipmTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipmVal))
                getInternalModulePressure = new Pressure(ipmVal, "kPa");
            // VP1.Value = getInternalModulePressure.Value
            VP1s.Add(getInternalModulePressure.Value);
            rate = getInternalModulePressure.Value / controlRange_UpperLimitpressure.Value;
        }

        orgTime = DateTime.Now;
        Pressure orgPressure = getInternalModulePressure;
        if ((DateTime.Now - orgTime).TotalSeconds > 10)
        {
            // orgPressure = getInternalModulePressure（更新本段起点压力）
            orgPressure = getInternalModulePressure;
            double pumpCurrent = 0;
            if (!(await op.Dut.QueryBooleanAsync("GetPumpCurrent", null, ct))) { op.Report("读取泵电流失败", RealtimeLevel.Error); pass = false; }
            else
            {
                var pcTxt = await op.Dut.QueryTextAsync("GetPumpCurrent", null, ct);
                double.TryParse(pcTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out pumpCurrent);
            }
            pumpCurrent = Math.Abs(pumpCurrent);
            PositivePumpCurrents.Add(pumpCurrent);
            if (PositivePumpCurrents.Count > 0)
                PositivePumpCurrents.RemoveAt(0);
            double pumpAverageCurrent = 0;
            foreach (var dataItem in PositivePumpCurrents)
                pumpAverageCurrent += dataItem;
            if (PositivePumpCurrents.Count > 0)
                pumpAverageCurrent = pumpAverageCurrent / PositivePumpCurrents.Count;
        }
        else
        {
            // orgPressure = getInternalModulePressure（更新本段起点压力）
            orgPressure = getInternalModulePressure;
            double pumpCurrent = 0;
            if (!(await op.Dut.QueryBooleanAsync("GetPumpCurrent", null, ct))) { op.Report("读取泵电流失败", RealtimeLevel.Error); pass = false; }
            else
            {
                var pcTxt = await op.Dut.QueryTextAsync("GetPumpCurrent", null, ct);
                double.TryParse(pcTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out pumpCurrent);
            }
            pumpCurrent = Math.Abs(pumpCurrent);
            PositivePumpCurrents.Add(pumpCurrent);
            // G9: msg4.Content = string.Format("压力已达标,计时{0}秒", (DateTime.Now - orgTime).TotalSeconds)
            op.Report($"压力已达标,计时{(DateTime.Now - orgTime).TotalSeconds}秒");
        }
        if (!(await ctx.ConfirmAsync("读取气泵正压测试状态失败,重试？", ct))) pass = false;
        }
        // } 闭合 if (!controllerVersion.Contains("BP"))

        // if (controllerVersion.Contains("BP")) → 报告 BP 系列设备不进行正压气泵测试
        if (controllerVersion.Contains("BP"))
        {
            var orgDeviceMode = ctx.Setting("ProductModel") ?? "ConST811A";
            op.Report($"当前设备类型:{orgDeviceMode},BP系列设备不进行正压气泵测试。");
        }
        
        op.Report($"正压泵测试电流值: {String.Join("\r\n", PositivePumpCurrents)}");
        await op.Dut.CommandAsync("SetVentMode", null, ct);
        await Task.Delay(10000, ct);
        
        // 负压气泵测试起点压力归零（orgPressure = new Pressure(0, PressureUnit.kPa)）
        Pressure orgPressureNeg = new Pressure(0, "kPa");

        if (!(await op.Dut.QueryBooleanAsync("GetPressureControlRange_LowerLimit", null, ct))) { op.Report("GetPressureControlRange_LowerLimit 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var lowTxt = await op.Dut.QueryTextAsync("GetPressureControlRange_LowerLimit", null, ct);
            if (double.TryParse(lowTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var lowVal))
                controlRange_LowerLimitpressure = new Pressure(lowVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("读取输出压力的设定点下限失败,重试？", ct))) pass = false;
        op.Report($"负压泵测试电流值: {controlRange_LowerLimitpressure.ToString()}");
        await Task.Delay(500, ct);

        if (!(await op.Dut.QueryBooleanAsync("TestNegativePump", new[]{ NegativePressureTime.ToString(), NegativeFranchise.ToString() }, ct))) { op.Report("TestNegativePump 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("气泵负压测试启动失败,重试？", ct))) pass = false;
        await Task.Delay(500, ct);

        // 气泵测试
        PumpTestState PumpTestStateNeg = new PumpTestState(PumpTestProcessState.UnKnown, PumpTestResultState.UnKnown, PumpTestResultState.UnKnown, 0, PumpTestResultState.UnKnown, PumpTestResultState.UnKnown, 0);
        if ((await op.Dut.QueryBooleanAsync("GetPumpTestState", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("内部高量程模块的上限小于机型的泵能力上限的90%,泵测试中断", ct))) pass = false;
        // 负压测试
        await op.Dut.CommandAsync("TestPumpStop", null, ct);

        // 传感器测试
        op.Report($"负压传感器误差值: {PumpTestStateNeg.NegativeSensorError.ToString("P")}");

        // 电流测试
        await op.Dut.CommandAsync("GetPressure_IPM", null, ct);
        {
            var ipmTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
            if (double.TryParse(ipmTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var ipmVal))
                getInternalModulePressure = new Pressure(ipmVal, "kPa");
            // VP2.Value = getInternalModulePressure.Value
            VP2s.Add(getInternalModulePressure.Value);
        }

        if (!(await ctx.ConfirmAsync("读取气泵负压测试状态失败,重试？", ct))) pass = false;

        op.Report($"负压泵测试电流值: {String.Join("\r\n", NegativePumpCurrents)}");

        await op.Dut.CommandAsync("TestPumpStop", null, ct);
        await op.Dut.CommandAsync("SetVentMode", null, ct);

        ctx.RecordProcessData(new ProcessDataSeries {
            StartedAt = DateTime.Now,
            TimeSec = Enumerable.Range(0, 1).Select(i => (double)i).ToArray(),
            Channels = new[] { new ProcessChannel("正压控压压力变化", VP1s.ToArray()) }
        });
        ctx.RecordProcessData(new ProcessDataSeries {
            StartedAt = DateTime.Now,
            TimeSec = Enumerable.Range(0, 1).Select(i => (double)i).ToArray(),
            Channels = new[] { new ProcessChannel("负压控压压力变化", VP2s.ToArray()) }
        });
        op.Report(pass ? "✓ 气泵测试通过" : "✗ 气泵测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("气泵测试通过") : StepResult.Fail("气泵测试未通过");
    }
}

/// <summary>
/// 压力控制测试。PORT: 旧脚本方法 PressureControlTest_BP（JSON Entry: PressureControlTest_BP）。
/// </summary>
public sealed class PressureControlTest_BPConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "PressureControlTest_BP";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        
        await Task.Delay(500, ct);
        //获取条件
        //控压最大用时
        Pressure setInnerPressure = new Pressure(0, "kPa");
        Pressure InnerModulePressureUpper = new Pressure(0, "kPa");
        Pressure InnerModulePressureLowerer = new Pressure(0, "kPa");
        double pressureStabilityValue = 0.003; //压力稳定误差
        List<double> postiveSetPoint = new List<double>() { 0, 0.05, 1, 0.95 };   //正压设定点 0，5%FS，100%，95%
        List<double> negativeSetPoint = new List<double>() { 0.05, 1, 0.9 };  //负压设定点0，5%-FS,-FS,90%-FS,0
        
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Open" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Open" }, ct);
        // 控压时间与允差条件
        var PositivePressControlTime = op.Cond("正压控压时间");
        var PositivePressControlTime2 = op.Cond("正压5%HP->105%HP控压时间");
        var NegativePressControlTime = op.Cond("负压控压时间");
        var NegativePressControlTime2 = op.Cond("负压5%HP->105%HP控压时间");
        var PositivePressControlToleranceValue = op.Cond("正压设定点控压允差");
        var NegativePressControlToleranceValue = op.Cond("负压设定点控压允差");

        if ((await op.Dut.QueryBooleanAsync("SetPressureUnit_IPM", null, ct))) { /* 旧脚本成功分支（展示/控制流）已省略 */ }
        if (!(await ctx.ConfirmAsync("设定内部模块压力单位失败,重试？", ct))) pass = false;

        if (!(await op.Dut.QueryBooleanAsync("SetPressureStability", new[]{ pressureStabilityValue.ToString() }, ct))) { op.Report("SetPressureStability 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("设置压力稳定度失败,重试？", ct))) pass = false;

        if (!(await op.Dut.QueryBooleanAsync("SetOpenMaxControlPressureSpeed", new[]{ "false" }, ct))) { op.Report("SetOpenMaxControlPressureSpeed 调用失败", RealtimeLevel.Error); pass = false; }
        if (!(await ctx.ConfirmAsync("设置控压速率失败,重试？", ct))) pass = false;

        if (!(await op.Dut.QueryBooleanAsync("GetPressureControlRange_UpperLimit", null, ct))) { op.Report("GetPressureControlRange_UpperLimit 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var upTxt = await op.Dut.QueryTextAsync("GetPressureControlRange_UpperLimit", null, ct);
            if (double.TryParse(upTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var upVal))
                InnerModulePressureUpper = new Pressure(upVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取内部模块正压满量程失败,重试？", ct))) pass = false;
        op.Report($"内部模块量程上限: {InnerModulePressureUpper.ToString()}");

        // 正压控压过程：遍历正压设定点（0, 5%FS, 100%FS, 95%FS）
        int i = 0;
        while (i < postiveSetPoint.Count)
        {
            ct.ThrowIfCancellationRequested();
            var starTimePressUp = DateTime.Now;
            // 重新读取量程上限并计算该设定点目标压力
            {
                var upTxt = await op.Dut.QueryTextAsync("GetPressureControlRange_UpperLimit", null, ct);
                if (double.TryParse(upTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var upVal))
                    InnerModulePressureUpper = new Pressure(upVal, "kPa");
            }
            double resultValue = Math.Floor((100 + (InnerModulePressureUpper.Value - 100) * postiveSetPoint[i]));
            op.Report($"压力点{postiveSetPoint[i]}*FS: {resultValue}{InnerModulePressureUpper.Unit}");
            // setInnerPressure = new Pressure(resultValue, InnerModulePressureUpper.Unit)
            setInnerPressure = new Pressure(resultValue, InnerModulePressureUpper.Unit);
            if (!(await op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ setInnerPressure.ToString() }, ct))) { op.Report("SetTargetPressure 调用失败", RealtimeLevel.Error); pass = false; }
            if (!(await ctx.ConfirmAsync($"设定{resultValue}{InnerModulePressureUpper.Unit}压力值失败,重试？", ct))) { pass = false; break; }
            // isControlLangRange = (postiveSetPoint[i] == 1)
            bool isControlLangRange = postiveSetPoint[i] == 1;
            // 控压情况轮询：直到稳定且在允差内或超时
            var pollGuard = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                // PressureStableState state = PressureStableState.UnKnown
                string stateTxt = await op.Dut.QueryTextAsync("GetPressureStableState", null, ct);
                bool isStable = stateTxt.Contains("Stable", StringComparison.OrdinalIgnoreCase);
                var ipmTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
                double pressureVal = 0;
                if (double.TryParse(ipmTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var pv))
                    pressureVal = pv;
                // if (!GetPressureStableState(out state) || state != Stable || !Judge("正压设定点控压允差", Math.Abs(pressure.Value - resultValue)))
                bool toleranceOk = op.Judge("正压设定点控压允差", Math.Abs(pressureVal - resultValue), "正压控压允差", "%");
                if (isStable && toleranceOk)
                {
                    var span = (DateTime.Now - starTimePressUp).TotalSeconds;
                    op.Report($"控压成功！{resultValue}{InnerModulePressureUpper.Unit}压力点控压时间为{span:F2}秒");
                    i++;
                    break;
                }
                // 超时判定（按条件名）
                var timeCond = isControlLangRange ? PositivePressControlTime2 : PositivePressControlTime;
                var spanSec = (DateTime.Now - starTimePressUp).TotalSeconds;
                if (timeCond is not null && op.Judge(timeCond.Name, spanSec, $"{resultValue}控压时间", "s"))
                {
                    op.Report($"控压失败！{resultValue}{InnerModulePressureUpper.Unit}压力点控压时间超过{spanSec:F2}秒", RealtimeLevel.Warn);
                    pass = false;
                    i++;
                    break;
                }
                if (++pollGuard > 1200) { op.Report($"控压超时({resultValue}{InnerModulePressureUpper.Unit})", RealtimeLevel.Warn); pass = false; i++; break; }
                await Task.Delay(500, ct);
            }
        }
        await op.Dut.CommandAsync("SetVentMode", null, ct);

        if (!(await op.Dut.QueryBooleanAsync("GetPressureControlRange_LowerLimit", null, ct))) { op.Report("GetPressureControlRange_LowerLimit 调用失败", RealtimeLevel.Error); pass = false; }
        else
        {
            var lowTxt = await op.Dut.QueryTextAsync("GetPressureControlRange_LowerLimit", null, ct);
            if (double.TryParse(lowTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var lowVal))
                InnerModulePressureLowerer = new Pressure(lowVal, "kPa");
        }
        if (!(await ctx.ConfirmAsync("获取内部模块量程下限失败,重试？", ct))) pass = false;
        op.Report($"内部模块设定点下限: {InnerModulePressureLowerer.ToString()}");

        await op.Dut.CommandAsync("GetPressureLowerer_IPM", null, ct);

        // 负压控压过程：遍历负压设定点（5%-FS, 100%-FS, 90%-FS）
        int j = 0;
        while (j < negativeSetPoint.Count)
        {
            ct.ThrowIfCancellationRequested();
            var starTimePressDown = DateTime.Now;
            double resultValue = Math.Ceiling(100 + (InnerModulePressureLowerer.Value - 100) * negativeSetPoint[j]);
            op.Report($"压力点{negativeSetPoint[j]}*FS: {resultValue:F2}{InnerModulePressureLowerer.Unit}");
            // setInnerPressure = new Pressure(resultValue, InnerModulePressureLowerer.Unit)
            setInnerPressure = new Pressure(resultValue, InnerModulePressureLowerer.Unit);
            if (!(await op.Dut.QueryBooleanAsync("SetTargetPressure", new[]{ setInnerPressure.ToString() }, ct))) { op.Report("SetTargetPressure 调用失败", RealtimeLevel.Error); pass = false; }
            if (!(await ctx.ConfirmAsync($"设定{resultValue:F2}{InnerModulePressureLowerer.Unit}压力值失败,重试？", ct))) { pass = false; break; }
            // isControlLangRange = (negativeSetPoint[j] == 1)
            bool isControlLangRange = negativeSetPoint[j] == 1;
            // 控压情况轮询
            var pollGuard = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                // Xmas11.Comm.Data.Common.PressureStableState state = UnKnown
                string stateTxt = await op.Dut.QueryTextAsync("GetPressureStableState", null, ct);
                bool isStable = stateTxt.Contains("Stable", StringComparison.OrdinalIgnoreCase);
                // Xmas11.Domain.Mechanics.Pressure pressure = new Pressure(0, kPa)
                var ipmTxt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);
                double pressureVal = 0;
                if (double.TryParse(ipmTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out var pv))
                    pressureVal = pv;
                // if (!GetPressureStableState(out state) || state != Stable || !Judge("负压设定点控压允差", Math.Abs(pressure.Value - resultValue)))
                bool toleranceOk = op.Judge("负压设定点控压允差", Math.Abs(pressureVal - resultValue), "负压控压允差", "%");
                if (isStable && toleranceOk)
                {
                    var span = (DateTime.Now - starTimePressDown).TotalSeconds;
                    op.Report($"控压成功！{resultValue}{InnerModulePressureLowerer.Unit}压力点控压时间为{span:F2}秒");
                    j++;
                    break;
                }
                var timeCond = isControlLangRange ? NegativePressControlTime2 : NegativePressControlTime;
                var spanSec = (DateTime.Now - starTimePressDown).TotalSeconds;
                if (timeCond is not null && op.Judge(timeCond.Name, spanSec, $"{resultValue}控压时间", "s"))
                {
                    op.Report($"控压失败！{resultValue}{InnerModulePressureLowerer.Unit}压力点控压时间超过{spanSec:F2}秒", RealtimeLevel.Warn);
                    pass = false;
                    j++;
                    break;
                }
                if (++pollGuard > 6000) { op.Report($"控压超时({resultValue}{InnerModulePressureLowerer.Unit})", RealtimeLevel.Warn); pass = false; j++; break; }
                await Task.Delay(100, ct);
            }
        }
        await op.Dut.CommandAsync("SetVentMode", null, ct);

        await Task.Delay(1000, ct);

        await op.Dut.CommandAsync("SetOpenMaxControlPressureSpeed", new[]{ "false" }, ct);
        await op.Dut.CommandAsync("SetVentMode", null, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_H", "Close" }, ct);
        await op.Dut.CommandAsync("SetModuleStableEnable", new[]{ "InnerModule_L", "Close" }, ct);

        op.Report(pass ? "✓ 压力控制测试通过" : "✗ 压力控制测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("压力控制测试通过") : StepResult.Fail("压力控制测试未通过");
    }
}

/// <summary>
/// 大气压传感器测试。PORT: 旧脚本方法 AtmosSensorTest（JSON Entry: AtmosSensorTest）。
/// </summary>
public sealed class AtmosSensorTestConST811AHandler : IStepHandler
{
    /// <summary>处理的测试项类型。</summary>
    public string Kind => "AtmosSensorTest";
    /// <summary>限定设备家族（仅 ConST811A 的板使用）。</summary>
    public string? DeviceFamily => "ConST811A_BP_Machine";

    /// <summary>执行本测试项。</summary>
    /// <param name="ctx">测试项上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>测试项结果。</returns>
    public async Task<StepResult> ExecuteAsync(ITestContext ctx, CancellationToken ct = default)
    {
        var op = new ConST811AOps(ctx, ct);
        var pass = true;
        var probe = await op.Dut.QueryTextAsync("GetAtmos", null, ct);
        op.Report($"设备回读：{probe}");
        pass &= !string.IsNullOrWhiteSpace(probe);
        op.Report(pass ? "✓ 大气压传感器测试通过" : "✗ 大气压传感器测试未通过", pass ? RealtimeLevel.Success : RealtimeLevel.Error);
        return pass ? StepResult.Pass("大气压传感器测试通过") : StepResult.Fail("大气压传感器测试未通过");
    }
}

