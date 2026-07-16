using System;
using UnityEngine;

/// <summary>
/// V30 高脚屋运行索引。
///
/// 这个资产本身放在 Resources 中，但 Sprite 仍引用只读的 V30 正式目录，
/// 因此打包时不会复制第二套 PNG，也不会让玩法代码依赖 AssetDatabase。
/// </summary>
[CreateAssetMenu(menuName = "Tide/V30 House Runtime Catalog", fileName = "V30HouseRuntimeCatalog")]
public sealed class TideV30HouseRuntimeCatalog : ScriptableObject
{
    [Serializable]
    public sealed class RepairOwnerEntry
    {
        [SerializeField] private string key;
        [SerializeField] private string displayNameZh;
        [SerializeField] private Sprite damageSprite;
        [SerializeField] private Sprite repairSprite;
        [SerializeField] private Vector2 worldOffsetFromHousePivot;
        [SerializeField] private Vector2Int originTopLeft;
        [SerializeField] private Vector2Int sourceSize;

        public string Key => key;
        public string DisplayNameZh => displayNameZh;
        public Sprite DamageSprite => damageSprite;
        public Sprite RepairSprite => repairSprite;
        public Vector2 WorldOffsetFromHousePivot => worldOffsetFromHousePivot;
        public Vector2Int OriginTopLeft => originTopLeft;
        public Vector2Int SourceSize => sourceSize;

        public void Configure(
            string ownerKey,
            string ownerDisplayNameZh,
            Sprite damage,
            Sprite repair,
            Vector2 worldOffset,
            Vector2Int cropOriginTopLeft,
            Vector2Int cropSize)
        {
            key = ownerKey;
            displayNameZh = ownerDisplayNameZh;
            damageSprite = damage;
            repairSprite = repair;
            worldOffsetFromHousePivot = worldOffset;
            originTopLeft = cropOriginTopLeft;
            sourceSize = cropSize;
        }
    }

    [SerializeField] private int version;
    [SerializeField] private Sprite[] exteriorFrames;
    [SerializeField] private Sprite exteriorNoCloth;
    [SerializeField] private Sprite interiorFound;
    [SerializeField] private Sprite interiorClean;
    [SerializeField] private Sprite[] interiorRepairedFrames;
    [SerializeField] private Sprite stableBase;
    [SerializeField] private RepairOwnerEntry[] repairOwners;

    public int Version => version;
    public Sprite[] ExteriorFrames => exteriorFrames;
    public Sprite ExteriorNoCloth => exteriorNoCloth;
    public Sprite InteriorFound => interiorFound;
    public Sprite InteriorClean => interiorClean;
    public Sprite[] InteriorRepairedFrames => interiorRepairedFrames;
    public Sprite StableBase => stableBase;
    public RepairOwnerEntry[] RepairOwners => repairOwners;

    public void Configure(
        int catalogVersion,
        Sprite[] exterior,
        Sprite noCloth,
        Sprite found,
        Sprite clean,
        Sprite[] repaired,
        Sprite repairStableBase,
        RepairOwnerEntry[] owners)
    {
        version = catalogVersion;
        exteriorFrames = exterior;
        exteriorNoCloth = noCloth;
        interiorFound = found;
        interiorClean = clean;
        interiorRepairedFrames = repaired;
        stableBase = repairStableBase;
        repairOwners = owners;
    }

    public bool IsComplete(out string reason)
    {
        if (version != 30)
        {
            reason = $"索引版本是 {version}，不是 V30";
            return false;
        }

        if (!HasCompleteSpriteArray(exteriorFrames, TideV30HousePresentationModel.ExteriorFrameCount))
        {
            reason = "外景十二帧不完整";
            return false;
        }

        if (!HasCompleteSpriteArray(interiorRepairedFrames, TideV30HousePresentationModel.InteriorFrameCount))
        {
            reason = "修复室内十二帧不完整";
            return false;
        }

        if (exteriorNoCloth == null || interiorFound == null || interiorClean == null || stableBase == null)
        {
            reason = "V30 完整档位或维修 StableBase 缺失";
            return false;
        }

        if (repairOwners == null || repairOwners.Length != TideV30HousePresentationModel.RepairOwnerCount)
        {
            reason = "维修 owner 数量不是 12";
            return false;
        }

        for (int i = 0; i < repairOwners.Length; i++)
        {
            RepairOwnerEntry owner = repairOwners[i];
            if (owner == null || string.IsNullOrWhiteSpace(owner.Key) ||
                owner.DamageSprite == null || owner.RepairSprite == null)
            {
                reason = $"维修 owner {i} 缺少键或 Damage/Repair Sprite";
                return false;
            }
        }

        reason = "完整";
        return true;
    }

    private static bool HasCompleteSpriteArray(Sprite[] sprites, int expectedCount)
    {
        if (sprites == null || sprites.Length != expectedCount)
        {
            return false;
        }

        for (int i = 0; i < sprites.Length; i++)
        {
            if (sprites[i] == null)
            {
                return false;
            }
        }

        return true;
    }
}
