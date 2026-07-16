public enum TideRepairTarget
{
    None,
    Stilt,
    Cistern,
    Sail,
    Lamp,
    Roof,
    InteriorSeal,
    Workbench,
    Bed,
    ChartRadio,
    Hull,
    Cabin,
    Net
}

/// <summary>
/// 维修目标的纯配方合同。它只回答“原物能去哪”和“这一阶段需要什么”，不读取
/// 场景、输入、时间或库存，也不负责消费材料。所有最终 owner 仍由运行控制器提交。
/// </summary>
public static class TideRepairRecipeModel
{
    public static TideRepairTarget GetArrivalRepairTarget(
        TideIslandSalvagePart part,
        TideIslandSalvageUse use)
    {
        bool repairsEscapeBoat = use == TideIslandSalvageUse.EscapeBoat;
        if (part == TideIslandSalvagePart.HullPlank)
        {
            return repairsEscapeBoat ? TideRepairTarget.Hull : TideRepairTarget.Stilt;
        }
        if (part == TideIslandSalvagePart.Sailcloth)
        {
            return repairsEscapeBoat ? TideRepairTarget.Sail : TideRepairTarget.Net;
        }
        if (part == TideIslandSalvagePart.RivetedPlate)
        {
            return repairsEscapeBoat ? TideRepairTarget.Cabin : TideRepairTarget.Cistern;
        }
        return TideRepairTarget.None;
    }

    public static TideIslandSalvageDestination GetStagingDestination(TideRepairTarget target)
    {
        if (target == TideRepairTarget.None)
        {
            return TideIslandSalvageDestination.None;
        }

        bool repairsEscapeBoat = target == TideRepairTarget.Hull ||
            target == TideRepairTarget.Sail ||
            target == TideRepairTarget.Cabin;
        return repairsEscapeBoat
            ? TideIslandSalvageDestination.EscapeBoatStaging
            : TideIslandSalvageDestination.ShelterStaging;
    }

    public static TideMaterialBundle GetMaterialNeeds(
        TideRepairTarget target,
        int currentBoatCabinIntegrity)
    {
        if (target == TideRepairTarget.Stilt || target == TideRepairTarget.Hull)
        {
            return new TideMaterialBundle(2, 1, 0, 0, 0);
        }
        if (target == TideRepairTarget.Cistern)
        {
            // 一整块铆接板被剪成补片、压条和铆钉，不留下不可见的半块库存。
            return new TideMaterialBundle(0, 0, 0, 2, 0);
        }
        if (target == TideRepairTarget.Net)
        {
            return new TideMaterialBundle(0, 1, 0, 0, 0);
        }
        if (target == TideRepairTarget.Roof)
        {
            return new TideMaterialBundle(1, 1, 0, 0, 0);
        }
        if (target == TideRepairTarget.InteriorSeal)
        {
            return new TideMaterialBundle(1, 0, 1, 0, 0);
        }
        if (target == TideRepairTarget.Workbench)
        {
            return new TideMaterialBundle(1, 0, 0, 0, 0);
        }
        if (target == TideRepairTarget.Bed)
        {
            return new TideMaterialBundle(0, 0, 1, 0, 0);
        }
        if (target == TideRepairTarget.ChartRadio)
        {
            return new TideMaterialBundle(0, 0, 0, 1, 0);
        }
        if (target == TideRepairTarget.Lamp)
        {
            return new TideMaterialBundle(0, 0, 0, 0, 1);
        }
        if (target == TideRepairTarget.Sail)
        {
            return new TideMaterialBundle(0, 1, 1, 0, 0);
        }
        if (target == TideRepairTarget.Cabin)
        {
            // 第一阶段是纯金属舱盖、排水护板和压舱隔片；第二阶段才补木框。
            return currentBoatCabinIntegrity <= 0
                ? new TideMaterialBundle(0, 0, 0, 2, 0)
                : new TideMaterialBundle(1, 0, 0, 1, 0);
        }
        return new TideMaterialBundle();
    }
}
