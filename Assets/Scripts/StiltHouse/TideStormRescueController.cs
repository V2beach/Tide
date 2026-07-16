using System;
using UnityEngine;

public readonly struct TideStormRescueAdvanceResult
{
    public TideStormRescueAdvanceResult(
        bool cargoReleasedThisStep,
        int securedMask,
        int lostMask)
    {
        CargoReleasedThisStep = cargoReleasedThisStep;
        SecuredMask = securedMask;
        LostMask = lostMask;
    }

    public bool CargoReleasedThisStep { get; }
    public int SecuredMask { get; }
    public int LostMask { get; }
}

/// <summary>
/// 暴潮低层物资的唯一连续运行状态。组件负责搁架失效、当前拉绳目标、
/// 吊升、冲失和退水后整理；它不知道房屋库存、水罐升数、天气来源或剧情后果。
/// 主世界每帧注入同一房屋中的真实水深和流速，并消费一次性实物事件。
/// </summary>
public sealed class TideStormRescueController : MonoBehaviour
{
    public const int ItemCount = 4;
    public const float FloodDepthThresholdMeters = 0.02f;

    [SerializeField] private TideStormRescueItemState[] items = Array.Empty<TideStormRescueItemState>();
    [SerializeField] private int heldIndex = -1;
    [SerializeField] private bool floodStarted;
    [SerializeField] private bool cargoReleased;

    public bool FloodStarted => floodStarted;
    public bool CargoReleased => cargoReleased;

    public void ResetRuntime()
    {
        items = new TideStormRescueItemState[ItemCount];
        for (int i = 0; i < ItemCount; i++)
        {
            TideStormRescueItemState item = TideStormRescueModel.Create((TideStormRescueItemKind)i);
            item.Present = false;
            items[i] = item;
        }

        heldIndex = -1;
        floodStarted = false;
        cargoReleased = false;
    }

    public TideStormRescueItemState GetItem(int index)
    {
        EnsureItems();
        return index >= 0 && index < items.Length
            ? items[index]
            : default;
    }

    public TideStormRescueItemState GetItem(TideStormRescueItemKind kind)
    {
        return GetItem((int)kind);
    }

    public void SetItemPresent(TideStormRescueItemKind kind, bool present)
    {
        EnsureItems();
        int index = (int)kind;
        if (index < 0 || index >= items.Length)
        {
            return;
        }

        TideStormRescueItemState item = TideStormRescueModel.Create(kind);
        item.Present = present;
        items[index] = item;
        if (heldIndex == index)
        {
            heldIndex = -1;
        }
    }

    public bool TryHoldItem(int index)
    {
        EnsureItems();
        if (!cargoReleased || index < 0 || index >= items.Length)
        {
            return false;
        }

        TideStormRescueItemState item = items[index];
        if (!item.Present || item.Lost || item.Secured)
        {
            return false;
        }

        heldIndex = index;
        return true;
    }

    public void ClearInteraction()
    {
        heldIndex = -1;
    }

    public TideStormRescueAdvanceResult Advance(
        float deltaSeconds,
        float localWaterDepthMeters,
        float currentSpeedMetersPerSecond)
    {
        EnsureItems();
        float dt = Mathf.Max(0f, deltaSeconds);
        float depth = Mathf.Max(0f, localWaterDepthMeters);
        if (depth > FloodDepthThresholdMeters)
        {
            floodStarted = true;
        }

        bool releasedBefore = cargoReleased;
        cargoReleased = TideStormRescueModel.ShouldReleaseCargo(
            cargoReleased,
            depth,
            currentSpeedMetersPerSecond);

        int securedMask = 0;
        int lostMask = 0;
        for (int i = 0; i < items.Length; i++)
        {
            TideStormRescueItemState before = items[i];
            TideStormRescueItemState after = TideStormRescueModel.Advance(
                before,
                dt,
                cargoReleased ? depth : 0f,
                currentSpeedMetersPerSecond,
                cargoReleased && heldIndex == i);
            items[i] = after;

            if (!before.Secured && after.Secured)
            {
                securedMask |= 1 << i;
            }
            if (!before.Lost && after.Lost)
            {
                lostMask |= 1 << i;
            }
        }

        // 输入必须由人物每帧在真实交互距离内重新取得，不能松开 F 后继续隔空施工。
        heldIndex = -1;
        return new TideStormRescueAdvanceResult(
            !releasedBefore && cargoReleased,
            securedMask,
            lostMask);
    }

    public int SecureSurvivorsAfterRecede(float localWaterDepthMeters)
    {
        EnsureItems();
        if (!floodStarted || !cargoReleased ||
            localWaterDepthMeters > FloodDepthThresholdMeters)
        {
            return 0;
        }

        int securedMask = 0;
        for (int i = 0; i < items.Length; i++)
        {
            TideStormRescueItemState item = items[i];
            if (!item.Present || item.Lost || item.Secured)
            {
                continue;
            }

            item.Secured = true;
            item.SecuringProgress01 = 1f;
            item.WashoutProgress01 = 0f;
            items[i] = item;
            securedMask |= 1 << i;
        }

        return securedMask;
    }

    public bool HasUnresolvedCargo()
    {
        return CountUnresolvedCargo() > 0;
    }

    public int CountUnresolvedCargo()
    {
        EnsureItems();
        int count = 0;
        for (int i = 0; i < items.Length; i++)
        {
            TideStormRescueItemState item = items[i];
            if (item.Present && !item.Lost && !item.Secured)
            {
                count++;
            }
        }

        return count;
    }

    public int GetPresentMask()
    {
        EnsureItems();
        int mask = 0;
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].Present)
            {
                mask |= 1 << i;
            }
        }

        return mask;
    }

    public void RestoreFloodState(bool didFloodStart, bool didCargoRelease)
    {
        // Save/load and deterministic Scene probes need to restore these two physical
        // facts without replaying an arbitrary hidden wave through the world clock.
        floodStarted = didFloodStart;
        cargoReleased = didCargoRelease;
        heldIndex = -1;
    }

    private void EnsureItems()
    {
        if (items == null || items.Length != ItemCount)
        {
            ResetRuntime();
        }
    }
}
