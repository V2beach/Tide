using UnityEngine;

/// <summary>
/// 渔网在一整潮内承受海力的确定性账本。模型只累计已经发生的受力，
/// 因此抬网能减少未来负担，却不能把此前的深网收益和损耗一起洗掉。
/// </summary>
public static class TideNetLoadLedgerModel
{
    // The continuous tide now keeps a deployed net in moving water for roughly fifty
    // real seconds. These thresholds therefore represent a natural-tide impulse budget,
    // not the old seven-second prototype encounter. With the current opening tide a
    // shallow/mid/deep set reaches about 20/37/55 impulse at the first catch: that leaves
    // shallow water forgiving, mid water damaged but usable, and deep water close to the
    // final strand without forcing every player to camp beside the post.
    public const float StressTierOneImpulse = 24f;
    public const float StressTierTwoImpulse = 44f;
    public const float StressTierThreeImpulse = 76f;

    // A forecast describes leaving the net through the useful part of one natural tide.
    // Contact, current and cargo are already represented by the sample rate below; this
    // duration is intentionally longer than the raw wet seconds so the lookout remains
    // conservative once later cargo increases drag.
    public const float RepresentativeFullTideContactSeconds = 96f;

    public static float EvaluateImpulseRate(
        float contact01,
        float depth01,
        float tideStrength01,
        float currentStrength01,
        float weatherLoadMultiplier,
        float materialLoadMultiplier,
        float cargoLoad01,
        bool routedExtraLoad)
    {
        float contact = Mathf.Clamp01(contact01);
        float depth = Mathf.Clamp01(depth01);
        float tide = Mathf.Lerp(0.48f, 1.18f, Mathf.Clamp01(tideStrength01));
        float current = Mathf.Lerp(0.24f, 1f, Mathf.Clamp01(currentStrength01));
        float depthLoad = Mathf.Lerp(0.32f, 1.42f, Mathf.Pow(depth, 1.2f));
        float weather = Mathf.Clamp(weatherLoadMultiplier, 0.55f, 1.85f);
        float material = Mathf.Clamp(materialLoadMultiplier, 0.55f, 1.9f);
        float cargo = Mathf.Lerp(1f, 1.48f, Mathf.Clamp01(cargoLoad01));
        float route = routedExtraLoad ? 1.18f : 1f;
        return contact * depthLoad * tide * current * weather * material * cargo * route;
    }

    public static float AdvanceImpulse(
        float currentImpulse,
        float deltaTime,
        float contact01,
        float depth01,
        float tideStrength01,
        float currentStrength01,
        float weatherLoadMultiplier,
        float materialLoadMultiplier,
        float cargoLoad01,
        bool routedExtraLoad)
    {
        float rate = EvaluateImpulseRate(
            contact01,
            depth01,
            tideStrength01,
            currentStrength01,
            weatherLoadMultiplier,
            materialLoadMultiplier,
            cargoLoad01,
            routedExtraLoad);
        return Mathf.Max(0f, currentImpulse) + rate * Mathf.Max(0f, deltaTime);
    }

    public static int EvaluateCommittedStress(float impulse, float peakLoad01, bool reinforcedRope)
    {
        float effectiveImpulse = Mathf.Max(0f, impulse) + Mathf.Clamp01(peakLoad01) * 0.9f;
        int stress = effectiveImpulse >= StressTierThreeImpulse
            ? 3
            : effectiveImpulse >= StressTierTwoImpulse
                ? 2
                : effectiveImpulse >= StressTierOneImpulse ? 1 : 0;
        return reinforcedRope ? Mathf.Max(0, stress - 1) : stress;
    }

    public static float EvaluateFatigue01(float impulse, float peakLoad01)
    {
        float effectiveImpulse = Mathf.Max(0f, impulse) + Mathf.Clamp01(peakLoad01) * 0.9f;
        return Mathf.InverseLerp(0f, StressTierThreeImpulse, effectiveImpulse);
    }

    public static float EstimateFullTideImpulse(
        float depth01,
        float tideStrength01,
        float weatherLoadMultiplier)
    {
        float representativeRate = EvaluateImpulseRate(
            0.74f,
            depth01,
            tideStrength01,
            0.66f,
            weatherLoadMultiplier,
            1f,
            0.35f,
            false);
        return representativeRate * RepresentativeFullTideContactSeconds;
    }
}
