using System;
using UnityEngine;

/// <summary>
/// V35 同外壳室内的稳定底图与十二个互斥维修所有者。
///
/// 每个所有者只保存一对共画布裁切 Sprite、相对 2048 母版底部 Pivot 的偏移，
/// 以及它归属的真实玩法维修线。运行时不会加载十二张中间整屋状态。
/// </summary>
[CreateAssetMenu(menuName = "Tide/V35 House Interior Catalog", fileName = "V35HouseInteriorCatalog")]
public sealed class TideV35HouseInteriorCatalog : ScriptableObject
{
    [Serializable]
    public sealed class OwnerEntry
    {
        [SerializeField] private string key;
        [SerializeField] private string displayNameZh;
        [SerializeField] private string gameplayOwner;
        [SerializeField] private int requiredStep;
        [SerializeField] private Sprite damageSprite;
        [SerializeField] private Sprite repairSprite;
        [SerializeField] private Vector2 worldOffsetFromHousePivot;
        [SerializeField] private Vector2Int originTopLeft;
        [SerializeField] private Vector2Int sourceSize;

        public string Key => key;
        public string DisplayNameZh => displayNameZh;
        public string GameplayOwner => gameplayOwner;
        public int RequiredStep => requiredStep;
        public Sprite DamageSprite => damageSprite;
        public Sprite RepairSprite => repairSprite;
        public Vector2 WorldOffsetFromHousePivot => worldOffsetFromHousePivot;
        public Vector2Int OriginTopLeft => originTopLeft;
        public Vector2Int SourceSize => sourceSize;

        public void Configure(
            string ownerKey,
            string ownerDisplayNameZh,
            string repairChannel,
            int repairRequiredStep,
            Sprite damage,
            Sprite repair,
            Vector2 worldOffset,
            Vector2Int cropOriginTopLeft,
            Vector2Int cropSize)
        {
            key = ownerKey;
            displayNameZh = ownerDisplayNameZh;
            gameplayOwner = repairChannel;
            requiredStep = repairRequiredStep;
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

    public void Configure(int catalogVersion, Sprite interiorStableBase, OwnerEntry[] repairOwners)
    {
        version = catalogVersion;
        stableBase = interiorStableBase;
        owners = repairOwners;
    }

    public bool IsComplete(out string reason)
    {
        if (version != TideV35HouseInteriorPresentationModel.Version)
        {
            reason = $"索引版本是 {version}，不是 V35";
            return false;
        }

        if (stableBase == null)
        {
            reason = "V35 室内 StableBase 缺失";
            return false;
        }

        if (owners == null || owners.Length != TideV35HouseInteriorPresentationModel.OwnerCount)
        {
            reason = "V35 室内 owner 数量不是 12";
            return false;
        }

        for (int i = 0; i < owners.Length; i++)
        {
            OwnerEntry owner = owners[i];
            if (owner == null || string.IsNullOrWhiteSpace(owner.Key) ||
                string.IsNullOrWhiteSpace(owner.GameplayOwner) ||
                owner.RequiredStep < 1 || owner.RequiredStep > 2 ||
                owner.DamageSprite == null || owner.RepairSprite == null)
            {
                reason = $"V35 室内 owner {i} 缺少键、维修归属、步骤或状态 Sprite";
                return false;
            }

            if (owner.DamageSprite.rect.size != owner.RepairSprite.rect.size)
            {
                reason = $"V35 室内 owner {owner.Key} 的 Damage/Repair 裁切尺寸不同";
                return false;
            }
        }

        reason = "完整";
        return true;
    }
}
