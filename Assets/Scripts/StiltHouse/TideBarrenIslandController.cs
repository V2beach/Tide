using System;
using UnityEngine;

public enum TideIslandSalvagePart
{
    None,
    HullPlank,
    Sailcloth,
    RivetedPlate
}

public enum TideIslandSalvageUse
{
    Shelter,
    EscapeBoat
}

public enum TideIslandSalvageDestination
{
    None,
    ShelterStaging,
    EscapeBoatStaging,
    IntegratedIntoShelter,
    IntegratedIntoEscapeBoat
}

public struct TideIslandDismantleFeedback
{
    public TideIslandSalvagePart Part;
    public float Progress01;
    public float WorkRate01;
    public bool Worked;
    public bool Completed;
    public TideWreckDismantleBlockReason BlockReason;
}

/// <summary>
/// 外海岩礁岛的独立表现与实物所有权。它只拥有岛、船骸可拆件和蓄水池；
/// 玩家移动、海况、库存和维修仍由第一切片编排器拥有。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class TideBarrenIslandController : MonoBehaviour
{
    public const float WalkableLeftX = -14.55f;
    public const float WalkableRightX = -6.9f;
    public const float OpeningPlayerX = -12.45f;
    public const float CisternX = -7.35f;
    public const float ShelterDeliveryX = -5.65f;

    private static readonly Vector2[] SalvageOffsets =
    {
        new Vector2(-0.48f, 0.08f),
        new Vector2(0.42f, 0.56f),
        new Vector2(-0.94f, -0.32f)
    };

    private static Sprite rockBackSprite;
    private static Sprite rockFrontSprite;
    private static Sprite cisternSprite;
    private static Sprite saltLineSprite;
    private static Sprite plantSprite;
    private static Sprite plankSprite;
    private static Sprite clothSprite;
    private static Sprite plateSprite;
    private static Sprite pipeSprite;

    [SerializeField] private TideRainCisternState cistern;
    [SerializeField] private bool cisternPlatePatchApplied;
    [SerializeField] private TideIslandSalvagePart carriedPart;
    [SerializeField] private bool hullPlankRemoved;
    [SerializeField] private bool sailclothRemoved;
    [SerializeField] private bool rivetedPlateRemoved;
    [SerializeField] private int shelterStagedParts;
    [SerializeField] private int boatStagedParts;
    [SerializeField] private TideIslandSalvageDestination hullPlankDestination;
    [SerializeField] private TideIslandSalvageDestination sailclothDestination;
    [SerializeField] private TideIslandSalvageDestination rivetedPlateDestination;
    [SerializeField] private TideIslandSalvagePart activeDismantlePart;
    [SerializeField, Range(0f, 1f)] private float hullPlankDismantleProgress01;
    [SerializeField, Range(0f, 1f)] private float sailclothDismantleProgress01;
    [SerializeField, Range(0f, 1f)] private float rivetedPlateDismantleProgress01;
    [SerializeField] private bool dismantleWorkActive;

    private Transform visualRoot;
    private SpriteRenderer rockBackRenderer;
    private SpriteRenderer rockFrontRenderer;
    private SpriteRenderer wreckRenderer;
    private SpriteRenderer cisternRenderer;
    private SpriteRenderer cisternWaterSurfaceRenderer;
    private SpriteRenderer cisternSaltLineRenderer;
    private SpriteRenderer cisternLeakStreamRenderer;
    private SpriteRenderer cisternPlatePatchRenderer;
    private SpriteRenderer carriedPartRenderer;
    private readonly SpriteRenderer[] salvageRenderers = new SpriteRenderer[3];
    private readonly SpriteRenderer[] shelterStagedRenderers = new SpriteRenderer[3];
    private readonly SpriteRenderer[] boatStagedRenderers = new SpriteRenderer[3];
    private readonly SpriteRenderer[] plants = new SpriteRenderer[8];
    private readonly SpriteRenderer[] gutters = new SpriteRenderer[3];
    private float groundY;
    private float lastPresentationTime;

    public TideRainCisternState Cistern => cistern;
    public bool CisternPlatePatchApplied => cisternPlatePatchApplied;
    public TideIslandSalvagePart CarriedPart => carriedPart;
    public int ShelterStagedParts => shelterStagedParts;
    public int BoatStagedParts => boatStagedParts;
    public bool IsDismantling => dismantleWorkActive;
    public TideIslandSalvagePart ActiveDismantlePart => activeDismantlePart;

    public TideIslandSalvageDestination GetDestination(TideIslandSalvagePart part)
    {
        return part == TideIslandSalvagePart.HullPlank ? hullPlankDestination :
            part == TideIslandSalvagePart.Sailcloth ? sailclothDestination :
            part == TideIslandSalvagePart.RivetedPlate ? rivetedPlateDestination :
            TideIslandSalvageDestination.None;
    }

    public int GetStagedPartMask(TideIslandSalvageDestination stagingDestination)
    {
        int mask = 0;
        for (int i = 0; i < SalvageOffsets.Length; i++)
        {
            TideIslandSalvagePart part = (TideIslandSalvagePart)(i + 1);
            if (GetDestination(part) == stagingDestination)
            {
                mask |= TideSalvageMaterialModel.GetPartBit(part);
            }
        }
        return mask;
    }

    public bool TryIntegrateStagedPart(
        TideIslandSalvagePart part,
        TideIslandSalvageDestination stagingDestination)
    {
        if (part == TideIslandSalvagePart.None || GetDestination(part) != stagingDestination)
        {
            return false;
        }

        TideIslandSalvageDestination integratedDestination =
            stagingDestination == TideIslandSalvageDestination.ShelterStaging
                ? TideIslandSalvageDestination.IntegratedIntoShelter
                : stagingDestination == TideIslandSalvageDestination.EscapeBoatStaging
                    ? TideIslandSalvageDestination.IntegratedIntoEscapeBoat
                    : TideIslandSalvageDestination.None;
        if (integratedDestination == TideIslandSalvageDestination.None)
        {
            return false;
        }

        SetDestination(part, integratedDestination);
        if (stagingDestination == TideIslandSalvageDestination.ShelterStaging)
        {
            shelterStagedParts = Mathf.Max(0, shelterStagedParts - 1);
        }
        else
        {
            boatStagedParts = Mathf.Max(0, boatStagedParts - 1);
        }
        UpdateVisibility(true);
        return true;
    }

    private void OnEnable()
    {
        EnsureVisuals();
        if (cistern.StoredLiters <= 0f && cistern.Crack01 <= 0f)
        {
            cistern = TideRainCisternModel.CreateDamaged();
        }
    }

    public void ResetIsland()
    {
        cistern = TideRainCisternModel.CreateDamaged();
        cisternPlatePatchApplied = false;
        carriedPart = TideIslandSalvagePart.None;
        hullPlankRemoved = false;
        sailclothRemoved = false;
        rivetedPlateRemoved = false;
        shelterStagedParts = 0;
        boatStagedParts = 0;
        hullPlankDestination = TideIslandSalvageDestination.None;
        sailclothDestination = TideIslandSalvageDestination.None;
        rivetedPlateDestination = TideIslandSalvageDestination.None;
        activeDismantlePart = TideIslandSalvagePart.None;
        hullPlankDismantleProgress01 = 0f;
        sailclothDismantleProgress01 = 0f;
        rivetedPlateDismantleProgress01 = 0f;
        dismantleWorkActive = false;
        UpdateVisibility(true);
    }

    /// <summary>
    /// 把已经放在蓄水池旁的铆接板固定到裂口。这里同时提交水力状态和最终可见
    /// owner，避免数值先修好、原板却消失，或场景出现第二块凭空生成的补片。
    /// </summary>
    public bool ApplyCisternPlatePatch()
    {
        if (cisternPlatePatchApplied)
        {
            return false;
        }

        float before = cistern.Crack01;
        cistern = TideRainCisternModel.RepairCrack(cistern, 1f);
        cisternPlatePatchApplied = cistern.Crack01 < before - 0.001f;
        UpdateVisibility(visualRoot != null && visualRoot.gameObject.activeSelf);
        return cisternPlatePatchApplied;
    }

    public void TickNaturalState(
        float deltaSeconds,
        float rainMillimetersPerHour,
        float roofIntegrity01,
        float stormOvertopping01)
    {
        cistern = TideRainCisternModel.Advance(
            cistern,
            deltaSeconds,
            rainMillimetersPerHour,
            roofIntegrity01,
            stormOvertopping01);
    }

    /// <summary>
    /// 持续拆卸玩家身边的同一件船骸原物。开始必须有按键边沿，之后只有持续按住、
    /// 站在可见岩面且局部海况允许时才推进；松手和坏浪都保留已经松开的结构。
    /// </summary>
    public bool TickDismantleNearestPart(
        Vector2 playerPosition,
        float deltaSeconds,
        bool interactionPressed,
        bool interactionHeld,
        bool hasStableFooting,
        float waterSurfaceY,
        float localWaveLoad01,
        out TideIslandDismantleFeedback feedback)
    {
        feedback = default;
        dismantleWorkActive = false;
        if (carriedPart != TideIslandSalvagePart.None)
        {
            activeDismantlePart = TideIslandSalvagePart.None;
            return false;
        }

        if (activeDismantlePart == TideIslandSalvagePart.None)
        {
            if (!interactionPressed ||
                !TryGetNearestAvailablePart(playerPosition, out activeDismantlePart))
            {
                activeDismantlePart = TideIslandSalvagePart.None;
                return false;
            }
        }

        TideIslandSalvagePart part = activeDismantlePart;
        if (IsRemoved(part) ||
            Vector2.Distance(playerPosition, GetPartWorldPosition(part)) > 0.68f)
        {
            activeDismantlePart = TideIslandSalvagePart.None;
            return false;
        }

        TideWreckDismantleStep step = TideWreckDismantleModel.Advance(
            part,
            GetDismantleProgress01(part),
            deltaSeconds,
            interactionHeld,
            hasStableFooting,
            waterSurfaceY - groundY,
            localWaveLoad01);
        SetDismantleProgress01(part, step.Progress01);
        dismantleWorkActive = step.Worked;
        feedback = new TideIslandDismantleFeedback
        {
            Part = part,
            Progress01 = step.Progress01,
            WorkRate01 = step.WorkRate01,
            Worked = step.Worked,
            Completed = step.Completed,
            BlockReason = step.BlockReason
        };

        if (step.Completed)
        {
            SetRemoved(part, true);
            carriedPart = part;
            activeDismantlePart = TideIslandSalvagePart.None;
            dismantleWorkActive = false;
            UpdateVisibility(true);
        }
        else if (!interactionHeld)
        {
            // 目标选择属于这次按住动作；进度属于原物。重新靠近按 F 会从原位置继续。
            activeDismantlePart = TideIslandSalvagePart.None;
        }

        return true;
    }

    public float GetDismantleProgress01(TideIslandSalvagePart part)
    {
        return part == TideIslandSalvagePart.HullPlank ? hullPlankDismantleProgress01 :
            part == TideIslandSalvagePart.Sailcloth ? sailclothDismantleProgress01 :
            part == TideIslandSalvagePart.RivetedPlate ? rivetedPlateDismantleProgress01 : 0f;
    }

    public Vector2 GetPartWorldPosition(TideIslandSalvagePart part)
    {
        int index = (int)part - 1;
        return index >= 0 && index < SalvageOffsets.Length
            ? GetWreckCenter() + SalvageOffsets[index]
            : GetWreckCenter();
    }

    private bool TryGetNearestAvailablePart(
        Vector2 playerPosition,
        out TideIslandSalvagePart part)
    {
        part = TideIslandSalvagePart.None;
        float bestDistance = 0.68f;
        for (int i = 0; i < SalvageOffsets.Length; i++)
        {
            TideIslandSalvagePart candidate = (TideIslandSalvagePart)(i + 1);
            if (IsRemoved(candidate))
            {
                continue;
            }

            float distance = Vector2.Distance(playerPosition, GetPartWorldPosition(candidate));
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                part = candidate;
            }
        }

        return part != TideIslandSalvagePart.None;
    }

    private void SetDismantleProgress01(TideIslandSalvagePart part, float progress01)
    {
        float clamped = Mathf.Clamp01(progress01);
        if (part == TideIslandSalvagePart.HullPlank)
        {
            hullPlankDismantleProgress01 = clamped;
        }
        else if (part == TideIslandSalvagePart.Sailcloth)
        {
            sailclothDismantleProgress01 = clamped;
        }
        else if (part == TideIslandSalvagePart.RivetedPlate)
        {
            rivetedPlateDismantleProgress01 = clamped;
        }
    }

    public bool TryStageCarriedPart(TideIslandSalvageUse use, out TideIslandSalvagePart part)
    {
        part = carriedPart;
        if (part == TideIslandSalvagePart.None)
        {
            return false;
        }

        carriedPart = TideIslandSalvagePart.None;
        TideIslandSalvageDestination destination = use == TideIslandSalvageUse.Shelter
            ? TideIslandSalvageDestination.ShelterStaging
            : TideIslandSalvageDestination.EscapeBoatStaging;
        SetDestination(part, destination);
        if (use == TideIslandSalvageUse.Shelter)
        {
            shelterStagedParts++;
        }
        else
        {
            boatStagedParts++;
        }

        UpdateVisibility(true);
        return true;
    }

    public bool TryDrink(Vector2 playerPosition, float liters, out float consumedLiters)
    {
        consumedLiters = 0f;
        if (Mathf.Abs(playerPosition.x - CisternX) > 0.48f)
        {
            return false;
        }

        cistern = TideRainCisternModel.WithdrawPotableWater(
            cistern,
            liters,
            true,
            out TidePortableWaterState withdrawn);
        consumedLiters = withdrawn.Liters;
        return consumedLiters > 0f;
    }

    public bool TryFillPortableWaterContainer(
        float requestedLiters,
        out TidePortableWaterState container)
    {
        cistern = TideRainCisternModel.WithdrawPotableWater(
            cistern,
            requestedLiters,
            true,
            out container);
        return container.Liters + 0.0001f >= Mathf.Max(0f, requestedLiters);
    }

    public float WithdrawPotableWater(float requestedLiters)
    {
        cistern = TideRainCisternModel.WithdrawPotableWater(
            cistern,
            requestedLiters,
            false,
            out TidePortableWaterState withdrawn);
        return withdrawn.Liters;
    }

    public bool IsNearWreck(Vector2 playerPosition)
    {
        return Mathf.Abs(playerPosition.x - GetWreckCenter().x) <= 2.15f;
    }

    public bool IsNearCistern(Vector2 playerPosition)
    {
        return Mathf.Abs(playerPosition.x - CisternX) <= 0.52f;
    }

    public bool IsVisibleWalkSupportAt(Vector2 feetPosition)
    {
        // 岩礁没有虚构的矩形碰撞台。这里验证的是同一个声明式岩面：脚点在
        // 可见前景岩板的横向范围内，并且 Y 与表现更新时登记的 groundY 一致。
        return visualRoot != null && visualRoot.gameObject.activeInHierarchy &&
            rockFrontRenderer != null && rockFrontRenderer.enabled &&
            feetPosition.x >= WalkableLeftX && feetPosition.x <= WalkableRightX &&
            feetPosition.x >= rockFrontRenderer.bounds.min.x - 0.02f &&
            feetPosition.x <= rockFrontRenderer.bounds.max.x + 0.02f &&
            Mathf.Abs(feetPosition.y - groundY) <= 0.02f;
    }

    public void UpdatePresentation(
        bool visible,
        float walkSurfaceY,
        Vector2 playerPosition,
        Vector2 shelterStagingAnchor,
        Vector2 boatStagingAnchor,
        float signedWind,
        float waterSurfaceY,
        float time,
        bool cisternRepairActive = false,
        float cisternRepairProgress01 = 0f)
    {
        EnsureVisuals();
        groundY = walkSurfaceY;
        lastPresentationTime = time;
        UpdateVisibility(visible);
        if (!visible)
        {
            return;
        }

        SetWorldSize(rockBackRenderer, new Vector2(-10.7f, groundY - 0.42f), new Vector2(8.35f, 2.5f), 0f);
        SetWorldSize(rockFrontRenderer, new Vector2(-10.55f, groundY - 0.68f), new Vector2(8.65f, 2.15f), 0f);

        Vector2 wreckCenter = GetWreckCenter();
        SetWorldSize(wreckRenderer, wreckCenter + new Vector2(0f, 0.62f), new Vector2(5.25f, 2.95f), -5f);
        for (int i = 0; i < salvageRenderers.Length; i++)
        {
            SpriteRenderer renderer = salvageRenderers[i];
            TideIslandSalvagePart part = (TideIslandSalvagePart)(i + 1);
            Vector2 size = i == 0 ? new Vector2(1.45f, 0.22f) :
                i == 1 ? new Vector2(0.98f, 0.72f) : new Vector2(0.64f, 0.38f);
            float baseRotation = i == 0 ? -8f : i == 1 ? -14f : 7f;
            float loosen01 = GetDismantleProgress01(part);
            Vector2 loosenOffset = part == TideIslandSalvagePart.HullPlank
                ? new Vector2(0.05f, -0.08f) * loosen01
                : part == TideIslandSalvagePart.Sailcloth
                    ? new Vector2(0.1f, -0.16f) * loosen01
                    : new Vector2(-0.04f, -0.07f) * loosen01;
            float loosenRotation = part == TideIslandSalvagePart.HullPlank ? 11f :
                part == TideIslandSalvagePart.Sailcloth ? -9f : 14f;
            SetWorldSize(
                renderer,
                wreckCenter + SalvageOffsets[i] + loosenOffset,
                size,
                baseRotation + loosenRotation * loosen01);
        }

        SetWorldSize(cisternRenderer, new Vector2(CisternX, groundY + 0.52f), new Vector2(1.18f, 1.06f), 0f);
        float fill01 = Mathf.Clamp01(cistern.StoredLiters / TideRainCisternModel.CapacityLiters);
        float cisternSurfaceY = groundY + 0.13f + fill01 * 0.67f;
        cisternWaterSurfaceRenderer.enabled = cistern.StoredLiters > 0.25f;
        if (cisternWaterSurfaceRenderer.enabled)
        {
            SetWorldSize(
                cisternWaterSurfaceRenderer,
                new Vector2(CisternX, cisternSurfaceY),
                new Vector2(0.82f, 0.028f),
                0f);
            cisternWaterSurfaceRenderer.color = new Color(0.31f, 0.58f, 0.63f, 0.86f);
        }

        // 盐线是池壁留下的历史痕迹，不得继续兼任当前水面。否则水量下降时
        // 玩家只能看到一条悬空不动的线，无法读出裂缝正在漏水。
        float saltLine01 = Mathf.Clamp01(cistern.HighestSaltLine01);
        SetWorldSize(
            cisternSaltLineRenderer,
            new Vector2(CisternX, groundY + 0.13f + saltLine01 * 0.67f),
            new Vector2(0.86f, 0.022f),
            0f);
        cisternSaltLineRenderer.color = Color.Lerp(
            new Color(0.78f, 0.82f, 0.78f, 0.55f),
            new Color(0.94f, 0.95f, 0.9f, 0.92f),
            TideRainCisternModel.GetSaltContamination01(cistern));

        float visibleLeak01 = GetVisibleCisternLeak01();
        cisternLeakStreamRenderer.enabled = cistern.StoredLiters > 0.25f && visibleLeak01 > 0.025f;
        if (cisternLeakStreamRenderer.enabled)
        {
            Vector2 leakStart = new Vector2(CisternX + 0.11f, groundY + 0.48f);
            Vector2 leakEnd = new Vector2(CisternX + 0.18f, groundY + 0.035f);
            SetSegment(
                cisternLeakStreamRenderer,
                leakStart,
                leakEnd,
                Mathf.Lerp(0.008f, 0.034f, visibleLeak01));
            cisternLeakStreamRenderer.color = new Color(
                0.31f,
                0.6f,
                0.65f,
                Mathf.Lerp(0.42f, 0.82f, visibleLeak01));
        }

        bool plateStagedAtCistern = GetDestination(TideIslandSalvagePart.RivetedPlate) ==
            TideIslandSalvageDestination.ShelterStaging;
        bool plateTestFitted = cisternRepairActive && plateStagedAtCistern &&
            cisternRepairProgress01 >= 0.42f;
        cisternPlatePatchRenderer.enabled = cisternPlatePatchApplied || plateTestFitted;
        if (cisternPlatePatchRenderer.enabled)
        {
            SetWorldSize(
                cisternPlatePatchRenderer,
                new Vector2(CisternX + 0.12f, groundY + 0.51f),
                new Vector2(0.34f, 0.46f),
                5f);
        }

        UpdateGutters();
        UpdatePlants(signedWind, time);
        UpdateCarriedPart(playerPosition);
        UpdateStagedParts(shelterStagingAnchor, boatStagingAnchor, plateTestFitted);

        // 岛前缘与水面只在真实高度相交，不能另画一条局部水线。
        float submerged01 = Mathf.InverseLerp(groundY - 0.55f, groundY + 0.2f, waterSurfaceY);
        rockFrontRenderer.color = Color.Lerp(Color.white, new Color(0.72f, 0.84f, 0.84f, 1f), submerged01 * 0.36f);
    }

    private void EnsureVisuals()
    {
        if (visualRoot == null)
        {
            Transform existing = transform.Find("GeneratedBarrenIslandRoot");
            visualRoot = existing != null ? existing : new GameObject("GeneratedBarrenIslandRoot").transform;
            visualRoot.SetParent(transform, false);
        }

        rockBackRenderer = EnsureRenderer("RockBack", GetRockBackSprite(), -13);
        rockFrontRenderer = EnsureRenderer("RockFront", GetRockFrontSprite(), 1);
        wreckRenderer = EnsureRenderer(
            "ShipwreckBase",
            Resources.Load<Sprite>("StiltFirstSliceAI/AIShipwreck") ?? GetRockBackSprite(),
            2);
        salvageRenderers[0] = EnsureRenderer("SalvageHullPlank", GetPlankSprite(), 4);
        salvageRenderers[1] = EnsureRenderer("SalvageSailcloth", GetClothSprite(), 4);
        salvageRenderers[2] = EnsureRenderer("SalvageRivetedPlate", GetPlateSprite(), 4);
        cisternRenderer = EnsureRenderer("CrackedRainCistern", GetCisternSprite(), 4);
        cisternWaterSurfaceRenderer = EnsureRenderer("CisternWaterSurface", GetSaltLineSprite(), 5);
        cisternSaltLineRenderer = EnsureRenderer("CisternSaltLine", GetSaltLineSprite(), 6);
        cisternLeakStreamRenderer = EnsureRenderer("CisternLeakStream", GetSaltLineSprite(), 7);
        cisternPlatePatchRenderer = EnsureRenderer("CisternRivetedPatch", GetPlateSprite(), 8);
        carriedPartRenderer = EnsureRenderer("CarriedWreckPart", null, 15);
        for (int i = 0; i < salvageRenderers.Length; i++)
        {
            TideIslandSalvagePart part = (TideIslandSalvagePart)(i + 1);
            shelterStagedRenderers[i] = EnsureRenderer(
                $"StagedAtShelter_{part}",
                GetSpriteForPart(part),
                7);
            boatStagedRenderers[i] = EnsureRenderer(
                $"StagedAtBoat_{part}",
                GetSpriteForPart(part),
                7);
        }
        for (int i = 0; i < plants.Length; i++)
        {
            plants[i] = EnsureRenderer($"WindPressedPlant_{i:00}", GetPlantSprite(), 3);
        }
        for (int i = 0; i < gutters.Length; i++)
        {
            gutters[i] = EnsureRenderer($"RainGutter_{i:00}", GetPipeSprite(), 5);
        }
    }

    private SpriteRenderer EnsureRenderer(string name, Sprite sprite, int sortingOrder)
    {
        Transform child = visualRoot.Find(name);
        GameObject target = child != null ? child.gameObject : new GameObject(name);
        if (child == null)
        {
            target.transform.SetParent(visualRoot, false);
        }

        SpriteRenderer renderer = target.GetComponent<SpriteRenderer>();
        if (renderer == null)
        {
            renderer = target.AddComponent<SpriteRenderer>();
        }

        renderer.sprite = sprite;
        renderer.sortingOrder = sortingOrder;
        return renderer;
    }

    private void UpdateVisibility(bool visible)
    {
        if (visualRoot == null)
        {
            return;
        }

        visualRoot.gameObject.SetActive(visible);
        if (!visible)
        {
            return;
        }

        salvageRenderers[0].enabled = !hullPlankRemoved;
        salvageRenderers[1].enabled = !sailclothRemoved;
        salvageRenderers[2].enabled = !rivetedPlateRemoved;
        carriedPartRenderer.enabled = carriedPart != TideIslandSalvagePart.None;
        cisternWaterSurfaceRenderer.enabled = cistern.StoredLiters > 0.25f;
        cisternSaltLineRenderer.enabled = true;
        cisternLeakStreamRenderer.enabled = cistern.StoredLiters > 0.25f &&
            GetVisibleCisternLeak01() > 0.025f;
        cisternPlatePatchRenderer.enabled = cisternPlatePatchApplied;
        for (int i = 0; i < salvageRenderers.Length; i++)
        {
            TideIslandSalvageDestination destination = GetDestination((TideIslandSalvagePart)(i + 1));
            shelterStagedRenderers[i].enabled = destination == TideIslandSalvageDestination.ShelterStaging;
            boatStagedRenderers[i].enabled = destination == TideIslandSalvageDestination.EscapeBoatStaging;
        }
    }

    private void UpdateGutters()
    {
        Vector2 roofEdge = new Vector2(-5.62f, groundY + 2.7f);
        Vector2 tankTop = new Vector2(CisternX, groundY + 1.05f);
        SetSegment(gutters[0], roofEdge, new Vector2(CisternX + 0.5f, roofEdge.y), 0.055f);
        SetSegment(gutters[1], new Vector2(CisternX + 0.5f, roofEdge.y), new Vector2(CisternX + 0.5f, tankTop.y), 0.05f);
        SetSegment(gutters[2], new Vector2(CisternX + 0.5f, tankTop.y), tankTop, 0.05f);
    }

    private float GetVisibleCisternLeak01()
    {
        float fill01 = Mathf.Clamp01(cistern.StoredLiters / TideRainCisternModel.CapacityLiters);
        return Mathf.InverseLerp(0.08f, 0.72f, cistern.Crack01) * Mathf.Sqrt(fill01);
    }

    private void UpdatePlants(float signedWind, float time)
    {
        float direction = Mathf.Abs(signedWind) < 0.03f ? -1f : Mathf.Sign(signedWind);
        float bend = Mathf.Lerp(12f, 34f, Mathf.Clamp01(Mathf.Abs(signedWind) / 0.8f));
        for (int i = 0; i < plants.Length; i++)
        {
            float x = -9.35f + i * 0.38f;
            float y = groundY + 0.03f + Mathf.Sin(i * 1.37f) * 0.025f;
            float sharedGust = Mathf.Sin(time * 0.65f + i * 0.18f) * 2.2f;
            SetWorldSize(plants[i], new Vector2(x, y + 0.12f), new Vector2(0.28f, 0.24f), -direction * (bend + sharedGust));
        }
    }

    private void UpdateCarriedPart(Vector2 playerPosition)
    {
        if (carriedPart == TideIslandSalvagePart.None)
        {
            carriedPartRenderer.enabled = false;
            return;
        }

        carriedPartRenderer.enabled = true;
        carriedPartRenderer.sprite = GetSpriteForPart(carriedPart);
        Vector2 size = carriedPart == TideIslandSalvagePart.HullPlank
            ? new Vector2(0.92f, 0.14f)
            : carriedPart == TideIslandSalvagePart.Sailcloth
                ? new Vector2(0.58f, 0.43f)
                : new Vector2(0.42f, 0.25f);
        SetWorldSize(carriedPartRenderer, playerPosition + new Vector2(0f, 0.03f), size, -7f);
    }

    private void UpdateStagedParts(
        Vector2 shelterAnchor,
        Vector2 boatAnchor,
        bool plateTestFitted)
    {
        for (int i = 0; i < salvageRenderers.Length; i++)
        {
            TideIslandSalvagePart part = (TideIslandSalvagePart)(i + 1);
            TideIslandSalvageDestination destination = GetDestination(part);
            SpriteRenderer renderer = destination == TideIslandSalvageDestination.ShelterStaging
                ? shelterStagedRenderers[i]
                : boatStagedRenderers[i];
            if (destination != TideIslandSalvageDestination.ShelterStaging &&
                destination != TideIslandSalvageDestination.EscapeBoatStaging)
            {
                continue;
            }

            // 铆接板的住所用途就是裂池本体。玩家必须把它搬到池边，试装阶段后
            // 同一 owner 才从地面切到裂口；不能在屋侧堆场和池壁同时出现两块板。
            bool plateAtCistern = part == TideIslandSalvagePart.RivetedPlate &&
                destination == TideIslandSalvageDestination.ShelterStaging;
            if (plateAtCistern && plateTestFitted)
            {
                renderer.enabled = false;
                continue;
            }

            // 三件原物分别靠在施工位、叠在干木面和放在可见检修面上；它们不跟
            // 船的浪上浮沉，因为此时尚未固定到船体，也不能提前获得维修收益。
            Vector2 anchor = plateAtCistern
                ? new Vector2(CisternX - 0.46f, groundY + 0.02f)
                : destination == TideIslandSalvageDestination.ShelterStaging
                    ? shelterAnchor
                    : boatAnchor;
            Vector2 offset = part == TideIslandSalvagePart.HullPlank
                ? new Vector2(0f, 0.11f)
                : part == TideIslandSalvagePart.Sailcloth
                    ? new Vector2(-0.32f, 0.24f)
                    : new Vector2(0.35f, 0.2f);
            Vector2 size = part == TideIslandSalvagePart.HullPlank
                ? new Vector2(1.08f, 0.16f)
                : part == TideIslandSalvagePart.Sailcloth
                    ? new Vector2(0.54f, 0.4f)
                    : new Vector2(0.4f, 0.24f);
            float rotation = part == TideIslandSalvagePart.HullPlank ? 0f :
                part == TideIslandSalvagePart.Sailcloth ? -9f : 4f;
            SetWorldSize(renderer, anchor + offset, size, rotation);
        }
    }

    private Vector2 GetWreckCenter()
    {
        return new Vector2(-12.25f, groundY + 0.4f);
    }

    private bool IsRemoved(TideIslandSalvagePart part)
    {
        return part == TideIslandSalvagePart.HullPlank ? hullPlankRemoved :
            part == TideIslandSalvagePart.Sailcloth ? sailclothRemoved :
            part == TideIslandSalvagePart.RivetedPlate && rivetedPlateRemoved;
    }

    private void SetRemoved(TideIslandSalvagePart part, bool removed)
    {
        if (part == TideIslandSalvagePart.HullPlank)
        {
            hullPlankRemoved = removed;
        }
        else if (part == TideIslandSalvagePart.Sailcloth)
        {
            sailclothRemoved = removed;
        }
        else if (part == TideIslandSalvagePart.RivetedPlate)
        {
            rivetedPlateRemoved = removed;
        }
    }

    private void SetDestination(TideIslandSalvagePart part, TideIslandSalvageDestination destination)
    {
        if (part == TideIslandSalvagePart.HullPlank)
        {
            hullPlankDestination = destination;
        }
        else if (part == TideIslandSalvagePart.Sailcloth)
        {
            sailclothDestination = destination;
        }
        else if (part == TideIslandSalvagePart.RivetedPlate)
        {
            rivetedPlateDestination = destination;
        }
    }

    private static Sprite GetSpriteForPart(TideIslandSalvagePart part)
    {
        return part == TideIslandSalvagePart.HullPlank ? GetPlankSprite() :
            part == TideIslandSalvagePart.Sailcloth ? GetClothSprite() : GetPlateSprite();
    }

    private static void SetWorldSize(SpriteRenderer renderer, Vector2 center, Vector2 size, float rotationDegrees)
    {
        if (renderer == null || renderer.sprite == null)
        {
            return;
        }

        Vector2 spriteSize = renderer.sprite.bounds.size;
        renderer.transform.localPosition = new Vector3(center.x, center.y, 0f);
        renderer.transform.localRotation = Quaternion.Euler(0f, 0f, rotationDegrees);
        renderer.transform.localScale = new Vector3(
            size.x / Mathf.Max(0.001f, spriteSize.x),
            size.y / Mathf.Max(0.001f, spriteSize.y),
            1f);
    }

    private static void SetSegment(SpriteRenderer renderer, Vector2 start, Vector2 end, float thickness)
    {
        Vector2 delta = end - start;
        SetWorldSize(
            renderer,
            (start + end) * 0.5f,
            new Vector2(delta.magnitude, thickness),
            Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }

    private static Sprite GetRockBackSprite()
    {
        return rockBackSprite ??= CreateRockSprite("TideBarrenRockBack", new Color32(38, 47, 49, 255), 17);
    }

    private static Sprite GetRockFrontSprite()
    {
        return rockFrontSprite ??= CreateRockSprite("TideBarrenRockFront", new Color32(25, 32, 34, 255), 43);
    }

    /// <summary>
    /// 短航浅礁与出生岩礁共用同一种岩性和纹理密度。返回运行时缓存 Sprite，
    /// 不复制 PNG，也不允许浅礁控制器生成另一套画风相近但尺寸不同的临时石块。
    /// </summary>
    public static Sprite GetSharedReefRockSprite()
    {
        return GetRockFrontSprite();
    }

    private static Sprite GetCisternSprite()
    {
        if (cisternSprite != null)
        {
            return cisternSprite;
        }

        Texture2D texture = NewTexture("TideCrackedCistern", 128, 128);
        Color32 clear = new Color32(0, 0, 0, 0);
        Color32 steel = new Color32(83, 91, 88, 255);
        Color32 dark = new Color32(39, 46, 44, 255);
        Color32 rust = new Color32(103, 69, 51, 255);
        for (int y = 0; y < 128; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                bool body = x >= 14 && x <= 113 && y >= 12 && y <= 109;
                if (!body)
                {
                    texture.SetPixel(x, y, clear);
                    continue;
                }

                bool rim = x < 19 || x > 108 || y < 18 || y > 103;
                bool band = y == 35 || y == 72;
                bool crack = x > 68 && x < 73 && y < 58 && ((y / 6) % 2 == 0 ? x == 69 : x == 72);
                texture.SetPixel(x, y, crack ? dark : rim || band ? rust : steel);
            }
        }

        cisternSprite = FinalizeSprite(texture, new Vector2(0.5f, 0.5f), 128f);
        return cisternSprite;
    }

    private static Sprite GetSaltLineSprite()
    {
        return saltLineSprite ??= CreateSolidSprite("TideCisternSaltLine", new Color32(232, 234, 220, 220));
    }

    private static Sprite GetPlantSprite()
    {
        if (plantSprite != null)
        {
            return plantSprite;
        }

        Texture2D texture = NewTexture("TideWindPressedPlant", 64, 32);
        Color32 clear = new Color32(0, 0, 0, 0);
        Color32 moss = new Color32(67, 81, 65, 255);
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                bool blade = (x > 8 && x < 56 && y < 5) ||
                    (x > 18 + y && x < 22 + y && y < 21) ||
                    (x > 34 + y / 2 && x < 38 + y / 2 && y < 17);
                texture.SetPixel(x, y, blade ? moss : clear);
            }
        }

        plantSprite = FinalizeSprite(texture, new Vector2(0.5f, 0f), 64f);
        return plantSprite;
    }

    private static Sprite GetPlankSprite()
    {
        if (plankSprite != null)
        {
            return plankSprite;
        }

        Texture2D texture = NewTexture("TideWreckHullPlank", 192, 32);
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 192; x++)
            {
                bool edge = y < 3 || y > 28 || x < 4 || x > 187;
                bool rivet = (x == 20 || x == 96 || x == 171) && y >= 14 && y <= 18;
                Color32 color = edge ? new Color32(42, 34, 29, 255) :
                    rivet ? new Color32(132, 118, 92, 255) : new Color32(92, 72, 55, 255);
                texture.SetPixel(x, y, color);
            }
        }

        plankSprite = FinalizeSprite(texture, new Vector2(0.5f, 0.5f), 96f);
        return plankSprite;
    }

    private static Sprite GetClothSprite()
    {
        if (clothSprite != null)
        {
            return clothSprite;
        }

        Texture2D texture = NewTexture("TideWreckSailcloth", 128, 96);
        Color32 clear = new Color32(0, 0, 0, 0);
        Color32 cloth = new Color32(150, 145, 127, 235);
        Color32 seam = new Color32(83, 75, 64, 235);
        for (int y = 0; y < 96; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                bool tornShape = x > 8 + y / 8 && x < 119 - (y % 17) / 3 && y > 7 && y < 88 - (x % 19) / 5;
                bool stitched = tornShape && (x == 44 || y == 47);
                texture.SetPixel(x, y, !tornShape ? clear : stitched ? seam : cloth);
            }
        }

        clothSprite = FinalizeSprite(texture, new Vector2(0.5f, 0.5f), 96f);
        return clothSprite;
    }

    private static Sprite GetPlateSprite()
    {
        if (plateSprite != null)
        {
            return plateSprite;
        }

        Texture2D texture = NewTexture("TideWreckRivetedPlate", 96, 56);
        Color32 clear = new Color32(0, 0, 0, 0);
        for (int y = 0; y < 56; y++)
        {
            for (int x = 0; x < 96; x++)
            {
                bool body = x > 5 && x < 90 && y > 4 && y < 51;
                bool edge = body && (x < 10 || x > 85 || y < 9 || y > 46);
                bool rivet = body && ((x == 18 || x == 78) && (y == 16 || y == 40));
                texture.SetPixel(x, y, !body ? clear : rivet ? new Color32(183, 153, 109, 255) :
                    edge ? new Color32(60, 53, 47, 255) : new Color32(102, 83, 67, 255));
            }
        }

        plateSprite = FinalizeSprite(texture, new Vector2(0.5f, 0.5f), 96f);
        return plateSprite;
    }

    private static Sprite GetPipeSprite()
    {
        return pipeSprite ??= CreateSolidSprite("TideRainGutterPipe", new Color32(67, 73, 72, 255));
    }

    private static Sprite CreateRockSprite(string name, Color32 baseColor, int seed)
    {
        const int width = 512;
        const int height = 256;
        Texture2D texture = NewTexture(name, width, height);
        Color32 clear = new Color32(0, 0, 0, 0);
        for (int x = 0; x < width; x++)
        {
            float broad = Mathf.Sin((x + seed) * 0.031f) * 11f + Mathf.Sin((x + seed * 3) * 0.083f) * 5f;
            int surface = Mathf.RoundToInt(169f + broad);
            for (int y = 0; y < height; y++)
            {
                if (y > surface)
                {
                    texture.SetPixel(x, y, clear);
                    continue;
                }

                float depth01 = Mathf.Clamp01((surface - y) / 150f);
                int seam = ((x * 17 + y * 11 + seed) % 97 == 0) ? -22 : 0;
                byte r = (byte)Mathf.Clamp(baseColor.r - depth01 * 18f + seam, 0f, 255f);
                byte g = (byte)Mathf.Clamp(baseColor.g - depth01 * 15f + seam, 0f, 255f);
                byte b = (byte)Mathf.Clamp(baseColor.b - depth01 * 13f + seam, 0f, 255f);
                texture.SetPixel(x, y, new Color32(r, g, b, 255));
            }
        }

        return FinalizeSprite(texture, new Vector2(0.5f, 0.5f), 128f);
    }

    private static Sprite CreateSolidSprite(string name, Color32 color)
    {
        Texture2D texture = NewTexture(name, 8, 8);
        Color32[] pixels = new Color32[64];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }
        texture.SetPixels32(pixels);
        return FinalizeSprite(texture, new Vector2(0.5f, 0.5f), 8f);
    }

    private static Texture2D NewTexture(string name, int width, int height)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        return texture;
    }

    private static Sprite FinalizeSprite(Texture2D texture, Vector2 pivot, float pixelsPerUnit)
    {
        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            pivot,
            pixelsPerUnit,
            0,
            SpriteMeshType.FullRect);
        sprite.name = texture.name;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
