using UnityEngine;

public enum TideSailingSalvageAttachmentPhase
{
    Inactive,
    Free,
    Hooking,
    Secured
}

public enum TideSailingSalvageAdvanceOutcome
{
    None,
    ThrowRetracted,
    HookAttached,
    Detached,
    Secured
}

public enum TideSailingSalvageThrowFailure
{
    None,
    AlreadyThrowing,
    OutOfReach,
    AheadOfStern,
    RelativeSpeedTooHigh
}

public readonly struct TideSailingSalvageThrowResult
{
    public TideSailingSalvageThrowResult(
        bool started,
        TideSailingSalvageThrowFailure failure,
        float relativeSpeed)
    {
        Started = started;
        Failure = failure;
        RelativeSpeed = relativeSpeed;
    }

    public bool Started { get; }
    public TideSailingSalvageThrowFailure Failure { get; }
    public float RelativeSpeed { get; }
}

public readonly struct TideSailingSalvageAdvanceResult
{
    public TideSailingSalvageAdvanceResult(
        TideSailingSalvageAdvanceOutcome outcome,
        float resolvedBoatVelocity)
    {
        Outcome = outcome;
        ResolvedBoatVelocity = resolvedBoatVelocity;
    }

    public TideSailingSalvageAdvanceOutcome Outcome { get; }
    public float ResolvedBoatVelocity { get; }
}

/// <summary>
/// 短航漂物的唯一运行状态。组件负责自由漂移、连续抛钩、绳长、张力、
/// 过载脱钩和收妥；它不知道物资类型、库存或剧情 owner，也不生成自己的风、
/// 潮或浪场。主场景每帧注入同一片海的样本，并把一次性结果翻译为实物 owner。
/// </summary>
public sealed class TideSailingSalvageController : MonoBehaviour
{
    [SerializeField] private float worldX;
    [SerializeField] private float velocity;
    [SerializeField] private float hookProgress01;
    [SerializeField] private float throw01;
    [SerializeField] private bool throwActive;
    [SerializeField] private bool hauling;
    [SerializeField] private float tension01;
    [SerializeField] private float overstrainSeconds;
    [SerializeField] private Vector2 hookWorldPosition;
    [SerializeField] private float initialRopeLength;

    // Setters are intentionally available for deterministic Scene previews and owner
    // restoration. Runtime progression still goes through BeginThrow/Advance.
    public float WorldX { get => worldX; set => worldX = value; }
    public float Velocity { get => velocity; set => velocity = value; }
    public float HookProgress01 { get => hookProgress01; set => hookProgress01 = Mathf.Clamp01(value); }
    public float Throw01 { get => throw01; set => throw01 = Mathf.Clamp01(value); }
    public bool ThrowActive { get => throwActive; set => throwActive = value; }
    public bool Hauling { get => hauling; set => hauling = value; }
    public float Tension01 { get => tension01; set => tension01 = Mathf.Clamp01(value); }
    public float OverstrainSeconds { get => overstrainSeconds; set => overstrainSeconds = Mathf.Max(0f, value); }
    public Vector2 HookWorldPosition { get => hookWorldPosition; set => hookWorldPosition = value; }
    public float InitialRopeLength { get => initialRopeLength; set => initialRopeLength = Mathf.Max(0f, value); }

    public void ResetRuntime(float initialWorldX, Vector2 initialHookPosition)
    {
        worldX = initialWorldX;
        velocity = 0f;
        ResetInteraction(initialHookPosition);
    }

    public void ResetInteraction(Vector2 initialHookPosition)
    {
        hookProgress01 = 0f;
        throw01 = 0f;
        throwActive = false;
        hauling = false;
        tension01 = 0f;
        overstrainSeconds = 0f;
        hookWorldPosition = initialHookPosition;
        initialRopeLength = 0f;
    }

    public TideSailingSalvageThrowResult BeginThrow(
        Vector2 sternPosition,
        float timberSurfaceY,
        float maximumReach,
        float maximumRelativeSpeed,
        float boatWorldVelocity)
    {
        float relativeSpeed = GetRelativeSpeed(boatWorldVelocity);
        if (throwActive)
        {
            return new TideSailingSalvageThrowResult(
                false,
                TideSailingSalvageThrowFailure.AlreadyThrowing,
                relativeSpeed);
        }

        Vector2 timberPosition = new Vector2(worldX, timberSurfaceY);
        if (Vector2.Distance(sternPosition, timberPosition) > maximumReach)
        {
            return new TideSailingSalvageThrowResult(
                false,
                TideSailingSalvageThrowFailure.OutOfReach,
                relativeSpeed);
        }

        // 绳系在左船艉。漂物仍在船艏前方时抛钩会让绳穿过船体，因此必须先
        // 越过漂物、收帆贴流，再从下流侧船艉抛出。
        if (timberPosition.x > sternPosition.x + 0.08f)
        {
            return new TideSailingSalvageThrowResult(
                false,
                TideSailingSalvageThrowFailure.AheadOfStern,
                relativeSpeed);
        }

        if (relativeSpeed > maximumRelativeSpeed)
        {
            return new TideSailingSalvageThrowResult(
                false,
                TideSailingSalvageThrowFailure.RelativeSpeedTooHigh,
                relativeSpeed);
        }

        throwActive = true;
        throw01 = 0f;
        hookWorldPosition = sternPosition;
        overstrainSeconds = 0f;
        return new TideSailingSalvageThrowResult(
            true,
            TideSailingSalvageThrowFailure.None,
            relativeSpeed);
    }

