using UnityEngine;

[System.Serializable]
public struct TideRainCisternState
{
    public float StoredLiters;
    public float SaltFraction01;
    public float Crack01;
    public float HighestSaltLine01;
}

/// <summary>
/// 裸岩岛没有稳定地下水。蓄水池只接屋顶雨水，并会持续漏水、蒸发以及在暴潮
/// 越过池口时混入盐水。所有输入均使用现实单位，游戏日压缩不会加速一秒内的流量。
/// </summary>
public static class TideRainCisternModel
{
    public const float CapacityLiters = 260f;
    public const float RoofCatchAreaSquareMeters = 18f;
    public const float CatchEfficiency01 = 0.76f;
    public const float DryWeatherEvaporationLitersPerDay = 3.2f;

    public static TideRainCisternState CreateDamaged(float initialFreshLiters = 24f)
    {
        return new TideRainCisternState
        {
            StoredLiters = Mathf.Clamp(initialFreshLiters, 0f, CapacityLiters),
            SaltFraction01 = 0.04f,
            Crack01 = 0.72f,
            HighestSaltLine01 = 0.12f
        };
    }

    public static TideRainCisternState Advance(
        TideRainCisternState state,
        float deltaSeconds,
        float rainMillimetersPerHour,
        float roofIntegrity01,
        float stormOvertopping01)
    {
        float dt = Mathf.Max(0f, deltaSeconds);
        float rainRate = Mathf.Max(0f, rainMillimetersPerHour);
        float usableRoof01 = Mathf.Lerp(0.28f, 1f, Mathf.Clamp01(roofIntegrity01));
        // 1 mm falling on 1 m2 equals exactly 1 litre.
        float collectedLiters = rainRate * RoofCatchAreaSquareMeters * CatchEfficiency01 *
            usableRoof01 * dt / 3600f;
        float evaporationLiters = DryWeatherEvaporationLitersPerDay * dt / 86400f;
        float leakLiters = state.StoredLiters * Mathf.Clamp01(state.Crack01) * 0.00018f * dt;

        float previousLiters = Mathf.Clamp(state.StoredLiters, 0f, CapacityLiters);
        float freshAfterWeather = Mathf.Clamp(
            previousLiters + collectedLiters - evaporationLiters - leakLiters,
            0f,
            CapacityLiters);
        float previousSaltLiters = previousLiters * Mathf.Clamp01(state.SaltFraction01);
        float retainedSaltLiters = previousLiters > 0.001f
            ? previousSaltLiters * Mathf.Clamp01(freshAfterWeather / previousLiters)
            : 0f;

        float overtop01 = Mathf.Clamp01(stormOvertopping01);
        float intrudingSaltLiters = CapacityLiters * 0.0024f * overtop01 * dt;
        float mixedLiters = Mathf.Clamp(freshAfterWeather + intrudingSaltLiters, 0f, CapacityLiters);
        float mixedSaltLiters = Mathf.Min(mixedLiters, retainedSaltLiters + intrudingSaltLiters * 0.92f);

        state.StoredLiters = mixedLiters;
        state.SaltFraction01 = mixedLiters > 0.001f
            ? Mathf.Clamp01(mixedSaltLiters / mixedLiters)
            : 0f;
        state.HighestSaltLine01 = Mathf.Max(
            state.HighestSaltLine01,
            Mathf.Clamp01(mixedLiters / CapacityLiters) * overtop01);
        return state;
    }

    public static float GetDrinkableLiters(TideRainCisternState state)
    {
        // 高于约 2% 的盐分已经不适合作为饮用水；保留连续值供视觉和后续净化。
        float potability01 = 1f - Mathf.SmoothStep(0.006f, 0.022f, state.SaltFraction01);
        return Mathf.Max(0f, state.StoredLiters) * potability01;
    }

    public static TideRainCisternState RepairCrack(TideRainCisternState state, float repair01)
    {
        state.Crack01 = Mathf.MoveTowards(state.Crack01, 0.08f, Mathf.Clamp01(repair01) * 0.64f);
        return state;
    }
}
