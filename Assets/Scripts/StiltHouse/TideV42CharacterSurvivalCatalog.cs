using System;
using UnityEngine;

public enum TideV42CharacterSurvivalAction
{
    ColdShiver,
    Sleep,
    Drown,
    ColdCollapse,
}

/// <summary>
/// V42 生存动作的轻量运行索引。
///
/// 资源只拥有完整人物像素和逐帧身体姿势。床、水面、根运动、淡出、死亡惩罚
/// 与复活位置仍由世界状态机负责，避免把环境重复烘焙进人物图。
/// </summary>
[CreateAssetMenu(
    menuName = "Tide/V42 Character Survival Catalog",
    fileName = "V42CharacterSurvivalCatalog")]
public sealed class TideV42CharacterSurvivalCatalog : ScriptableObject
{
    [SerializeField] private int version;
    [SerializeField] private Sprite[] coldShiverFrames;
    [SerializeField] private Sprite[] sleepFrames;
    [SerializeField] private Sprite[] drownFrames;
    [SerializeField] private Sprite[] coldCollapseFrames;

    public int Version => version;
    public int TotalFrameCount =>
        GetLength(coldShiverFrames) +
        GetLength(sleepFrames) +
        GetLength(drownFrames) +
        GetLength(coldCollapseFrames);

    public void Configure(
        Sprite[] coldShiver,
        Sprite[] sleep,
        Sprite[] drown,
        Sprite[] coldCollapse)
    {
        version = TideV42CharacterSurvivalPresentationModel.CatalogVersion;
        coldShiverFrames = coldShiver;
        sleepFrames = sleep;
        drownFrames = drown;
        coldCollapseFrames = coldCollapse;
    }

    public Sprite[] GetFrames(TideV42CharacterSurvivalAction action)
    {
        switch (action)
        {
            case TideV42CharacterSurvivalAction.ColdShiver:
                return coldShiverFrames;
            case TideV42CharacterSurvivalAction.Sleep:
                return sleepFrames;
            case TideV42CharacterSurvivalAction.Drown:
                return drownFrames;
            case TideV42CharacterSurvivalAction.ColdCollapse:
                return coldCollapseFrames;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    public Sprite GetFrame(TideV42CharacterSurvivalAction action, int frameIndex)
    {
        Sprite[] frames = GetFrames(action);
        if (frames == null || frames.Length == 0)
        {
            return null;
        }

        int wrappedIndex = ((frameIndex % frames.Length) + frames.Length) % frames.Length;
        return frames[wrappedIndex];
    }

    public bool IsComplete(out string reason)
    {
        if (version != TideV42CharacterSurvivalPresentationModel.CatalogVersion)
        {
            reason = $"索引版本是 {version}，不是 V42";
            return false;
        }

        foreach (TideV42CharacterSurvivalAction action in
                 Enum.GetValues(typeof(TideV42CharacterSurvivalAction)))
        {
            Sprite[] frames = GetFrames(action);
            int expected = TideV42CharacterSurvivalPresentationModel.GetFrameCount(action);
            if (frames == null || frames.Length != expected)
            {
                reason = $"{action} 帧数是 {(frames == null ? 0 : frames.Length)}，不是 {expected}";
                return false;
            }

            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i] == null)
                {
                    reason = $"{action} 第 {i} 帧缺失";
                    return false;
                }
            }
        }

        if (TotalFrameCount != TideV42CharacterSurvivalPresentationModel.TotalFrameCount)
        {
            reason = $"生存动作总帧数是 {TotalFrameCount}，不是 30";
            return false;
        }

        reason = "完整";
        return true;
    }

    private static int GetLength(Sprite[] sprites)
    {
        return sprites == null ? 0 : sprites.Length;
    }
}
