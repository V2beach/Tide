using System;

public enum TideV69HouseProfile
{
    Exterior,
    Interior
}

public enum TideV69HouseStructuralOwner
{
    Foundation,
    AccessLadder,
    RoofLeft,
    RoofRight,
    Lookout,
    Envelope
}

public enum TideV69HouseRepairStage
{
    Damage,
    Cleared,
    TestFit,
    Fastened,
    Sealed,
    Serviceable
}

public enum TideV69HouseBinaryOwner
{
    Workbench,
    EntryDoor,
    Bed,
    ChartRadio,
    Stove,
    LightAndHeat
}

/// <summary>
/// 把现有修柱、修屋面和封围护的提交步数，投影为 V69 六个结构 owner 的施工阶段。
/// 该模型不持有场景对象、材料或资源引用，内外视图必须消费同一次求值结果。
/// </summary>
public static class TideV69HouseRepairPresentationModel
{
    public const int CatalogVersion = 69;
    public const int StructuralOwnerCount = 6;
    public const int BinaryOwnerCount = 6;
    public const int StageCount = 6;
    public const float PixelsPerUnit = 256f;

    public static int GetRequiredStep(TideV69HouseStructuralOwner owner)
    {
        switch (owner)
        {
            case TideV69HouseStructuralOwner.Foundation:
            case TideV69HouseStructuralOwner.RoofLeft:
            case TideV69HouseStructuralOwner.Envelope:
                return 1;
            case TideV69HouseStructuralOwner.AccessLadder:
            case TideV69HouseStructuralOwner.RoofRight:
            case TideV69HouseStructuralOwner.Lookout:
                return 2;
            default:
                throw new ArgumentOutOfRangeException(nameof(owner), owner, "未知的 V69 结构 owner。");
        }
    }

    public static TideV69HouseRepairStage EvaluateStage(
        TideV69HouseStructuralOwner owner,
        int committedChannelSteps,
        bool activeRepairTargetsOwner,
        float repairProgress01)
    {
        int requiredStep = GetRequiredStep(owner);
        if (committedChannelSteps >= requiredStep)
        {
            return TideV69HouseRepairStage.Serviceable;
        }

        // 后续施工目标不能因为同一维修频道较早的一轮正在进行而提前变化。
        if (committedChannelSteps < requiredStep - 1 ||
            !activeRepairTargetsOwner || repairProgress01 <= 0f)
        {
            return TideV69HouseRepairStage.Damage;
        }

        float progress = UnityEngine.Mathf.Clamp01(repairProgress01);
        if (progress < 0.12f)
        {
            // 检查阶段只改变知识，不应在玩家摸到部件的一瞬间清空腐料。
            return TideV69HouseRepairStage.Damage;
        }
        if (progress < 0.3f)
        {
            return TideV69HouseRepairStage.Cleared;
        }
        if (progress < 0.56f)
        {
            return TideV69HouseRepairStage.TestFit;
        }
        if (progress < 0.78f)
        {
            return TideV69HouseRepairStage.Fastened;
        }

        // 玩法收益仍在施工提交时结算。进度末段只显示已经封缝但尚未复核的结构，
        // 避免画面先进入可用态而数值尚未生效。
        return TideV69HouseRepairStage.Sealed;
    }

    public static string GetProfileBaseResourcePath(TideV69HouseProfile profile)
    {
        return $"StiltFirstSliceAI/V69House/{profile}Base";
    }

    public static string GetStructuralStageResourcePath(
        TideV69HouseProfile profile,
        TideV69HouseStructuralOwner owner,
        TideV69HouseRepairStage stage)
    {
        return $"StiltFirstSliceAI/V69House/Structural/{profile}/{owner}_{(int)stage:00}_{stage}";
    }

    public static string GetBinaryStageResourcePath(
        TideV69HouseBinaryOwner owner,
        bool serviceable)
    {
        TideV69HouseRepairStage stage = serviceable
            ? TideV69HouseRepairStage.Serviceable
            : TideV69HouseRepairStage.Damage;
        return $"StiltFirstSliceAI/V69House/Binary/{owner}_{(int)stage:00}_{stage}";
    }
}
