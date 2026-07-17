using UnityEngine;

public readonly struct TideSailingWaveHandlingSample
{
    public TideSailingWaveHandlingSample(
        float handlingQuality01,
        float slamming01,
        float momentumDampingPerSecond,
        float ingressMultiplier)
    {
        HandlingQuality01 = handlingQuality01;
        Slamming01 = slamming01;
        MomentumDampingPerSecond = momentumDampingPerSecond;
        IngressMultiplier = ingressMultiplier;
    }

    public float HandlingQuality01 { get; }
    public float Slamming01 { get; }
    public float MomentumDampingPerSecond { get; }
    public float IngressMultiplier { get; }
}

/// <summary>
/// 玩家面对一组已经可见、正在接触船体的浪时，收帆和前后移动身体所形成的
/// 处理结果。模型不生成浪，也不读取输入；它只把唯一海况、船速和当前操船姿态
/// 转成动量损失与进水倍率，因而可被正式运行和确定性探针共同使用。
/// </summary>
public static class TideSailingWaveHandlingModel
{
    public const float BallastCounterPitchDegrees = 5.2f;

    public static TideSailingWaveHandlingSample Evaluate(
        float localWaveContact01,
        float waveAgitation01,
        float sampledSurfaceSlope,
        float ballast01,
        float sailRaised01,
        float boatVelocityMetersPerSecond,
        float waterVelocityMetersPerSecond)
    {
        float contact01 = Mathf.Clamp01(localWaveContact01);
        if (contact01 <= 0f)
        {
            return new TideSailingWaveHandlingSample(1f, 0f, 0f, 1f);
        }

        float surfacePitchDegrees = Mathf.Atan(sampledSurfaceSlope) * Mathf.Rad2Deg;
        float ballastCounterPitch = -Mathf.Clamp(ballast01, -1f, 1f) *
            BallastCounterPitchDegrees;
        float residualPitch01 = Mathf.Clamp01(
            Mathf.Abs(surfacePitchDegrees + ballastCounterPitch) / 10f);

        // 帆在平海面上仍然是收益；只有局部浪真正接触船体时，过大的帆面
        // 才会增加迎浪负荷。相对水速表达船与浪头相撞的强度。
        float rigExposure01 = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(0.18f, 0.92f, Mathf.Clamp01(sailRaised01)));
        float relativeWaterSpeed01 = Mathf.Clamp01(
            Mathf.InverseLerp(
                0.18f,
                2.4f,
                Mathf.Abs(boatVelocityMetersPerSecond - waterVelocityMetersPerSecond)));
        float unprepared01 = Mathf.Clamp01(
            residualPitch01 * 0.5f +
            rigExposure01 * 0.3f +
            relativeWaterSpeed01 * 0.2f);
        float handlingQuality01 = 1f - unprepared01;

        float contactEnergy01 = contact01 * Mathf.Lerp(
            0.5f,
            1f,
            Mathf.Clamp01(waveAgitation01));
        float slamming01 = Mathf.Clamp01(
            contactEnergy01 * Mathf.Lerp(0.12f, 1f, unprepared01));
        float momentumDampingPerSecond = slamming01 * Mathf.Lerp(
            0.12f,
            0.72f,
            unprepared01);
        float ingressMultiplier = Mathf.Lerp(1f, 2.9f, slamming01);
        return new TideSailingWaveHandlingSample(
            handlingQuality01,
            slamming01,
            momentumDampingPerSecond,
            ingressMultiplier);
    }
}
