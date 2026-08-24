using System.Threading;
using System.Threading.Tasks;
using SYST.Core.Abstractions;

namespace SYST.Devices.Abstractions.Test;

/// <summary>
/// ZCZH VA 采集器最小桩接口（用于 E05 自检电流/电压现场回测）。
/// 真实 DLL 为 GeneralZCZH；这里只保留自检脚本直接用到的读值能力，后续接真驱动。
/// </summary>
public interface IZCZH : IStandardModule
{
    Task<bool> SetMeasureModeAsync(string mode, CancellationToken ct = default);
    Task<double> ReadValueAsync(string unit, CancellationToken ct = default);
}
