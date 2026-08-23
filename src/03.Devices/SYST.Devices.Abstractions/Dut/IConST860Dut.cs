namespace SYST.Devices.Abstractions.Dut
{
    /// <summary>
    /// ConST860（P25 气压/液压整机）被检设备接口。
    /// 采用“基础接口 + 扩展接口”分层，避免单一巨型接口：
    /// <list type="bullet">
    ///   <item><see cref="IConST860Dut"/> — 基础接口，所有 P25 共用的连接/自检/泄漏/泄压/泵转速。</item>
    ///   <item><see cref="IConST860PressureQBase"/> — 气压（Q）扩展接口：量程、控压、自整定、目标压力。</item>
    ///   <item><see cref="IConST860PressureYGbk"/> — 液压/工本站（Y）扩展接口：校准、外循环、液泵效率、液泵调速、资阳 D05 快充。</item>
    /// </list>
    /// 逐步演进：后续新增时仅需新增扩展接口和对应 Handler，不破坏旧代码。
    /// </summary>
    public interface IConST860Dut : IDutDevice
    {
        /// <summary>补充连接（重连）。</summary>
        Task<bool> ReplenishLinkAsync(CancellationToken ct = default);

        /// <summary>设备自检（公共抓手/电平）。</summary>
        Task<bool> SelfCheckAsync(CancellationToken ct = default);

        /// <summary>获取泵的实时转速 rpm（气泵或液泵）。</summary>
        Task<double> GetPumpRpmAsync(CancellationToken ct = default);

        /// <summary>充能（蓄能器充电）。</summary>
        /// <param name="target">目标压力 kPa；若 char='#'，表示快速充满。</param>
        Task<double> ChargeAsync(char target, CancellationToken ct = default);

        /// <summary>测量泄漏（次关键路径）。</summary>
        Task<double> MeasureLeakAsync(CancellationToken ct = default);

        /// <summary>关闭维修泄压阀。</summary>
        Task CloseRepairVentAsync(CancellationToken ct = default);

        /// <summary>写入介质类型（气/油/水）。</summary>
        Task<bool> SetMediumAsync(string medium, CancellationToken ct = default);
    }

    /// <summary>
    /// 气压（Q）扩展能力。
    /// </summary>
    public interface IConST860PressureQBase : IConST860Dut
    {
        /// <summary>设置计量模式（GW/GW4 等不同模式）。</summary>
        Task<bool> SetMeasureModeAsync(string mode, CancellationToken ct = default);

        /// <summary>启动/停止自整定。</summary>
        Task<bool> SelfTuningAsync(bool start, CancellationToken ct = default);

        /// <summary>读取自整定结果。</summary>
        Task<string> ReadSelfTuningResultAsync(CancellationToken ct = default);

        /// <summary>获取当前输出压力 kPa。</summary>
        Task<double> ReadOutputPressureAsync(CancellationToken ct = default);

        /// <summary>读取 PV/SV 显示值。</summary>
        Task<(double PV, double SV)> ReadPvSvAsync(CancellationToken ct = default);

        /// <summary>设置目标压力值 kPa。</summary>
        Task<bool> SetTargetPressureAsync(double pressureKpa, CancellationToken ct = default);

        /// <summary>读取量程列表（每个元素形如 "索引:量程下限~上限"）。</summary>
        Task<IReadOnlyList<string>> GetRangeListAsync(CancellationToken ct = default);

        /// <summary>切换量程。</summary>
        Task<bool> SetCurrentRangeAsync(int rangeIndex, CancellationToken ct = default);

        /// <summary>读取当前量程索引。</summary>
        Task<int> GetCurrentRangeAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// 液压 / 工本站（Y）扩展能力。
    /// </summary>
    public interface IConST860PressureYGbk : IConST860Dut
    {
        /// <summary>读取外循环状态。</summary>
        Task<bool> GetExternalLoopStateAsync(CancellationToken ct = default);

        /// <summary>启动/停止液源模块校准。</summary>
        Task<bool> RunCalibrationAsync(bool start, CancellationToken ct = default);

        /// <summary>执行泵效率或电机调速（液压泵专用）。</summary>
        Task<double> PumpEfficiencyTestAsync(CancellationToken ct = default);

        /// <summary>液泵电机速率控制（0~100%）。</summary>
        Task<bool> SetPumpSpeedAsync(int percentage, CancellationToken ct = default);

        /// <summary>快速充满控制板。资阳 D05 专用缩写。</summary>
        /// <param name="valve">控制阀：V2/V6/Z4。</param>
        Task<bool> ChargeControlBoardAsync(string valve, CancellationToken ct = default);
    }
}