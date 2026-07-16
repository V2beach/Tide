using UnityEngine;

/// <summary>
/// 连续引流索的确定性计算。它只回答“收放多快”和“盐木经过岔流时走哪边”，
/// 不持有场景、输入、时间或盐木所有权，便于用同一套规则驱动探针和运行时。
/// </summary>
public static class TideContinuousRoutingModel
{
    public const float DecisionTravel01 = 0.72f;
    public const float FeedNetThreshold01 = 0.68f;

    public static float EvaluateAdjustmentSpeed(
        float currentStrength01,
        float stormPressure01,
        float waterLoad01,
        bool releasing)
    {
        float resistance01 = Mathf.Clamp01(
            Mathf.Clamp01(currentStrength01) * 0.42f +
            Mathf.Clamp01(stormPressure01) * 0.34f +
            Mathf.Clamp01(waterLoad01) * 0.24f);
        float haulSpeed = Mathf.Lerp(1f / 1.5f, 1f / 2.7f, resistance01);
        return releasing ? haulSpeed * 1.2f : haulSpeed;
    }

    public static float AdvanceBoom01(
        float current01,
        float direction,
        float deltaTime,
        float currentStrength01,
        float stormPressure01,
        float waterLoad01)
    {
        if (Mathf.Abs(direction) <= 0.01f || deltaTime <= 0f)
        {
            return Mathf.Clamp01(current01);
        }

        bool releasing = direction < 0f;
        float speed = EvaluateAdjustmentSpeed(
            currentStrength01,
            stormPressure01,
            waterLoad01,
            releasing);
        return Mathf.Clamp01(current01 + Mathf.Sign(direction) * speed * deltaTime);
    }

    public static bool ShouldLockDecision(bool alreadyLocked, float travel01)
    {
        return !alreadyLocked && travel01 >= DecisionTravel01;
    }

    public static bool RoutesToNet(float boom01, bool netAvailable)
    {
        return netAvailable && boom01 >= FeedNetThreshold01;
    }
}
