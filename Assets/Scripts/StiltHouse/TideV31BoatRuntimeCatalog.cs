using System;
using UnityEngine;

/// <summary>
/// V31 船体遮挡水线档位。风暴档优先级高于载货档，选择规则由纯表现模型统一维护。
/// </summary>
public enum TideV31BoatWaterlineMode
{
    Calm,
    Loaded,
    Storm
}

/// <summary>
/// V31 分层帆船运行索引。
///
/// Catalog 只保存正式 Sprite 的引用与契约定位数据，不复制任何 PNG；运行时因此既能
/// 脱离 AssetDatabase，又能让所有裁切层共享同一个 BoatRoot 缩放与枢轴坐标系。
/// </summary>
[CreateAssetMenu(menuName = "Tide/V31 Boat Runtime Catalog", fileName = "V31BoatRuntimeCatalog")]
public sealed class TideV31BoatRuntimeCatalog : ScriptableObject
{
    [Serializable]
    public sealed class LayerEntry
    {
        [SerializeField] private string key;
        [SerializeField] private Sprite sprite;
        [SerializeField] private Vector2 worldOffsetFromBoatPivot;
        [SerializeField] private Vector2Int originTopLeft;
        [SerializeField] private Vector2Int sourceSize;

        public string Key => key;
        public Sprite Sprite => sprite;
        public Vector2 WorldOffsetFromBoatPivot => worldOffsetFromBoatPivot;
        public Vector2Int OriginTopLeft => originTopLeft;
        public Vector2Int SourceSize => sourceSize;

        public void Configure(
            string layerKey,
            Sprite layerSprite,
            Vector2 worldOffset,
            Vector2Int cropOriginTopLeft,
            Vector2Int cropSize)
        {
            key = layerKey;
            sprite = layerSprite;
            worldOffsetFromBoatPivot = worldOffset;
            originTopLeft = cropOriginTopLeft;
            sourceSize = cropSize;
        }
    }

    [Serializable]
    public sealed class WaterlineMaskEntry
    {
        [SerializeField] private string key;
        [SerializeField] private Sprite sprite;
        [SerializeField] private int waterlineYTopLeft;
        [SerializeField] private Vector2 worldOffsetFromBoatPivot;
        [SerializeField] private Vector2Int originTopLeft;
        [SerializeField] private Vector2Int sourceSize;

        public string Key => key;
        public Sprite Sprite => sprite;
        public int WaterlineYTopLeft => waterlineYTopLeft;
        public Vector2 WorldOffsetFromBoatPivot => worldOffsetFromBoatPivot;
        public Vector2Int OriginTopLeft => originTopLeft;
        public Vector2Int SourceSize => sourceSize;

        public void Configure(
            string maskKey,
            Sprite maskSprite,
            int contractWaterlineYTopLeft,
            Vector2 worldOffset,
            Vector2Int cropOriginTopLeft,
            Vector2Int cropSize)
        {
            key = maskKey;
            sprite = maskSprite;
            waterlineYTopLeft = contractWaterlineYTopLeft;
            worldOffsetFromBoatPivot = worldOffset;
            originTopLeft = cropOriginTopLeft;
            sourceSize = cropSize;
        }
    }

    [Serializable]
    public sealed class ContractAnchorSet
    {
        [SerializeField] private Vector2Int seatTopLeft;
        [SerializeField] private Vector2Int tillerHandTopLeft;
        [SerializeField] private Vector2Int boardingSternTopLeft;
        [SerializeField] private Vector2Int cargoHookTopLeft;
        [SerializeField] private Vector2Int calmWaterlineTopLeft;

        public Vector2Int SeatTopLeft => seatTopLeft;
        public Vector2Int TillerHandTopLeft => tillerHandTopLeft;
        public Vector2Int BoardingSternTopLeft => boardingSternTopLeft;
        public Vector2Int CargoHookTopLeft => cargoHookTopLeft;
        public Vector2Int CalmWaterlineTopLeft => calmWaterlineTopLeft;

