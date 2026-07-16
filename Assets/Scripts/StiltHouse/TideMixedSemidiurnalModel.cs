using UnityEngine;

/// <summary>
/// 远海岛屿采用混合半日潮：一个月球日里约有两次高潮，但相邻高潮并不
/// 完全等高。这里用两潮周期的平滑振幅包络近似月球赤纬造成的日不等现象。
///
/// 模型不读取场景、天气、故事潮次或玩家状态。天气增水必须在调用侧独立
/// 叠加；故事 tideRound 也不能兼当天文潮次，否则睡眠、死亡或剧情推进会
/// 凭空改变海面。
/// </summary>
public static class TideMixedSemidiurnalModel
{
    public const float LunarDeclinationPeriodDays = 27.3217f;
    public const float MaximumInequalityRatio = 0.08f;

    /// <summary>
    /// 返回当前月球赤纬周期对应的相邻高潮不等高比例。
    /// continuousDeclinationDays 必须是连续增加的天数，不能传入每 29.53 天
    /// 回绕的月龄；月相周期和月球赤纬周期并不相同。该值不含随机数，
    /// 赤纬接近零时两潮近似等高，赤纬较大时差异更明显。
    /// </summary>
    public static float EvaluateInequalityRatio(float continuousDeclinationDays)
    {
        float declinationPhase01 = Mathf.Repeat(
            continuousDeclinationDays / LunarDeclinationPeriodDays,
            1f);
        float declinationMagnitude01 = Mathf.Abs(
            Mathf.Sin(declinationPhase01 * Mathf.PI * 2f));
        return declinationMagnitude01 * MaximumInequalityRatio;
    }

    /// <summary>
    /// 返回未调制的半日潮高度：低潮为 0，高潮为 1。
    /// </summary>
    public static float EvaluateBaseHeight01(float phase01)
    {
        float phase = Mathf.Repeat(phase01, 1f);
        return 0.5f - Mathf.Cos(phase * Mathf.PI * 2f) * 0.5f;
    }

    /// <summary>
    /// 返回当前潮周期内的平滑潮差包络。第一潮的高潮尺度保持 1，下一潮
    /// 降为 (1-r)/(1+r)，随后按两个潮周期连续重复。低潮处即使包络不为 1，
    /// 基础高度仍为 0，因此不会抬高低潮基准或制造水位跳变。
    /// </summary>
    public static float EvaluateRangeEnvelope(
        float phase01,
        int astronomicalCycleOrdinal,
        float inequalityRatio)
    {
        float phase = Mathf.Repeat(phase01, 1f);
        float ratio = Mathf.Clamp(inequalityRatio, 0f, MaximumInequalityRatio);
        float unwrappedCycle = astronomicalCycleOrdinal + phase;
        return (1f + ratio * Mathf.Sin(unwrappedCycle * Mathf.PI)) /
            Mathf.Max(0.001f, 1f + ratio);
    }

    /// <summary>
    /// 返回加入相邻潮不等高后的天文潮高度。结果仍以“较高潮潮差”为 1，
    /// 因而可以直接乘现有米制天文潮差。
    /// </summary>
    public static float EvaluateHeight01(
        float phase01,
        int astronomicalCycleOrdinal,
        float inequalityRatio)
    {
        return EvaluateBaseHeight01(phase01) * EvaluateRangeEnvelope(
            phase01,
            astronomicalCycleOrdinal,
            inequalityRatio);
    }

    /// <summary>
    /// 返回水位解析导数对应的带方向潮流波形。正值为离岸退潮，负值为
    /// 向岸涨潮。它与 EvaluateHeight01 是同一个函数的导数，不允许再用
    /// 另一条正弦近似，否则高低潮平流和视觉水位会逐渐错相。
    /// </summary>
    public static float EvaluateSignedCurrentWave(
        float phase01,
        int astronomicalCycleOrdinal,
        float inequalityRatio)
    {
        float phase = Mathf.Repeat(phase01, 1f);
        float ratio = Mathf.Clamp(inequalityRatio, 0f, MaximumInequalityRatio);
        float unwrappedCycle = astronomicalCycleOrdinal + phase;
        float baseHeight01 = EvaluateBaseHeight01(phase);
        float semidiurnalDerivative = Mathf.Sin(phase * Mathf.PI * 2f);
        float envelopeNumerator = 1f + ratio * Mathf.Sin(unwrappedCycle * Mathf.PI);
        float envelopeDerivative = ratio * Mathf.Cos(unwrappedCycle * Mathf.PI);
        float normalizedWaterDerivative =
            (semidiurnalDerivative * envelopeNumerator + baseHeight01 * envelopeDerivative) /
            Mathf.Max(0.001f, 1f + ratio);
        return -normalizedWaterDerivative;
    }

    /// <summary>
    /// 返回指定潮周期高潮时的潮差尺度。
    /// </summary>
    public static float EvaluateHighWaterScale(
        int astronomicalCycleOrdinal,
        float inequalityRatio)
    {
        return EvaluateHeight01(0.5f, astronomicalCycleOrdinal, inequalityRatio);
    }

    /// <summary>
    /// 返回从当前潮相向前遇到的下一次高潮属于哪个天文潮周期。
    /// </summary>
    public static int GetNextHighWaterCycleOrdinal(
        float currentPhase01,
        int currentAstronomicalCycleOrdinal)
    {
        float phase = Mathf.Repeat(currentPhase01, 1f);
        return phase <= 0.5f
            ? currentAstronomicalCycleOrdinal
            : currentAstronomicalCycleOrdinal + 1;
    }
}
