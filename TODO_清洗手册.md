# SYST 遗留 TODO 清理手册（供子代理执行）

## 你的任务
清理指定 Machine 文件中的所有 `// TODO(自动转换...)` 注释为真实 C# 逻辑，并移除转换器占位行，
最终该文件不再包含任何 `TODO(自动转换` 字样，且 `dotnet build SYST.TestSteps.csproj` 0 错误。

## 目标文件与参考源
- 目标文件：E:\WPFCli\Output\SYST\src\04.TestSteps\SYST.TestSteps\ConST811A\ConST811A_XX_Machine\ConST811A_XX_Machine.cs
- 旧平台原始方法（**每个 handler 的 PORT 注释已标明对应方法名**）：
  E:\WPFCli\References\OldPlatform\NewTestBench\Bots.TestBench\11.APP\UserInterface\P23\Bots.TestBench.UI.P23\Task\P23\p23_AutoTest.cs
  用 Grep 按方法名（如 `public dynamic TestKeyBoard(`）定位原始实现，对照恢复被丢弃的语句。
- 设备派发表：E:\WPFCli\Output\SYST\src\03.Devices\SYST.Devices\Dut\ConST811A\ConST811ADut.cs 的 `Execute` 方法
  （方法名 → APC2 调用；所有 op.Dut 调用的方法名都能在这里找到对应）。

## Ops API（本文件顶部的 ConST811AOps 类）
- `op.Dut`：被检 IConST811ADut，通用派发：
  - `await op.Dut.QueryBooleanAsync("方法名", new[]{ "参数" } 或 null, ct)` → bool（指令成功与否）
  - `await op.Dut.QueryTextAsync("方法名", args, ct)` → string（读数值/状态文本）
  - `await op.Dut.CommandAsync("方法名", args, ct)` → Task（无返回指令）
- `await op.Gzp21.SetOutputAsync("通道名", bool, ct)`：工装继电器输出
- `await op.P06.ReadVoltageAsync(通道号, ct)` / `ReadCurrentAsync(通道号, ct)`：共享设备读数
- `op.Report("文本", RealtimeLevel.X)`：实时消息（Info/Success/Warn/Error）
- `await op.Sleep(ms)`：真机延时（仿真跳过）
- `op.Cond("条件名")` → ConditionDescriptor?（按名取条件）
- `op.Judge("条件名", value, "标签", "单位")` → bool：按条件判定并报告
- `op.TrimCurrents(List<double>)`：掐头去尾各 5 点
- `await ctx.ConfirmAsync("消息", ct)` → bool：人工确认
- `ctx.Setting("名")` / `ctx.Parameter("名")?.Value` / `ctx.SerialNumber`
- `await RetryHelper.RetryAsync(attempt => 动作bool, () => ctx.ConfirmAsync("重试？", ct), 最大次数, ct)` → bool
  （RetryHelper 在 E:\WPFCli\Output\SYST\src\01.Core\SYST.Core.Abstractions\RetryHelper.cs）

## 各类 TODO 转换规则

### A) `TODO(自动转换-G8): goto xxx → RetryHelper 重构`
旧模式（原始旧脚本）：
```
tryagain:
    动作...
    if (!(await ctx.ConfirmAsync("...重试？", ct))) pass = false;   // 用户取消
    goto tryagain;
```
转为：
```
var retryOk = await RetryHelper.RetryAsync(async attempt =>
{
    pass = true;  // 每次重试重置本段结果
    动作...（return pass）
}, () => ctx.ConfirmAsync("...重试？", ct), 3, ct);
if (!retryOk) pass = false;
```
注意：这些 G8 标记处**没有**像其他 G8 那样被转成 while(true)，原因通常是原 goto 跳回点包含用户确认弹窗，
需要用 RetryHelper 处理。动作语句保持现有代码不动，仅包进 RetryAsync。

### B) `TODO(自动转换-G10): List<旧类型> xxx = ...`（旧框架集合）
旧类型（TextData/PAMassage/DataBase/RealTimeMsg/Result）在 SYST 不存在。处理：
- 若仅用于向 UI 报结果文本 → 换成本文件可用的 `List<string>` 或 `List<(string Name, string Value)>`，
  收集后用 `foreach` 逐条 `op.Report(...)`。
