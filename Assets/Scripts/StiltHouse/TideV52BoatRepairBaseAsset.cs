using UnityEngine;

/// <summary>
/// V52 两张永久稳定底图。它们随场景常驻，但不包含任何维修阶段或人物像素。
/// </summary>
public sealed class TideV52BoatRepairBaseAsset : ScriptableObject
{
    [SerializeField] private int version;
    [SerializeField] private string profile;
    [SerializeField] private Sprite frontGunwaleStable;
    [SerializeField] private Vector2 frontGunwaleOffset;
    [SerializeField] private Sprite cockpitFloorStable;
    [SerializeField] private Vector2 cockpitFloorOffset;

    public int Version => version;
    public string Profile => profile;
    public Sprite FrontGunwaleStable => frontGunwaleStable;
    public Vector2 FrontGunwaleOffset => frontGunwaleOffset;
    public Sprite CockpitFloorStable => cockpitFloorStable;
    public Vector2 CockpitFloorOffset => cockpitFloorOffset;

    public void Configure(
        int catalogVersion,
        string runtimeProfile,
        Sprite gunwaleSprite,
        Vector2 gunwaleOffset,
        Sprite floorSprite,
        Vector2 floorOffset)
    {
        version = catalogVersion;
        profile = runtimeProfile;
        frontGunwaleStable = gunwaleSprite;
        frontGunwaleOffset = gunwaleOffset;
        cockpitFloorStable = floorSprite;
        cockpitFloorOffset = floorOffset;
    }

    public bool IsComplete(out string reason)
    {
        if (version != TideV52BoatRepairPresentationModel.CatalogVersion || profile != "Balanced")
        {
            reason = $"V52 稳定底索引版本/档位错误：V{version}/{profile}";
            return false;
        }

        if (frontGunwaleStable == null || cockpitFloorStable == null)
        {
            reason = "V52 前船舷或舱底稳定底图缺失";
            return false;
        }

        if (Vector2.Distance(frontGunwaleOffset, TideV52BoatRepairPresentationModel.FrontGunwaleStableOffset) > 0.001f ||
            Vector2.Distance(cockpitFloorOffset, TideV52BoatRepairPresentationModel.CockpitFloorStableOffset) > 0.001f)
        {
            reason = "V52 稳定底图偏移与 V67 契约不一致";
            return false;
        }

        reason = "完整";
        return true;
    }
}
