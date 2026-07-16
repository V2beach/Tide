using System;
using UnityEngine;

/// <summary>
/// V34 高脚屋外景的稳定底图与六个互斥维修 owner 索引。
///
/// 每个 owner 的 Damage/Repair 使用同一裁切画布和中心 Pivot，运行时只显示
/// 其中一个状态。世界偏移由 2048 母版和房屋底部 Pivot 推导，不能手工微调。
/// </summary>
[CreateAssetMenu(menuName = "Tide/V34 House Exterior Catalog", fileName = "V34HouseExteriorCatalog")]
public sealed class TideV34HouseExteriorCatalog : ScriptableObject
{
    [Serializable]
    public sealed class OwnerEntry
    {
        [SerializeField] private string key;
        [SerializeField] private string displayNameZh;
        [SerializeField] private string gameplayOwner;
        [SerializeField] private Sprite damageSprite;
        [SerializeField] private Sprite repairSprite;
        [SerializeField] private Vector2 worldOffsetFromHousePivot;
        [SerializeField] private Vector2Int originTopLeft;
        [SerializeField] private Vector2Int sourceSize;

        public string Key => key;
        public string DisplayNameZh => displayNameZh;
        public string GameplayOwner => gameplayOwner;
        public Sprite DamageSprite => damageSprite;
        public Sprite RepairSprite => repairSprite;
        public Vector2 WorldOffsetFromHousePivot => worldOffsetFromHousePivot;
        public Vector2Int OriginTopLeft => originTopLeft;
        public Vector2Int SourceSize => sourceSize;

        public void Configure(
            string ownerKey,
            string ownerDisplayNameZh,
            string repairChannel,
            Sprite damage,
            Sprite repair,
            Vector2 worldOffset,
            Vector2Int cropOriginTopLeft,
            Vector2Int cropSize)
        {
            key = ownerKey;
            displayNameZh = ownerDisplayNameZh;
            gameplayOwner = repairChannel;
            damageSprite = damage;
            repairSprite = repair;
            worldOffsetFromHousePivot = worldOffset;
            originTopLeft = cropOriginTopLeft;
            sourceSize = cropSize;
        }
    }

    [SerializeField] private int version;
    [SerializeField] private Sprite stableBase;
    [SerializeField] private OwnerEntry[] owners;

    public int Version => version;
    public Sprite StableBase => stableBase;
    public OwnerEntry[] Owners => owners;

    public void Configure(int catalogVersion, Sprite exteriorStableBase, OwnerEntry[] repairOwners)
    {
        version = catalogVersion;
        stableBase = exteriorStableBase;
        owners = repairOwners;
    }

    public bool IsComplete(out string reason)
    {
        if (version != TideV34HouseExteriorPresentationModel.Version)
        {
            reason = $"索引版本是 {version}，不是 V34";
            return false;
        }

        if (stableBase == null)
        {
            reason = "V34 外景 StableBase 缺失";
            return false;
        }

        if (owners == null || owners.Length != TideV34HouseExteriorPresentationModel.OwnerCount)
        {
            reason = "V34 外景 owner 数量不是 6";
            return false;
        }

        for (int i = 0; i < owners.Length; i++)
        {
            OwnerEntry owner = owners[i];
            if (owner == null || string.IsNullOrWhiteSpace(owner.Key) ||
                string.IsNullOrWhiteSpace(owner.GameplayOwner) ||
                owner.DamageSprite == null || owner.RepairSprite == null)
            {
                reason = $"V34 外景 owner {i} 缺少键、玩法归属或 Damage/Repair Sprite";
                return false;
            }

            if (owner.DamageSprite.rect.size != owner.RepairSprite.rect.size)
            {
                reason = $"V34 外景 owner {owner.Key} 的 Damage/Repair 裁切尺寸不同";
                return false;
            }
        }

        reason = "完整";
        return true;
    }
}
