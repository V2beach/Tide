using UnityEngine;

/// <summary>
/// 同一时空点的海况采样结果。所有漂浮物、船体、泡沫和人物都应读取这一个结果，
/// 避免海面向上、道具却向下或每个对象各自随机抖动。
/// </summary>
public readonly struct TideOceanSample
{
    public TideOceanSample(float surfaceY, float slope, float horizontalVelocity, float agitation01)
    {
        SurfaceY = surfaceY;
        Slope = slope;
        HorizontalVelocity = horizontalVelocity;
        Agitation01 = agitation01;
    }

    public float SurfaceY { get; }
    public float Slope { get; }
    public float HorizontalVelocity { get; }
    public float Agitation01 { get; }
}

/// <summary>
/// 可复现的二维海况场。潮位提供缓慢的平均水位，长涌、短浪和阵风提供局部变化；
/// Perlin 只改变连续浪群的包络，不直接逐帧写随机数，所以重播和自动探针保持稳定。
/// </summary>
public static class TideOceanFieldModel
{
    private const float SpatialDerivativeStep = 0.035f;

    public static TideOceanSample Sample(
        float meanWaterY,
        float worldX,
        float worldTime,
        float tideStrength01,
        float storm01,
        float wind01)
    {
        float clampedTide = Mathf.Clamp01(tideStrength01);
        float clampedStorm = Mathf.Clamp01(storm01);
        float clampedWind = Mathf.Clamp01(wind01);
        float height = EvaluateWaveHeight(worldX, worldTime, clampedTide, clampedStorm, clampedWind);
        float heightLeft = EvaluateWaveHeight(
            worldX - SpatialDerivativeStep,
            worldTime,
            clampedTide,
            clampedStorm,
            clampedWind);
        float heightRight = EvaluateWaveHeight(
            worldX + SpatialDerivativeStep,
            worldTime,
            clampedTide,
            clampedStorm,
            clampedWind);
        float slope = (heightRight - heightLeft) / (SpatialDerivativeStep * 2f);

        // 潮流方向随涨退潮由调用方额外带符号；这里返回浪群产生的局部水平速度。
        float orbitalVelocity = Mathf.Cos(worldX * 0.58f - worldTime * 0.72f) *
            Mathf.Lerp(0.035f, 0.13f, clampedStorm);
        float gust = (Mathf.PerlinNoise(worldX * 0.045f + 9.7f, worldTime * 0.055f + 3.1f) - 0.5f) *
            Mathf.Lerp(0.025f, 0.22f, Mathf.Max(clampedWind, clampedStorm));
        float horizontalVelocity = orbitalVelocity + gust;
        float agitation01 = Mathf.Clamp01(
            Mathf.Abs(slope) * 1.45f + clampedStorm * 0.62f + clampedWind * 0.22f);
        return new TideOceanSample(meanWaterY + height, slope, horizontalVelocity, agitation01);
    }

    public static bool ProbeDeterministicContinuity(out string reason)
    {
        TideOceanSample a = Sample(-1.2f, 3.4f, 18.75f, 0.66f, 0.42f, 0.38f);
        TideOceanSample repeated = Sample(-1.2f, 3.4f, 18.75f, 0.66f, 0.42f, 0.38f);
        TideOceanSample nearby = Sample(-1.2f, 3.41f, 18.76f, 0.66f, 0.42f, 0.38f);
        bool deterministic = Mathf.Abs(a.SurfaceY - repeated.SurfaceY) < 0.000001f &&
            Mathf.Abs(a.HorizontalVelocity - repeated.HorizontalVelocity) < 0.000001f;
        bool continuous = Mathf.Abs(a.SurfaceY - nearby.SurfaceY) < 0.035f &&
            Mathf.Abs(a.HorizontalVelocity - nearby.HorizontalVelocity) < 0.04f;
        reason = $"deterministic={deterministic}; continuous={continuous}; " +
            $"heightDelta={Mathf.Abs(a.SurfaceY - nearby.SurfaceY):F4}; " +
            $"flowDelta={Mathf.Abs(a.HorizontalVelocity - nearby.HorizontalVelocity):F4}";
        return deterministic && continuous;
    }

