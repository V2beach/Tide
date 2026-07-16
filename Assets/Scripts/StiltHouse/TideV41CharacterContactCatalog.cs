using System;
using UnityEngine;

public enum TideV41CharacterContactAction
{
    Walk,
    CarryNetWalk,
    Board,
    TieNet,
    DoorEnter,
    LowerSinkline,
    Lookout,
}

/// <summary>
/// V41 人物接触动作的运行时索引。
///
/// 这里只保存正式 Sprite 的引用。动作资源拥有完整身体像素和逐帧接触姿态，
/// 世界位移、船体、码头、渔网和绳索仍由运行时负责，避免把可交互物烘焙进人物图。
/// </summary>
[CreateAssetMenu(
    menuName = "Tide/V41 Character Contact Catalog",
    fileName = "V41CharacterContactCatalog")]
public sealed class TideV41CharacterContactCatalog : ScriptableObject
{
    [SerializeField] private int version;
    [SerializeField] private Sprite[] walkFrames;
    [SerializeField] private Sprite[] carryNetWalkFrames;
    [SerializeField] private Sprite[] boardFrames;
    [SerializeField] private Sprite[] tieNetFrames;
    [SerializeField] private Sprite[] doorEnterFrames;
    [SerializeField] private Sprite[] lowerSinklineFrames;
    [SerializeField] private Sprite[] lookoutFrames;

    public int Version => version;
    public int TotalFrameCount =>
        GetLength(walkFrames) +
        GetLength(carryNetWalkFrames) +
        GetLength(boardFrames) +
        GetLength(tieNetFrames) +
        GetLength(doorEnterFrames) +
        GetLength(lowerSinklineFrames) +
        GetLength(lookoutFrames);

    public void Configure(
        Sprite[] walk,
        Sprite[] carryNetWalk,
        Sprite[] board,
        Sprite[] tieNet,
        Sprite[] doorEnter,
        Sprite[] lowerSinkline,
        Sprite[] lookout)
    {
        version = TideV41CharacterContactPresentationModel.CatalogVersion;
        walkFrames = walk;
        carryNetWalkFrames = carryNetWalk;
        boardFrames = board;
        tieNetFrames = tieNet;
        doorEnterFrames = doorEnter;
        lowerSinklineFrames = lowerSinkline;
        lookoutFrames = lookout;
    }

    public Sprite[] GetFrames(TideV41CharacterContactAction action)
    {
        switch (action)
        {
            case TideV41CharacterContactAction.Walk:
                return walkFrames;
            case TideV41CharacterContactAction.CarryNetWalk:
                return carryNetWalkFrames;
            case TideV41CharacterContactAction.Board:
                return boardFrames;
            case TideV41CharacterContactAction.TieNet:
                return tieNetFrames;
            case TideV41CharacterContactAction.DoorEnter:
                return doorEnterFrames;
            case TideV41CharacterContactAction.LowerSinkline:
                return lowerSinklineFrames;
            case TideV41CharacterContactAction.Lookout:
                return lookoutFrames;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    public Sprite GetFrame(TideV41CharacterContactAction action, int frameIndex)
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
        if (version != TideV41CharacterContactPresentationModel.CatalogVersion)
        {
            reason = $"索引版本是 {version}，不是 V41";
            return false;
        }

        foreach (TideV41CharacterContactAction action in
                 Enum.GetValues(typeof(TideV41CharacterContactAction)))
        {
            Sprite[] frames = GetFrames(action);
            int expected = TideV41CharacterContactPresentationModel.GetFrameCount(action);
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

        if (TotalFrameCount != TideV41CharacterContactPresentationModel.TotalFrameCount)
        {
            reason = $"人物接触动作总帧数是 {TotalFrameCount}，不是 48";
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