    public TideSailingSalvageAdvanceResult Advance(
        float deltaSeconds,
        bool interactionHeld,
        TideSailingSalvageAttachmentPhase phase,
        Vector2 sternPosition,
        float timberSurfaceY,
        float restingWorldX,
        TideOceanSample oceanAtTimber,
        float surfaceFlowSpeed,
        float windSpeed,
        float stormPressure01,
        float waterIngress01,
        float boatWorldVelocity,
        float boatVelocity)
    {
        float dt = Mathf.Max(0f, deltaSeconds);
        hauling = false;
        if (dt <= 0f || phase == TideSailingSalvageAttachmentPhase.Inactive)
        {
            return new TideSailingSalvageAdvanceResult(
                TideSailingSalvageAdvanceOutcome.None,
                boatVelocity);
        }

        if (phase == TideSailingSalvageAttachmentPhase.Free)
        {
            AdvanceFreeDrift(dt, restingWorldX, oceanAtTimber, surfaceFlowSpeed, windSpeed);
            if (!throwActive)
            {
                return new TideSailingSalvageAdvanceResult(
                    TideSailingSalvageAdvanceOutcome.None,
                    boatVelocity);
            }

            throw01 = TideContinuousSalvageModel.AdvanceThrow01(
                throw01,
                dt,
                interactionHeld);
            Vector2 timberPosition = new Vector2(worldX, timberSurfaceY);
            hookWorldPosition = Vector2.Lerp(
                sternPosition,
                timberPosition,
                Mathf.SmoothStep(0f, 1f, throw01));
            if (!interactionHeld && throw01 <= 0.001f)
            {
                throwActive = false;
                return new TideSailingSalvageAdvanceResult(
                    TideSailingSalvageAdvanceOutcome.ThrowRetracted,
                    boatVelocity);
            }

            if (throw01 < 0.999f)
            {
                return new TideSailingSalvageAdvanceResult(
                    TideSailingSalvageAdvanceOutcome.None,
                    boatVelocity);
            }

            throwActive = false;
            throw01 = 1f;
            hookProgress01 = 0f;
            initialRopeLength = Mathf.Max(
                0.38f,
                Vector2.Distance(sternPosition, timberPosition));
            tension01 = 0f;
            overstrainSeconds = 0f;
            return new TideSailingSalvageAdvanceResult(
                TideSailingSalvageAdvanceOutcome.HookAttached,
                boatVelocity);
        }

        if (phase == TideSailingSalvageAttachmentPhase.Hooking)
        {
            return AdvanceHooked(
                dt,
                interactionHeld,
                sternPosition,
                timberSurfaceY,
                stormPressure01,
                waterIngress01,
                boatWorldVelocity,
                boatVelocity);
        }

        hookProgress01 = 1f;
        tension01 = Mathf.Lerp(
            tension01,
            Mathf.Clamp01(Mathf.Abs(boatWorldVelocity) * 0.28f + stormPressure01 * 0.16f),
            Mathf.Clamp01(dt * 3f));
        worldX = sternPosition.x - 0.3f;
        velocity = boatWorldVelocity;
        return new TideSailingSalvageAdvanceResult(
            TideSailingSalvageAdvanceOutcome.None,
            boatVelocity);
    }

    public float EvaluateTowLoad01(TideSailingSalvageAttachmentPhase phase)
    {
        if (phase == TideSailingSalvageAttachmentPhase.Secured)
        {
            return 1f;
        }

        return phase == TideSailingSalvageAttachmentPhase.Hooking
            ? TideContinuousSalvageModel.EvaluateTowLoad01(hookProgress01, tension01, false)
            : 0f;
    }

    public float GetRelativeSpeed(float boatWorldVelocity)
    {
        return Mathf.Abs(boatWorldVelocity - velocity);
    }

    public void DetachPreservingWorld(Vector2 hookReturnPosition)
    {
        // WorldX and Velocity deliberately survive. A failed tow or player death
        // returns the same physical bundle to free drift instead of respawning it.
        ResetInteraction(hookReturnPosition);
    }

