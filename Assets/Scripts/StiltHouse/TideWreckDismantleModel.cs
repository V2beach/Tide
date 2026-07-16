using System;
using UnityEngine;

public enum TideWreckDismantleBlockReason
{
    None,
    NoTarget,
    Released,
    NoStableFooting,
    BreakingWave
}

[Serializable]
public struct TideWreckDismantleStep
{
    public float Progress01;
    public float WorkRate01;
    public bool Worked;
    public bool Completed;
    public TideWreckDismantleBlockReason BlockReason;
}

/// <summary>
/// 海难船骸的连续拆卸公式。它只读取同一片海提供的水深和局部浪载，
/// 不知道 Input、Renderer 或库存，因此场景、编辑器探针和以后存档可复用同一规则。
/// </summary>
public static class TideWreckDismantleModel
{
    public const float MaximumWorkableWaterDepthMeters = 0.42f;
    public const float BreakingWaveLoadThreshold01 = 0.82f;

    public static float GetRequiredWorkSeconds(TideIslandSalvagePart part)
    {
        // 帆布只需沿残存边绳割开；外板要逐枚撬钉；铆接板最重且要避免
        // 把仍可用的边缘撕裂。这里使用现实秒，不随八分钟游戏日缩放。
        return part == TideIslandSalvagePart.Sailcloth ? 3.2f :
            part == TideIslandSalvagePart.HullPlank ? 4.8f :
            part == TideIslandSalvagePart.RivetedPlate ? 6.2f : 0f;
    }

    public static TideWreckDismantleStep Advance(
        TideIslandSalvagePart part,
        float currentProgress01,
        float deltaSeconds,
        bool held,
        bool hasStableFooting,
        float localWaterDepthMeters,
        float localWaveLoad01)
    {
        float progress01 = Mathf.Clamp01(currentProgress01);
        if (part == TideIslandSalvagePart.None || GetRequiredWorkSeconds(part) <= 0f)
        {
            return Blocked(progress01, TideWreckDismantleBlockReason.NoTarget);
        }

        if (!held)
        {
            return Blocked(progress01, TideWreckDismantleBlockReason.Released);
        }

        float waterDepth = Mathf.Max(0f, localWaterDepthMeters);
        if (!hasStableFooting || waterDepth > MaximumWorkableWaterDepthMeters)
        {
            return Blocked(progress01, TideWreckDismantleBlockReason.NoStableFooting);
        }

        float waveLoad01 = Mathf.Clamp01(localWaveLoad01);
        if (waveLoad01 >= BreakingWaveLoadThreshold01)
        {
            return Blocked(progress01, TideWreckDismantleBlockReason.BreakingWave);
        }

        // 脚边进水和浪载不会凭空抹掉已经撬松的钉，但会降低有效工作速率。
        // 真正失去支撑或遇到破浪时上面的硬门会暂停作业。
        float depthRate01 = Mathf.Lerp(
            1f,
            0.46f,
            Mathf.InverseLerp(0.05f, MaximumWorkableWaterDepthMeters, waterDepth));
        float waveRate01 = Mathf.Lerp(
            1f,
            0.58f,
            Mathf.InverseLerp(0.08f, BreakingWaveLoadThreshold01, waveLoad01));
        float workRate01 = depthRate01 * waveRate01;
        float nextProgress01 = Mathf.Clamp01(
            progress01 + Mathf.Max(0f, deltaSeconds) * workRate01 / GetRequiredWorkSeconds(part));

        return new TideWreckDismantleStep
        {
            Progress01 = nextProgress01,
            WorkRate01 = workRate01,
            Worked = nextProgress01 > progress01 + 0.000001f,
            Completed = nextProgress01 >= 0.9999f,
            BlockReason = TideWreckDismantleBlockReason.None
        };
    }

    private static TideWreckDismantleStep Blocked(
        float progress01,
        TideWreckDismantleBlockReason reason)
    {
        return new TideWreckDismantleStep
        {
            Progress01 = progress01,
            WorkRate01 = 0f,
            Worked = false,
            Completed = false,
            BlockReason = reason
        };
    }
}
