using UnityEngine;

public readonly struct TideBoatConditionPerformanceSample
{
    public TideBoatConditionPerformanceSample(
        float hullTightness01,
        float hullSpeedMultiplier,
        float baseLeakRatePerSecond,
        float sailDriveEfficiency01,
        float sailTrimRatePerSecond,
        float ballastShiftRatePerSecond,
        float bailRateMultiplier,
        float bailingDragMultiplier)
    {
        HullTightness01 = hullTightness01;
        HullSpeedMultiplier = hullSpeedMultiplier;
        BaseLeakRatePerSecond = baseLeakRatePerSecond;
        SailDriveEfficiency01 = sailDriveEfficiency01;
        SailTrimRatePerSecond = sailTrimRatePerSecond;
        BallastShiftRatePerSecond = ballastShiftRatePerSecond;
        BailRateMultiplier = bailRateMultiplier;
        BailingDragMultiplier = bailingDragMultiplier;
    }

    public float HullTightness01 { get; }
    public float HullSpeedMultiplier { get; }
    public float BaseLeakRatePerSecond { get; }
    public float SailDriveEfficiency01 { get; }
    public float SailTrimRatePerSecond { get; }
    public float BallastShiftRatePerSecond { get; }
    public float BailRateMultiplier { get; }
    public float BailingDragMultiplier { get; }
}

/// <summary>
/// 把三处可见船体维修映射到各自真实职责。它不拥有库存、维修阶段或航行状态：
/// 船壳负责水密和船速上限，船帆负责收放与风力利用，舱底负责压舱移动和舀水。
/// 这样新增部件时可以扩展自己的后果，而不是继续放大一个含义模糊的总船况分数。
/// </summary>
public static class TideBoatConditionPerformanceModel
{
    public static TideBoatConditionPerformanceSample Evaluate(
        int hullIntegrity,
        int sailIntegrity,
        int cabinIntegrity)
    {
        float hull01 = Mathf.Clamp01(hullIntegrity / 3f);
        float sail01 = Mathf.Clamp01(sailIntegrity / 2f);
        float cabin01 = Mathf.Clamp01(cabinIntegrity / 2f);
        return new TideBoatConditionPerformanceSample(
            hull01,
            Mathf.Lerp(0.76f, 1.04f, hull01),
            Mathf.Lerp(0.032f, 0.002f, hull01),
            Mathf.Lerp(0.42f, 1f, sail01),
            Mathf.Lerp(0.22f, 0.42f, sail01),
            Mathf.Lerp(0.38f, 0.65f, cabin01),
            Mathf.Lerp(0.72f, 1.28f, cabin01),
            Mathf.Lerp(1.2f, 0.82f, cabin01));
    }
}
