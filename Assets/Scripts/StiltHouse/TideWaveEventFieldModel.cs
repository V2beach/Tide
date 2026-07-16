using UnityEngine;

/// <summary>
/// A wave event is a short-lived visible crest or breaker layered over the continuous
/// ocean body. It never owns mean water level, collision, buoyancy or tide timing.
/// </summary>
public enum TideWaveEventKind
{
    LongSwell,
    WindWave,
    StormBreaker
}

/// <summary>
/// Deterministic presentation data for one world-space wave event.
/// </summary>
public readonly struct TideWaveEventSample
{
    public TideWaveEventSample(
        bool visible,
        int cellIndex,
        int cycleIndex,
        float cycleDurationSeconds,
        float worldX,
        float life01,
        float opacity01,
        float widthScale,
        float heightScale,
        float framePhase01,
        float frameSpeedScale,
        TideWaveEventKind kind)
    {
        Visible = visible;
        CellIndex = cellIndex;
        CycleIndex = cycleIndex;
        CycleDurationSeconds = cycleDurationSeconds;
        WorldX = worldX;
        Life01 = life01;
        Opacity01 = opacity01;
        WidthScale = widthScale;
        HeightScale = heightScale;
        FramePhase01 = framePhase01;
        FrameSpeedScale = frameSpeedScale;
        Kind = kind;
    }

    public bool Visible { get; }
    public int CellIndex { get; }
    public int CycleIndex { get; }
    public float CycleDurationSeconds { get; }
    public float WorldX { get; }
    public float Life01 { get; }
    public float Opacity01 { get; }
    public float WidthScale { get; }
    public float HeightScale { get; }
    public float FramePhase01 { get; }
    public float FrameSpeedScale { get; }
    public TideWaveEventKind Kind { get; }
}

/// <summary>
/// World-space field for intermittent visible wave events.
///
/// The continuous sea and all physical objects still sample <see cref="TideOceanFieldModel"/>.
/// This model only decides where a transparent V43 crest forms and fades. Events are
/// keyed by world cell and cycle rather than renderer index or camera position, so a
/// camera pan cannot reroll the water. Time is raw real seconds; the compressed game
/// day and macro tide clocks are deliberately not inputs.
/// </summary>
public static class TideWaveEventFieldModel
{
    public const float CellWidthMeters = 3.2f;
    public const float MinimumCycleSeconds = 8.2f;
    public const float MaximumCycleSeconds = 14.8f;

    public static TideWaveEventSample Sample(
        int slotIndex,
        int slotCount,
        float viewCenterWorldX,
        float worldTimeSeconds,
        float travelDirection,
        float wind01,
        float storm01,
        float agitation01)
    {
        int safeSlotCount = Mathf.Max(1, slotCount);
        int clampedSlot = Mathf.Clamp(slotIndex, 0, safeSlotCount - 1);
        int centerCell = Mathf.FloorToInt(viewCenterWorldX / CellWidthMeters);
        int firstCell = centerCell - safeSlotCount / 2;
        int cellIndex = firstCell + clampedSlot;

        float cellClockOffset01 = Hash01(cellIndex, 0, 41);
        float cycleDuration = Mathf.Lerp(
            MinimumCycleSeconds,
            MaximumCycleSeconds,
            Hash01(cellIndex, 0, 73));
        float shiftedCycles = Mathf.Max(0f, worldTimeSeconds) / cycleDuration + cellClockOffset01;
        int cycleIndex = Mathf.FloorToInt(shiftedCycles);
        float cycle01 = Mathf.Repeat(shiftedCycles, 1f);

        // Each cycle spends part of its time with no visible local breaker. The base
        // V56 sea remains present, so calm water is allowed to be visually quiet.
        float activeFraction01 = Mathf.Lerp(
            0.46f,
            0.74f,
            Hash01(cellIndex, cycleIndex, 109));
        float life01 = cycle01 / Mathf.Max(0.01f, activeFraction01);
        bool insideLifetime = life01 < 1f;
        life01 = Mathf.Clamp01(life01);
        float envelope01 = insideLifetime
            ? Mathf.Pow(Mathf.Sin(life01 * Mathf.PI), 1.35f)
            : 0f;

        float clampedWind = Mathf.Clamp01(wind01);
        float clampedStorm = Mathf.Clamp01(storm01);
        float clampedAgitation = Mathf.Clamp01(agitation01);
        float seaEnergy01 = Mathf.Clamp01(
            0.12f +
            clampedWind * 0.42f +
            clampedStorm * 0.76f +
            clampedAgitation * 0.24f);
        float eventThreshold01 = Mathf.Lerp(
            0.08f,
            0.84f,
            Hash01(cellIndex, cycleIndex, 151));
        float presence01 = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(eventThreshold01 - 0.16f, eventThreshold01 + 0.12f, seaEnergy01));
        float opacity01 = envelope01 * presence01 * Mathf.Lerp(0.42f, 1f, seaEnergy01);

