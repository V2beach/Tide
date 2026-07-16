using UnityEngine;

public readonly struct TideSailingReefSample
{
    public TideSailingReefSample(
        float waterSurfaceElevationMeters,
        float reefCrownElevationMeters,
        float waterDepthAboveCrownMeters,
        float boatDraftMeters,
        float underKeelClearanceMeters,
        float exposedRock01,
        float shallowRisk01)
    {
        WaterSurfaceElevationMeters = waterSurfaceElevationMeters;
        ReefCrownElevationMeters = reefCrownElevationMeters;
        WaterDepthAboveCrownMeters = waterDepthAboveCrownMeters;
        BoatDraftMeters = boatDraftMeters;
        UnderKeelClearanceMeters = underKeelClearanceMeters;
        ExposedRock01 = exposedRock01;
        ShallowRisk01 = shallowRisk01;
    }

    public float WaterSurfaceElevationMeters { get; }
    public float ReefCrownElevationMeters { get; }
    public float WaterDepthAboveCrownMeters { get; }
    public float BoatDraftMeters { get; }
    public float UnderKeelClearanceMeters { get; }
    public float ExposedRock01 { get; }
    public float ShallowRisk01 { get; }
    public bool GroundsKeel => UnderKeelClearanceMeters <= 0f;
    public bool HasComfortableClearance => UnderKeelClearanceMeters >= TideSailingReefModel.ComfortableClearanceMeters;
}

/// <summary>
/// 固定浅礁、瞬时水深与船吃水之间的纯规则。礁石不会跟浪上下移动；天文潮、
/// 局部浪谷、舱内进水、拖载和高速浅水下沉共同改变船底净空。输入输出均为米和
/// 米每秒，压缩的游戏昼夜不会加速这里的任何动作频率。
/// </summary>
public static class TideSailingReefModel
{
    public const float ReefCrownAboveLowestWaterMeters = 0.72f;
    public const float ReefHalfWidthMeters = 0.56f;
    public const float BaseBoatDraftMeters = 0.3f;
    public const float MaximumIngressDraftMeters = 0.18f;
    public const float MaximumTowLoadDraftMeters = 0.08f;
    public const float MaximumShallowSpeedDraftMeters = 0.065f;
    public const float ComfortableClearanceMeters = 0.18f;
    public const float HullStrikeSpeedMetersPerSecond = 0.68f;

    public static TideSailingReefSample Evaluate(
        float lowestAstronomicalWaterY,
        float instantaneousWaterSurfaceY,
        float ingress01,
        float towLoad01,
        float horizontalSpeedMetersPerSecond,
        float referenceMaximumSpeedMetersPerSecond)
    {
        float reefCrownY = lowestAstronomicalWaterY + ReefCrownAboveLowestWaterMeters;
        float waterDepth = instantaneousWaterSurfaceY - reefCrownY;
        float speed01 = Mathf.Clamp01(
            Mathf.Abs(horizontalSpeedMetersPerSecond) /
            Mathf.Max(0.01f, referenceMaximumSpeedMetersPerSecond));
        float draft = BaseBoatDraftMeters +
            Mathf.Clamp01(ingress01) * MaximumIngressDraftMeters +
            Mathf.Clamp01(towLoad01) * MaximumTowLoadDraftMeters +
            speed01 * speed01 * MaximumShallowSpeedDraftMeters;
        float clearance = waterDepth - draft;

        // 露礁值只表达固定岩体相对水面的关系；它不负责额外碰撞或提示。
        float exposedRock01 = 1f - Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(-0.2f, 0.62f, waterDepth));
        float shallowRisk01 = 1f - Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(-0.04f, ComfortableClearanceMeters, clearance));
        return new TideSailingReefSample(
            instantaneousWaterSurfaceY,
            reefCrownY,
            waterDepth,
            draft,
            clearance,
            exposedRock01,
            shallowRisk01);
    }

    public static bool SegmentEntersGroundedReef(
        float previousBoatX,
        float proposedBoatX,
        float reefCenterX,
        TideSailingReefSample sample)
    {
        if (!sample.GroundsKeel || Mathf.Approximately(previousBoatX, proposedBoatX))
        {
            return false;
        }

        float leftEdge = reefCenterX - ReefHalfWidthMeters;
        float rightEdge = reefCenterX + ReefHalfWidthMeters;
        float segmentMin = Mathf.Min(previousBoatX, proposedBoatX);
        float segmentMax = Mathf.Max(previousBoatX, proposedBoatX);
        return segmentMax >= leftEdge && segmentMin <= rightEdge;
    }

    public static float ConstrainOutsideGroundedReef(
        float previousBoatX,
        float proposedBoatX,
        float reefCenterX)
    {
        float leftEdge = reefCenterX - ReefHalfWidthMeters;
        float rightEdge = reefCenterX + ReefHalfWidthMeters;
        if (previousBoatX >= leftEdge && previousBoatX <= rightEdge)
        {
            // 高潮时已经在礁顶上方，随后浪谷或退潮使船坐礁：船应在当前
            // 接触点停住，不能为了满足边界约束瞬移到礁石另一侧。
            return previousBoatX;
        }

        return proposedBoatX > previousBoatX
            ? Mathf.Min(proposedBoatX, leftEdge)
            : Mathf.Max(proposedBoatX, rightEdge);
    }

    public static bool ShouldDamageHull(
        TideSailingReefSample sample,
        float horizontalSpeedMetersPerSecond)
    {
        return sample.GroundsKeel &&
            Mathf.Abs(horizontalSpeedMetersPerSecond) >= HullStrikeSpeedMetersPerSecond;
    }
}
