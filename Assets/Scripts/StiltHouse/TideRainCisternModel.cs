using UnityEngine;

[System.Serializable]
public struct TideRainCisternState
{
    public float StoredLiters;
    public float SaltFraction01;
    public float Crack01;
    public float HighestSaltLine01;
}

[System.Serializable]
public struct TidePortableWaterState
{
    public float Liters;
    public float SaltFraction01;
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
    public const float SeaWaterSalinity01 = 0.035f;
    public const float CleanRainTankSalinity01 = 0.00035f;
    public const float MaximumPotableSalinity01 = 0.0012f;

    public static TideRainCisternState CreateDamaged(float initialFreshLiters = 24f)
    {
        return new TideRainCisternState
        {
            StoredLiters = Mathf.Clamp(initialFreshLiters, 0f, CapacityLiters),
            // 0.035 means 3.5% seawater salinity. The old value 0.04 therefore made
            // the supposedly fresh starting tank saltier than the surrounding sea.
            SaltFraction01 = CleanRainTankSalinity01,
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
        float previousLiters = Mathf.Clamp(state.StoredLiters, 0f, CapacityLiters);
        float previousSalinity01 = Mathf.Clamp(state.SaltFraction01, 0f, SeaWaterSalinity01);
        float evaporationLiters = DryWeatherEvaporationLitersPerDay * dt / 86400f;
        float leakLiters = previousLiters * Mathf.Clamp01(state.Crack01) * 0.00018f * dt;

        // Evaporation removes water but leaves dissolved salt behind. Leakage removes
        // the same mixture that is in the tank. Rain adds water with negligible salt.
        float saltEquivalentLiters = previousLiters * previousSalinity01;
        saltEquivalentLiters = Mathf.Max(
            0f,
            saltEquivalentLiters - Mathf.Min(previousLiters, leakLiters) * previousSalinity01);
        float afterWeatherUnclamped = Mathf.Max(
            0f,
            previousLiters + collectedLiters - evaporationLiters - leakLiters);
        float freshAfterWeather = Mathf.Min(CapacityLiters, afterWeatherUnclamped);
        if (afterWeatherUnclamped > CapacityLiters && afterWeatherUnclamped > 0.001f)
        {
            // Overflow carries away the fully mixed contents rather than deleting only
            // water and leaving an impossible salt concentrate behind.
            saltEquivalentLiters *= CapacityLiters / afterWeatherUnclamped;
        }

        float overtop01 = Mathf.Clamp01(stormOvertopping01);
        float intrudingSeaWaterLiters = CapacityLiters * 0.0024f * overtop01 * dt;
        float mixedUnclamped = freshAfterWeather + intrudingSeaWaterLiters;
        float mixedSaltEquivalentLiters = saltEquivalentLiters +
            intrudingSeaWaterLiters * SeaWaterSalinity01;
        float mixedLiters = Mathf.Min(CapacityLiters, mixedUnclamped);
        if (mixedUnclamped > CapacityLiters && mixedUnclamped > 0.001f)
        {
            mixedSaltEquivalentLiters *= CapacityLiters / mixedUnclamped;
        }

        state.StoredLiters = mixedLiters;
        state.SaltFraction01 = mixedLiters > 0.001f
            ? Mathf.Clamp(mixedSaltEquivalentLiters / mixedLiters, 0f, SeaWaterSalinity01)
            : 0f;
        state.HighestSaltLine01 = Mathf.Max(
            state.HighestSaltLine01,
            Mathf.Clamp01(mixedLiters / CapacityLiters) * overtop01);
        return state;
    }

    public static float GetDrinkableLiters(TideRainCisternState state)
    {
        // A mixed tank has one salinity. It cannot contain a fictional drinkable
        // fraction that may be withdrawn separately from the contaminated remainder.
        return state.SaltFraction01 <= MaximumPotableSalinity01
            ? Mathf.Max(0f, state.StoredLiters)
            : 0f;
    }

    public static float GetSaltContamination01(TideRainCisternState state)
    {
        return Mathf.InverseLerp(
            CleanRainTankSalinity01,
            SeaWaterSalinity01,
            Mathf.Max(0f, state.SaltFraction01));
    }

    public static TideRainCisternState WithdrawPotableWater(
        TideRainCisternState state,
        float requestedLiters,
        bool requireFullAmount,
        out TidePortableWaterState withdrawn)
    {
        withdrawn = default;
        float request = Mathf.Max(0f, requestedLiters);
        float available = GetDrinkableLiters(state);
        if (request <= 0f || available <= 0f || (requireFullAmount && available + 0.0001f < request))
        {
            return state;
        }

        float amount = Mathf.Min(request, available);
        withdrawn = new TidePortableWaterState
        {
            Liters = amount,
            SaltFraction01 = state.SaltFraction01
        };
        state.StoredLiters = Mathf.Max(0f, state.StoredLiters - amount);
        if (state.StoredLiters <= 0.001f)
        {
            state.StoredLiters = 0f;
            state.SaltFraction01 = 0f;
        }
        return state;
    }

    public static TidePortableWaterState ConsumePortableWater(
        TidePortableWaterState state,
        float requestedLiters,
        out float consumedLiters)
    {
        consumedLiters = Mathf.Min(Mathf.Max(0f, requestedLiters), Mathf.Max(0f, state.Liters));
        state.Liters = Mathf.Max(0f, state.Liters - consumedLiters);
        if (state.Liters <= 0.001f)
        {
            state = default;
        }
        return state;
    }

    public static TideRainCisternState RepairCrack(TideRainCisternState state, float repair01)
    {
        state.Crack01 = Mathf.MoveTowards(state.Crack01, 0.08f, Mathf.Clamp01(repair01) * 0.64f);
        return state;
    }
}
