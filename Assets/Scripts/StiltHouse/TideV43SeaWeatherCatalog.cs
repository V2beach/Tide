using UnityEngine;

public enum TideV43WaveKind
{
    LongSwell,
    WindWave,
    StormBreaker,
}

public enum TideV43VortexLayer
{
    DepressionMask,
    InnerFlow,
    OuterFoam,
}

public enum TideV43CloudLayer
{
    FarCloudWall,
    MidWeatherBank,
    NearScud,
}

/// <summary>
/// V43 海况透明层的运行索引。
///
/// Catalog 不拥有连续海体、水位、风力或昼夜颜色；它只提供可叠加的浪头、
/// 漩涡和云层 Sprite，世界状态仍由现有模拟决定。
/// </summary>
[CreateAssetMenu(menuName = "Tide/V43 Sea Weather Catalog", fileName = "V43SeaWeatherCatalog")]
public sealed class TideV43SeaWeatherCatalog : ScriptableObject
{
    [SerializeField] private int version;
    [SerializeField] private Sprite[] longSwellFrames;
    [SerializeField] private Sprite[] windWaveFrames;
    [SerializeField] private Sprite[] stormBreakerFrames;
    [SerializeField] private Sprite[] vortexDepressionFrames;
    [SerializeField] private Sprite[] vortexInnerFlowFrames;
    [SerializeField] private Sprite[] vortexOuterFoamFrames;
    [SerializeField] private Sprite farCloudWall;
    [SerializeField] private Sprite midWeatherBank;
    [SerializeField] private Sprite nearScud;

    public int Version => version;

    public void Configure(
        Sprite[] longSwell,
        Sprite[] windWave,
        Sprite[] stormBreaker,
        Sprite[] vortexDepression,
        Sprite[] vortexInnerFlow,
        Sprite[] vortexOuterFoam,
        Sprite farCloud,
        Sprite midCloud,
        Sprite nearCloud)
    {
        version = TideV43SeaWeatherPresentationModel.CatalogVersion;
        longSwellFrames = longSwell;
        windWaveFrames = windWave;
        stormBreakerFrames = stormBreaker;
        vortexDepressionFrames = vortexDepression;
        vortexInnerFlowFrames = vortexInnerFlow;
        vortexOuterFoamFrames = vortexOuterFoam;
        farCloudWall = farCloud;
        midWeatherBank = midCloud;
        nearScud = nearCloud;
    }

    public Sprite GetWaveFrame(TideV43WaveKind kind, int frameIndex)
    {
        return GetWrapped(GetWaveFrames(kind), frameIndex);
    }

    public Sprite GetVortexFrame(TideV43VortexLayer layer, int frameIndex)
    {
        Sprite[] frames = layer == TideV43VortexLayer.DepressionMask
            ? vortexDepressionFrames
            : layer == TideV43VortexLayer.InnerFlow
                ? vortexInnerFlowFrames
                : vortexOuterFoamFrames;
        return GetWrapped(frames, frameIndex);
    }

    public Sprite GetCloud(TideV43CloudLayer layer)
    {
        if (layer == TideV43CloudLayer.FarCloudWall)
        {
            return farCloudWall;
        }

        return layer == TideV43CloudLayer.MidWeatherBank
            ? midWeatherBank
            : nearScud;
    }

    public bool IsComplete(out string reason)
    {
        if (version != TideV43SeaWeatherPresentationModel.CatalogVersion)
        {
            reason = $"索引版本是 {version}，不是 V43";
            return false;
        }

        if (!HasFrames(longSwellFrames, 8) ||
            !HasFrames(windWaveFrames, 8) ||
            !HasFrames(stormBreakerFrames, 8))
        {
            reason = "长涌、普通风浪或暴潮碎浪八帧不完整";
            return false;
        }

        if (!HasFrames(vortexDepressionFrames, 12) ||
            !HasFrames(vortexInnerFlowFrames, 12) ||
            !HasFrames(vortexOuterFoamFrames, 12))
        {
            reason = "漩涡三层十二相不完整";
            return false;
        }

        if (farCloudWall == null || midWeatherBank == null || nearScud == null)
        {
            reason = "远、中、近三层云缺失";
            return false;
        }

        reason = "完整";
        return true;
    }

    private Sprite[] GetWaveFrames(TideV43WaveKind kind)
    {
        if (kind == TideV43WaveKind.LongSwell)
        {
            return longSwellFrames;
        }

        return kind == TideV43WaveKind.WindWave
            ? windWaveFrames
            : stormBreakerFrames;
    }

    private static Sprite GetWrapped(Sprite[] frames, int frameIndex)
    {
        if (frames == null || frames.Length == 0)
        {
            return null;
        }

        int wrapped = ((frameIndex % frames.Length) + frames.Length) % frames.Length;
        return frames[wrapped];
    }

    private static bool HasFrames(Sprite[] frames, int expectedCount)
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
