using UnityEngine;

/// <summary>
/// 外露龙骨肋的场景 owner。它只把主控制器提供的同一份海况采样映射到残骸、
/// 两根缆绳和 V85 拆解层；潮钟、玩家输入和维修库存仍由上层编排器拥有。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class TideHeavyWreckSalvageController : MonoBehaviour
{
    public const float StartWorldX = -9.72f;
    public const float RecoveryWorldX = -8.05f;
    public const float WorkInteractionX = -7.72f;
    public const float BedOffsetFromWalkSurface = -0.52f;

    private static readonly Vector2 LeftShoreAnchorOffset = new Vector2(0.2f, 0.03f);
    private static readonly Vector2 RightShoreAnchorOffset = new Vector2(0.62f, 0.03f);
    private static TideV85HeavyWreckCatalog catalog;
    private static Sprite lineSprite;

    [SerializeField] private TideHeavyWreckState state;
    [SerializeField] private TideHeavyWreckPieceOwnershipState pieceOwnership;

    private Transform visualRoot;
    private SpriteRenderer intactRenderer;
    private SpriteRenderer scoreMarksRenderer;
    private SpriteRenderer remainderRenderer;
    private SpriteRenderer pieceARenderer;
    private SpriteRenderer pieceBRenderer;
    private SpriteRenderer leftRopeRenderer;
    private SpriteRenderer rightRopeRenderer;
    private SpriteRenderer leftStakeRenderer;
    private SpriteRenderer rightStakeRenderer;
    private float walkSurfaceY;
    private float cachedWaterDepthMeters;
    private Vector2 cachedPlayerPosition;
    private Vector2 cachedShelterStagingAnchor;
    private Vector2 cachedBoatStagingAnchor;
    private bool reelHeldThisFrame;
    private bool workHeldThisFrame;

    public TideHeavyWreckState State => state;
    public float SampleWorldX => Mathf.Lerp(StartWorldX, RecoveryWorldX, state.TowProgress01) + state.DriftMeters;
    public bool IsCarryingPiece => pieceOwnership.CarriedPiece != TideHeavyWreckPiece.None;

    private void OnEnable()
    {
        EnsureVisuals();
        if (state.Phase == 0 && state.SecuredPointMask == 0 &&
            state.TowProgress01 <= 0f && state.WorkProgress01 <= 0f)
        {
            state = TideHeavyWreckTidalLiftModel.CreateInitial();
        }
    }

    public void ResetFeature()
    {
        state = TideHeavyWreckTidalLiftModel.CreateInitial();
        pieceOwnership = TideHeavyWreckPieceOwnershipModel.CreateUnavailable();
        cachedWaterDepthMeters = 0f;
        reelHeldThisFrame = false;
        workHeldThisFrame = false;
        UpdateVisibility(true);
    }

    public void TickNaturalState(
        float deltaSeconds,
        float currentWalkSurfaceY,
        TideOceanSample ocean,
        float signedAstronomicalCurrentMetersPerSecond)
    {
        walkSurfaceY = currentWalkSurfaceY;
        float bedY = walkSurfaceY + BedOffsetFromWalkSurface;
        cachedWaterDepthMeters = ocean.SurfaceY - bedY;
        state = TideHeavyWreckTidalLiftModel.AdvanceNatural(
            state,
            deltaSeconds,
            cachedWaterDepthMeters,
            signedAstronomicalCurrentMetersPerSecond,
            ocean.HorizontalVelocity,
            reelHeldThisFrame);
        TideHeavyWreckPhase phaseBeforeWork = state.Phase;
        state = TideHeavyWreckTidalLiftModel.AdvanceWork(
            state,
            deltaSeconds,
            workHeldThisFrame);
        if (phaseBeforeWork != TideHeavyWreckPhase.Separated &&
            state.Phase == TideHeavyWreckPhase.Separated)
        {
            pieceOwnership = TideHeavyWreckPieceOwnershipModel.CreateSeparated();
        }
        reelHeldThisFrame = false;
        workHeldThisFrame = false;
    }

    public bool TryHandleInteraction(
        Vector2 playerPosition,
        bool pressed,
        bool held,
        out string feedback)
    {
        feedback = string.Empty;
        if (state.Phase == TideHeavyWreckPhase.Lost)
        {
            return false;
        }
        if (state.Phase == TideHeavyWreckPhase.Separated)
        {
            return TryHandleSeparatedPieceInteraction(playerPosition, pressed, out feedback);
        }

        Vector2 sourceCenter = GetSourceCenter(new TideOceanSample(0f, 0f, 0f, 0f));
        int nearestPoint = GetNearestSecurePointIndex(playerPosition, sourceCenter);
        Vector2 nearestPosition = GetSecurePointWorldPosition(sourceCenter, nearestPoint, 0f);
        if (state.SecuredPointMask != 3 &&
            Mathf.Abs(playerPosition.x - nearestPosition.x) <= 0.5f)
        {
            if (!pressed)
            {
                return held;
            }

            state = TideHeavyWreckTidalLiftModel.TrySecurePoint(
                state,
                nearestPoint,
                cachedWaterDepthMeters,
                out bool secured);
            feedback = secured
                ? state.SecuredPointMask == 3
                    ? "两处旧绳箍都已经接上岸缆。残骸仍压在岩床上，要等海水真正托起它。"
                    : "第一根岸缆已经接住绳箍；单点会让弯肋在水里扭转，另一端也必须系牢。"
                : "水已经深到无法在浪里看清绳箍。等退潮露出肋材后再系。";
            return true;
        }

        bool atHaulPoint = Mathf.Abs(playerPosition.x - WorkInteractionX) <= 0.55f;
        if (!atHaulPoint)
        {
            return false;
        }

        if (state.Phase == TideHeavyWreckPhase.RecoveredIntact ||
            state.Phase == TideHeavyWreckPhase.ExposingJoints ||
            state.Phase == TideHeavyWreckPhase.JointsExposed ||
            state.Phase == TideHeavyWreckPhase.Separating)
        {
            if (pressed)
            {
                state = TideHeavyWreckTidalLiftModel.TryBeginWork(state, out bool started);
                if (started)
                {
                    feedback = state.Phase == TideHeavyWreckPhase.ExposingJoints
                        ? "先刮掉盐壳和缠藻，让原来的接缝重新露出来。"
                        : "接缝已经看清。重新按住工具，逐段分开左右弯肋和中段余件。";
                }
            }

            workHeldThisFrame = held &&
                (state.Phase == TideHeavyWreckPhase.ExposingJoints ||
                 state.Phase == TideHeavyWreckPhase.Separating);
            return pressed || held;
        }

        if (state.SecuredPointMask == 3 &&
            state.Phase < TideHeavyWreckPhase.RecoveredIntact)
        {
            reelHeldThisFrame = held;
            if (pressed)
            {
                feedback = state.Lift01 < 0.72f
                    ? "绳已经绷直，但肋材仍压着岩床。硬拉只会磨断旧缆，等涨潮提供浮力。"
                    : Mathf.Abs(state.Tension01) >= TideHeavyWreckTidalLiftModel.CriticalTension01
                        ? "流还太急，缆绳正在吃满力。先松手，等平流窗口再收。"
                        : "两根缆分担了扭矩。保持按住，趁平流把浮起的肋材逐段带向作业架。";
            }
            return pressed || held;
        }

        return false;
    }

    public void UpdatePresentation(
        bool visible,
        float currentWalkSurfaceY,
        Vector2 playerPosition,
        Vector2 shelterStagingAnchor,
        Vector2 boatStagingAnchor,
        TideOceanSample ocean,
        float time)
    {
        EnsureVisuals();
        walkSurfaceY = currentWalkSurfaceY;
        cachedPlayerPosition = playerPosition;
        cachedShelterStagingAnchor = shelterStagingAnchor;
        cachedBoatStagingAnchor = boatStagingAnchor;
        bool physicallyVisible = visible && state.Phase != TideHeavyWreckPhase.Lost;
        UpdateVisibility(physicallyVisible);
        if (!physicallyVisible)
        {
            return;
        }

        float rotation = EvaluateRotationDegrees(ocean);
        Vector2 sourceCenter = GetSourceCenter(ocean);
        SetPose(intactRenderer, sourceCenter, rotation);
        bool separated = state.Phase == TideHeavyWreckPhase.Separated;
        bool jointsVisible = state.Phase == TideHeavyWreckPhase.JointsExposed ||
            state.Phase == TideHeavyWreckPhase.Separating;
        intactRenderer.enabled = !separated;
        scoreMarksRenderer.enabled = !separated && jointsVisible;
        remainderRenderer.enabled = separated;
        pieceARenderer.enabled = separated;
        pieceBRenderer.enabled = separated;

        if (scoreMarksRenderer.enabled)
        {
            SetOwnerPose(scoreMarksRenderer, sourceCenter, TideV85HeavyWreckCatalog.ScoreMarksOffset, rotation);
        }
        if (separated)
        {
            SetOwnerPose(remainderRenderer, sourceCenter, TideV85HeavyWreckCatalog.RemainderOffset, rotation);
            UpdateSeparatedPiecePose(TideHeavyWreckPiece.PieceA, pieceARenderer, sourceCenter);
            UpdateSeparatedPiecePose(TideHeavyWreckPiece.PieceB, pieceBRenderer, sourceCenter);
        }

        UpdateRopes(sourceCenter, rotation, time);
        UpdateStakes();
    }

    public string GetDebugSummary()
    {
        string pieceText = state.Phase == TideHeavyWreckPhase.Separated
            ? $" / A {pieceOwnership.PieceAOwner} / B {pieceOwnership.PieceBOwner}"
            : string.Empty;
        return $"重物 {state.Phase} / 系缆 {CountBits(state.SecuredPointMask)}/2 / " +
            $"浮力 {state.Lift01:P0} / 拖运 {state.TowProgress01:P0} / 张力 {state.Tension01:P0}{pieceText}";
    }

    public int GetStagedPartMask(TideIslandSalvageDestination destination)
    {
        return TideHeavyWreckPieceOwnershipModel.GetStagedMask(pieceOwnership, destination);
    }

    public bool TryIntegrateStagedPiece(
        TideHeavyWreckPiece piece,
        TideIslandSalvageDestination stagingDestination)
    {
        pieceOwnership = TideHeavyWreckPieceOwnershipModel.TryIntegrate(
            pieceOwnership,
            piece,
            stagingDestination,
            out bool integrated);
        return integrated;
    }

    public string RunEditorIntegrationProbe()
    {
        EnsureVisuals();
        string catalogReason = "Catalog 资源不存在";
        bool catalogReady = catalog != null && catalog.IsComplete(out catalogReason);
        bool renderersReady = intactRenderer != null && scoreMarksRenderer != null &&
            remainderRenderer != null && pieceARenderer != null && pieceBRenderer != null &&
            leftRopeRenderer != null && rightRopeRenderer != null;
        bool exactSourceScale = catalogReady && intactRenderer.sprite == catalog.IntactKeelRib &&
            Vector2.Distance(intactRenderer.sprite.bounds.size, TideV85HeavyWreckCatalog.VisibleWorldSize) <= 0.02f;
        return catalogReady && renderersReady && exactSourceScale
            ? $"PASS V85五owner/原尺寸{intactRenderer.sprite.bounds.size.x:F2}x{intactRenderer.sprite.bounds.size.y:F2}m"
            : $"FAIL catalog={catalogReady}({catalogReason})/renderers={renderersReady}/scale={exactSourceScale}";
    }

    private bool TryHandleSeparatedPieceInteraction(
        Vector2 playerPosition,
        bool pressed,
        out string feedback)
    {
        feedback = string.Empty;
        if (!pressed)
        {
            return false;
        }

        if (pieceOwnership.CarriedPiece != TideHeavyWreckPiece.None)
        {
            bool nearShelter = Mathf.Abs(playerPosition.x - cachedShelterStagingAnchor.x) <= 0.58f;
            bool nearBoat = Mathf.Abs(playerPosition.x - cachedBoatStagingAnchor.x) <= 0.58f;
            if (!nearShelter && !nearBoat)
            {
                feedback = "弯肋很长，不能塞进背包。把它拖到屋侧承重施工位，或船边检修位。";
                return true;
            }

            TideIslandSalvageDestination destination = nearShelter
                ? TideIslandSalvageDestination.ShelterStaging
                : TideIslandSalvageDestination.EscapeBoatStaging;
            pieceOwnership = TideHeavyWreckPieceOwnershipModel.TryStageCarried(
                pieceOwnership,
                destination,
                out TideHeavyWreckPiece stagedPiece);
            if (stagedPiece == TideHeavyWreckPiece.None)
            {
                return false;
            }

            feedback = destination == TideIslandSalvageDestination.ShelterStaging
                ? $"{stagedPiece} 靠在屋侧干燥施工位；最终校正柱脚时才会固定成斜撑。"
                : $"{stagedPiece} 放到船边检修位；最终校正船壳曲线时才会固定成肋骨。";
            return true;
        }

        Vector2 sourceCenter = GetSourceCenter(new TideOceanSample(0f, 0f, 0f, 0f));
        TideHeavyWreckPiece nearest = TideHeavyWreckPiece.None;
        float nearestDistance = 0.62f;
        for (int i = 1; i <= 2; i++)
        {
            TideHeavyWreckPiece piece = (TideHeavyWreckPiece)i;
            if (TideHeavyWreckPieceOwnershipModel.GetOwner(pieceOwnership, piece) !=
                TideHeavyWreckPieceOwner.Worksite)
            {
                continue;
            }

            float distance = Mathf.Abs(playerPosition.x - GetWorksitePiecePosition(piece, sourceCenter).x);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = piece;
            }
        }

        if (nearest == TideHeavyWreckPiece.None)
        {
            return false;
        }

        pieceOwnership = TideHeavyWreckPieceOwnershipModel.TryPickUp(
            pieceOwnership,
            nearest,
            out bool pickedUp);
        if (!pickedUp)
        {
            return false;
        }

        feedback = "你没有把整根弯肋举起来，而是让下端贴着岩板拖行；移动会明显变慢。";
        return true;
    }

    private void UpdateSeparatedPiecePose(
        TideHeavyWreckPiece piece,
        SpriteRenderer renderer,
        Vector2 sourceCenter)
    {
        TideHeavyWreckPieceOwner owner = TideHeavyWreckPieceOwnershipModel.GetOwner(pieceOwnership, piece);
        bool visible = owner == TideHeavyWreckPieceOwner.Worksite ||
            owner == TideHeavyWreckPieceOwner.Carried ||
            owner == TideHeavyWreckPieceOwner.ShelterStaging ||
            owner == TideHeavyWreckPieceOwner.BoatStaging;
        renderer.enabled = visible;
        if (!visible)
        {
            return;
        }

        if (owner == TideHeavyWreckPieceOwner.Worksite)
        {
            SetPose(renderer, GetWorksitePiecePosition(piece, sourceCenter), 0f);
            return;
        }

        if (owner == TideHeavyWreckPieceOwner.Carried)
        {
            float rotation = piece == TideHeavyWreckPiece.PieceA ? -22f : 18f;
            float xOffset = piece == TideHeavyWreckPiece.PieceA ? -0.38f : 0.36f;
            SetBottomOnSurface(
                renderer,
                cachedPlayerPosition.x + xOffset,
                walkSurfaceY,
                rotation);
            return;
        }

        Vector2 stagingAnchor = owner == TideHeavyWreckPieceOwner.ShelterStaging
            ? cachedShelterStagingAnchor
            : cachedBoatStagingAnchor;
        float stagedXOffset = piece == TideHeavyWreckPiece.PieceA ? -0.28f : 0.3f;
        float stagedRotation = piece == TideHeavyWreckPiece.PieceA ? -14f : 12f;
        SetBottomOnSurface(
            renderer,
            stagingAnchor.x + stagedXOffset,
            stagingAnchor.y,
            stagedRotation);
    }

    private static Vector2 GetWorksitePiecePosition(
        TideHeavyWreckPiece piece,
        Vector2 sourceCenter)
    {
        Vector2 offset = piece == TideHeavyWreckPiece.PieceA
            ? TideV85HeavyWreckCatalog.PieceAOffset
            : TideV85HeavyWreckCatalog.PieceBOffset;
        return sourceCenter + offset;
    }

    private void EnsureVisuals()
    {
        EnsureCatalog();
        if (visualRoot == null)
        {
            Transform existing = transform.Find("GeneratedHeavyWreckTidalLiftRoot");
            visualRoot = existing != null
                ? existing
                : new GameObject("GeneratedHeavyWreckTidalLiftRoot").transform;
            visualRoot.SetParent(transform, false);
        }

        intactRenderer = EnsureRenderer("HeavyKeelRibIntact", catalog != null ? catalog.IntactKeelRib : null, 0);
        scoreMarksRenderer = EnsureRenderer("HeavyKeelRibScoreMarks", catalog != null ? catalog.ScoreMarks : null, 2);
        remainderRenderer = EnsureRenderer("HeavyKeelRibRemainder", catalog != null ? catalog.Remainder : null, 2);
        pieceARenderer = EnsureRenderer("HeavyKeelRibPieceA", catalog != null ? catalog.PieceA : null, 2);
        pieceBRenderer = EnsureRenderer("HeavyKeelRibPieceB", catalog != null ? catalog.PieceB : null, 2);
        leftRopeRenderer = EnsureRenderer("HeavyKeelRibLeftShoreLine", GetLineSprite(), 3);
        rightRopeRenderer = EnsureRenderer("HeavyKeelRibRightShoreLine", GetLineSprite(), 3);
        leftStakeRenderer = EnsureRenderer("HeavyKeelRibLeftMooringStake", GetLineSprite(), 3);
        rightStakeRenderer = EnsureRenderer("HeavyKeelRibRightMooringStake", GetLineSprite(), 3);
        Color ropeColor = new Color(0.33f, 0.27f, 0.18f, 0.96f);
        leftRopeRenderer.color = ropeColor;
        rightRopeRenderer.color = ropeColor;
        leftStakeRenderer.color = new Color(0.27f, 0.22f, 0.15f, 1f);
        rightStakeRenderer.color = leftStakeRenderer.color;
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
        if (visualRoot != null)
        {
            visualRoot.gameObject.SetActive(visible);
        }
    }

    private void UpdateRopes(Vector2 sourceCenter, float rotationDegrees, float time)
    {
        Vector2 leftAnchor = GetShoreAnchor(0);
        Vector2 rightAnchor = GetShoreAnchor(1);
        Vector2 leftSecure = GetSecurePointWorldPosition(sourceCenter, 0, rotationDegrees);
        Vector2 rightSecure = GetSecurePointWorldPosition(sourceCenter, 1, rotationDegrees);
        bool leftAttached = (state.SecuredPointMask & 1) != 0;
        bool rightAttached = (state.SecuredPointMask & 2) != 0;
        leftRopeRenderer.enabled = leftAttached && state.Phase < TideHeavyWreckPhase.Separated;
        rightRopeRenderer.enabled = rightAttached && state.Phase < TideHeavyWreckPhase.Separated;
        if (leftRopeRenderer.enabled)
        {
            SetSegment(leftRopeRenderer, leftSecure, leftAnchor, 0.025f, time, 0f);
        }
        if (rightRopeRenderer.enabled)
        {
            SetSegment(rightRopeRenderer, rightSecure, rightAnchor, 0.025f, time, 1.7f);
        }
    }

    private void UpdateStakes()
    {
        SetSegment(
            leftStakeRenderer,
            GetShoreAnchor(0) + Vector2.down * 0.12f,
            GetShoreAnchor(0) + Vector2.up * 0.18f,
            0.065f,
            0f,
            0f);
        SetSegment(
            rightStakeRenderer,
            GetShoreAnchor(1) + Vector2.down * 0.12f,
            GetShoreAnchor(1) + Vector2.up * 0.18f,
            0.065f,
            0f,
            0f);
    }

    private Vector2 GetSourceCenter(TideOceanSample ocean)
    {
        float bedY = walkSurfaceY + BedOffsetFromWalkSurface;
        float groundedCenterY = bedY + TideV85HeavyWreckCatalog.VisibleWorldSize.y * 0.5f;
        float afloatCenterY = ocean.SurfaceY - TideV85HeavyWreckCatalog.WaterlineFromSourcePivot.y;
        float y = Mathf.Lerp(groundedCenterY, afloatCenterY, state.Lift01);
        if (state.Phase >= TideHeavyWreckPhase.RecoveredIntact)
        {
            y = groundedCenterY;
        }
        return new Vector2(SampleWorldX, y);
    }

    private float EvaluateRotationDegrees(TideOceanSample ocean)
    {
        if (state.Phase >= TideHeavyWreckPhase.RecoveredIntact)
        {
            return 0f;
        }

        float waveRotation = ocean.Slope * 8f * state.Lift01;
        float singlePointTwist = state.SecuredPointMask == 1
            ? -state.Tension01 * 8f
            : state.SecuredPointMask == 2
                ? state.Tension01 * 8f
                : 0f;
        return Mathf.Clamp(waveRotation + singlePointTwist, -11f, 11f);
    }

    private int GetNearestSecurePointIndex(Vector2 playerPosition, Vector2 sourceCenter)
    {
        float leftDistance = Mathf.Abs(
            playerPosition.x - GetSecurePointWorldPosition(sourceCenter, 0, 0f).x);
        float rightDistance = Mathf.Abs(
            playerPosition.x - GetSecurePointWorldPosition(sourceCenter, 1, 0f).x);
        return leftDistance <= rightDistance ? 0 : 1;
    }

    private static Vector2 GetSecurePointWorldPosition(
        Vector2 sourceCenter,
        int pointIndex,
        float rotationDegrees)
    {
        Vector2 local = pointIndex == 0
            ? TideV85HeavyWreckCatalog.SecurePointAFromSourcePivot
            : TideV85HeavyWreckCatalog.SecurePointBFromSourcePivot;
        return sourceCenter + (Vector2)(Quaternion.Euler(0f, 0f, rotationDegrees) * local);
    }

    private Vector2 GetShoreAnchor(int pointIndex)
    {
        Vector2 offset = pointIndex == 0 ? LeftShoreAnchorOffset : RightShoreAnchorOffset;
        return new Vector2(RecoveryWorldX, walkSurfaceY) + offset;
    }

    private static void SetPose(SpriteRenderer renderer, Vector2 position, float rotationDegrees)
    {
        if (renderer == null || renderer.sprite == null)
        {
            return;
        }

        renderer.transform.position = new Vector3(position.x, position.y, 0f);
        renderer.transform.rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
        renderer.transform.localScale = Vector3.one;
    }

    private static void SetOwnerPose(
        SpriteRenderer renderer,
        Vector2 sourceCenter,
        Vector2 offset,
        float rotationDegrees)
    {
        Vector2 rotatedOffset = Quaternion.Euler(0f, 0f, rotationDegrees) * offset;
        SetPose(renderer, sourceCenter + rotatedOffset, rotationDegrees);
    }

    private static void SetBottomOnSurface(
        SpriteRenderer renderer,
        float desiredPivotX,
        float surfaceY,
        float rotationDegrees)
    {
        if (renderer == null || renderer.sprite == null)
        {
            return;
        }

        // The V85 pieces keep their original transparent canvas and pivot so they can
        // reconstruct the source image exactly. Derive the rotated lower bound from the
        // sprite bounds instead of tuning a visual-only Y offset; the visible wood then
        // rests on the same walk surface used by locomotion and collision.
        Bounds bounds = renderer.sprite.bounds;
        Quaternion rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
        Vector2[] corners =
        {
            new Vector2(bounds.min.x, bounds.min.y),
            new Vector2(bounds.min.x, bounds.max.y),
            new Vector2(bounds.max.x, bounds.min.y),
            new Vector2(bounds.max.x, bounds.max.y)
        };
        float rotatedMinY = float.PositiveInfinity;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 rotatedCorner = rotation * corners[i];
            rotatedMinY = Mathf.Min(rotatedMinY, rotatedCorner.y);
        }

        renderer.transform.position = new Vector3(desiredPivotX, surfaceY - rotatedMinY, 0f);
        renderer.transform.rotation = rotation;
        renderer.transform.localScale = Vector3.one;
    }

    private static void SetSegment(
        SpriteRenderer renderer,
        Vector2 start,
        Vector2 end,
        float thickness,
        float time,
        float phase)
    {
        Vector2 delta = end - start;
        float length = delta.magnitude;
        if (renderer == null || renderer.sprite == null || length <= 0.001f)
        {
            return;
        }

        float tautness = Mathf.Clamp01(length / 2.4f);
        float sag = Mathf.Lerp(0.035f, 0.008f, tautness) *
            (0.75f + Mathf.Sin(time * 0.8f + phase) * 0.25f);
        Vector2 center = (start + end) * 0.5f + Vector2.down * sag;
        renderer.transform.position = new Vector3(center.x, center.y, 0f);
        renderer.transform.rotation = Quaternion.Euler(
            0f,
            0f,
            Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        renderer.transform.localScale = new Vector3(length, thickness, 1f);
    }

    private static void EnsureCatalog()
    {
        if (catalog == null)
        {
            catalog = Resources.Load<TideV85HeavyWreckCatalog>(
                "StiltFirstSliceAI/V85HeavyWreckCatalog");
        }
    }

    private static Sprite GetLineSprite()
    {
        if (lineSprite != null)
        {
            return lineSprite;
        }

        Texture2D texture = new Texture2D(8, 8, TextureFormat.RGBA32, false)
        {
            name = "GeneratedHeavyWreckLineTexture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        Color32[] pixels = new Color32[64];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Color.white;
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        lineSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            8f,
            0,
            SpriteMeshType.FullRect);
        lineSprite.name = "GeneratedHeavyWreckLineSprite";
        lineSprite.hideFlags = HideFlags.HideAndDontSave;
        return lineSprite;
    }

    private static int CountBits(int mask)
    {
        return (mask & 1) + ((mask >> 1) & 1);
    }
}
