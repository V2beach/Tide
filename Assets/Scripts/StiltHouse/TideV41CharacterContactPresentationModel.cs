using System;
using UnityEngine;

/// <summary>
/// V41 人物接触动作的纯表现规则。
///
/// 所有动作使用同一画布、PPU、Pivot 和脚底基线。运行时只做统一缩放与选帧，
/// 不用每张图的透明包围盒重新算尺寸，避免动作切换时人物忽大忽小。
/// </summary>
public static class TideV41CharacterContactPresentationModel
{
    public const int CatalogVersion = 41;
    public const int WalkFrameCount = 8;
    public const int CarryNetWalkFrameCount = 8;
    public const int BoardFrameCount = 8;
    public const int TieNetFrameCount = 6;
    public const int DoorEnterFrameCount = 6;
    public const int LowerSinklineFrameCount = 6;
    public const int LookoutFrameCount = 6;
    public const int TotalFrameCount = 48;

    public const float PixelsPerUnit = 512f;
    public const float CanvasPixels = 1024f;
    public const float PivotNormalizedY = 0.0625f;
    public const float FootContactTopLeftY = 930f;
    public const float AuthoredBodyPixels = 848f;
    public const float BodyWorldLength = TideV20CharacterPresentationModel.BodyWorldLength;

    public static float UniformScale => BodyWorldLength / (AuthoredBodyPixels / PixelsPerUnit);

    /// <summary>
    /// Sprite Pivot 位于画布底部 64px，脚底接触线位于底部 94px。
    /// Renderer 原点因此要略微下移，脚底像素才会真正落在逻辑楼板/船艉上。
    /// </summary>
    public static float FootPivotCorrectionWorldY
    {
        get
        {
            float pivotFromBottomPixels = CanvasPixels * PivotNormalizedY;
            float contactFromBottomPixels = CanvasPixels - FootContactTopLeftY;
            return -(contactFromBottomPixels - pivotFromBottomPixels) /
                PixelsPerUnit * UniformScale;
        }
    }

    public static int GetFrameCount(TideV41CharacterContactAction action)
    {
        switch (action)
        {
            case TideV41CharacterContactAction.Walk:
                return WalkFrameCount;
            case TideV41CharacterContactAction.CarryNetWalk:
                return CarryNetWalkFrameCount;
            case TideV41CharacterContactAction.Board:
                return BoardFrameCount;
            case TideV41CharacterContactAction.TieNet:
                return TieNetFrameCount;
            case TideV41CharacterContactAction.DoorEnter:
                return DoorEnterFrameCount;
            case TideV41CharacterContactAction.LowerSinkline:
                return LowerSinklineFrameCount;
            case TideV41CharacterContactAction.Lookout:
                return LookoutFrameCount;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    public static Sprite EvaluateLoopFrame(
        TideV41CharacterContactCatalog catalog,
        TideV41CharacterContactAction action,
        float movementCycle01)
    {
        if (catalog == null)
        {
            return null;
        }

        int count = GetFrameCount(action);
        int index = Mathf.FloorToInt(Mathf.Repeat(movementCycle01, 1f) * count) % count;
        return catalog.GetFrame(action, index);
    }

    public static Sprite EvaluateOneShotFrame(
        TideV41CharacterContactCatalog catalog,
        TideV41CharacterContactAction action,
        float progress01,
        bool reverse)
    {
        if (catalog == null)
        {
            return null;
        }

        int count = GetFrameCount(action);
        int index = Mathf.Min(count - 1, Mathf.FloorToInt(Mathf.Clamp01(progress01) * count));
        if (reverse)
        {
            index = count - 1 - index;
        }

        return catalog.GetFrame(action, index);
    }

    /// <summary>
    /// 返回 CarryNetWalk 当前帧双手之间的契约锚点。数值来自 V41
    /// runtime-contract.json，而不是从人物透明轮廓猜测。
    /// </summary>
    public static Vector2 GetCarryNetHandCenterTopLeftPixels(float movementCycle01)
    {
        int index = Mathf.FloorToInt(Mathf.Repeat(movementCycle01, 1f) * CarryNetWalkFrameCount) %
            CarryNetWalkFrameCount;
        switch (index)
        {
            case 1:
                return new Vector2(586f, 416f);
            case 2:
                return new Vector2(600.5f, 416f);
            case 3:
                return new Vector2(602.5f, 414f);
            case 4:
                return new Vector2(584f, 406f);
            case 5:
                return new Vector2(583f, 400f);
            case 6:
                return new Vector2(606.5f, 406f);
            case 7:
                return new Vector2(602.5f, 402f);
            default:
                return new Vector2(588.5f, 411f);
        }
    }
}
