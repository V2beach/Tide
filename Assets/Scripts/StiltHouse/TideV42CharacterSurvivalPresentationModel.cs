using System;
using UnityEngine;

/// <summary>
/// V42 生存动作的确定性选帧和表面锚点规则。
///
/// 四组动作沿用 V41 的人物身份和身体尺度。Drown 使用画布中心 Pivot，
/// 其余动作使用脚/床接触 Pivot；运行时必须按动作语义对齐海面或承重面，
/// 不能再以逐帧透明包围盒猜人物大小。
/// </summary>
public static class TideV42CharacterSurvivalPresentationModel
{
    public const int CatalogVersion = 42;
    public const int ColdShiverFrameCount = 6;
    public const int SleepFrameCount = 8;
    public const int DrownFrameCount = 8;
    public const int ColdCollapseFrameCount = 8;
    public const int TotalFrameCount = 30;

    public const float PixelsPerUnit = 512f;
    public const float CanvasPixels = 1024f;
    public const float SurfacePivotNormalizedY = 0.0625f;
    public const float DrownPivotNormalizedY = 0.5f;
    public const float FootOrBedContactTopLeftY = 930f;
    public const float DrownMaxDepthWorld = 0.72f;
    public const float CollapseMaxForwardWorld = 0.18f;

    public static float UniformScale => TideV41CharacterContactPresentationModel.UniformScale;

    private static readonly float[] FrameDurationSeconds =
    {
        0.15f,
        0.18f,
        0.13f,
        0.14f,
    };

    private static readonly float[] DrownWaterlineTopLeftY =
    {
        317f, 275f, 315f, 317f, 324f, 315f, 307f, 307f,
    };

    private static readonly float[] DrownDepth01 =
    {
        0f, 0.05f, 0.12f, 0.22f, 0.36f, 0.52f, 0.72f, 1f,
    };

    private static readonly float[] ColdCollapseForward01 =
    {
        0f, 0.04f, 0.1f, 0.18f, 0.28f, 0.42f, 0.58f, 0.72f,
    };

    private static readonly float[] SleepBedEntry01 =
    {
        0f, 0.1f, 0.24f, 0.46f, 0.72f, 0.88f, 0.96f, 1f,
    };

    public static float SurfacePivotCorrectionWorldY
    {
        get
        {
            float pivotFromBottomPixels = CanvasPixels * SurfacePivotNormalizedY;
            float contactFromBottomPixels = CanvasPixels - FootOrBedContactTopLeftY;
            return -(contactFromBottomPixels - pivotFromBottomPixels) /
                PixelsPerUnit * UniformScale;
        }
    }

    public static int GetFrameCount(TideV42CharacterSurvivalAction action)
    {
        switch (action)
        {
            case TideV42CharacterSurvivalAction.ColdShiver:
                return ColdShiverFrameCount;
            case TideV42CharacterSurvivalAction.Sleep:
                return SleepFrameCount;
            case TideV42CharacterSurvivalAction.Drown:
                return DrownFrameCount;
            case TideV42CharacterSurvivalAction.ColdCollapse:
                return ColdCollapseFrameCount;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    public static float GetFrameDurationSeconds(TideV42CharacterSurvivalAction action)
    {
        return FrameDurationSeconds[(int)action];
    }

    public static float GetOneShotDurationSeconds(TideV42CharacterSurvivalAction action)
    {
        return GetFrameCount(action) * GetFrameDurationSeconds(action);
    }

    public static int EvaluateFrameIndex(
        TideV42CharacterSurvivalAction action,
        float progress01)
    {
        int count = GetFrameCount(action);
        return Mathf.Min(count - 1, Mathf.FloorToInt(Mathf.Clamp01(progress01) * count));
    }

    public static Sprite EvaluateLoopFrame(
        TideV42CharacterSurvivalCatalog catalog,
        TideV42CharacterSurvivalAction action,
        float elapsedSeconds)
    {
        if (catalog == null)
        {
            return null;
        }

        int count = GetFrameCount(action);
        int index = Mathf.FloorToInt(
            Mathf.Max(0f, elapsedSeconds) /
            Mathf.Max(0.01f, GetFrameDurationSeconds(action))) % count;
        return catalog.GetFrame(action, index);
    }

    public static Sprite EvaluateOneShotFrame(
        TideV42CharacterSurvivalCatalog catalog,
        TideV42CharacterSurvivalAction action,
        float progress01)
    {
        return catalog == null
            ? null
            : catalog.GetFrame(action, EvaluateFrameIndex(action, progress01));
    }

    public static float EvaluateSleepBedEntry01(float progress01)
    {
        return EvaluateCurve(SleepBedEntry01, progress01);
    }

    public static float EvaluateCollapseForwardWorld(float progress01, int facing)
    {
        float direction = facing < 0 ? -1f : 1f;
        return direction * EvaluateCurve(ColdCollapseForward01, progress01) *
            CollapseMaxForwardWorld;
    }

    /// <summary>
    /// 返回 Drown Sprite 的世界 Pivot。每帧水线锚点略有不同，先把声明锚点
    /// 精确放到当地海面，再叠加连续下沉深度，避免人物在换帧时上下跳动。
    /// </summary>
    public static Vector2 EvaluateDrownPivotWorld(
        Vector2 waterlinePoint,
        float progress01)
    {
        int frameIndex = EvaluateFrameIndex(
            TideV42CharacterSurvivalAction.Drown,
            progress01);
        float anchorAbovePivotWorld =
            (CanvasPixels * DrownPivotNormalizedY - DrownWaterlineTopLeftY[frameIndex]) /
            PixelsPerUnit * UniformScale;
        float depthWorld = EvaluateCurve(DrownDepth01, progress01) * DrownMaxDepthWorld;
        return new Vector2(
            waterlinePoint.x,
            waterlinePoint.y - anchorAbovePivotWorld - depthWorld);
    }

    private static float EvaluateCurve(float[] values, float progress01)
    {
        if (values == null || values.Length == 0)
        {
            return 0f;
        }

        float sample = Mathf.Clamp01(progress01) * (values.Length - 1);
        int from = Mathf.FloorToInt(sample);
        int to = Mathf.Min(values.Length - 1, from + 1);
        return Mathf.Lerp(values[from], values[to], sample - from);
    }
}