        public void Configure(
            Vector2Int seat,
            Vector2Int tillerHand,
            Vector2Int boardingStern,
            Vector2Int cargoHook,
            Vector2Int calmWaterline)
        {
            seatTopLeft = seat;
            tillerHandTopLeft = tillerHand;
            boardingSternTopLeft = boardingStern;
            cargoHookTopLeft = cargoHook;
            calmWaterlineTopLeft = calmWaterline;
        }
    }

    private static readonly string[] ExpectedFoundLayerKeys =
    {
        "CockpitBack",
        "FrontGunwale"
    };

    private static readonly string[] ExpectedRepairedLayerKeys =
    {
        "CockpitBack",
        "FrontGunwale",
        "BackRig",
        "SailRest",
        "RudderRest"
    };

    private static readonly string[] ExpectedPassengerFrameKeys =
    {
        "SeatedSteer_F00",
        "SeatedSteer_F01",
        "SeatedSteer_F02",
        "SeatedSteer_F03",
        "SeatedSteer_F04",
        "SeatedSteer_F05"
    };

    private static readonly string[] ExpectedWaterlineKeys =
    {
        "Calm",
        "Loaded",
        "Storm"
    };

    [SerializeField] private int version;
    [SerializeField] private Vector2Int canvasSize;
    [SerializeField] private Vector2 pivotNormalized;
    [SerializeField] private float pixelsPerUnit;
    [SerializeField] private Sprite foundBaseSprite;
    [SerializeField] private Sprite sailableBaseSprite;
    [SerializeField] private Sprite repairedBaseSprite;
    [SerializeField] private LayerEntry[] foundLayers;
    [SerializeField] private LayerEntry[] repairedLayers;
    [SerializeField] private LayerEntry[] passengerFrames;
    [SerializeField] private float passengerFrameSeconds;
    [SerializeField] private WaterlineMaskEntry[] waterlineMasks;
    [SerializeField] private ContractAnchorSet anchors;

    public int Version => version;
    public Vector2Int CanvasSize => canvasSize;
    public Vector2 PivotNormalized => pivotNormalized;
    public float PixelsPerUnit => pixelsPerUnit;
    public Sprite FoundBaseSprite => foundBaseSprite;
    public Sprite SailableBaseSprite => sailableBaseSprite;
    public Sprite RepairedBaseSprite => repairedBaseSprite;
    public LayerEntry[] FoundLayers => foundLayers;
    public LayerEntry[] RepairedLayers => repairedLayers;
    public LayerEntry[] PassengerFrames => passengerFrames;
    public float PassengerFrameSeconds => passengerFrameSeconds;
    public WaterlineMaskEntry[] WaterlineMasks => waterlineMasks;
    public ContractAnchorSet Anchors => anchors;
    public int ReferencedSpriteCount
    {
        get
        {
            int count = (foundBaseSprite != null ? 1 : 0) +
                (sailableBaseSprite != null ? 1 : 0) +
                (repairedBaseSprite != null ? 1 : 0) +
                CountLayerSprites(foundLayers) +
                CountLayerSprites(repairedLayers) +
                CountLayerSprites(passengerFrames);

            if (waterlineMasks != null)
            {
                for (int i = 0; i < waterlineMasks.Length; i++)
                {
                    if (waterlineMasks[i] != null && waterlineMasks[i].Sprite != null)
                    {
                        count++;
                    }
                }
            }

            return count;
        }
    }

    public void Configure(
        int catalogVersion,
        Vector2Int contractCanvasSize,
        Vector2 contractPivotNormalized,
        float contractPixelsPerUnit,
        Sprite foundBase,
        Sprite sailableBase,
        Sprite repairedBase,
        LayerEntry[] found,
        LayerEntry[] repaired,
        LayerEntry[] passenger,
        float passengerSecondsPerFrame,
        WaterlineMaskEntry[] masks,
        ContractAnchorSet contractAnchors)
    {
        version = catalogVersion;
        canvasSize = contractCanvasSize;
        pivotNormalized = contractPivotNormalized;
        pixelsPerUnit = contractPixelsPerUnit;
        foundBaseSprite = foundBase;
        sailableBaseSprite = sailableBase;
        repairedBaseSprite = repairedBase;
        foundLayers = found;
        repairedLayers = repaired;
        passengerFrames = passenger;
        passengerFrameSeconds = passengerSecondsPerFrame;
        waterlineMasks = masks;
        anchors = contractAnchors;
    }

