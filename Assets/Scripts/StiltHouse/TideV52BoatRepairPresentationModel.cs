using System;
using UnityEngine;

/// <summary>
/// V52 三个互不替代的船体维修部位。枚举顺序也是运行探针和资源索引的稳定顺序。
/// </summary>
public enum TideV52BoatRepairOwner
{
    HullBreach,
    SailRepair,
    CockpitFloor
}
/// <summary>
/// V52 资源已经画好的六个真实施工状态。禁止用透明度插值伪造中间施工结果。
/// </summary>
public enum TideV52BoatRepairStage
{
    Damage,
    Cleared,
    TestFit,
    Fastened,
    Sealed,
    Serviceable
}

/// <summary>
/// 把玩法侧的两轮维修，投影到 V52 六个施工阶段。
///
/// 第一轮处理最危险的破损，依次完成清理、试装和固定；第二轮才做密封与可用性
/// 复核。这样每次材料投入都改变真实结构，同时不会因为接入美术而另造一套维修账本。
/// </summary>
public static class TideV52BoatRepairPresentationModel
{
    public const int CatalogVersion = 67;
    public const int OwnerCount = 3;
    public const int StageCount = 6;
    public const float PixelsPerUnit = 192f;
    public const float BoatRootScale = 0.42f;

    public const string BaseResourcePath =
        "StiltFirstSliceAI/V52BoatRepair/V67BalancedBase";

    private static readonly Vector2[] OwnerOffsets =
    {
        new Vector2(-0.041667f, 1.377188f),
        new Vector2(1.46875f, 4.262604f),
        new Vector2(0.5f, 2.205313f)
    };

    public static readonly Vector2 FrontGunwaleStableOffset =
        new Vector2(-0.088542f, 1.543854f);

    public static readonly Vector2 CockpitFloorStableOffset =
        new Vector2(-0.315104f, 2.205313f);

    public static TideV52BoatRepairStage EvaluateStage(
        TideV52BoatRepairOwner owner,
        int committedLevel,
        bool activeRepair,
        float repairProgress01)
    {
        TideV52BoatRepairStage committedStage = GetCommittedStage(owner, committedLevel);
        if (!activeRepair || repairProgress01 <= 0f || IsMaximumLevel(owner, committedLevel))
        {
            return committedStage;
        }

        // 第一轮必须保留三个可辨认的物理动作；0.34/0.72 与控制器现有的
        // 检查清理、试装、固定密封三段工作指令保持同一节拍。
        float progress = Mathf.Clamp01(repairProgress01);
        if (committedStage == TideV52BoatRepairStage.Damage)
        {
            if (progress < 0.34f)
            {
                return TideV52BoatRepairStage.Cleared;
            }

            return progress < 0.72f
                ? TideV52BoatRepairStage.TestFit
                : TideV52BoatRepairStage.Fastened;
        }

        // 第二轮从已经固定的补件继续处理。前半段做密封，最后一段才进入
        // 可用态；施工完成前玩法属性仍由旧 level 决定，不会提前获得收益。
        return progress < 0.72f
            ? TideV52BoatRepairStage.Sealed
            : TideV52BoatRepairStage.Serviceable;
    }

    public static TideV52BoatRepairStage GetCommittedStage(
        TideV52BoatRepairOwner owner,
        int committedLevel)
    {
        int minimum = GetMinimumLevel(owner);
        int maximum = GetMaximumLevel(owner);
        int clamped = Mathf.Clamp(committedLevel, minimum, maximum);
        int completedRounds = clamped - minimum;
        if (completedRounds <= 0)
        {
            return TideV52BoatRepairStage.Damage;
        }

        return completedRounds == 1
            ? TideV52BoatRepairStage.Fastened
            : TideV52BoatRepairStage.Serviceable;
    }

    public static int GetMinimumLevel(TideV52BoatRepairOwner owner)
    {
        return owner == TideV52BoatRepairOwner.HullBreach ? 1 : 0;
    }

    public static int GetMaximumLevel(TideV52BoatRepairOwner owner)
    {
        return owner == TideV52BoatRepairOwner.HullBreach ? 3 : 2;
    }

    public static bool IsMaximumLevel(TideV52BoatRepairOwner owner, int level)
    {
        return level >= GetMaximumLevel(owner);
    }

    public static Vector2 GetOwnerOffset(TideV52BoatRepairOwner owner)
    {
        int index = (int)owner;
        if (index < 0 || index >= OwnerOffsets.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(owner), owner, "未知的 V52 维修 owner。");
        }

        return OwnerOffsets[index];
    }

    public static string GetStageResourcePath(
        TideV52BoatRepairOwner owner,
        TideV52BoatRepairStage stage)
    {
        return $"StiltFirstSliceAI/V52BoatRepair/{owner}_{(int)stage:00}_{stage}";
    }
}
