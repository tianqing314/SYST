using System.Threading;
using System.Threading.Tasks;
using SYST.Core.Abstractions;

namespace SYST.Devices.Abstractions.Test;

/// <summary>
/// ConST326 标准信号源最小桩接口（用于 E05 自检串行仪器协同）。
/// 真实 DLL 为 Xmas11.Comm.Devices.DPG2 / ConST326 命令集；
/// 这里只保留自检脚本直接用到的输出/测量能力，后续可换成真驱动。
/// </summary>
public interface IConST326 : IStandardModule
{
    Task<bool> SetMeasureModeAsync(string mode, CancellationToken ct = default);
    Task<bool> SetOutputModeAsync(string mode, CancellationToken ct = default);
    Task<bool> SetOutputAsync(string type, double value, string unit, CancellationToken ct = default);
    Task<bool> SetPower24VAsync(bool on, CancellationToken ct = default);
    Task<double> ReadMeasureValueAsync(string unit, CancellationToken ct = default);
}
