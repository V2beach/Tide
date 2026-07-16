using UnityEngine;

/// <summary>
/// V53 泊位木构的最小运行索引。
///
/// 固定木路只拥有可见木板和支撑，活动跳板只拥有最后一小段连接；
/// 行走边界、船艉锚点、潮位、碰撞和人物仍由运行逻辑决定。
/// </summary>
[CreateAssetMenu(menuName = "Tide/V53 Mooring Catalog", fileName = "V53MooringCatalog")]
public sealed class TideV53MooringCatalog : ScriptableObject
{
    public const int CatalogVersion = 53;
    public const string RuntimeProfile = "Balanced";
    public const float PierSpanMeters = 4.804025f;
    public const float PierWorldHeight = 0.823f;
    public const float FoundPierCenterYOffsetFromSurface = -0.186835f;
    public const float ServiceablePierCenterYOffsetFromSurface = -0.192025f;

    [SerializeField] private int version;
    [SerializeField] private string profile;
    [SerializeField] private Sprite foundWeatheredPier;
    [SerializeField] private Sprite serviceablePier;
    [SerializeField] private Sprite serviceableGangplank;

    public int Version => version;
    public string Profile => profile;
    public Sprite FoundWeatheredPier => foundWeatheredPier;
    public Sprite ServiceablePier => serviceablePier;
    public Sprite ServiceableGangplank => serviceableGangplank;

    public void Configure(Sprite found, Sprite serviceable, Sprite gangplank)
    {
        version = CatalogVersion;
        profile = RuntimeProfile;
        foundWeatheredPier = found;
        serviceablePier = serviceable;
        serviceableGangplank = gangplank;
    }

    public bool IsComplete(out string reason)
    {
        bool complete = version == CatalogVersion && profile == RuntimeProfile &&
            foundWeatheredPier != null && serviceablePier != null && serviceableGangplank != null;
        reason = complete
            ? "V53 Balanced 固定木路双态与活动跳板完整"
            : $"V53 索引不完整：version={version}, profile={profile}, " +
              $"found={foundWeatheredPier != null}, serviceable={serviceablePier != null}, " +
              $"gangplank={serviceableGangplank != null}";
        return complete;
    }
}
