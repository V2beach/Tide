using UnityEngine;

/// <summary>
/// 把下一次高潮的不确定区间投影成同一根主桩上的两道麻绳结。
/// 它不计算潮位、不显示文字，也不提供正确答案；场景注入权威世界 Y，
/// presenter 只负责两个无碰撞的物理刻痕和合理遮挡层级。
/// </summary>
public sealed class TideForecastTideNotchController : MonoBehaviour
{
    public const string LowerNotchName = "GeneratedStiltFirstForecastLowerKnot";
    public const string UpperNotchName = "GeneratedStiltFirstForecastUpperKnot";

    [SerializeField] private SpriteRenderer lowerNotch;
    [SerializeField] private SpriteRenderer upperNotch;
    [SerializeField] private float staffWorldX;

    public bool IsVisible => lowerNotch != null && upperNotch != null &&
        lowerNotch.enabled && upperNotch.enabled;
    public float LowerWorldY => lowerNotch != null ? lowerNotch.transform.position.y : 0f;
    public float UpperWorldY => upperNotch != null ? upperNotch.transform.position.y : 0f;
    public float StaffWorldX => staffWorldX;
    public float VisibleBandWidthMeters => IsVisible ? UpperWorldY - LowerWorldY : 0f;
    public int SortingOrder => upperNotch != null ? upperNotch.sortingOrder : 0;
    public bool HasCollider =>
        (lowerNotch != null && lowerNotch.GetComponent<Collider2D>() != null) ||
        (upperNotch != null && upperNotch.GetComponent<Collider2D>() != null);

    public void UpdatePresentation(
        bool visible,
        float staffWorldX,
        TideNetForecastModel.HighWaterBand band,
        Sprite ropeSprite,
        bool repairedChart)
    {
        EnsureRenderers(ropeSprite);
        this.staffWorldX = staffWorldX;
        bool show = visible && ropeSprite != null && band.UpperY > band.LowerY;
        lowerNotch.enabled = show;
        upperNotch.enabled = show;
        if (!show)
        {
            return;
        }

        lowerNotch.sprite = ropeSprite;
        upperNotch.sprite = ropeSprite;
        lowerNotch.sortingOrder = 13;
        upperNotch.sortingOrder = 13;

        // 两道结故意略有宽窄、偏心和角度差，避免读成悬空 HUD 横线。
        // 修复海图潮尺只缩小上下间距，不通过发光或颜色告诉玩家答案。
        Color twine = repairedChart
            ? new Color(0.72f, 0.63f, 0.43f, 0.94f)
            : new Color(0.59f, 0.51f, 0.36f, 0.9f);
        SetWorldSize(
            lowerNotch,
            new Vector2(staffWorldX - 0.012f, band.LowerY),
            new Vector2(0.19f, 0.028f),
            twine,
            -3.5f);
        SetWorldSize(
            upperNotch,
            new Vector2(staffWorldX + 0.01f, band.UpperY),
            new Vector2(0.225f, 0.032f),
            twine,
            2.5f);
    }

    public void Hide()
    {
        if (lowerNotch != null)
        {
            lowerNotch.enabled = false;
        }
        if (upperNotch != null)
        {
            upperNotch.enabled = false;
        }
    }

    private void EnsureRenderers(Sprite sprite)
    {
        lowerNotch = EnsureRenderer(lowerNotch, LowerNotchName, sprite);
        upperNotch = EnsureRenderer(upperNotch, UpperNotchName, sprite);
    }

    private SpriteRenderer EnsureRenderer(
        SpriteRenderer current,
        string objectName,
        Sprite sprite)
    {
        if (current == null)
        {
            Transform child = transform.Find(objectName);
            if (child == null)
            {
                GameObject childObject = new GameObject(objectName);
                childObject.transform.SetParent(transform, false);
                child = childObject.transform;
            }

            current = child.GetComponent<SpriteRenderer>();
            if (current == null)
            {
                current = child.gameObject.AddComponent<SpriteRenderer>();
            }
        }

        current.sprite = sprite;
        current.enabled = false;
        return current;
    }

    private static void SetWorldSize(
        SpriteRenderer renderer,
        Vector2 worldPosition,
        Vector2 worldSize,
        Color color,
        float rotationDegrees)
    {
        renderer.transform.position = new Vector3(worldPosition.x, worldPosition.y, 0f);
        renderer.transform.rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
        renderer.color = color;
        Vector2 spriteSize = renderer.sprite != null
            ? renderer.sprite.bounds.size
            : Vector2.one;
        renderer.transform.localScale = new Vector3(
            worldSize.x / Mathf.Max(0.001f, spriteSize.x),
            worldSize.y / Mathf.Max(0.001f, spriteSize.y),
            1f);
    }
}
