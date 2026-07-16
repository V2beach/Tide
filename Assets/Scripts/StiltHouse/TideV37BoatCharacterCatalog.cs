using UnityEngine;

public enum TideV37BoatCharacterAction
{
    Trim,
    Bail,
    Brace
}

/// <summary>
/// V37 只拥有船上人物的三组专用动作。
///
/// V31 继续拥有船体前后层，V32 继续拥有普通坐姿，桶和水花仍由运行时独立
/// Renderer 拥有。这样人物可以完整夹在船舷中间，同时不会把船具烘焙两次。
/// </summary>
[CreateAssetMenu(
    menuName = "Tide/V37 Boat Character Catalog",
    fileName = "V37BoatCharacterCatalog")]
public sealed class TideV37BoatCharacterCatalog : ScriptableObject
{
    public const int CatalogVersion = 37;
    public const int FrameCount = 6;

    [SerializeField] private int version;
    [SerializeField] private Sprite[] trimFrames;
    [SerializeField] private Sprite[] bailFrames;
    [SerializeField] private Sprite[] braceFrames;
    [SerializeField] private Sprite bailingBucketSprite;
    [SerializeField] private float uniformScale;

    public int Version => version;
    public Sprite[] TrimFrames => trimFrames;
    public Sprite[] BailFrames => bailFrames;
    public Sprite[] BraceFrames => braceFrames;
    public Sprite BailingBucketSprite => bailingBucketSprite;
    public float UniformScale => uniformScale;

    public void Configure(
        Sprite[] trim,
        Sprite[] bail,
        Sprite[] brace,
        Sprite bailingBucket,
        float characterUniformScale)
    {
        version = CatalogVersion;
        trimFrames = trim;
        bailFrames = bail;
        braceFrames = brace;
        bailingBucketSprite = bailingBucket;
        uniformScale = characterUniformScale;
    }

    public Sprite GetFrame(TideV37BoatCharacterAction action, float normalizedCycle01)
    {
        Sprite[] frames = GetFrames(action);
        if (frames == null || frames.Length == 0)
        {
            return null;
        }

        float cycle = Mathf.Repeat(normalizedCycle01, 1f);
        int frameIndex = Mathf.Min(
            frames.Length - 1,
            Mathf.FloorToInt(cycle * frames.Length));
        return frames[frameIndex];
    }

    public bool IsComplete(out string reason)
    {
        if (version != CatalogVersion)
        {
            reason = $"索引版本是 {version}，不是 V37";
            return false;
        }

        if (!HasCompleteFrames(trimFrames) ||
            !HasCompleteFrames(bailFrames) ||
            !HasCompleteFrames(braceFrames))
        {
            reason = "操帆、舀水或暴潮受击六帧不完整";
            return false;
        }

        if (bailingBucketSprite == null)
        {
            reason = "缺少独立的小型船用舀水桶";
            return false;
        }

        if (uniformScale <= 0f)
        {
            reason = "人物统一缩放无效";
            return false;
        }

        reason = "完整";
        return true;
    }

    private Sprite[] GetFrames(TideV37BoatCharacterAction action)
    {
        if (action == TideV37BoatCharacterAction.Bail)
        {
            return bailFrames;
        }

        if (action == TideV37BoatCharacterAction.Brace)
        {
            return braceFrames;
        }

        return trimFrames;
    }

    private static bool HasCompleteFrames(Sprite[] frames)
    {
        if (frames == null || frames.Length != FrameCount)
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
