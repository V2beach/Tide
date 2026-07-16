using UnityEngine;

public enum TideHeavyWreckPhase
{
    GroundedLoose,
    OnePointSecured,
    TwoPointSecured,
    FloatingSecured,
    Reeling,
    AtCradleAfloat,
    RecoveredIntact,
    ExposingJoints,
    JointsExposed,
    Separating,
    Separated,
    Lost
}

[System.Serializable]
public struct TideHeavyWreckState
{
    public TideHeavyWreckPhase Phase;
    public int SecuredPointMask;
    public float TowProgress01;
    public float DriftMeters;
    public float Lift01;
    public float Tension01;
    public float OverstrainSeconds;
    public float WorkProgress01;
}

/// <summary>
/// 借潮拖运重型残骸的纯状态模型。低潮只负责接近和双点系稳，涨潮浮力才解除
/// 海床摩擦；平流窗口允许收绳，退潮后原物落在作业架上才能继续显缝和拆解。
/// </summary>
public static class TideHeavyWreckTidalLiftModel
{
    public const float MaximumSecuringDepthMeters = 0.46f;
    public const float LiftStartsAtDepthMeters = 0.46f;
    public const float FullyAfloatDepthMeters = 0.82f;
    public const float CradleGroundingLift01 = 0.32f;
    public const float CriticalTension01 = 0.9f;
    public const float OnePointFailureSeconds = 0.9f;
    public const float MaximumLooseDriftMeters = 1.35f;
    public const float ExposeJointSeconds = 2.4f;
    public const float SeparateSeconds = 3.2f;

    public static TideHeavyWreckState CreateInitial()
    {
        return new TideHeavyWreckState
        {
            Phase = TideHeavyWreckPhase.GroundedLoose
        };
    }

