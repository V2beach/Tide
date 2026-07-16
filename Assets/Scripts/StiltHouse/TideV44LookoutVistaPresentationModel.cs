using UnityEngine;

/// <summary>
/// V44 共享画布、灯塔锚点与雾光选帧规则。
/// </summary>
public static class TideV44LookoutVistaPresentationModel
{
    public const int CatalogVersion = 44;
    public const int BeamFrameCount = 12;
    public const float PixelsPerUnit = 256f;
    public const float ViewportWorldHeight = 7.6f;
    public const float BeamFrameSeconds = 0.12f;

    public static readonly Vector2Int VistaCanvasPixels = new Vector2Int(4096, 2304);
    public static readonly Vector2Int LighthouseCanvasPixels = new Vector2Int(2048, 2048);
    public static readonly Vector2 LighthouseLensTopLeft = new Vector2(1024f, 303f);
    public static readonly Vector2 LighthouseRockBaselineTopLeft = new Vector2(1024f, 1950f);
    public static readonly Vector2 LighthouseTargetTopLeft = new Vector2(3270f, 1320f);

    public static float VistaUniformScale =>
        ViewportWorldHeight / (VistaCanvasPixels.y / PixelsPerUnit);

    public static Vector2 VistaWorldSize => new Vector2(
        VistaCanvasPixels.x / PixelsPerUnit * VistaUniformScale,
        ViewportWorldHeight);

    public static Sprite EvaluateBeamFrame(TideV44LookoutVistaCatalog catalog, float worldTime)
    {
        if (catalog == null)
        {
            return null;
        }

        int frame = Mathf.FloorToInt(Mathf.Max(0f, worldTime) / BeamFrameSeconds) % BeamFrameCount;
        return catalog.GetLighthouseBeamFrame(frame);
    }

    public static Vector2 VistaTopLeftToWorld(Vector2 topLeftPixels)
    {
        Vector2 centredPixels = new Vector2(
            topLeftPixels.x - VistaCanvasPixels.x * 0.5f,
            VistaCanvasPixels.y * 0.5f - topLeftPixels.y);
        return centredPixels / PixelsPerUnit * VistaUniformScale;
    }

    public static Vector2 LighthouseSourceTopLeftToLocalWorld(Vector2 topLeftPixels, float uniformScale)
    {
        Vector2 centredPixels = new Vector2(
            topLeftPixels.x - LighthouseCanvasPixels.x * 0.5f,
            LighthouseCanvasPixels.y * 0.5f - topLeftPixels.y);
        return centredPixels / PixelsPerUnit * uniformScale;
    }

    public static Vector2 EvaluateLighthouseRootPosition(float targetWaterY, float lighthouseScale)
    {
        Vector2 target = VistaTopLeftToWorld(LighthouseTargetTopLeft);
        target.y = targetWaterY;
        Vector2 localBaseline = LighthouseSourceTopLeftToLocalWorld(
            LighthouseRockBaselineTopLeft,
            lighthouseScale);
        return target - localBaseline;
    }

    public static Vector2 EvaluateLighthouseLensPosition(Vector2 lighthouseRoot, float lighthouseScale)
    {
        return lighthouseRoot + LighthouseSourceTopLeftToLocalWorld(
            LighthouseLensTopLeft,
            lighthouseScale);
    }
}
