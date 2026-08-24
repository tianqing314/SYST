using System.Collections.Generic;

namespace SYST.TestSteps.ConST560;

/// <summary>
/// 齿轮箱工装动作地址/通道组合（与旧 ConST575_SelfCheck_Script.SwitchGearEnum 接线一一对应）。
/// </summary>
public sealed class GearMap : List<GearEntry>
{
    public GearMap Add(int address, int channel)
    {
        base.Add(new GearEntry(address, channel));
        return this;
    }
}

public readonly record struct GearEntry(int Address, int Channel);

/// <summary>
/// 旧平台常用工装动作（地址-通道），供自检步骤引用。
/// 这里只列出 ConST560 自检流程里直接用到的组合；
/// 其余可在运行前通过 GearMap.Add 继续扩展。
/// </summary>
public static class ConST560Gears
{
    public static GearMap FullReset()
        => new GearMap().Add(1,1).Add(1,2).Add(1,3).Add(1,4).Add(1,5).Add(2,1).Add(2,2).Add(3,1).Add(3,2);
    public static GearMap ProbePush()
        => new GearMap().Add(1,2);
    public static GearMap ProbeRetract()
        => new GearMap().Add(1,2);
    public static GearMap TypeCInsert()
        => new GearMap().Add(1,3);
    public static GearMap TypeCRetract()
        => new GearMap().Add(1,3);
    public static GearMap AviationPlugInsert()
        => new GearMap().Add(1,4);
    public static GearMap AviationPlugRetract()
        => new GearMap().Add(1,4);
    public static GearMap ClampClose()
        => new GearMap().Add(1,1);
    public static GearMap ClampOpen()
        => new GearMap().Add(1,1);
    public static GearMap ChargeInsert()
        => new GearMap().Add(1,5);
    public static GearMap ChargeDisconnect()
        => new GearMap().Add(1,5);
    public static GearMap HartInnerSourceInnerResistor()
        => new GearMap().Add(1,6).Add(1,7);
    public static GearMap HartOuterSourceOuterResistor()
        => new GearMap().Add(1,8).Add(1,9);
    public static GearMap HartOuterSourceInnerResistor()
        => new GearMap().Add(1,10).Add(1,11);
    public static GearMap FFOuterSourceOuterResistor()
        => new GearMap().Add(2,1).Add(2,2);
    public static GearMap FFInnerSourceInnerResistor()
        => new GearMap().Add(2,3).Add(2,4);
    public static GearMap PAOuterSourceOuterResistor()
        => new GearMap().Add(3,1).Add(3,2);
}
