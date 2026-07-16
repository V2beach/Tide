using System;
using UnityEngine;

/// <summary>
/// V32 只保存本轮明确换新的外景房屋、外梯人物和船上乘员。
///
/// 船体前后层、帆桅、舵和水线仍由 V31 负责，室内维修仍由 V30 负责，
/// 这样每个可动画部件只有一个资源所有者，不会重新出现整图与切件双显。
/// </summary>
[CreateAssetMenu(
    menuName = "Tide/V32 First Slice Art Catalog",
    fileName = "V32FirstSliceArtCatalog")]
public sealed class TideV32FirstSliceArtCatalog : ScriptableObject
{
    public const int CatalogVersion = 32;
    public const int ClimbFrameCount = 6;
    public const int SeatedFrameCount = 6;

    [SerializeField] private int version;
    [SerializeField] private Sprite houseFound;
    [SerializeField] private Sprite houseRepaired;
    [SerializeField] private Sprite[] climbFrames;
    [SerializeField] private Sprite[] seatedFrames;
    [SerializeField] private float climbFrameSeconds;
    [SerializeField] private float seatedFrameSeconds;
    [SerializeField] private float climbUniformScale;
    [SerializeField] private float seatedUniformScale;

    public int Version => version;
    public Sprite HouseFound => houseFound;
    public Sprite HouseRepaired => houseRepaired;
    public Sprite[] ClimbFrames => climbFrames;
    public Sprite[] SeatedFrames => seatedFrames;
    public float ClimbFrameSeconds => climbFrameSeconds;
    public float SeatedFrameSeconds => seatedFrameSeconds;
    public float ClimbUniformScale => climbUniformScale;
    public float SeatedUniformScale => seatedUniformScale;

    public void Configure(
        Sprite found,
        Sprite repaired,
        Sprite[] climb,
        Sprite[] seated,
        float climbSeconds,
        float seatedSeconds,
        float climbScale,
        float seatedScale)
    {
        version = CatalogVersion;
        houseFound = found;
        houseRepaired = repaired;
        climbFrames = climb;
        seatedFrames = seated;
        climbFrameSeconds = climbSeconds;
        seatedFrameSeconds = seatedSeconds;
        climbUniformScale = climbScale;
        seatedUniformScale = seatedScale;
    }

    public Sprite GetHouseEndpoint(float restoration01)
    {
        return restoration01 >= 0.72f ? houseRepaired : houseFound;
    }

    public Sprite GetClimbFrame(float normalizedProgress)
    {
        return GetProgressFrame(climbFrames, normalizedProgress);
    }

    public Sprite GetSeatedFrame(float worldTime)
    {
        if (seatedFrames == null || seatedFrames.Length == 0)
        {
            return null;
        }

        float seconds = Mathf.Max(0.01f, seatedFrameSeconds);
        int index = Mathf.FloorToInt(Mathf.Max(0f, worldTime) / seconds) % seatedFrames.Length;
        return seatedFrames[index];
    }

    /// <summary>
    /// 船上没有操作时必须保持稳定坐姿。六张坐姿图只有细小轮廓差异，按世界
    /// 时间高速循环会像人物不断抽动；真正的操帆、舀水和受击由 V37 单独接管。
    /// </summary>
    public Sprite GetStableSeatedFrame()
    {
        return seatedFrames != null && seatedFrames.Length > 0
            ? seatedFrames[0]
            : null;
    }

    public bool IsComplete(out string reason)
    {
        if (version != CatalogVersion)
        {
            reason = $"索引版本是 {version}，不是 V32";
            return false;
        }

        if (houseFound == null || houseRepaired == null)
        {
            reason = "高脚屋发现态或修复态缺失";
            return false;
        }

        if (!HasCompleteFrames(climbFrames, ClimbFrameCount) ||
            !HasCompleteFrames(seatedFrames, SeatedFrameCount))
        {
            reason = "爬梯或坐船六帧不完整";
            return false;
        }

        if (climbFrameSeconds <= 0f || seatedFrameSeconds <= 0f ||
            climbUniformScale <= 0f || seatedUniformScale <= 0f)
        {
            reason = "动作帧时长或统一缩放无效";
            return false;
        }

        reason = "完整";
        return true;
    }

    private static Sprite GetProgressFrame(Sprite[] frames, float normalizedProgress)
    {
        if (frames == null || frames.Length == 0)
        {
            return null;
        }

        float progress = Mathf.Clamp01(normalizedProgress);
        int index = Mathf.Min(
            frames.Length - 1,
            Mathf.FloorToInt(progress * frames.Length));
        return frames[index];
    }

    private static bool HasCompleteFrames(Sprite[] frames, int expectedCount)
    {
        if (frames == null || frames.Length != expectedCount)
        {
            return false;
        }

        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] == null)
            {
                return false;
            }
        }

        return true;
    }
}