    public static float EvaluateLift01(float waterDepthMeters)
    {
        return Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(
                LiftStartsAtDepthMeters,
                FullyAfloatDepthMeters,
                waterDepthMeters));
    }

    public static bool CanSecureAtDepth(float waterDepthMeters)
    {
        return waterDepthMeters <= MaximumSecuringDepthMeters;
    }

    public static TideHeavyWreckState TrySecurePoint(
        TideHeavyWreckState state,
        int pointIndex,
        float waterDepthMeters,
        out bool secured)
    {
        secured = false;
        if (state.Phase == TideHeavyWreckPhase.Lost ||
            state.Phase >= TideHeavyWreckPhase.AtCradleAfloat ||
            pointIndex < 0 || pointIndex > 1 ||
            !CanSecureAtDepth(waterDepthMeters))
        {
            return state;
        }

        int bit = 1 << pointIndex;
        if ((state.SecuredPointMask & bit) != 0)
        {
            return state;
        }

        state.SecuredPointMask |= bit;
        state.OverstrainSeconds = 0f;
        state.Phase = state.SecuredPointMask == 3
            ? TideHeavyWreckPhase.TwoPointSecured
            : TideHeavyWreckPhase.OnePointSecured;
        secured = true;
        return state;
    }

    public static TideHeavyWreckState AdvanceNatural(
        TideHeavyWreckState state,
        float deltaSeconds,
        float waterDepthMeters,
        float signedCurrentMetersPerSecond,
        float waveOrbitalVelocity,
        bool reelHeld)
    {
        float dt = Mathf.Max(0f, deltaSeconds);
        if (dt <= 0f || state.Phase >= TideHeavyWreckPhase.RecoveredIntact)
        {
            return state;
        }

        state.Lift01 = EvaluateLift01(waterDepthMeters);
        bool afloat = state.Lift01 >= 0.72f;
        float effectiveFlow = signedCurrentMetersPerSecond + waveOrbitalVelocity * 0.42f;
        float flowLoad01 = Mathf.InverseLerp(0.08f, 0.82f, Mathf.Abs(effectiveFlow));

        if (state.TowProgress01 >= 0.999f)
        {
            state.TowProgress01 = 1f;
            state.DriftMeters = Mathf.MoveTowards(state.DriftMeters, 0f, dt * 0.35f);
            state.Tension01 = afloat ? Mathf.Clamp01(0.16f + flowLoad01 * 0.48f) : 0.08f;
            state.Phase = state.Lift01 <= CradleGroundingLift01
                ? TideHeavyWreckPhase.RecoveredIntact
                : TideHeavyWreckPhase.AtCradleAfloat;
            return state;
        }

        if (state.SecuredPointMask != 3)
        {
            AdvanceLooseOrSinglePoint(
                ref state,
                dt,
                afloat,
                effectiveFlow,
                flowLoad01);
            return state;
        }

        state.DriftMeters = Mathf.MoveTowards(state.DriftMeters, 0f, dt * 0.42f);
        state.Tension01 = afloat
            ? Mathf.Clamp01(flowLoad01 * 0.68f + Mathf.Abs(state.DriftMeters) * 0.45f)
            : 0.06f;
        state.OverstrainSeconds = 0f;

        if (!afloat)
        {
            state.Phase = TideHeavyWreckPhase.TwoPointSecured;
            return state;
        }

        if (!reelHeld)
        {
            state.Phase = TideHeavyWreckPhase.FloatingSecured;
            return state;
        }

        state.Phase = TideHeavyWreckPhase.Reeling;
        float slack01 = 1f - Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(0.06f, 0.68f, Mathf.Abs(signedCurrentMetersPerSecond)));
        float workableTension01 = 1f - Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(0.62f, 0.96f, state.Tension01));
        float liftFactor = Mathf.Lerp(0.42f, 1f, state.Lift01);
        float reelRate = Mathf.Lerp(0.025f, 0.19f, slack01 * workableTension01) * liftFactor;
        state.TowProgress01 = Mathf.Clamp01(state.TowProgress01 + reelRate * dt);
        if (state.TowProgress01 >= 0.999f)
        {
            state.TowProgress01 = 1f;
            state.Phase = TideHeavyWreckPhase.AtCradleAfloat;
        }

        return state;
    }

    public static TideHeavyWreckState TryBeginWork(
        TideHeavyWreckState state,
        out bool started)
    {
        started = false;
        if (state.Phase == TideHeavyWreckPhase.RecoveredIntact)
        {
            state.Phase = TideHeavyWreckPhase.ExposingJoints;
            state.WorkProgress01 = 0f;
            started = true;
        }
        else if (state.Phase == TideHeavyWreckPhase.JointsExposed)
        {
            state.Phase = TideHeavyWreckPhase.Separating;
            state.WorkProgress01 = 0f;
            started = true;
        }

        return state;
    }

    public static TideHeavyWreckState AdvanceWork(
        TideHeavyWreckState state,
        float deltaSeconds,
        bool held)
    {
        if (!held || deltaSeconds <= 0f)
        {
            return state;
        }

        if (state.Phase == TideHeavyWreckPhase.ExposingJoints)
        {
            state.WorkProgress01 = Mathf.Clamp01(
                state.WorkProgress01 + deltaSeconds / ExposeJointSeconds);
            if (state.WorkProgress01 >= 0.999f)
            {
                state.WorkProgress01 = 0f;
                state.Phase = TideHeavyWreckPhase.JointsExposed;
            }
        }
        else if (state.Phase == TideHeavyWreckPhase.Separating)
        {
            state.WorkProgress01 = Mathf.Clamp01(
                state.WorkProgress01 + deltaSeconds / SeparateSeconds);
            if (state.WorkProgress01 >= 0.999f)
            {
                state.WorkProgress01 = 1f;
                state.Phase = TideHeavyWreckPhase.Separated;
            }
        }

        return state;
    }

    private static void AdvanceLooseOrSinglePoint(
        ref TideHeavyWreckState state,
        float deltaSeconds,
        bool afloat,
        float effectiveFlow,
        float flowLoad01)
    {
        if (!afloat)
        {
            state.Tension01 = 0f;
            state.OverstrainSeconds = Mathf.Max(0f, state.OverstrainSeconds - deltaSeconds);
            state.Phase = state.SecuredPointMask == 0
                ? TideHeavyWreckPhase.GroundedLoose
                : TideHeavyWreckPhase.OnePointSecured;
            return;
        }

        float constraintFactor = state.SecuredPointMask == 0 ? 1f : 0.24f;
        state.DriftMeters += effectiveFlow * constraintFactor * deltaSeconds;
        if (state.SecuredPointMask == 0)
        {
            state.Tension01 = 0f;
        }
        else
        {
            // 单点缆会让 U 形肋材绕系点扭转；持续强流最终磨断而不是凭空锁死。
            state.Tension01 = Mathf.Clamp01(0.34f + flowLoad01 * 0.78f);
            state.OverstrainSeconds = state.Tension01 >= CriticalTension01
                ? state.OverstrainSeconds + deltaSeconds
                : Mathf.Max(0f, state.OverstrainSeconds - deltaSeconds * 0.75f);
            if (state.OverstrainSeconds >= OnePointFailureSeconds)
            {
                state.SecuredPointMask = 0;
                state.OverstrainSeconds = 0f;
                state.Tension01 = 0f;
            }
        }

        if (Mathf.Abs(state.DriftMeters) >= MaximumLooseDriftMeters)
        {
            state.Phase = TideHeavyWreckPhase.Lost;
            return;
        }

        state.Phase = state.SecuredPointMask == 0
            ? TideHeavyWreckPhase.GroundedLoose
            : TideHeavyWreckPhase.OnePointSecured;
    }
}
