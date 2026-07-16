using UnityEngine;

public enum TideV54NetVisualState
{
    DeployedDry,
    DeployedWet,
    DeployedFrayed,
    BrokenResidue,
    StoredDry,
    CarriedDry,
    HauledWet,
}

/// <summary>
/// V54 同世界潮网的运行索引。
///
/// Catalog 只拥有网衣、浮纲、沉纲、浮子和坠子。悬绳、人物双手、捕获物、
/// 海水、泡沫和交互状态仍由运行逻辑生成，避免把不同世界状态烘进同一张图。
/// </summary>
[CreateAssetMenu(menuName = "Tide/V54 Net Catalog", fileName = "V54NetCatalog")]
public sealed class TideV54NetCatalog : ScriptableObject
{
    [SerializeField] private int version;
    [SerializeField] private string profile;
    [SerializeField] private Sprite deployedDry;
    [SerializeField] private Sprite deployedWet;
    [SerializeField] private Sprite deployedFrayed;
    [SerializeField] private Sprite brokenResidue;
    [SerializeField] private Sprite storedDry;
    [SerializeField] private Sprite carriedDry;
    [SerializeField] private Sprite hauledWet;

    public int Version => version;
    public string Profile => profile;

    public void Configure(
        Sprite dry,
        Sprite wet,
        Sprite frayed,
        Sprite broken,
        Sprite stored,
        Sprite carried,
        Sprite hauled)
    {
        version = TideV54NetPresentationModel.CatalogVersion;
        profile = TideV54NetPresentationModel.RuntimeProfile;
        deployedDry = dry;
        deployedWet = wet;
        deployedFrayed = frayed;
        brokenResidue = broken;
        storedDry = stored;
        carriedDry = carried;
        hauledWet = hauled;
    }

    public Sprite Get(TideV54NetVisualState state)
    {
        switch (state)
        {
            case TideV54NetVisualState.DeployedWet:
                return deployedWet;
            case TideV54NetVisualState.DeployedFrayed:
                return deployedFrayed;
            case TideV54NetVisualState.BrokenResidue:
                return brokenResidue;
            case TideV54NetVisualState.StoredDry:
                return storedDry;
            case TideV54NetVisualState.CarriedDry:
                return carriedDry;
            case TideV54NetVisualState.HauledWet:
                return hauledWet;
            default:
                return deployedDry;
        }
    }

    public bool IsComplete(out string reason)
    {
        if (version != TideV54NetPresentationModel.CatalogVersion)
        {
            reason = $"索引版本是 {version}，不是 V54";
            return false;
        }

        if (profile != TideV54NetPresentationModel.RuntimeProfile)
        {
            reason = $"运行档位是 {profile}，不是唯一允许的 Balanced";
            return false;
        }

        for (int i = 0; i < TideV54NetPresentationModel.VisualStateCount; i++)
        {
            TideV54NetVisualState state = (TideV54NetVisualState)i;
            if (Get(state) == null)
            {
                reason = $"缺少 {state} 网态";
                return false;
            }
        }

        reason = "V54 Balanced 七态完整";
        return true;
    }
}
