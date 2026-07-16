public enum TideRepairWorkPhase
{
    None,
    Inspect,
    Clean,
    TestFit,
    Fasten,
    Seal
}

/// <summary>
/// 所有修屋和修船工作共用的现实工序节拍。模型只把连续施工进度解释成动作，
/// 不拥有材料、输入或 Renderer；视觉资源可按自身可用帧数合并相邻工序。
/// </summary>
public static class TideRepairWorkPhaseModel
{
    public static TideRepairWorkPhase Evaluate(float progress01)
    {
        if (progress01 < 0f)
        {
            return TideRepairWorkPhase.None;
        }
        if (progress01 < 0.12f)
        {
            return TideRepairWorkPhase.Inspect;
        }
        if (progress01 < 0.3f)
        {
            return TideRepairWorkPhase.Clean;
        }
        if (progress01 < 0.56f)
        {
            return TideRepairWorkPhase.TestFit;
        }
        if (progress01 < 0.78f)
        {
            return TideRepairWorkPhase.Fasten;
        }
        return TideRepairWorkPhase.Seal;
    }
}
