using UnityEngine;

/// <summary>
/// V43 海况层的确定性选帧和物理尺寸规则。
/// </summary>
public static class TideV43SeaWeatherPresentationModel
{
    public const int CatalogVersion = 43;
    public const int WaveFrameCount = 8;
    public const int VortexFrameCount = 12;
    public const float VortexFrameSeconds = 0.11f;

    public static Sprite EvaluateWaveFrame(
        TideV43SeaWeatherCatalog catalog,
        TideV43WaveKind kind,
        float worldTime,
        float phase01,
        float playbackRate)
    {
        if (catalog == null)
        {
            return null;
        }

        float frameSeconds = kind == TideV43WaveKind.LongSwell
            ? 0.17f
            : kind == TideV43WaveKind.WindWave ? 0.125f : 0.095f;
        float cycle = Mathf.Max(0f, worldTime) * Mathf.Max(0.05f, playbackRate) /
            (frameSeconds * WaveFrameCount) + phase01;
        int frame = Mathf.FloorToInt(Mathf.Repeat(cycle, 1f) * WaveFrameCount);
        return catalog.GetWaveFrame(kind, frame);
    }

    public static Sprite EvaluateVortexFrame(
        TideV43SeaWeatherCatalog catalog,
        TideV43VortexLayer layer,
        float worldTime)
    {
        if (catalog == null)
        {
            return null;
        }

        int frame = Mathf.FloorToInt(Mathf.Max(0f, worldTime) / VortexFrameSeconds) %
            VortexFrameCount;
        return catalog.GetVortexFrame(layer, frame);
    }

    public static Vector2 GetWaveWorldSize(TideV43WaveKind kind)
    {
        if (kind == TideV43WaveKind.LongSwell)
        {
            return new Vector2(2.8f, 0.34f);
        }

        return kind == TideV43WaveKind.WindWave
            ? new Vector2(1.8f, 0.56f)
            : new Vector2(2.25f, 0.92f);
    }
}
