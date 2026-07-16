using UnityEngine;

public readonly struct TideSailingReefMovementResult
{
    public TideSailingReefMovementResult(
        float resolvedBoatX,
        bool contactedReef,
        bool damagesHull,
        bool newlyPassedOutbound)
    {
        ResolvedBoatX = resolvedBoatX;
        ContactedReef = contactedReef;
        DamagesHull = damagesHull;
        NewlyPassedOutbound = newlyPassedOutbound;
    }

    public float ResolvedBoatX { get; }
    public bool ContactedReef { get; }
    public bool DamagesHull { get; }
    public bool NewlyPassedOutbound { get; }
}

/// <summary>
/// 浅礁在短航中的运行时边界。组件只拥有“是否真实越过”和撞击冷却，
/// 不保存潮位、浪场、船速、舱水或船体耐久；这些权威状态由主场景逐帧注入。
/// 物理约束与岩脊/碎浪表现都消费同一个 <see cref="TideSailingReefSample"/>，
/// 避免玩家看到可通行水面、碰撞却仍按另一套隐藏规则阻挡。
/// </summary>
public sealed class TideSailingReefController : MonoBehaviour
{
    private const float StrikeCooldownSeconds = 3.2f;
    private const float OutboundPassMarginMeters = 0.04f;

    [SerializeField] private bool passedOutbound;
    [SerializeField] private float strikeCooldownRemainingSeconds;

    public bool PassedOutbound => passedOutbound;
    public float StrikeCooldownRemainingSeconds => strikeCooldownRemainingSeconds;

    public void ResetRuntime()
    {
        passedOutbound = false;
        strikeCooldownRemainingSeconds = 0f;
    }

    public void AdvanceEnvironment(float deltaSeconds)
    {
        strikeCooldownRemainingSeconds = Mathf.Max(
            0f,
            strikeCooldownRemainingSeconds - Mathf.Max(0f, deltaSeconds));
    }

    /// <summary>
    /// 解析一段连续位移，而不是在礁点周围使用圆形热区。船底触礁时只把本帧
    /// 位移约束在同一固定岩脊边缘；是否扣船体由返回结果交给主场景结算。
    /// </summary>
    public TideSailingReefMovementResult ResolveMovement(
        float previousBoatX,
        float proposedBoatX,
        float horizontalSpeedMetersPerSecond,
        float reefCenterX,
        TideSailingReefSample sample)
    {
        bool contacted = TideSailingReefModel.SegmentEntersGroundedReef(
            previousBoatX,
            proposedBoatX,
            reefCenterX,
            sample);
        float resolvedBoatX = contacted
            ? TideSailingReefModel.ConstrainOutsideGroundedReef(
                previousBoatX,
                proposedBoatX,
                reefCenterX)
            : proposedBoatX;

        bool damagesHull = contacted &&
            strikeCooldownRemainingSeconds <= 0f &&
            TideSailingReefModel.ShouldDamageHull(sample, horizontalSpeedMetersPerSecond);
        if (damagesHull)
        {
            strikeCooldownRemainingSeconds = StrikeCooldownSeconds;
        }

        float rightEdge = reefCenterX + TideSailingReefModel.ReefHalfWidthMeters;
        bool newlyPassed = !passedOutbound &&
            previousBoatX <= rightEdge &&
            resolvedBoatX > rightEdge + OutboundPassMarginMeters;
        if (newlyPassed)
        {
            passedOutbound = true;
        }

        return new TideSailingReefMovementResult(
            resolvedBoatX,
            contacted,
            damagesHull,
            newlyPassed);
    }

    /// <summary>
    /// 岩脊和碎浪与碰撞共享同一净空样本。reefScreenPosition 已由主场景相机
    /// 投影；ocean 仍是权威连续海况，所以组件不会自行生成局部水线。
    /// </summary>
    public void UpdatePresentation(
        SpriteRenderer rockRenderer,
        SpriteRenderer foamRenderer,
        bool worldVisible,
        Vector2 reefScreenPosition,
        TideSailingReefSample sample,
        TideOceanSample ocean,
        float time,
        Sprite rockSprite,
        Sprite foamSprite,
        float visualZ = 0f)
    {
        if (rockRenderer == null || foamRenderer == null)
        {
            return;
        }

        float rockHeight = TideSailingReefModel.ReefCrownAboveLowestWaterMeters;
        bool rockVisible = worldVisible && sample.ExposedRock01 > 0.025f && rockSprite != null;
        SetEnabled(rockRenderer, rockVisible);
        if (rockVisible)
        {
            rockRenderer.sprite = rockSprite;
            rockRenderer.sortingOrder = 8;
            Color submergedRock = new Color(0.12f, 0.19f, 0.2f, 0.22f);
            Color drySaltRock = new Color(0.36f, 0.39f, 0.37f, 0.96f);
            Color rockColor = Color.Lerp(submergedRock, drySaltRock, sample.ExposedRock01);
            rockColor.a = Mathf.Lerp(0.18f, 0.96f, sample.ExposedRock01);
            SetWorldSize(
                rockRenderer,
                reefScreenPosition + new Vector2(0f, -rockHeight * 0.5f),
                new Vector2(TideSailingReefModel.ReefHalfWidthMeters * 2f, rockHeight),
                rockColor,
                0f,
                visualZ);
        }

        bool foamVisible = worldVisible && sample.ShallowRisk01 > 0.04f && foamSprite != null;
        SetEnabled(foamRenderer, foamVisible);
        if (foamVisible)
        {
            float foamBreath = 0.86f + Mathf.Sin(time * 1.7f + ocean.SurfaceY * 2.3f) * 0.08f;
            float foamAlpha = Mathf.Lerp(0.16f, 0.68f, sample.ShallowRisk01) * foamBreath;
            foamRenderer.sprite = foamSprite;
            foamRenderer.sortingOrder = 9;
            SetWorldSize(
                foamRenderer,
                new Vector2(reefScreenPosition.x, ocean.SurfaceY + 0.018f),
                new Vector2(
                    TideSailingReefModel.ReefHalfWidthMeters * 2f + ocean.Agitation01 * 0.38f,
                    0.12f + ocean.Agitation01 * 0.08f),
                new Color(0.76f, 0.89f, 0.86f, foamAlpha),
                Mathf.Atan(ocean.Slope) * Mathf.Rad2Deg * 0.55f,
                visualZ);
        }
    }

    private static void SetWorldSize(
        SpriteRenderer renderer,
        Vector2 position,
        Vector2 worldSize,
        Color color,
        float rotationZ,
        float visualZ)
    {
        if (renderer == null || renderer.sprite == null)
        {
            SetEnabled(renderer, false);
            return;
        }

        renderer.drawMode = SpriteDrawMode.Simple;
        renderer.enabled = true;
        renderer.color = color;
        Vector2 spriteSize = renderer.sprite.bounds.size;
        renderer.transform.localPosition = new Vector3(
            position.x,
            position.y,
            visualZ + renderer.sortingOrder * -0.001f);
        renderer.transform.localScale = new Vector3(
            worldSize.x / Mathf.Max(0.001f, spriteSize.x),
            worldSize.y / Mathf.Max(0.001f, spriteSize.y),
            1f);
        renderer.transform.localRotation = Quaternion.Euler(0f, 0f, rotationZ);
    }

    private static void SetEnabled(SpriteRenderer renderer, bool enabled)
    {
        if (renderer != null)
        {
            renderer.enabled = enabled;
        }
    }
}
