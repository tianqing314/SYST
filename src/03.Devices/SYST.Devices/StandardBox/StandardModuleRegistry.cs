using System.Reflection;
using Microsoft.Extensions.Logging;
using SYST.Core.Abstractions;
using SYST.Devices.Dut;

namespace SYST.Devices.StandardBox;

/// <summary>
/// 标准模块注册表：型号 → <see cref="IStandardModule"/> 工厂。新增标准设备（如正压/真空标准模块）
/// 只需给驱动类打 <see cref="DutDriverAttribute"/>（实现 <see cref="IStandardModule"/>）即可自动注册，
/// 无需改引擎/UI。同一型号若同时有真机与仿真实现，按 <paramref name="useReal"/>
/// 择一（真机开关关且无仿真变体时回落真机实现）；未注册型号抛异常。
/// </summary>
public sealed class StandardModuleRegistry
{
    /// <summary>
    /// 型号 → 标准模块驱动工厂（大小写不敏感）。
    /// </summary>
    private readonly Dictionary<string, Func<DeviceDescriptor, IStandardModule>> _factories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 日志工厂（供驱动建日志）。
    /// </summary>
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// 构造标准模块注册表。
    /// </summary>
    /// <param name="loggerFactory">日志工厂。</param>
    public StandardModuleRegistry(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// 注册某型号的标准模块驱动工厂。
    /// </summary>
    /// <param name="model">型号（=manifest ToolDevices 的 Model）。</param>
    /// <param name="factory">驱动工厂。</param>
    public void Register(string model, Func<DeviceDescriptor, IStandardModule> factory)
    {
        _factories[model] = factory;
    }

    /// <summary>
    /// 反射扫描本程序集（SYST.Devices），把实现 <see cref="IStandardModule"/> 且打
    /// <see cref="DutDriverAttribute"/> 的标准模块驱动按型号自动注册——新增标准设备只需给驱动类打特性。
    /// 同一型号若同时有真机与仿真实现，按 <paramref name="useReal"/> 择一
    /// （真机开关关且无仿真变体时回落真机实现，对齐纯真机设备的既有行为）。
    /// </summary>
    /// <param name="useReal">是否用真机驱动。</param>
    public void AutoRegisterFromAssembly(bool useReal = true)
    {
        var candidates = typeof(StandardModuleRegistry).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IStandardModule).IsAssignableFrom(t))
            .SelectMany(t => t.GetCustomAttributes<DutDriverAttribute>().Select(a => (a.Model, a.IsSimulation, Type: t)));

        foreach (var group in candidates.GroupBy(x => x.Model, StringComparer.OrdinalIgnoreCase))
        {
            var real = group.FirstOrDefault(x => !x.IsSimulation).Type;
            var sim = group.FirstOrDefault(x => x.IsSimulation).Type;
            var chosen = useReal ? real ?? sim : sim ?? real;
            if (chosen is null)
            {
                continue;
            }

            var type = chosen;
            var model = group.Key;
            Register(model, d => (IStandardModule)Activator.CreateInstance(type, d, _loggerFactory.CreateLogger($"STD.{d.Model}"))!);
        }
    }

    /// <summary>
    /// 按描述符型号创建标准模块驱动。
    /// </summary>
    /// <param name="descriptor">设备描述符（Model 决定驱动，Comm 为连接端点）。</param>
    /// <returns>标准模块驱动实例。</returns>
    /// <exception cref="InvalidOperationException">型号未注册。</exception>
    public IStandardModule Create(DeviceDescriptor descriptor)
    {
        if (_factories.TryGetValue(descriptor.Model, out var factory))
        {
            return factory(descriptor);
        }
        throw new InvalidOperationException($"未注册标准模块驱动：{descriptor.Model}（实现 IStandardModule 并打 [DutDriver(\"{descriptor.Model}\")]）");
    }
}
