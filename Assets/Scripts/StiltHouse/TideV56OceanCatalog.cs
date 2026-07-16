using UnityEngine;

/// <summary>
/// V56 中性连续海体的运行索引。
///
/// 本资源不拥有浪峰、泡沫或物理水位；V43 和 TideOceanFieldModel 继续分别
/// 负责局部浪头表现与连续海况采样。
/// </summary>
[CreateAssetMenu(menuName = "Tide/V56 Ocean Catalog", fileName = "V56OceanCatalog")]
public sealed class TideV56OceanCatalog : ScriptableObject
{
    public const int CatalogVersion = 56;
    public const string RuntimeProfile = "Balanced";

    [SerializeField] private int version;
    [SerializeField] private string profile;
    [SerializeField] private Sprite continuousBase;

    public int Version => version;
    public string Profile => profile;
    public Sprite ContinuousBase => continuousBase;

    public void Configure(Sprite sprite)
    {
        version = CatalogVersion;
        profile = RuntimeProfile;
        continuousBase = sprite;
    }

    public bool IsComplete(out string reason)
    {
        bool complete = version == CatalogVersion && profile == RuntimeProfile && continuousBase != null;
        reason = complete
            ? "V56 Balanced 低起伏连续海体完整"
            : $"V56 索引不完整：version={version}, profile={profile}, sprite={continuousBase != null}";
        return complete;
    }
}
