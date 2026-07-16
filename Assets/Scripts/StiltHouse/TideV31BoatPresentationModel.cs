using System;
using UnityEngine;

/// <summary>
/// V31 分层帆船的纯表现计算。
///
/// 这里不持有 Transform 或 SpriteRenderer，也不按层改世界尺寸；调用方只需给 BoatRoot
/// 一次等比缩放，再使用契约 offset 计算各裁切层的位置即可。
/// </summary>
public static class TideV31BoatPresentationModel
{
    public const int CatalogVersion = 31;
    public const int FoundLayerCount = 2;
    public const int RepairedLayerCount = 5;
    public const int PassengerFrameCount = 6;
    public const int WaterlineMaskCount = 3;
    // 16 张 V31 裁切/乘员/遮罩，加上 V21 Found/Repaired 两张无洞完整底图。
    public const int ExpectedSpriteCount = 19;
    public const float PassengerFrameSeconds = 0.12f;

    /// <summary>
    /// V31 原图按 512 PPU 制作，首切片场景默认把整个 BoatRoot 等比缩到约 42%。
    /// 需要调尺寸时只覆盖这一处 uniformScale，不要逐层调用 SetWorldSize。
    /// </summary>
    public const float BoatRootScale = 0.42f;

    public static Vector3 EvaluateBoatRootLocalScale(float uniformScale = BoatRootScale)
    {
        return Vector3.one * uniformScale;
    }

    /// <summary>
    /// 所有裁切层都遵守同一公式：Root + 契约偏移 * BoatRoot 等比缩放。
    /// </summary>
    public static Vector2 EvaluateLayerWorldPosition(
        Vector2 boatRootWorldPosition,
        Vector2 worldOffsetFromBoatPivot,
        float uniformScale = BoatRootScale)
    {
        return boatRootWorldPosition + worldOffsetFromBoatPivot * uniformScale;
    }

    /// <summary>
    /// 将资源局部点按船的完整姿态变换到世界空间。镜像发生在船的局部空间，
    /// 随后再应用滚转；船图、分层组件、乘员和交互锚点必须共用此顺序。
    /// </summary>
    public static Vector2 EvaluatePoseWorldPosition(
        Vector2 boatRootWorldPosition,
        Vector2 worldOffsetFromBoatPivot,
        float rotationZ,
        bool flipX,
        float uniformScale = BoatRootScale)
    {
        Vector2 posedOffset = worldOffsetFromBoatPivot * uniformScale;
        if (flipX)
        {
            posedOffset.x = -posedOffset.x;
        }

        float radians = rotationZ * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        Vector2 rotatedOffset = new Vector2(
            posedOffset.x * cos - posedOffset.y * sin,
            posedOffset.x * sin + posedOffset.y * cos);
        return boatRootWorldPosition + rotatedOffset;
    }

