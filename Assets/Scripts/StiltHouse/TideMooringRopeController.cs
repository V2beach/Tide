using System.Collections.Generic;
using UnityEngine;

public enum TideMooringRopeInteractionOutcome
{
    None,
    SwingStarted,
    ThrowAttached,
    ThrowMissed
}

public enum TideMooringRopeEnvironmentOutcome
{
    None,
    RopeBroke,
    BoatSecured
}

public struct TideMooringRopeInteractionResult
{
    public bool Handled;
    public TideMooringRopeInteractionOutcome Outcome;
}

/// <summary>
/// 泊位绳的运行时编排边界。纯粹的受力公式仍由 <see cref="TideMooringRopeModel"/>
/// 负责；本组件只把玩家输入、每帧环境推进和绳形表现组织成一个可替换模块。
/// 主场景控制器仍拥有统一风场、潮流、人物锚点和船体锚点，避免这里生成第二套世界状态。
/// </summary>
public sealed class TideMooringRopeController : MonoBehaviour
{
    [SerializeField] private TideMooringRopeState state;

    private bool reelHeldForNextEnvironmentStep;

    public TideMooringRopeState State => state;
    public TideMooringRopePhase Phase => state.Phase;
    public float BoatOffsetMeters => state.BoatOffsetMeters;

    public void ResetRuntime(float initialBoatOffsetMeters)
    {
        state = TideMooringRopeModel.CreateLoose(initialBoatOffsetMeters);
        reelHeldForNextEnvironmentStep = false;
    }

    /// <summary>
    /// 只供场景几何探针摆姿使用。正常游玩必须让偏移经过风、流和绳张力推进。
    /// 保留其余状态可让探针检查“同一条已抛绳”在不同船位下的几何关系。
    /// </summary>
    public void SetBoatOffsetForEditor(float boatOffsetMeters)
    {
        state.BoatOffsetMeters = Mathf.Clamp(
            boatOffsetMeters,
            -TideMooringRopeModel.MaximumBoatDriftMeters,
            TideMooringRopeModel.MaximumBoatDriftMeters);
    }

    public TideMooringRopeInteractionResult HandleInteraction(
        bool canInteract,
        bool pressed,
        bool held,
        bool released,
        float deltaSeconds)
    {
        if (!canInteract || state.Phase == TideMooringRopePhase.Secured)
        {
            return default;
        }

        if (state.Phase == TideMooringRopePhase.Loose)
        {
            if (!pressed)
            {
                return default;
            }

            state = TideMooringRopeModel.BeginSwing(state);
            return new TideMooringRopeInteractionResult
            {
                Handled = true,
                Outcome = TideMooringRopeInteractionOutcome.SwingStarted
            };
        }

        if (state.Phase == TideMooringRopePhase.Swinging)
        {
            if (held)
            {
                state = TideMooringRopeModel.AdvanceSwing(state, deltaSeconds, true);
                return new TideMooringRopeInteractionResult { Handled = true };
            }

            if (released)
            {
                state = TideMooringRopeModel.ReleaseThrow(state);
                return new TideMooringRopeInteractionResult
                {
                    Handled = true,
                    Outcome = state.Phase == TideMooringRopePhase.Attached
                        ? TideMooringRopeInteractionOutcome.ThrowAttached
                        : TideMooringRopeInteractionOutcome.ThrowMissed
                };
            }

            // 甩绳已经占用交互动作。即便这一帧没有新的按键边沿，也不能让同一个 F
            // 被主控制器继续解释成登船、拾取或施工。
            return new TideMooringRopeInteractionResult { Handled = true };
        }

        if (state.Phase == TideMooringRopePhase.Attached ||
            state.Phase == TideMooringRopePhase.Reeling)
        {
            reelHeldForNextEnvironmentStep = held;
            return new TideMooringRopeInteractionResult { Handled = true };
        }

        return default;
    }

    public TideMooringRopeEnvironmentOutcome AdvanceEnvironment(
        float deltaSeconds,
        float currentVelocityMetersPerSecond,
        float windVelocityMetersPerSecond,
        bool sailingActive)
    {
        if (sailingActive)
        {
            reelHeldForNextEnvironmentStep = false;
            return TideMooringRopeEnvironmentOutcome.None;
        }

        TideMooringRopePhase phaseBefore = state.Phase;
        state = TideMooringRopeModel.Advance(
            state,
            deltaSeconds,
            currentVelocityMetersPerSecond,
            windVelocityMetersPerSecond,
            reelHeldForNextEnvironmentStep);
        reelHeldForNextEnvironmentStep = false;

        if (phaseBefore != TideMooringRopePhase.Loose &&
            state.Phase == TideMooringRopePhase.Loose)
        {
            return TideMooringRopeEnvironmentOutcome.RopeBroke;
        }

        if (phaseBefore != TideMooringRopePhase.Secured &&
            state.Phase == TideMooringRopePhase.Secured)
        {
            return TideMooringRopeEnvironmentOutcome.BoatSecured;
        }

        return TideMooringRopeEnvironmentOutcome.None;
    }

