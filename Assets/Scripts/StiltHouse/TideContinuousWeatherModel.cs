using UnityEngine;

/// <summary>
/// Deterministic weather-front calculations for the stilt-house slice.
/// The model owns no scene state: a given world time always produces the same pressure.
/// </summary>
public static class TideContinuousWeatherModel
{
    private const float FrontBuildStart01 = 0.12f;

    public static float EvaluateFrontProgress01(float worldSeconds, float dayLengthSeconds, float arrivalDays)
    {
        float duration = Mathf.Max(1f, dayLengthSeconds) * Mathf.Max(0.25f, arrivalDays);
        return Mathf.Clamp01(Mathf.Max(0f, worldSeconds) / duration);
    }

    public static float EvaluatePressure01(float worldSeconds, float dayLengthSeconds, float arrivalDays)
    {
        float progress01 = EvaluateFrontProgress01(worldSeconds, dayLengthSeconds, arrivalDays);
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FrontBuildStart01, 1f, progress01));
    }

    public static float EvaluateRain01(float pressure01)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.96f, Mathf.Clamp01(pressure01)));
    }

    public static float SecondsUntilNextHighWater(float tideClockSeconds, float tideCycleSeconds)
    {
        float cycle = Mathf.Max(8f, tideCycleSeconds);
        float phaseSeconds = Mathf.Repeat(tideClockSeconds, cycle);
        float highWaterSeconds = cycle * 0.5f;
        return phaseSeconds <= highWaterSeconds
            ? highWaterSeconds - phaseSeconds
            : cycle - phaseSeconds + highWaterSeconds;
    }

    public static float EvaluatePressureAtNextHighWater01(
        float weatherClockSeconds,
        float dayLengthSeconds,
        float arrivalDays,
        float tideClockSeconds,
        float tideCycleSeconds)
    {
        float leadSeconds = SecondsUntilNextHighWater(tideClockSeconds, tideCycleSeconds);
        return EvaluatePressure01(weatherClockSeconds + leadSeconds, dayLengthSeconds, arrivalDays);
    }

    public static float EvaluateStormOnshoreWind01(float pressure01)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 0.92f, Mathf.Clamp01(pressure01)));
    }

    public static float EvaluateWaveLoadMultiplier(float pressure01)
    {
        float stormWave01 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 1f, Mathf.Clamp01(pressure01)));
        return Mathf.Lerp(1f, 1.42f, stormWave01);
    }

    public static float EvaluateRainLeanDegrees(float signedWindSpeed, float pressure01)
    {
        float rain01 = EvaluateRain01(pressure01);
        if (rain01 <= 0.001f || Mathf.Abs(signedWindSpeed) <= 0.001f)
        {
            return 0f;
        }

        float wind01 = Mathf.Clamp01(Mathf.Abs(signedWindSpeed) / 0.75f);
        float lean = Mathf.Lerp(5f, 21f, rain01) * Mathf.Lerp(0.42f, 1f, wind01);
        return Mathf.Sign(signedWindSpeed) * lean;
    }
}
