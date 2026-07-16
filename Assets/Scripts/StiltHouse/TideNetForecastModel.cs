using UnityEngine;

/// <summary>
/// 潮位预报与布网选择的纯计算层。
///
/// 预报只能减少不确定性，不能替玩家挑出一个“正确网深”。场景层传入下一次
/// 高潮、候选网深的预计触水时间和真实网压，这里只返回区间与后果，供阁楼、
/// 海图和调试探针共同消费。
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
            float predictedExposureSeconds,
            float requiredExposureSeconds,
            float tideCycleSeconds,
            int stressTier)
        {
            PredictedExposureSeconds = Mathf.Max(0f, predictedExposureSeconds);
            RequiredExposureSeconds = Mathf.Max(0.1f, requiredExposureSeconds);
            ExposureRatio = PredictedExposureSeconds / RequiredExposureSeconds;
            TideContactFraction01 = Mathf.Clamp01(
                PredictedExposureSeconds / Mathf.Max(0.1f, tideCycleSeconds));
            StressTier = Mathf.Clamp(stressTier, 0, 3);
        }

        /// <summary>
        /// 预计触水时间与取得首批潮获所需时间的比值。小于 1 可能空网，
        /// 大于 1 表示有余量；它不是成功概率，也不承诺漂物一定进入网口。
        /// </summary>
        public float ExposureRatio { get; }
        public float PredictedExposureSeconds { get; }
        public float RequiredExposureSeconds { get; }
        public float TideContactFraction01 { get; }

        public int StressTier { get; }
        public bool LikelyMissesFirstCatch => ExposureRatio < 0.82f;
        public bool MarginalContact => ExposureRatio >= 0.82f && ExposureRatio < 1.18f;
        public bool HasReliableContact => ExposureRatio >= 1.18f;
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
        float predictedExposureSeconds,
        float requiredExposureSeconds,
        float tideCycleSeconds,
        int stressTier)
    {
        return new NetChoice(
            predictedExposureSeconds,
            requiredExposureSeconds,
            tideCycleSeconds,
            stressTier);
    }
}
