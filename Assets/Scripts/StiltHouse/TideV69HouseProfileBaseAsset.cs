using UnityEngine;

public sealed class TideV69HouseProfileBaseAsset : ScriptableObject
{
    [SerializeField] private int version;
    [SerializeField] private TideV69HouseProfile profile;
    [SerializeField] private Sprite stableBase;

    public TideV69HouseProfile Profile => profile;
    public Sprite StableBase => stableBase;

    public void Configure(TideV69HouseProfile valueProfile, Sprite valueStableBase)
    {
        version = TideV69HouseRepairPresentationModel.CatalogVersion;
        profile = valueProfile;
        stableBase = valueStableBase;
    }

    public bool IsComplete(out string reason)
    {
        if (version != TideV69HouseRepairPresentationModel.CatalogVersion)
        {
            reason = "版本不是 V69。";
            return false;
        }
        if (stableBase == null)
        {
            reason = "稳定底图为空。";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