    public static Vector2 PixelTopLeftToBoatOffset(
        Vector2 pixelTopLeft,
        Vector2Int canvasSize,
        Vector2 pivotNormalized,
        float pixelsPerUnit)
    {
        if (pixelsPerUnit <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelsPerUnit), "PPU 必须大于 0。");
        }

        // 契约像素以左上为原点，Unity pivot 的归一化 Y 以底边为原点，转换时需翻转 Y。
        return new Vector2(
            (pixelTopLeft.x - canvasSize.x * pivotNormalized.x) / pixelsPerUnit,
            (canvasSize.y - pixelTopLeft.y - canvasSize.y * pivotNormalized.y) / pixelsPerUnit);
    }

    public static Vector2 EvaluateAnchorWorldPosition(
        Vector2 boatRootWorldPosition,
        Vector2Int anchorTopLeft,
        TideV31BoatRuntimeCatalog catalog,
        float uniformScale = BoatRootScale)
    {
        RequireCatalog(catalog);
        Vector2 anchorOffset = PixelTopLeftToBoatOffset(
            anchorTopLeft,
            catalog.CanvasSize,
            catalog.PivotNormalized,
            catalog.PixelsPerUnit);
        return EvaluateLayerWorldPosition(boatRootWorldPosition, anchorOffset, uniformScale);
    }

    public static Vector2 EvaluateAnchorPoseWorldPosition(
        Vector2 boatRootWorldPosition,
        Vector2Int anchorTopLeft,
        TideV31BoatRuntimeCatalog catalog,
        float rotationZ,
        bool flipX,
        float uniformScale = BoatRootScale)
    {
        RequireCatalog(catalog);
        Vector2 anchorOffset = PixelTopLeftToBoatOffset(
            anchorTopLeft,
            catalog.CanvasSize,
            catalog.PivotNormalized,
            catalog.PixelsPerUnit);
        return EvaluatePoseWorldPosition(
            boatRootWorldPosition,
            anchorOffset,
            rotationZ,
            flipX,
            uniformScale);
    }

    /// <summary>
    /// 将选定契约水线贴到场景实际水位。先算水线相对船枢轴的局部高度，再反推 RootY。
    /// </summary>
    public static float EvaluateBoatRootY(
        float actualWaterY,
        int waterlineYTopLeft,
        TideV31BoatRuntimeCatalog catalog,
        float uniformScale = BoatRootScale)
    {
        RequireCatalog(catalog);
        float waterlineOffsetY = PixelTopLeftToBoatOffset(
            new Vector2(0f, waterlineYTopLeft),
            catalog.CanvasSize,
            catalog.PivotNormalized,
            catalog.PixelsPerUnit).y;
        return actualWaterY - waterlineOffsetY * uniformScale;
    }

    public static float EvaluateBoatRootY(
        float actualWaterY,
        TideV31BoatRuntimeCatalog catalog,
        bool isLoaded,
        bool isStorm,
        float uniformScale = BoatRootScale)
    {
        TideV31BoatRuntimeCatalog.WaterlineMaskEntry mask =
            SelectWaterlineMask(catalog, isLoaded, isStorm);
        if (mask == null)
        {
            throw new InvalidOperationException("V31 Catalog 缺少当前状态对应的水线遮罩。");
        }

        return EvaluateBoatRootY(actualWaterY, mask.WaterlineYTopLeft, catalog, uniformScale);
    }

    public static int EvaluatePassengerFrame(float worldTime)
    {
        int absoluteFrame = Mathf.FloorToInt(
            Mathf.Max(0f, worldTime) / PassengerFrameSeconds);
        return absoluteFrame % PassengerFrameCount;
    }

    public static TideV31BoatWaterlineMode SelectWaterlineMode(bool isLoaded, bool isStorm)
    {
        // 风暴吃水表现优先；无风暴时才根据是否载货在 Calm 与 Loaded 间选择。
        if (isStorm)
        {
            return TideV31BoatWaterlineMode.Storm;
        }

        return isLoaded
            ? TideV31BoatWaterlineMode.Loaded
            : TideV31BoatWaterlineMode.Calm;
    }

    public static TideV31BoatRuntimeCatalog.WaterlineMaskEntry SelectWaterlineMask(
        TideV31BoatRuntimeCatalog catalog,
        bool isLoaded,
        bool isStorm)
    {
        RequireCatalog(catalog);
        return catalog.GetWaterlineMask(SelectWaterlineMode(isLoaded, isStorm));
    }

    /// <summary>
    /// 纯探针：检查 BoatRoot 三轴是否都使用同一个约定缩放，不读取或修改场景对象。
    /// </summary>
    public static bool ProbeUniformRootScale(
        Vector3 actualRootScale,
        float expectedUniformScale = BoatRootScale,
        float tolerance = 0.0001f)
    {
        float safeTolerance = Mathf.Max(0f, tolerance);
        return Mathf.Abs(actualRootScale.x - expectedUniformScale) <= safeTolerance &&
            Mathf.Abs(actualRootScale.y - expectedUniformScale) <= safeTolerance &&
            Mathf.Abs(actualRootScale.z - expectedUniformScale) <= safeTolerance;
    }

    /// <summary>
    /// 纯探针：验证某层是否严格落在 Root + offset * uniformScale 的契约位置。
    /// </summary>
    public static bool ProbeLayerWorldPosition(
        Vector2 boatRootWorldPosition,
        Vector2 worldOffsetFromBoatPivot,
        Vector2 actualLayerWorldPosition,
        float uniformScale = BoatRootScale,
        float tolerance = 0.001f)
    {
        Vector2 expectedPosition = EvaluateLayerWorldPosition(
            boatRootWorldPosition,
            worldOffsetFromBoatPivot,
            uniformScale);
        float safeTolerance = Mathf.Max(0f, tolerance);
        return (actualLayerWorldPosition - expectedPosition).sqrMagnitude <=
            safeTolerance * safeTolerance;
    }

    private static void RequireCatalog(TideV31BoatRuntimeCatalog catalog)
    {
        if (catalog == null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }
    }
}
