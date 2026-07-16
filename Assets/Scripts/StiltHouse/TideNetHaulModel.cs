using UnityEngine;

/// <summary>
/// 收网的人力与水动力模型。
///
/// 这里刻意只接收归一化物理量，不读取场景或输入。这样高低潮的平流窗口、
/// 中段急流、网深和挂物重量只有一套计算，运行交互、预报和验收不会各写一份公式。
/// </summary>
public static class TideNetHaulModel
{
    public const float MinimumStrokeSeconds = 0.82f;
    public const float MaximumStrokeSeconds = 1.22f;

    public readonly struct Step
    {
        public Step(float phase01, float effort01, float load01, float progressDelta)
        {
            Phase01 = phase01;
            Effort01 = effort01;
            Load01 = load01;
            ProgressDelta = progressDelta;
        }

        public float Phase01 { get; }
        public float Effort01 { get; }
        public float Load01 { get; }
        public float ProgressDelta { get; }
    }

    /// <summary>
    /// 潮流阻力近似与流速平方成正比。浸没决定湿网自重和受水面积，挂物与网深
    /// 提供次要阻力；所以高潮平流仍然沉，但比同样水深的急流窗口容易收回。
    /// </summary>
    public static float EvaluateHydrodynamicLoad01(
        float submersion01,
        float currentStrength01,
        float catchLoad01,
        float depth01)
    {
        float submerged = Mathf.Clamp01(submersion01);
        float current = Mathf.Clamp01(currentStrength01);
        float catchLoad = Mathf.Clamp01(catchLoad01);
        float depth = Mathf.Clamp01(depth01);
        float currentDrag = current * current;

        return Mathf.Clamp01(
            submerged * 0.28f +
            currentDrag * 0.52f +
            catchLoad * 0.12f +
            depth * 0.08f);
    }

    /// <summary>
    /// 返回带方向的网面拖曳。方向来自实际潮流，幅度按流速平方增长；干网没有
    /// 受水面积，挂物和散开的破口只会略微放大已有拖曳，不会凭空制造流向。
    /// </summary>
    public static float EvaluateSignedCurrentDrag01(
        float submersion01,
        float signedCurrent01,
        float catchLoad01,
        float fraying01)
    {
        float submerged = Mathf.Clamp01(submersion01);
        float current = Mathf.Clamp(signedCurrent01, -1f, 1f);
        float catchArea = Mathf.Lerp(1f, 1.18f, Mathf.Clamp01(catchLoad01));
        float frayedArea = Mathf.Lerp(1f, 1.08f, Mathf.Clamp01(fraying01));
        float signedSquare = Mathf.Sign(current) * current * current;
        return Mathf.Clamp(
            signedSquare * submerged * catchArea * frayedArea,
            -1f,
            1f);
    }

    /// <summary>
    /// 把水动力换算成世界空间位移。网被逐段提出水面后，受水面积连续减少，
    /// 因此不会在收完前一帧还保持整张网的横向偏移。
    /// </summary>
    public static float EvaluateVisibleShiftWorldX(
        float signedDrag01,
        float haulProgress01,
        float maximumShiftWorld)
    {
        float hauledOut01 = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(haulProgress01));
        float remainingWetArea01 = 1f - hauledOut01;
        return Mathf.Clamp(signedDrag01, -1f, 1f) *
            Mathf.Max(0f, maximumShiftWorld) * remainingWetArea01;
    }

    public static float EvaluateStrokeSeconds(float load01)
    {
        return Mathf.Lerp(
            MinimumStrokeSeconds,
            MaximumStrokeSeconds,
            Mathf.Clamp01(load01));
    }

    public static float EvaluateEffort01(float phase01)
    {
        float phase = Mathf.Repeat(phase01, 1f);
        if (phase < 0.18f)
        {
            return Mathf.Lerp(0.08f, 0.3f, Mathf.SmoothStep(0f, 1f, phase / 0.18f));
        }

        if (phase < 0.58f)
        {
            return Mathf.Lerp(0.3f, 1f, Mathf.SmoothStep(0f, 1f, (phase - 0.18f) / 0.4f));
        }

        if (phase < 0.72f)
        {
            return 1f;
        }

        return Mathf.Lerp(1f, 0.08f, Mathf.SmoothStep(0f, 1f, (phase - 0.72f) / 0.28f));
    }

    public static Step EvaluateStep(
        float previousPhase01,
        float deltaTime,
        float baseHaulDurationSeconds,
        float submersion01,
        float currentStrength01,
        float catchLoad01,
        float depth01)
    {
        float load01 = EvaluateHydrodynamicLoad01(
            submersion01,
            currentStrength01,
            catchLoad01,
            depth01);
        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        float phase01 = Mathf.Repeat(
            previousPhase01 + safeDeltaTime / EvaluateStrokeSeconds(load01),
            1f);
        float effort01 = EvaluateEffort01(phase01);

        // 流急时一把拉得更慢，同样距离也需要更多把。进度始终为正，玩家松手
        // 只会锁绳暂停，不会因等待平流窗口而倒退或丢失已经收回的长度。
        float effortRate = Mathf.Lerp(0.18f, 1.68f, effort01);
        float duration = Mathf.Max(0.1f, baseHaulDurationSeconds) *
            Mathf.Lerp(1f, 2.15f, load01);
        float progressDelta = safeDeltaTime * effortRate / duration;

        return new Step(phase01, effort01, load01, progressDelta);
    }
}
