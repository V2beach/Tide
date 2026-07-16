using UnityEngine;

[System.Serializable]
public struct TideSailboatDynamicsState
{
    public float HorizontalVelocity;
    public float HeaveY;
    public float HeaveVelocity;
    public float PitchDegrees;
    public float SailRaised01;
    public float Ballast01;
    public float Ingress01;
}

/// <summary>
/// 侧视短航动力模型。只模拟玩家能读懂的自由度：左右速度、随浪浮沉和越浪俯仰。
/// A/D 是有限的摇橹/舵控推进，不是隐藏传送带；帆只从真实有符号风中获得推力。
/// </summary>
public static class TideSailboatDynamicsModel
{
    public static TideSailboatDynamicsState Advance(
        TideSailboatDynamicsState state,
        float deltaSeconds,
        float pilotInput,
        float sailInput,
        float ballastInput,
        float signedWindMetersPerSecond,
        float signedCurrentMetersPerSecond,
        float sampledSurfaceY,
        float sampledSurfaceSlope,
        float waveAgitation01,
        float hullIntegrity01)
    {
        float dt = Mathf.Max(0f, deltaSeconds);
        state.SailRaised01 = Mathf.Clamp01(state.SailRaised01 + Mathf.Clamp(sailInput, -1f, 1f) * dt * 0.48f);
        state.Ballast01 = Mathf.Clamp(state.Ballast01 + Mathf.Clamp(ballastInput, -1f, 1f) * dt * 0.65f, -1f, 1f);

        float manualAcceleration = Mathf.Clamp(pilotInput, -1f, 1f) * 0.46f;
        float sailDrive = signedWindMetersPerSecond * Mathf.SmoothStep(0f, 1f, state.SailRaised01) *
            Mathf.Lerp(0.52f, 0.92f, Mathf.Clamp01(hullIntegrity01));
        float currentCoupling = (signedCurrentMetersPerSecond - state.HorizontalVelocity) * 0.34f;
        float quadraticDrag = -Mathf.Sign(state.HorizontalVelocity) *
            state.HorizontalVelocity * state.HorizontalVelocity * 0.12f;
        state.HorizontalVelocity += (manualAcceleration + sailDrive + currentCoupling + quadraticDrag) * dt;
        state.HorizontalVelocity = Mathf.Clamp(state.HorizontalVelocity, -2.8f, 2.8f);

        // Q/E 表示把现有压舱物沿船身前后移动，而不是凭空增加重量。
        // 它因此改变纵倾平衡点，但不会因为“移到最边上”就神奇地让船更稳。
        const float heaveResponse = 4.2f;
        const float heaveDamping = 4f;
        float heaveAcceleration = (sampledSurfaceY - state.HeaveY) * heaveResponse -
            state.HeaveVelocity * heaveDamping;
        state.HeaveVelocity += heaveAcceleration * dt;
        state.HeaveY += state.HeaveVelocity * dt;

        float slopePitch = Mathf.Atan(sampledSurfaceSlope) * Mathf.Rad2Deg;
        float ballastPitch = -state.Ballast01 * 5.2f;
        float targetPitch = Mathf.Clamp(slopePitch + ballastPitch, -12f, 12f);
        state.PitchDegrees = Mathf.Lerp(
            state.PitchDegrees,
            targetPitch,
            1f - Mathf.Exp(-3.2f * dt));

        float impact01 = Mathf.Clamp01(Mathf.Abs(state.HeaveVelocity) * 0.45f + waveAgitation01 * 0.55f);
        float leakRate = Mathf.Lerp(0.032f, 0.002f, Mathf.Clamp01(hullIntegrity01));
        state.Ingress01 = Mathf.Clamp01(state.Ingress01 + leakRate * impact01 * dt);
        return state;
    }
}
