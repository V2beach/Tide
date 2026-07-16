using UnityEngine;

/// <summary>
/// V70 Balanced 侧视漩涡的运行索引。
///
/// Catalog 只引用三组透明逐帧图和契约偏移。连续海面、天气、船、吸力与碰撞
/// 均不进入资源索引，防止 QA 合成海板或另一档 High 纹理被误带进运行。
/// </summary>
[CreateAssetMenu(menuName = "Tide/V70 Side View Vortex Catalog", fileName = "V70VortexCatalog")]
public sealed class TideV70VortexCatalog : ScriptableObject
{
    [SerializeField] private int version;
    [SerializeField] private string profile;
    [SerializeField] private Sprite[] throatDepressionFrames;
    [SerializeField] private Sprite[] underwaterReturnFlowFrames;
    [SerializeField] private Sprite[] surfaceConvergenceFoamFrames;
    [SerializeField] private Vector2 throatDepressionOffset;
    [SerializeField] private Vector2 underwaterReturnFlowOffset;
    [SerializeField] private Vector2 surfaceConvergenceFoamOffset;

    public int Version => version;
    public string Profile => profile;

    public void Configure(
        Sprite[] throatFrames,
        Sprite[] underwaterFrames,
        Sprite[] foamFrames,
        Vector2 throatOffset,
        Vector2 underwaterOffset,
        Vector2 foamOffset)
    {
        version = TideV70VortexPresentationModel.CatalogVersion;
        profile = TideV70VortexPresentationModel.RuntimeProfile;
        throatDepressionFrames = throatFrames;
        underwaterReturnFlowFrames = underwaterFrames;
        surfaceConvergenceFoamFrames = foamFrames;
        throatDepressionOffset = throatOffset;
        underwaterReturnFlowOffset = underwaterOffset;
        surfaceConvergenceFoamOffset = foamOffset;
    }

    public Sprite GetFrame(TideV70VortexLayer layer, int frameIndex)
    {
        Sprite[] frames = GetFrames(layer);
        if (frames == null || frames.Length == 0)
        {
            return null;
        }

        int wrapped = ((frameIndex % frames.Length) + frames.Length) % frames.Length;
        return frames[wrapped];
    }

    public Vector2 GetWorldOffset(TideV70VortexLayer layer)
    {
        switch (layer)
        {
            case TideV70VortexLayer.ThroatDepression:
                return throatDepressionOffset;
            case TideV70VortexLayer.UnderwaterReturnFlow:
                return underwaterReturnFlowOffset;
            default:
                return surfaceConvergenceFoamOffset;
        }
    }

    public bool IsComplete(out string reason)
    {
        if (version != TideV70VortexPresentationModel.CatalogVersion)
        {
            reason = $"索引版本是 {version}，不是 V70";
            return false;
        }

        if (profile != TideV70VortexPresentationModel.RuntimeProfile)
        {
            reason = $"运行档位是 {profile}，不是唯一允许的 Balanced";
            return false;
        }

        for (int layerIndex = 0; layerIndex < TideV70VortexPresentationModel.LayerCount; layerIndex++)
        {
            TideV70VortexLayer layer = (TideV70VortexLayer)layerIndex;
            Sprite[] frames = GetFrames(layer);
            if (frames == null || frames.Length != TideV70VortexPresentationModel.FrameCount)
            {
                reason = $"{layer} 不是完整十二相";
                return false;
            }

            for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
            {
                if (frames[frameIndex] == null)
                {
                    reason = $"{layer} 第 {frameIndex} 帧缺失";
                    return false;
                }
            }

            if (Vector2.Distance(
                    GetWorldOffset(layer),
                    TideV70VortexPresentationModel.GetContractWorldOffset(layer)) > 0.00001f)
            {
                reason = $"{layer} 的水线偏移不符合 Balanced 契约";
                return false;
            }
        }

        reason = "V70 Balanced 三层十二相完整";
        return true;
    }

    private Sprite[] GetFrames(TideV70VortexLayer layer)
    {
        switch (layer)
        {
            case TideV70VortexLayer.ThroatDepression:
                return throatDepressionFrames;
            case TideV70VortexLayer.UnderwaterReturnFlow:
                return underwaterReturnFlowFrames;
            default:
                return surfaceConvergenceFoamFrames;
        }
    }
}
