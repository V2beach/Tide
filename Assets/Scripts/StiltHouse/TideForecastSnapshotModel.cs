using UnityEngine;

/// <summary>
/// 一次真实观测留下的下一高潮快照。
///
/// TargetCycleOrdinal 使用连续天文潮次，不使用睡眠或故事轮次。上下界在观测
/// 当刻固定；后续天气变化可以让实际高潮偏离预报，却不能反过来改写旧记录。
/// </summary>
public readonly struct TideForecastSnapshot
{
    public TideForecastSnapshot(
        int targetCycleOrdinal,
        float lowerY,
        float upperY,
        float uncertaintyMeters,
        bool repairedChart)
    {
        TargetCycleOrdinal = targetCycleOrdinal;
        LowerY = lowerY;
        UpperY = upperY;
        UncertaintyMeters = uncertaintyMeters;
        RepairedChart = repairedChart;
    }

    public int TargetCycleOrdinal { get; }
    public float LowerY { get; }
    public float UpperY { get; }
    public float UncertaintyMeters { get; }
    public bool RepairedChart { get; }
    public float WidthMeters => UpperY - LowerY;
    public bool IsValid => TargetCycleOrdinal >= 0 && UpperY > LowerY;

    public TideNetForecastModel.HighWaterBand ToHighWaterBand()
    {
        return new TideNetForecastModel.HighWaterBand(
            LowerY,
            UpperY,
            UncertaintyMeters);
    }
}

/// <summary>
/// 把连续潮汐预测冻结成一次可过期的观察结果。模型不推进时钟，也不决定网深；
/// 调用侧只需传入观测当刻的下一高潮编号和预测高度。
/// </summary>
public static class TideForecastSnapshotModel
{
    public static TideForecastSnapshot Capture(
        int targetCycleOrdinal,
        float predictedHighWaterY,
        bool repairedChart)
    {
        TideNetForecastModel.HighWaterBand band =
            TideNetForecastModel.EvaluateHighWaterBand(
                predictedHighWaterY,
                repairedChart);
        return new TideForecastSnapshot(
            Mathf.Max(0, targetCycleOrdinal),
            band.LowerY,
            band.UpperY,
            band.UncertaintyMeters,
            repairedChart);
    }

    /// <summary>
    /// 快照只服务它记录的那一次“下一高潮”。当前时刻跨过该高潮后，
    /// GetNextHighWaterCycleOrdinal 会自然指向下一潮，旧快照随即失效。
    /// </summary>
    public static bool IsCurrent(
        TideForecastSnapshot snapshot,
        float currentPhase01,
        int currentAstronomicalCycleOrdinal)
    {
        if (!snapshot.IsValid)
        {
            return false;
        }

        int nextHighCycleOrdinal =
            TideMixedSemidiurnalModel.GetNextHighWaterCycleOrdinal(
                currentPhase01,
                currentAstronomicalCycleOrdinal);
        return snapshot.TargetCycleOrdinal == nextHighCycleOrdinal;
    }
}