        float cellCenterX = (cellIndex + 0.5f) * CellWidthMeters;
        float formationOffsetX = Mathf.Lerp(
            -CellWidthMeters * 0.22f,
            CellWidthMeters * 0.22f,
            Hash01(cellIndex, cycleIndex, 193));
        float driftDistance = Mathf.Lerp(
            0.34f,
            1.08f,
            Hash01(cellIndex, cycleIndex, 227));
        float signedDirection = Mathf.Abs(travelDirection) <= 0.01f
            ? 1f
            : Mathf.Sign(travelDirection);
        float easedTravel01 = Mathf.SmoothStep(0f, 1f, life01);
        float worldX = cellCenterX + formationOffsetX +
            signedDirection * (easedTravel01 - 0.5f) * driftDistance;

        float shapeVariation01 = Hash01(cellIndex, cycleIndex, 269);
        float widthScale = Mathf.Lerp(0.78f, 1.18f, shapeVariation01) *
            Mathf.Lerp(0.92f, 1.1f, seaEnergy01);
        float heightScale = Mathf.Lerp(
            0.74f,
            1.15f,
            Hash01(cellIndex, cycleIndex, 307)) *
            Mathf.Lerp(0.88f, 1.16f, seaEnergy01);
        float kindRoll01 = Hash01(cellIndex, cycleIndex, 347);
        TideWaveEventKind kind = ResolveKind(clampedWind, clampedStorm, seaEnergy01, kindRoll01);