    private void AdvanceFreeDrift(
        float deltaSeconds,
        float restingWorldX,
        TideOceanSample ocean,
        float surfaceFlowSpeed,
        float windSpeed)
    {
        // 同一 OceanField 决定船、游泳者和漂物。restingWorldX 只是远海残骸带
        // 的宽缓回拉，防止原型唯一漂物永久离开首个可玩海域，不是隐藏吸附点。
        float flowTarget = surfaceFlowSpeed * 0.72f +
            windSpeed * 0.14f +
            ocean.HorizontalVelocity * Mathf.Lerp(0.7f, 1.2f, ocean.Agitation01);
        float wrackPull = (restingWorldX - worldX) * 0.16f;
        float targetVelocity = Mathf.Clamp(flowTarget + wrackPull, -0.82f, 0.82f);
        float response = Mathf.Lerp(0.28f, 0.52f, ocean.Agitation01);
        velocity = Mathf.MoveTowards(velocity, targetVelocity, deltaSeconds * response);
        worldX += velocity * deltaSeconds;

        float minimumX = restingWorldX - 1.75f;
        float maximumX = restingWorldX + 2.05f;
        float edgePush = worldX < minimumX
            ? minimumX - worldX
            : worldX > maximumX
                ? maximumX - worldX
                : 0f;
        if (Mathf.Abs(edgePush) > 0.001f)
        {
            velocity = Mathf.MoveTowards(velocity, edgePush * 0.4f, deltaSeconds * 0.48f);
            worldX += edgePush * Mathf.Clamp01(deltaSeconds * 0.75f);
        }
    }

    private TideSailingSalvageAdvanceResult AdvanceHooked(
        float deltaSeconds,
        bool interactionHeld,
        Vector2 sternPosition,
        float timberSurfaceY,
        float stormPressure01,
        float waterIngress01,
        float boatWorldVelocity,
        float boatVelocity)
    {
        Vector2 timberPosition = new Vector2(worldX, timberSurfaceY);
        float ropeDistance = Vector2.Distance(sternPosition, timberPosition);
        if (initialRopeLength <= 0.01f)
        {
            initialRopeLength = Mathf.Max(0.38f, ropeDistance);
        }

        float relativeSpeed = GetRelativeSpeed(boatWorldVelocity);
        float allowedLength = Mathf.Lerp(
            initialRopeLength,
            0.32f,
            Mathf.SmoothStep(0f, 1f, hookProgress01));
        tension01 = TideContinuousSalvageModel.EvaluateTension01(
            ropeDistance,
            allowedLength,
            relativeSpeed,
            stormPressure01);
        overstrainSeconds = TideContinuousSalvageModel.AdvanceOverstrainSeconds(
            overstrainSeconds,
            tension01,
            deltaSeconds);
        if (TideContinuousSalvageModel.ShouldDetach(overstrainSeconds))
        {
            ResetInteraction(sternPosition);
            return new TideSailingSalvageAdvanceResult(
                TideSailingSalvageAdvanceOutcome.Detached,
                boatVelocity);
        }

        float previousProgress = hookProgress01;
        hookProgress01 = TideContinuousSalvageModel.AdvanceHaul01(
            hookProgress01,
            deltaSeconds,
            interactionHeld,
            relativeSpeed,
            tension01,
            stormPressure01,
            waterIngress01);
        hauling = hookProgress01 > previousProgress + 0.0001f;

        float desiredLength = Mathf.Lerp(
            initialRopeLength,
            0.32f,
            Mathf.SmoothStep(0f, 1f, hookProgress01));
        float signedOffset = worldX - sternPosition.x;
        float side = Mathf.Abs(signedOffset) > 0.001f ? Mathf.Sign(signedOffset) : 1f;
        float targetX = sternPosition.x + side * desiredLength;
        float previousX = worldX;
        if (Mathf.Abs(signedOffset) > desiredLength || hauling)
        {
            float correctionSpeed = Mathf.Lerp(0.28f, 1.08f, hauling ? 1f : tension01);
            worldX = Mathf.MoveTowards(worldX, targetX, correctionSpeed * deltaSeconds);
        }
        else
        {
            worldX += velocity * deltaSeconds;
        }

        float actualVelocity = deltaSeconds > 0.0001f
            ? (worldX - previousX) / deltaSeconds
            : velocity;
        velocity = Mathf.Lerp(velocity, actualVelocity, Mathf.Clamp01(deltaSeconds * 4f));
        float resolvedBoatVelocity = Mathf.MoveTowards(
            boatVelocity,
            velocity,
            tension01 * deltaSeconds * 0.22f);

        if (hookProgress01 >= TideContinuousSalvageModel.SecuredProgress01 &&
            Mathf.Abs(worldX - sternPosition.x) <= 0.48f)
        {
            hookProgress01 = 1f;
            worldX = sternPosition.x - 0.3f;
            velocity = boatWorldVelocity;
            tension01 = 0.24f;
            overstrainSeconds = 0f;
            hauling = false;
            return new TideSailingSalvageAdvanceResult(
                TideSailingSalvageAdvanceOutcome.Secured,
                resolvedBoatVelocity);
        }

        return new TideSailingSalvageAdvanceResult(
            TideSailingSalvageAdvanceOutcome.None,
            resolvedBoatVelocity);
    }
}
