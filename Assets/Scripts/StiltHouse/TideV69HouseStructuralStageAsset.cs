using UnityEngine;

/// <summary>
/// 一个视图中的单个结构阶段。内外同步由纯阶段模型保证，纹理只为当前视图驻留。
/// </summary>
public sealed class TideV69HouseStructuralStageAsset : ScriptableObject
{
    [SerializeField] private int version;
    [SerializeField] private TideV69HouseProfile profile;
    [SerializeField] private TideV69HouseStructuralOwner owner;
    [SerializeField] private TideV69HouseRepairStage stage;
    [SerializeField] private Sprite sprite;
    [SerializeField] private Vector2 offsetFromHousePivot;

    public TideV69HouseProfile Profile => profile;
    public TideV69HouseStructuralOwner Owner => owner;
    public TideV69HouseRepairStage Stage => stage;
    public Sprite Sprite => sprite;
    public Vector2 OffsetFromHousePivot => offsetFromHousePivot;

    public void Configure(
        TideV69HouseProfile valueProfile,
        TideV69HouseStructuralOwner valueOwner,
        TideV69HouseRepairStage valueStage,
        Sprite valueSprite,
        Vector2 valueOffset)
    {
        version = TideV69HouseRepairPresentationModel.CatalogVersion;
        profile = valueProfile;
        owner = valueOwner;
        stage = valueStage;
        sprite = valueSprite;
        offsetFromHousePivot = valueOffset;
    }

    public bool IsComplete(out string reason)
    {
        if (version != TideV69HouseRepairPresentationModel.CatalogVersion)
        {
            reason = "版本不是 V69。";
            return false;
        }
        if (sprite == null)
        {
            reason = "结构 Sprite 为空。";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
