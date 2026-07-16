using System;
using UnityEngine;

public enum TideV39BoatLayer
{
    BackRig,
    SailRest,
    BackHull,
    CockpitFloor,
    FrontGunwale,
    RudderRest,
}

/// <summary>
/// V39 帆船语义契约的纯坐标模型。
///
/// 所有输入像素都使用资源契约的左上原点；运行时只允许在 BoatRoot 上做一次
/// 等比缩放和一次整体镜像/滚转。船体层、乘员和交互锚点必须走同一条变换链，
/// 否则会出现图像在水里、碰撞和人物却悬在另一处的“所见非所得”。
/// </summary>
public static class TideV39BoatPresentationModel
{
    public const int CatalogVersion = 39;
    public const int LayerCount = 6;
    public const float PixelsPerUnit = 512f;
    public const float BoatRootScale = 0.42f;
    public const int StaticCalmWaterlineYTopLeft = 3440;

    public static readonly Vector2Int CanvasSize = new Vector2Int(6144, 4096);
    public static readonly Vector2 PivotNormalized = new Vector2(0.5f, 0.03f);

    public static readonly Vector2Int SeatTopLeft = new Vector2Int(4188, 3031);
    public static readonly Vector2Int SternStepTopLeft = new Vector2Int(4740, 2830);
    public static readonly Vector2Int CockpitEntryTopLeft = new Vector2Int(4470, 2910);
    public static readonly Vector2Int MooringPointTopLeft = new Vector2Int(4930, 2825);

    private static readonly Vector2[] DamagedLayerOffsets =
    {
        new Vector2(1.210938f, 4.718008f),
        new Vector2(1.469727f, 4.253164f),
        new Vector2(-0.168945f, 4.119375f),
        new Vector2(-0.315430f, 2.203359f),
        new Vector2(-0.087891f, 1.544180f),
        new Vector2(3.394531f, 1.393789f),
    };

    private static readonly Vector2[] RepairedLayerOffsets =
    {
        new Vector2(1.405273f, 4.645742f),
        new Vector2(1.469727f, 4.261953f),
        new Vector2(-1.389648f, 2.765859f),
        new Vector2(-0.315430f, 2.203359f),
        new Vector2(-0.090820f, 1.544180f),
        new Vector2(3.394531f, 1.393789f),
    };

    public static Vector2 GetLayerOffset(TideV39BoatLayer layer, bool repaired)
    {
        int index = (int)layer;
        if (index < 0 || index >= LayerCount)
        {
            throw new ArgumentOutOfRangeException(nameof(layer), layer, null);
        }

        return repaired ? RepairedLayerOffsets[index] : DamagedLayerOffsets[index];
    }

    public static Vector2 PixelTopLeftToBoatOffset(Vector2 pixelTopLeft)
    {
        return new Vector2(
            (pixelTopLeft.x - CanvasSize.x * PivotNormalized.x) / PixelsPerUnit,
            (CanvasSize.y - pixelTopLeft.y - CanvasSize.y * PivotNormalized.y) / PixelsPerUnit);
    }

    public static float EvaluateBoatRootY(float actualWaterY, float uniformScale = BoatRootScale)
    {
        float waterlineOffsetY = PixelTopLeftToBoatOffset(
            new Vector2(0f, StaticCalmWaterlineYTopLeft)).y;
        return actualWaterY - waterlineOffsetY * uniformScale;
    }

    public static Vector2 EvaluateLayerWorldPosition(
        Vector2 boatRootWorldPosition,
        TideV39BoatLayer layer,
        bool repaired,
        float rotationZ,
        bool flipX,
        float uniformScale = BoatRootScale)
    {
        return EvaluateOffsetWorldPosition(
            boatRootWorldPosition,
            GetLayerOffset(layer, repaired),
            rotationZ,
            flipX,
            uniformScale);
    }

    public static Vector2 EvaluateAnchorWorldPosition(
        Vector2 boatRootWorldPosition,
        Vector2Int anchorTopLeft,
        float rotationZ,
        bool flipX,
        float uniformScale = BoatRootScale)
    {
        return EvaluateOffsetWorldPosition(
            boatRootWorldPosition,
            PixelTopLeftToBoatOffset(anchorTopLeft),
            rotationZ,
            flipX,
            uniformScale);
    }

    public static Vector2 EvaluateOffsetWorldPosition(
        Vector2 boatRootWorldPosition,
        Vector2 localOffset,
        float rotationZ,
        bool flipX,
        float uniformScale = BoatRootScale)
    {
        Vector2 posedOffset = localOffset * uniformScale;
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
}
