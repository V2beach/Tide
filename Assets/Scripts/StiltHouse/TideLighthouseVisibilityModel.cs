using UnityEngine;

public enum TideLighthouseVisibilityState
{
    Unknown,
    Bearing,
    Known
}

public readonly struct TideLighthouseVisibilitySample
{
    public TideLighthouseVisibilitySample(
        TideLighthouseVisibilityState state,
        float lighthouseAlpha,
        float fogAlpha,
        float beamAlpha)
    {
        State = state;
        LighthouseAlpha = Mathf.Clamp01(lighthouseAlpha);
        FogAlpha = Mathf.Clamp01(fogAlpha);
        BeamAlpha = Mathf.Clamp01(beamAlpha);
    }

    public TideLighthouseVisibilityState State { get; }
    public float LighthouseAlpha { get; }
    public float FogAlpha { get; }
    public float BeamAlpha { get; }
    public bool ShowsLighthouse => LighthouseAlpha > 0.001f;
    public bool ShowsBeam => BeamAlpha > 0.001f;
}

/// <summary>
/// 把既有航线线索和连续世界状态投影为灯塔能见度。
/// 这里不推进任务、不改变天气，也不知道任何 Renderer；瞭望与短航必须消费同一结果。
/// </summary>
public static class TideLighthouseVisibilityModel
{
    public static TideLighthouseVisibilityState EvaluateState(
        int lighthouseClues,
        int routeClueReturnRound,
        int tideRound,
        bool finalDeparture)
    {
        if (finalDeparture ||
            (lighthouseClues > 0 && routeClueReturnRound >= 0 && tideRound > routeClueReturnRound))
        {
            return TideLighthouseVisibilityState.Known;
        }

        return lighthouseClues > 0 && routeClueReturnRound >= 0
            ? TideLighthouseVisibilityState.Bearing
            : TideLighthouseVisibilityState.Unknown;
    }

    public static TideLighthouseVisibilitySample Evaluate(
        int lighthouseClues,
        int routeClueReturnRound,
        int tideRound,
        bool finalDeparture,
        float daylight01,
        float storm01)
    {
        TideLighthouseVisibilityState state = EvaluateState(
            lighthouseClues,
            routeClueReturnRound,
            tideRound,
            finalDeparture);
        float daylight = Mathf.Clamp01(daylight01);
        float storm = Mathf.Clamp01(storm01);
        float night = 1f - daylight;

        if (state == TideLighthouseVisibilityState.Unknown)
        {
            return new TideLighthouseVisibilitySample(state, 0f, 0.72f + storm * 0.1f, 0f);
        }

        if (state == TideLighthouseVisibilityState.Bearing)
        {
            // 方位纸只让玩家知道“雾后可能有竖直建筑”，不提供光束或精确世界标记。
            float silhouette = Mathf.Lerp(0.2f, 0.26f, night) * Mathf.Lerp(1f, 0.82f, storm);
            return new TideLighthouseVisibilitySample(state, silhouette, 0.58f + storm * 0.08f, 0f);
        }

        float lighthouseAlpha = Mathf.Lerp(0.94f, 0.72f, storm);
        float moonBeam = night * Mathf.Lerp(0.08f, 0.28f, night);
        float stormBeam = storm * 0.12f;
        float beamAlpha = Mathf.Max(moonBeam, stormBeam) * Mathf.Lerp(1f, 0.72f, storm);
        return new TideLighthouseVisibilitySample(
            state,
            lighthouseAlpha,
            Mathf.Lerp(0.28f, 0.52f, storm),
            beamAlpha);
    }
}
