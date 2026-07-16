using System;
using UnityEngine;

/// <summary>
/// V20 正式人物动作的轻量运行索引。
///
/// Catalog 只保存对正式 Sprite 的引用，不复制 PNG，也不让运行时代码依赖 AssetDatabase。
/// </summary>
[CreateAssetMenu(menuName = "Tide/V20 Character Runtime Catalog", fileName = "V20CharacterRuntimeCatalog")]
public sealed class TideV20CharacterRuntimeCatalog : ScriptableObject
{
    [SerializeField] private int version;
    [SerializeField] private Sprite[] idleFrames;
    [SerializeField] private Sprite[] walkFrames;
    [SerializeField] private Sprite[] swimFrames;
    [SerializeField] private Sprite[] repairFrames;
    [SerializeField] private Sprite[] haulFrames;

    public int Version => version;
    public Sprite[] IdleFrames => idleFrames;
    public Sprite[] WalkFrames => walkFrames;
    public Sprite[] SwimFrames => swimFrames;
    public Sprite[] RepairFrames => repairFrames;
    public Sprite[] HaulFrames => haulFrames;

    public int TotalFrameCount =>
        GetLength(idleFrames) +
        GetLength(walkFrames) +
        GetLength(swimFrames) +
        GetLength(repairFrames) +
        GetLength(haulFrames);

    public void Configure(
        int catalogVersion,
        Sprite[] idle,
        Sprite[] walk,
        Sprite[] swim,
        Sprite[] repair,
        Sprite[] haul)
    {
        version = catalogVersion;
        idleFrames = idle;
        walkFrames = walk;
        swimFrames = swim;
        repairFrames = repair;
        haulFrames = haul;
    }

    public Sprite[] GetFrames(TideV20CharacterActionState actionState)
    {
        switch (actionState)
        {
            case TideV20CharacterActionState.Idle:
                return idleFrames;
            case TideV20CharacterActionState.Walk:
                return walkFrames;
            case TideV20CharacterActionState.Swim:
                return swimFrames;
            case TideV20CharacterActionState.Repair:
                return repairFrames;
            case TideV20CharacterActionState.Haul:
                return haulFrames;
            default:
                throw new ArgumentOutOfRangeException(nameof(actionState), actionState, null);
        }
    }

    public Sprite GetFrame(TideV20CharacterActionState actionState, int frameIndex)
    {
        Sprite[] frames = GetFrames(actionState);
        if (frames == null || frames.Length == 0)
        {
            return null;
        }

        int wrappedIndex = ((frameIndex % frames.Length) + frames.Length) % frames.Length;
        return frames[wrappedIndex];
    }

    public bool IsComplete(out string reason)
    {
        if (version != TideV20CharacterPresentationModel.CatalogVersion)
        {
            reason = $"索引版本是 {version}，不是 V20";
            return false;
        }

        if (!HasCompleteSpriteArray(
            idleFrames,
            TideV20CharacterPresentationModel.IdleFrameCount,
            "Idle",
            out reason))
        {
            return false;
        }

        if (!HasCompleteSpriteArray(
            walkFrames,
            TideV20CharacterPresentationModel.WalkFrameCount,
            "Walk",
            out reason))
        {
            return false;
        }

        if (!HasCompleteSpriteArray(
            swimFrames,
            TideV20CharacterPresentationModel.SwimFrameCount,
            "Swim",
            out reason))
        {
            return false;
        }

        if (!HasCompleteSpriteArray(
            repairFrames,
            TideV20CharacterPresentationModel.RepairFrameCount,
            "Repair",
            out reason))
        {
            return false;
        }

        if (!HasCompleteSpriteArray(
            haulFrames,
            TideV20CharacterPresentationModel.HaulFrameCount,
            "Haul",
            out reason))
        {
            return false;
        }

        if (TotalFrameCount != TideV20CharacterPresentationModel.TotalFrameCount)
        {
            reason = $"人物总帧数是 {TotalFrameCount}，不是 28";
            return false;
        }

        reason = "完整";
        return true;
    }

    private static bool HasCompleteSpriteArray(
        Sprite[] sprites,
        int expectedCount,
        string actionName,
        out string reason)
    {
        if (sprites == null || sprites.Length != expectedCount)
        {
            int actualCount = sprites == null ? 0 : sprites.Length;
            reason = $"{actionName} 帧数是 {actualCount}，不是 {expectedCount}";
            return false;
        }

        for (int i = 0; i < sprites.Length; i++)
        {
            if (sprites[i] == null)
            {
                reason = $"{actionName} 第 {i} 帧缺失";
                return false;
            }
        }

        reason = null;
        return true;
    }

    private static int GetLength(Sprite[] sprites)
    {
        return sprites == null ? 0 : sprites.Length;
    }
}
