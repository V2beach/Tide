using UnityEngine;

/// <summary>
/// 短航抛钩、收绳与张力的确定性模型。它不读取输入、不持有场景对象，
/// Controller 只把真实绳长、相对速度、天气和船况交给这里计算。
/// </summary>
public static class TideContinuousSalvageModel
{
    public const float ThrowDurationSeconds = 0.34f;
    public const float ThrowRetractSeconds = 0.22f;
    public const float CriticalTension01 = 0.88f;
    public const float DetachHoldSeconds = 0.42f;
    public const float SecuredProgress01 = 0.999f;

    public static float AdvanceThrow01(float current01, float deltaTime, bool held)
    {
        float duration = held ? ThrowDurationSeconds : ThrowRetractSeconds;
        float direction = held ? 1f : -1f;
        return Mathf.Clamp01(current01 + direction * Mathf.Max(0f, deltaTime) / duration);
    }

    public static float EvaluateTension01(
        float ropeDistance,
        float allowedRopeLength,
        float relativeSpeed,
        float stormPressure01)
    {
        float stretch01 = Mathf.InverseLerp(
            0f,
            0.72f,
            Mathf.Max(0f, ropeDistance - Mathf.Max(0.18f, allowedRopeLength)));
        float speedLoad01 = Mathf.InverseLerp(0.12f, 1.35f, Mathf.Abs(relativeSpeed));
        float stormLoad01 = Mathf.Clamp01(stormPressure01);
        return Mathf.Clamp01(stretch01 * 0.58f + speedLoad01 * 0.48f + stormLoad01 * 0.14f);
    }

    public static float EvaluateHaulRate(
        float relativeSpeed,
        float tension01,
        float stormPressure01,
        float waterIngress01)
    {
        float speedMatch01 = 1f - Mathf.InverseLerp(0.08f, 0.92f, Mathf.Abs(relativeSpeed));
        float workableTension01 = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.96f, tension01));
        float weatherFactor = Mathf.Lerp(1f, 0.62f, Mathf.Clamp01(stormPressure01));
        float ingressFactor = Mathf.Lerp(1f, 0.7f, Mathf.Clamp01(waterIngress01));
        return Mathf.Lerp(0.08f, 0.72f, speedMatch01 * workableTension01) * weatherFactor * ingressFactor;
    }

    public static float AdvanceHaul01(
        float current01,
        float deltaTime,
        bool held,
        float relativeSpeed,
        float tension01,
        float stormPressure01,
        float waterIngress01)
    {
        if (!held || deltaTime <= 0f)
        {
            return Mathf.Clamp01(current01);
        }

        float rate = EvaluateHaulRate(relativeSpeed, tension01, stormPressure01, waterIngress01);
        return Mathf.Clamp01(current01 + rate * deltaTime);
    }

    public static float AdvanceOverstrainSeconds(float currentSeconds, float tension01, float deltaTime)
    {
        float safeDelta = Mathf.Max(0f, deltaTime);
        if (tension01 >= CriticalTension01)
        {
            return Mathf.Max(0f, currentSeconds) + safeDelta;
        }

        return Mathf.Max(0f, currentSeconds - safeDelta * 1.6f);
    }

    public static bool ShouldDetach(float overstrainSeconds)
    {
        return overstrainSeconds >= DetachHoldSeconds;
    }

    public static float EvaluateTowLoad01(float haulProgress01, float tension01, bool secured)
    {
        float attachedLoad01 = Mathf.Clamp01(haulProgress01) * 0.72f + Mathf.Clamp01(tension01) * 0.28f;
        return secured ? 1f : Mathf.Clamp01(attachedLoad01);
    }
}