        return new TideWaveEventSample(
            insideLifetime && opacity01 > 0.025f,
            cellIndex,
            cycleIndex,
            cycleDuration,
            worldX,
            life01,
            opacity01,
            widthScale,
            heightScale,
            Hash01(cellIndex, cycleIndex, 389),
            Mathf.Lerp(0.82f, 1.16f, Hash01(cellIndex, cycleIndex, 419)),
            kind);
    }

    public static bool ProbeNaturalCadence(out string reason)
    {
        const int slotCount = 5;
        TideWaveEventSample deterministicA = Sample(2, slotCount, 0.2f, 37.25f, 1f, 0.4f, 0.2f, 0.32f);
        TideWaveEventSample deterministicB = Sample(2, slotCount, 0.2f, 37.25f, 1f, 0.4f, 0.2f, 0.32f);
        bool deterministic = SameEvent(deterministicA, deterministicB);

        // Cell zero is slot 2 while the camera is in cell zero, then slot 1 after the
        // camera crosses into cell one. Its event must remain bit-for-bit identical.
        TideWaveEventSample beforePan = Sample(2, slotCount, 0.2f, 37.25f, 1f, 0.4f, 0.2f, 0.32f);
        TideWaveEventSample afterPan = Sample(1, slotCount, 3.3f, 37.25f, 1f, 0.4f, 0.2f, 0.32f);
        bool cameraStable = beforePan.CellIndex == 0 && afterPan.CellIndex == 0 &&
            SameEvent(beforePan, afterPan);

        float minimumPeriod = float.PositiveInfinity;
        float maximumPeriod = float.NegativeInfinity;
        float calmWeight = 0f;
        float stormWeight = 0f;
        float maximumOpacityStep = 0f;
        float maximumCellExcursion = 0f;
        const int timeSamples = 720;
        const float timeStep = 1f / 12f;
        TideWaveEventSample[] previous = new TideWaveEventSample[slotCount];
        bool[] hasPrevious = new bool[slotCount];

        for (int sampleIndex = 0; sampleIndex < timeSamples; sampleIndex++)
        {
            float time = sampleIndex * timeStep;
            for (int slot = 0; slot < slotCount; slot++)
            {
                TideWaveEventSample calm = Sample(slot, slotCount, 0.2f, time, 1f, 0.08f, 0.03f, 0.1f);
                TideWaveEventSample storm = Sample(slot, slotCount, 0.2f, time, 1f, 0.9f, 0.92f, 0.88f);
                calmWeight += calm.Opacity01;
                stormWeight += storm.Opacity01;
                minimumPeriod = Mathf.Min(minimumPeriod, calm.CycleDurationSeconds);
                maximumPeriod = Mathf.Max(maximumPeriod, calm.CycleDurationSeconds);

                float cellCenterX = (calm.CellIndex + 0.5f) * CellWidthMeters;
                maximumCellExcursion = Mathf.Max(
                    maximumCellExcursion,
                    Mathf.Abs(calm.WorldX - cellCenterX));
                if (hasPrevious[slot])
                {
                    maximumOpacityStep = Mathf.Max(
                        maximumOpacityStep,
                        Mathf.Abs(calm.Opacity01 - previous[slot].Opacity01));
                }

                previous[slot] = calm;
                hasPrevious[slot] = true;
            }
        }

        float divisor = timeSamples;
        float calmAverageVisibleWeight = calmWeight / divisor;
        float stormAverageVisibleWeight = stormWeight / divisor;
        bool cadenceRealistic = minimumPeriod >= MinimumCycleSeconds - 0.001f &&
            maximumPeriod <= MaximumCycleSeconds + 0.001f;
        bool weatherSeparatesDensity = calmAverageVisibleWeight >= 0.08f &&
            calmAverageVisibleWeight <= 1.1f &&
            stormAverageVisibleWeight >= 1.65f &&
            stormAverageVisibleWeight >= calmAverageVisibleWeight * 2.2f;
        bool fadesContinuously = maximumOpacityStep <= 0.075f;
        bool staysLocal = maximumCellExcursion <= CellWidthMeters * 0.42f;

        reason = $"确定={deterministic}；跨镜头同浪={cameraStable}；" +
            $"周期={minimumPeriod:F1}-{maximumPeriod:F1}s；" +
            $"平静/暴潮活跃权重={calmAverageVisibleWeight:F2}/{stormAverageVisibleWeight:F2}；" +
            $"最大透明步进={maximumOpacityStep:F3}；局部漂移={maximumCellExcursion:F2}m";
        return deterministic && cameraStable && cadenceRealistic &&
            weatherSeparatesDensity && fadesContinuously && staysLocal;
    }

    private static TideWaveEventKind ResolveKind(
        float wind01,
        float storm01,
        float seaEnergy01,
        float roll01)
    {
        if (storm01 >= 0.56f && roll01 <= Mathf.Lerp(0.35f, 0.9f, storm01))
        {
            return TideWaveEventKind.StormBreaker;
        }

        if (wind01 >= 0.28f || seaEnergy01 >= 0.46f)
        {
            return TideWaveEventKind.WindWave;
        }

        return TideWaveEventKind.LongSwell;
    }

    private static bool SameEvent(TideWaveEventSample a, TideWaveEventSample b)
    {
        return a.Visible == b.Visible &&
            a.CellIndex == b.CellIndex &&
            a.CycleIndex == b.CycleIndex &&
            a.Kind == b.Kind &&
            Mathf.Abs(a.CycleDurationSeconds - b.CycleDurationSeconds) <= 0.000001f &&
            Mathf.Abs(a.WorldX - b.WorldX) <= 0.000001f &&
            Mathf.Abs(a.Opacity01 - b.Opacity01) <= 0.000001f &&
            Mathf.Abs(a.WidthScale - b.WidthScale) <= 0.000001f &&
            Mathf.Abs(a.HeightScale - b.HeightScale) <= 0.000001f;
    }

    private static float Hash01(int cellIndex, int cycleIndex, int salt)
    {
        // Integer hashing is deterministic across platforms and does not consume
        // UnityEngine.Random state. The final 24 bits map exactly into float precision.
        unchecked
        {
            uint value = (uint)cellIndex * 0x9E3779B9u;
            value ^= (uint)cycleIndex * 0x85EBCA6Bu;
            value ^= (uint)salt * 0xC2B2AE35u;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 16777215f;
        }
    }
}
