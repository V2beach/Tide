using UnityEngine;

/// <summary>
/// 潮位预报与布网选择的纯计算层。
///
/// 预报只能减少不确定性，不能替玩家挑出一个永远正确的网深。场景层前向模拟
/// 下一批实物沿真实水路穿网后的有效接触和真实网压，这里只整理区间与后果，
/// 供阁楼、海图和调试探针共同消费。
/// </summary>
public static class TideNetForecastModel
{
    public const float LookoutUncertaintyMeters = 0.22f;
    public const float RepairedChartUncertaintyMeters = 0.08f;

    public readonly struct HighWaterBand
    {
        public HighWaterBand(float lowerY, float upperY, float uncertaintyMeters)
        {
            LowerY = lowerY;
            UpperY = upperY;
            UncertaintyMeters = uncertaintyMeters;
        }

        public float LowerY { get; }
        public float UpperY { get; }
        public float UncertaintyMeters { get; }
        public float WidthMeters => UpperY - LowerY;
    }

    public readonly struct NetChoice
    {
        public NetChoice(
            float predictedEffectiveContactSeconds,
            float requiredEffectiveContactSeconds,
            float tideCycleSeconds,
            int stressTier)
        {
            PredictedEffectiveContactSeconds = Mathf.Max(0f, predictedEffectiveContactSeconds);
            RequiredEffectiveContactSeconds = Mathf.Max(0.1f, requiredEffectiveContactSeconds);
            ContactRatio = PredictedEffectiveContactSeconds / RequiredEffectiveContactSeconds;
            TideEncounterFraction01 = Mathf.Clamp01(
                PredictedEffectiveContactSeconds / Mathf.Max(0.1f, tideCycleSeconds));
            StressTier = Mathf.Clamp(stressTier, 0, 3);
        }

        /// <summary>
        /// 同一批漂物单次穿过网口时的最大有效缠挂与所需接触之比。小于 1 表示
        /// 按当前潮窗会从网缘漏过；它不是概率，也不会改变漂物来源或材质。
        /// </summary>
        public float ContactRatio { get; }
        public float PredictedEffectiveContactSeconds { get; }
        public float RequiredEffectiveContactSeconds { get; }
        public float TideEncounterFraction01 { get; }

        public int StressTier { get; }
        public bool LikelyMissesFirstCatch => ContactRatio < 0.82f;
        public bool MarginalContact => ContactRatio >= 0.82f && ContactRatio < 1f;
        public bool HasReliableContact => ContactRatio >= 1f;
    }

    public static HighWaterBand EvaluateHighWaterBand(float predictedHighWaterY, bool repairedChart)
    {
        float uncertainty = repairedChart
            ? RepairedChartUncertaintyMeters
            : LookoutUncertaintyMeters;
        return new HighWaterBand(
            predictedHighWaterY - uncertainty,
            predictedHighWaterY + uncertainty,
            uncertainty);
    }

    public static NetChoice EvaluateNetChoice(
        float predictedEffectiveContactSeconds,
        float requiredEffectiveContactSeconds,
        float tideCycleSeconds,
        int stressTier)
    {
        return new NetChoice(
            predictedEffectiveContactSeconds,
            requiredEffectiveContactSeconds,
            tideCycleSeconds,
            stressTier);
    }
}
