using UnityEngine;

public enum TideMooredBoatAccessBlockReason
{
    None,
    RopeUnsecured,
    GangplankDisconnected,
    GangplankTooSteep,
    Night,
    StrongCurrent,
    RoughSea
}

public readonly struct TideMooringGangplankSample
{
    public TideMooringGangplankSample(
        float lengthMeters,
        float slopeDegrees,
        bool canSpan,
        bool isWalkable)
    {
        LengthMeters = lengthMeters;
        SlopeDegrees = slopeDegrees;
        CanSpan = canSpan;
        IsWalkable = isWalkable;
    }

    public float LengthMeters { get; }
    public float SlopeDegrees { get; }
    public bool CanSpan { get; }
    public bool IsWalkable { get; }
}

public readonly struct TideMooredBoatAccessSample
{
    public TideMooredBoatAccessSample(
        TideMooringGangplankSample gangplank,
        float localWaterVelocityMetersPerSecond,
        float localAgitation01,
        TideMooredBoatAccessBlockReason blockReason)
    {
        Gangplank = gangplank;
        LocalWaterVelocityMetersPerSecond = localWaterVelocityMetersPerSecond;
        LocalAgitation01 = localAgitation01;
        BlockReason = blockReason;
    }

    public TideMooringGangplankSample Gangplank { get; }
    public float LocalWaterVelocityMetersPerSecond { get; }
    public float LocalAgitation01 { get; }
    public TideMooredBoatAccessBlockReason BlockReason { get; }
    public bool IsOpen => BlockReason == TideMooredBoatAccessBlockReason.None;
}

/// <summary>
/// 泊位登船的唯一物理判定。平均潮位本身不代表流速，画面里也没有不可见的泥滩；
/// 因此这里只读取玩家能观察到的事实：船是否系稳、跳板是否真的接上且坡度可走、
/// 同一片海的局部流速与扰动，以及夜间能否看清航标。最终离开可以越过夜间限制，
/// 但不能越过断开的跳板、未固定的船或危险海况。
/// </summary>
public static class TideMooredBoatAccessModel
{
    public const float MinimumGangplankLengthMeters = 0.18f;
    public const float MaximumGangplankLengthMeters = 2.4f;
    public const float MaximumWalkableSlopeDegrees = 24f;
    public const float MaximumBoardingCurrentMetersPerSecond = 0.68f;
    public const float MaximumBoardingAgitation01 = 0.84f;

    public static TideMooringGangplankSample EvaluateGangplank(
        Vector2 pierTip,
        Vector2 boatSternFoot)
    {
        Vector2 span = boatSternFoot - pierTip;
        float length = span.magnitude;
        float slopeDegrees = length <= 0.0001f
            ? 90f
            : Mathf.Atan2(Mathf.Abs(span.y), Mathf.Abs(span.x)) * Mathf.Rad2Deg;
        bool canSpan = length >= MinimumGangplankLengthMeters &&
            length <= MaximumGangplankLengthMeters &&
            Mathf.Abs(span.x) >= 0.12f;
        return new TideMooringGangplankSample(
            length,
            slopeDegrees,
            canSpan,
            canSpan && slopeDegrees <= MaximumWalkableSlopeDegrees);
    }

    public static TideMooredBoatAccessSample Evaluate(
        TideMooringRopePhase ropePhase,
        TideMooringGangplankSample gangplank,
        float localWaterVelocityMetersPerSecond,
        float localAgitation01,
        bool isNight,
        bool finalDepartureReady)
    {
        float agitation01 = Mathf.Clamp01(localAgitation01);
        TideMooredBoatAccessBlockReason reason;
        if (ropePhase != TideMooringRopePhase.Secured)
        {
            reason = TideMooredBoatAccessBlockReason.RopeUnsecured;
        }
        else if (!gangplank.CanSpan)
        {
            reason = TideMooredBoatAccessBlockReason.GangplankDisconnected;
        }
        else if (!gangplank.IsWalkable)
        {
            reason = TideMooredBoatAccessBlockReason.GangplankTooSteep;
        }
        else if (isNight && !finalDepartureReady)
        {
            reason = TideMooredBoatAccessBlockReason.Night;
        }
        else if (Mathf.Abs(localWaterVelocityMetersPerSecond) >
                 MaximumBoardingCurrentMetersPerSecond)
        {
            reason = TideMooredBoatAccessBlockReason.StrongCurrent;
        }
        else if (agitation01 > MaximumBoardingAgitation01)
        {
            reason = TideMooredBoatAccessBlockReason.RoughSea;
        }
        else
        {
            reason = TideMooredBoatAccessBlockReason.None;
        }

        return new TideMooredBoatAccessSample(
            gangplank,
            localWaterVelocityMetersPerSecond,
            agitation01,
            reason);
    }
}