    public WaterlineMaskEntry GetWaterlineMask(TideV31BoatWaterlineMode mode)
    {
        if (waterlineMasks == null)
        {
            return null;
        }

        string expectedKey = mode.ToString();
        for (int i = 0; i < waterlineMasks.Length; i++)
        {
            WaterlineMaskEntry mask = waterlineMasks[i];
            if (mask != null && string.Equals(mask.Key, expectedKey, StringComparison.Ordinal))
            {
                return mask;
            }
        }

        return null;
    }

    public bool IsComplete(out string reason)
    {
        if (version != TideV31BoatPresentationModel.CatalogVersion)
        {
            reason = $"索引版本是 {version}，不是 V31";
            return false;
        }

        if (!HasCompleteLayerArray(foundLayers, ExpectedFoundLayerKeys))
        {
            reason = "Found 两层不完整或顺序与契约不一致";
            return false;
        }

        if (!HasCompleteLayerArray(repairedLayers, ExpectedRepairedLayerKeys))
        {
            reason = "Repaired 五层不完整或顺序与契约不一致";
            return false;
        }

        if (!HasCompleteLayerArray(passengerFrames, ExpectedPassengerFrameKeys))
        {
            reason = "Passenger 六帧不完整或顺序与契约不一致";
            return false;
        }

        if (!HasCompleteWaterlineArray(waterlineMasks, ExpectedWaterlineKeys))
        {
            reason = "Calm/Loaded/Storm 三张水线遮罩不完整或顺序与契约不一致";
            return false;
        }

        if (foundBaseSprite == null || sailableBaseSprite == null || repairedBaseSprite == null)
        {
            reason = "V21 Found/RiskyRig/Repaired 完整底图缺失，V31 分层会露出背景或丢失船帆";
            return false;
        }

        // 分组数量正确仍不足以证明资源完整；这里明确验证 18 个槽位都持有 Sprite。
        if (ReferencedSpriteCount != TideV31BoatPresentationModel.ExpectedSpriteCount)
        {
            reason = $"V31/V21 Sprite 引用数量是 {ReferencedSpriteCount}，不是 " +
                TideV31BoatPresentationModel.ExpectedSpriteCount;
            return false;
        }

        if (canvasSize.x <= 0 || canvasSize.y <= 0 || pixelsPerUnit <= 0f || anchors == null)
        {
            reason = "V31 画布、PPU 或契约锚点缺失";
            return false;
        }

        if (passengerFrameSeconds <= 0f)
        {
            reason = "Passenger 帧时长无效";
            return false;
        }

        reason = "完整";
        return true;
    }

    private static int CountLayerSprites(LayerEntry[] entries)
    {
        if (entries == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i] != null && entries[i].Sprite != null)
            {
                count++;
            }
        }
        return count;
    }

    private static bool HasCompleteLayerArray(LayerEntry[] entries, string[] expectedKeys)
    {
        if (entries == null || entries.Length != expectedKeys.Length)
        {
            return false;
        }

        for (int i = 0; i < entries.Length; i++)
        {
            LayerEntry entry = entries[i];
            if (entry == null || entry.Sprite == null ||
                !string.Equals(entry.Key, expectedKeys[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasCompleteWaterlineArray(WaterlineMaskEntry[] entries, string[] expectedKeys)
    {
        if (entries == null || entries.Length != expectedKeys.Length)
        {
            return false;
        }

        for (int i = 0; i < entries.Length; i++)
        {
            WaterlineMaskEntry entry = entries[i];
            if (entry == null || entry.Sprite == null || entry.WaterlineYTopLeft <= 0 ||
                !string.Equals(entry.Key, expectedKeys[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
