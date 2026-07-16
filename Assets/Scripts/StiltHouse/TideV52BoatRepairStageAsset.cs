using UnityEngine;

/// <summary>
/// 单个 owner 的单个施工阶段。每张资源独立放在 Resources 中，运行控制器只持有
/// 当前三个阶段的对象引用，避免一个总 Catalog 在进场时把十八张施工图一起拉入内存。
/// </summary>
public sealed class TideV52BoatRepairStageAsset : ScriptableObject
{
    [SerializeField] private int version;
    [SerializeField] private string profile;
    [SerializeField] private TideV52BoatRepairOwner owner;
    [SerializeField] private TideV52BoatRepairStage stage;
    [SerializeField] private Sprite sprite;
    [SerializeField] private Vector2 worldOffsetFromBoatPivot;

    public int Version => version;
    public string Profile => profile;
    public TideV52BoatRepairOwner Owner => owner;
    public TideV52BoatRepairStage Stage => stage;
    public Sprite Sprite => sprite;
    public Vector2 WorldOffsetFromBoatPivot => worldOffsetFromBoatPivot;

    public void Configure(
        int catalogVersion,
        string runtimeProfile,
        TideV52BoatRepairOwner repairOwner,
        TideV52BoatRepairStage repairStage,
        Sprite stageSprite,
        Vector2 worldOffset)
    {
        version = catalogVersion;
        profile = runtimeProfile;
        owner = repairOwner;
        stage = repairStage;
        sprite = stageSprite;
        worldOffsetFromBoatPivot = worldOffset;
    }

    public bool IsComplete(out string reason)
    {
        if (version != TideV52BoatRepairPresentationModel.CatalogVersion || profile != "Balanced")
        {
            reason = $"V52 阶段索引版本/档位错误：V{version}/{profile}";
            return false;
        }

        if (sprite == null)
        {
            reason = $"V52 {owner}/{stage} Sprite 缺失";
            return false;
        }

        if (Vector2.Distance(
                worldOffsetFromBoatPivot,
                TideV52BoatRepairPresentationModel.GetOwnerOffset(owner)) > 0.001f)
        {
            reason = $"V52 {owner}/{stage} 偏移与 V67 契约不一致";
            return false;
        }

        reason = "完整";
        return true;
    }
}