    public static bool ProbeSpectralVariation(out string reason)
    {
        const int sampleCount = 72;
        const float sampleStep = 0.25f;
        float calmMin = float.PositiveInfinity;
        float calmMax = float.NegativeInfinity;
        float stormMin = float.PositiveInfinity;
        float stormMax = float.NegativeInfinity;
        for (int i = 0; i < sampleCount; i++)
        {
            float x = i * sampleStep;
            float calm = Sample(0f, x, 37.4f, 0.52f, 0.08f, 0.16f).SurfaceY;
            float storm = Sample(0f, x, 37.4f, 0.92f, 0.9f, 0.82f).SurfaceY;
            calmMin = Mathf.Min(calmMin, calm);
            calmMax = Mathf.Max(calmMax, calm);
            stormMin = Mathf.Min(stormMin, storm);
            stormMax = Mathf.Max(stormMax, storm);
        }

        float calmRange = calmMax - calmMin;
        float stormRange = stormMax - stormMin;
        // 旧三正弦在主涌周期后会几乎复位。新浪场必须仍保留缓慢浪组和交叉浪差异，
        // 但同一时空点重复采样仍完全一致，才能让自动回归和重播稳定。
        float nominalPrimaryPeriod = Mathf.PI * 2f / 0.66f;
        float repeatedCycleDelta = Mathf.Abs(
            Sample(0f, 4.2f, 21.7f, 0.68f, 0.34f, 0.42f).SurfaceY -
            Sample(0f, 4.2f, 21.7f + nominalPrimaryPeriod, 0.68f, 0.34f, 0.42f).SurfaceY);
        bool calmReadable = calmRange >= 0.045f && calmRange <= 0.24f;
        bool stormAmplifies = stormRange >= calmRange * 1.55f && stormRange <= 0.72f;
        bool cadenceDoesNotLoop = repeatedCycleDelta >= 0.008f;
        reason = $"calmRange={calmRange:F3}; stormRange={stormRange:F3}; " +
            $"primaryCycleDelta={repeatedCycleDelta:F3}";
        return calmReadable && stormAmplifies && cadenceDoesNotLoop;
    }

    private static float EvaluateWaveHeight(
        float worldX,
        float worldTime,
        float tideStrength01,
        float storm01,
        float wind01)
    {
        float groupEnvelope = Mathf.Lerp(
            0.56f,
            1.24f,
            Mathf.PerlinNoise(worldX * 0.032f - worldTime * 0.009f + 2.8f, 6.4f));
        float spectralBreath = Mathf.Lerp(
            0.82f,
            1.18f,
            Mathf.PerlinNoise(worldTime * 0.027f + 11.3f, worldX * 0.018f + 1.9f));
        // 月相主要改变平均潮位和流速，只轻微抬升长涌；风和风暴才是浪高主因。
        float swellAmplitude = Mathf.Lerp(0.032f, 0.064f, tideStrength01) *
            Mathf.Lerp(0.92f, 2.35f, storm01) * groupEnvelope * spectralBreath;
        float chopAmplitude = Mathf.Lerp(0.006f, 0.046f, Mathf.Max(wind01, storm01));
        float crossAmplitude = Mathf.Lerp(0.003f, 0.03f, storm01);
        float phaseWander = (Mathf.PerlinNoise(worldTime * 0.018f + 4.2f, 8.7f) - 0.5f) * 0.92f;
        float chopWander = (Mathf.PerlinNoise(worldTime * 0.041f + 17.5f, worldX * 0.025f + 2.4f) - 0.5f) * 0.66f;

        float primarySwell = Mathf.Sin(worldX * 0.53f - worldTime * 0.66f + phaseWander) *
            swellAmplitude;
        float secondarySwell = Mathf.Sin(worldX * 0.74f - worldTime * 0.91f + 2.41f) *
            swellAmplitude * Mathf.Lerp(0.28f, 0.48f, storm01);
        float shortChop = Mathf.Sin(
            worldX * 1.55f - worldTime * Mathf.Lerp(1.58f, 1.94f, wind01) + 1.7f + chopWander) *
            chopAmplitude;
        float capillaryChop = Mathf.Sin(worldX * 2.43f - worldTime * 2.68f + 5.2f) *
            chopAmplitude * Mathf.Lerp(0.18f, 0.36f, wind01);
        float crossSea = Mathf.Sin(worldX * 0.97f + worldTime * 1.08f + 4.3f) * crossAmplitude;
        return primarySwell + secondarySwell + shortChop + capillaryChop + crossSea;
    }
}
