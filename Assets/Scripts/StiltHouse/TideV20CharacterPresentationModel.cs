using System;
using UnityEngine;

public enum TideV20CharacterActionState
{
    Idle,
    Walk,
    Swim,
    Repair,
    Haul,
}

/// <summary>
/// V20 人物的纯表现模型。它不持有场景对象，只负责动作决策、循环选帧、
/// 语义 Pivot 和统一身体尺度计算。
/// </summary>
public static class TideV20CharacterPresentationModel
{
    public const int CatalogVersion = 20;
    public const int IdleFrameCount = 4;
    public const int WalkFrameCount = 6;
    public const int SwimFrameCount = 6;
    public const int RepairFrameCount = 6;
    public const int HaulFrameCount = 6;
    public const int TotalFrameCount = 28;
    public const int ActionStateCount = 5;

    // 帧时长继承 V20 的 V3 动作契约，播放倍率由调用方按实际移动速度调节。
    public const float IdleFrameSeconds = 0.22f;
    public const float WalkFrameSeconds = 0.12f;
    public const float SwimFrameSeconds = 0.14f;
    public const float RepairFrameSeconds = 0.16f;
    public const float HaulFrameSeconds = 0.15f;

    public const float BodyWorldLength = 1.16f;
    // V20 的 2048x3072 画布保留了大量透明边距。实测四张 Idle 的身体
    // 轮廓为 2667-2715px；按整张 3072px 画布缩放会把静止人物压小约 13%，
    // 一切到 V41 行走就明显“长高”。2700px 是同一角色跨动作的生产标尺，
    // 不是逐帧透明包围盒，因此动作播放时不会再随姿势伸缩。
    public const float AuthoredBodyPixels = 2700f;
    public const float BodyScaleTolerance01 = 0.03f;
    public const float UniformScaleTolerance01 = 0.0001f;

    public const float StandingPivotNormalizedX = 0.5f;
    public const float StandingFootPivotNormalizedY = 0.03f;
    public const float SwimCenterPivotNormalizedX = 0.5f;
    public const float SwimCenterPivotNormalizedY = 0.5f;
    public const float HaulPivotNormalizedX = 0.35f;
    public const float HaulPivotNormalizedY = 0.03f;

    /// <summary>
    /// 动作优先级与现有角色语义一致：入水覆盖地面动作，持续拉网覆盖维修，
    /// 最后才根据真实移动选择 Walk 或 Idle。
    /// </summary>
    public static TideV20CharacterActionState ResolveActionState(
        bool isSwimming,
        bool isHauling,
        bool isRepairing,
        bool isMoving)
    {
        if (isSwimming)
        {
            return TideV20CharacterActionState.Swim;
        }

        if (isHauling)
        {
            return TideV20CharacterActionState.Haul;
        }

        if (isRepairing)
        {
            return TideV20CharacterActionState.Repair;
        }

        return isMoving
            ? TideV20CharacterActionState.Walk
            : TideV20CharacterActionState.Idle;
    }

    public static int EvaluateFrameIndex(
        TideV20CharacterActionState actionState,
        float worldTime,
        float playbackRate = 1f)
    {
        float frameSeconds = GetFrameSeconds(actionState);
        int frameCount = GetFrameCount(actionState);
        float animationTime = Mathf.Max(0f, worldTime) * Mathf.Max(0f, playbackRate);
        int absoluteFrame = Mathf.FloorToInt(animationTime / Mathf.Max(0.001f, frameSeconds));
        return absoluteFrame % frameCount;
    }

    public static Sprite EvaluateFrame(
        TideV20CharacterRuntimeCatalog catalog,
        TideV20CharacterActionState actionState,
        float worldTime,
        float playbackRate = 1f)
    {
        if (catalog == null)
        {
            return null;
        }

        return catalog.GetFrame(
            actionState,
            EvaluateFrameIndex(actionState, worldTime, playbackRate));
    }

    public static int GetFrameCount(TideV20CharacterActionState actionState)
    {
        switch (actionState)
        {
            case TideV20CharacterActionState.Idle:
                return IdleFrameCount;
            case TideV20CharacterActionState.Walk:
                return WalkFrameCount;
            case TideV20CharacterActionState.Swim:
                return SwimFrameCount;
            case TideV20CharacterActionState.Repair:
                return RepairFrameCount;
            case TideV20CharacterActionState.Haul:
                return HaulFrameCount;
            default:
                throw new ArgumentOutOfRangeException(nameof(actionState), actionState, null);
        }
    }

    public static float GetFrameSeconds(TideV20CharacterActionState actionState)
    {
        switch (actionState)
        {
            case TideV20CharacterActionState.Idle:
                return IdleFrameSeconds;
            case TideV20CharacterActionState.Walk:
                return WalkFrameSeconds;
            case TideV20CharacterActionState.Swim:
                return SwimFrameSeconds;
            case TideV20CharacterActionState.Repair:
                return RepairFrameSeconds;
            case TideV20CharacterActionState.Haul:
                return HaulFrameSeconds;
            default:
                throw new ArgumentOutOfRangeException(nameof(actionState), actionState, null);
        }
    }

