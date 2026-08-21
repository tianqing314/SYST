using SYST.Core.Abstractions;
using SYST.Devices.Abstractions;

namespace SYST.TestSteps.ConST811A;

/// <summary>
/// ConST811A Ops 工厂。创建公共 Ops 实例。
/// 共享处理器使用基类即可（不需要 P06 标准模块功能）。
/// DP/LLP 特有处理器需要 P06 时，可直接创建 <see cref="ConST811AOpsWithP06"/>。
/// </summary>
internal static class ConST811AOpsFactory
{
    /// <summary>
    /// 创建公共 Ops 实例（无 P06 标准模块）。
    /// </summary>
    public static ConST811AOpsBase Create(ITestContext ctx, CancellationToken ct)
    {
        return new ConST811AOpsBase(ctx, ct);
    }

    /// <summary>
    /// 创建带 P06 标准模块的 Ops 实例（DP/LLP 变体使用）。
    /// </summary>
    public static ConST811AOpsWithP06 CreateWithP06(ITestContext ctx, CancellationToken ct)
    {
        return new ConST811AOpsWithP06(ctx, ct);
    }
}
