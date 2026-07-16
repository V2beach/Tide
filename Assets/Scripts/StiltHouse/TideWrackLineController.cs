using UnityEngine;

/// <summary>
/// 右岸高潮漂积线的一件实体。组件拥有搁浅/卷走/拾取状态和 Renderer，
/// 不生成库存、不计算潮位，也不添加发光标记或碰撞热区。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class TideWrackLineController : MonoBehaviour
{
    public const string VisualName = "GeneratedStiltFirstPhysicalWrackDeposit";
    public const float SeawardWorldX = -8.08f;
    public const float InlandWorldX = -9.28f;

    private static TideV59FindCatalog catalog;

    [SerializeField] private SpriteRenderer depositRenderer;
    private TideWrackDepositState deposit;

    public TideWrackDepositState Deposit => deposit;
    public bool HasDeposit => deposit.IsPresent;
    public bool IsVisible => depositRenderer != null && depositRenderer.enabled;
    public bool HasCollider => depositRenderer != null &&
        depositRenderer.GetComponent<Collider2D>() != null;
    public Vector2 VisualCenter => depositRenderer != null
        ? (Vector2)depositRenderer.bounds.center
        : Vector2.zero;
    public float SampleWorldX => deposit.IsPresent ? deposit.WorldX : SeawardWorldX;

    private void OnEnable()
    {
        EnsureRenderer();
    }

    public void ResetFeature()
    {
        deposit = default;
        Hide();
    }

    public bool TrySettle(
        TideDriftBatch batch,
        int astronomicalCycleOrdinal,
        float peakWaterY,
        float groundY,
        bool captured,
        bool stillNearshore)
    {
        TideWrackDepositState previous = deposit;
        deposit = TideWrackDepositModel.TrySettle(
            deposit,
            batch,
            astronomicalCycleOrdinal,
            peakWaterY,
            groundY,
            SeawardWorldX,
            InlandWorldX,
            captured,
            stillNearshore);
        return deposit.IsPresent &&
            (!previous.IsPresent || previous.BatchId != deposit.BatchId);
    }

    public bool TickNaturalState(
        int currentAstronomicalCycleOrdinal,
        float localWaterSurfaceY)
    {
        if (!TideWrackDepositModel.ShouldRefloat(
            deposit,
            currentAstronomicalCycleOrdinal,
            localWaterSurfaceY))
        {
            return false;
        }

        deposit = default;
        Hide();
        return true;
    }

    public bool TryCollect(
        Vector2 playerFeetPosition,
        float localWaterSurfaceY,
        out TideWrackDepositState collected)
    {
        collected = default;
        if (!deposit.IsPresent ||
            localWaterSurfaceY > deposit.GroundY - 0.1f ||
            Mathf.Abs(playerFeetPosition.x - deposit.WorldX) > 0.52f ||
            Mathf.Abs(playerFeetPosition.y - deposit.GroundY) > 0.08f)
        {
            return false;
        }

        collected = deposit;
        deposit = default;
        Hide();
        return true;
    }

    public void UpdatePresentation(bool visible)
    {
        EnsureRenderer();
        Sprite sprite = null;
        TideV59FindSpec spec = default;
        bool show = visible && deposit.IsPresent && TryResolveVisual(
            deposit,
            out sprite,
            out spec);
        depositRenderer.enabled = show;
        if (!show)
        {
            return;
        }

        depositRenderer.sprite = sprite;
        depositRenderer.sortingOrder = 14;
        depositRenderer.color = Color.white;
        depositRenderer.flipX = PositiveModulo(deposit.BatchId, 2) == 1;
        depositRenderer.transform.position = new Vector3(
            deposit.WorldX,
            deposit.GroundY + spec.VisibleWorldSize.y * 0.5f,
            0f);
        depositRenderer.transform.rotation = Quaternion.Euler(
            0f,
            0f,
            deposit.RotationDegrees);
        Vector2 spriteSize = sprite.bounds.size;
        depositRenderer.transform.localScale = new Vector3(
            spec.VisibleWorldSize.x / Mathf.Max(0.001f, spriteSize.x),
            spec.VisibleWorldSize.y / Mathf.Max(0.001f, spriteSize.y),
            1f);
    }

    private static bool TryResolveVisual(
        TideWrackDepositState state,
        out Sprite sprite,
        out TideV59FindSpec spec)
    {
        if (catalog == null)
        {
            catalog = Resources.Load<TideV59FindCatalog>(
                "StiltFirstSliceAI/V59TideFindCatalog");
        }

        TideV59FindKind kind = ToFindKind(state.Material);
        int variant = TideV59FindPresentationModel.ResolveVariantIndex(
            kind,
            0,
            state.BatchId,
            false);
        sprite = catalog != null ? catalog.Get(kind, variant) : null;
        spec = TideV59FindPresentationModel.GetSpec(kind, variant);
        return sprite != null;
    }

    private static TideV59FindKind ToFindKind(TideDriftMaterial material)
    {
        switch (material)
        {
            case TideDriftMaterial.SaltWood:
                return TideV59FindKind.Wood;
            case TideDriftMaterial.ChartParcel:
                return TideV59FindKind.Relic;
            case TideDriftMaterial.TangledDebris:
                return TideV59FindKind.Trash;
            default:
                return TideV59FindKind.Fish;
        }
    }

    private void EnsureRenderer()
    {
        if (depositRenderer == null)
        {
            Transform child = transform.Find(VisualName);
            if (child == null)
            {
                GameObject childObject = new GameObject(VisualName);
                childObject.transform.SetParent(transform, false);
                child = childObject.transform;
            }

            depositRenderer = child.GetComponent<SpriteRenderer>();
            if (depositRenderer == null)
            {
                depositRenderer = child.gameObject.AddComponent<SpriteRenderer>();
            }
        }

        depositRenderer.enabled = false;
    }

    private void Hide()
    {
        if (depositRenderer != null)
        {
            depositRenderer.enabled = false;
        }
    }

    private static int PositiveModulo(int value, int modulo)
    {
        int result = value % modulo;
        return result < 0 ? result + modulo : result;
    }
}