- 若引用旧 Result/RealTimeMsg 结构 → 直接用 `op.Report` 表达，删除旧声明。
- 若后续代码用到了该变量（如 `massage[0].Address`），必须保留同名变量并给可编译的替代类型
  （如 `var massage = new List<(string Address, string Name)>();` 已有先例，保持即可）。

### C) `TODO(自动转换-G9): msgN.Content = "..."`（旧 UI 控件文本）
旧 `RealTimeMsg msg.Content = "..."` = 实时消息推送。转 `op.Report("文本", RealtimeLevel.Info)`；
失败场景用 Warn/Error。若涉及测量值，先读值再拼接文本（参考旧方法原文）。

### D) `TODO(自动转换-G1type): var R = item.GetDevice("P21").GetPressure_IPM(out x)`（out 语义）
用 `var txt = await op.Dut.QueryTextAsync("GetPressure_IPM", null, ct);` 读回压力文本，
`double.TryParse(txt, out x)`（不变文化）。方法名可在 ConST811ADut 派发表中查找。

### E) `TODO(自动转换-G1out): ... out 语义丢失`
按上下文恢复：被检方法用 QueryTextAsync 读回值解析；工装方法用 op.Gzp21/op.P06 对应读接口。

### F) `TODO(自动转换-G6): 人工核对条件名`
核对 `ctx.Conditions` 中实际条件名（先 Read 目标 .json manifest 或 Grep 条件名），
用 `op.Cond("名")`/`op.Judge("名", value, ...)` 判定；找不到确切名就用最接近者并保留语义。

### G) `TODO(自动转换): <语句>`（普通，占大多数）
把该语句还原为等价 SYST 逻辑：
- `ProgramFunctionCheckResult checkResult = ProgramFunctionCheckResult.Unknow;`
  → SYST 无此类型。若它服务于"轮询 GetCheckerState 直到出结果"的自检流程，
    改用 `QueryBooleanAsync("GetCheckerState", ...)` 循环（参考旧方法原文的 while(true) 轮询，超时则 fail），
    或直接以 QueryBooleanAsync 成功代表通过（与 G8 已转换的自检循环保持一致）。
- 算术/赋值/`i++`/`isFinished = true`/字符串拼接 → 原样转，注意变量作用域（必要时在方法开头声明）。
- `item.GetDevice("GZP21").Gett27VState(out v27state)` 等工装/被检调用 → 映射到 op.Gzp21/op.Dut 对应方法
  （先在派发表/本文件其他 handler 中查找同名调用先例，照抄模式）。
- `tvalue.Append(...)`（StringBuilder）→ 改用 string 拼接，最后 op.Report。

### H) 清理占位行
每个 handler 末尾都有两行转换器占位：
```
await op.ExecuteLegacyAsync(new[] { ... }, ct);
op.Report("... 旧平台设备调用已按真实驱动回放，仍有 N 条非设备语句待核对", RealtimeLevel.Warn);
```
- 上方代码已用 op.Dut/op.Gzp21 直接表达了这些设备调用 → **删除这两行**。
- 例外：若 ExecuteLegacyAsync 列表中有上方未出现的设备调用，先在上方补成直接 op.Dut 调用再删除。

## 铁律
1. 不改变 handler 签名、Kind、DeviceFamily、类名。
2. 不引入本工程不存在的类型/命名空间（先 Grep 确认存在再引用）。不要 using 旧框架命名空间。
3. 保持 pass 语义与既有 G8 结构（`prevPassN`/`while(true)`/`continue` 模式）不破坏。
4. 自检类（TestKeyBoard/LCDTest/BeeperTest/FANTest）已有 while(true) 轮询结构，只补 TODO 语句，不改结构。
5. 删除全部 `TODO(自动转换` 注释后，再删除占位行，文件应能编译。
6. 每个 TODO 都要处理，不允许残留、不允许"跳过"、不允许把 TODO 改成空注释。
7. 验证：`dotnet build E:\WPFCli\Output\SYST\src\04.TestSteps\SYST.TestSteps\SYST.TestSteps.csproj -v q`
   必须 0 错误 0 新警告。循环修复到通过。构建通过后最后再 Grep 确认文件无 `TODO(自动转换`。

## 交付
返回：处理的 TODO 总数、删除的占位行数、最终构建结果（错误/警告数）、
以及任何无法确定语义、需要人工确认的条目清单（如有）。
