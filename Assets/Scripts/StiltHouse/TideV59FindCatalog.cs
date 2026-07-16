using UnityEngine;

/// <summary>
/// V59 潮带物 High 档的唯一运行索引。
///
/// Catalog 只拥有十二张物件 Sprite。米制锚点由 TideV59FindPresentationModel
/// 统一解释，海水、泡沫、网、手、数量和状态仍由运行逻辑拥有。
/// </summary>
[CreateAssetMenu(menuName = "Tide/V59 Tide Find Catalog", fileName = "V59TideFindCatalog")]
public sealed class TideV59FindCatalog : ScriptableObject
{
    [SerializeField] private int version;
    [SerializeField] private string profile;
    [SerializeField] private Sprite[] fish;
    [SerializeField] private Sprite[] wood;
    [SerializeField] private Sprite[] trash;
    [SerializeField] private Sprite[] relic;

    public int Version => version;
    public string Profile => profile;

    public void Configure(Sprite[] fishSprites, Sprite[] woodSprites, Sprite[] trashSprites, Sprite[] relicSprites)
    {
        version = TideV59FindPresentationModel.CatalogVersion;
        profile = TideV59FindPresentationModel.RuntimeProfile;
        fish = CopyThree(fishSprites);
        wood = CopyThree(woodSprites);
        trash = CopyThree(trashSprites);
        relic = CopyThree(relicSprites);
    }

    public Sprite Get(TideV59FindKind kind, int variantIndex)
    {
        Sprite[] source;
        switch (kind)
        {
            case TideV59FindKind.Wood:
                source = wood;
                break;
            case TideV59FindKind.Trash:
                source = trash;
                break;
            case TideV59FindKind.Relic:
                source = relic;
                break;
            default:
                source = fish;
                break;
        }

        if (source == null || source.Length != TideV59FindPresentationModel.VariantsPerKind)
        {
            return null;
        }

        int index = Mathf.Abs(variantIndex % TideV59FindPresentationModel.VariantsPerKind);
        return source[index];
    }

    public bool IsComplete(out string reason)
    {
        if (version != TideV59FindPresentationModel.CatalogVersion)
        {
            reason = $"索引版本是 {version}，不是 V59";
            return false;
        }

        if (profile != TideV59FindPresentationModel.RuntimeProfile)
        {
            reason = $"运行档位是 {profile}，不是唯一允许的 High";
            return false;
        }

        for (int kindIndex = 0; kindIndex < TideV59FindPresentationModel.KindCount; kindIndex++)
        {
            TideV59FindKind kind = (TideV59FindKind)kindIndex;
            for (int variantIndex = 0; variantIndex < TideV59FindPresentationModel.VariantsPerKind; variantIndex++)
            {
                if (Get(kind, variantIndex) == null)
                {
                    reason = $"缺少 {kind}[{variantIndex}]";
                    return false;
                }
            }
        }

        reason = "V59 High 十二件潮带物完整";
        return true;
    }

    private static Sprite[] CopyThree(Sprite[] source)
    {
        Sprite[] result = new Sprite[TideV59FindPresentationModel.VariantsPerKind];
        if (source == null)
        {
            return result;
        }

        for (int i = 0; i < result.Length && i < source.Length; i++)
        {
            result[i] = source[i];
        }

        return result;
    }
}