    public void HidePresentation(
        IList<SpriteRenderer> segments,
        SpriteRenderer ropeEndRenderer)
    {
        SetEnabled(segments, false);
        SetEnabled(ropeEndRenderer, false);
    }

    public void UpdatePresentation(
        IList<SpriteRenderer> segments,
        SpriteRenderer ropeEndRenderer,
        bool worldVisible,
        Vector2 playerHand,
        Vector2 securedDockPoint,
        Vector2 boatTiePoint,
        Sprite ropeEndSprite,
        float visualZ)
    {
        bool visible = worldVisible && state.Phase != TideMooringRopePhase.Loose;
        SetEnabled(segments, visible);
        SetEnabled(ropeEndRenderer, visible);
        if (!visible || segments == null || segments.Count == 0 || ropeEndRenderer == null)
        {
            return;
        }

        Vector2 hand = state.Phase == TideMooringRopePhase.Secured
            ? securedDockPoint
            : playerHand;
        Vector2 ropeEnd = boatTiePoint;
        float sag;
        if (state.Phase == TideMooringRopePhase.Swinging)
        {
            float phase01 = Mathf.Clamp01(state.ThrowCharge01);
            float angleRadians = Mathf.Lerp(22f, 154f, phase01) * Mathf.Deg2Rad;
            float radius = Mathf.Lerp(0.38f, 0.92f, Mathf.Sin(phase01 * Mathf.PI));
            ropeEnd = hand + new Vector2(Mathf.Cos(angleRadians), Mathf.Sin(angleRadians)) * radius;
            sag = Mathf.Lerp(0.06f, 0.22f, Mathf.Sin(phase01 * Mathf.PI));
        }
        else
        {
            float slackMeters = Mathf.Max(
                0f,
                state.RopeLengthMeters - Vector2.Distance(hand, boatTiePoint));
            sag = -Mathf.Lerp(0.06f, 0.34f, Mathf.Clamp01(slackMeters / 0.7f));
        }

        Vector2 control = (hand + ropeEnd) * 0.5f + Vector2.up * sag;
        Vector2 previous = hand;
        for (int i = 0; i < segments.Count; i++)
        {
            float t = (i + 1f) / segments.Count;
            Vector2 point = EvaluateQuadraticBezier(hand, control, ropeEnd, t);
            SetThinRopeSegment(segments[i], previous, point, visualZ);
            previous = point;
        }

        ropeEndRenderer.sprite = ropeEndSprite;
        SetWorldSize(
            ropeEndRenderer,
            ropeEnd,
            new Vector2(0.08f, 0.11f),
            new Color(0.47f, 0.4f, 0.3f, 0.96f),
            0f,
            visualZ);
    }

    private static Vector2 EvaluateQuadraticBezier(
        Vector2 start,
        Vector2 control,
        Vector2 end,
        float t)
    {
        float oneMinusT = 1f - Mathf.Clamp01(t);
        return oneMinusT * oneMinusT * start +
            2f * oneMinusT * t * control +
            t * t * end;
    }

    private static void SetThinRopeSegment(
        SpriteRenderer renderer,
        Vector2 start,
        Vector2 end,
        float visualZ)
    {
        Vector2 delta = end - start;
        SetWorldSize(
            renderer,
            (start + end) * 0.5f,
            new Vector2(Mathf.Max(0.01f, delta.magnitude), 0.026f),
            new Color(0.42f, 0.35f, 0.25f, 0.92f),
            Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg,
            visualZ);
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

    private static void SetEnabled(IList<SpriteRenderer> renderers, bool enabled)
    {
        if (renderers == null)
        {
            return;
        }

        for (int i = 0; i < renderers.Count; i++)
        {
            SetEnabled(renderers[i], enabled);
        }
    }

    private static void SetEnabled(SpriteRenderer renderer, bool enabled)
    {
        if (renderer != null)
        {
            renderer.enabled = enabled;
        }
    }
}
