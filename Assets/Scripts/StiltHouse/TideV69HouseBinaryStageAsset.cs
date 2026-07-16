using UnityEngine;

public sealed class TideV69HouseBinaryStageAsset : ScriptableObject
{
    [SerializeField] private int version;
    [SerializeField] private TideV69HouseBinaryOwner owner;
    [SerializeField] private bool serviceable;
    [SerializeField] private Sprite sprite;
    [SerializeField] private Vector2 offsetFromHousePivot;

    public TideV69HouseBinaryOwner Owner => owner;
    public bool Serviceable => serviceable;
    public Sprite Sprite => sprite;
    public Vector2 OffsetFromHousePivot => offsetFromHousePivot;

    public void Configure(
        TideV69HouseBinaryOwner valueOwner,
        bool valueServiceable,
        Sprite valueSprite,
        Vector2 valueOffset)
    {
        version = TideV69HouseRepairPresentationModel.CatalogVersion;
        owner = valueOwner;
        serviceable = valueServiceable;
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
            reason = "设备 Sprite 为空。";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
