using UnityEngine;

public enum TideMooringRopePhase
{
    Loose,
    Swinging,
    Attached,
    Reeling,
    Secured
}

[System.Serializable]
public struct TideMooringRopeState
{
    public TideMooringRopePhase Phase;
    public float ThrowCharge01;
    public float RopeLengthMeters;
    public float Tension01;
    public float OverstrainSeconds;
    public float BoatOffsetMeters;
    public float BoatVelocityMetersPerSecond;
}

/// <summary>
/// 泊位绳索模型。船不会被传送到岸边：潮流和风先推动船，抛绳只建立约束，
/// 玩家收绳再通过有限张力逐渐改变船速和距离。
/// </summary>
public static class TideMooringRopeModel
{
    // 玩家站在长潮汐跳板的岸端甩的是轻质引缆，不是把整根系缆抛过去。
    // 3.6m 足以覆盖 1.70m 跳板、初始离岸量和自然漂移边界。
    public const float MaximumThrowReachMeters = 3.6f;
    // 漂移硬边界必须小于理论最大抛距，否则船停在边界时只有甩绳正好经过
    // 数学峰值的单帧才能套中。抛缆距离与漂移边界分开，让一次接近完整的绳圈
    // 在真实输入帧率下仍可命中，同时船依旧会明显离开跳板。
    public const float MaximumBoatDriftMeters = 1.68f;
    public const float SecuredOffsetMeters = 0.14f;
    public const float ReelSpeedMetersPerSecond = 0.42f;

    public static TideMooringRopeState CreateLoose(float boatOffsetMeters)
    {
        return new TideMooringRopeState
        {
            Phase = TideMooringRopePhase.Loose,
            BoatOffsetMeters = boatOffsetMeters,
            RopeLengthMeters = Mathf.Abs(boatOffsetMeters) + 0.12f
        };
    }

    public static TideMooringRopeState BeginSwing(TideMooringRopeState state)
    {
        if (state.Phase == TideMooringRopePhase.Loose)
        {
            state.Phase = TideMooringRopePhase.Swinging;
            state.ThrowCharge01 = 0f;
        }

        return state;
    }

    public static TideMooringRopeState AdvanceSwing(
        TideMooringRopeState state,
        float deltaSeconds,
        bool held)
    {
        if (state.Phase != TideMooringRopePhase.Swinging || !held)
        {
            return state;
        }

        // 一次完整甩绳约 1.1 秒；超过一圈后回到短距离，避免“按得越久越远”。
        state.ThrowCharge01 = Mathf.Repeat(state.ThrowCharge01 + Mathf.Max(0f, deltaSeconds) / 1.1f, 1f);
        return state;
    }

    public static TideMooringRopeState ReleaseThrow(TideMooringRopeState state)
    {
        if (state.Phase != TideMooringRopePhase.Swinging)
        {
            return state;
        }

        float reach01 = Mathf.Sin(Mathf.Clamp01(state.ThrowCharge01) * Mathf.PI);
        float throwReach = Mathf.Lerp(0.32f, MaximumThrowReachMeters, reach01);
        bool caught = Mathf.Abs(state.BoatOffsetMeters) <= throwReach && reach01 >= 0.34f;
        state.Phase = caught ? TideMooringRopePhase.Attached : TideMooringRopePhase.Loose;
        state.RopeLengthMeters = caught
            ? Mathf.Abs(state.BoatOffsetMeters) + 0.08f
            : state.RopeLengthMeters;
        state.ThrowCharge01 = 0f;
        return state;
    }

    public static TideMooringRopeState Advance(
        TideMooringRopeState state,
        float deltaSeconds,
        float currentVelocity,
        float windVelocity,
        bool reelHeld)
    {
        float dt = Mathf.Max(0f, deltaSeconds);
        float environmentalTarget = currentVelocity * 0.72f + windVelocity * 0.22f;
        state.BoatVelocityMetersPerSecond = Mathf.MoveTowards(
            state.BoatVelocityMetersPerSecond,
            environmentalTarget,
            dt * 0.34f);

        bool constrained = state.Phase == TideMooringRopePhase.Attached ||
            state.Phase == TideMooringRopePhase.Reeling ||
            state.Phase == TideMooringRopePhase.Secured;
        if (constrained)
        {
            state.Phase = reelHeld ? TideMooringRopePhase.Reeling : state.Phase;
            if (reelHeld)
            {
                state.RopeLengthMeters = Mathf.Max(
                    SecuredOffsetMeters,
                    state.RopeLengthMeters - ReelSpeedMetersPerSecond * dt);
            }

            float distance = Mathf.Abs(state.BoatOffsetMeters);
            float extension = Mathf.Max(0f, distance - state.RopeLengthMeters);
            float relativeSpeed = Mathf.Abs(state.BoatVelocityMetersPerSecond);
            state.Tension01 = Mathf.Clamp01(extension / 0.42f + relativeSpeed / 1.8f);
            float towardDock = state.BoatOffsetMeters == 0f ? 0f : -Mathf.Sign(state.BoatOffsetMeters);
            state.BoatVelocityMetersPerSecond += towardDock * state.Tension01 * 1.35f * dt;
            state.OverstrainSeconds = state.Tension01 > 0.94f
                ? state.OverstrainSeconds + dt
                : Mathf.Max(0f, state.OverstrainSeconds - dt * 1.8f);

            if (state.OverstrainSeconds >= 0.75f)
            {
                state.Phase = TideMooringRopePhase.Loose;
                state.Tension01 = 0f;
                state.OverstrainSeconds = 0f;
            }
            else if (distance <= SecuredOffsetMeters && relativeSpeed <= 0.12f)
            {
                state.Phase = TideMooringRopePhase.Secured;
                state.BoatOffsetMeters = Mathf.MoveTowards(state.BoatOffsetMeters, 0f, dt * 0.22f);
                state.BoatVelocityMetersPerSecond = Mathf.MoveTowards(
                    state.BoatVelocityMetersPerSecond,
                    0f,
                    dt * 0.8f);
                state.RopeLengthMeters = SecuredOffsetMeters;
            }
        }
        else
        {
            state.Tension01 = 0f;
        }

        state.BoatVelocityMetersPerSecond *= Mathf.Exp(-0.18f * dt);
        state.BoatOffsetMeters = Mathf.Clamp(
            state.BoatOffsetMeters + state.BoatVelocityMetersPerSecond * dt,
            -MaximumBoatDriftMeters,
            MaximumBoatDriftMeters);
        return state;
    }
}
