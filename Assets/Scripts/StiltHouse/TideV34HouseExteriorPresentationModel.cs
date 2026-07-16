/// <summary>
/// V34 外景维修状态映射。这里仅决定六个美术 owner 应显示 Damage 还是 Repair，
/// 不持有场景对象，也不改变现有维修数值和材料消耗规则。
/// </summary>
public static class TideV34HouseExteriorPresentationModel
{
    public const int Version = 34;
    public const int OwnerCount = 6;
    public const float PixelsPerUnit = 256f;
    public const float CanvasPixels = 2048f;
    public const float PivotNormalizedY = 0.03125f;

    /// <summary>
    /// 地基、屋面、内室三条现有维修线各自推进自己的外景区域。施工预览计入
    /// steps，但在 50% 前保持 Damage，过半后互斥换成 Repair，避免双层重影。
    /// </summary>
    public static bool UseRepairSprite(
        string ownerKey,
        float foundationSteps,
        float roofSteps,
        float interiorSteps)
    {
        switch (ownerKey)
        {
            case "Foundation":
                return foundationSteps >= 0.5f;
            case "AccessLadder":
                return foundationSteps >= 1.5f;
            case "RoofLeft":
                return roofSteps >= 0.5f;
            case "RoofRight":
            case "Lookout":
                return roofSteps >= 1.5f;
            case "WallDeck":
                return interiorSteps >= 0.5f;
            default:
                return false;
        }
    }
}