    public static Vector2 GetPivotNormalized(TideV20CharacterActionState actionState)
    {
        switch (actionState)
        {
            case TideV20CharacterActionState.Swim:
                return new Vector2(SwimCenterPivotNormalizedX, SwimCenterPivotNormalizedY);
            case TideV20CharacterActionState.Haul:
                return new Vector2(HaulPivotNormalizedX, HaulPivotNormalizedY);
            case TideV20CharacterActionState.Idle:
            case TideV20CharacterActionState.Walk:
            case TideV20CharacterActionState.Repair:
                return new Vector2(StandingPivotNormalizedX, StandingFootPivotNormalizedY);
            default:
                throw new ArgumentOutOfRangeException(nameof(actionState), actionState, null);
        }
    }

    /// <summary>
    /// 根据同一人物的生产身体标尺和 PPU 返回唯一缩放标量。透明画布只用于
    /// 保持 Pivot 和动作空间，不能拿来当身体长度；返回值也不能拆成独立 X/Y。
    /// </summary>
    public static float CalculateUniformScale(
        TideV20CharacterActionState actionState,
        Vector2 spriteRectPixels,
        float pixelsPerUnit)
    {
        if (pixelsPerUnit <= 0f || spriteRectPixels.x <= 0f || spriteRectPixels.y <= 0f)
        {
            return 0f;
        }

        float authoredBodyWorldLength = GetAuthoredBodyPixelLength(actionState, spriteRectPixels) /
            pixelsPerUnit;
        return authoredBodyWorldLength <= 0f
            ? 0f
            : BodyWorldLength / authoredBodyWorldLength;
    }

    public static float CalculateUniformScale(
        TideV20CharacterActionState actionState,
        Sprite sprite)
    {
        return sprite == null
            ? 0f
            : CalculateUniformScale(actionState, sprite.rect.size, sprite.pixelsPerUnit);
    }

    public static Vector3 ToUniformLocalScale(float uniformScale)
    {
        return new Vector3(uniformScale, uniformScale, 1f);
    }

    public static float EvaluateBodyWorldLength(
        TideV20CharacterActionState actionState,
        Vector2 spriteRectPixels,
        float pixelsPerUnit,
        float uniformScale)
    {
        if (pixelsPerUnit <= 0f || uniformScale == 0f)
        {
            return 0f;
        }

        float bodyPixels = GetAuthoredBodyPixelLength(actionState, spriteRectPixels);
        return bodyPixels / pixelsPerUnit * Mathf.Abs(uniformScale);
    }

    public static bool IsUniformScale(Vector2 scaleXY)
    {
        float largestAxis = Mathf.Max(Mathf.Abs(scaleXY.x), Mathf.Abs(scaleXY.y));
        if (largestAxis <= 0f)
        {
            return false;
        }

        return Mathf.Abs(Mathf.Abs(scaleXY.x) - Mathf.Abs(scaleXY.y)) /
            largestAxis <= UniformScaleTolerance01;
    }

    /// <summary>
    /// 纯数据探针：调用方把五种动作最终测得的身体世界长度按枚举顺序传入。
    /// 它独立检查目标误差和动作间极差，避免只用缩放公式自证连续性。
    /// </summary>
    public static bool ProbeBodyScaleContinuity(float[] measuredBodyWorldLengths, out string reason)
    {
        if (measuredBodyWorldLengths == null || measuredBodyWorldLengths.Length != ActionStateCount)
        {
            reason = "身体尺度探针需要 Idle/Walk/Swim/Repair/Haul 五个实测值";
            return false;
        }

        float minLength = float.MaxValue;
        float maxLength = float.MinValue;
        for (int i = 0; i < measuredBodyWorldLengths.Length; i++)
        {
            float length = measuredBodyWorldLengths[i];
            if (float.IsNaN(length) || float.IsInfinity(length) || length <= 0f)
            {
                reason = $"动作 {(TideV20CharacterActionState)i} 的身体尺度无效：{length}";
                return false;
            }

            float targetError01 = Mathf.Abs(length - BodyWorldLength) / BodyWorldLength;
            if (targetError01 > BodyScaleTolerance01)
            {
                reason = $"动作 {(TideV20CharacterActionState)i} 的身体尺度 {length:F3} " +
                    $"偏离目标 {BodyWorldLength:F2} 超过 3%";
                return false;
            }

            minLength = Mathf.Min(minLength, length);
            maxLength = Mathf.Max(maxLength, length);
        }

        float continuityError01 = (maxLength - minLength) / BodyWorldLength;
        if (continuityError01 > BodyScaleTolerance01)
        {
            reason = $"动作间身体尺度极差 {maxLength - minLength:F3} 超过 3%";
            return false;
        }

        reason = $"连续：{minLength:F3}-{maxLength:F3}";
        return true;
    }

    private static float GetAuthoredBodyPixelLength(
        TideV20CharacterActionState actionState,
        Vector2 spriteRectPixels)
    {
        // 所有 V20 动作由同一人物尺度生产。使用动作姿势的透明包围盒会让弯腰、
        // 跨步和横游各自被“纠正”成相同外接框，反而造成角色真实比例跳变。
        return AuthoredBodyPixels;
    }
}
