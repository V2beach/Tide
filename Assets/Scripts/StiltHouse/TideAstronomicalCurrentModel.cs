using UnityEngine;

/// <summary>
/// 把月相造成的天文潮差转换成统一的实际潮流速度。
///
/// 这个模型故意不读取场景、天气、船况或输入：风暴增水可以改变平均水位和浪载，
/// 但不能伪装成每半个潮周期自动往返的天文潮流。调用方可分别消费实际米制速度
/// 和归一化潮相，避免在网压等已经单独读取潮强的系统里重复乘月相倍率。
/// </summary>
public static class TideAstronomicalCurrentModel
{
    public const float MinimumAstronomicalRangeMeters = 0.68f;
    public const float MaximumAstronomicalRangeMeters = 1.32f;

    /// <summary>
    /// 返回当前月相对应的低潮到高潮天文潮差，不包含风暴增水。
    /// </summary>
    public static float EvaluateAstronomicalRangeMeters(float tideStrength01)
    {
        return Mathf.Lerp(
            MinimumAstronomicalRangeMeters,
            MaximumAstronomicalRangeMeters,
            Mathf.Clamp01(tideStrength01));
    }

    /// <summary>
    /// 返回实际世界流速。正值为离岸退潮流，负值为向岸涨潮流。
    ///
    /// meanTransportSpeed 保留原切片半潮累计位移的玩法标尺；潮差倍率只改变
    /// 同一潮相的流速强弱。referenceTideStrength01 是兼容校准点，不是新的
    /// 难度参数：当当前潮强等于它时，结果与旧正弦潮流完全相同。
    /// </summary>
    public static float EvaluateSignedSpeed(
        float clockSeconds,
        float cycleSeconds,
        float tideStrength01,
        float referenceTideStrength01,
        float meanTransportSpeed,
        float ebbBoost)
    {
        float cycle = Mathf.Max(8f, cycleSeconds);
        float phase01 = Mathf.Repeat(clockSeconds / cycle, 1f);
        float signedFlowWave = -Mathf.Sin(phase01 * Mathf.PI * 2f);
        return EvaluateSignedSpeedFromWave(
            signedFlowWave,
            tideStrength01,
            referenceTideStrength01,
            meanTransportSpeed,
            ebbBoost);
    }

    /// <summary>
    /// 把任意由真实水位解析导数得到的潮流波形换算为实际世界流速。
    /// 简单半日潮和混合半日潮都走这一个米制倍率与涨退潮方向修正，避免
    /// 水位模型升级后又复制一份春/小潮换算公式。
    /// </summary>
    public static float EvaluateSignedSpeedFromWave(
        float signedFlowWave,
        float tideStrength01,
        float referenceTideStrength01,
        float meanTransportSpeed,
        float ebbBoost)
    {
        float basePeakSpeed = Mathf.Max(0f, meanTransportSpeed) * Mathf.PI * 0.5f;
        float referenceRange = EvaluateAstronomicalRangeMeters(referenceTideStrength01);
        float currentRange = EvaluateAstronomicalRangeMeters(tideStrength01);
        float rangeScale = currentRange / Mathf.Max(0.001f, referenceRange);
        float directionalBoost = signedFlowWave > 0f ? Mathf.Max(0f, ebbBoost) : 1f;
        return signedFlowWave * basePeakSpeed * rangeScale * directionalBoost;
    }

    /// <summary>
    /// 返回当前潮相在本潮峰值内的位置。它只描述“离平流有多远”，不描述
    /// 大潮比小潮更强；因此已有同时读取 tideStrength 的受力公式不会双重计强。
    /// </summary>
    public static float EvaluateSignedPhase01(float clockSeconds, float cycleSeconds)
    {
        float cycle = Mathf.Max(8f, cycleSeconds);
        float phase01 = Mathf.Repeat(clockSeconds / cycle, 1f);
        return Mathf.Clamp(-Mathf.Sin(phase01 * Mathf.PI * 2f), -1f, 1f);
    }

    /// <summary>
    /// 把实际流速换算为相对最强大潮退潮流的 0..1 强度，供风险反馈使用。
    /// </summary>
    public static float EvaluateGlobalCurrentStrength01(
        float absoluteSpeed,
        float referenceTideStrength01,
        float meanTransportSpeed,
        float ebbBoost)
    {
        float basePeakSpeed = Mathf.Max(0f, meanTransportSpeed) * Mathf.PI * 0.5f;
        float springScale = MaximumAstronomicalRangeMeters /
            Mathf.Max(0.001f, EvaluateAstronomicalRangeMeters(referenceTideStrength01));
        float springEbbPeak = basePeakSpeed * springScale * Mathf.Max(0f, ebbBoost);
        return Mathf.Clamp01(Mathf.Abs(absoluteSpeed) / Mathf.Max(0.001f, springEbbPeak));
    }
}
