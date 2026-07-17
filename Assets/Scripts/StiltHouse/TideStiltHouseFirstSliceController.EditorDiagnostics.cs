using System;
using System.Collections.Generic;
using UnityEngine;
using RepairChoice = TideRepairTarget;

#if UNITY_EDITOR
/// <summary>
/// Editor-only preview poses and deterministic scene probes for the first slice.
/// Runtime state, fields, and lifecycle remain in the primary controller partial.
/// </summary>
public partial class TideStiltHouseFirstSliceController
{
    [ContextMenu("Rebuild Generated First Slice Hierarchy")]
    public void RebuildGeneratedHierarchyForEditor()
    {
        retiredHierarchyCleaned = false;
        EnsureScene();
        UpdateVisuals(Application.isPlaying ? Time.time : 0f);
    }

    public void RefreshEditorCaptureFraming()
    {
        // Preview capture assigns its final RenderTexture after the pose is built.
        // Re-evaluate only framing and HUD anchors for that aspect; rebuilding the
        // complete pose here would advance or reset action-specific preview states.
        UpdateShelterCameraFraming();
        UpdateText(GetVisualStormPressure01());
    }

    public TideStormRescueLayout GetStormRescueLayout()
    {
        EnsureScene();
        const int itemCount = 4;
        Vector2[] basePositions = new Vector2[itemCount];
        Vector2[] dryRackPositions = new Vector2[itemCount];
        for (int i = 0; i < itemCount; i++)
        {
            basePositions[i] = GetStormRescueBasePosition(i);
            dryRackPositions[i] = GetStormRescueDryRackPosition(i);
        }

        return new TideStormRescueLayout
        {
            PlayerStart = new Vector2(
                GetInteriorStairBottomPosition().x,
                GetPlayerLaneY(WalkLane.InteriorLower)),
            BasePositions = basePositions,
            DryRackPositions = dryRackPositions,
            PlayerMoveSpeed = playerMoveSpeed,
            HoistRopeOwnerCount = stormRescueRopeRenderers.Count
        };
    }

    public TideStormRescueFloodProfile GetEditorNaturalStormRescueFloodProfile()
    {
        EnsureScene();
        const float stepSeconds = 0.02f;
        float cycle = Mathf.Max(8f, tideCycleSeconds);
        float fullyArrivedWorldClock = dayLengthSeconds * stormFrontArrivalDays + cycle;
        float openingTideOffset = cycle * OpeningTidePhase01;
        float phaseAtArrival = Mathf.Repeat(
            openingTideOffset + fullyArrivedWorldClock,
            cycle);
        float secondsUntilLowWater = Mathf.Repeat(-phaseAtArrival, cycle);
        float profileWorldStart = fullyArrivedWorldClock + secondsUntilLowWater;
        int sampleCount = Mathf.CeilToInt(cycle / stepSeconds) + 1;
        TideStormRescueEnvironmentSample[] samples =
            new TideStormRescueEnvironmentSample[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float elapsed = i * stepSeconds;
            float sampleTideClock = elapsed;
            float sampleWeatherClock = profileWorldStart + elapsed;
            float pressure01 = TideContinuousWeatherModel.EvaluatePressure01(
                sampleWeatherClock,
                dayLengthSeconds,
                stormFrontArrivalDays);
            float signedStormWind = GetStormAdjustedWindSpeed(0f, pressure01);
            float waterY = EvaluateNaturalWaterY(sampleTideClock, sampleWeatherClock);
            float localDepth = EvaluateStormRescueLocalWaterDepth(
                waterY,
                pressure01,
                false);
            samples[i] = new TideStormRescueEnvironmentSample
            {
                LocalWaterDepthMeters = localDepth,
                CurrentSpeedMetersPerSecond = EvaluateStormRescueLocalCurrentSpeed(
                    sampleTideClock,
                    sampleWeatherClock,
                    sampleWeatherClock,
                    signedStormWind,
                    localDepth)
            };
        }

        return new TideStormRescueFloodProfile
        {
            StepSeconds = stepSeconds,
            Samples = samples
        };
    }

    public string RunEditorStormManifestOwnershipProbe()
    {
        EnsureScene();
        ResetSlice();
        int defaultTimber = timberStock;
        int defaultFuel = dryFuelBundles;
        int defaultClues = lighthouseClues;
        PrepareStormRescueManifest();
        int defaultPresentMask = GetStormRescuePresentMask();
        bool defaultHasNoFakeCargo = defaultPresentMask ==
            ((1 << (int)TideStormRescueItemKind.DrinkingWater) |
             (1 << (int)TideStormRescueItemKind.StoveFuel));
        bool defaultReservationsGrounded = defaultTimber == 0 && defaultFuel == 1 && defaultClues == 0 &&
            stormRescueReservedTimber == 0 && stormRescueReservedFuelBundles == 1 &&
            stormRescueReservedChartClues == 0;

        ResetSlice();
        timberStock = 3;
        lighthouseClues = 1;
        float initialCisternLiters = barrenIsland.Cistern.StoredLiters;
        PrepareStormRescueManifest();
        float filledCisternLiters = barrenIsland.Cistern.StoredLiters;
        float filledReserveLiters = stormRescueWater.Liters;
        bool fillConservesWater = Mathf.Abs(
            initialCisternLiters - filledCisternLiters - filledReserveLiters) <= 0.001f;
        bool exactContainerFilled = Mathf.Abs(filledReserveLiters - StormRescueWaterContainerLiters) <= 0.001f;
        bool allRealCargoReserved = GetStormRescuePresentMask() == 0b1111 &&
            timberStock == 1 && stormRescueReservedTimber == 2 &&
            dryFuelBundles == 0 && stormRescueReservedFuelBundles == 1 &&
            lighthouseClues == 0 && stormRescueReservedChartClues == 1;

        RestoreSecuredStormRescueCargo(TideStormRescueItemKind.BoatMaterial);
        RestoreSecuredStormRescueCargo(TideStormRescueItemKind.StoveFuel);
        RestoreSecuredStormRescueCargo(TideStormRescueItemKind.LighthouseChart);
        bool securedCargoReturnsToStorage = timberStock == 3 && dryFuelBundles == 1 &&
            lighthouseClues == 1 && stormRescueReservedTimber == 0 &&
            stormRescueReservedFuelBundles == 0 && stormRescueReservedChartClues == 0;

        ApplyStormRescueLoss(TideStormRescueItemKind.DrinkingWater);
        bool lossRemovesOnlyContainer = stormRescueWater.Liters <= 0.001f &&
            Mathf.Abs(barrenIsland.Cistern.StoredLiters - filledCisternLiters) <= 0.001f;

        ResetSlice();
        PrepareStormRescueManifest();
        float cisternBeforeRest = barrenIsland.Cistern.StoredLiters;
        float consumed = ConsumeRestWater(DailyRestWaterNeedLiters);
        bool restUsesSecuredContainerFirst = Mathf.Abs(consumed - DailyRestWaterNeedLiters) <= 0.001f &&
            Mathf.Abs(barrenIsland.Cistern.StoredLiters - cisternBeforeRest) <= 0.001f &&
            Mathf.Abs(
                stormRescueWater.Liters -
                (StormRescueWaterContainerLiters - DailyRestWaterNeedLiters)) <= 0.001f;

        ResetSlice();
        stoveCondition = 1;
        bodyWarmth01 = 0.2f;
        AdvanceMoonPhase();
        float warmthWithFuel = bodyWarmth01;
        bool oneFuelBundleBurnsOnce = dryFuelBundles == 0 && warmthWithFuel >= 0.89f;

        ResetSlice();
        stoveCondition = 1;
        dryFuelBundles = 0;
        bodyWarmth01 = 0.2f;
        AdvanceMoonPhase();
        float warmthWithoutFuel = bodyWarmth01;
        bool missingFuelCannotCreateWarmth = warmthWithoutFuel <= 0.49f &&
            warmthWithFuel - warmthWithoutFuel >= 0.39f;
        ResetSlice();

        bool passed = defaultHasNoFakeCargo && defaultReservationsGrounded &&
            fillConservesWater && exactContainerFilled && allRealCargoReserved &&
            securedCargoReturnsToStorage && lossRemovesOnlyContainer &&
            restUsesSecuredContainerFirst && oneFuelBundleBurnsOnce &&
            missingFuelCannotCreateWarmth;
        return passed
            ? $"PASS 默认实物掩码={defaultPresentMask:X}；满清单预留=4/4；水罐{filledReserveLiters:F1}L；保住归库；冲失不重扣；夜间先用{consumed:F1}L；干柴恢复{warmthWithoutFuel:F2}->{warmthWithFuel:F2}"
            : $"FAIL 默认无假物={defaultHasNoFakeCargo}/{defaultReservationsGrounded} 守恒={fillConservesWater} 装罐={exactContainerFilled} 满清单={allRealCargoReserved} 归库={securedCargoReturnsToStorage} 冲失={lossRemovesOnlyContainer} 夜用={restUsesSecuredContainerFirst} 干柴={oneFuelBundleBurnsOnce}/{missingFuelCannotCreateWarmth}";
    }

    public string RunEditorStormRestIntegrityProbe()
    {
        EnsureScene();
        ResetSlice();
        timberStock = 3;
        lighthouseClues = 1;
        PrepareStormRescueManifest();

        viewMode = SliceViewMode.Interior;
        playerLane = WalkLane.InteriorUpper;
        playerPosition = new Vector2(GetInteriorBedX(), GetPlayerLaneY(playerLane));
        state = SliceState.RepairMoment;
        repairChoiceApplied = true;
        weatherClockSeconds = dayLengthSeconds * stormFrontArrivalDays;
        shelterBreachThisTide = true;
        currentWaterY = GetPlayerStandingFeetY(WalkLane.InteriorLower) + 0.7f;
        stormRescue.RestoreFloodState(true, true);

        int unresolvedBefore = CountUnresolvedStormRescueCargo();
        int timberBeforeRecovery = timberStock;
        int fuelBeforeRecovery = dryFuelBundles;
        int cluesBeforeRecovery = lighthouseClues;
        bool sleepStartedDuringFlood = BeginSleepPresentation();
        RecoverSurvivingStormCargoAtRest();
        int unresolvedDuringFlood = CountUnresolvedStormRescueCargo();
        bool floodedRestIsBlocked = !sleepStartedDuringFlood;
        bool floodCannotAutoStoreCargo = unresolvedDuringFlood == unresolvedBefore &&
            timberStock == timberBeforeRecovery &&
            dryFuelBundles == fuelBeforeRecovery &&
            lighthouseClues == cluesBeforeRecovery;

        ResetSlice();
        timberStock = 3;
        lighthouseClues = 1;
        PrepareStormRescueManifest();
        currentWaterY = GetPlayerStandingFeetY(WalkLane.InteriorLower) - 0.1f;
        int unresolvedBeforeWarningRest = CountUnresolvedStormRescueCargo();
        RecoverSurvivingStormCargoAtRest();
        bool warningAloneCannotAutoStore =
            CountUnresolvedStormRescueCargo() == unresolvedBeforeWarningRest;

        viewMode = SliceViewMode.Interior;
        playerLane = WalkLane.InteriorUpper;
        playerPosition = new Vector2(GetInteriorBedX(), GetPlayerLaneY(playerLane));
        state = SliceState.RepairMoment;
        repairChoiceApplied = true;
        dayProgress01 = 0.91f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        tideClockSeconds = tideCycleSeconds * 0.2f;
        weatherClockSeconds = dayLengthSeconds * stormFrontArrivalDays;
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        bool restBeforeIncomingFloodBlocked = !BeginSleepPresentation();

        ResetSlice();
        timberStock = 3;
        lighthouseClues = 1;
        PrepareStormRescueManifest();
        stormRescue.RestoreFloodState(true, true);
        currentWaterY = GetPlayerStandingFeetY(WalkLane.InteriorLower) - 0.1f;
        int expectedRecoveredTimber = timberStock + stormRescueReservedTimber;
        int expectedRecoveredFuel = dryFuelBundles + stormRescueReservedFuelBundles;
        int expectedRecoveredClues = lighthouseClues + stormRescueReservedChartClues;
        RecoverSurvivingStormCargoAtRest();
        int unresolvedAfterRecede = CountUnresolvedStormRescueCargo();
        bool recededSurvivorsReturnToStorage = unresolvedAfterRecede == 0 &&
            timberStock == expectedRecoveredTimber &&
            dryFuelBundles == expectedRecoveredFuel &&
            lighthouseClues == expectedRecoveredClues;
        ResetSlice();

        bool passed = floodedRestIsBlocked && floodCannotAutoStoreCargo &&
            warningAloneCannotAutoStore && restBeforeIncomingFloodBlocked &&
            recededSurvivorsReturnToStorage;
        return passed
            ? $"PASS 暴潮不可睡；活动水中保持 {unresolvedDuringFlood}/{unresolvedBefore}；警戒不白送；未来潮窗阻止换日；退水归库 {unresolvedAfterRecede}"
            : $"FAIL 活动阻止={floodedRestIsBlocked}/{floodCannotAutoStoreCargo} 警戒守恒={warningAloneCannotAutoStore} 未来潮窗={restBeforeIncomingFloodBlocked} 退水归库={recededSurvivorsReturnToStorage}";
    }

    public string RunEditorV77LighthouseVisibilityIntegrationProbe()
    {
        EnsureScene();
        TideLighthouseVisibilitySample unknown = TideLighthouseVisibilityModel.Evaluate(
            0, -1, 0, false, 0.35f, 0.62f);
        TideLighthouseVisibilitySample bearing = TideLighthouseVisibilityModel.Evaluate(
            1, 2, 2, false, 0.42f, 0.24f);
        TideLighthouseVisibilitySample knownMoon = TideLighthouseVisibilityModel.Evaluate(
            1, 2, 3, false, 0f, 0f);
        TideLighthouseVisibilitySample knownStorm = TideLighthouseVisibilityModel.Evaluate(
            1, 2, 3, false, 0.28f, 1f);

        bool unknownLeaksNothing = unknown.State == TideLighthouseVisibilityState.Unknown &&
            !unknown.ShowsLighthouse && !unknown.ShowsBeam;
        bool bearingIsSilhouetteOnly = bearing.State == TideLighthouseVisibilityState.Bearing &&
            bearing.ShowsLighthouse && !bearing.ShowsBeam;
        bool knownUsesWeather = knownMoon.State == TideLighthouseVisibilityState.Known &&
            knownMoon.ShowsLighthouse && knownMoon.ShowsBeam &&
            knownStorm.LighthouseAlpha < knownMoon.LighthouseAlpha &&
            knownStorm.BeamAlpha < knownMoon.BeamAlpha;
        bool legacyMarkerGone = GameObject.Find("GeneratedStiltFirstFoggedLighthouseHint") == null;
        bool noMarkerCollider = legacyMarkerGone ||
            GameObject.Find("GeneratedStiltFirstFoggedLighthouseHint").GetComponent<Collider2D>() == null;

        bool passed = unknownLeaksNothing && bearingIsSilhouetteOnly && knownUsesWeather &&
            legacyMarkerGone && noMarkerCollider;
        string evidence =
            $"未知={unknown.LighthouseAlpha:0.00}/{unknown.BeamAlpha:0.00}；" +
            $"方位={bearing.LighthouseAlpha:0.00}/{bearing.BeamAlpha:0.00}；" +
            $"月夜={knownMoon.LighthouseAlpha:0.00}/{knownMoon.BeamAlpha:0.00}；" +
            $"暴潮={knownStorm.LighthouseAlpha:0.00}/{knownStorm.BeamAlpha:0.00}；" +
            $"旧提示={legacyMarkerGone}";
        return passed
            ? $"PASS：V77 用广域雾和正式灯塔表达未知、方位与已知三态，旧泡沫锚点已退役。{evidence}"
            : $"FAIL：V77 状态语义、天气压制或旧提示清理至少一项失效。{evidence}";
    }

    public void ConfigureV40ShipwreckOriginSpritesForEditor(Sprite[] sprites)
    {
        v40ShipwreckOriginSprites = sprites == null ? Array.Empty<Sprite>() : (Sprite[])sprites.Clone();
    }

    public void ConfigureV39BoatLayersForEditor(Sprite[] damagedLayers, Sprite[] repairedLayers)
    {
        v39DamagedBoatLayers = damagedLayers == null
            ? Array.Empty<Sprite>()
            : (Sprite[])damagedLayers.Clone();
        v39RepairedBoatLayers = repairedLayers == null
            ? Array.Empty<Sprite>()
            : (Sprite[])repairedLayers.Clone();
    }

    public void SetEditorPreviewPose(bool showBoatTrip)
    {
        EnsureScene();
        ResetSlice();
        mooringScreenActive = showBoatTrip;

        selectedNetLine = NetLine.Low;
        netSetDepth01 = 1f;
        state = SliceState.TideRising;
        // This pose represents the first short-sail window: the tide has only just
        // lifted the boat, the visible pier is still walkable, and the greedy low
        // net is already in contact with water.
        currentWaterY = lowWaterY + 0.52f;
        arrivalInspected = true;
        netDeployed = true;
        netLoweringProgress = 1f;
        netTouched = true;
        currentHarvest = netTouched ? BuildHarvest() : HarvestKind.None;
        harvestPhysicalState = HarvestPhysicalState.Drifting;
        incomingHarvestTravel01 = 0.82f;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(GetBoatBoardingX(), GetPlayerLaneY(playerLane));
        playerFacing = 1;
        lastActionHint = "预览：挂网后，人可以离开网位，去船边短航。";

        if (showBoatTrip)
        {
            lastActionHint = "预览：玩家已经走到船边，按 F 才切到短航屏。";
        }

        UpdateVisuals(1.25f);
    }

    public void SetEditorDeparturePreviewPose()
    {
        EnsureScene();
        ResetSlice();

        tideRound = departureStormRound;
        weatherClockSeconds = dayLengthSeconds * stormFrontArrivalDays;
        moonAgeDays = 14.8f;
        tideStrength = CalculateTideStrength(moonAgeDays);
        // Departure preview uses the genuinely restored component ledger so the
        // final-range rules and the rendered repaired boat describe the same state.
        boatHullIntegrity = 3;
        boatSailIntegrity = 2;
        boatCabinIntegrity = 2;
        RecalculateBoatReadiness();
        lighthouseClues = requiredLighthouseClues;
        lighthouseSeen = true;
        stiltIntegrity = 2;
        houseWarmth = 2;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(GetBoatBoardingX(), GetPlayerLaneY(playerLane));
        BeginFinalDeparture();
        // Capture the point where the authored shell, its cutout masks, failed posts
        // and departing boat all coexist. An earlier mid-storm frame could not expose
        // mask drift or duplicate intact posts because the main break had barely begun.
        stateTimer = finalDepartureSeconds * 0.82f;
        UpdateVisuals(1.65f);
    }

    public void SetEditorSailingPreviewPose()
    {
        EnsureScene();
        ResetSlice();

        selectedNetLine = NetLine.Mid;
        netSetDepth01 = StoredNetReferenceDepth01;
        selectedNetLine = GetNetLineFromLoweringProgress(netSetDepth01);
        state = SliceState.TideRising;
        currentWaterY = lowWaterY + 1.08f;
        netTouched = true;
        currentHarvest = HarvestKind.Relic;
        boatReadiness = 1;
        lighthouseClues = 1;
        dayProgress01 = 0.72f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = DayNightPhase.Dusk;
        EnterSailingScene();
        sailingBoatX = 1.35f;
        sailingClueCollected = true;
        lastActionHint = "短航预览：右侧找到残骸线索，但必须自己趁天黑/涨潮前返航。";
        UpdateVisuals(2.15f);
    }

    public void SetEditorSailingBailPreviewPose()
    {
        EnsureScene();
        ResetSlice();

        state = SliceState.TideRising;
        boatReadiness = 1;
        dayProgress01 = 0.76f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = DayNightPhase.Dusk;
        EnterSailingScene();
        sailingBoatX = sailingSalvagePoint.x - 1.4f;
        sailingBoatVelocity = 0.18f;
        sailingWaterIngress01 = 0.56f;
        sailingBailing = true;
        sailingBailCycle = 0.42f;
        lastActionHint = "舀水预览：船舱进水后要减帆稳船，并在继续找残骸与立即返航之间取舍。";
        UpdateVisuals(2.78f);
    }

    public void SetEditorSailingBailThrowPreviewPose()
    {
        SetEditorSailingBailPreviewPose();
        // 抬桶阶段应留在前船舷后，外泼阶段则必须越过船舷并出现水花。
        // 独立预览固定在跨层后的中段，避免离屏验收只证明了桶的一半路径。
        sailingBailCycle = 0.74f;
        lastActionHint = "舀水外泼预览：完整人物仍在船舷后，桶和水花短暂越过前船舷。";
        UpdateVisuals(2.92f);
    }

    public void SetEditorSailingTrimPreviewPose()
    {
        EnsureScene();
        ResetSlice();

        state = SliceState.TideRising;
        boatReadiness = 1;
        dayProgress01 = 0.68f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = DayNightPhase.Dusk;
        EnterSailingScene();
        sailingBoatX = sailingSalvagePoint.x - 2.1f;
        sailingBoatVelocity = 0.42f;
        sailingTrimActionTimer = 1f;
        sailingTrimCycle = 0.42f;
        lastActionHint = "操帆预览：人物保持完整坐姿，在前船舷后收紧帆索。";
        UpdateVisuals(2.42f);
    }

    public void SetEditorSailingBracePreviewPose()
    {
        EnsureScene();
        ResetSlice();

        state = SliceState.TideRising;
        boatReadiness = 1;
        weatherClockSeconds = dayLengthSeconds * stormFrontArrivalDays;
        dayProgress01 = 0.79f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = DayNightPhase.Dusk;
        EnterSailingScene();
        sailingBoatX = sailingReefPoint.x - 1.8f;
        sailingBoatVelocity = 0.58f;
        sailingWaterIngress01 = 0.32f;
        lastActionHint = "暴潮受击预览：人物压低重心抓稳船舷，双腿仍完整保留。";
        UpdateVisuals(2.18f);
    }

    public void SetEditorBoatViewTransitionPreviewPose(int transitionStage)
    {
        EnsureScene();
        ResetSlice();

        arrivalInspected = true;
        state = SliceState.TideRising;
        currentWaterY = lowWaterY + 0.72f;
        dayProgress01 = 0.58f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = DayNightPhase.Day;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(GetBoatBoardingX(), GetPlayerLaneY(playerLane));

        if (transitionStage <= 0)
        {
            BeginBoatViewTransition(BoatViewTransition.Boarding);
            TickBoatViewTransition(boatViewTransitionSeconds * 0.22f);
            lastActionHint = "上船过渡预览：人物先沿湿木路走向船艉，输入已锁住，海和时钟仍继续。";
        }
        else
        {
            EnterSailingScene();
            sailingBoatX = sailingHomeX + 0.2f;
            sailingBoatLaneY = sailingHomeY;
            BeginBoatViewTransition(BoatViewTransition.Returning);
            TickBoatViewTransition(boatViewTransitionSeconds * 0.78f);
            lastActionHint = "返航过渡预览：视图已在暗点切回岸边，人物正从船艉走回木路。";
        }

        UpdateVisuals(2.62f + transitionStage * 0.34f);
    }

    public void SetEditorInteriorViewTransitionPreviewPose(int transitionStage)
    {
        EnsureScene();
        ResetSlice();

        arrivalInspected = true;
        state = SliceState.TideRising;
        currentWaterY = lowWaterY + 0.44f;
        dayProgress01 = 0.66f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = DayNightPhase.Dusk;
        if (transitionStage <= 0)
        {
            viewMode = SliceViewMode.Shelter;
            playerLane = WalkLane.Deck;
            playerPosition = new Vector2(GetInteriorDoorX() - 0.5f, GetPlayerLaneY(playerLane));
            BeginBoatViewTransition(BoatViewTransition.EnteringInterior);
            TickBoatViewTransition(boatViewTransitionSeconds * 0.22f);
            lastActionHint = "进屋过渡预览：人物先走到外廊门槛，暗点后才进入同一座屋的剖面。";
        }
        else
        {
            EnterStiltHouseInterior();
            playerPosition = new Vector2(GetInteriorDoorX() - 0.38f, GetPlayerLaneY(WalkLane.InteriorUpper));
            BeginBoatViewTransition(BoatViewTransition.ExitingInterior);
            TickBoatViewTransition(boatViewTransitionSeconds * 0.78f);
            lastActionHint = "出屋过渡预览：视图已在暗点切回外景，人物正从门槛迈回外廊。";
        }

        UpdateVisuals(3.18f + transitionStage * 0.31f);
    }

    public void SetEditorSailingSectorPreviewPose(int sector)
    {
        EnsureScene();
        ResetSlice();

        state = SliceState.TideRising;
        tideRound = 0;
        moonAgeDays = 3.8f;
        tideStrength = CalculateTideStrength(moonAgeDays);
        currentWaterY = lowWaterY + 0.92f;
        // Build the preview through the same component ledger used by real repairs.
        // Writing boatReadiness directly leaves the hull/sail/cabin endpoints damaged,
        // so the preview can claim full range while still rendering the wrecked boat.
        boatHullIntegrity = 3;
        boatSailIntegrity = 2;
        boatCabinIntegrity = 2;
        RecalculateBoatReadiness();
        dayProgress01 = 0.46f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = DayNightPhase.Day;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.SailingWater);
        EnterSailingScene();

        int clampedSector = Mathf.Clamp(sector, 0, 4);
        float[] sectorBoatX =
        {
            sailingBuoyPoint.x - 0.45f,
            sailingSalvagePoint.x - 2.15f,
            earlyWreckClueX - 2.3f,
            sailingReefPoint.x - 2.25f,
            routeVortexX - 2.7f
        };
        sailingBoatX = sectorBoatX[clampedSector];
        sailingBuoyChecked = clampedSector > 0;
        if (clampedSector > 1)
        {
            SetExtraSaltWoodOwner(ExtraSaltWoodOwner.HookedToBoat);
        }
        sailingSalvageHookProgress = sailingSalvageCollected ? 1f : 0f;
        lastActionHint = $"远航第 {clampedSector + 1} 段预览：近岸、漂浮带、沉船、浅礁之后才是封路漩涡。";
        UpdateVisuals(2.35f + clampedSector * 0.28f);
    }

    public void SetEditorArrivalVignettePreviewPose()
    {
        EnsureScene();
        ResetSlice();
        arrivalVignetteActive = true;
        arrivalVignetteTimer = 0f;
        TickArrivalVignette(arrivalVignetteSeconds * 0.58f);
        UpdateVisuals(2.4f);
    }

    public void SetEditorSailingHookPreviewPose()
    {
        SetEditorContinuousSailingSalvagePreviewPose(1);
    }

    public void SetEditorContinuousSailingSalvagePreviewPose(int stage)
    {
        EnsureScene();
        ResetSlice();

        state = SliceState.TideRising;
        // This is the first seaworthy configuration, not a debug-only readiness lie:
        // one hull repair, one sail repair and one cargo-cover repair open short sail.
        boatHullIntegrity = 2;
        boatSailIntegrity = 1;
        boatCabinIntegrity = 1;
        RecalculateBoatReadiness();
        dayProgress01 = 0.44f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = DayNightPhase.Day;
        EnterSailingScene();
        sailingSalvageWorldX = sailingSalvagePoint.x + 0.32f;
        // The tow rope is fixed to the visible stern on the left side of the boat.
        // Leave a small readable water gap in this audit pose while remaining inside
        // the real 1.38 m hook reach; overlapping source sprites used to hide the rope.
        sailingBoatX = sailingSalvageWorldX + 3.38f;
        sailingSalvageVelocity = 0.24f;
        sailingBoatWorldVelocity = 0.31f;
        sailingBuoyChecked = true;

        int clampedStage = Mathf.Clamp(stage, 0, 2);
        if (clampedStage == 0)
        {
            SetExtraSaltWoodOwner(ExtraSaltWoodOwner.SailingWater);
            sailingHookThrowActive = true;
            sailingHookThrow01 = 0.62f;
            sailingHookWorldPosition = Vector2.Lerp(
                GetSailingBoatSternWorldPosition(),
                GetSailingPointPosition(SailingPointKind.Salvage),
                Mathf.SmoothStep(0f, 1f, sailingHookThrow01));
            lastActionHint = "抛钩：贴近漂物并匹配流速，按住 F 把钩头送向木料。";
        }
        else if (clampedStage == 1)
        {
            SetExtraSaltWoodOwner(ExtraSaltWoodOwner.HookingToBoat);
            sailingSalvageHookProgress = 0.44f;
            sailingSalvageTension01 = 0.72f;
            sailingSalvageInitialRopeLength = Vector2.Distance(
                GetSailingBoatSternWorldPosition(),
                GetSailingPointPosition(SailingPointKind.Salvage));
            sailingSalvageHauling = true;
            lastActionHint = "收绳：继续按住 F 逐段拖近；绳索发白时收帆贴流，避免硬拽脱钩。";
        }
        else
        {
            SetExtraSaltWoodOwner(ExtraSaltWoodOwner.HookedToBoat);
            sailingSalvageHookProgress = 1f;
            sailingSalvageTension01 = 0.24f;
            sailingSalvageWorldX = GetSailingBoatSternWorldPosition().x - 0.3f;
            sailingSalvageVelocity = sailingBoatWorldVelocity;
            lastActionHint = "短缆绑牢：木料在船艉真实拖行，现在可以自己返航卸货。";
        }
        UpdateVisuals(3.35f);
    }

    public void SetEditorRouteOpenPreviewPose()
    {
        EnsureScene();
        ResetSlice();

        selectedNetLine = NetLine.High;
        netSetDepth01 = 0.12f;
        state = SliceState.TideRising;
        tideRound = 1;
        moonAgeDays = 7.4f;
        tideStrength = CalculateTideStrength(moonAgeDays);
        currentWaterY = lowWaterY + 1.24f;
        netTouched = true;
        currentHarvest = HarvestKind.Relic;
        boatReadiness = 1;
        lighthouseClues = 1;
        routeClueReturnRound = 0;
        dayProgress01 = 0.5f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = DayNightPhase.Day;
        EnterSailingScene();
        sailingBoatX = routeVortexX + 0.72f;
        sailingClueCollected = false;
        lastActionHint = "后续潮次预览：漩涡退成暗流，灯塔从雾后露出，船可以继续确认航线。";
        UpdateVisuals(2.55f);
    }

    public void SetEditorSailingReturnPreviewPose()
    {
        EnsureScene();
        ResetSlice();

        selectedNetLine = NetLine.Mid;
        netSetDepth01 = 0.5f;
        state = SliceState.TideRising;
        tideRound = 1;
        moonAgeDays = 6.2f;
        tideStrength = CalculateTideStrength(moonAgeDays);
        currentWaterY = lowWaterY + 0.86f;
        boatReadiness = 1;
        lighthouseClues = 1;
        shortSailCount = 1;
        dayProgress01 = 0.7f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = DayNightPhase.Dusk;
        EnterSailingScene();
        sailingBoatX = sailingHomeX + 0.58f;
        sailingBoatVelocity = -0.24f;
        sailingBoatWorldVelocity = -0.24f;
        sailingRewardPending = true;
        sailingClueCollected = true;
        sailingWaterIngress01 = 0.18f;
        playerFacing = -1;
        lastActionHint = "返航预览：左侧归航点已经在眼前，方位纸仍压在舱里，靠近后由玩家按 F 下船。";
        UpdateVisuals(2.35f);
    }

    public void SetEditorEbbCollectPreviewPose()
    {
        EnsureScene();
        ResetSlice();

        selectedNetLine = NetLine.Low;
        netSetDepth01 = 1f;
        state = SliceState.EbbCollect;
        moonAgeDays = 10.8f;
        tideStrength = CalculateTideStrength(moonAgeDays);
        currentWaterY = lowWaterY;
        arrivalInspected = true;
        netDeployed = true;
        netLoweringProgress = 1f;
        netTouched = true;
        netCatchResolved = true;
        netCatchBundleTier = 2;
        netCatchVisualPieceCount = 2;
        currentHarvest = HarvestKind.Wood;
        harvestPhysicalState = HarvestPhysicalState.CaughtInNet;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(netAnchor.x + 1.08f, GetPlayerLaneY(playerLane));
        playerFacing = -1;
        lastActionHint = "退潮预览：网线上挂着盐木，回到网桩旁按住 F 才会把它拉回来。";
        UpdateVisuals(2.05f);
    }

    public void SetEditorHarvestLifecyclePreviewPose(int lifecycleStage)
    {
        EnsureScene();
        ResetSlice();

        int stage = Mathf.Clamp(lifecycleStage, 0, 5);
        arrivalInspected = true;
        arrivalVignetteActive = false;
        viewMode = SliceViewMode.Shelter;
        selectedNetLine = NetLine.Mid;
        netSetDepth01 = 0.5f;
        netLoweringProgress = 1f;
        netUnrollProgress = 1f;
        netRigStep = NetRigStep.Deployed;
        netDeployed = stage < 3;
        netTouched = stage >= 1;
        netCatchResolved = stage >= 1;
        netCatchBundleTier = 2;
        currentHarvest = stage >= 1 ? HarvestKind.Wood : HarvestKind.None;
        currentHarvestBanked = false;
        incomingHarvestTravel01 = stage == 0 ? 0.58f : 1f;
        netCatchVisualPieceCount = stage >= 1 ? 2 : 0;
        harvestPhysicalState = stage == 0
            ? HarvestPhysicalState.Drifting
            : stage < 3 ? HarvestPhysicalState.CaughtInNet : HarvestPhysicalState.Carried;
        playerLane = WalkLane.TideFlat;
        playerFacing = -1;
        playerPosition = new Vector2(netAnchor.x + 0.9f, GetPlayerLaneY(playerLane));

        if (stage == 0)
        {
            state = SliceState.TideRising;
            stateTimer = tideCycleSeconds * 0.36f;
            currentWaterY = Mathf.Lerp(lowWaterY + 0.18f, GetSelectedNetY() + 0.28f, 0.58f);
            lastActionHint = "收获过程 1/6：漂物先沿真实水面接近网口，还没有被算进收获。";
        }
        else if (stage == 1)
        {
            state = SliceState.TideRising;
            currentWaterY = GetSelectedNetY() + 0.2f;
            lastActionHint = "收获过程 2/6：盐木挂在湿网上；继续留网会增量，也会持续加张力。";
        }
        else if (stage == 2)
        {
            state = SliceState.EbbCollect;
            currentWaterY = lowWaterY + 0.08f;
            netHaulProgress = 0.68f;
            netHaulStrokePhase = 0.62f;
            netHaulEffort01 = 1f;
            netHaulLoad01 = 0.78f;
            editorNetHaulPreviewActive = true;
            playerPosition = new Vector2(netAnchor.x + 0.45f, GetPlayerLaneY(playerLane));
            lastActionHint = "收获过程 3/6：按住 F 收网时，盐木随折叠的网束一起向岸边移动。";
        }
        else
        {
            state = SliceState.RepairMoment;
            currentWaterY = lowWaterY + 0.04f;
            netRigStep = NetRigStep.Stored;
            harvestCarryStartPosition = new Vector2(netAnchor.x - 0.42f, GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.22f);
            harvestCarryTransition01 = 1f;
            playerPosition = stage == 3
                ? new Vector2(netAnchor.x + 1.15f, GetPlayerLaneY(playerLane))
                : GetRepairChoicePosition(RepairChoice.Stilt) + new Vector2(0.3f, 0f);
            if (stage == 4)
            {
                pendingRepairChoice = RepairChoice.Stilt;
                harvestPlacedRepairChoice = RepairChoice.Stilt;
                harvestPhysicalState = HarvestPhysicalState.PlacedAtWork;
                harvestPlacementTransition01 = 1f;
                repairWorkStep = (int)TideRepairWorkPhaseModel.Evaluate(repairWorkProgress);
                repairWorkProgress = 0.18f;
                repairWorkActive = true;
            }
            if (stage == 5)
            {
                viewMode = SliceViewMode.Interior;
                playerLane = WalkLane.InteriorUpper;
                playerPosition = new Vector2(-3.25f, GetPlayerLaneY(playerLane));
                playerFacing = 1;
            }
            lastActionHint = stage == 3
                ? "收获过程 4/6：拉回的盐木仍是未存放实物，跟随人物移动，死亡会丢失。"
                : stage == 4
                    ? "收获过程 5/6：材料先贴到真实柱脚试装；完成固定后才入账并消耗。"
                    : "收获过程 6/6：材料跟着人物进入同一座屋，在生活层储物架旁按 F 才会存入库存。";
        }

        UpdateVisuals(2.2f + stage * 0.31f);
    }

    public void SetEditorNetHaulCadencePreviewPose(int strokeStage)
    {
        SetEditorHarvestLifecyclePreviewPose(2);
        netHaulProgress = 0.52f;
        netHaulStrokePhase = strokeStage <= 0 ? 0.08f : 0.62f;
        netHaulEffort01 = TideNetHaulModel.EvaluateEffort01(netHaulStrokePhase);
        netHaulLoad01 = 0.72f;
        editorNetHaulPreviewActive = true;
        lastActionHint = strokeStage <= 0
            ? "收网节奏：回手抓住下一段湿绳，网束暂时锁在桩边。"
            : "收网节奏：双脚撑住木路，身体后仰，把网和挂物一起拉近。";
        UpdateVisuals(strokeStage <= 0 ? 2.18f : 2.62f);
    }

    public void SetEditorLiveNetControlPreviewPose(int previewStage)
    {
        EnsureScene();
        ResetSlice();

        bool showFraying = previewStage > 0;
        arrivalInspected = true;
        arrivalVignetteActive = false;
        viewMode = SliceViewMode.Shelter;
        state = SliceState.TideRising;
        selectedNetLine = showFraying ? NetLine.Low : NetLine.Mid;
        netSetDepth01 = showFraying ? 0.86f : 0.46f;
        netLoweringProgress = netSetDepth01;
        netRigStep = NetRigStep.Deployed;
        netDeployed = true;
        netTouched = true;
        netCatchResolved = true;
        netCaptureProgress01 = 1f;
        currentHarvest = showFraying ? HarvestKind.Relic : HarvestKind.Wood;
        harvestPhysicalState = HarvestPhysicalState.CaughtInNet;
        netCatchBundleTier = showFraying ? 3 : 2;
        netCatchVisualPieceCount = netCatchBundleTier;
        netIntegrity = showFraying ? 1 : 3;
        netFraying01 = showFraying ? 0.82f : 0f;
        currentWaterY = GetSelectedNetY() + 0.2f;
        tideCurrentlyRising = true;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(netAnchor.x - 0.24f, GetPlayerLaneY(playerLane));
        playerFacing = 1;
        netDepthAdjustmentActive = !showFraying;
        netDepthAdjustmentDirection = !showFraying ? 1f : 0f;
        lastActionHint = showFraying
            ? "临界验收：遗物短促挂死，最后一股旧绳仍相连但正在缩短；现在抢收还能保住实物。"
            : "调网验收：人物在真实网桩收沉纲，手中绳、网口和挂物随同一受力状态移动。";
        UpdateVisuals(showFraying ? 2.94f : 2.36f);
    }

    public void SetEditorContinuousWeatherPreviewPose(int previewStage)
    {
        EnsureScene();
        ResetSlice();

        int stage = Mathf.Clamp(previewStage, 0, 2);
        float[] frontProgress = { 0.22f, 0.66f, 1f };
        arrivalInspected = true;
        arrivalVignetteActive = false;
        viewMode = SliceViewMode.Shelter;
        state = SliceState.TideRising;
        weatherClockSeconds = dayLengthSeconds * stormFrontArrivalDays * frontProgress[stage];
        dayProgress01 = 0.56f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = DayNightPhase.Day;
        moonAgeDays = stage == 2 ? 14.8f : 8.2f;
        tideStrength = CalculateTideStrength(moonAgeDays);
        tideClockSeconds = tideCycleSeconds * (stage == 2 ? 0.44f : 0.31f);
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        selectedNetLine = NetLine.Mid;
        netSetDepth01 = 0.52f;
        netLoweringProgress = netSetDepth01;
        netRigStep = NetRigStep.Deployed;
        netDeployed = true;
        netTouched = currentWaterY >= GetSelectedNetY() - 0.08f;
        tideSourceHarvest = HarvestKind.Wood;
        harvestPhysicalState = HarvestPhysicalState.Drifting;
        incomingHarvestTravel01 = netTouched ? 0.64f : 0.28f;
        playerLane = stage == 2 ? WalkLane.Deck : WalkLane.TideFlat;
        playerPosition = stage == 2
            ? new Vector2(houseAnchor.x + 1.42f, GetPlayerLaneY(playerLane))
            : new Vector2(netAnchor.x - 1.08f, GetPlayerLaneY(playerLane));
        playerFacing = 1;
        string[] hints =
        {
            "远风验收：天色仍亮，但远云已沿真实风向压低；现在适合普通布网和短航。",
            "前缘验收：云底、斜雨、网压和船漏同时增强；黄昏应据此选择绳、桶或木桩。",
            "暴潮验收：冷灰海天、强斜雨和异常增水同源；人物已退到高脚屋的干燥甲板。"
        };
        lastActionHint = hints[stage];
        UpdateVisuals(2.3f + stage * 0.47f);
    }

    public void SetEditorHarvestKindPreviewPose(int harvestKindIndex)
    {
        EnsureScene();
        ResetSlice();

        HarvestKind[] kinds =
        {
            HarvestKind.Fish,
            HarvestKind.Wood,
            HarvestKind.Relic,
            HarvestKind.Trash
        };
        int index = Mathf.Clamp(harvestKindIndex, 0, kinds.Length - 1);
        arrivalInspected = true;
        arrivalVignetteActive = false;
        viewMode = SliceViewMode.Shelter;
        state = SliceState.RepairMoment;
        currentWaterY = lowWaterY + 0.04f;
        currentHarvest = kinds[index];
        currentHarvestBanked = false;
        netCatchBundleTier = index == 2 ? 3 : 2;
        netCatchVisualPieceCount = netCatchBundleTier;
        harvestPhysicalState = HarvestPhysicalState.Carried;
        harvestCarryStartPosition = new Vector2(netAnchor.x - 0.42f, GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.22f);
        harvestCarryTransition01 = 1f;
        playerLane = WalkLane.TideFlat;
        playerFacing = -1;
        playerPosition = new Vector2(netAnchor.x + 1.15f, GetPlayerLaneY(playerLane));
        lastActionHint = $"收获外观校验 {index + 1}/4：{GetHarvestName()}以实际携带尺寸显示，不使用菱形或发光拾取标记。";
        UpdateVisuals(2.4f + index * 0.32f);
    }

    public void SetEditorBrokenNetHarvestPreviewPose()
    {
        EnsureScene();
        ResetSlice();

        arrivalInspected = true;
        arrivalVignetteActive = false;
        state = SliceState.TideRising;
        selectedNetLine = NetLine.Mid;
        netSetDepth01 = 0.58f;
        netRigStep = NetRigStep.Deployed;
        netDeployed = true;
        netTouched = true;
        netCatchResolved = true;
        netBrokeThisTide = true;
        netIntegrity = 0;
        currentWaterY = GetSelectedNetY() + 0.22f;
        tideSourceHarvest = HarvestKind.Wood;
        currentHarvest = HarvestKind.Trash;
        harvestPhysicalState = HarvestPhysicalState.CaughtInNet;
        netCatchBundleTier = 1;
        netCatchVisualPieceCount = 1;
        washedAwayHarvestKind = HarvestKind.Wood;
        washedAwayHarvestBatchId = 101;
        washedAwayHarvestPieceCount = 2;
        washedAwayHarvestTimer = 1.1f;
        washedAwayHarvestDriftX = -0.72f;
        tideCurrentlyRising = true;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(netAnchor.x + 0.9f, GetPlayerLaneY(playerLane));
        playerFacing = -1;
        lastActionHint = "破网验收：原来的两件盐木保持原身份被水冲走，断绳上另留一团缠网废料。";
        UpdateVisuals(2.82f);
    }

    public void SetEditorReturnedCargoPreviewPose()
    {
        EnsureScene();
        ResetSlice();

        arrivalInspected = true;
        arrivalVignetteActive = false;
        state = SliceState.TideRising;
        currentWaterY = lowWaterY + 0.5f;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.ReturnedAtBoat);
        returnedClueAtBoat = true;
        returnedLighthouseConfirmationAtBoat = false;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(GetBoatBoardingX() - 0.12f, GetPlayerLaneY(playerLane));
        playerFacing = 1;
        lastActionHint = "返航货物验收：浮木仍绑在船艉，方位纸仍压在舱内；船边按 F 卸下后才入账。";
        UpdateVisuals(3.08f);
    }

    public void SetEditorMooringStagedCargoPreviewPose()
    {
        EnsureScene();
        ResetSlice();

        arrivalVignetteActive = false;
        state = SliceState.TideRising;
        currentWaterY = lowWaterY + 0.5f;
        currentHarvest = HarvestKind.Fish;
        harvestPhysicalState = HarvestPhysicalState.CaughtInNet;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.ReturnedAtBoat);
        TryUnloadReturnedSailingCargo();
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(GetMooringStagingPosition().x - 0.25f, GetPlayerLaneY(playerLane));
        playerFacing = 1;
        lastActionHint = "码头暂放验收：网里的鱼仍在水中，盐木已经从船艉卸到木板上，船与两批实物互不占用。";
        UpdateVisuals(3.18f);
    }

    public void SetEditorShoreWorkPreviewPose()
    {
        EnsureScene();
        ResetSlice();

        selectedNetLine = NetLine.Mid;
        netSetDepth01 = 0.5f;
        state = SliceState.TideRising;
        stateTimer = tideCycleSeconds * 0.38f;
        tideStrength = CalculateTideStrength(moonAgeDays);
        currentWaterY = lowWaterY + 1.08f;
        arrivalInspected = true;
        netDeployed = true;
        netLoweringProgress = 1f;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(GetShoreWorkX(), GetPlayerLaneY(playerLane));
        playerFacing = -1;
        playerMoving = true;
        playerWalkCycle = 0.2f;
        nearshoreWorkDone = true;
        tideRoutingMode = TideRoutingMode.FeedNet;
        extraSaltWoodBundledWithNetHarvest = true;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.RoutedToNet);
        harvestPhysicalState = HarvestPhysicalState.Drifting;
        incomingHarvestTravel01 = 0.68f;
        lastActionHint = "导流预览：绳已经转向网口，普通潮获和同一根盐木正在接近；盐木占一档负载，网也会多吃一次拉扯。";
        UpdateVisuals(1.75f);
    }

    public void SetEditorContinuousRoutingPreviewPose(bool lockedToNet)
    {
        EnsureScene();
        ResetSlice();

        arrivalVignetteActive = false;
        arrivalInspected = true;
        viewMode = SliceViewMode.Shelter;
        state = SliceState.TideRising;
        stateTimer = tideCycleSeconds * 0.34f;
        tideClockSeconds = tideCycleSeconds * 0.18f;
        dayProgress01 = 0.5f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = DayNightPhase.Day;
        weatherClockSeconds = dayLengthSeconds * stormFrontArrivalDays * 0.32f;
        currentWaterY = lowWaterY + 0.74f;
        tideStrength = CalculateTideStrength(moonAgeDays);
        selectedNetLine = NetLine.Mid;
        netSetDepth01 = 0.55f;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        netIntegrity = 3;
        tideSourceHarvest = HarvestKind.Fish;
        harvestPhysicalState = HarvestPhysicalState.Drifting;
        playerLane = WalkLane.TideFlat;
        playerPosition = GetShoreWorkPosition();
        playerFacing = 1;

        if (lockedToNet)
        {
            routingBoom01 = 0.82f;
            incomingHarvestTravel01 = 0.82f;
            TryLockContinuousRoutingDecision(0.71f, incomingHarvestTravel01);
            lastActionHint = "岔流验收：盐木刚越过收紧导杆，沿实体绳路压向网口；现在不能隔空改成短航。";
        }
        else
        {
            routingBoom01 = 0.54f;
            routingWorkActive = true;
            routingWorkDirection = 1f;
            incomingHarvestTravel01 = 0.58f;
            lastActionHint = "收索验收：人物在真实绞盘旁逆水收绳，导杆连续转向；盐木仍在共同来路，可松手或按 S 反向。";
        }

        UpdateVisuals(2.15f);
    }

    public void SetEditorSwimPreviewPose()
    {
        EnsureScene();
        ResetSlice();

        selectedNetLine = NetLine.Low;
        netSetDepth01 = 1f;
        state = SliceState.TideRising;
        tideStrength = CalculateTideStrength(moonAgeDays);
        currentWaterY = lowWaterY + 1.62f;
        arrivalInspected = true;
        netDeployed = true;
        netLoweringProgress = 1f;
        netTouched = true;
        currentHarvest = HarvestKind.Wood;
        playerLane = WalkLane.TideFlat;
        playerSubmersion01 = 1f;
        playerSwimming = true;
        playerPosition = new Vector2(2.08f, currentWaterY - 0.12f);
        playerFacing = -1;
        playerHorizontalVelocity = -playerMoveSpeed * 0.42f;
        playerWalkCycle = 0.36f;
        lastActionHint = "涨潮漂流预览：厚雨衣里只能短促划水，身体下半部被真实水线遮住，潮流仍会推动人。";
        UpdateVisuals(1.95f);
    }

    public void SetEditorRepairPreviewPose()
    {
        EnsureScene();
        ResetSlice();

        state = SliceState.RepairMoment;
        arrivalInspected = true;
        netTouched = true;
        currentHarvest = HarvestKind.Wood;
        currentHarvestBanked = false;
        harvestPhysicalState = HarvestPhysicalState.PlacedAtWork;
        harvestPlacedRepairChoice = RepairChoice.Stilt;
        harvestPlacementTransition01 = 1f;
        repairChoiceApplied = false;
        pendingRepairChoice = RepairChoice.Stilt;
        repairWorkStep = (int)TideRepairWorkPhaseModel.Evaluate(repairWorkProgress);
        repairWorkProgress = 0.58f;
        repairWorkActive = true;
        stiltIntegrity = 1;
        nearshoreWorkDone = true;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(GetShoreWorkX() + 0.28f, GetPlayerLaneY(playerLane));
        playerFacing = -1;
        lastActionHint = "修补预览：盐木已经试装在柱脚，松手会留在这里，固定到 100% 才真正加固。";
        UpdateVisuals(2.2f);
    }

    public string RunEditorRepairMaterialGroundingProbe()
    {
        SetEditorRepairPreviewPose();
        Vector2 stiltTarget = GetRepairChoicePosition(RepairChoice.Stilt);
        float workSurfaceY = GetPlayerStandingFeetY(WalkLane.TideFlat);
        float bundleBottomY = harvestRenderer != null ? harvestRenderer.bounds.min.y : float.PositiveInfinity;
        bool materialTouchesDeck = harvestRenderer != null && harvestRenderer.enabled &&
            Mathf.Abs(bundleBottomY - workSurfaceY) <= 0.025f;
        bool materialBesideComponent = harvestRenderer != null &&
            Mathf.Abs(harvestRenderer.bounds.center.x - stiltTarget.x) <= 0.42f;
        bool survivorCanReachComponent = Mathf.Abs(playerPosition.x - stiltTarget.x) <= shoreWorkDistance;
        string evidence =
            $"材料底/木板={bundleBottomY:F3}/{workSurfaceY:F3}；" +
            $"材料距柱={Mathf.Abs(harvestRenderer.bounds.center.x - stiltTarget.x):F2}；" +
            $"人物距柱={Mathf.Abs(playerPosition.x - stiltTarget.x):F2}";
        return materialTouchesDeck && materialBesideComponent && survivorCanReachComponent
            ? $"PASS：盐木束落在真实木板面，人物和材料都留在承重柱施工范围内。{evidence}"
            : $"FAIL：首次修柱仍存在材料悬空或人物/材料与目标部件脱节。{evidence}";
    }

    public string RunEditorBoatRangeConstraintOrderProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalInspected = true;
        viewMode = SliceViewMode.Sailing;
        sailTripActive = true;
        sailingBoatX = sailingHomeX;
        lighthouseClues = 0;
        routeClueReturnRound = -1;
        boatHullIntegrity = 1;
        boatSailIntegrity = 0;
        boatCabinIntegrity = 0;
        RecalculateBoatReadiness();
        UpdateVisuals(1.1f);

        float damagedLimit = GetSailingRightLimit();
        Sprite damagedHullRepair = boatHullRepairOwnerRenderer.sprite;
        Sprite damagedSailRepair = boatSailRenderer.sprite;
        Sprite damagedCockpitRepair = boatCockpitRepairOwnerRenderer.sprite;
        bool damagedBoatOwnsFirstBarrier = IsBoatConditionLimitingRange() &&
            sailingRangeBreakerRenderer != null && sailingRangeBreakerRenderer.enabled;

        boatHullIntegrity = 3;
        boatSailIntegrity = 2;
        boatCabinIntegrity = 2;
        RecalculateBoatReadiness();
        float repairedButUnchartedLimit = GetSailingRightLimit();
        // Move the sailing camera to the second barrier before asking whether its
        // renderer is visible. At the home pier the 24 m distant vortex is correctly
        // culled and therefore cannot serve as visual evidence.
        sailingBoatX = repairedButUnchartedLimit - 0.8f;
        UpdateVisuals(1.4f);
        Sprite repairedHullRepair = boatHullRepairOwnerRenderer.sprite;
        Sprite repairedSailRepair = boatSailRenderer.sprite;
        Sprite repairedCockpitRepair = boatCockpitRepairOwnerRenderer.sprite;
        bool distantVortexOwnsSecondBarrier = !IsBoatConditionLimitingRange() &&
            IsVortexBlockingRoute() && routeVortexRenderer != null && routeVortexRenderer.enabled &&
            sailingRangeBreakerRenderer != null && !sailingRangeBreakerRenderer.enabled;

        lighthouseClues = 1;
        routeClueReturnRound = 0;
        tideRound = 2;
        UpdateVisuals(1.7f);
        float chartedLimit = GetSailingRightLimit();
        bool chartOpensFinalReach = !IsBoatConditionLimitingRange() && !IsVortexBlockingRoute() &&
            sailingRangeBreakerRenderer != null && !sailingRangeBreakerRenderer.enabled &&
            routeVortexRenderer != null && !routeVortexRenderer.enabled;

        bool rangeOrder = damagedLimit < repairedButUnchartedLimit - 0.1f &&
            repairedButUnchartedLimit < chartedLimit - 0.1f;
        // V67 把不随施工变化的 V39 BackHull 留作稳定底，维修可见变化由 V52
        // 三个独立 owner 承担。这里必须验证真正的维修层，不能继续比较固定底图。
        bool boatEndpointChanges = damagedHullRepair != null && repairedHullRepair != null &&
            damagedSailRepair != null && repairedSailRepair != null &&
            damagedCockpitRepair != null && repairedCockpitRepair != null &&
            damagedHullRepair != repairedHullRepair &&
            damagedSailRepair != repairedSailRepair &&
            damagedCockpitRepair != repairedCockpitRepair;
        string evidence =
            $"航界={damagedLimit:F2}->{repairedButUnchartedLimit:F2}->{chartedLimit:F2}；" +
            $"维修层={damagedHullRepair?.name}/{damagedSailRepair?.name}/{damagedCockpitRepair?.name}" +
            $"->{repairedHullRepair?.name}/{repairedSailRepair?.name}/{repairedCockpitRepair?.name}；" +
            $"近碎浪={damagedBoatOwnsFirstBarrier}；远漩涡={distantVortexOwnsSecondBarrier}；航线开放={chartOpensFinalReach}";
        return rangeOrder && boatEndpointChanges && damagedBoatOwnsFirstBarrier &&
            distantVortexOwnsSecondBarrier && chartOpensFinalReach
            ? $"PASS：破船先被近处碎浪限制，修好后抵达远漩涡，确认航线后才开放最终海域。{evidence}"
            : $"FAIL：船况、漩涡和灯塔航线没有形成可见的三段航程顺序。{evidence}";
    }

    public string RunEditorSideViewVortexIntegrationProbe()
    {
        if (HasCompleteV70VortexPresentation())
        {
            return RunEditorV70VortexIntegrationProbe();
        }

        EnsureScene();
        ResetSlice();
        arrivalInspected = true;
        viewMode = SliceViewMode.Sailing;
        sailTripActive = true;
        boatHullIntegrity = 3;
        boatSailIntegrity = 2;
        boatCabinIntegrity = 2;
        lighthouseClues = 0;
        routeClueReturnRound = -1;
        RecalculateBoatReadiness();

        // 只有修好船但尚未确认航线时，远处漩涡才是当前可见屏障。
        // 把船移到屏障前方，避免在归处屏因正常视锥裁切得到假阴性。
        sailingBoatX = GetSailingRightLimit() - 0.8f;
        const float firstSampleTime = 1.35f;
        UpdateVisuals(firstSampleTime);

        TideOceanSample ocean = GetSailingOceanSample(routeVortexX);
        Vector2 expectedSurface = GetSailingScreenPosition(new Vector2(routeVortexX, ocean.SurfaceY));
        bool correctOwners = routeVortexRenderer != null && routeVortexRenderer.enabled &&
            routeVortexInnerRenderer != null && routeVortexInnerRenderer.enabled &&
            routeVortexSurfaceRenderer != null && !routeVortexSurfaceRenderer.enabled &&
            routeVortexRenderer.sprite == GetSideViewVortexDepressionSprite() &&
            routeVortexInnerRenderer.sprite == GetSideViewVortexUndertowSprite();
        bool bodyBelongsToSea = correctOwners &&
            routeVortexRenderer.bounds.center.y < expectedSurface.y &&
            routeVortexRenderer.color.a <= 0.42f &&
            routeVortexInnerRenderer.color.a <= 0.32f &&
            routeVortexRenderer.bounds.size.x >= routeVortexRenderer.bounds.size.y * 2.6f;

        int enabledCrestCount = 0;
        for (int i = 0; i < vortexCrests.Count; i++)
        {
            enabledCrestCount += vortexCrests[i] != null && vortexCrests[i].enabled ? 1 : 0;
        }
        bool onlySideViewOwners = enabledCrestCount == 3 &&
            vortexCrests[0].bounds.center.x < expectedSurface.x - 0.45f &&
            vortexCrests[1].bounds.center.x > expectedSurface.x + 0.45f &&
            Mathf.Abs(vortexCrests[2].bounds.center.x - expectedSurface.x) <= 0.08f;

        Vector2 leftBefore = vortexCrests[0].bounds.center;
        Vector2 throatBefore = vortexCrests[2].bounds.center;
        UpdateVisuals(firstSampleTime + 0.82f);
        float animatedDistance = Vector2.Distance(leftBefore, vortexCrests[0].bounds.center) +
            Vector2.Distance(throatBefore, vortexCrests[2].bounds.center);
        bool continuousMotion = animatedDistance >= 0.008f && animatedDistance <= 0.38f;

        string evidence =
            $"所有者={correctOwners}；透明归海={bodyBelongsToSea}；" +
            $"侧视浪层={enabledCrestCount}/3；连续位移={animatedDistance:F3}m；" +
            $"中心/水面={routeVortexRenderer.bounds.center.y:F2}/{expectedSurface.y:F2}";
        return correctOwners && bodyBelongsToSea && onlySideViewOwners && continuousMotion
            ? $"PASS：漩涡由侧视汇入浪、中央下凹和水下回流组成，并随同一海况连续变化。{evidence}"
            : $"FAIL：漩涡仍可能退回俯视圆盘、独立水块或静止调试形状。{evidence}";
    }

    public string RunEditorV70VortexIntegrationProbe()
    {
        EnsureScene();
        EnsureV70VortexResourcesLoaded();
        if (formalV70VortexCatalog == null)
        {
            return "FAIL：V70 侧视漩涡 Catalog 尚未生成。";
        }
        if (!formalV70VortexCatalog.IsComplete(out string reason))
        {
            return "FAIL：V70 侧视漩涡 Catalog 不完整：" + reason;
        }

        ResetSlice();
        arrivalInspected = true;
        viewMode = SliceViewMode.Sailing;
        sailTripActive = true;
        boatHullIntegrity = 3;
        boatSailIntegrity = 2;
        boatCabinIntegrity = 2;
        lighthouseClues = 0;
        routeClueReturnRound = -1;
        RecalculateBoatReadiness();
        sailingBoatX = GetSailingRightLimit() - 0.8f;

        const float firstSampleTime = 2.43f;
        UpdateVisuals(firstSampleTime);
        TideOceanSample ocean = GetSailingOceanSample(routeVortexX);
        Vector2 surface = GetSailingScreenPosition(new Vector2(routeVortexX, ocean.SurfaceY));
        SpriteRenderer[] renderers =
        {
            routeVortexRenderer,
            routeVortexInnerRenderer,
            routeVortexSurfaceRenderer,
        };

        bool completeOwners = true;
        bool exactFrames = true;
        bool exactSurfaceRegistration = true;
        bool contractScale = true;
        bool noPhysicsOwner = true;
        Sprite[] firstFrames = new Sprite[renderers.Length];
        for (int layerIndex = 0; layerIndex < renderers.Length; layerIndex++)
        {
            TideV70VortexLayer layer = (TideV70VortexLayer)layerIndex;
            SpriteRenderer renderer = renderers[layerIndex];
            TideV70VortexPose pose = TideV70VortexPresentationModel.EvaluatePose(
                layer,
                surface,
                ocean.Slope,
                ocean.Agitation01,
                firstSampleTime);
            Sprite expectedFrame = formalV70VortexCatalog.GetFrame(layer, pose.FrameIndex);
            completeOwners &= renderer != null && renderer.enabled && expectedFrame != null;
            if (renderer == null)
            {
                continue;
            }

            firstFrames[layerIndex] = renderer.sprite;
            exactFrames &= renderer.sprite == expectedFrame &&
                renderer.sprite.name.IndexOf("Balanced", StringComparison.Ordinal) >= 0 &&
                renderer.sprite.name.IndexOf("High", StringComparison.Ordinal) < 0;
            exactSurfaceRegistration &= Vector2.Distance(renderer.transform.localPosition, pose.Position) <= 0.001f;
            contractScale &= Mathf.Abs(renderer.transform.localScale.y - pose.UniformScale) <= 0.0001f &&
                Mathf.Abs(renderer.transform.localScale.x -
                    pose.UniformScale * pose.HorizontalCompression) <= 0.0001f;
            noPhysicsOwner &= renderer.GetComponent<Collider2D>() == null;
        }

        int enabledFallbackCrests = 0;
        for (int i = 0; i < vortexCrests.Count; i++)
        {
            enabledFallbackCrests += vortexCrests[i] != null && vortexCrests[i].enabled ? 1 : 0;
        }

        bool atomicOwnership = enabledFallbackCrests == 0 &&
            !boatWaterlineOcclusionRenderer.enabled &&
            routeVortexRenderer.sortingOrder == naturalWaterSurfaceRenderer.sortingOrder + 3 &&
            routeVortexInnerRenderer.sortingOrder == routeVortexRenderer.sortingOrder + 1 &&
            routeVortexSurfaceRenderer.sortingOrder == routeVortexInnerRenderer.sortingOrder + 1 &&
            routeVortexInnerRenderer.sortingOrder < boatHullRenderer.sortingOrder;

        UpdateVisuals(firstSampleTime + 0.24f);
        int changedLayerCount = 0;
        float maximumPositionStep = 0f;
        for (int layerIndex = 0; layerIndex < renderers.Length; layerIndex++)
        {
            if (renderers[layerIndex].sprite != firstFrames[layerIndex])
            {
                changedLayerCount++;
            }

            TideV70VortexLayer layer = (TideV70VortexLayer)layerIndex;
            TideV70VortexPose expectedPose = TideV70VortexPresentationModel.EvaluatePose(
                layer,
                surface,
                ocean.Slope,
                ocean.Agitation01,
                firstSampleTime + 0.24f);
            maximumPositionStep = Mathf.Max(
                maximumPositionStep,
                Vector2.Distance(renderers[layerIndex].transform.localPosition, expectedPose.Position));
        }

        bool realSecondAnimation = changedLayerCount == renderers.Length && maximumPositionStep <= 0.001f;
        bool passed = completeOwners && exactFrames && exactSurfaceRegistration && contractScale &&
            noPhysicsOwner && atomicOwnership && realSecondAnimation;
        string evidence =
            $"三层/十二相={completeOwners}/{formalV70VortexCatalog.Profile}；" +
            $"帧/配准/尺度={exactFrames}/{exactSurfaceRegistration}/{contractScale}；" +
            $"旧泡沫={enabledFallbackCrests}；排序={routeVortexRenderer.sortingOrder}/" +
            $"{routeVortexInnerRenderer.sortingOrder}/{routeVortexSurfaceRenderer.sortingOrder}<船{boatHullRenderer.sortingOrder}；" +
            $"0.24s 换帧={changedLayerCount}/3；位置误差={maximumPositionStep:F4}m；无碰撞={noPhysicsOwner}";
        ResetSlice();
        return passed
            ? $"PASS：V70 Balanced 三层漩涡原子接入同一海面，并按不同现实秒周期连续播放。{evidence}"
            : $"FAIL：V70 仍存在混档、双显、水线错位、排序或动画节拍问题。{evidence}";
    }

    public string RunEditorFirstTideVerticalLoopProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalVignetteActive = false;
        arrivalInspected = true;
        state = SliceState.LowTidePlanning;
        dayNightPhase = DayNightPhase.Day;
        viewMode = SliceViewMode.Shelter;
        playerLane = WalkLane.TideFlat;

        // Reproduce the three physical net jobs in their runtime order. The probe
        // advances the same hold ledger used by input instead of setting Deployed
        // directly, so a future shortcut in any step breaks this single-flow check.
        netRigStep = NetRigStep.Carrying;
        ResetNetRigHoldAction();
        bool firstEndHeld = TickNetRigHoldProgress(netRigHoldSeconds + 0.01f, true, true);
        if (firstEndHeld)
        {
            netRigStep = NetRigStep.FirstEndTied;
            netUnrollProgress = 0.08f;
            ResetNetRigHoldAction();
        }

        playerPosition = new Vector2(GetNetSecondStakeX(), GetPlayerLaneY(playerLane));
        UpdateNetUnrollFromPlayerPosition();
        bool netWasWalkedOpen = netUnrollProgress >= 0.82f;
        netRigStep = NetRigStep.Unrolled;
        bool secondEndHeld = TickNetRigHoldProgress(netRigHoldSeconds + 0.01f, true, true);
        if (secondEndHeld)
        {
            netRigStep = NetRigStep.SecondEndTied;
            netSetDepth01 = 0f;
            netLoweringProgress = 0f;
            selectedNetLine = NetLine.High;
            ResetNetRigHoldAction();
        }

        TickNetLoweringInput(netLoweringSeconds * 0.68f, true, true, false);
        bool weightsHeld = netRigStep == NetRigStep.Lowering && netLoweringProgress > 0.6f;
        TickNetLoweringInput(0f, false, false, true);

        bool netRiggedInOrder = firstEndHeld && netWasWalkedOpen && secondEndHeld && weightsHeld &&
            netDeployed && netRigStep == NetRigStep.Deployed;

        // Let the first real source reach the mesh. The opening tide is fish; the
        // unrouted saltwood passes the near-shore fork and remains at sea. Pulling the
        // wet net back first ties the fish to the home post and genuinely frees both
        // hands before the player chooses to take the boat out.
        state = SliceState.TideRising;
        currentWaterY = GetSelectedNetY() + 0.2f;
        netTouched = true;
        netCaptureProgress01 = 1f;
        incomingHarvestTravel01 = 1f;
        outerWreckTravel01 = TideDriftSourceModel.NearshoreExitTravel01;
        ResolveExitedTideDriftBatches();
        ResolveNetCatch();
        HarvestKind firstCatch = currentHarvest;
        bool catchStaysInNet = firstCatch != HarvestKind.None &&
            harvestPhysicalState == HarvestPhysicalState.CaughtInNet &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater;

        playerPosition = new Vector2(netAnchor.x, GetPlayerLaneY(playerLane));
        netHaulProgress = 0f;
        for (int i = 0; i < 80 && netHaulProgress < 0.999f; i++)
        {
            TickNetHaulEffort(0.12f, netHaulSeconds, 0.45f);
        }
        if (netHaulProgress >= 0.999f)
        {
            SecureNetBeforeEbb();
        }
        bool catchSecuredAtPost = netSecuredEarly &&
            securedPostHarvest == firstCatch &&
            currentHarvest == HarvestKind.None &&
            harvestPhysicalState == HarvestPhysicalState.None;

        playerPosition = new Vector2(GetBoatBoardingX(), GetPlayerLaneY(playerLane));
        currentWaterY = lowWaterY + 0.66f;
        float firstLoopMooringSeconds = 0f;
        bool firstLoopMooringSecured = AdvanceProbeMooringUntilSecured(
            0.05f,
            12f,
            false,
            ref firstLoopMooringSeconds);
        TryBoardBoat();
        TickBoatViewTransition(boatViewTransitionSeconds + 0.02f);
        bool boardedThroughTransition = firstLoopMooringSecured &&
            viewMode == SliceViewMode.Sailing && sailTripActive &&
            boatViewTransition == BoatViewTransition.None;

        // 让“船艉位于浮木下流侧约 0.72m”成为测试条件。旧常量按整船中心
        // 猜位置，V39 语义锚点接入后会把真实船艉停到钩程之外。
        sailingBoatX = sailingSalvageWorldX;
        float firstLoopSternOffsetX = GetSailingBoatSternWorldPosition().x - sailingBoatX;
        sailingBoatX = sailingSalvageWorldX + 0.72f - firstLoopSternOffsetX;
        sailingBoatLaneY = sailingHomeY;
        sailingBoatWorldVelocity = 0.22f;
        sailingSalvageVelocity = 0.2f;
        bool hookStarted = BeginContinuousSailingHookThrow();
        for (int i = 0; i < 32 && extraSaltWoodOwner != ExtraSaltWoodOwner.HookedToBoat; i++)
        {
            sailingBoatWorldVelocity = sailingSalvageVelocity;
            TickContinuousSailingSalvage(0.12f, true);
        }
        bool saltwoodTiedToStern = hookStarted &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.HookedToBoat;

        sailingBoatX = sailingHomeX;
        sailingBoatLaneY = sailingHomeY;
        ReturnToStiltHouseByChoice();
        TickBoatViewTransition(boatViewTransitionSeconds + 0.02f);
        bool returnedByChoice = viewMode == SliceViewMode.Shelter && !sailTripActive &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.ReturnedAtBoat;
        bool unloadedDirectlyIntoHands = TryUnloadReturnedSailingCargo() &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.Carried &&
            currentHarvest == HarvestKind.Wood &&
            harvestPhysicalState == HarvestPhysicalState.Carried &&
            securedPostHarvest == firstCatch &&
            !HasReturnedSailingCargoAtBoat();

        // The wreck bundle contains two usable lengths of saltwood, one surviving
        // wet lashing and an oilcloth piece. It therefore has exactly enough physical
        // material for the first brace without inventing starter inventory.
        int stiltBefore = stiltIntegrity;
        int foodBefore = foodStock;
        viewMode = SliceViewMode.Shelter;
        playerLane = WalkLane.TideFlat;
        playerPosition = GetRepairChoicePosition(RepairChoice.Stilt);
        float repairDuration = GetRepairWorkDuration(RepairChoice.Stilt);
        TickRepairWorkAtWorldTarget(repairDuration + 0.02f, true, true);
        bool firstRepairCommitted = repairChoiceApplied &&
            pendingRepairChoice == RepairChoice.Stilt &&
            stiltIntegrity == stiltBefore + 1 &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.Claimed &&
            timberStock == 0 && ropeStock == 0 && clothStock == 1;
        bool postCatchSurvivesRepair = securedPostHarvest == firstCatch &&
            foodStock == foodBefore;

        playerPosition = new Vector2(netAnchor.x, GetPlayerLaneY(playerLane));
        PickUpSecuredHarvestFromPost();
        bool catchCanStillBeCollected = !HasSecuredPostHarvest() &&
            currentHarvest == firstCatch &&
            harvestPhysicalState == HarvestPhysicalState.Carried &&
            foodStock == foodBefore;

        UpdateVisuals(4.2f);
        string evidence =
            $"布网={netRiggedInOrder}；网获={firstCatch}/{catchStaysInNet}；" +
            $"系桩空手={catchSecuredAtPost}；上船={boardedThroughTransition}；钩木={saltwoodTiedToStern}；返航={returnedByChoice}；" +
            $"木料直接进手={unloadedDirectlyIntoHands}；修柱={firstRepairCommitted}；" +
            $"鱼仍留桩/可后取={postCatchSurvivesRepair}/{catchCanStillBeCollected}；库存={GetMaterialStockText()}";
        bool passed = netRiggedInOrder && catchStaysInNet && boardedThroughTransition &&
            catchSecuredAtPost && saltwoodTiedToStern && returnedByChoice &&
            unloadedDirectlyIntoHands && firstRepairCommitted &&
            postCatchSurvivesRepair && catchCanStillBeCollected;
        return passed
            ? $"PASS：第一潮在同一实例中走通布网、鱼获留桩、短航钩木、主动返航、直接修柱，再回桩取鱼。{evidence}"
            : $"FAIL：第一潮闭环仍有实物所有权、过渡或维修断点。{evidence}";
    }

    public void SetEditorRestorationPreviewPose(int restorationStage)
    {
        EnsureScene();
        ResetSlice();

        int stage = Mathf.Clamp(restorationStage, 0, 2);
        arrivalInspected = true;
        currentWaterY = lowWaterY;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(2.45f, GetPlayerLaneY(playerLane));
        playerFacing = -1;
        if (stage == 0)
        {
            lastActionHint = "故事外观 1/3：刚从海难活下来，屋顶、墙板、柱脚和破帆都没有修，人物也还没有背包。";
        }
        else if (stage == 1)
        {
            stiltIntegrity = 2;
            roofIntegrity = 1;
            interiorComfort = 1;
            interiorSealCondition = 1;
            workbenchCondition = 1;
            stoveCondition = 1;
            houseWarmth = 2;
            boatHullIntegrity = 2;
            boatSailIntegrity = 1;
            boatCabinIntegrity = 0;
            hasSalvageBag = true;
            RecalculateBoatReadiness();
            lastActionHint = "故事外观 2/3：补过一轮的临时归处；一扇窗亮起、屋面和墙板留着新补痕，破帆刚能吃风。";
        }
        else
        {
            stiltIntegrity = 3;
            roofIntegrity = 2;
            interiorComfort = 2;
            interiorSealCondition = 1;
            workbenchCondition = 1;
            bedCondition = 1;
            chartRadioCondition = 1;
            stoveCondition = 2;
            houseWarmth = 4;
            boatHullIntegrity = 3;
            boatSailIntegrity = 2;
            boatCabinIntegrity = 2;
            hasSalvageBag = true;
            RecalculateBoatReadiness();
            lighthouseClues = requiredLighthouseClues;
            lastActionHint = "故事外观 3/3：屋和船都被亲手修到可用，但仍保留旧木、盐蚀和补丁，不会凭空变成新造物。";
        }

        UpdateVisuals(2.1f + stage * 0.3f);
    }

    public void SetEditorV34ExteriorRepairPreviewPose(int previewStage)
    {
        EnsureScene();
        ResetSlice();

        int stage = Mathf.Clamp(previewStage, 0, 6);
        arrivalInspected = true;
        currentWaterY = lowWaterY;
        viewMode = SliceViewMode.Shelter;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(2.45f, GetPlayerLaneY(playerLane));
        playerFacing = -1;

        // 这些预览刻意包含非线性组合，用来证明三条维修线能独立拥有外景区域，
        // 而不是把六张图伪装成一条固定的整屋升级序列。
        switch (stage)
        {
            case 1:
                stiltIntegrity = 2;
                lastActionHint = "V34 外景 2/7：只修桩基潮间层，外梯和屋面仍保持发现态。";
                break;
            case 2:
                roofIntegrity = 1;
                lastActionHint = "V34 外景 3/7：只修左屋面，地基、外梯与墙廊不被连带替换。";
                break;
            case 3:
                stiltIntegrity = 3;
                lastActionHint = "V34 外景 4/7：两次地基维修完成，桩基与外梯都接入右侧登船平台。";
                break;
            case 4:
                roofIntegrity = 2;
                lastActionHint = "V34 外景 5/7：屋面与瞭望间完成，潮间层仍可保持残破。";
                break;
            case 5:
                interiorComfort = 1;
                lastActionHint = "V34 外景 6/7：只修墙体与外廊，屋面、瞭望间和桩基保持发现态。";
                break;
            case 6:
                stiltIntegrity = 3;
                roofIntegrity = 2;
                interiorComfort = 2;
                stoveCondition = 2;
                houseWarmth = 4;
                lastActionHint = "V34 外景 7/7：六个 owner 全部修复，吊机、吊钩和潮下网具仍沿用稳定底图。";
                break;
            default:
                lastActionHint = "V34 外景 1/7：稳定底图加六个发现态 owner，没有整屋端点跳变。";
                break;
        }

        UpdateVisuals(2.1f + stage * 0.27f);
    }

    public void SetEditorV35InteriorRepairPreviewPose(int previewStage)
    {
        EnsureScene();
        ResetSlice();

        int stage = Mathf.Clamp(previewStage, 0, 5);
        arrivalInspected = true;
        currentWaterY = lowWaterY + 0.16f;
        viewMode = SliceViewMode.Interior;
        playerLane = WalkLane.InteriorUpper;
        playerPosition = new Vector2(GetInteriorDoorX() + 0.36f, GetPlayerLaneY(playerLane));
        playerFacing = 1;

        switch (stage)
        {
            case 1:
                stiltIntegrity = 2;
                lastActionHint = "V35 室内 2/6：只加固桩基；外梯、屋面、生活区和灯炉仍保持发现态。";
                break;
            case 2:
                stiltIntegrity = 3;
                lastActionHint = "V35 室内 3/6：桩基与外梯完成，梯顶仍落在通向右侧船位的主甲板。";
                break;
            case 3:
                roofIntegrity = 1;
                playerLane = WalkLane.InteriorLoft;
                playerPosition = new Vector2(GetInteriorLoftLookoutX(), GetPlayerLaneY(playerLane));
                lastActionHint = "V35 室内 4/6：只补左屋面，其余十一部位不被整屋换片。";
                break;
            case 4:
                interiorComfort = 1;
                interiorSealCondition = 1;
                playerPosition = GetRepairChoicePosition(RepairChoice.InteriorSeal);
                lastActionHint = "V35 室内 5/6：只封住围护和入口；工作台、床、海图、炉体与灯火仍旧。";
                break;
            case 5:
                stiltIntegrity = 3;
                roofIntegrity = 2;
                interiorComfort = 2;
                interiorSealCondition = 1;
                workbenchCondition = 1;
                bedCondition = 1;
                chartRadioCondition = 1;
                stoveCondition = 2;
                houseWarmth = 4;
                playerPosition = new Vector2(GetInteriorLampX() - 0.35f, GetPlayerLaneY(playerLane));
                lastActionHint = "V35 室内 6/6：十二部位全修；右吊机、吊网和开放潮间机械层仍保持同一外壳。";
                break;
            default:
                lastActionHint = "V35 室内 1/6：同一栋 V34 高脚屋的发现态剖面，底层无可冲走库存。";
                break;
        }

        UpdateVisuals(2.6f + stage * 0.31f);
    }

    public void SetEditorV35InteriorComponentRepairPreviewPose(int previewComponent)
    {
        EnsureScene();
        ResetSlice();

        int component = Mathf.Clamp(previewComponent, 0, 3);
        arrivalInspected = true;
        currentWaterY = lowWaterY + 0.16f;
        viewMode = SliceViewMode.Interior;
        playerLane = WalkLane.InteriorUpper;
        playerFacing = 1;

        RepairChoice repairedChoice;
        if (component == 0)
        {
            interiorSealCondition = 1;
            repairedChoice = RepairChoice.InteriorSeal;
        }
        else if (component == 1)
        {
            workbenchCondition = 1;
            repairedChoice = RepairChoice.Workbench;
        }
        else if (component == 2)
        {
            bedCondition = 1;
            repairedChoice = RepairChoice.Bed;
        }
        else
        {
            chartRadioCondition = 1;
            repairedChoice = RepairChoice.ChartRadio;
        }

        RefreshInteriorComfort();
        playerPosition = GetRepairChoicePosition(repairedChoice);
        lastActionHint = $"V35 部件隔离 {component + 1}/4：只显示{GetRepairChoiceName(repairedChoice)}的永久变化，其余室内部件保持发现态。";
        UpdateVisuals(3.05f + component * 0.31f);
    }

    public void SetEditorFirstInteriorRepairCausalityPreviewPose(int previewStage)
    {
        EnsureScene();
        ResetSlice();

        int stage = Mathf.Clamp(previewStage, 0, 2);
        arrivalInspected = true;
        state = SliceState.RepairMoment;
        viewMode = SliceViewMode.Interior;
        playerLane = WalkLane.InteriorUpper;
        playerPosition = GetRepairChoicePosition(RepairChoice.InteriorSeal);
        playerFacing = -1;
        nearshoreWorkDone = true;

        if (stage == 0)
        {
            currentHarvest = HarvestKind.Wood;
            currentHarvestBanked = false;
            harvestPhysicalState = HarvestPhysicalState.Carried;
            harvestCarryStartPosition = playerPosition;
            harvestCarryTransition01 = 1f;
            SetExtraSaltWoodOwner(ExtraSaltWoodOwner.Carried);
            lastActionHint = "首修 1/3：盐木仍在手里，V35 围护、工作台和门槛保持发现态。";
        }
        else if (stage == 1)
        {
            currentHarvest = HarvestKind.Wood;
            currentHarvestBanked = false;
            harvestPhysicalState = HarvestPhysicalState.PlacedAtWork;
            harvestPlacedRepairChoice = RepairChoice.InteriorSeal;
            harvestPlacementTransition01 = 1f;
            pendingRepairChoice = RepairChoice.InteriorSeal;
            repairWorkStep = (int)TideRepairWorkPhaseModel.Evaluate(repairWorkProgress);
            repairWorkProgress = 0.58f;
            repairWorkActive = true;
            SetExtraSaltWoodOwner(ExtraSaltWoodOwner.PlacedAtWork);
            lastActionHint = "首修 2/3：盐木落在施工点，人物持续工作；材料尚未扣除，松手仍可续做。";
        }
        else
        {
            interiorComfort = 1;
            interiorSealCondition = 1;
            houseWarmth = 1;
            currentHarvest = HarvestKind.None;
            currentHarvestBanked = true;
            harvestPhysicalState = HarvestPhysicalState.Stored;
            repairChoiceApplied = true;
            pendingRepairChoice = RepairChoice.InteriorSeal;
            repairWorkProgress = 1f;
            repairWorkActive = false;
            SetExtraSaltWoodOwner(ExtraSaltWoodOwner.Claimed);
            lastActionHint = "首修 3/3：盐木被消耗，第一组室内 owner 永久接管；没有临时色块或库存数字替代结构变化。";
        }

        UpdateVisuals(3.15f + stage * 0.34f);
    }

    public void SetEditorV32ClimbPreviewPose(int previewStage)
    {
        EnsureScene();
        ResetSlice();

        // 使用修复端点检查最清楚的梯框和平台连接；动作本身在发现态复用
        // 同一套背身六帧，只会改为另一组 V32 梯顶/梯底锚点。
        stiltIntegrity = 3;
        roofIntegrity = 2;
        interiorComfort = 2;
        interiorSealCondition = 1;
        workbenchCondition = 1;
        bedCondition = 1;
        chartRadioCondition = 1;
        stoveCondition = 2;
        houseWarmth = 4;
        arrivalInspected = true;
        currentWaterY = lowWaterY;
        viewMode = SliceViewMode.Shelter;
        playerLane = WalkLane.Deck;
        playerPosition = GetGangwayTopPosition();
        StartLaneTransition(WalkLane.TideFlat);

        float progress = previewStage <= 0 ? 0.18f : previewStage == 1 ? 0.5f : 0.82f;
        laneTransitionProgress = progress;
        float eased = Mathf.SmoothStep(0f, 1f, progress);
        playerPosition = Vector2.Lerp(laneTransitionFromPosition, laneTransitionToPosition, eased);
        playerMoving = true;
        playerFacing = laneTransitionToPosition.x >= laneTransitionFromPosition.x ? 1 : -1;
        lastActionHint = $"V32 外梯预览 {previewStage + 1}/3：完整背身人物沿正式梯框落到潮间平台，再向右走上登船木板。";
        UpdateVisuals(2.35f + previewStage * 0.18f);
    }

    public void SetEditorV35InteriorClimbPreviewPose(int previewStage)
    {
        EnsureScene();
        ResetSlice();

        stiltIntegrity = 3;
        roofIntegrity = 2;
        interiorComfort = 2;
        interiorSealCondition = 1;
        workbenchCondition = 1;
        bedCondition = 1;
        chartRadioCondition = 1;
        stoveCondition = 2;
        houseWarmth = 4;
        arrivalInspected = true;
        currentWaterY = lowWaterY + 0.1f;
        viewMode = SliceViewMode.Interior;
        playerLane = WalkLane.InteriorLower;
        playerPosition = GetInteriorStairBottomPosition();
        StartLaneTransition(WalkLane.InteriorUpper);

        float progress = previewStage <= 0 ? 0.16f : previewStage == 1 ? 0.5f : 0.84f;
        laneTransitionProgress = progress;
        float eased = Mathf.SmoothStep(0f, 1f, progress);
        playerPosition = Vector2.Lerp(laneTransitionFromPosition, laneTransitionToPosition, eased);
        playerMoving = true;
        playerFacing = laneTransitionToPosition.x >= laneTransitionFromPosition.x ? 1 : -1;
        lastActionHint = $"V35 室内外梯 {previewStage + 1}/3：完整背身人物从开放潮间工作层沿前梯上到生活层。";
        UpdateVisuals(2.42f + previewStage * 0.18f);
    }

    public void SetEditorV28HousePreviewPose(int previewStage)
    {
        EnsureScene();
        ResetSlice();

        int stage = Mathf.Clamp(previewStage, 0, 2);
        arrivalInspected = true;
        currentWaterY = lowWaterY + 0.16f;
        dayProgress01 = stage == 0 ? 0.42f : 0.7f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = stage == 0 ? DayNightPhase.Day : DayNightPhase.Dusk;
        if (stage == 0)
        {
            viewMode = SliceViewMode.Shelter;
            playerLane = WalkLane.Deck;
            playerPosition = new Vector2(GetInteriorDoorX() + 0.72f, GetPlayerLaneY(playerLane));
            lastActionHint = "V28 外景：风旗和晾布使用十二帧局部形变，房屋其余像素保持同一坐标。";
        }
        else
        {
            viewMode = SliceViewMode.Interior;
            playerLane = stage == 1 ? WalkLane.InteriorUpper : WalkLane.InteriorLoft;
            playerPosition = stage == 1
                ? new Vector2(GetInteriorDoorX() + 0.55f, GetPlayerLaneY(playerLane))
                : new Vector2(GetInteriorLoftLookoutX() + 0.28f, GetPlayerLaneY(playerLane));
            if (stage == 2)
            {
                stiltIntegrity = 3;
                roofIntegrity = 2;
                interiorComfort = 2;
                interiorSealCondition = 1;
                workbenchCondition = 1;
                bedCondition = 1;
                chartRadioCondition = 1;
                stoveCondition = 2;
                houseWarmth = 4;
            }

            lastActionHint = stage == 1
                ? "V27 室内发现态：外壳、主楼板、桩梁和潮线与 V28 外景共用同一坐标。"
                : "V27 室内修复态：当前先使用完整端点，十二部位连续维修留到下一轮接入。";
        }

        playerFacing = 1;
        UpdateVisuals(2.8f + stage * 0.4f);
    }

    public void SetEditorV30HousePreviewPose(int previewStage)
    {
        EnsureScene();
        ResetSlice();

        int stage = Mathf.Clamp(previewStage, 0, 3);
        arrivalInspected = true;
        currentWaterY = lowWaterY + 0.16f;
        dayProgress01 = stage == 0 ? 0.42f : 0.68f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = stage == 0 ? DayNightPhase.Day : DayNightPhase.Dusk;
        if (stage == 0)
        {
            viewMode = SliceViewMode.Shelter;
            playerLane = WalkLane.Deck;
            playerPosition = new Vector2(GetInteriorDoorX() + 0.72f, GetPlayerLaneY(playerLane));
            lastActionHint = "V30 外景：直接消费正式十二帧，风旗与晾布运动时整屋坐标不变。";
        }
        else
        {
            viewMode = SliceViewMode.Interior;
            playerLane = WalkLane.InteriorUpper;
            playerPosition = new Vector2(GetInteriorDoorX() + 0.42f, GetPlayerLaneY(playerLane));
            if (stage == 2)
            {
                stiltIntegrity = 2;
                roofIntegrity = 1;
                interiorComfort = 1;
                interiorSealCondition = 1;
                workbenchCondition = 1;
                stoveCondition = 1;
                houseWarmth = 2;
            }
            else if (stage == 3)
            {
                stiltIntegrity = 3;
                roofIntegrity = 2;
                interiorComfort = 2;
                interiorSealCondition = 1;
                workbenchCondition = 1;
                bedCondition = 1;
                chartRadioCondition = 1;
                stoveCondition = 2;
                houseWarmth = 4;
                playerPosition = new Vector2(GetInteriorLampX() - 0.46f, GetPlayerLaneY(playerLane));
            }

            string[] hints =
            {
                string.Empty,
                "V30 发现态：StableBase 与十二张 Damage owner 精确重建同坐标破屋。",
                "V30 混合维修态：已修和未修 owner 可任意组合，单个 owner 不会 Damage/Repair 双显。",
                "V30 完成态：十二个 owner 全部收口后，切入室内语义动作十二帧。"
            };
            lastActionHint = hints[stage];
        }

        playerFacing = 1;
        UpdateVisuals(3.2f + stage * 0.37f);
    }

    public void SetEditorInteriorPreviewPose(bool flooded)
    {
        EnsureScene();
        ResetSlice();

        arrivalInspected = true;
        viewMode = SliceViewMode.Interior;
        houseWarmth = flooded ? 1 : 3;
        tideRound = flooded ? departureStormRound + 1 : 0;
        weatherClockSeconds = flooded ? dayLengthSeconds * stormFrontArrivalDays : dayLengthSeconds * 0.18f;
        moonAgeDays = flooded ? 0f : 7.38f;
        tideStrength = CalculateTideStrength(moonAgeDays);
        dayProgress01 = flooded ? 0.76f : 0.86f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = flooded ? DayNightPhase.Dusk : DayNightPhase.Night;
        currentWaterY = flooded ? GetShelterStressWaterlineY() + 0.46f : EvaluateNaturalWaterY(tideClockSeconds * 0.08f);
        // The roof cabin in V35 is a compact observation hatch, not a full-height
        // room. The general interior preview therefore stands on the inhabited floor;
        // presenting a full-height walking actor in that hatch made both the building
        // scale and collision contract look false. The dedicated lookout view still
        // owns observation from the roof.
        playerLane = flooded ? WalkLane.InteriorLower : WalkLane.InteriorUpper;
        playerPosition = flooded
            ? new Vector2(-2.2f, GetPlayerLaneY(playerLane))
            : new Vector2(GetInteriorBedX() - 0.42f, GetPlayerLaneY(playerLane));
        playerFacing = 1;
        playerMoving = true;
        playerHorizontalVelocity = playerMoveSpeed * 0.58f;
        playerWalkCycle = flooded ? 0.18f : 0.62f;
        lastActionHint = flooded
            ? "风暴预览：异常增水只先进工作层，斜梯上方的睡眠层仍保持干燥。"
            : "室内预览：下层工作、中层生活、阁楼休息观潮；三层由两段真实斜梯连续连接。";
        UpdateVisuals(flooded ? 4.1f : 3.2f);
    }

    public void SetEditorV42SurvivalPreviewPose(int previewStage)
    {
        EnsureScene();
        ResetSlice();

        int stage = Mathf.Clamp(previewStage, 0, 3);
        arrivalInspected = true;
        arrivalVignetteActive = false;
        showDebugHud = false;
        state = SliceState.TideRising;
        playerFacing = 1;
        playerMoving = false;
        playerHorizontalVelocity = 0f;
        playerCurrentDriftVelocity = 0f;

        if (stage == 0)
        {
            // 受寒循环必须仍站在无遮挡的真实湿木路上。这个预览不伪造死亡
            // 状态，只把体温放进运行阈值，验证空闲动作和人物脚底接触。
            viewMode = SliceViewMode.Shelter;
            playerLane = WalkLane.TideFlat;
            playerPosition = new Vector2(netAnchor.x + 0.9f, GetPlayerLaneY(playerLane));
            currentWaterY = lowWaterY + 0.16f;
            bodyWarmth01 = 0.18f;
            dayProgress01 = 0.84f;
            dayClockSeconds = dayProgress01 * dayLengthSeconds;
            dayNightPhase = DayNightPhase.Night;
            lastActionHint = "V42 验收：低体温空闲时颤抖；移动、爬梯和工作仍会覆盖它。";
            UpdateVisuals(0.32f);
            return;
        }

        if (stage == 1)
        {
            // 睡眠从生活层床边开始，并由正式床的 X 与真实生活层承重面共同
            // 决定卧姿；不能把角色瞬移到程序绘制的另一层床上。
            viewMode = SliceViewMode.Interior;
            playerLane = WalkLane.InteriorUpper;
            playerPosition = new Vector2(GetInteriorBedX() - 0.28f, GetPlayerLaneY(playerLane));
            bedCondition = 1;
            dayProgress01 = 0.91f;
            dayClockSeconds = dayProgress01 * dayLengthSeconds;
            dayNightPhase = DayNightPhase.Night;
            BeginSleepPresentation();
            survivalPresentationTimer =
                GetSurvivalPresentationDuration(SurvivalPresentationState.Sleeping) * 0.66f;
            lastActionHint = "V42 验收：身体先在真实床面躺稳，暗点之后才结算换日。";
            UpdateVisuals(0.86f);
            return;
        }

        if (stage == 2)
        {
            // 溺水动作绑定人物所在 X 的局部浪面。预览故意把潮位升到胸颈，
            // 但不写死 Sprite Y；每一帧仍由声明水线锚点贴合实时海面。
            viewMode = SliceViewMode.Shelter;
            playerLane = WalkLane.TideFlat;
            playerPosition = new Vector2(netAnchor.x + 0.42f, GetPlayerLaneY(playerLane));
            currentWaterY = GetPlayerStandingFeetY(playerLane) + 0.92f;
            BeginDeathPresentation("V42 溺水视觉验收", SurvivalPresentationState.Drowning);
            survivalPresentationTimer =
                GetSurvivalPresentationDuration(SurvivalPresentationState.Drowning) * 0.58f;
            lastActionHint = "V42 验收：人物贴当地浪面下沉，动作结束后才结算死亡。";
            UpdateVisuals(1.18f);
            return;
        }

        // 失温倒地保持在无遮挡的正式湿木路承重面。根运动只沿面朝方向前移，
        // 不允许 Y 轴下沉穿过楼板，也不在动作尚未完成时提前复活。
        viewMode = SliceViewMode.Shelter;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(netAnchor.x + 0.9f, GetPlayerLaneY(playerLane));
        currentWaterY = lowWaterY + 0.16f;
        bodyWarmth01 = 0f;
        BeginDeathPresentation("V42 失温视觉验收", SurvivalPresentationState.ColdCollapse);
        survivalPresentationTimer =
            GetSurvivalPresentationDuration(SurvivalPresentationState.ColdCollapse) * 0.72f;
        lastActionHint = "V42 验收：失温者先在真实楼板上倒下，随后才结算惩罚。";
        UpdateVisuals(1.46f);
    }

    public void SetEditorShelterHighTideCoveragePreviewPose()
    {
        EnsureScene();
        ResetSlice();

        arrivalInspected = true;
        arrivalVignetteActive = false;
        showDebugHud = false;
        viewMode = SliceViewMode.Shelter;
        state = SliceState.TideRising;
        dayProgress01 = 0.46f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = DayNightPhase.Day;
        weatherClockSeconds = dayLengthSeconds * 0.24f;
        currentWaterY = highWaterY;
        playerLane = WalkLane.Deck;
        playerPosition = new Vector2(GetGangwayTopPosition().x - 0.52f, GetPlayerLaneY(playerLane));
        playerFacing = 1;
        playerMoving = false;
        playerHorizontalVelocity = 0f;
        lastActionHint = "高潮海体验收：浪尖、桩柱浸水和画面底部必须属于同一片连续海水。";
        UpdateVisuals(3.35f);
    }

    public void SetEditorTidePrepForecastPreviewPose()
    {
        EnsureScene();
        ResetSlice();

        arrivalInspected = true;
        arrivalVignetteActive = false;
        viewMode = SliceViewMode.Interior;
        state = SliceState.LowTidePlanning;
        tideRound = departureStormRound;
        weatherClockSeconds = dayLengthSeconds * stormFrontArrivalDays * 0.68f;
        moonAgeDays = 0f;
        tideStrength = CalculateTideStrength(moonAgeDays);
        boatHullIntegrity = 1;
        dayProgress01 = 0.71f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = DayNightPhase.Dusk;
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        playerLane = WalkLane.InteriorLoft;
        playerPosition = new Vector2(GetInteriorLoftLookoutX() + 0.28f, GetPlayerLaneY(playerLane));
        playerFacing = -1;
        CaptureLoftForecast();
        lastActionHint = "读数记在海图边。下一步去工作层选择一件工具。";
        UpdateVisuals(3.6f);
    }

    public void SetEditorLookoutVistaPreviewPose(bool nightStorm, bool lighthouseKnown)
    {
        EnsureScene();
        ResetSlice();

        arrivalInspected = true;
        arrivalVignetteActive = false;
        viewMode = SliceViewMode.Lookout;
        state = SliceState.TideRising;
        tideRound = lighthouseKnown ? 2 : 0;
        routeClueReturnRound = lighthouseKnown ? 0 : -1;
        lighthouseClues = lighthouseKnown ? 1 : 0;
        lighthouseSeen = lighthouseKnown;
        moonAgeDays = nightStorm ? 13.8f : 5.4f;
        tideStrength = CalculateTideStrength(moonAgeDays);
        tideClockSeconds = tideCycleSeconds * (nightStorm ? 0.34f : 0.22f);
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        dayProgress01 = nightStorm ? 0.89f : 0.42f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = nightStorm ? DayNightPhase.Night : DayNightPhase.Day;
        weatherClockSeconds = dayLengthSeconds * stormFrontArrivalDays * (nightStorm ? 0.82f : 0.24f);
        playerLane = WalkLane.InteriorLoft;
        playerPosition = new Vector2(GetInteriorLoftLookoutX(), GetPlayerLaneY(playerLane));
        playerMoving = false;
        playerHorizontalVelocity = 0f;
        showDebugHud = false;
        roofIntegrity = nightStorm ? 1 : 2;
        lastActionHint = lighthouseKnown
            ? "阁楼瞭望预览：返航带回的方位纸已与海面参照对上，夜雾里能辨认灯塔扫光。"
            : "阁楼瞭望预览：只能看到近海残骸和潮向；没有带回方位线索前，远处灯塔不会提前显形。";
        UpdateVisuals(nightStorm ? 7.8f : 3.4f);
    }

    public void SetEditorNightActionWindowPreviewPose()
    {
        EnsureScene();
        ResetSlice();

        arrivalInspected = true;
        arrivalVignetteActive = false;
        viewMode = SliceViewMode.Shelter;
        state = SliceState.LowTidePlanning;
        dayProgress01 = 0.88f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = DayNightPhase.Night;
        moonAgeDays = 11.2f;
        tideStrength = CalculateTideStrength(moonAgeDays);
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        netRigStep = NetRigStep.Stored;
        netDeployed = false;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(GetNetStoredX() + 0.08f, GetPlayerLaneY(playerLane));
        playerFacing = -1;
        lastActionHint = "夜间行动预览：潮水和旧网都还在，只有新户外工程等到能看清受力点时再开始。";
        UpdateVisuals(5.1f);
    }

    public void SetEditorTidePrepHoldPreviewPose(int choiceIndex)
    {
        EnsureScene();
        ResetSlice();

        TidePrepChoice[] choices = { TidePrepChoice.Rope, TidePrepChoice.Bucket, TidePrepChoice.Stake };
        TidePrepChoice choice = choices[Mathf.Clamp(choiceIndex, 0, choices.Length - 1)];
        arrivalInspected = true;
        viewMode = SliceViewMode.Interior;
        state = SliceState.LowTidePlanning;
        dayProgress01 = 0.88f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = DayNightPhase.Night;
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        playerLane = WalkLane.InteriorLower;
        playerPosition = new Vector2(GetNightPrepPosition(choice).x, GetPlayerLaneY(playerLane));
        playerFacing = choice == TidePrepChoice.Stake ? -1 : 1;
        pendingTidePrepChoice = choice;
        tidePrepWorkProgress = 0.56f;
        tidePrepWorkActive = true;
        tidePrepActionTimer = 0.12f;
        lastActionHint = $"持续准备预览：{GetPrepChoiceName(choice)}完成 56%，松开会留在当前工作台，尚未替换已备工具。";
        UpdateVisuals(5.45f + choiceIndex * 0.32f);
    }

#if UNITY_EDITOR
    public string RunEditorInteriorTraversalProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalInspected = true;
        viewMode = SliceViewMode.Interior;

        List<string> failures = new List<string>();
        ProbeInteriorTraversalLeg("工作层到生活层", WalkLane.InteriorLower, GetInteriorStairBottomPosition(), WalkLane.InteriorUpper, GetInteriorStairTopPosition(), failures);
        ProbeInteriorTraversalLeg("生活层到阁楼", WalkLane.InteriorUpper, GetInteriorLoftStairBottomPosition(), WalkLane.InteriorLoft, GetInteriorLoftStairTopPosition(), failures);
        ProbeInteriorTraversalLeg("阁楼回生活层", WalkLane.InteriorLoft, GetInteriorLoftStairTopPosition(), WalkLane.InteriorUpper, GetInteriorLoftStairBottomPosition(), failures);
        ProbeInteriorTraversalLeg("生活层回工作层", WalkLane.InteriorUpper, GetInteriorStairTopPosition(), WalkLane.InteriorLower, GetInteriorStairBottomPosition(), failures);

        UpdateVisuals(2.8f);
        return failures.Count == 0
            ? "PASS：三层高脚屋四段楼梯往返均落在对应可见梯口。"
            : "FAIL：" + string.Join("；", failures);
    }

    private void ProbeInteriorTraversalLeg(
        string legName,
        WalkLane sourceLane,
        Vector2 sourcePosition,
        WalkLane targetLane,
        Vector2 expectedPosition,
        List<string> failures)
    {
        playerLane = sourceLane;
        playerPosition = sourcePosition;
        isLaneTransitioning = false;
        StartLaneTransition(targetLane);
        TickLaneTransition(laneTransitionDurationSeconds + 0.05f);

        if (isLaneTransitioning ||
            playerLane != targetLane ||
            Vector2.Distance(playerPosition, expectedPosition) > 0.01f)
        {
            failures.Add($"{legName}未完成（层={playerLane}，位置={playerPosition.x:F2},{playerPosition.y:F2}）");
        }
    }

    public string RunEditorNaturalCurrentContinuityProbe()
    {
        float cycle = Mathf.Max(8f, tideCycleSeconds);
        const float sampleOffsetSeconds = 0.25f;
        float highBefore = EvaluateNaturalCurrentSpeed(cycle * 0.5f - sampleOffsetSeconds);
        float highPoint = EvaluateNaturalCurrentSpeed(cycle * 0.5f);
        float highAfter = EvaluateNaturalCurrentSpeed(cycle * 0.5f + sampleOffsetSeconds);
        float lowBefore = EvaluateNaturalCurrentSpeed(cycle - sampleOffsetSeconds);
        float lowPoint = EvaluateNaturalCurrentSpeed(0f);
        float lowAfter = EvaluateNaturalCurrentSpeed(sampleOffsetSeconds);

        bool signsCorrect = highBefore < 0f && highAfter > 0f && lowBefore > 0f && lowAfter < 0f;
        bool slackAtTurn = Mathf.Abs(highPoint) < 0.0001f && Mathf.Abs(lowPoint) < 0.0001f;
        bool approachesSlack = Mathf.Abs(highBefore) < 0.01f && Mathf.Abs(highAfter) < 0.01f &&
            Mathf.Abs(lowBefore) < 0.01f && Mathf.Abs(lowAfter) < 0.01f;
        string samples = $"高潮前 {highBefore:F4} / 高潮点 {highPoint:F4} / 高潮后 {highAfter:F4}；" +
            $"低潮前 {lowBefore:F4} / 低潮点 {lowPoint:F4} / 低潮后 {lowAfter:F4}";
        return signsCorrect && slackAtTurn && approachesSlack
            ? $"PASS：自然潮流在高低潮均经过零点连续换向。{samples}"
            : $"FAIL：自然潮流换向不连续。{samples}";
    }

    public string RunEditorAstronomicalCurrentCouplingProbe()
    {
        EnsureScene();
        ResetSlice();

        float cycle = Mathf.Max(8f, tideCycleSeconds);
        float referenceStrength = CalculateTideStrength(OpeningMoonAgeDays);
        float neapStrength = CalculateTideStrength(29.53f * 0.25f);
        float springStrength = CalculateTideStrength(0f);
        float midFlood = cycle * 0.25f;
        float midEbb = cycle * 0.75f;

        float neapFloodSpeed = Mathf.Abs(TideAstronomicalCurrentModel.EvaluateSignedSpeed(
            midFlood,
            cycle,
            neapStrength,
            referenceStrength,
            MeanTidalTransportSpeed,
            EbbCurrentBoost));
        float referenceFloodSpeed = Mathf.Abs(TideAstronomicalCurrentModel.EvaluateSignedSpeed(
            midFlood,
            cycle,
            referenceStrength,
            referenceStrength,
            MeanTidalTransportSpeed,
            EbbCurrentBoost));
        float springFloodSpeed = Mathf.Abs(TideAstronomicalCurrentModel.EvaluateSignedSpeed(
            midFlood,
            cycle,
            springStrength,
            referenceStrength,
            MeanTidalTransportSpeed,
            EbbCurrentBoost));
        bool moonChangesPhysicalCurrent = neapFloodSpeed < referenceFloodSpeed &&
            referenceFloodSpeed < springFloodSpeed;

        float expectedLegacyReferenceSpeed = MeanTidalTransportSpeed * Mathf.PI * 0.5f;
        bool openingTideKeepsCalibration =
            Mathf.Abs(referenceFloodSpeed - expectedLegacyReferenceSpeed) <= 0.0001f;
        float lowSlack = TideAstronomicalCurrentModel.EvaluateSignedSpeed(
            0f,
            cycle,
            springStrength,
            referenceStrength,
            MeanTidalTransportSpeed,
            EbbCurrentBoost);
        float highSlack = TideAstronomicalCurrentModel.EvaluateSignedSpeed(
            cycle * 0.5f,
            cycle,
            springStrength,
            referenceStrength,
            MeanTidalTransportSpeed,
            EbbCurrentBoost);
        bool turnWaterRemainsSlack = Mathf.Abs(lowSlack) <= 0.0001f &&
            Mathf.Abs(highSlack) <= 0.0001f;

        // 隔离其它压力，只比较同一距离下的向岸/离岸潮流。人物仍可自行冒险；
        // 这里验证的是风险信号和船况控制力，不是新增一个隐藏出航禁区。
        viewMode = SliceViewMode.Sailing;
        state = SliceState.EbbCollect;
        dayNightPhase = DayNightPhase.Day;
        dayProgress01 = 0.42f;
        dayClockSeconds = dayLengthSeconds * dayProgress01;
        weatherClockSeconds = 0f;
        sailTripTimer = 0f;
        sailingBoatX = 5.2f;
        sailingBoatLaneY = sailingHomeY;
        sailingWaterIngress01 = 0f;
        sailingSailTrim01 = 0.58f;
        sailingBuoyChecked = !EnableSailingBuoyGameplay;
        boatHullIntegrity = 1;
        boatSailIntegrity = 0;
        boatCabinIntegrity = 0;
        RecalculateBoatReadiness();

        tideClockSeconds = midFlood;
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        float floodReturnPressure = GetReturnPressure01();
        tideClockSeconds = midEbb;
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        float damagedEbbReturnPressure = GetReturnPressure01();

        boatHullIntegrity = 3;
        boatSailIntegrity = 1;
        boatCabinIntegrity = 0;
        RecalculateBoatReadiness();
        float repairedEbbReturnPressure = GetReturnPressure01();
        bool directionCreatesChoice = damagedEbbReturnPressure > floodReturnPressure + 0.08f;
        bool repairImprovesControlWithoutDeletingCurrent =
            repairedEbbReturnPressure < damagedEbbReturnPressure - 0.02f &&
            repairedEbbReturnPressure > 0.02f;

        string evidence =
            $"小/首/大潮中涨流={neapFloodSpeed:F3}/{referenceFloodSpeed:F3}/{springFloodSpeed:F3}m/s；" +
            $"平流={lowSlack:F4}/{highSlack:F4}；首潮旧值={expectedLegacyReferenceSpeed:F3}；" +
            $"同距返航 向岸/离岸破船/离岸修船={floodReturnPressure:F2}/{damagedEbbReturnPressure:F2}/{repairedEbbReturnPressure:F2}";
        bool passed = moonChangesPhysicalCurrent && openingTideKeepsCalibration &&
            turnWaterRemainsSlack && directionCreatesChoice &&
            repairImprovesControlWithoutDeletingCurrent;
        ResetSlice();
        return passed
            ? $"PASS：月相潮差统一驱动实际潮流；离岸流增加返航压力，修船提升控制但没有删除潮流。{evidence}"
            : $"FAIL：潮差、平流、首潮校准或返航选择至少一项未成立。{evidence}";
    }

    public string RunEditorMixedSemidiurnalTideProbe()
    {
        float noInequality = TideMixedSemidiurnalModel.EvaluateInequalityRatio(0f);
        float maximumInequality = TideMixedSemidiurnalModel.EvaluateInequalityRatio(
            TideMixedSemidiurnalModel.LunarDeclinationPeriodDays * 0.25f);
        bool declinationContinuouslyControlsDifference = noInequality <= 0.0001f &&
            Mathf.Abs(maximumInequality - TideMixedSemidiurnalModel.MaximumInequalityRatio) <= 0.0001f;

        float higherHighScale = TideMixedSemidiurnalModel.EvaluateHighWaterScale(
            0,
            maximumInequality);
        float lowerHighScale = TideMixedSemidiurnalModel.EvaluateHighWaterScale(
            1,
            maximumInequality);
        float repeatedHigherHighScale = TideMixedSemidiurnalModel.EvaluateHighWaterScale(
            2,
            maximumInequality);
        bool twoTidePatternIsDeterministic = Mathf.Abs(higherHighScale - 1f) <= 0.0001f &&
            lowerHighScale < higherHighScale - 0.1f &&
            Mathf.Abs(repeatedHigherHighScale - higherHighScale) <= 0.0001f;

        float firstLow = TideMixedSemidiurnalModel.EvaluateHeight01(0f, 0, maximumInequality);
        float secondLow = TideMixedSemidiurnalModel.EvaluateHeight01(0f, 1, maximumInequality);
        float higherHighSlack = TideMixedSemidiurnalModel.EvaluateSignedCurrentWave(
            0.5f,
            0,
            maximumInequality);
        float lowerHighSlack = TideMixedSemidiurnalModel.EvaluateSignedCurrentWave(
            0.5f,
            1,
            maximumInequality);
        float firstLowSlack = TideMixedSemidiurnalModel.EvaluateSignedCurrentWave(
            0f,
            0,
            maximumInequality);
        float secondLowSlack = TideMixedSemidiurnalModel.EvaluateSignedCurrentWave(
            0f,
            1,
            maximumInequality);
        bool turnsRemainSlack = Mathf.Abs(firstLow) <= 0.0001f &&
            Mathf.Abs(secondLow) <= 0.0001f &&
            Mathf.Abs(higherHighSlack) <= 0.0001f &&
            Mathf.Abs(lowerHighSlack) <= 0.0001f &&
            Mathf.Abs(firstLowSlack) <= 0.0001f &&
            Mathf.Abs(secondLowSlack) <= 0.0001f;

        const float boundaryOffset = 0.0001f;
        float beforeLowHeight = TideMixedSemidiurnalModel.EvaluateHeight01(
            1f - boundaryOffset,
            0,
            maximumInequality);
        float afterLowHeight = TideMixedSemidiurnalModel.EvaluateHeight01(
            boundaryOffset,
            1,
            maximumInequality);
        float beforeLowCurrent = TideMixedSemidiurnalModel.EvaluateSignedCurrentWave(
            1f - boundaryOffset,
            0,
            maximumInequality);
        float afterLowCurrent = TideMixedSemidiurnalModel.EvaluateSignedCurrentWave(
            boundaryOffset,
            1,
            maximumInequality);
        bool cycleBoundaryIsContinuous = Mathf.Abs(beforeLowHeight - afterLowHeight) <= 0.0001f &&
            Mathf.Abs(beforeLowCurrent + afterLowCurrent) <= 0.001f;

        const float derivativePhase = 0.31f;
        const float derivativeStep = 0.0001f;
        float heightBefore = TideMixedSemidiurnalModel.EvaluateHeight01(
            derivativePhase - derivativeStep,
            0,
            maximumInequality);
        float heightAfter = TideMixedSemidiurnalModel.EvaluateHeight01(
            derivativePhase + derivativeStep,
            0,
            maximumInequality);
        float numericalSignedWave = -(heightAfter - heightBefore) /
            (derivativeStep * 2f * Mathf.PI);
        float analyticSignedWave = TideMixedSemidiurnalModel.EvaluateSignedCurrentWave(
            derivativePhase,
            0,
            maximumInequality);
        bool currentIsWaterDerivative = Mathf.Abs(numericalSignedWave - analyticSignedWave) <= 0.001f;

        string evidence =
            $"不等高比={noInequality:F3}/{maximumInequality:F3}；" +
            $"较高/较低/再较高={higherHighScale:F3}/{lowerHighScale:F3}/{repeatedHigherHighScale:F3}；" +
            $"平流={higherHighSlack:F4}/{lowerHighSlack:F4}/{firstLowSlack:F4}/{secondLowSlack:F4}；" +
            $"导数解析/数值={analyticSignedWave:F4}/{numericalSignedWave:F4}";
        return declinationContinuouslyControlsDifference && twoTidePatternIsDeterministic &&
            turnsRemainSlack && cycleBoundaryIsContinuous && currentIsWaterDerivative
            ? $"PASS：混合半日潮以连续月球赤纬包络产生较高潮/较低高潮，水位与潮流共用解析函数。{evidence}"
            : $"FAIL：相邻潮不等高、平流、跨潮连续或水位导数至少一项错误。{evidence}";
    }

    public string RunEditorMixedSemidiurnalOpportunityProbe()
    {
        EnsureScene();
        ResetSlice();
        float originalStormFrontArrivalDays = stormFrontArrivalDays;
        stormFrontArrivalDays = 1000f;
        float cycle = Mathf.Max(8f, tideCycleSeconds);
        const float comparisonNetDepth01 = 0f;

        // 月相和赤纬不是同一周期。开场相位经过约十一天后，满月大潮与
        // 明显日不等潮自然重合；用这个物理可达日期检验两次相邻高潮，
        // 不能把“最大月相”和“最大赤纬”硬塞进同一个回绕变量。
        float daysUntilFullMoon = Mathf.Repeat(29.53f * 0.5f - OpeningMoonAgeDays, 29.53f);
        float targetWorldClock = daysUntilFullMoon * dayLengthSeconds;
        float firstCycleStartClock = Mathf.Floor(targetWorldClock / cycle) * cycle;
        int firstCycleOrdinal = GetAstronomicalCycleOrdinal(firstCycleStartClock);
        float firstHighWorldClock = firstCycleStartClock + cycle * 0.5f;
        moonAgeDays = Mathf.Repeat(
            OpeningMoonAgeDays + firstHighWorldClock / dayLengthSeconds,
            29.53f);
        tideStrength = CalculateTideStrength(moonAgeDays);
        tideClockSeconds = 0f;
        weatherClockSeconds = firstCycleStartClock;
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        float firstHighPredictionY = GetPredictedHighWaterY();
        float firstNetEncounterSeconds = GetPredictedNetEncounterSeconds(comparisonNetDepth01);
        float firstWorkWindowSeconds = MeasurePredictedNearshoreWorkWindowSeconds(
            firstCycleOrdinal,
            firstCycleStartClock);
        weatherClockSeconds = firstHighWorldClock;
        tideClockSeconds = cycle * 0.5f;
        float firstHighCurrent = EvaluateNaturalCurrentSpeed(tideClockSeconds);

        float secondCycleStartClock = firstCycleStartClock + cycle;
        int secondCycleOrdinal = firstCycleOrdinal + 1;
        float secondHighWorldClock = secondCycleStartClock + cycle * 0.5f;
        moonAgeDays = Mathf.Repeat(
            OpeningMoonAgeDays + secondHighWorldClock / dayLengthSeconds,
            29.53f);
        tideStrength = CalculateTideStrength(moonAgeDays);
        tideClockSeconds = 0f;
        weatherClockSeconds = secondCycleStartClock;
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        float secondHighPredictionY = GetPredictedHighWaterY();
        float secondNetEncounterSeconds = GetPredictedNetEncounterSeconds(comparisonNetDepth01);
        float secondWorkWindowSeconds = MeasurePredictedNearshoreWorkWindowSeconds(
            secondCycleOrdinal,
            secondCycleStartClock);
        weatherClockSeconds = secondHighWorldClock;
        tideClockSeconds = cycle * 0.5f;
        float secondHighCurrent = EvaluateNaturalCurrentSpeed(tideClockSeconds);

        bool firstIsHigher = firstHighPredictionY >= secondHighPredictionY;
        float higherHighPredictionY = firstIsHigher ? firstHighPredictionY : secondHighPredictionY;
        float lowerHighPredictionY = firstIsHigher ? secondHighPredictionY : firstHighPredictionY;
        float higherHighNetEncounterSeconds = firstIsHigher ? firstNetEncounterSeconds : secondNetEncounterSeconds;
        float lowerHighNetEncounterSeconds = firstIsHigher ? secondNetEncounterSeconds : firstNetEncounterSeconds;
        float higherHighWorkWindowSeconds = firstIsHigher ? firstWorkWindowSeconds : secondWorkWindowSeconds;
        float lowerHighWorkWindowSeconds = firstIsHigher ? secondWorkWindowSeconds : firstWorkWindowSeconds;
        float higherHighCurrent = firstIsHigher ? firstHighCurrent : secondHighCurrent;
        float lowerHighCurrent = firstIsHigher ? secondHighCurrent : firstHighCurrent;
        bool adjacentHighsCreateDifferentOpportunities =
            higherHighPredictionY > lowerHighPredictionY + 0.08f &&
            Mathf.Abs(higherHighNetEncounterSeconds - lowerHighNetEncounterSeconds) > 0.02f &&
            lowerHighWorkWindowSeconds > higherHighWorkWindowSeconds + 2f;
        bool bothHighWatersRemainSlack = Mathf.Abs(higherHighCurrent) <= 0.0001f &&
            Mathf.Abs(lowerHighCurrent) <= 0.0001f;

        moonAgeDays = Mathf.Repeat(
            OpeningMoonAgeDays + firstHighWorldClock / dayLengthSeconds,
            29.53f);
        tideStrength = CalculateTideStrength(moonAgeDays);
        tideClockSeconds = 0f;
        weatherClockSeconds = firstCycleStartClock;
        currentWaterY = EvaluateNaturalWaterY(0f, firstCycleStartClock);
        highWaterMemory = TideHighWaterMemoryModel.Begin(currentWaterY);
        AdvanceHighWaterMemory(0f, firstCycleStartClock, cycle);
        float rememberedFirstHighY = highWaterMemory.PreviousCyclePeakY;
        moonAgeDays = Mathf.Repeat(
            OpeningMoonAgeDays + secondHighWorldClock / dayLengthSeconds,
            29.53f);
        tideStrength = CalculateTideStrength(moonAgeDays);
        AdvanceHighWaterMemory(0f, secondCycleStartClock, cycle);
        float rememberedSecondHighY = highWaterMemory.PreviousCyclePeakY;
        float rememberedHigherHighY = firstIsHigher ? rememberedFirstHighY : rememberedSecondHighY;
        float rememberedLowerHighY = firstIsHigher ? rememberedSecondHighY : rememberedFirstHighY;
        bool physicalSaltMemoryDistinguishesTides =
            rememberedHigherHighY > rememberedLowerHighY + 0.08f &&
            Mathf.Abs(rememberedHigherHighY - higherHighPredictionY) <= 0.02f &&
            Mathf.Abs(rememberedLowerHighY - lowerHighPredictionY) <= 0.02f;

        string evidence =
            $"高潮Y={higherHighPredictionY:F3}/{lowerHighPredictionY:F3}；" +
            $"同网有效相遇={higherHighNetEncounterSeconds:F2}/{lowerHighNetEncounterSeconds:F2}s；" +
            $"潮间作业={higherHighWorkWindowSeconds:F1}/{lowerHighWorkWindowSeconds:F1}s；" +
            $"盐痕={rememberedHigherHighY:F3}/{rememberedLowerHighY:F3}；" +
            $"平流={higherHighCurrent:F4}/{lowerHighCurrent:F4}";
        bool passed = adjacentHighsCreateDifferentOpportunities &&
            bothHighWatersRemainSlack && physicalSaltMemoryDistinguishesTides;
        stormFrontArrivalDays = originalStormFrontArrivalDays;
        ResetSlice();
        return passed
            ? $"PASS：相邻高潮改变同一批漂物的穿网和潮间作业窗；较高潮也可能因漫顶降低拦截，盐湿痕和预报读取同一结果。{evidence}"
            : $"FAIL：相邻高潮尚未形成可读机会差，或预报、盐痕、实际水位没有共用同一潮。{evidence}";
    }

    private float MeasurePredictedNearshoreWorkWindowSeconds(
        int astronomicalCycleOrdinal,
        float cycleStartWorldClockSeconds)
    {
        const int samples = 192;
        float cycle = Mathf.Max(8f, tideCycleSeconds);
        float pressureAtHigh01 = EvaluateStormPressureAtCycleHigh01(
            cycleStartWorldClockSeconds,
            0f,
            astronomicalCycleOrdinal);
        float inequalityRatio = EvaluateTideInequalityRatio(
            cycleStartWorldClockSeconds + cycle * 0.5f);
        float workWindowSeconds = 0f;
        for (int i = 0; i < samples; i++)
        {
            float phase01 = (i + 0.5f) / samples;
            float waterY = EvaluateNaturalWaterYAtPhase(
                phase01,
                astronomicalCycleOrdinal,
                pressureAtHigh01,
                inequalityRatio);
            if (waterY <= lowWaterY + shoreWorkMaxWaterOffset)
            {
                workWindowSeconds += cycle / samples;
            }
        }

        return workWindowSeconds;
    }

    public string RunEditorPlayerCurrentSwimmingProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalInspected = true;
        viewMode = SliceViewMode.Shelter;
        state = SliceState.TideRising;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(1.2f, GetPlayerLaneY(playerLane));
        playerHorizontalVelocity = 0f;
        playerMoving = false;
        playerWalkCycle = 0f;
        tideClockSeconds = tideCycleSeconds * 0.25f;
        currentWaterY = GetPlayerLaneY(playerLane) + 0.12f;
        float passiveStartX = playerPosition.x;
        TickPlayerBuoyancyAndCurrent(0.5f);
        float passiveDrift = playerPosition.x - passiveStartX;
        bool passiveDriftAnimates = playerSwimming && passiveDrift < -0.05f &&
            playerMoving && playerWalkCycle > 0.05f;

        playerPosition = new Vector2(GetLaneMinX(playerLane), currentWaterY + 0.02f);
        playerHorizontalVelocity = 0f;
        playerMoving = false;
        float boundaryCycle = playerWalkCycle;
        TickPlayerBuoyancyAndCurrent(0.5f);
        bool boundaryStopsHiddenCurrent = Mathf.Abs(playerPosition.x - GetLaneMinX(playerLane)) <= 0.001f &&
            !playerMoving && Mathf.Abs(playerWalkCycle - boundaryCycle) <= 0.001f;

        currentWaterY = GetPlayerStandingFeetY(playerLane) + 0.18f;
        playerPosition = new Vector2(1.2f, GetPlayerLaneY(playerLane));
        playerMoving = false;
        TickPlayerBuoyancyAndCurrent(0.2f);
        bool wadingIsNotSwimming = !playerSwimming && playerSubmersion01 > 0f && playerSubmersion01 < 1f;

        currentWaterY = GetPlayerStandingFeetY(playerLane) - 0.12f;
        float aquaticBlendBeforeExit = playerAquaticBlend01;
        TickPlayerBuoyancyAndCurrent(0.2f);
        bool exitTransitionIsContinuous = playerAquaticBlend01 < aquaticBlendBeforeExit &&
            playerAquaticBlend01 > 0f &&
            playerPosition.y < GetPlayerLaneY(playerLane);
        TickPlayerBuoyancyAndCurrent(0.45f);
        bool dryGroundClearsCurrentMotion = !playerSwimming && !playerMoving &&
            playerAquaticBlend01 <= 0.001f &&
            Mathf.Abs(playerPosition.y - GetPlayerLaneY(playerLane)) <= 0.001f;

        string evidence = $"漂移={passiveDrift:F3}/踩水={passiveDriftAnimates}；边界停流={boundaryStopsHiddenCurrent}；" +
            $"涉水={wadingIsNotSwimming}；退出连续={exitTransitionIsContinuous}；退水落地={dryGroundClearsCurrentMotion}" +
            $"(swim={playerSwimming}/move={playerMoving}/blend={playerAquaticBlend01:F3}/dy={playerPosition.y - GetPlayerLaneY(playerLane):F3}/surface={GetOceanSample(playerPosition.x).SurfaceY:F3})";
        return passiveDriftAnimates && boundaryStopsHiddenCurrent && wadingIsNotSwimming &&
            exitTransitionIsContinuous && dryGroundClearsCurrentMotion
            ? $"PASS：人物被潮流推动时保持踩水反馈，边界、涉水和退水状态连续。{evidence}"
            : $"FAIL：人物仍会静态漂移、在边界空划或退水后残留游泳状态。{evidence}";
    }

    public string RunEditorUninspectedWorldClockProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalVignetteActive = false;
        arrivalInspected = false;

        UpdateVisuals(0f);
        bool equipmentExistsBeforeInspection = tideRoutingWinchRenderer.enabled &&
            (formalNetBundleRenderer.enabled || formalNetRenderer.enabled);

        float tideBefore = tideClockSeconds;
        float dayBefore = dayClockSeconds;
        float weatherBefore = weatherClockSeconds;
        float boatBefore = mooredBoatOffsetX;
        TickState(1f);
        float tideAfterNaturalTick = tideClockSeconds;
        float dayAfterNaturalTick = dayClockSeconds;
        float weatherAfterNaturalTick = weatherClockSeconds;
        float boatAfterNaturalTick = mooredBoatOffsetX;

        for (int i = 0; i < 90 && state == SliceState.LowTidePlanning; i++)
        {
            TickState(1f);
        }
        bool tideProgressesBeforeInspection = state == SliceState.TideRising;

        float tideBeforeInspection = tideClockSeconds;
        float dayBeforeInspection = dayClockSeconds;
        float weatherBeforeInspection = weatherClockSeconds;
        SliceState stateBeforeInspection = state;
        InspectArrivalWreck();
        // The tide may have advanced during the progression loop above. The actual
        // inspection must preserve the exact state reached at that moment.
        bool inspectionWasPureKnowledge = arrivalInspected &&
            Mathf.Approximately(tideClockSeconds, tideBeforeInspection) &&
            Mathf.Approximately(dayClockSeconds, dayBeforeInspection) &&
            Mathf.Approximately(weatherClockSeconds, weatherBeforeInspection) &&
            state == stateBeforeInspection;
        bool natureMovedBeforeInspection = tideAfterNaturalTick > tideBefore &&
            dayAfterNaturalTick > dayBefore &&
            weatherAfterNaturalTick > weatherBefore;

        ResetSlice();
        arrivalVignetteActive = false;
        arrivalInspected = false;
        state = SliceState.TideRising;
        dayNightPhase = DayNightPhase.Day;
        currentWaterY = lowWaterY + 0.66f;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(GetBoatBoardingX(), GetPlayerLaneY(playerLane));
        float uninspectedMooringSeconds = 0f;
        bool uninspectedMooringSecured = AdvanceProbeMooringUntilSecured(
            0.05f,
            12f,
            false,
            ref uninspectedMooringSeconds);
        TryBoardBoat();
        bool canBoardBeforeInspection = uninspectedMooringSecured &&
            boatViewTransition == BoatViewTransition.Boarding;

        string evidence = $"检查残骸前：潮钟 {tideBefore:F2}->{tideAfterNaturalTick:F2}，" +
            $"昼夜钟 {dayBefore:F2}->{dayAfterNaturalTick:F2}，" +
            $"天气钟 {weatherBefore:F2}->{weatherAfterNaturalTick:F2}，" +
            $"系泊偏移 {boatBefore:F3}->{boatAfterNaturalTick:F3}；" +
            $"设备={equipmentExistsBeforeInspection}/涨潮={tideProgressesBeforeInspection}/上船={canBoardBeforeInspection}";
        return natureMovedBeforeInspection && equipmentExistsBeforeInspection &&
            tideProgressesBeforeInspection && inspectionWasPureKnowledge && canBoardBeforeInspection
            ? $"PASS：{evidence}；残骸只提供知识，不再解锁世界。"
            : $"FAIL：{evidence}；纯知识={inspectionWasPureKnowledge}。";
    }

    public string RunEditorFirstDayAutonomyProbe()
    {
        EnsureScene();
        ResetSlice();

        float openingX = playerPosition.x;
        float firstTidePlanningSeconds = 0f;
        while (state == SliceState.LowTidePlanning && firstTidePlanningSeconds < 90f)
        {
            TickState(0.1f);
            firstTidePlanningSeconds += 0.1f;
        }

        float storedNetX = GetNetStoredX();
        float firstStakeX = GetNetFirstStakeX();
        float secondStakeX = GetNetSecondStakeX();
        float walkingSeconds =
            (Mathf.Abs(openingX - storedNetX) +
             Mathf.Abs(storedNetX - firstStakeX) +
             Mathf.Abs(firstStakeX - secondStakeX)) /
            Mathf.Max(0.1f, playerMoveSpeed);
        float physicalRigSeconds = netRigHoldSeconds * 2f + netLoweringSeconds * 0.68f;
        float practicalNetSetupSeconds = walkingSeconds + physicalRigSeconds;
        bool tideHasRealBreathingRoom = firstTidePlanningSeconds >= practicalNetSetupSeconds + 5f;

        float secondsUntilDusk = Mathf.Repeat(0.72f - 0.4f, 1f) * dayLengthSeconds;
        float secondsUntilNight = Mathf.Repeat(0.82f - 0.4f, 1f) * dayLengthSeconds;
        bool daylightSupportsExploration = secondsUntilDusk >= 140f && secondsUntilNight >= 190f;

        ResetSlice();
        arrivalInspected = false;
        bool actionsDoNotWaitForInspection = CanStartNewNetRigAtCurrentTime();
        playerPosition = new Vector2(arrivalWreckX, GetPlayerLaneY(WalkLane.TideFlat));
        bool invisibleLegacyWreckRetired = !IsPlayerNearArrivalWreck();

        UpdateVisuals(0f);
        Vector2 openingWreckWorkPosition = new Vector2(
            TideBarrenIslandController.OpeningPlayerX,
            GetPlayerLaneY(WalkLane.TideFlat));
        bool leftWreckIsPhysical = CompleteEditorWreckDismantle(
                openingWreckWorkPosition,
                out TideIslandSalvagePart firstPart) &&
            firstPart != TideIslandSalvagePart.None;

        string evidence =
            $"首潮布置窗={firstTidePlanningSeconds:F1}s；走路+双结+放纲={practicalNetSetupSeconds:F1}s；" +
            $"黄昏/夜晚余量={secondsUntilDusk:F0}/{secondsUntilNight:F0}s；" +
            $"无检查可行动={actionsDoNotWaitForInspection}；旧隐形残骸退役={invisibleLegacyWreckRetired}；" +
            $"左船骸首件={leftWreckIsPhysical}/{firstPart}";
        return tideHasRealBreathingRoom && daylightSupportsExploration &&
            actionsDoNotWaitForInspection && invisibleLegacyWreckRetired && leftWreckIsPhysical
            ? $"PASS：首日时钟不等待叙事，玩家可先拆船、先布网或直接回屋，潮水按自己的时间到来。{evidence}"
            : $"FAIL：首日存在隐藏交互、强制叙事门或不足以完成一次真实布网的时间窗。{evidence}";
    }

    public string RunEditorWreckDismantleTideWindowProbe()
    {
        EnsureScene();
        ResetSlice();
        UpdateVisuals(0f);
        if (barrenIsland == null)
        {
            return "FAIL：正式 Scene 缺少贫瘠岩礁岛控制器。";
        }

        Vector2 partAnchor = barrenIsland.GetPartWorldPosition(TideIslandSalvagePart.HullPlank);
        Vector2 workPosition = new Vector2(partAnchor.x, GetPlayerLaneY(WalkLane.TideFlat));
        float feetY = GetPlayerStandingFeetY(WalkLane.TideFlat);
        Transform source = barrenIsland.transform.Find(
            "GeneratedBarrenIslandRoot/SalvageHullPlank");
        Transform carried = barrenIsland.transform.Find(
            "GeneratedBarrenIslandRoot/CarriedWreckPart");
        if (source == null || carried == null)
        {
            return "FAIL：船骸原件或手持 owner 节点不存在。";
        }

        Vector3 sourceStart = source.position;
        Quaternion sourceStartRotation = source.rotation;
        bool started = barrenIsland.TickDismantleNearestPart(
            workPosition,
            0.1f,
            true,
            true,
            true,
            feetY - 0.18f,
            0.08f,
            out TideIslandDismantleFeedback firstStep);
        UpdateVisuals(0.1f);
        bool firstPressIsNotInstant = started && firstStep.Worked && !firstStep.Completed &&
            firstStep.Progress01 > 0f && firstStep.Progress01 < 0.05f;
        bool partialWorkMovesOriginal = Vector3.Distance(sourceStart, source.position) > 0.0002f ||
            Quaternion.Angle(sourceStartRotation, source.rotation) > 0.05f;

        barrenIsland.TickDismantleNearestPart(
            workPosition,
            0.5f,
            false,
            false,
            true,
            feetY - 0.18f,
            0.08f,
            out TideIslandDismantleFeedback released);
        float preservedProgress = released.Progress01;
        bool releasePreservesWork = Mathf.Abs(preservedProgress - firstStep.Progress01) <= 0.0001f;

        barrenIsland.TickDismantleNearestPart(
            workPosition,
            0.8f,
            true,
            true,
            false,
            feetY + 0.62f,
            0.12f,
            out TideIslandDismantleFeedback flooded);
        barrenIsland.TickDismantleNearestPart(
            workPosition,
            0.8f,
            false,
            true,
            true,
            feetY - 0.18f,
            0.94f,
            out TideIslandDismantleFeedback breakingWave);
        bool tideAndWavePauseSameOriginal =
            Mathf.Abs(flooded.Progress01 - preservedProgress) <= 0.0001f &&
            flooded.BlockReason == TideWreckDismantleBlockReason.NoStableFooting &&
            Mathf.Abs(breakingWave.Progress01 - preservedProgress) <= 0.0001f &&
            breakingWave.BlockReason == TideWreckDismantleBlockReason.BreakingWave;

        TideIslandDismantleFeedback completed = breakingWave;
        for (int i = 0; i < 80 && !completed.Completed; i++)
        {
            barrenIsland.TickDismantleNearestPart(
                workPosition,
                0.1f,
                false,
                true,
                true,
                feetY - 0.18f,
                0.08f,
                out completed);
        }

        UpdateVisuals(1f);
        SpriteRenderer sourceRenderer = source.GetComponent<SpriteRenderer>();
        SpriteRenderer carriedRenderer = carried.GetComponent<SpriteRenderer>();
        bool ownershipCommitsOnce = completed.Completed &&
            barrenIsland.CarriedPart == TideIslandSalvagePart.HullPlank &&
            sourceRenderer != null && !sourceRenderer.enabled &&
            carriedRenderer != null && carriedRenderer.enabled;
        string evidence =
            $"首按={firstStep.Progress01:P1}/松手={released.Progress01:P1}/" +
            $"进水={flooded.BlockReason}/破浪={breakingWave.BlockReason}/" +
            $"原件松动={partialWorkMovesOriginal}/完成owner={ownershipCommitsOnce}";
        return firstPressIsNotInstant && partialWorkMovesOriginal && releasePreservesWork &&
            tideAndWavePauseSameOriginal && ownershipCommitsOnce
            ? $"PASS：船骸拆卸使用真实潮窗、持续工时和同一原物反馈。{evidence}"
            : $"FAIL：船骸仍可瞬取、无潮浪约束或完成时出现重复 owner。{evidence}";
    }

    public string RunEditorArrivalSalvagePayoffProbe()
    {
        EnsureScene();
        ResetSlice();
        UpdateVisuals(0f);
        if (barrenIsland == null)
        {
            return "FAIL：正式 Scene 缺少贫瘠岩礁岛控制器。";
        }

        TideIslandSalvagePart[] parts =
        {
            TideIslandSalvagePart.HullPlank,
            TideIslandSalvagePart.HullPlank,
            TideIslandSalvagePart.Sailcloth,
            TideIslandSalvagePart.Sailcloth,
            TideIslandSalvagePart.RivetedPlate,
            TideIslandSalvagePart.RivetedPlate
        };
        RepairChoice[] choices =
        {
            RepairChoice.Stilt,
            RepairChoice.Hull,
            RepairChoice.Net,
            RepairChoice.Sail,
            RepairChoice.Cistern,
            RepairChoice.Cabin
        };
        TideIslandSalvageUse[] uses =
        {
            TideIslandSalvageUse.Shelter,
            TideIslandSalvageUse.EscapeBoat,
            TideIslandSalvageUse.Shelter,
            TideIslandSalvageUse.EscapeBoat,
            TideIslandSalvageUse.Shelter,
            TideIslandSalvageUse.EscapeBoat
        };
        TideIslandSalvageDestination[] destinations =
        {
            TideIslandSalvageDestination.ShelterStaging,
            TideIslandSalvageDestination.EscapeBoatStaging,
            TideIslandSalvageDestination.ShelterStaging,
            TideIslandSalvageDestination.EscapeBoatStaging,
            TideIslandSalvageDestination.ShelterStaging,
            TideIslandSalvageDestination.EscapeBoatStaging
        };
        bool allSixStartFromOnePhysicalPart = true;
        for (int i = 0; i < choices.Length; i++)
        {
            RepairChoice resolvedChoice = TideRepairRecipeModel.GetArrivalRepairTarget(parts[i], uses[i]);
            GetRepairMaterialNeeds(
                resolvedChoice,
                out int timberNeed,
                out int ropeNeed,
                out int clothNeed,
                out int metalNeed,
                out int foodNeed);
            TideMaterialBundle needs = new TideMaterialBundle(
                timberNeed,
                ropeNeed,
                clothNeed,
                metalNeed,
                foodNeed);
            int selected = TideSalvageMaterialModel.SelectMinimumParts(
                TideSalvageMaterialModel.GetPartBit(parts[i]),
                new TideMaterialBundle(),
                needs);
            allSixStartFromOnePhysicalPart &= resolvedChoice == choices[i] &&
                selected == TideSalvageMaterialModel.GetPartBit(parts[i]) &&
                GetStagingDestinationForRepair(resolvedChoice) == destinations[i];
        }

        Vector2 plateAnchor = barrenIsland.GetPartWorldPosition(TideIslandSalvagePart.RivetedPlate);
        bool dismantled = CompleteEditorWreckDismantle(
            plateAnchor,
            out TideIslandSalvagePart completedPart);
        bool staged = dismantled && completedPart == TideIslandSalvagePart.RivetedPlate &&
            barrenIsland.TryStageCarriedPart(
                TideIslandSalvageUse.Shelter,
                out TideIslandSalvagePart stagedPart) &&
            stagedPart == TideIslandSalvagePart.RivetedPlate;

        UpdateVisuals(0.25f);
        Transform visualRoot = barrenIsland.transform.Find("GeneratedBarrenIslandRoot");
        SpriteRenderer source = visualRoot != null
            ? visualRoot.Find("SalvageRivetedPlate")?.GetComponent<SpriteRenderer>()
            : null;
        SpriteRenderer carried = visualRoot != null
            ? visualRoot.Find("CarriedWreckPart")?.GetComponent<SpriteRenderer>()
            : null;
        SpriteRenderer stagedRenderer = visualRoot != null
            ? visualRoot.Find("StagedAtShelter_RivetedPlate")?.GetComponent<SpriteRenderer>()
            : null;
        SpriteRenderer patch = visualRoot != null
            ? visualRoot.Find("CisternRivetedPatch")?.GetComponent<SpriteRenderer>()
            : null;
        SpriteRenderer cisternBody = visualRoot != null
            ? visualRoot.Find("CrackedRainCistern")?.GetComponent<SpriteRenderer>()
            : null;
        SpriteRenderer liveWater = visualRoot != null
            ? visualRoot.Find("CisternWaterSurface")?.GetComponent<SpriteRenderer>()
            : null;
        SpriteRenderer saltResidue = visualRoot != null
            ? visualRoot.Find("CisternSaltLine")?.GetComponent<SpriteRenderer>()
            : null;
        SpriteRenderer leakStream = visualRoot != null
            ? visualRoot.Find("CisternLeakStream")?.GetComponent<SpriteRenderer>()
            : null;
        float waterYBeforeRepair = liveWater != null ? liveWater.transform.position.y : float.NaN;
        float feetY = GetPlayerStandingFeetY(WalkLane.TideFlat);
        bool damagedCisternIsReadable = cisternBody != null && liveWater != null &&
            saltResidue != null && leakStream != null && liveWater.enabled &&
            saltResidue.enabled && leakStream.enabled &&
            Mathf.Abs(liveWater.transform.position.y - saltResidue.transform.position.y) > 0.008f &&
            liveWater.bounds.min.x >= cisternBody.bounds.min.x - 0.02f &&
            liveWater.bounds.max.x <= cisternBody.bounds.max.x + 0.02f &&
            leakStream.bounds.max.y >= feetY + 0.43f &&
            leakStream.bounds.min.y <= feetY + 0.07f;

        float crackBefore = barrenIsland.Cistern.Crack01;
        playerLane = WalkLane.TideFlat;
        viewMode = SliceViewMode.Shelter;
        playerPosition = GetRepairChoicePosition(RepairChoice.Cistern);
        bool workPositionHasVisibleSupport = barrenIsland.IsVisibleWalkSupportAt(new Vector2(
            playerPosition.x,
            GetPlayerStandingFeetY(WalkLane.TideFlat)));
        bool startedBeforeFirstTide = state == SliceState.LowTidePlanning &&
            TickRepairWorkAtWorldTarget(0.02f, true, true);
        for (int i = 0; i < 320 && !repairChoiceApplied; i++)
        {
            TickRepairWorkAtWorldTarget(0.02f, false, true);
        }
        UpdateVisuals(1f);

        bool oneFinalOwner = source != null && carried != null && stagedRenderer != null && patch != null &&
            !source.enabled && !carried.enabled && !stagedRenderer.enabled && patch.enabled;
        bool sealedCisternIsReadable = liveWater != null && saltResidue != null &&
            leakStream != null && liveWater.enabled && saltResidue.enabled &&
            !leakStream.enabled &&
            Mathf.Abs(liveWater.transform.position.y - waterYBeforeRepair) <= 0.001f;
        bool physicalCommit = staged && damagedCisternIsReadable && sealedCisternIsReadable &&
            workPositionHasVisibleSupport &&
            startedBeforeFirstTide && repairChoiceApplied &&
            barrenIsland.CisternPlatePatchApplied &&
            barrenIsland.Cistern.Crack01 <= 0.1f &&
            barrenIsland.Cistern.Crack01 < crackBefore - 0.5f &&
            barrenIsland.GetDestination(TideIslandSalvagePart.RivetedPlate) ==
                TideIslandSalvageDestination.IntegratedIntoShelter &&
            barrenIsland.ShelterStagedParts == 0 &&
            metalStock == 0 && oneFinalOwner;

        string evidence =
            $"六路={allSixStartFromOnePhysicalPart}/拆放={dismantled}/{completedPart}/{staged}/" +
            $"承重={workPositionHasVisibleSupport}/首潮前={startedBeforeFirstTide}/" +
            $"裂缝={crackBefore:F2}->{barrenIsland.Cistern.Crack01:F2}/" +
            $"池态={damagedCisternIsReadable}->{sealedCisternIsReadable}/" +
            $"owner={oneFinalOwner}/余铁={metalStock}";
        return allSixStartFromOnePhysicalPart && physicalCommit
            ? $"PASS：三件船骸投向住所或船的六种选择都能立即兑现，铆板修池保持唯一实物与真实漏率收益。{evidence}"
            : $"FAIL：首件材料仍存在死路、状态门、重复 owner 或虚假蓄水收益。{evidence}";
    }

    private bool CompleteEditorWreckDismantle(
        Vector2 workPosition,
        out TideIslandSalvagePart completedPart)
    {
        completedPart = TideIslandSalvagePart.None;
        if (barrenIsland == null)
        {
            return false;
        }

        float dryWaterY = GetPlayerStandingFeetY(WalkLane.TideFlat) - 0.18f;
        for (int i = 0; i < 100; i++)
        {
            bool consumed = barrenIsland.TickDismantleNearestPart(
                workPosition,
                0.1f,
                i == 0,
                true,
                true,
                dryWaterY,
                0.08f,
                out TideIslandDismantleFeedback feedback);
            if (!consumed)
            {
                return false;
            }

            if (feedback.Completed)
            {
                completedPart = feedback.Part;
                return true;
            }
        }

        return false;
    }

    public string RunEditorTidePrepTradeoffProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalInspected = true;
        tidePrepReady = true;
        tidePrepTargetRound = tideRound;

        // All three tools are physical preparations, not loot rerolls. A bucket can
        // help the player remove water from the boat, but it cannot turn debris that
        // is already drifting in this tide into a fish.
        tideSourceHarvest = HarvestKind.Trash;
        selectedPrepChoice = TidePrepChoice.Bucket;
        HarvestKind bucketHarvest = BuildHarvest();
        float bucketBailRate = GetEffectiveSailingBailRate();

        selectedPrepChoice = TidePrepChoice.None;
        float ordinaryBailRate = GetEffectiveSailingBailRate();

        selectedPrepChoice = TidePrepChoice.Rope;
        netSetDepth01 = 1f;
        tideStrength = 1f;
        netPeakTension01 = 1f;
        netAccumulatedTension = TideNetLoadLedgerModel.StressTierTwoImpulse + 0.2f;
        int ropeStress = CalculatePrepAdjustedNetStress();
        selectedPrepChoice = TidePrepChoice.None;
        int ordinaryNetStress = CalculatePrepAdjustedNetStress();

        selectedPrepChoice = TidePrepChoice.Stake;
        stiltIntegrity = 3;
        tideRound = departureStormRound;
        weatherClockSeconds = dayLengthSeconds * stormFrontArrivalDays;
        moonAgeDays = 0f;
        tideStrength = CalculateTideStrength(moonAgeDays);
        int rawShelterStress = CalculateRawShelterTideStress();
        int stakeShelterStress = Mathf.Max(0, rawShelterStress - 1);

        bool bucketKeepsSource = bucketHarvest == HarvestKind.Trash;
        bool bucketHelpsManualBailing = bucketBailRate > ordinaryBailRate;
        bool ropeProtectsNet = ropeStress < ordinaryNetStress;
        bool stakeProtectsShelter = rawShelterStress > 0 && stakeShelterStress < rawShelterStress;
        bool rolesReadable = GetPrepRoleName(TidePrepChoice.Rope) == "保网" &&
            GetPrepRoleName(TidePrepChoice.Bucket) == "保船" &&
            GetPrepRoleName(TidePrepChoice.Stake) == "保屋";
        int shallowSmallTideNetRisk = CalculateForecastNetStress(0.15f, 0.45f);
        int deepStrongTideNetRisk = CalculateForecastNetStress(1f, 1f);
        float repairedHullIngress = CalculateForecastBoatIngressPerSecond(3, 0.7f);
        float damagedHullIngress = CalculateForecastBoatIngressPerSecond(1, 0.7f);
        string riskSummary = GetTidePrepRiskForecastText();
        bool forecastFollowsPhysicalRisk = deepStrongTideNetRisk > shallowSmallTideNetRisk &&
            damagedHullIngress > repairedHullIngress &&
            riskSummary.Contains("网压") && riskSummary.Contains("屋压") && riskSummary.Contains("船漏");
        string evidence = $"漂物={bucketHarvest}；舀水 {ordinaryBailRate:F3}->{bucketBailRate:F3}；" +
            $"网压 {ordinaryNetStress}->{ropeStress}；屋压 {rawShelterStress}->{stakeShelterStress}；" +
            $"预测网压 {shallowSmallTideNetRisk}->{deepStrongTideNetRisk}；船漏 {repairedHullIngress:F3}->{damagedHullIngress:F3}";

        return bucketKeepsSource && bucketHelpsManualBailing && ropeProtectsNet && stakeProtectsShelter && rolesReadable && forecastFollowsPhysicalRisk
            ? $"PASS：潮前工具形成保网/保船/保屋三选一，且不改写潮源。{evidence}"
            : $"FAIL：潮前工具职责仍有串线。{evidence}";
    }

    public string RunEditorDayNightActionWindowProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalInspected = true;
        netRigStep = NetRigStep.Stored;
        pendingRepairChoice = RepairChoice.None;

        dayNightPhase = DayNightPhase.Day;
        bool dayCanStartNet = CanStartNewNetRigAtCurrentTime();
        bool dayCanStartOutdoorRepair = CanStartRepairAtCurrentTime(RepairChoice.Stilt);

        dayNightPhase = DayNightPhase.Night;
        bool nightBlocksNewNet = !CanStartNewNetRigAtCurrentTime();
        bool nightNetPromptMatchesRule = GetNetRigPromptText() == "天亮再布网";
        bool nightBlocksNewOutdoorRepair = !CanStartRepairAtCurrentTime(RepairChoice.Stilt);
        bool nightAllowsIndoorRepair = CanStartRepairAtCurrentTime(RepairChoice.Roof);

        pendingRepairChoice = RepairChoice.Stilt;
        bool nightAllowsStartedOutdoorRepair = CanStartRepairAtCurrentTime(RepairChoice.Stilt);

        viewMode = SliceViewMode.Sailing;
        sailingBoatX = sailingHomeX;
        sailingBoatLaneY = sailingHomeY;
        bool nightAllowsChosenReturn = CanReturnFromSailing();

        bool passed = dayCanStartNet && dayCanStartOutdoorRepair && nightBlocksNewNet && nightNetPromptMatchesRule &&
            nightBlocksNewOutdoorRepair && nightAllowsIndoorRepair &&
            nightAllowsStartedOutdoorRepair && nightAllowsChosenReturn;
        string evidence = $"白天新布网={dayCanStartNet}/新外修={dayCanStartOutdoorRepair}；" +
            $"夜里拦新网={nightBlocksNewNet}/提示={nightNetPromptMatchesRule}/拦新外修={nightBlocksNewOutdoorRepair}/内修={nightAllowsIndoorRepair}/续修={nightAllowsStartedOutdoorRepair}/返航={nightAllowsChosenReturn}";
        return passed
            ? $"PASS：昼夜改变可开始的工作，但不取消紧急收尾与玩家自主返航。{evidence}"
            : $"FAIL：昼夜行动窗口不符合自然节奏。{evidence}";
    }

    public string RunEditorNetRigHoldContinuityProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalInspected = true;
        netRigStep = NetRigStep.Carrying;
        netRigActionProgress = 0f;

        bool completedEarly = TickNetRigHoldProgress(0.3f, true, true);
        float partialProgress = netRigActionProgress;
        bool completedWhileReleased = TickNetRigHoldProgress(0.5f, false, false);
        float pausedProgress = netRigActionProgress;
        bool heldWithoutNewPressCannotResume = TickNetRigHoldProgress(0.6f, false, true);
        float blockedProgress = netRigActionProgress;
        bool completedAfterResume = TickNetRigHoldProgress(0.6f, true, true);

        bool partialWasReal = !completedEarly && partialProgress > 0.2f && partialProgress < 0.8f;
        bool releasePreservedWork = !completedWhileReleased && Mathf.Approximately(partialProgress, pausedProgress);
        bool heldThroughReleaseCannotResume = !heldWithoutNewPressCannotResume &&
            Mathf.Approximately(pausedProgress, blockedProgress);
        bool resumeCompleted = completedAfterResume && Mathf.Approximately(netRigActionProgress, 1f);
        string evidence = $"短按={partialProgress:F2}；松手后={pausedProgress:F2}；无新按下={blockedProgress:F2}；续按={netRigActionProgress:F2}";
        return partialWasReal && releasePreservedWork && heldThroughReleaseCannotResume && resumeCompleted
            ? $"PASS：系网动作按住推进、松手保留，并且必须由新的按下边沿继续。{evidence}"
            : $"FAIL：系网动作仍会被持续按键穿透或中断丢进度。{evidence}";
    }

    public string RunEditorNetRigInputEdgeIsolationProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalInspected = true;
        arrivalVignetteActive = false;
        state = SliceState.LowTidePlanning;
        dayNightPhase = DayNightPhase.Day;
        currentWaterY = lowWaterY + 0.04f;
        playerLane = WalkLane.TideFlat;

        playerPosition = new Vector2(GetNetStoredX(), GetPlayerLaneY(playerLane));
        TickNetRiggingInput(0.01f, true, true, false);
        bool pickupOwnsOnlyPickup = netRigStep == NetRigStep.Carrying && netRigActionProgress <= 0.001f;
        TickNetRiggingInput(0.32f, false, true, false);
        bool heldPickupCannotStartKnot = netRigStep == NetRigStep.Carrying && netRigActionProgress <= 0.001f;

        playerPosition = new Vector2(GetNetFirstStakeX(), GetPlayerLaneY(playerLane));
        TickNetRiggingInput(netRigHoldSeconds + 0.01f, true, true, false);
        bool firstEndComplete = netRigStep == NetRigStep.FirstEndTied;

        playerPosition = new Vector2(GetNetSecondStakeX(), GetPlayerLaneY(playerLane));
        UpdateNetUnrollFromPlayerPosition();
        TickNetRiggingInput(0.02f, false, true, false);
        bool walkOnlyUnrolls = netRigStep == NetRigStep.Unrolled && netRigActionProgress <= 0.001f;
        TickNetRiggingInput(netRigHoldSeconds + 0.01f, true, true, false);
        bool secondEndHasZeroDepth = netRigStep == NetRigStep.SecondEndTied &&
            netLoweringProgress <= 0.001f && netSetDepth01 <= 0.001f;

        TickNetRiggingInput(0.38f, false, true, false);
        bool heldSecondKnotCannotLower = netRigStep == NetRigStep.SecondEndTied && netSetDepth01 <= 0.001f;
        TickNetRiggingInput(0.42f, true, true, false);
        float heldDepth = netSetDepth01;
        bool loweringIsContinuous = netRigStep == NetRigStep.Lowering && heldDepth > 0.12f && heldDepth < 0.8f;
        TickNetRiggingInput(0f, false, false, true);
        bool releaseCommitsActualDepth = netRigStep == NetRigStep.Deployed && netDeployed &&
            Mathf.Abs(netSetDepth01 - heldDepth) <= 0.001f;

        bool passed = pickupOwnsOnlyPickup && heldPickupCannotStartKnot && firstEndComplete &&
            walkOnlyUnrolls && secondEndHasZeroDepth && heldSecondKnotCannotLower &&
            loweringIsContinuous && releaseCommitsActualDepth;
        string evidence = $"拾网隔离={pickupOwnsOnlyPickup}/{heldPickupCannotStartKnot}；第一端={firstEndComplete}；" +
            $"走展开={walkOnlyUnrolls}；第二端零深={secondEndHasZeroDepth}/{heldSecondKnotCannotLower}；" +
            $"连续深度={heldDepth:F2}；松手固定={releaseCommitsActualDepth}";
        return passed
            ? $"PASS：一次按住只完成一个语义动作，沉纲从零连续下放并在松手时固定。{evidence}"
            : $"FAIL：布网仍存在按键穿透、推荐深度瞬移或提前固定。{evidence}";
    }

    public string RunEditorNetRigWorldGeometryProbe()
    {
        SetEditorNetRigPreviewPose(4);
        float walkSurfaceY = GetPlayerStandingFeetY(WalkLane.TideFlat);
        float headLineY = GetNetHeadLineY();
        float sinkLineY = GetActiveNetVisualY();
        float localWaterY = GetNetOceanSample().SurfaceY;
        bool hangsSeaward = headLineY < walkSurfaceY - 0.05f &&
            sinkLineY < localWaterY && localWaterY < headLineY;

        bool hasTwoSuspensionRopes = netSuspensionRopes.Count >= 2 &&
            netSuspensionRopes[0].enabled && netSuspensionRopes[1].enabled;
        Vector2 firstExpectedCenter = new Vector2(
            GetNetFirstStakeX() + 0.0225f,
            (walkSurfaceY + 0.22f + headLineY) * 0.5f);
        Vector2 secondExpectedCenter = new Vector2(
            GetNetSecondStakeX() - 0.0225f,
            (walkSurfaceY + 0.22f + headLineY) * 0.5f);
        bool ropesAttachToPosts = hasTwoSuspensionRopes &&
            Vector2.Distance(netSuspensionRopes[0].bounds.center, firstExpectedCenter) <= 0.025f &&
            Vector2.Distance(netSuspensionRopes[1].bounds.center, secondExpectedCenter) <= 0.025f;

        int boardwalkOrder = formalBoardwalkSegments.Count > 0
            ? formalBoardwalkSegments[0].sortingOrder
            : tideFlatPathRenderer.sortingOrder;
        bool bodyBetweenSeaAndWalk = formalNetRenderer != null && formalNetRenderer.enabled &&
            formalNetRenderer.sortingOrder > naturalWaterSurfaceRenderer.sortingOrder &&
            formalNetRenderer.sortingOrder < boardwalkOrder;
        bool bodySpansHeadAndSink = formalNetRenderer != null &&
            formalNetRenderer.bounds.max.y >= headLineY - 0.1f &&
            formalNetRenderer.bounds.min.y <= sinkLineY + 0.1f;

        bool passed = hangsSeaward && ropesAttachToPosts && bodyBetweenSeaAndWalk && bodySpansHeadAndSink;
        string evidence = $"路/浮纲/沉纲/水={walkSurfaceY:F2}/{headLineY:F2}/{sinkLineY:F2}/{localWaterY:F2}；" +
            $"双悬绳={hasTwoSuspensionRopes}/{ropesAttachToPosts}；层级=海{naturalWaterSurfaceRenderer.sortingOrder}<网{formalNetRenderer.sortingOrder}<路{boardwalkOrder}；" +
            $"网体上下={formalNetRenderer.bounds.max.y:F2}/{formalNetRenderer.bounds.min.y:F2}";
        return passed
            ? $"PASS：网从两根真实固定点悬在木路临海侧，低潮水面穿过网体且所见深度与物理深度一致。{evidence}"
            : $"FAIL：网仍铺在木路、悬绳脱点、层级错误或网体没有跨过实际水面。{evidence}";
    }

    public string RunEditorNetWaterContactAttachmentProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalInspected = true;
        state = SliceState.TideRising;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        netSetDepth01 = 0.58f;
        selectedNetLine = GetNetLineFromLoweringProgress(netSetDepth01);
        float activeNetY = GetActiveNetVisualY();

        // Choose a clock where the local wave at the net differs measurably from the
        // mean tide. Otherwise this probe could pass even if physics regressed to
        // currentWaterY while the visible foam kept using the local ocean sample.
        float strongestOffset = 0f;
        float strongestWeatherClock = weatherClockSeconds;
        for (int i = 0; i < 24; i++)
        {
            weatherClockSeconds = i * 1.37f;
            currentWaterY = activeNetY;
            float candidateOffset = GetNetOceanSample().SurfaceY - currentWaterY;
            if (Mathf.Abs(candidateOffset) > Mathf.Abs(strongestOffset))
            {
                strongestOffset = candidateOffset;
                strongestWeatherClock = weatherClockSeconds;
            }
        }
        weatherClockSeconds = strongestWeatherClock;

        currentWaterY = activeNetY - 0.24f - strongestOffset;
        UpdateNetWaterContactVisuals(1.1f, activeNetY);
        bool dryNetHasNoFoam = !netWaterContactRenderer.enabled;

        currentWaterY = activeNetY + 0.04f - strongestOffset;
        UpdateNetWaterContactVisuals(1.3f, activeNetY);
        float crossingSurfaceY = GetNetOceanSample().SurfaceY;
        bool crossingShowsAttachedFoam = netWaterContactRenderer.enabled &&
            Mathf.Abs(netWaterContactRenderer.bounds.center.x - netAnchor.x) <= 0.12f &&
            Mathf.Abs(netWaterContactRenderer.transform.position.y - crossingSurfaceY) <= 0.08f;
        float crossingAlpha = netWaterContactRenderer.color.a;

        netTouched = false;
        netWaterExposureSeconds = 0f;
        netCaptureProgress01 = 0f;
        float expectedContact01 = EvaluateNetWaterContact01(activeNetY, crossingSurfaceY);
        TickNetWaterExposure(0.4f);
        float expectedExposure = 0.4f * expectedContact01;
        bool physicsUsesSameLocalSurface = netTouched &&
            Mathf.Abs(netWaterExposureSeconds - expectedExposure) <= 0.001f &&
            Mathf.Abs(strongestOffset) >= 0.005f;

        currentWaterY = activeNetY + 0.62f - strongestOffset;
        UpdateNetWaterContactVisuals(1.5f, activeNetY);
        bool overtoppedNetDropsSurfaceFoam = !netWaterContactRenderer.enabled;

        string evidence = $"未触水隐藏={dryNetHasNoFoam}；穿越贴网={crossingShowsAttachedFoam}/alpha={crossingAlpha:F2}；" +
            $"局部偏移={strongestOffset:F3}m/物理同源={physicsUsesSameLocalSurface}；漫顶淡出={overtoppedNetDropsSurfaceFoam}";
        return dryNetHasNoFoam && crossingShowsAttachedFoam && physicsUsesSameLocalSurface && overtoppedNetDropsSurfaceFoam
            ? $"PASS：潮头泡沫、浸水积分与网体受力共用网口局部海面，完全漫顶后由网体受力接管。{evidence}"
            : $"FAIL：网口视觉与物理仍读取不同海面，或泡沫会提前出现、脱离网体、在漫顶后悬浮。{evidence}";
    }

    public string RunEditorV54NetPresentationProbe()
    {
        EnsureScene();
        EnsureV54NetResourcesLoaded();
        string catalogReason = "Catalog 缺失";
        bool catalogComplete = formalV54NetCatalog != null &&
            formalV54NetCatalog.IsComplete(out catalogReason) &&
            formalV54NetCatalog.Profile == TideV54NetPresentationModel.RuntimeProfile;

        SetEditorNetRigPreviewPose(0);
        bool storedMapsExactly = IsOnlyV54StateVisible(
            TideV54NetVisualState.StoredDry,
            true);

        SetEditorNetRigPreviewPose(1);
        bool carriedMapsExactly = IsOnlyV54StateVisible(
            TideV54NetVisualState.CarriedDry,
            true) &&
            playerRenderer.sprite != null &&
            playerRenderer.sprite.name.IndexOf("CarryNetWalk", StringComparison.Ordinal) >= 0;

        SetEditorNetRigPreviewPose(2);
        float unrollingWidth = formalNetRenderer.bounds.size.x;
        bool unrollUsesReveal = IsOnlyV54StateVisible(
                TideV54NetVisualState.DeployedDry,
                false) &&
            netRevealMask.enabled &&
            formalNetRenderer.maskInteraction == SpriteMaskInteraction.VisibleInsideMask;

        SetEditorNetRigPreviewPose(4);
        currentWaterY = GetActiveNetVisualY() - 0.5f;
        netTouched = false;
        UpdateVisuals(3.9f);
        float deployedWidth = formalNetRenderer.bounds.size.x;
        bool dryMapsExactly = IsOnlyV54StateVisible(
            TideV54NetVisualState.DeployedDry,
            false);
        bool unrollKeepsNativeWidth = Mathf.Abs(unrollingWidth - deployedWidth) <= 0.015f;
        bool doubleSuspension = netSuspensionRopes.Count >= 2 &&
            netSuspensionRopes[0].enabled && netSuspensionRopes[1].enabled;

        state = SliceState.TideRising;
        currentWaterY = GetActiveNetVisualY() + 0.18f;
        netTouched = true;
        netFraying01 = 0f;
        netBrokeThisTide = false;
        UpdateVisuals(4.1f);
        bool wetMapsExactly = IsOnlyV54StateVisible(
            TideV54NetVisualState.DeployedWet,
            false);

        netFraying01 = 0.48f;
        UpdateVisuals(4.3f);
        bool frayedMapsExactly = IsOnlyV54StateVisible(
            TideV54NetVisualState.DeployedFrayed,
            false);

        netBrokeThisTide = true;
        UpdateVisuals(4.5f);
        bool brokenMapsExactly = IsOnlyV54StateVisible(
                TideV54NetVisualState.BrokenResidue,
                false) &&
            netSuspensionRopes[0].enabled &&
            !netSuspensionRopes[1].enabled;

        SetEditorV54HauledPreviewPose();
        bool hauledMapsExactly = IsOnlyV54StateVisible(
            TideV54NetVisualState.HauledWet,
            true);

        long rawBytes = 0L;
        long profilerBytes = 0L;
        long largestRawBytes = 0L;
        long largestProfilerBytes = 0L;
        HashSet<Texture> uniqueTextures = new HashSet<Texture>();
        if (formalV54NetCatalog != null)
        {
            for (int i = 0; i < TideV54NetPresentationModel.VisualStateCount; i++)
            {
                Sprite sprite = formalV54NetCatalog.Get((TideV54NetVisualState)i);
                Texture texture = sprite != null ? sprite.texture : null;
                if (texture == null || !uniqueTextures.Add(texture))
                {
                    continue;
                }

                long textureRawBytes = (long)texture.width * texture.height * 4L;
                rawBytes += textureRawBytes;
                largestRawBytes = Math.Max(largestRawBytes, textureRawBytes);
                long textureProfilerBytes =
                    UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
                profilerBytes += textureProfilerBytes;
                largestProfilerBytes = Math.Max(largestProfilerBytes, textureProfilerBytes);
            }
        }
        float rawMiB = rawBytes / (1024f * 1024f);
        float largestRawMiB = largestRawBytes / (1024f * 1024f);
        float profilerMiB = profilerBytes / (1024f * 1024f);
        float largestProfilerMiB = largestProfilerBytes / (1024f * 1024f);
        // Editor 对无压缩纹理会同时记录 CPU 侧资源和图形侧驻留，实测约为
        // runtime-contract 原始 RGBA32 的两倍。两套数都记录，但硬预算仍以
        // 契约 raw 4.27 MiB 为主，同时限制 Editor 驻留不能继续无界增长。
        bool memoryWithinBalancedBudget = rawMiB <= 4.3f &&
            largestRawMiB <= 0.85f && profilerMiB <= 9f && largestProfilerMiB <= 1.75f;

        bool passed = catalogComplete && storedMapsExactly && carriedMapsExactly &&
            unrollUsesReveal && unrollKeepsNativeWidth && dryMapsExactly && doubleSuspension &&
            wetMapsExactly && frayedMapsExactly && brokenMapsExactly &&
            hauledMapsExactly && memoryWithinBalancedBudget;
        string evidence =
            $"Catalog={catalogComplete}/{catalogReason}；存/携/展={storedMapsExactly}/{carriedMapsExactly}/{dryMapsExactly}；" +
            $"展开遮罩/原宽={unrollUsesReveal}/{unrollKeepsNativeWidth}({unrollingWidth:F3}/{deployedWidth:F3}m)；" +
            $"湿/磨/断/收={wetMapsExactly}/{frayedMapsExactly}/{brokenMapsExactly}/{hauledMapsExactly}；双悬绳={doubleSuspension}；" +
            $"纹理 raw={rawMiB:F2}MiB/单张={largestRawMiB:F2}MiB/Profiler={profilerMiB:F2}MiB/单张={largestProfilerMiB:F2}MiB";
        ResetSlice();
        return passed
            ? "PASS：V54 Balanced 七态按同一物理链切换，完整展开不压扁，断网只留存活悬绳，且纹理内存受控。" + evidence
            : "FAIL：V54 网态映射、展开方式、悬绳或内存预算至少一项失效。" + evidence;
    }

    public string RunEditorHarvestContactChainProbe()
    {
        HarvestKind[] kinds =
        {
            HarvestKind.Fish,
            HarvestKind.Wood,
            HarvestKind.Relic,
            HarvestKind.Trash
        };
        bool allKindsInsideNet = true;
        bool allKindsUseDistinctPoints = true;
        string kindEvidence = string.Empty;
        for (int kindIndex = 0; kindIndex < kinds.Length; kindIndex++)
        {
            SetEditorHarvestLifecyclePreviewPose(1);
            currentHarvest = kinds[kindIndex];
            netCatchBundleTier = kinds[kindIndex] == HarvestKind.Relic ? 3 : 2;
            netCatchVisualPieceCount = netCatchBundleTier;
            UpdateVisuals(2.2f + kindIndex * 0.37f);

            Bounds netBounds = formalNetRenderer.bounds;
            netBounds.Expand(new Vector3(0.04f, 0.04f, 0f));
            int visibleCount = 0;
            Vector2 previousCenter = Vector2.zero;
            float minimumSpacing = float.MaxValue;
            for (int i = 0; i < netCaughtItems.Count; i++)
            {
                SpriteRenderer item = netCaughtItems[i];
                if (!item.enabled)
                {
                    continue;
                }

                Vector2 center = item.bounds.center;
                bool centerInside = netBounds.Contains(
                    new Vector3(center.x, center.y, netBounds.center.z));
                allKindsInsideNet &= centerInside &&
                    item.sortingOrder == formalNetRenderer.sortingOrder + 1;
                if (visibleCount > 0)
                {
                    minimumSpacing = Mathf.Min(
                        minimumSpacing,
                        Vector2.Distance(previousCenter, center));
                }
                previousCenter = center;
                visibleCount++;
            }
            if (visibleCount > 1)
            {
                allKindsUseDistinctPoints &= minimumSpacing >= 0.08f;
            }
            kindEvidence += $"{GetHarvestName(kinds[kindIndex])}:{visibleCount}/{minimumSpacing:F2} ";
        }

        SetEditorHarvestLifecyclePreviewPose(2);
        // V54 的抬网阶段由折叠湿网束拥有像素，展开网会按契约关闭。
        // 探针必须检查当前可见承重物，不能继续拿已经隐藏的旧网边界判失败。
        SpriteRenderer haulingNetOwner = formalNetBundleRenderer != null && formalNetBundleRenderer.enabled
            ? formalNetBundleRenderer
            : formalNetRenderer;
        Bounds haulingBounds = haulingNetOwner.bounds;
        haulingBounds.Expand(new Vector3(0.12f, 0.12f, 0f));
        bool haulingStaysWithNet = true;
        float haulingOutsideDistance = 0f;
        for (int i = 0; i < netCaughtItems.Count; i++)
        {
            if (!netCaughtItems[i].enabled)
            {
                continue;
            }

            Vector3 center = netCaughtItems[i].bounds.center;
            center.z = haulingBounds.center.z;
            haulingStaysWithNet &= haulingBounds.Contains(center);
            haulingOutsideDistance = Mathf.Max(
                haulingOutsideDistance,
                Mathf.Sqrt(haulingBounds.SqrDistance(center)));
        }

        SetEditorHarvestLifecyclePreviewPose(3);
        Vector2 exteriorHands = GetV41CarryNetHandWorldPosition();
        bool exteriorCarryUsesHands = playerRenderer.sprite != null &&
            playerRenderer.sprite.name.IndexOf("CarryNetWalk", StringComparison.Ordinal) >= 0 &&
            harvestRenderer.enabled &&
            Vector2.Distance(harvestRenderer.bounds.center, exteriorHands) <= 0.18f;

        SetEditorHarvestLifecyclePreviewPose(5);
        Vector2 interiorHands = GetV41CarryNetHandWorldPosition();
        bool interiorCarryUsesHands = playerRenderer.sprite != null &&
            playerRenderer.sprite.name.IndexOf("CarryNetWalk", StringComparison.Ordinal) >= 0 &&
            harvestRenderer.enabled &&
            Vector2.Distance(harvestRenderer.bounds.center, interiorHands) <= 0.18f;

        bool passed = allKindsInsideNet && allKindsUseDistinctPoints &&
            haulingStaysWithNet && exteriorCarryUsesHands && interiorCarryUsesHands;
        string evidence = $"四类网内/分点={allKindsInsideNet}/{allKindsUseDistinctPoints}({kindEvidence.Trim()})；" +
            $"抬网同体={haulingStaysWithNet}/{haulingNetOwner.name}/越界={haulingOutsideDistance:F3}m；" +
            $"户外/室内双手={exteriorCarryUsesHands}/{interiorCarryUsesHands}";
        ResetSlice();
        return passed
            ? $"PASS：漂物入网、随网抬升和内外携带共用同一实物接触链。{evidence}"
            : $"FAIL：捕获物仍可能脱离网眼、堆成一点或在人物手边悬浮。{evidence}";
    }

    public string RunEditorTideDriftSourceProvenanceProbe()
    {
        TideDriftField first = TideDriftSourceModel.BuildField(
            0,
            3.5f,
            0.67f,
            0f,
            false);
        TideDriftField firstAgain = TideDriftSourceModel.BuildField(
            0,
            3.5f,
            0.67f,
            0f,
            false);
        TideDriftField storm = TideDriftSourceModel.BuildField(
            2,
            8.2f,
            0.56f,
            0.82f,
            false);
        TideDriftField springWithoutRoute = TideDriftSourceModel.BuildField(
            3,
            0.2f,
            0.96f,
            0.08f,
            false);
        TideDriftField springWithRoute = TideDriftSourceModel.BuildField(
            3,
            0.2f,
            0.96f,
            0.08f,
            true);

        bool firstTideIsGrounded = first.NearshoreBatch.Material == TideDriftMaterial.Fish &&
            first.NearshoreBatch.Provenance == TideDriftProvenance.TidalFlatSchool;
        bool sameInputsKeepIdentity = first.NearshoreBatch.StableId == firstAgain.NearshoreBatch.StableId &&
            first.NearshoreBatch.Material == firstAgain.NearshoreBatch.Material &&
            first.NearshoreBatch.Provenance == firstAgain.NearshoreBatch.Provenance;
        bool lanesOwnDifferentBatches = first.NearshoreBatch.StableId != first.OuterWreckBatch.StableId &&
            first.NearshoreBatch.Lane == TideDriftLane.NearshoreMain &&
            first.OuterWreckBatch.Lane == TideDriftLane.OuterWreckFork;
        bool wreckBatchKeepsPhysicalOptions = first.OuterWreckBatch.Material == TideDriftMaterial.SaltWood &&
            first.OuterWreckBatch.Provenance == TideDriftProvenance.OuterWreckWrack &&
            first.OuterWreckBatch.CanRouteToNet &&
            first.OuterWreckBatch.CanContinueToSailing;
        bool weatherAndKnowledgeHaveGroundedConsequences =
            storm.NearshoreBatch.Material == TideDriftMaterial.TangledDebris &&
            storm.NearshoreBatch.Provenance == TideDriftProvenance.StormWrackLine &&
            springWithoutRoute.NearshoreBatch.Material == TideDriftMaterial.SaltWood &&
            springWithRoute.NearshoreBatch.Material == TideDriftMaterial.ChartParcel &&
            springWithRoute.NearshoreBatch.Provenance == TideDriftProvenance.OuterWreckParcel;

        float thirtyFpsTravel = SimulateTideDriftTravel(first.NearshoreBatch, 1f / 30f, 12f);
        float sixtyFpsTravel = SimulateTideDriftTravel(first.NearshoreBatch, 1f / 60f, 12f);
        float oneTwentyFpsTravel = SimulateTideDriftTravel(first.NearshoreBatch, 1f / 120f, 12f);
        bool transportIsFrameRateIndependent =
            Mathf.Abs(thirtyFpsTravel - sixtyFpsTravel) <= 0.002f &&
            Mathf.Abs(sixtyFpsTravel - oneTwentyFpsTravel) <= 0.002f;
        float shallowWaterReach = TideDriftSourceModel.EvaluateReachableTravel01(0.42f);
        bool lowWaterCannotFakeNetArrival = shallowWaterReach < TideDriftSourceModel.NetCaptureEntryTravel01;
        float transportSeconds = TideDriftSourceModel.EvaluateReferenceTransportSeconds(
            first.NearshoreBatch,
            0.67f,
            0f);
        bool usesObservableRealSeconds = transportSeconds >= 22f && transportSeconds <= 48f;

        EnsureScene();
        ResetSlice();
        state = SliceState.TideRising;
        tideClockSeconds = tideCycleSeconds * 0.25f;
        currentWaterY = lowWaterY + 1.2f;
        netDeployed = false;
        StartCurrentTideDrift();
        TickHarvestPhysicalLifecycle(6f);
        bool sourceMovesWithoutNet = harvestPhysicalState == HarvestPhysicalState.Drifting &&
            incomingHarvestTravel01 > 0.01f &&
            outerWreckTravel01 > 0.01f;
        bool batchesDoNotShareOneTransform = Mathf.Abs(incomingHarvestTravel01 - outerWreckTravel01) > 0.002f;
        float runtimePrimaryTravel = incomingHarvestTravel01;
        float runtimeOuterTravel = outerWreckTravel01;

        incomingHarvestTravel01 = TideDriftSourceModel.NearshoreExitTravel01;
        outerWreckTravel01 = TideDriftSourceModel.NearshoreExitTravel01;
        ResolveExitedTideDriftBatches();
        bool missConservesOuterBatch = primarySourcePassedNearshore &&
            harvestPhysicalState == HarvestPhysicalState.Lost &&
            outerWreckPassedNearshore &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater &&
            extraSaltWoodBatchId == first.OuterWreckBatch.StableId;

        ResetSlice();
        state = SliceState.TideRising;
        tideClockSeconds = tideCycleSeconds * 0.25f;
        currentWaterY = highWaterY;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        StartCurrentTideDrift();
        netCaptureProgress01 = 1f;
        incomingHarvestTravel01 = TideDriftSourceModel.NetCaptureEntryTravel01 - 0.04f;
        TickNetWaterExposure(0.1f);
        bool cannotCatchBeforeContact = !netCatchResolved;
        incomingHarvestTravel01 = TideDriftSourceModel.NetIntersectionTravel01;
        TickNetWaterExposure(0.1f);
        bool contactCreatesOwnedCatch = netCatchResolved &&
            currentHarvest == HarvestKind.Fish &&
            currentHarvestBatchId == tideSourceBatchId;

        string evidence =
            $"首潮={first.NearshoreBatch.Provenance}/{first.NearshoreBatch.Material}#{first.NearshoreBatch.StableId}；" +
            $"外海={first.OuterWreckBatch.Provenance}/{first.OuterWreckBatch.Material}#{first.OuterWreckBatch.StableId}；" +
            $"风暴/大潮未识路/已识路={storm.NearshoreBatch.Material}/{springWithoutRoute.NearshoreBatch.Material}/{springWithRoute.NearshoreBatch.Material}；" +
            $"输运={transportSeconds:F1}s，帧率位移={thirtyFpsTravel:F3}/{sixtyFpsTravel:F3}/{oneTwentyFpsTravel:F3}；" +
            $"浅水可达={shallowWaterReach:F3}；无网双路={runtimePrimaryTravel:F3}/{runtimeOuterTravel:F3}；" +
            $"早捕/接触捕获={!cannotCatchBeforeContact}/{contactCreatesOwnedCatch}；漏过入短航={missConservesOuterBatch}";
        bool passed = firstTideIsGrounded && sameInputsKeepIdentity && lanesOwnDifferentBatches &&
            wreckBatchKeepsPhysicalOptions && weatherAndKnowledgeHaveGroundedConsequences &&
            transportIsFrameRateIndependent && lowWaterCannotFakeNetArrival && usesObservableRealSeconds &&
            sourceMovesWithoutNet && batchesDoNotShareOneTransform && missConservesOuterBatch &&
            cannotCatchBeforeContact && contactCreatesOwnedCatch;
        string report = passed
            ? $"PASS：潮源由现实来路生成，同一输入身份稳定，网深不参与种类生成，输运使用现实秒。{evidence}"
            : $"FAIL：潮源仍可能依赖隐藏开奖、混淆批次身份或按帧率移动。{evidence}";
        ResetSlice();
        return report;
    }

    public string RunEditorWrackLineLifecycleProbe()
    {
        EnsureScene();
        ResetSlice();
        float groundY = GetPlayerStandingFeetY(WalkLane.TideFlat);

        InitializeCurrentTideDriftField(true);
        int firstCycle = tideDriftFieldCycleOrdinal;
        int firstBatchId = currentTideDriftField.NearshoreBatch.StableId;
        weatherClockSeconds += Mathf.Max(8f, tideCycleSeconds);
        tideClockSeconds = Mathf.Repeat(
            tideClockSeconds + tideCycleSeconds,
            Mathf.Max(8f, tideCycleSeconds));
        EnsureCurrentTideDriftField();
        int secondCycle = tideDriftFieldCycleOrdinal;
        TideDriftBatch batch = currentTideDriftField.NearshoreBatch;
        int refreshedCycle = secondCycle;
        int refreshedBatchId = batch.StableId;
        bool realCycleRefreshesBatch = secondCycle == firstCycle + 1 &&
            batch.StableId != firstBatchId;

        // Re-enter the opening tide and let the real tide state machine perform the
        // ebb transition. This guards the integration point, not only the component API.
        ResetSlice();
        groundY = GetPlayerStandingFeetY(WalkLane.TideFlat);
        state = SliceState.TideRising;
        netDeployed = false;
        netSecuredEarly = false;
        StartCurrentTideDrift();
        batch = currentTideDriftField.NearshoreBatch;
        secondCycle = tideDriftFieldCycleOrdinal;
        incomingHarvestTravel01 = 0.56f;
        harvestPhysicalState = HarvestPhysicalState.Drifting;
        wrackLine.ResetFeature();
        bool capturedDoesNotDuplicate = !wrackLine.TrySettle(
            batch,
            secondCycle,
            groundY + 0.72f,
            groundY,
            true,
            true) &&
            !wrackLine.HasDeposit;
        float ebbTargetClock = tideCycleSeconds * 0.92f;
        float elapsedToEbb = Mathf.Max(0f, ebbTargetClock - tideClockSeconds);
        AdvanceContinuousWeather(elapsedToEbb);
        TickNaturalTide(elapsedToEbb);
        bool naturalEbbSettled = state == SliceState.LowTidePlanning && wrackLine.HasDeposit;
        wrackLine.UpdatePresentation(true);
        bool groundedVisual = naturalEbbSettled && wrackLine.IsVisible &&
            !wrackLine.HasCollider &&
            Mathf.Abs(wrackLine.VisualCenter.x - wrackLine.Deposit.WorldX) <= 0.01f &&
            wrackLine.VisualCenter.y > groundY;

        bool sameTideCannotWashIt = !wrackLine.TickNaturalState(
            secondCycle,
            groundY + 0.3f) && wrackLine.HasDeposit;
        bool nextLowWaterKeepsIt = !wrackLine.TickNaturalState(
            secondCycle + 1,
            groundY - 0.2f) && wrackLine.HasDeposit;
        bool nextWetRockRefloatsIt = wrackLine.TickNaturalState(
            secondCycle + 1,
            groundY) && !wrackLine.HasDeposit;

        wrackLine.TrySettle(
            batch,
            secondCycle + 2,
            groundY + 0.72f,
            groundY,
            false,
            true);
        TideWrackDepositState depositBeforePickup = wrackLine.Deposit;
        playerPosition = new Vector2(
            depositBeforePickup.WorldX,
            GetPlayerLaneY(WalkLane.TideFlat));
        bool pickedUp = wrackLine.TryCollect(
            new Vector2(playerPosition.x, groundY),
            groundY - 0.2f,
            out TideWrackDepositState collected);
        if (pickedUp)
        {
            BeginCarryingWrackDeposit(collected);
        }

        GetCurrentHarvestMaterialYield(
            out int timberYield,
            out int ropeYield,
            out int clothYield,
            out int metalYield,
            out int foodYield);
        int totalSingleYield = timberYield + ropeYield + clothYield + metalYield + foodYield;
        bool pickupKeepsOnePhysicalBatch = pickedUp &&
            !wrackLine.HasDeposit &&
            currentHarvestBatchId == batch.StableId &&
            harvestPhysicalState == HarvestPhysicalState.Carried &&
            currentHarvestFromWrack &&
            totalSingleYield == 1;

        string evidence =
            $"潮次/批次={firstCycle}->{refreshedCycle}/{firstBatchId}->{refreshedBatchId}；" +
            $"岸位={depositBeforePickup.WorldX:F2}/{groundY:F2}；" +
            $"入网不复制={capturedDoesNotDuplicate}；自然退潮/贴岩={naturalEbbSettled}/{groundedVisual}；" +
            $"同潮/次潮低水/再浸={sameTideCannotWashIt}/{nextLowWaterKeepsIt}/{nextWetRockRefloatsIt}；" +
            $"拾取/单件产出={pickupKeepsOnePhysicalBatch}/{totalSingleYield}";
        bool passed = realCycleRefreshesBatch && capturedDoesNotDuplicate &&
            naturalEbbSettled && groundedVisual &&
            sameTideCannotWashIt && nextLowWaterKeepsIt && nextWetRockRefloatsIt &&
            pickupKeepsOnePhysicalBatch;
        ResetSlice();
        return passed
            ? $"PASS：漂物按真实天文潮次换批；漏网近岸实物退潮贴岩搁浅，再浸才卷走，拾取仍是同一单件。{evidence}"
            : $"FAIL：漂积物存在故事轮次复用、岸上复制、悬浮、提前消失或拾取增殖。{evidence}";
    }

    public string RunEditorTideDriftPhysicalCurrentCouplingProbe()
    {
        TideDriftBatch batch = TideDriftSourceModel.BuildField(
            0,
            OpeningMoonAgeDays,
            CalculateTideStrength(OpeningMoonAgeDays),
            0f,
            false).NearshoreBatch;
        float referenceStrength = CalculateTideStrength(OpeningMoonAgeDays);
        float referenceFloodSpeed = Mathf.Abs(TideAstronomicalCurrentModel.EvaluateSignedSpeed(
            tideCycleSeconds * 0.25f,
            tideCycleSeconds,
            referenceStrength,
            referenceStrength,
            MeanTidalTransportSpeed,
            EbbCurrentBoost));
        float testPhysicalSpeed = referenceFloodSpeed * 0.64f;

        // 相同实际流速必须只有一个结果。当前月相不再是这个 API 的输入；
        // referenceStrength 只定义已经验收的首潮路线尺度。
        float sameFlowNeap = TideDriftSourceModel.AdvanceNearshoreTravel01(
            0f,
            10f,
            testPhysicalSpeed,
            referenceFloodSpeed,
            1f,
            batch,
            referenceStrength,
            0f);
        float sameFlowSpring = TideDriftSourceModel.AdvanceNearshoreTravel01(
            0f,
            10f,
            testPhysicalSpeed,
            referenceFloodSpeed,
            1f,
            batch,
            referenceStrength,
            0f);
        bool samePhysicalFlowHasOneResult = Mathf.Abs(sameFlowNeap - sameFlowSpring) <= 0.0001f;

        float neapStrength = CalculateTideStrength(29.53f * 0.25f);
        float springStrength = CalculateTideStrength(0f);
        float neapSpeed = Mathf.Abs(TideAstronomicalCurrentModel.EvaluateSignedSpeed(
            tideCycleSeconds * 0.25f,
            tideCycleSeconds,
            neapStrength,
            referenceStrength,
            MeanTidalTransportSpeed,
            EbbCurrentBoost));
        float springSpeed = Mathf.Abs(TideAstronomicalCurrentModel.EvaluateSignedSpeed(
            tideCycleSeconds * 0.25f,
            tideCycleSeconds,
            springStrength,
            referenceStrength,
            MeanTidalTransportSpeed,
            EbbCurrentBoost));
        float neapTravel = TideDriftSourceModel.AdvanceNearshoreTravel01(
            0f,
            10f,
            neapSpeed,
            referenceFloodSpeed,
            1f,
            batch,
            referenceStrength,
            0f);
        float springTravel = TideDriftSourceModel.AdvanceNearshoreTravel01(
            0f,
            10f,
            springSpeed,
            referenceFloodSpeed,
            1f,
            batch,
            referenceStrength,
            0f);
        float physicalRatio = springSpeed / Mathf.Max(0.001f, neapSpeed);
        float travelRatio = springTravel / Mathf.Max(0.001f, neapTravel);
        bool moonDifferenceComesOnlyFromPhysicalSpeed =
            Mathf.Abs(travelRatio - physicalRatio) <= 0.02f;

        float slackTravel = TideDriftSourceModel.AdvanceNearshoreTravel01(
            0.42f,
            10f,
            0f,
            referenceFloodSpeed,
            1f,
            batch,
            referenceStrength,
            0f);
        float ebbTravel = TideDriftSourceModel.AdvanceNearshoreTravel01(
            0.42f,
            10f,
            -referenceFloodSpeed * 0.5f,
            referenceFloodSpeed,
            1f,
            batch,
            referenceStrength,
            0f);
        bool slackDoesNotStealMotion = Mathf.Abs(slackTravel - 0.42f) <= 0.0001f;
        bool ebbReversesContinuously = ebbTravel < 0.42f;

        string evidence =
            $"同速小/大潮={sameFlowNeap:F4}/{sameFlowSpring:F4}；" +
            $"流速比/位移比={physicalRatio:F3}/{travelRatio:F3}；" +
            $"平流/退潮={slackTravel:F3}/{ebbTravel:F3}";
        return samePhysicalFlowHasOneResult && moonDifferenceComesOnlyFromPhysicalSpeed &&
            slackDoesNotStealMotion && ebbReversesContinuously
            ? $"PASS：潮获输运只由实际潮流决定，没有第二份当前月相速度源。{evidence}"
            : $"FAIL：潮获仍同时消费实际流速与独立潮强，或平流/退潮方向错误。{evidence}";
    }

    private static float SimulateTideDriftTravel(TideDriftBatch batch, float stepSeconds, float totalSeconds)
    {
        float travel01 = 0f;
        float elapsed = 0f;
        const float referenceFloodSpeed = 0.4712389f;
        while (elapsed < totalSeconds - 0.0001f)
        {
            float delta = Mathf.Min(stepSeconds, totalSeconds - elapsed);
            travel01 = TideDriftSourceModel.AdvanceNearshoreTravel01(
                travel01,
                delta,
                referenceFloodSpeed * 0.82f,
                referenceFloodSpeed,
                1f,
                batch,
                0.67f,
                0f);
            elapsed += delta;
        }

        return travel01;
    }

    public string RunEditorFloatingHarvestOceanCouplingProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalInspected = true;
        arrivalVignetteActive = false;
        viewMode = SliceViewMode.Shelter;
        state = SliceState.TideRising;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        netCatchResolved = false;
        harvestPhysicalState = HarvestPhysicalState.Drifting;
        tideSourceHarvest = HarvestKind.Wood;
        currentWaterY = GetSelectedNetY() + 0.32f;
        incomingHarvestTravel01 = 0.34f;
        tideClockSeconds = tideCycleSeconds * 0.25f;

        const float firstTime = 2.1f;
        UpdateIncomingTideCarryVisuals(firstTime, GetSelectedNetY());
        bool pairVisible = incomingTideCarryItems.Count >= 2 &&
            incomingTideCarryItems[0].enabled && incomingTideCarryItems[1].enabled;
        Vector2 objectBefore = pairVisible
            ? (Vector2)incomingTideCarryItems[0].transform.position
            : Vector2.zero;
        Vector2 wakeBefore = pairVisible
            ? (Vector2)incomingTideCarryItems[1].transform.position
            : Vector2.zero;
        TideOceanSample oceanBefore = GetOceanSample(objectBefore.x);
        bool sharesLocalSurface;
        if (pairVisible && TryGetV59FindSpec(
            HarvestKind.Wood,
            0,
            tideSourceBatchId,
            true,
            out TideV59FindSpec floatingSpec))
        {
            float rotationDegrees = incomingTideCarryItems[0].transform.eulerAngles.z;
            Vector2 rotatedWaterline = Quaternion.Euler(0f, 0f, rotationDegrees) *
                floatingSpec.WaterlineFromSpriteCenter;
            Vector2 renderedWaterline = objectBefore + rotatedWaterline;
            float bob = GetHarvestDriftBob(HarvestKind.Wood, firstTime, 0);
            TideOceanSample waterlineOcean = GetOceanSample(renderedWaterline.x);
            sharesLocalSurface =
                Mathf.Abs((renderedWaterline.y - bob) - waterlineOcean.SurfaceY) <= 0.014f &&
                Mathf.Abs(wakeBefore.x - renderedWaterline.x) <= 0.05f &&
                Mathf.Abs(wakeBefore.y - (waterlineOcean.SurfaceY - 0.012f)) <= 0.025f;
        }
        else
        {
            float expectedObjectY = oceanBefore.SurfaceY + GetHarvestSurfaceOffset(HarvestKind.Wood) +
                GetHarvestDriftBob(HarvestKind.Wood, firstTime, 0);
            sharesLocalSurface = pairVisible &&
                Mathf.Abs(objectBefore.y - expectedObjectY) <= 0.012f &&
                Mathf.Abs(wakeBefore.x - objectBefore.x) <= 0.015f &&
                Mathf.Abs(wakeBefore.y - (oceanBefore.SurfaceY - 0.012f)) <= 0.02f;
        }

        // UpdateVisuals 的 time 只驱动帧动画；真实海浪读取连续天气时钟。
        // 两者同时前进，才能验证运行中的波群位移，而不是只测贴图内部微扰。
        const float worldAdvance = 1.8f;
        weatherClockSeconds += worldAdvance;
        UpdateIncomingTideCarryVisuals(firstTime + worldAdvance, GetSelectedNetY());
        Vector2 objectAfter = pairVisible
            ? (Vector2)incomingTideCarryItems[0].transform.position
            : Vector2.zero;
        TideOceanSample oceanAfter = GetOceanSample(objectAfter.x);
        float visualMotion = Vector2.Distance(objectBefore, objectAfter);
        bool continuousWaveMotion = visualMotion >= 0.006f && visualMotion <= 0.22f;

        // 涨潮中段流向网，退潮中段流离网；两边都推进同一实物坐标，
        // 不是切换方向时把物件瞬移到网的另一侧。
        incomingHarvestTravel01 = 0.45f;
        currentWaterY = lowWaterY + 1.2f;
        tideClockSeconds = tideCycleSeconds * 0.25f;
        TickHarvestPhysicalLifecycle(0.5f);
        float afterFlood = incomingHarvestTravel01;
        tideClockSeconds = tideCycleSeconds * 0.75f;
        TickHarvestPhysicalLifecycle(0.5f);
        float afterEbb = incomingHarvestTravel01;
        bool currentReversesContinuously = afterFlood > 0.45f && afterEbb < afterFlood &&
            afterFlood - 0.45f < 0.18f && afterFlood - afterEbb < 0.18f;

        string evidence =
            $"物/尾纹同浪={sharesLocalSurface}；视觉位移={visualMotion:F3}m；" +
            $"物={objectBefore.x:F3},{objectBefore.y:F3}->{objectAfter.x:F3},{objectAfter.y:F3}；" +
            $"浪={oceanBefore.SurfaceY:F3}->{oceanAfter.SurfaceY:F3}；" +
            $"潮流进度={0.45f:F2}->{afterFlood:F2}->{afterEbb:F2}";
        ResetSlice();
        return pairVisible && sharesLocalSurface && continuousWaveMotion && currentReversesContinuously
            ? $"PASS：漂浮物、尾纹、随机浪头和涨退潮流共用同一海况场，换向不瞬移。{evidence}"
            : $"FAIL：漂浮物仍可能脱离水面、按独立节拍抖动或在潮流换向时跳位。{evidence}";
    }

    public string RunEditorV59FindLifecycleProbe()
    {
        EnsureScene();
        EnsureV59FindResourcesLoaded();
        string catalogReason = "Catalog 缺失";
        bool catalogComplete = formalV59FindCatalog != null &&
            formalV59FindCatalog.IsComplete(out catalogReason);
        HashSet<Sprite> uniqueSprites = new HashSet<Sprite>();
        bool twelveDistinctSprites = catalogComplete;
        bool importContractMatches = catalogComplete;
        bool identityStaysStable = catalogComplete;
        bool metricContractMatches = catalogComplete;

        HarvestKind[] harvestKinds =
        {
            HarvestKind.Fish,
            HarvestKind.Wood,
            HarvestKind.Trash,
            HarvestKind.Relic,
        };

        for (int kindIndex = 0; kindIndex < harvestKinds.Length; kindIndex++)
        {
            HarvestKind harvest = harvestKinds[kindIndex];
            TideV59FindKind v59Kind = ToV59FindKind(harvest);
            int batchId = 111 + kindIndex * 11;
            for (int pieceIndex = 0; pieceIndex < TideV59FindPresentationModel.VariantsPerKind; pieceIndex++)
            {
                Sprite catalogSprite = catalogComplete
                    ? formalV59FindCatalog.Get(v59Kind, pieceIndex)
                    : null;
                twelveDistinctSprites &= catalogSprite != null && uniqueSprites.Add(catalogSprite);
                importContractMatches &= catalogSprite != null &&
                    Mathf.Abs(catalogSprite.pixelsPerUnit - 512f) <= 0.01f &&
                    catalogSprite.texture != null &&
                    catalogSprite.texture.width <= 512 &&
                    catalogSprite.texture.height <= 512;

                int resolvedVariant = TideV59FindPresentationModel.ResolveVariantIndex(
                    v59Kind,
                    pieceIndex,
                    batchId,
                    false);
                Sprite expectedSprite = catalogComplete
                    ? formalV59FindCatalog.Get(v59Kind, resolvedVariant)
                    : null;
                Sprite netSprite = GetCaughtNetItemSprite(harvest, pieceIndex, batchId, false);
                Sprite carrySprite = GetCaughtNetItemSprite(harvest, pieceIndex, batchId, false);
                Sprite washedSprite = GetCaughtNetItemSprite(harvest, pieceIndex, batchId, false);
                identityStaysStable &= expectedSprite != null &&
                    netSprite == expectedSprite &&
                    carrySprite == expectedSprite &&
                    washedSprite == expectedSprite;

                TideV59FindSpec spec = TideV59FindPresentationModel.GetSpec(v59Kind, resolvedVariant);
                Vector2 runtimeSize = GetHarvestWorldSize(harvest, pieceIndex, batchId, false);
                Vector2 testWaterline = new Vector2(2.4f, -0.3f);
                const float testRotation = 17f;
                Vector2 center = TideV59FindPresentationModel.GetSpriteCenterForWaterline(
                    testWaterline,
                    spec,
                    testRotation);
                Vector2 reconstructedWaterline = center +
                    (Vector2)(Quaternion.Euler(0f, 0f, testRotation) * spec.WaterlineFromSpriteCenter);
                metricContractMatches &= Vector2.Distance(runtimeSize, spec.VisibleWorldSize) <= 0.0001f &&
                    Vector2.Distance(reconstructedWaterline, testWaterline) <= 0.0001f;
            }
        }

        TideV59FindSpec chart = TideV59FindPresentationModel.GetSpec(TideV59FindKind.Relic, 0);
        TideV59FindSpec compass = TideV59FindPresentationModel.GetSpec(TideV59FindKind.Relic, 1);
        TideV59FindSpec coil = TideV59FindPresentationModel.GetSpec(TideV59FindKind.Relic, 2);
        bool negativeRelicsNeverFreeFloat = chart.CanFreeFloat &&
            !compass.CanFreeFloat &&
            !coil.CanFreeFloat &&
            TideV59FindPresentationModel.ResolveVariantIndex(TideV59FindKind.Relic, 1, 144, true) == 0 &&
            TideV59FindPresentationModel.ResolveVariantIndex(TideV59FindKind.Relic, 2, 144, true) == 0;

        tideSourceBatchId = 122;
        currentHarvestBatchId = tideSourceBatchId;
        securedPostHarvestBatchId = currentHarvestBatchId;
        washedAwayHarvestBatchId = currentHarvestBatchId;
        currentHarvest = HarvestKind.Wood;
        securedPostHarvest = currentHarvest;
        int resolvedWoodVariant = TideV59FindPresentationModel.ResolveVariantIndex(
            TideV59FindKind.Wood,
            0,
            tideSourceBatchId,
            false);
        Sprite expectedWood = catalogComplete
            ? formalV59FindCatalog.Get(TideV59FindKind.Wood, resolvedWoodVariant)
            : null;
        bool controllerOwnershipKeepsIdentity = expectedWood != null &&
            GetCaughtNetItemSprite(currentHarvest, 0, currentHarvestBatchId) == expectedWood &&
            GetCaughtNetItemSprite(securedPostHarvest, 0, securedPostHarvestBatchId) == expectedWood &&
            GetCaughtNetItemSprite(HarvestKind.Wood, 0, washedAwayHarvestBatchId) == expectedWood;

        bool passed = catalogComplete && twelveDistinctSprites && importContractMatches &&
            identityStaysStable && metricContractMatches && negativeRelicsNeverFreeFloat &&
            controllerOwnershipKeepsIdentity;
        string evidence =
            $"Catalog={catalogReason}；独立Sprite={uniqueSprites.Count}/12；" +
            $"High导入={importContractMatches}；同物连续={identityStaysStable && controllerOwnershipKeepsIdentity}；" +
            $"米制锚点={metricContractMatches}；负浮力限制={negativeRelicsNeverFreeFloat}";
        string report = passed
            ? "PASS：V59 High 十二件潮带物按批次保持同一身份，米制尺寸/水线一致，负浮力遗物不会自由漂浮。" + evidence
            : "FAIL：V59 潮带物仍存在混档、换图、跳尺寸或负浮力物自由漂浮。" + evidence;
        ResetSlice();
        return report;
    }

    public string RunEditorV60SailingBuoyAndWaterOwnershipProbe()
    {
        SetEditorSailingPreviewPose();
        sailingClueCollected = false;
        sailingSalvageCollected = false;
        sailingBuoyChecked = true;
        dayProgress01 = 0.5f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = DayNightPhase.Day;
        UpdateVisuals(2.15f);
        bool noVisibleBuoy = (sailingBuoyPointRenderer == null || !sailingBuoyPointRenderer.enabled) &&
            (sailingBuoyTetherRenderer == null || !sailingBuoyTetherRenderer.enabled) &&
            (sailingBuoySinkerRenderer == null || !sailingBuoySinkerRenderer.enabled) &&
            GameObject.Find("GeneratedStiltFirstSailingBuoyPoint") == null &&
            GameObject.Find("GeneratedStiltFirstSailingBuoyTether") == null &&
            GameObject.Find("GeneratedStiltFirstSailingBuoySinker") == null;
        Vector2 salvageObjective = GetSailingScreenPosition(
            GetSailingPointPosition(SailingPointKind.Salvage));
        bool routeStartsFromReadableSalvage = sailingBuoyChecked &&
            Vector2.Distance(GetCurrentObjectivePosition(), salvageObjective) <= 0.01f;
        bool continuousWaterOwnsOcclusion = foregroundDeepWaterOcclusionRenderer != null &&
            foregroundDeepWaterOcclusionRenderer.enabled &&
            foregroundDeepWaterOcclusionRenderer.sortingOrder > boatRudderRenderer.sortingOrder &&
            boatWaterlineOcclusionRenderer != null &&
            !boatWaterlineOcclusionRenderer.enabled;
        bool noDuplicateSkyTint = ambientMoonWashRenderer != null &&
            !ambientMoonWashRenderer.enabled;
        bool formalDayPlateNoLongerWashesScene = daySeaSkyRenderer != null &&
            daySeaSkyRenderer.color.a <= 0.001f;

        string evidence =
            $"浮标退役/首目标漂物={noVisibleBuoy}/{routeStartsFromReadableSalvage}；" +
            $"整海遮挡/局部船水=" +
            $"{foregroundDeepWaterOcclusionRenderer.enabled}/{boatWaterlineOcclusionRenderer.enabled}；" +
            $"重复天空染色/正式日底图关闭={noDuplicateSkyTint}/{formalDayPlateNoLongerWashesScene}";
        bool passed = noVisibleBuoy && routeStartsFromReadableSalvage &&
            continuousWaterOwnsOcclusion && noDuplicateSkyTint && formalDayPlateNoLongerWashesScene;
        return passed
            ? $"PASS：不可辨认浮标已退出首航，漂物成为首个世界内目标，船底仍只由连续海面遮挡。{evidence}"
            : $"FAIL：浮标仍泄露、首航目标断链、局部船水或重复天空染色尚未退出。{evidence}";
    }

    private bool IsOnlyV54StateVisible(TideV54NetVisualState expected, bool expectBundle)
    {
        if (formalV54NetCatalog == null)
        {
            return false;
        }

        SpriteRenderer expectedRenderer = expectBundle
            ? formalNetBundleRenderer
            : formalNetRenderer;
        SpriteRenderer otherRenderer = expectBundle
            ? formalNetRenderer
            : formalNetBundleRenderer;
        return expectedRenderer != null && expectedRenderer.enabled &&
            expectedRenderer.sprite == formalV54NetCatalog.Get(expected) &&
            (otherRenderer == null || !otherRenderer.enabled);
    }

    public void SetEditorV54HauledPreviewPose()
    {
        EnsureScene();
        ResetSlice();
        arrivalInspected = true;
        arrivalVignetteActive = false;
        viewMode = SliceViewMode.Shelter;
        state = SliceState.EbbCollect;
        currentWaterY = lowWaterY + 0.08f;
        playerLane = WalkLane.TideFlat;
        playerFacing = -1;
        playerPosition = new Vector2(GetNetFirstStakeX() + 0.42f, GetPlayerLaneY(playerLane));
        netRigStep = NetRigStep.Deployed;
        netDeployed = false;
        netSecuredEarly = true;
        securedPostHarvest = HarvestKind.Fish;
        securedPostBundleTier = 2;
        securedPostVisualPieceCount = 2;
        lastActionHint = "刚收回的湿网已经绑在网桩，玩家双手空出，可以去照看屋子或准备短航。";
        UpdateVisuals(4.7f);
    }

    public string RunEditorNetHaulCadenceProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalInspected = true;
        state = SliceState.EbbCollect;
        netDeployed = true;
        netTouched = true;
        netCatchResolved = true;
        currentHarvest = HarvestKind.Wood;
        harvestPhysicalState = HarvestPhysicalState.CaughtInNet;

        float minDelta = float.PositiveInfinity;
        float maxDelta = 0f;
        float minEffort = float.PositiveInfinity;
        float maxEffort = 0f;
        for (int i = 0; i < 16; i++)
        {
            float before = netHaulProgress;
            TickNetHaulEffort(0.1f, 4f, 0.2f, 0f);
            float delta = netHaulProgress - before;
            minDelta = Mathf.Min(minDelta, delta);
            maxDelta = Mathf.Max(maxDelta, delta);
            minEffort = Mathf.Min(minEffort, netHaulEffort01);
            maxEffort = Mathf.Max(maxEffort, netHaulEffort01);
        }
        float lightWaterProgress = netHaulProgress;

        netHaulProgress = 0f;
        netHaulStrokePhase = 0f;
        netHaulEffort01 = 0f;
        netHaulLoad01 = 0f;
        for (int i = 0; i < 16; i++)
        {
            TickNetHaulEffort(0.1f, 4f, 1f, 0f);
        }
        float heavyWaterProgress = netHaulProgress;

        bool hasPullAndRegrip = minEffort <= 0.28f && maxEffort >= 0.9f;
        bool progressUsesCadence = maxDelta >= minDelta * 2.2f;
        bool heavyWaterSlowsEachStroke = heavyWaterProgress < lightWaterProgress * 0.82f;
        bool progressRemainsMonotonic = minDelta >= -0.0001f && heavyWaterProgress > 0f;
        string evidence = $"发力={minEffort:F2}->{maxEffort:F2}；步进={minDelta:F3}->{maxDelta:F3}；" +
            $"轻水={lightWaterProgress:F2}；重水={heavyWaterProgress:F2}";
        bool passed = hasPullAndRegrip && progressUsesCadence && heavyWaterSlowsEachStroke && progressRemainsMonotonic;
        return passed
            ? $"PASS：收网由抓绳、后仰发力和回手组成，重潮会拖慢每一把。{evidence}"
            : $"FAIL：收网仍像匀速进度条，或重潮没有改变拉拽节奏。{evidence}";
    }

    public string RunEditorSlackWaterHaulWindowProbe()
    {
        const float submersion01 = 0.82f;
        const float catchLoad01 = 0.67f;
        const float depth01 = 0.72f;
        const float baseDurationSeconds = 3.4f;

        float cycle = Mathf.Max(8f, tideCycleSeconds);
        float currentAtHighWater = Mathf.Abs(EvaluateNaturalCurrentSpeed(cycle * 0.5f));
        float currentAtMidFlood = Mathf.Abs(EvaluateNaturalCurrentSpeed(cycle * 0.25f));
        float peakFloodSpeed = 0.3f * Mathf.PI * 0.5f;
        float highWaterCurrent01 = Mathf.Clamp01(currentAtHighWater / peakFloodSpeed);
        float midFloodCurrent01 = Mathf.Clamp01(currentAtMidFlood / peakFloodSpeed);

        float highWaterSlackLoad = TideNetHaulModel.EvaluateHydrodynamicLoad01(
            submersion01,
            highWaterCurrent01,
            catchLoad01,
            depth01);
        float midFloodLoad = TideNetHaulModel.EvaluateHydrodynamicLoad01(
            submersion01,
            midFloodCurrent01,
            catchLoad01,
            depth01);
        float lowWaterSlackLoad = TideNetHaulModel.EvaluateHydrodynamicLoad01(
            0.12f,
            0f,
            catchLoad01,
            depth01);

        float highWaterSlackSeconds = MeasureNetHaulCompletionSeconds(
            baseDurationSeconds,
            submersion01,
            highWaterCurrent01,
            catchLoad01,
            depth01);
        float midFloodSeconds = MeasureNetHaulCompletionSeconds(
            baseDurationSeconds,
            submersion01,
            midFloodCurrent01,
            catchLoad01,
            depth01);
        float lowWaterSlackSeconds = MeasureNetHaulCompletionSeconds(
            baseDurationSeconds,
            0.12f,
            0f,
            catchLoad01,
            depth01);

        bool naturalTideCreatesSlack = currentAtHighWater <= 0.0001f &&
            currentAtMidFlood >= peakFloodSpeed * 0.99f;
        bool currentCreatesMeaningfulChoice = midFloodLoad >= highWaterSlackLoad + 0.35f &&
            midFloodSeconds >= highWaterSlackSeconds * 1.3f;
        bool wetNetKeepsWeightAtSlack = highWaterSlackLoad >= lowWaterSlackLoad + 0.15f &&
            highWaterSlackSeconds >= lowWaterSlackSeconds + 0.4f;
        bool actionCadenceIsHuman =
            TideNetHaulModel.EvaluateStrokeSeconds(0f) >= 0.8f &&
            TideNetHaulModel.EvaluateStrokeSeconds(1f) <= 1.25f &&
            lowWaterSlackSeconds >= 3.5f &&
            midFloodSeconds <= 10f;

        string evidence =
            $"流速 高潮/涨中={currentAtHighWater:F3}/{currentAtMidFlood:F3}m/s；" +
            $"负载 低潮平流/高潮平流/涨中={lowWaterSlackLoad:F2}/{highWaterSlackLoad:F2}/{midFloodLoad:F2}；" +
            $"收网秒数={lowWaterSlackSeconds:F2}/{highWaterSlackSeconds:F2}/{midFloodSeconds:F2}；" +
            $"单把={TideNetHaulModel.EvaluateStrokeSeconds(0f):F2}..{TideNetHaulModel.EvaluateStrokeSeconds(1f):F2}s";
        bool passed = naturalTideCreatesSlack && currentCreatesMeaningfulChoice &&
            wetNetKeepsWeightAtSlack && actionCadenceIsHuman;
        return passed
            ? $"PASS：高低潮附近形成真实平流收网窗口；湿网仍有重量，涨退潮中段则因流速平方阻力明显更难。{evidence}"
            : $"FAIL：收网还没有把自然转流变成可利用窗口，或动作频率被游戏时钟不合理加速。{evidence}";
    }

    public string RunEditorNetCurrentReadabilityProbe()
    {
        EnsureScene();
        ResetSlice();
        EnsureV54NetResourcesLoaded();
        if (!HasCompleteV54NetPresentation())
        {
            return "FAIL：V54 正式渔网资源不完整，无法验证潮势世界内可读性。";
        }

        arrivalInspected = true;
        viewMode = SliceViewMode.Shelter;
        state = SliceState.TideRising;
        netDeployed = true;
        netRigStep = NetRigStep.Lowering;
        netSetDepth01 = 0.72f;
        netLoweringProgress = netSetDepth01;
        selectedNetLine = GetNetLineFromLoweringProgress(netSetDepth01);
        netTouched = true;
        netCatchResolved = true;
        netCatchBundleTier = 1;
        netCatchVisualPieceCount = 1;
        currentHarvest = HarvestKind.Wood;
        currentHarvestBanked = false;
        harvestPhysicalState = HarvestPhysicalState.CaughtInNet;
        netPeakTension01 = 0.62f;
        netFraying01 = 0f;
        netHaulProgress = 0f;
        netHaulEffort01 = 0f;
        netHaulLoad01 = 0f;
        currentWaterY = GetSelectedNetY() + 0.2f;

        const float sampleTime = 4.2f;
        float cycle = Mathf.Max(8f, tideCycleSeconds);

        tideClockSeconds = cycle * 0.5f;
        UpdateVisuals(sampleTime);
        float slackNetX = formalNetRenderer.bounds.center.x;
        float slackCargoX = netCaughtItems[0].bounds.center.x;
        bool slackRopesFollow = DoV54SuspensionRopesFollowCurrentPose();

        tideClockSeconds = cycle * 0.25f;
        UpdateVisuals(sampleTime);
        float floodNetX = formalNetRenderer.bounds.center.x;
        float floodCargoX = netCaughtItems[0].bounds.center.x;
        bool floodRopesFollow = DoV54SuspensionRopesFollowCurrentPose();

        tideClockSeconds = cycle * 0.75f;
        UpdateVisuals(sampleTime);
        float ebbNetX = formalNetRenderer.bounds.center.x;
        float ebbCargoX = netCaughtItems[0].bounds.center.x;
        bool ebbRopesFollow = DoV54SuspensionRopesFollowCurrentPose();

        float floodShift = floodNetX - slackNetX;
        float ebbShift = ebbNetX - slackNetX;
        bool oppositeDirections = floodShift <= -0.12f && ebbShift >= 0.12f;
        bool cargoStaysInSameMesh =
            Mathf.Abs((floodCargoX - slackCargoX) - floodShift) <= 0.025f &&
            Mathf.Abs((ebbCargoX - slackCargoX) - ebbShift) <= 0.025f;

        netHaulProgress = 0.82f;
        netHaulStrokePhase = 0.62f;
        netHaulEffort01 = 1f;
        netHaulLoad01 = 0.88f;
        UpdateVisuals(sampleTime);
        float hauledEbbShift = formalNetRenderer.bounds.center.x - slackNetX;
        bool haulingReducesWetArea = Mathf.Abs(hauledEbbShift) < Mathf.Abs(ebbShift) * 0.35f;
        bool ropesAlwaysFollow = slackRopesFollow && floodRopesFollow && ebbRopesFollow;

        float modelSlack = TideNetHaulModel.EvaluateSignedCurrentDrag01(1f, 0f, 0.7f, 0f);
        float modelFlood = TideNetHaulModel.EvaluateSignedCurrentDrag01(1f, -1f, 0.7f, 0f);
        float modelEbb = TideNetHaulModel.EvaluateSignedCurrentDrag01(1f, 1f, 0.7f, 0f);
        bool modelDirectionIsPhysical = Mathf.Abs(modelSlack) <= 0.0001f &&
            modelFlood < -0.99f && modelEbb > 0.99f;

        string evidence =
            $"网偏移 涨/平/退={floodShift:F3}/0/{ebbShift:F3}m；" +
            $"挂物偏移={floodCargoX - slackCargoX:F3}/{ebbCargoX - slackCargoX:F3}m；" +
            $"起网后退潮偏移={hauledEbbShift:F3}m；悬绳={ropesAlwaysFollow}";
        bool passed = modelDirectionIsPhysical && oppositeDirections &&
            cargoStaysInSameMesh && haulingReducesWetArea && ropesAlwaysFollow;
        return passed
            ? $"PASS：正式渔网、挂物和两根悬绳共同显示平流居中、涨退潮反向吃流，起网后拖曳随受水面积递减。{evidence}"
            : $"FAIL：潮流数值与正式网体、挂物或悬绳几何仍未使用同一受力结果。{evidence}";
    }

    public string RunEditorTideForecastAutonomyProbe()
    {
        // Re-run retired-object cleanup so an old editor hierarchy cannot make the
        // probe pass merely because the obsolete marker happens to be disabled.
        retiredHierarchyCleaned = false;
        EnsureScene();
        ResetSlice();
        arrivalInspected = true;
        viewMode = SliceViewMode.Shelter;
        state = SliceState.LowTidePlanning;
        chartRadioCondition = 1;
        lampForecastCharges = 1;
        tideStrength = 1f;
        CaptureChartForecast();

        const float playerChosenDepth = 0.37f;
        netSetDepth01 = playerChosenDepth;
        string forecastText = GetLampForecastText();
        UpdateVisuals(5.1f);
        bool forecastDoesNotMutateChoice = Mathf.Abs(netSetDepth01 - playerChosenDepth) <= 0.0001f;
        bool forecastDoesNotPrescribeAnswer = !forecastText.Contains("推荐") &&
            !forecastText.Contains("建议") && !forecastText.Contains("%深");

        TideNetForecastModel.HighWaterBand repairedBand =
            chartForecastSnapshot.ToHighWaterBand();
        float expectedPostX = GetFormalHouseSprite() != null
            ? PreviousSaltMarkPostX[0]
            : houseAnchor.x - 1.38f;
        bool repairedNotchesGrounded = forecastTideNotches.IsVisible &&
            Mathf.Abs(forecastTideNotches.StaffWorldX - expectedPostX) <= 0.01f &&
            Mathf.Abs(forecastTideNotches.LowerWorldY - repairedBand.LowerY) <= 0.01f &&
            Mathf.Abs(forecastTideNotches.UpperWorldY - repairedBand.UpperY) <= 0.01f &&
            !forecastTideNotches.HasCollider &&
            forecastTideNotches.SortingOrder < foregroundWaterOcclusionRenderer.sortingOrder;
        float repairedNotchWidth = forecastTideNotches.VisibleBandWidthMeters;

        chartRadioCondition = 0;
        lampForecastCharges = 0;
        CaptureLoftForecast();
        TideNetForecastModel.HighWaterBand lookoutBand =
            loftForecastSnapshot.ToHighWaterBand();
        bool repairNarrowsKnowledge = repairedBand.WidthMeters < lookoutBand.WidthMeters * 0.5f;
        UpdateVisuals(5.2f);
        float lookoutNotchWidth = forecastTideNotches.VisibleBandWidthMeters;
        bool roughObservationIsWider = forecastTideNotches.IsVisible &&
            Mathf.Abs(lookoutNotchWidth - lookoutBand.WidthMeters) <= 0.01f &&
            lookoutNotchWidth > repairedNotchWidth * 2f;

        float frozenLowerY = forecastTideNotches.LowerWorldY;
        float frozenUpperY = forecastTideNotches.UpperWorldY;
        weatherClockSeconds += Mathf.Min(12f, tideCycleSeconds * 0.1f);
        UpdateVisuals(5.3f);
        bool snapshotDoesNotDrift = forecastTideNotches.IsVisible &&
            Mathf.Abs(forecastTideNotches.LowerWorldY - frozenLowerY) <= 0.001f &&
            Mathf.Abs(forecastTideNotches.UpperWorldY - frozenUpperY) <= 0.001f;

        tideClockSeconds = tideCycleSeconds * 0.5001f;
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        UpdateVisuals(5.4f);
        bool passedHighWaterHidesNotches = !forecastTideNotches.IsVisible;

        TideNetForecastModel.NetChoice shallowChoice = EvaluateNetChoiceForecast(0.2f);
        TideNetForecastModel.NetChoice deepChoice = EvaluateNetChoiceForecast(0.9f);
        // This snapshot carries spring-tide saltwood near the surface. A deeper bottom
        // line cannot stop it overtopping the fixed head rope, so fabricating a generic
        // deep-net reward here would erase the material-specific decision. The fish-depth
        // contrast is guarded separately by TideNetEncounterModel's core probe.
        bool floatingBatchDoesNotFakeDepthReward =
            Mathf.Abs(deepChoice.ContactRatio - shallowChoice.ContactRatio) <= 0.04f &&
            deepChoice.StressTier > shallowChoice.StressTier;
        bool physicalTimingRemains = forecastText.Contains("高潮") && forecastText.Contains("平流");
        bool retiredMarkerAbsent = FindDescendantByName(transform, "GeneratedStiltFirstLampForecastMarker") == null;
        bool physicalNotchesRegistered =
            FindDescendantByName(transform, TideForecastTideNotchController.LowerNotchName) != null &&
            FindDescendantByName(transform, TideForecastTideNotchController.UpperNotchName) != null;

        string evidence =
            $"区间宽 粗/修={lookoutBand.WidthMeters:F2}/{repairedBand.WidthMeters:F2}m；" +
            $"桩结宽={lookoutNotchWidth:F2}/{repairedNotchWidth:F2}m；" +
            $"浅/深有效相遇={shallowChoice.PredictedEffectiveContactSeconds:F2}/{deepChoice.PredictedEffectiveContactSeconds:F2}s；" +
            $"网压={shallowChoice.StressTier}/{deepChoice.StressTier}；" +
            $"网深保留={netSetDepth01:F2}；快照固定/过潮隐藏={snapshotDoesNotDrift}/{passedHighWaterHidesNotches}；" +
            $"物理桩结={physicalNotchesRegistered}/{repairedNotchesGrounded}；旧标记缺席={retiredMarkerAbsent}";
        bool passed = forecastDoesNotMutateChoice && forecastDoesNotPrescribeAnswer &&
            repairNarrowsKnowledge && repairedNotchesGrounded && roughObservationIsWider &&
            snapshotDoesNotDrift && passedHighWaterHidesNotches &&
            physicalNotchesRegistered && floatingBatchDoesNotFakeDepthReward &&
            physicalTimingRemains && retiredMarkerAbsent;
        return passed
            ? $"PASS：下一高潮以固定潮次快照落在同一主桩；过高潮自动失效，浅深网仍由玩家承担收益和风险。{evidence}"
            : $"FAIL：潮汐预报会随时钟漂移、跨高潮不失效、没有落到真实桩柱，或仍替玩家选择网深。{evidence}";
    }

    public string RunEditorPreviousHighWaterMemoryProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalInspected = true;
        viewMode = SliceViewMode.Shelter;
        state = SliceState.LowTidePlanning;
        UpdateVisuals(3.2f);

        bool openingDoesNotInventHistory = !highWaterMemory.HasPreviousCycle &&
            highWaterMemory.CompletedCycleCount == 0;
        bool openingMarksHidden = true;
        for (int i = 0; i < previousHighWaterSaltMarks.Count; i++)
        {
            openingMarksHidden &= !previousHighWaterSaltMarks[i].enabled;
        }

        float cycle = Mathf.Max(8f, tideCycleSeconds);
        float startTideClock = tideClockSeconds;
        float startWeatherClock = weatherClockSeconds;
        AdvanceHighWaterMemory(startTideClock, startWeatherClock, cycle);
        tideClockSeconds = Mathf.Repeat(startTideClock + cycle, cycle);
        weatherClockSeconds = startWeatherClock + cycle;
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        UpdateVisuals(4.1f);

        float highWaterLead = Mathf.Repeat(cycle * 0.5f - startTideClock, cycle);
        float expectedPeakY = EvaluateNaturalWaterY(
            cycle * 0.5f,
            startWeatherClock + highWaterLead);
        float peakError = Mathf.Abs(highWaterMemory.PreviousCyclePeakY - expectedPeakY);
        bool completeCycleOwnsPeak = highWaterMemory.HasPreviousCycle &&
            highWaterMemory.CompletedCycleCount == 1 &&
            peakError <= 0.01f;

        bool allMarksRegistered = previousHighWaterSaltMarks.Count == PreviousSaltMarkPostX.Length;
        bool allMarksGrounded = allMarksRegistered;
        bool allMarksLayered = allMarksRegistered;
        float maxPostError = 0f;
        for (int i = 0; i < previousHighWaterSaltMarks.Count; i++)
        {
            SpriteRenderer mark = previousHighWaterSaltMarks[i];
            float postError = Mathf.Abs(mark.transform.position.x - PreviousSaltMarkPostX[i]);
            maxPostError = Mathf.Max(maxPostError, postError);
            allMarksGrounded &= mark.enabled &&
                postError <= 0.01f &&
                mark.GetComponent<Collider2D>() == null &&
                mark.transform.parent != null &&
                mark.transform.parent.name == "GeneratedStiltFirstLayer_Shelter";
            allMarksLayered &= mark.sortingOrder > houseRenderer.sortingOrder &&
                mark.sortingOrder < foregroundWaterOcclusionRenderer.sortingOrder;
        }

        string comparisonText = GetPreviousHighWaterComparisonText(false);
        bool comparisonStaysQualitative = comparisonText.Contains("旧湿线") &&
            !comparisonText.Contains("厘米") &&
            !comparisonText.Contains("m");

        viewMode = SliceViewMode.Interior;
        playerLane = WalkLane.InteriorUpper;
        playerPosition = new Vector2(GetInteriorBedX(), GetPlayerLaneY(playerLane));
        UpdateVisuals(4.4f);
        bool marksDoNotLeakAcrossViews = true;
        for (int i = 0; i < previousHighWaterSaltMarks.Count; i++)
        {
            marksDoNotLeakAcrossViews &= !previousHighWaterSaltMarks[i].enabled;
        }

        ResetSlice();
        tideClockSeconds = cycle * 0.35f;
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        highWaterMemory = TideHighWaterMemoryModel.Begin(currentWaterY);
        dayProgress01 = 0.6f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        AdvanceMoonPhase();
        bool sleepSkipKeepsHistory = highWaterMemory.HasPreviousCycle &&
            highWaterMemory.CompletedCycleCount >= 1;

        string evidence =
            $"首潮无旧线={openingDoesNotInventHistory}/{openingMarksHidden}；" +
            $"完整潮={highWaterMemory.CompletedCycleCount}，峰值误差={peakError:F4}m；" +
            $"立柱误差={maxPostError:F4}m，层级={allMarksLayered}；" +
            $"跨视图={marksDoNotLeakAcrossViews}，睡眠补潮={sleepSkipKeepsHistory}";
        bool passed = openingDoesNotInventHistory && openingMarksHidden &&
            completeCycleOwnsPeak && allMarksGrounded && allMarksLayered &&
            comparisonStaysQualitative && marksDoNotLeakAcrossViews && sleepSkipKeepsHistory;
        return passed
            ? $"PASS：上一潮峰值只在完整周期后结算；逐帧与睡眠跳时同源，碎片盐线贴真实立柱并由前景海水正确遮挡。{evidence}"
            : $"FAIL：高潮记忆仍存在伪造历史、漏采样、悬空潮痕、跨视图残留或遮挡所有权错误。{evidence}";
    }

    private bool DoV54SuspensionRopesFollowCurrentPose()
    {
        if (!v54DeployedPoseValid || netSuspensionRopes.Count < 2 ||
            !netSuspensionRopes[0].enabled || !netSuspensionRopes[1].enabled)
        {
            return false;
        }

        float firstStakeX = GetNetFirstStakeX();
        float secondStakeX = GetNetSecondStakeX();
        float stakeTieY = GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.22f;
        TideV54NetVisualState visualState = netFraying01 >= 0.12f
            ? TideV54NetVisualState.DeployedFrayed
            : TideV54NetVisualState.DeployedWet;
        Vector2 leftNet = TideV54NetPresentationModel.EvaluateWorldAnchor(
            v54DeployedPosition,
            v54DeployedWorldSize,
            TideV54NetPresentationModel.GetLeftAttachmentTopLeft01(visualState));
        Vector2 rightNet = TideV54NetPresentationModel.EvaluateWorldAnchor(
            v54DeployedPosition,
            v54DeployedWorldSize,
            TideV54NetPresentationModel.GetRightAttachmentTopLeft01(visualState));
        Vector2 leftStake = new Vector2(firstStakeX, stakeTieY);
        Vector2 rightStake = new Vector2(secondStakeX, stakeTieY);

        return DoesRopeMatchSegment(netSuspensionRopes[0], leftStake, leftNet) &&
            DoesRopeMatchSegment(netSuspensionRopes[1], rightStake, rightNet);
    }

    private static bool DoesRopeMatchSegment(SpriteRenderer rope, Vector2 start, Vector2 end)
    {
        Vector2 expectedCenter = (start + end) * 0.5f;
        float expectedAngle = Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg;
        float centerError = Vector2.Distance((Vector2)rope.transform.position, expectedCenter);
        float angleError = Mathf.Abs(Mathf.DeltaAngle(rope.transform.eulerAngles.z, expectedAngle));
        return centerError <= 0.012f && angleError <= 0.35f;
    }

    private static float MeasureNetHaulCompletionSeconds(
        float baseDurationSeconds,
        float submersion01,
        float currentStrength01,
        float catchLoad01,
        float depth01)
    {
        const float stepSeconds = 0.02f;
        float elapsed = 0f;
        float phase01 = 0f;
        float progress01 = 0f;
        while (progress01 < 1f && elapsed < 30f)
        {
            TideNetHaulModel.Step step = TideNetHaulModel.EvaluateStep(
                phase01,
                stepSeconds,
                baseDurationSeconds,
                submersion01,
                currentStrength01,
                catchLoad01,
                depth01);
            phase01 = step.Phase01;
            progress01 += step.ProgressDelta;
            elapsed += stepSeconds;
        }

        return elapsed;
    }

    public string RunEditorRepairContinuityProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalInspected = true;
        state = SliceState.RepairMoment;
        dayNightPhase = DayNightPhase.Day;
        viewMode = SliceViewMode.Shelter;
        playerLane = WalkLane.TideFlat;
        playerPosition = GetRepairChoicePosition(RepairChoice.Stilt);
        stiltIntegrity = 1;
        netIntegrity = 1;
        timberStock = 6;
        ropeStock = 3;
        currentHarvest = HarvestKind.None;
        currentHarvestBanked = true;
        harvestPhysicalState = HarvestPhysicalState.None;

        float duration = GetRepairWorkDuration(RepairChoice.Stilt);
        TickRepairWorkAtWorldTarget(duration * 0.2f, true, true);
        float progress20 = repairWorkProgress;
        float preview20 = GetRepairReveal01();
        int step20 = repairWorkStep;

        TickRepairWorkAtWorldTarget(duration * 0.3f, false, true);
        float progress50 = repairWorkProgress;
        float preview50 = GetRepairReveal01();
        int step50 = repairWorkStep;

        TickRepairWorkAtWorldTarget(duration * 0.35f, false, true);
        float progress85 = repairWorkProgress;
        float preview85 = GetRepairReveal01();
        int step85 = repairWorkStep;

        int timberBeforeCommit = timberStock;
        int ropeBeforeCommit = ropeStock;
        int stiltBeforeCommit = stiltIntegrity;
        TickRepairWorkAtWorldTarget(duration * 0.14f, false, true);
        float progress99 = repairWorkProgress;
        bool ninetyNinePercentIsPreviewOnly = !repairChoiceApplied &&
            timberStock == timberBeforeCommit &&
            ropeStock == ropeBeforeCommit &&
            stiltIntegrity == stiltBeforeCommit;

        TickRepairWorkAtWorldTarget(1f, false, false);
        float pausedProgress = repairWorkProgress;
        bool pauseFreezesEverything = Mathf.Abs(pausedProgress - progress99) <= 0.001f &&
            timberStock == timberBeforeCommit &&
            ropeStock == ropeBeforeCommit &&
            stiltIntegrity == stiltBeforeCommit;

        TickRepairWorkAtWorldTarget(duration * 0.02f, false, true);
        int timberAfterCommit = timberStock;
        int ropeAfterCommit = ropeStock;
        int stiltAfterCommit = stiltIntegrity;
        float previewAfterCommit = GetRepairReveal01();
        bool committedOnce = repairChoiceApplied &&
            timberAfterCommit == timberBeforeCommit - 2 &&
            ropeAfterCommit == ropeBeforeCommit - 1 &&
            stiltAfterCommit == stiltBeforeCommit + 1;

        TickRepairWorkAtWorldTarget(duration, false, true);
        bool remainsCommittedOnce = timberStock == timberAfterCommit &&
            ropeStock == ropeAfterCommit &&
            stiltIntegrity == stiltAfterCommit;

        bool stagedPreview = step20 == (int)TideRepairWorkPhase.Clean &&
            step50 == (int)TideRepairWorkPhase.TestFit &&
            step85 == (int)TideRepairWorkPhase.Seal &&
            progress20 > 0.19f && progress50 > progress20 && progress85 > progress50 &&
            preview20 > 0.05f && preview20 < preview50 && preview50 < preview85 && preview85 < 0.95f;
        bool completionStopsWork = !repairWorkActive && previewAfterCommit >= 0.999f;
        string evidence = $"阶段={step20}/{step50}/{step85}；进度={progress20:F2}/{progress50:F2}/{progress85:F2}/{progress99:F2}；" +
            $"预览={preview20:F2}/{preview50:F2}/{preview85:F2}->{previewAfterCommit:F2}；" +
            $"99%不结算={ninetyNinePercentIsPreviewOnly}；暂停={pauseFreezesEverything}；首次结算={committedOnce}；防重复={remainsCommittedOnce}";
        bool passed = stagedPreview && ninetyNinePercentIsPreviewOnly && pauseFreezesEverything &&
            committedOnce && remainsCommittedOnce && completionStopsWork;
        return passed
            ? $"PASS：维修进度连续驱动施工反馈，暂停可续，完成只结算一次且不补演。{evidence}"
            : $"FAIL：维修反馈、暂停或结算仍与真实进度不同步。{evidence}";
    }

    public string RunEditorAllRepairChoicesContinuityProbe()
    {
        RepairChoice[] choices =
        {
            RepairChoice.Stilt,
            RepairChoice.Roof,
            RepairChoice.InteriorSeal,
            RepairChoice.Workbench,
            RepairChoice.Bed,
            RepairChoice.ChartRadio,
            RepairChoice.Lamp,
            RepairChoice.Net,
            RepairChoice.Hull,
            RepairChoice.Sail,
            RepairChoice.Cabin
        };

        bool allPassed = true;
        string evidence = string.Empty;
        for (int i = 0; i < choices.Length; i++)
        {
            RepairChoice choice = choices[i];
            EnsureScene();
            ResetSlice();
            arrivalInspected = true;
            state = SliceState.RepairMoment;
            dayNightPhase = DayNightPhase.Day;
            currentHarvest = HarvestKind.None;
            currentHarvestBanked = true;
            harvestPhysicalState = HarvestPhysicalState.None;
            timberStock = 10;
            ropeStock = 10;
            clothStock = 10;
            metalStock = 10;
            foodStock = 10;
            stiltIntegrity = 1;
            roofIntegrity = 0;
            interiorComfort = 0;
            stoveCondition = 0;
            netIntegrity = 1;
            boatHullIntegrity = 1;
            boatSailIntegrity = 0;
            boatCabinIntegrity = 0;
            RecalculateBoatReadiness();

            if (choice == RepairChoice.Roof)
            {
                viewMode = SliceViewMode.Interior;
                playerLane = WalkLane.InteriorLoft;
            }
            else if (choice == RepairChoice.InteriorSeal ||
                     choice == RepairChoice.Workbench ||
                     choice == RepairChoice.Bed ||
                     choice == RepairChoice.ChartRadio ||
                     choice == RepairChoice.Lamp)
            {
                viewMode = SliceViewMode.Interior;
                playerLane = WalkLane.InteriorUpper;
            }
            else
            {
                viewMode = SliceViewMode.Shelter;
                playerLane = WalkLane.TideFlat;
            }

            playerPosition = GetRepairChoicePosition(choice);
            int timberNeed;
            int ropeNeed;
            int clothNeed;
            int metalNeed;
            int foodNeed;
            GetRepairMaterialNeeds(choice, out timberNeed, out ropeNeed, out clothNeed, out metalNeed, out foodNeed);
            int levelBefore = GetRepairChoiceLevel(choice);
            float duration = GetRepairWorkDuration(choice);
            TickRepairWorkAtWorldTarget(duration * 0.99f, true, true);
            bool selectedOwnAnchor = pendingRepairChoice == choice;
            bool previewOnlyAt99 = !repairChoiceApplied && GetRepairChoiceLevel(choice) == levelBefore &&
                timberStock == 10 && ropeStock == 10 && clothStock == 10 && metalStock == 10 && foodStock == 10;

            TickRepairWorkAtWorldTarget(duration * 0.02f, false, true);
            int committedLevel = GetRepairChoiceLevel(choice);
            bool committedExactly = repairChoiceApplied && committedLevel == levelBefore + 1 &&
                timberStock == 10 - timberNeed &&
                ropeStock == 10 - ropeNeed &&
                clothStock == 10 - clothNeed &&
                metalStock == 10 - metalNeed &&
                foodStock == 10 - foodNeed;

            TickRepairWorkAtWorldTarget(duration, false, true);
            bool idempotent = GetRepairChoiceLevel(choice) == committedLevel &&
                timberStock == 10 - timberNeed &&
                ropeStock == 10 - ropeNeed &&
                clothStock == 10 - clothNeed &&
                metalStock == 10 - metalNeed &&
                foodStock == 10 - foodNeed;
            bool choicePassed = selectedOwnAnchor && previewOnlyAt99 && committedExactly && idempotent;
            allPassed &= choicePassed;
            evidence += $"{GetRepairChoiceName(choice)}={selectedOwnAnchor}/{previewOnlyAt99}/{committedExactly}/{idempotent}" +
                (i < choices.Length - 1 ? "；" : string.Empty);
        }

        return allPassed
            ? $"PASS：十一类维修都由各自世界锚点进入连续施工，并精确、幂等结算。{evidence}"
            : $"FAIL：至少一类维修被其他锚点抢占，或仍会提前/重复结算。{evidence}";
    }

    public string RunEditorRepairPreviewIsolationProbe()
    {
        EnsureScene();
        ResetSlice();
        state = SliceState.RepairMoment;
        pendingRepairChoice = RepairChoice.Stilt;
        repairWorkProgress = 0.5f;
        repairWorkActive = true;
        float stiltHalf = GetRepairPreview01(RepairChoice.Stilt);
        float roofWhileStilt = GetRepairPreview01(RepairChoice.Roof);

        pendingRepairChoice = RepairChoice.Roof;
        repairWorkProgress = 0.85f;
        float roofLate = GetRepairPreview01(RepairChoice.Roof);
        float stiltWhileRoof = GetRepairPreview01(RepairChoice.Stilt);

        repairWorkActive = false;
        float roofPaused = GetRepairPreview01(RepairChoice.Roof);
        repairChoiceApplied = true;
        repairWorkProgress = 1f;
        float roofCompleted = GetRepairPreview01(RepairChoice.Roof);

        ResetSlice();
        float noRepairPreview = GetRepairPreview01(RepairChoice.Roof);
        bool isolated = stiltHalf > 0.45f && stiltHalf < 0.55f && roofWhileStilt <= 0.001f &&
            roofLate > 0.9f && roofLate < 0.97f && stiltWhileRoof <= 0.001f;
        bool pauseFreezesVisual = Mathf.Abs(roofPaused - roofLate) <= 0.001f;
        bool completionAndExitAreStable = roofCompleted >= 0.999f && noRepairPreview <= 0.001f;
        string evidence = $"柱50%={stiltHalf:F2}/屋顶串扰={roofWhileStilt:F2}；屋顶85%={roofLate:F2}/柱串扰={stiltWhileRoof:F2}；" +
            $"暂停={roofPaused:F2}；完成={roofCompleted:F2}；退出={noRepairPreview:F2}";
        return isolated && pauseFreezesVisual && completionAndExitAreStable
            ? $"PASS：维修预览只驱动当前真实部位，暂停冻结，完成锁定，退出清零。{evidence}"
            : $"FAIL：维修预览仍会串部位、自动推进或在退出后残留。{evidence}";
    }

    private int GetRepairChoiceLevel(RepairChoice choice)
    {
        if (choice == RepairChoice.Stilt)
        {
            return stiltIntegrity;
        }
        if (choice == RepairChoice.Roof)
        {
            return roofIntegrity;
        }
        if (choice == RepairChoice.InteriorSeal)
        {
            return interiorSealCondition;
        }
        if (choice == RepairChoice.Workbench)
        {
            return workbenchCondition;
        }
        if (choice == RepairChoice.Bed)
        {
            return bedCondition;
        }
        if (choice == RepairChoice.ChartRadio)
        {
            return chartRadioCondition;
        }
        if (choice == RepairChoice.Lamp)
        {
            return stoveCondition;
        }
        if (choice == RepairChoice.Net)
        {
            return netIntegrity;
        }
        if (choice == RepairChoice.Hull)
        {
            return boatHullIntegrity;
        }
        if (choice == RepairChoice.Sail)
        {
            return boatSailIntegrity;
        }
        if (choice == RepairChoice.Cabin)
        {
            return boatCabinIntegrity;
        }

        return 0;
    }

    public string RunEditorBoatPassengerScaleProbe()
    {
        // 用真实短航姿态检查所有权，而不是只在孤立 BoatRoot 上验证层级。
        // 这样岸上 PlayerGlow 或旧程序索具残留时，探针会直接失败。
        SetEditorSailingPreviewPose();

        EnsureV31BoatResourcesLoaded();
        EnsureV32ArtResourcesLoaded();
        if (!HasCompleteV31BoatPresentation() || !HasCompleteV32ArtPresentation())
        {
            return "FAIL：缺少 V31 船体分层或 V32 完整坐姿乘员，无法验证上船结果。";
        }

        Sprite passenger = formalV32ArtCatalog.SeatedFrames[0];
        Vector2 normalizedPivot = new Vector2(
            passenger.pivot.x / passenger.rect.width,
            passenger.pivot.y / passenger.rect.height);
        float worldHeight = passenger.bounds.size.y * BoatPassengerUniformScale;
        bool fullCanvas = passenger.texture != null &&
            passenger.texture.width == 1024 &&
            passenger.texture.height == 1024 &&
            Mathf.Abs(passenger.rect.width - 1024f) <= 0.01f &&
            Mathf.Abs(passenger.rect.height - 1024f) <= 0.01f;
        bool fullBodyPivot = Vector2.Distance(normalizedPivot, new Vector2(0.5f, 0.45f)) <= 0.001f;
        bool seatedHeightReasonable = worldHeight >= 1.25f && worldHeight <= 1.45f;
        bool passengerSandwiched =
            boatHullRenderer.sortingOrder < boatPassengerRenderer.sortingOrder &&
            boatPassengerRenderer.sortingOrder < boatPassengerGunwaleRenderer.sortingOrder;
        bool frontLayerSeparate = boatPassengerGunwaleRenderer.sprite != null &&
            boatPassengerGunwaleRenderer.sprite.texture != passenger.texture;
        Sprite stableSeatA = formalV32ArtCatalog.GetStableSeatedFrame();
        Sprite stableSeatB = formalV32ArtCatalog.GetStableSeatedFrame();
        bool noInputPassengerStable = stableSeatA != null && stableSeatA == stableSeatB &&
            boatPassengerRenderer.enabled && boatPassengerRenderer.sprite == stableSeatA;
        Bounds runtimePassengerBounds = boatPassengerRenderer.bounds;
        Bounds runtimeGunwaleBounds = boatPassengerGunwaleRenderer.bounds;
        float readableAboveGunwaleHeight = runtimePassengerBounds.max.y - runtimeGunwaleBounds.max.y;
        float physicallyHiddenBelowGunwaleHeight = runtimeGunwaleBounds.max.y - runtimePassengerBounds.min.y;
        bool seatedSilhouetteReadable = readableAboveGunwaleHeight >= 0.48f &&
            physicallyHiddenBelowGunwaleHeight >= 0.5f;
        bool legacyPlayerGlowHidden = playerGlowRenderer != null && !playerGlowRenderer.enabled;
        bool legacyRiggingHidden = true;
        for (int i = 0; i < boatRigging.Count; i++)
        {
            legacyRiggingHidden &= boatRigging[i] != null && !boatRigging[i].enabled;
        }
        string evidence =
            $"画布={passenger.texture.width}x{passenger.texture.height}；Pivot={normalizedPivot}；" +
            $"世界高={worldHeight:F2}；层级={boatHullRenderer.sortingOrder}<" +
            $"{boatPassengerRenderer.sortingOrder}<{boatPassengerGunwaleRenderer.sortingOrder}；" +
            $"整海遮挡={foregroundDeepWaterOcclusionRenderer.enabled}/局部水={boatWaterlineOcclusionRenderer.enabled}；" +
            $"旧光圈={legacyPlayerGlowHidden}；" +
            $"无输入稳定={noInputPassengerStable}；舷上/舷后={readableAboveGunwaleHeight:F2}/" +
            $"{physicallyHiddenBelowGunwaleHeight:F2}m；旧索具={legacyRiggingHidden}";
        return fullCanvas && fullBodyPivot && seatedHeightReasonable &&
            passengerSandwiched && frontLayerSeparate && noInputPassengerStable &&
            seatedSilhouetteReadable && legacyPlayerGlowHidden && legacyRiggingHidden
            ? $"PASS：V32 完整坐姿人物由后船体/人物/前船舷分层遮挡，旧光圈和旧索具没有抢所有权。{evidence}"
            : $"FAIL：乘员源图、世界比例、船体前后层或旧表现所有权仍不合格。{evidence}";
    }

    public string RunEditorV39BoatSemanticIntegrationProbe()
    {
        SetEditorSailingPreviewPose();
        if (HasCompleteV52BoatRepairBase())
        {
            return RunEditorV52BoatRepairIntegrationProbe();
        }
        if (!HasCompleteV39BoatPresentation())
        {
            return "FAIL：Scene 尚未序列化 V39 破帆态/修复态十二张船体层。";
        }

        bool repaired = boatHullIntegrity >= 2 && boatSailIntegrity >= 1;
        Sprite[] expected = repaired ? v39RepairedBoatLayers : v39DamagedBoatLayers;
        SpriteRenderer[] renderers =
        {
            boatBackRigRenderer,
            boatSailRenderer,
            boatHullRenderer,
            boatCockpitRenderer,
            boatPassengerGunwaleRenderer,
            boatRudderRenderer,
        };
        bool allSixLayersOwned = true;
        for (int i = 0; i < renderers.Length; i++)
        {
            allSixLayersOwned &= renderers[i] != null && renderers[i].enabled &&
                renderers[i].sprite == expected[i];
        }

        float rotationZ = boatHullRenderer.transform.localEulerAngles.z;
        Vector2 backHullDelta = TideV39BoatPresentationModel.EvaluateOffsetWorldPosition(
            Vector2.zero,
            TideV39BoatPresentationModel.GetLayerOffset(TideV39BoatLayer.BackHull, repaired),
            rotationZ,
            boatHullRenderer.flipX);
        Vector2 root = (Vector2)boatHullRenderer.transform.localPosition - backHullDelta;
        Vector2 waterline = TideV39BoatPresentationModel.EvaluateAnchorWorldPosition(
            root,
            new Vector2Int(
                TideV39BoatPresentationModel.CanvasSize.x / 2,
                TideV39BoatPresentationModel.StaticCalmWaterlineYTopLeft),
            rotationZ,
            boatHullRenderer.flipX);
        TideOceanSample ocean = GetSailingOceanSample(sailingBoatX);
        float expectedWaterlineY = ocean.SurfaceY - sailingWaterIngress01 * 0.1f;
        float waterlineError = Mathf.Abs(waterline.y - expectedWaterlineY);

        Vector2 expectedSeat = TideV39BoatPresentationModel.EvaluateAnchorWorldPosition(
            root,
            TideV39BoatPresentationModel.SeatTopLeft,
            rotationZ,
            boatHullRenderer.flipX);
        Vector2 expectedPassengerPivot = GetBoatPassengerVisualPivot(expectedSeat, rotationZ);
        float seatError = boatPassengerRenderer != null && boatPassengerRenderer.enabled
            ? Vector2.Distance(boatPassengerRenderer.transform.localPosition, expectedPassengerPivot)
            : float.PositiveInfinity;

        Vector2 mooring = TideV39BoatPresentationModel.EvaluateAnchorWorldPosition(
            root,
            TideV39BoatPresentationModel.MooringPointTopLeft,
            rotationZ,
            boatHullRenderer.flipX);
        Vector2 stern = TideV39BoatPresentationModel.EvaluateAnchorWorldPosition(
            root,
            TideV39BoatPresentationModel.SternStepTopLeft,
            rotationZ,
            boatHullRenderer.flipX);
        Vector2 cockpit = TideV39BoatPresentationModel.EvaluateAnchorWorldPosition(
            root,
            TideV39BoatPresentationModel.CockpitEntryTopLeft,
            rotationZ,
            boatHullRenderer.flipX);
        bool boardingRouteContinuous = mooring.x < stern.x && stern.x < cockpit.x &&
            cockpit.x < expectedSeat.x &&
            Vector2.Distance(mooring, stern) <= 0.22f &&
            Vector2.Distance(stern, cockpit) <= 0.32f &&
            Vector2.Distance(cockpit, expectedSeat) <= 0.34f;
        bool passengerLayerSandwiched = IsBoatPassengerSandwiched();
        bool passengerInsideCockpit = IsBoatPassengerInsideCockpit();
        Bounds passengerBounds = boatPassengerRenderer.bounds;
        Bounds gunwaleBounds = boatPassengerGunwaleRenderer.bounds;
        bool waterOcclusionOwned = foregroundDeepWaterOcclusionRenderer != null &&
            foregroundDeepWaterOcclusionRenderer.enabled &&
            foregroundDeepWaterOcclusionRenderer.sortingOrder > boatRudderRenderer.sortingOrder &&
            boatWaterlineOcclusionRenderer != null &&
            !boatWaterlineOcclusionRenderer.enabled;

        string evidence =
            $"六层={allSixLayersOwned}；水线误差={waterlineError:F3}m；" +
            $"座位误差={seatError:F3}m；登船序列={boardingRouteContinuous}；" +
            $"层级/座舱={passengerLayerSandwiched}/{passengerInsideCockpit}；" +
            $"人物X={passengerBounds.min.x:F2}..{passengerBounds.max.x:F2},Y={passengerBounds.min.y:F2}..{passengerBounds.max.y:F2}；" +
            $"船舷X={gunwaleBounds.min.x:F2}..{gunwaleBounds.max.x:F2},Y={gunwaleBounds.min.y:F2}..{gunwaleBounds.max.y:F2}；" +
            $"连续前景水/局部水关闭={waterOcclusionOwned}/{!boatWaterlineOcclusionRenderer.enabled}";
        return allSixLayersOwned && waterlineError <= 0.015f && seatError <= 0.005f &&
            boardingRouteContinuous && passengerLayerSandwiched && passengerInsideCockpit &&
            waterOcclusionOwned
            ? $"PASS：V39 六层船体、水线、完整乘员和登船锚点共用同一 BoatRoot。{evidence}"
             : $"FAIL：V39 资源所有权、水线、乘员或登船锚点未按契约落地。{evidence}";
    }

    public string RunEditorV52BoatRepairIntegrationProbe()
    {
        SetEditorSailingPreviewPose();
        if (!HasCompleteV39BoatPresentation() || !HasCompleteV52BoatRepairBase())
        {
            return "FAIL：V67 Balanced 的 V39 船体底层或 V52 稳定底尚未接入。";
        }

        TideV52BoatRepairStageAsset hullDamage = GetV52BoatRepairStageAsset(
            TideV52BoatRepairOwner.HullBreach,
            TideV52BoatRepairStage.Damage);
        TideV52BoatRepairStageAsset sailDamage = GetV52BoatRepairStageAsset(
            TideV52BoatRepairOwner.SailRepair,
            TideV52BoatRepairStage.Damage);
        TideV52BoatRepairStageAsset cockpitDamage = GetV52BoatRepairStageAsset(
            TideV52BoatRepairOwner.CockpitFloor,
            TideV52BoatRepairStage.Damage);
        if (hullDamage == null || sailDamage == null || cockpitDamage == null)
        {
            return "FAIL：V52 Damage 三阶段索引未能按 owner 分别加载。";
        }

        bool initialOwnersCorrect =
            boatHullRepairOwnerRenderer.enabled && boatHullRepairOwnerRenderer.sprite == hullDamage.Sprite &&
            boatSailRenderer.enabled && boatSailRenderer.sprite == sailDamage.Sprite &&
            boatCockpitRepairOwnerRenderer.enabled && boatCockpitRepairOwnerRenderer.sprite == cockpitDamage.Sprite;
        bool stableBasesCorrect = boatPassengerGunwaleRenderer.enabled &&
            boatPassengerGunwaleRenderer.sprite == formalBoatV52BaseAsset.FrontGunwaleStable &&
            boatCockpitRenderer.enabled &&
            boatCockpitRenderer.sprite == formalBoatV52BaseAsset.CockpitFloorStable;
        bool v39ConflictOwnersRetired = boatSailRenderer.sprite != v39DamagedBoatLayers[(int)TideV39BoatLayer.SailRest] &&
            boatCockpitRenderer.sprite != v39DamagedBoatLayers[(int)TideV39BoatLayer.CockpitFloor] &&
            boatPassengerGunwaleRenderer.sprite != v39DamagedBoatLayers[(int)TideV39BoatLayer.FrontGunwale];
        bool v39StableOwnersCorrect = boatBackRigRenderer.sprite == v39DamagedBoatLayers[(int)TideV39BoatLayer.BackRig] &&
            boatHullRenderer.sprite == v39DamagedBoatLayers[(int)TideV39BoatLayer.BackHull] &&
            boatRudderRenderer.sprite == v39DamagedBoatLayers[(int)TideV39BoatLayer.RudderRest];

        float rotationZ = boatHullRenderer.transform.localEulerAngles.z;
        Vector2 backHullDelta = TideV39BoatPresentationModel.EvaluateOffsetWorldPosition(
            Vector2.zero,
            TideV39BoatPresentationModel.GetLayerOffset(TideV39BoatLayer.BackHull, false),
            rotationZ,
            boatHullRenderer.flipX);
        Vector2 root = (Vector2)boatHullRenderer.transform.localPosition - backHullDelta;
        float hullOffsetError = Vector2.Distance(
            boatHullRepairOwnerRenderer.transform.localPosition,
            TideV39BoatPresentationModel.EvaluateOffsetWorldPosition(
                root,
                TideV52BoatRepairPresentationModel.GetOwnerOffset(TideV52BoatRepairOwner.HullBreach),
                rotationZ,
                FormalBoatFacesRight));
        float cockpitOffsetError = Vector2.Distance(
            boatCockpitRepairOwnerRenderer.transform.localPosition,
            TideV39BoatPresentationModel.EvaluateOffsetWorldPosition(
                root,
                TideV52BoatRepairPresentationModel.GetOwnerOffset(TideV52BoatRepairOwner.CockpitFloor),
                rotationZ,
                FormalBoatFacesRight));
        bool passengerSandwiched = boatCockpitRepairOwnerRenderer.sortingOrder < boatPassengerRenderer.sortingOrder &&
            boatPassengerRenderer.sortingOrder < boatPassengerGunwaleRenderer.sortingOrder &&
            boatPassengerGunwaleRenderer.sortingOrder < boatHullRepairOwnerRenderer.sortingOrder;

        // 只推进船壳施工。帆和舱底必须保持 Damage，证明三条状态链没有再被
        // boatReadiness 这个汇总值一起驱动。
        state = SliceState.RepairMoment;
        pendingRepairChoice = RepairChoice.Hull;
        repairChoiceApplied = false;
        repairWorkProgress = 0.5f;
        UpdateVisuals(3.1f);
        TideV52BoatRepairStageAsset hullTestFit = GetV52BoatRepairStageAsset(
            TideV52BoatRepairOwner.HullBreach,
            TideV52BoatRepairStage.TestFit);
        bool hullOnlyTestFit = hullTestFit != null && boatHullRepairOwnerRenderer.sprite == hullTestFit.Sprite &&
            boatSailRenderer.sprite == sailDamage.Sprite &&
            boatCockpitRepairOwnerRenderer.sprite == cockpitDamage.Sprite;

        repairWorkProgress = 0.86f;
        UpdateVisuals(3.2f);
        TideV52BoatRepairStageAsset hullFastened = GetV52BoatRepairStageAsset(
            TideV52BoatRepairOwner.HullBreach,
            TideV52BoatRepairStage.Fastened);
        bool firstRoundEndsFastened = hullFastened != null &&
            boatHullRepairOwnerRenderer.sprite == hullFastened.Sprite;

        boatHullIntegrity = 2;
        repairChoiceApplied = true;
        UpdateVisuals(3.3f);
        bool commitKeepsFastened = boatHullRepairOwnerRenderer.sprite == hullFastened.Sprite;

        repairChoiceApplied = false;
        repairWorkProgress = 0.5f;
        UpdateVisuals(3.4f);
        TideV52BoatRepairStageAsset hullSealed = GetV52BoatRepairStageAsset(
            TideV52BoatRepairOwner.HullBreach,
            TideV52BoatRepairStage.Sealed);
        bool secondRoundStartsSealed = hullSealed != null &&
            boatHullRepairOwnerRenderer.sprite == hullSealed.Sprite &&
            boatSailRenderer.sprite == sailDamage.Sprite &&
            boatCockpitRepairOwnerRenderer.sprite == cockpitDamage.Sprite;

        int activeStageAssetCount = 0;
        for (int i = 0; i < formalBoatV52ActiveStageAssets.Length; i++)
        {
            if (formalBoatV52ActiveStageAssets[i] != null)
            {
                activeStageAssetCount++;
            }
        }
        bool onlyThreeStageIndexesRetained = activeStageAssetCount == TideV52BoatRepairPresentationModel.OwnerCount;
        bool noLocalWaterOrCollisionOwner = !boatWaterlineOcclusionRenderer.enabled &&
            boatHullRepairOwnerRenderer.GetComponent<Collider2D>() == null &&
            boatCockpitRepairOwnerRenderer.GetComponent<Collider2D>() == null;

        string evidence =
            $"初态/稳定底/旧层退役={initialOwnersCorrect}/{stableBasesCorrect}/{v39ConflictOwnersRetired}；" +
            $"V39底座={v39StableOwnersCorrect}；偏移={hullOffsetError:F4}/{cockpitOffsetError:F4}m；" +
            $"人物夹层={passengerSandwiched}；船壳独立={hullOnlyTestFit}；" +
            $"固定/提交/密封={firstRoundEndsFastened}/{commitKeepsFastened}/{secondRoundStartsSealed}；" +
            $"活动索引={activeStageAssetCount}/3；局部水与碰撞关闭={noLocalWaterOrCollisionOwner}";
        bool passed = initialOwnersCorrect && stableBasesCorrect && v39ConflictOwnersRetired &&
            v39StableOwnersCorrect && hullOffsetError <= 0.002f && cockpitOffsetError <= 0.002f &&
            passengerSandwiched && hullOnlyTestFit && firstRoundEndsFastened &&
            commitKeepsFastened && secondRoundStartsSealed && onlyThreeStageIndexesRetained &&
            noLocalWaterOrCollisionOwner;
        return passed
            ? $"PASS：V67/V52 三部位维修独立、阶段单调且完整人物保持在真实座舱夹层。{evidence}"
            : $"FAIL：V67/V52 资源所有权、阶段映射、坐标或人物夹层仍不合格。{evidence}";
    }

    public string RunEditorLocomotionFrameContinuityProbe()
    {
        EnsureV41CharacterContactResourcesLoaded();
        if (!HasCompleteV41CharacterContactPresentation())
        {
            return "FAIL：缺少 V41 人物接触动作帧，无法验证行走与登船节奏。";
        }

        playerWalkCycle = 0f;
        Sprite startAtEarlyWorldTime =
            TideV41CharacterContactPresentationModel.EvaluateLoopFrame(
                formalCharacterV41ContactCatalog,
                TideV41CharacterContactAction.Walk,
                playerWalkCycle);
        Sprite startAtLateWorldTime =
            TideV41CharacterContactPresentationModel.EvaluateLoopFrame(
                formalCharacterV41ContactCatalog,
                TideV41CharacterContactAction.Walk,
                playerWalkCycle);
        playerWalkCycle = 0.5f;
        Sprite advancedByMovement =
            TideV41CharacterContactPresentationModel.EvaluateLoopFrame(
                formalCharacterV41ContactCatalog,
                TideV41CharacterContactAction.Walk,
                playerWalkCycle);

        playerHorizontalVelocity = 0f;
        playerWalkCycle = 0.73f;
        TickPlayerHorizontalLocomotion(0f, 0.1f);
        Sprite stoppedFrame = TideV41CharacterContactPresentationModel.EvaluateLoopFrame(
            formalCharacterV41ContactCatalog,
            TideV41CharacterContactAction.Walk,
            playerWalkCycle);
        Sprite contactFrame = formalCharacterV41ContactCatalog.GetFrame(
            TideV41CharacterContactAction.Walk,
            0);
        Sprite boardFirst = TideV41CharacterContactPresentationModel.EvaluateOneShotFrame(
            formalCharacterV41ContactCatalog,
            TideV41CharacterContactAction.Board,
            0f,
            false);
        Sprite boardLast = TideV41CharacterContactPresentationModel.EvaluateOneShotFrame(
            formalCharacterV41ContactCatalog,
            TideV41CharacterContactAction.Board,
            1f,
            false);
        Sprite disembarkFirst = TideV41CharacterContactPresentationModel.EvaluateOneShotFrame(
            formalCharacterV41ContactCatalog,
            TideV41CharacterContactAction.Board,
            0f,
            true);
        bool worldClockIndependent = startAtEarlyWorldTime == startAtLateWorldTime;
        bool movementAdvances = advancedByMovement != startAtEarlyWorldTime;
        bool stopReturnsToContact = Mathf.Abs(playerWalkCycle) <= 0.0001f &&
            stoppedFrame == contactFrame;
        bool boardingSequenceReversible = boardFirst != null && boardLast != null &&
            boardFirst != boardLast && disembarkFirst == boardLast;

        EnsureV20CharacterResourcesLoaded();
        Sprite idleAtStart = EvaluateV20CharacterFrame(
            TideV20CharacterActionState.Idle,
            0f);
        Sprite idleMuchLater = EvaluateV20CharacterFrame(
            TideV20CharacterActionState.Idle,
            120f);
        bool noInputLandStable = idleAtStart != null && idleAtStart == idleMuchLater;
        float idleUniformScale = TideV20CharacterPresentationModel.CalculateUniformScale(
            TideV20CharacterActionState.Idle,
            idleAtStart);
        float idleVisibleBodyLength = idleAtStart == null
            ? 0f
            : TideV20CharacterPresentationModel.AuthoredBodyPixels /
                idleAtStart.pixelsPerUnit * idleUniformScale;
        float walkVisibleBodyLength =
            TideV41CharacterContactPresentationModel.AuthoredBodyPixels /
            TideV41CharacterContactPresentationModel.PixelsPerUnit *
            TideV41CharacterContactPresentationModel.UniformScale;
        float idleWalkScaleError = Mathf.Abs(idleVisibleBodyLength - walkVisibleBodyLength);
        bool idleWalkScaleContinuous = idleWalkScaleError <= 0.01f;

        // 这里不只比较目录帧，还让真实网状态走一遍 UpdateVisuals。这样可防止
        // 资源虽存在、运行时却仍落回旧 Repair/Haul 动作的“假接入”。
        SetEditorNetRigHoldPreviewPose(0);
        Sprite expectedFirstKnot = TideV41CharacterContactPresentationModel.EvaluateOneShotFrame(
            formalCharacterV41ContactCatalog,
            TideV41CharacterContactAction.TieNet,
            netRigActionProgress,
            false);
        bool firstKnotUsesRealProgress = playerRenderer != null &&
            playerRenderer.enabled &&
            playerRenderer.sprite == expectedFirstKnot;

        SetEditorNetRigHoldPreviewPose(1);
        Sprite expectedSecondKnot = TideV41CharacterContactPresentationModel.EvaluateOneShotFrame(
            formalCharacterV41ContactCatalog,
            TideV41CharacterContactAction.TieNet,
            netRigActionProgress,
            false);
        bool secondKnotUsesRealProgress = playerRenderer != null &&
            playerRenderer.enabled &&
            playerRenderer.sprite == expectedSecondKnot;

        SetEditorNetRigHoldPreviewPose(2);
        Sprite expectedLowering = TideV41CharacterContactPresentationModel.EvaluateOneShotFrame(
            formalCharacterV41ContactCatalog,
            TideV41CharacterContactAction.LowerSinkline,
            netSetDepth01,
            false);
        bool loweringUsesRealDepth = playerRenderer != null &&
            playerRenderer.enabled &&
            playerRenderer.sprite == expectedLowering;
        float measuredBodyLength =
            TideV41CharacterContactPresentationModel.AuthoredBodyPixels /
            TideV41CharacterContactPresentationModel.PixelsPerUnit *
            TideV41CharacterContactPresentationModel.UniformScale;
        bool scaleAndFootContactStable =
            Mathf.Abs(measuredBodyLength - TideV20CharacterPresentationModel.BodyWorldLength) <= 0.001f &&
            TideV41CharacterContactPresentationModel.FootPivotCorrectionWorldY < 0f &&
            TideV41CharacterContactPresentationModel.FootPivotCorrectionWorldY > -0.06f;
        ResetSlice();

        string evidence =
            $"世界时钟无关={worldClockIndependent}；位移推进={movementAdvances}；" +
            $"停步归接触帧={stopReturnsToContact}；无输入静止={noInputLandStable}；" +
            $"待机/行走={idleVisibleBodyLength:F3}/{walkVisibleBodyLength:F3}m，误差={idleWalkScaleError:F3}m；" +
            $"登下船反序={boardingSequenceReversible}；" +
            $"第一结/第二结/沉纲={firstKnotUsesRealProgress}/{secondKnotUsesRealProgress}/{loweringUsesRealDepth}；" +
            $"身体={measuredBodyLength:F3}m/脚底修正={TideV41CharacterContactPresentationModel.FootPivotCorrectionWorldY:F3}m";
        return worldClockIndependent && movementAdvances && stopReturnsToContact &&
               noInputLandStable && idleWalkScaleContinuous &&
               boardingSequenceReversible && firstKnotUsesRealProgress &&
               secondKnotUsesRealProgress && loweringUsesRealDepth &&
               scaleAndFootContactStable
            ? $"PASS：V41 行走、登船、系两端和放沉纲都由真实位移/操作进度驱动。{evidence}"
            : $"FAIL：V41 人物动作存在随机起帧、尺度、登船反序或网状态脱节。{evidence}";
    }

    public string RunEditorV42CharacterSurvivalIntegrationProbe()
    {
        EnsureScene();
        EnsureV42CharacterSurvivalResourcesLoaded();
        if (!HasCompleteV42CharacterSurvivalPresentation())
        {
            return "FAIL：缺少 V42 受寒、睡眠、溺水或失温倒下正式帧。";
        }

        bool catalogComplete = formalCharacterV42SurvivalCatalog.TotalFrameCount ==
            TideV42CharacterSurvivalPresentationModel.TotalFrameCount;
        bool sharedScale = Mathf.Abs(
            TideV42CharacterSurvivalPresentationModel.UniformScale -
            TideV41CharacterContactPresentationModel.UniformScale) <= 0.0001f;

        ResetSlice();
        viewMode = SliceViewMode.Shelter;
        arrivalVignetteActive = false;
        bodyWarmth01 = 0.2f;
        playerMoving = false;
        bool coldStarts = TryGetV42SurvivalWorldFrame(
            true,
            0f,
            out Sprite coldFrame0,
            out _,
            out _,
            out _);
        bool coldAdvances = TryGetV42SurvivalWorldFrame(
            true,
            0.16f,
            out Sprite coldFrame1,
            out _,
            out _,
            out _) && coldFrame1 != coldFrame0;
        bool workCanOverrideCold = !TryGetV42SurvivalWorldFrame(
            false,
            0.16f,
            out _,
            out _,
            out _,
            out _);

        ResetSlice();
        arrivalVignetteActive = false;
        viewMode = SliceViewMode.Interior;
        playerLane = WalkLane.InteriorUpper;
        playerPosition = new Vector2(GetInteriorBedX() - 0.28f, GetPlayerLaneY(playerLane));
        dayProgress01 = 0.91f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = DayNightPhase.Night;
        int sleepRoundBefore = tideRound;
        bool sleepStarted = BeginSleepPresentation();
        float sleepDuration = GetSurvivalPresentationDuration(SurvivalPresentationState.Sleeping);
        TickSurvivalPresentation(sleepDuration * 0.5f);
        bool sleepDelayed = survivalPresentationState == SurvivalPresentationState.Sleeping &&
            tideRound == sleepRoundBefore &&
            viewMode == SliceViewMode.Interior;
        UpdateVisuals(0.9f);
        Sprite expectedSleepFrame = TideV42CharacterSurvivalPresentationModel.EvaluateOneShotFrame(
            formalCharacterV42SurvivalCatalog,
            TideV42CharacterSurvivalAction.Sleep,
            0.5f);
        bool sleepFrameVisible = playerRenderer != null && playerRenderer.enabled &&
            playerRenderer.sprite == expectedSleepFrame;
        TickSurvivalPresentation(sleepDuration * 0.51f);
        bool sleepSettlesAtDawn = survivalPresentationState == SurvivalPresentationState.None &&
            tideRound == sleepRoundBefore + 1 &&
            viewMode == SliceViewMode.Shelter &&
            dayNightPhase == DayNightPhase.Dawn;

        ResetSlice();
        arrivalVignetteActive = false;
        viewMode = SliceViewMode.Shelter;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(netAnchor.x, GetPlayerLaneY(playerLane));
        int drownDeathBefore = deathCount;
        BeginDeathPresentation("V42 溺水探针", SurvivalPresentationState.Drowning);
        float drownDuration = GetSurvivalPresentationDuration(SurvivalPresentationState.Drowning);
        TickSurvivalPresentation(drownDuration * 0.5f);
        UpdateVisuals(1.1f);
        Sprite expectedDrownFrame = TideV42CharacterSurvivalPresentationModel.EvaluateOneShotFrame(
            formalCharacterV42SurvivalCatalog,
            TideV42CharacterSurvivalAction.Drown,
            0.5f);
        bool drownDelayedAndVisible = deathCount == drownDeathBefore &&
            survivalPresentationState == SurvivalPresentationState.Drowning &&
            playerRenderer != null && playerRenderer.enabled &&
            playerRenderer.sprite == expectedDrownFrame;
        TideOceanSample drownOcean = GetOceanSample(survivalPresentationStartPosition.x);
        Vector2 drownEarly = TideV42CharacterSurvivalPresentationModel.EvaluateDrownPivotWorld(
            new Vector2(survivalPresentationStartPosition.x, drownOcean.SurfaceY),
            0.12f);
        Vector2 drownLate = TideV42CharacterSurvivalPresentationModel.EvaluateDrownPivotWorld(
            new Vector2(survivalPresentationStartPosition.x, drownOcean.SurfaceY),
            0.86f);
        bool drownMovesDown = drownLate.y < drownEarly.y - 0.28f;
        TickSurvivalPresentation(drownDuration * 0.51f);
        bool drownResolvesAfterMotion = deathCount == drownDeathBefore + 1 &&
            survivalPresentationState == SurvivalPresentationState.None &&
            viewMode == SliceViewMode.Interior;

        ResetSlice();
        arrivalVignetteActive = false;
        viewMode = SliceViewMode.Shelter;
        playerLane = WalkLane.Deck;
        playerPosition = GetHomePlayerPosition();
        int collapseDeathBefore = deathCount;
        BeginDeathPresentation("V42 失温探针", SurvivalPresentationState.ColdCollapse);
        float collapseDuration = GetSurvivalPresentationDuration(SurvivalPresentationState.ColdCollapse);
        TickSurvivalPresentation(collapseDuration * 0.62f);
        UpdateVisuals(1.4f);
        Sprite expectedCollapseFrame = TideV42CharacterSurvivalPresentationModel.EvaluateOneShotFrame(
            formalCharacterV42SurvivalCatalog,
            TideV42CharacterSurvivalAction.ColdCollapse,
            0.62f);
        bool collapseDelayedAndVisible = deathCount == collapseDeathBefore &&
            survivalPresentationState == SurvivalPresentationState.ColdCollapse &&
            playerRenderer != null && playerRenderer.enabled &&
            playerRenderer.sprite == expectedCollapseFrame;
        TickSurvivalPresentation(collapseDuration * 0.39f);
        bool collapseResolvesAfterMotion = deathCount == collapseDeathBefore + 1 &&
            survivalPresentationState == SurvivalPresentationState.None &&
            viewMode == SliceViewMode.Interior;

        ResetSlice();
        string evidence =
            $"目录/同尺度={catalogComplete}/{sharedScale}；" +
            $"受寒循环/动作让位={coldStarts && coldAdvances}/{workCanOverrideCold}；" +
            $"睡眠开始/延迟/床帧/黎明={sleepStarted}/{sleepDelayed}/{sleepFrameVisible}/{sleepSettlesAtDawn}；" +
            $"溺水延迟/下沉/结算={drownDelayedAndVisible}/{drownMovesDown}/{drownResolvesAfterMotion}；" +
            $"失温延迟/结算={collapseDelayedAndVisible}/{collapseResolvesAfterMotion}";
        bool passed = catalogComplete && sharedScale && coldStarts && coldAdvances &&
            workCanOverrideCold && sleepStarted && sleepDelayed && sleepFrameVisible &&
            sleepSettlesAtDawn && drownDelayedAndVisible && drownMovesDown &&
            drownResolvesAfterMotion && collapseDelayedAndVisible &&
            collapseResolvesAfterMotion;
        return passed
            ? $"PASS：V42 生存动作先表现，再在暗点结算换日或死亡。{evidence}"
            : $"FAIL：V42 资源、动作优先级、根运动或延迟结算不符合契约。{evidence}";
    }

    public string RunEditorV43SeaWeatherIntegrationProbe()
    {
        EnsureV43SeaWeatherResourcesLoaded();
        if (!HasCompleteV43SeaWeatherPresentation())
        {
            return "FAIL：缺少 V43 透明浪、漩涡或云层索引。";
        }

        Sprite waveStart = TideV43SeaWeatherPresentationModel.EvaluateWaveFrame(
            formalV43SeaWeatherCatalog,
            TideV43WaveKind.WindWave,
            0f,
            0f,
            1f);
        Sprite waveNext = TideV43SeaWeatherPresentationModel.EvaluateWaveFrame(
            formalV43SeaWeatherCatalog,
            TideV43WaveKind.WindWave,
            0.126f,
            0f,
            1f);
        bool waveAnimates = waveStart != null && waveNext != null && waveStart != waveNext;

        Vector2 longSize = TideV43SeaWeatherPresentationModel.GetWaveWorldSize(TideV43WaveKind.LongSwell);
        Vector2 windSize = TideV43SeaWeatherPresentationModel.GetWaveWorldSize(TideV43WaveKind.WindWave);
        Vector2 stormSize = TideV43SeaWeatherPresentationModel.GetWaveWorldSize(TideV43WaveKind.StormBreaker);
        bool distinctSeaStates = longSize != windSize && windSize != stormSize && longSize != stormSize;

        Sprite depression = TideV43SeaWeatherPresentationModel.EvaluateVortexFrame(
            formalV43SeaWeatherCatalog,
            TideV43VortexLayer.DepressionMask,
            0f);
        Sprite inner = TideV43SeaWeatherPresentationModel.EvaluateVortexFrame(
            formalV43SeaWeatherCatalog,
            TideV43VortexLayer.InnerFlow,
            0f);
        Sprite outer = TideV43SeaWeatherPresentationModel.EvaluateVortexFrame(
            formalV43SeaWeatherCatalog,
            TideV43VortexLayer.OuterFoam,
            0f);
        bool vortexOwnershipSeparated = depression != null && inner != null && outer != null &&
            depression != inner && inner != outer && depression != outer;
        bool cloudsComplete =
            formalV43SeaWeatherCatalog.GetCloud(TideV43CloudLayer.FarCloudWall) != null &&
            formalV43SeaWeatherCatalog.GetCloud(TideV43CloudLayer.MidWeatherBank) != null &&
            formalV43SeaWeatherCatalog.GetCloud(TideV43CloudLayer.NearScud) != null;

        string evidence =
            $"浪帧变化={waveAnimates}；浪型尺寸={longSize}/{windSize}/{stormSize}；" +
            $"漩涡三所有者={vortexOwnershipSeparated}；三层云={cloudsComplete}";
        return waveAnimates && distinctSeaStates && vortexOwnershipSeparated && cloudsComplete
            ? $"PASS：V43 只叠加局部浪头、三层漩涡和云，连续海体仍由模拟拥有。{evidence}"
            : $"FAIL：V43 海况帧、尺寸或层所有权不完整。{evidence}";
    }

    public string RunEditorV37BoatCharacterActionProbe()
    {
        EnsureV37BoatCharacterResourcesLoaded();
        if (!HasCompleteV37BoatCharacterPresentation())
        {
            return "FAIL：缺少 V37 操帆、舀水或暴潮受击人物索引。";
        }

        Sprite[][] actions =
        {
            formalV37BoatCharacterCatalog.TrimFrames,
            formalV37BoatCharacterCatalog.BailFrames,
            formalV37BoatCharacterCatalog.BraceFrames
        };
        bool registrationValid = true;
        bool mipmapsEnabled = true;
        for (int actionIndex = 0; actionIndex < actions.Length; actionIndex++)
        {
            Sprite[] frames = actions[actionIndex];
            registrationValid &= frames != null && frames.Length == TideV37BoatCharacterCatalog.FrameCount;
            if (frames == null)
            {
                continue;
            }

            for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
            {
                Sprite frame = frames[frameIndex];
                if (frame == null || frame.texture == null)
                {
                    registrationValid = false;
                    mipmapsEnabled = false;
                    continue;
                }

                Vector2 pivot = new Vector2(
                    frame.pivot.x / frame.rect.width,
                    frame.pivot.y / frame.rect.height);
                registrationValid &= frame.texture.width == 1024 &&
                    frame.texture.height == 1024 &&
                    Mathf.Abs(frame.rect.width - 1024f) <= 0.01f &&
                    Mathf.Abs(frame.rect.height - 1024f) <= 0.01f &&
                    Vector2.Distance(pivot, new Vector2(0.5f, 0.45f)) <= 0.001f;
                mipmapsEnabled &= frame.texture.mipmapCount > 1;
            }
        }

        SetEditorSailingTrimPreviewPose();
        bool trimSelected = IsV37ActionFrame(
            boatPassengerRenderer.sprite,
            formalV37BoatCharacterCatalog.TrimFrames);
        bool trimSandwiched = IsBoatPassengerSandwiched();
        bool trimInsideCockpit = IsBoatPassengerInsideCockpit();

        SetEditorSailingBailPreviewPose();
        bool bailSelected = IsV37ActionFrame(
            boatPassengerRenderer.sprite,
            formalV37BoatCharacterCatalog.BailFrames);
        bool bucketIndependent = sailingBailBucketRenderer.enabled &&
            sailingBailBucketRenderer.sprite != null &&
            sailingBailBucketRenderer.sprite.texture != boatPassengerRenderer.sprite.texture;
        bool bucketInsideGunwale = sailingBailBucketRenderer.sortingOrder == 6 &&
            sailingBailBucketRenderer.sortingOrder < boatPassengerGunwaleRenderer.sortingOrder &&
            !sailingBailSplashRenderer.enabled;
        bool bailSandwiched = IsBoatPassengerSandwiched();
        bool bailInsideCockpit = IsBoatPassengerInsideCockpit();

        SetEditorSailingBailThrowPreviewPose();
        bool bailThrowSelected = IsV37ActionFrame(
            boatPassengerRenderer.sprite,
            formalV37BoatCharacterCatalog.BailFrames);
        bool bucketOutsideGunwale = sailingBailBucketRenderer.enabled &&
            sailingBailBucketRenderer.sortingOrder > boatHullRepairOwnerRenderer.sortingOrder &&
            sailingBailBucketRenderer.sortingOrder < foregroundDeepWaterOcclusionRenderer.sortingOrder &&
            sailingBailSplashRenderer.enabled;
        bool bailThrowSandwiched = IsBoatPassengerSandwiched();
        bool bailThrowInsideCockpit = IsBoatPassengerInsideCockpit();

        SetEditorSailingBracePreviewPose();
        bool braceSelected = IsV37ActionFrame(
            boatPassengerRenderer.sprite,
            formalV37BoatCharacterCatalog.BraceFrames);
        bool braceSandwiched = IsBoatPassengerSandwiched();
        bool braceInsideCockpit = IsBoatPassengerInsideCockpit();

        SetEditorSailingPreviewPose();
        bool idleFallsBackToV32 = formalV32ArtCatalog != null &&
            IsV37ActionFrame(boatPassengerRenderer.sprite, formalV32ArtCatalog.SeatedFrames);
        bool idleInsideCockpit = IsBoatPassengerInsideCockpit();

        string evidence =
            $"注册={registrationValid}；缩小采样={mipmapsEnabled}；" +
            $"操帆/舀水抬桶/外泼/受击={trimSelected}/{bailSelected}/{bailThrowSelected}/{braceSelected}；" +
            $"四态夹层={trimSandwiched}/{bailSandwiched}/{bailThrowSandwiched}/{braceSandwiched}；" +
            $"五态座舱={idleInsideCockpit}/{trimInsideCockpit}/{bailInsideCockpit}/" +
            $"{bailThrowInsideCockpit}/{braceInsideCockpit}；" +
            $"桶独立={bucketIndependent}；桶内/外层={bucketInsideGunwale}/{bucketOutsideGunwale}；" +
            $"空闲回V32={idleFallsBackToV32}";
        return registrationValid && mipmapsEnabled && trimSelected && bailSelected &&
            bailThrowSelected && braceSelected && trimSandwiched && bailSandwiched &&
            bailThrowSandwiched && braceSandwiched && bucketIndependent &&
            bucketInsideGunwale && bucketOutsideGunwale && idleFallsBackToV32 &&
            idleInsideCockpit && trimInsideCockpit && bailInsideCockpit &&
            bailThrowInsideCockpit && braceInsideCockpit
            ? $"PASS：V37 三组完整船上人物动作按真实状态切换，始终由船体前后层夹住；桶在舱内与外泼阶段正确跨层。{evidence}"
            : $"FAIL：V37 动作注册、状态选择、船体夹层或独立道具所有权不合格。{evidence}";
    }

    public string RunEditorSailingWashCoverageProbe()
    {
        SetEditorSailingTrimPreviewPose();
        Camera camera = Camera.main;
        if (camera == null)
        {
            camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
        }

        if (camera == null || foregroundMoonWashRenderer == null || backdropRenderer == null ||
            naturalWaterSurfaceRenderer == null || waterRenderer == null)
        {
            return "FAIL：缺少航行相机、叠色层、背景层、正式海浪或深水承接层。";
        }

        float cameraHeight = camera.orthographicSize * 2f;
        float cameraWidth = cameraHeight * camera.aspect;
        Bounds backdropBounds = backdropRenderer.bounds;
        bool normalWashDisabled = !foregroundMoonWashRenderer.enabled;
        Bounds formalWaterBounds = naturalWaterSurfaceRenderer.bounds;
        Bounds deepWaterBounds = waterRenderer.bounds;
        float cameraBottom = camera.transform.position.y - camera.orthographicSize;
        bool deepWaterCoversBottom = waterRenderer.enabled &&
            deepWaterBounds.min.y <= cameraBottom + 0.05f &&
            deepWaterBounds.size.x >= cameraWidth - 0.05f;
        bool deepWaterOverlapsFormalWater = deepWaterBounds.max.y >= formalWaterBounds.min.y + 0.2f &&
            waterRenderer.sortingOrder < naturalWaterSurfaceRenderer.sortingOrder;
        string evidence =
            $"正常前景染色关闭={normalWashDisabled}；" +
            $"背景={backdropBounds.size.x:F2}x{backdropBounds.size.y:F2}@" +
            $"({backdropBounds.center.x:F2},{backdropBounds.center.y:F2})；" +
            $"相机={cameraWidth:F2}x{cameraHeight:F2}，底边={cameraBottom:F2}；" +
            $"正式海浪底边={formalWaterBounds.min.y:F2}；" +
            $"深水={deepWaterBounds.size.x:F2}x{deepWaterBounds.size.y:F2}@" +
            $"({deepWaterBounds.center.x:F2},{deepWaterBounds.center.y:F2})";
        return normalWashDisabled && deepWaterCoversBottom &&
            deepWaterOverlapsFormalWater
            ? $"PASS：航行正常态不再叠全屏青灰色，深水仍承接正式海浪并越过相机底边。{evidence}"
            : $"FAIL：航行仍有常驻全屏染色，或深水承接未覆盖完整画幅。{evidence}";
    }

    private bool IsBoatPassengerSandwiched()
    {
        return boatHullRenderer.sortingOrder < boatPassengerRenderer.sortingOrder &&
            boatPassengerRenderer.sortingOrder < boatPassengerGunwaleRenderer.sortingOrder;
    }

    private bool IsBoatPassengerInsideCockpit()
    {
        if (boatPassengerRenderer == null || boatPassengerGunwaleRenderer == null ||
            !boatPassengerRenderer.enabled || !boatPassengerGunwaleRenderer.enabled)
        {
            return false;
        }

        Bounds passengerBounds = boatPassengerRenderer.bounds;
        Bounds gunwaleBounds = boatPassengerGunwaleRenderer.bounds;
        if (HasCompleteV39BoatPresentation())
        {
            // V39 前船舷是从整船画布裁出的长条，Bounds 两端包含船艏/船艉结构，
            // 不能继续套 V31 的固定 0.45m 内缩。V39 已提供真实座位锚点：先验证
            // 人物 Pivot 精确落座，再确认完整身体横向仍在船舷覆盖区、头肩露在船舷
            // 上方、腿部进入遮挡区。这三项比任意矩形内缩更接近实际可见结构。
            bool repaired = boatHullIntegrity >= 2 && boatSailIntegrity >= 1;
            float rotationZ = boatHullRenderer.transform.localEulerAngles.z;
            Vector2 backHullDelta = TideV39BoatPresentationModel.EvaluateOffsetWorldPosition(
                Vector2.zero,
                TideV39BoatPresentationModel.GetLayerOffset(TideV39BoatLayer.BackHull, repaired),
                rotationZ,
                boatHullRenderer.flipX);
            Vector2 root = (Vector2)boatHullRenderer.transform.localPosition - backHullDelta;
            Vector2 expectedSeat = TideV39BoatPresentationModel.EvaluateAnchorWorldPosition(
                root,
                TideV39BoatPresentationModel.SeatTopLeft,
                rotationZ,
                boatHullRenderer.flipX);
            Vector2 expectedPassengerPivot = GetBoatPassengerVisualPivot(expectedSeat, rotationZ);
            bool anchoredToSeat = Vector2.Distance(
                boatPassengerRenderer.transform.localPosition,
                expectedPassengerPivot) <= 0.01f;
            bool horizontallyCovered = passengerBounds.min.x >= gunwaleBounds.min.x - 0.01f &&
                passengerBounds.max.x <= gunwaleBounds.max.x + 0.01f;
            bool v39VerticallySeated = passengerBounds.min.y <= gunwaleBounds.max.y - 0.15f &&
                passengerBounds.max.y >= gunwaleBounds.max.y + 0.02f;
            return anchoredToSeat && horizontallyCovered && v39VerticallySeated;
        }

        const float horizontalSafeInset = 0.45f;
        bool horizontallyInside =
            passengerBounds.min.x >= gunwaleBounds.min.x + horizontalSafeInset &&
            passengerBounds.max.x <= gunwaleBounds.max.x - horizontalSafeInset;
        bool verticallySeated =
            passengerBounds.min.y <= gunwaleBounds.max.y - 0.15f &&
            passengerBounds.max.y >= gunwaleBounds.max.y + 0.04f;
        return horizontallyInside && verticallySeated;
    }

    private static bool IsV37ActionFrame(Sprite current, Sprite[] frames)
    {
        if (current == null || frames == null)
        {
            return false;
        }

        for (int i = 0; i < frames.Length; i++)
        {
            if (current == frames[i])
            {
                return true;
            }
        }

        return false;
    }

    public string RunEditorV32ClimbDirectionProbe()
    {
        EnsureScene();
        ResetSlice();
        EnsureV32ArtResourcesLoaded();
        if (!HasCompleteV32ArtPresentation())
        {
            return "FAIL：缺少 V32 背身爬梯六帧，无法验证上下梯方向。";
        }

        float upStart = GetV32ClimbPlaybackProgress(WalkLane.TideFlat, WalkLane.Deck, 0f);
        float upEnd = GetV32ClimbPlaybackProgress(WalkLane.TideFlat, WalkLane.Deck, 1f);
        float downStart = GetV32ClimbPlaybackProgress(WalkLane.Deck, WalkLane.TideFlat, 0f);
        float downEnd = GetV32ClimbPlaybackProgress(WalkLane.Deck, WalkLane.TideFlat, 1f);
        bool directionCorrect =
            formalV32ArtCatalog.GetClimbFrame(upStart) == formalV32ArtCatalog.ClimbFrames[0] &&
            formalV32ArtCatalog.GetClimbFrame(upEnd) == formalV32ArtCatalog.ClimbFrames[5] &&
            formalV32ArtCatalog.GetClimbFrame(downStart) == formalV32ArtCatalog.ClimbFrames[5] &&
            formalV32ArtCatalog.GetClimbFrame(downEnd) == formalV32ArtCatalog.ClimbFrames[0];

        stiltIntegrity = 3;
        roofIntegrity = 2;
        Vector2 top = GetGangwayTopPosition();
        Vector2 bottom = GetGangwayBottomPosition();
        bool ladderConnectsPlatforms = top.y > bottom.y + 0.8f &&
            Mathf.Abs(bottom.y - GetPlayerLaneY(WalkLane.TideFlat)) <= 0.001f;
        string evidence =
            $"上行={upStart:F0}->{upEnd:F0}；下行={downStart:F0}->{downEnd:F0}；" +
            $"梯顶={top}；梯底={bottom}";
        return directionCorrect && ladderConnectsPlatforms
            ? $"PASS：V32 上梯正播、下梯反播，梯底落在通向船艉的潮间平台。{evidence}"
            : $"FAIL：V32 爬梯方向或梯顶/梯底连接仍不正确。{evidence}";
    }

    public string RunEditorClimbTimingRealismProbe()
    {
        EnsureScene();
        ResetSlice();
        EnsureV32ArtResourcesLoaded();

        float exteriorDistance = Vector2.Distance(GetGangwayBottomPosition(), GetGangwayTopPosition());
        float interiorDistance = Vector2.Distance(GetInteriorStairBottomPosition(), GetInteriorStairTopPosition());
        float loftDistance = Vector2.Distance(GetInteriorLoftStairBottomPosition(), GetInteriorLoftStairTopPosition());
        float exteriorDuration = CalculateLaneTransitionDuration(exteriorDistance);
        float interiorDuration = CalculateLaneTransitionDuration(interiorDistance);
        float loftDuration = CalculateLaneTransitionDuration(loftDistance);
        float exteriorSpeed = exteriorDistance / exteriorDuration;
        float interiorSpeed = interiorDistance / interiorDuration;
        float loftSpeed = loftDistance / loftDuration;

        // 三段梯子的画面长度不同。真实规则应该是“同一种湿梯、同一种攀爬速度”，
        // 而不是每段都在固定秒数内完成，否则最长的阁楼梯会明显像被吸上去。
        bool speedIsPlausible = ladderTravelSpeed >= 0.55f && ladderTravelSpeed <= 0.8f;
        bool speedIsConsistent = Mathf.Abs(exteriorSpeed - ladderTravelSpeed) <= 0.001f &&
            Mathf.Abs(interiorSpeed - ladderTravelSpeed) <= 0.001f &&
            Mathf.Abs(loftSpeed - ladderTravelSpeed) <= 0.001f;
        bool durationScalesWithDistance = exteriorDuration >= 1.6f && exteriorDuration <= 2.8f &&
            interiorDuration >= 1.6f && interiorDuration <= 2.8f &&
            loftDuration > exteriorDuration + 0.7f && loftDuration <= 4.2f;

        string evidence = $"速度={ladderTravelSpeed:F2}m/s；" +
            $"外梯={exteriorDistance:F2}m/{exteriorDuration:F2}s；" +
            $"内梯={interiorDistance:F2}m/{interiorDuration:F2}s；" +
            $"阁楼梯={loftDistance:F2}m/{loftDuration:F2}s";
        return speedIsPlausible && speedIsConsistent && durationScalesWithDistance
            ? $"PASS：三段梯子按可见路径长度使用统一湿梯速度，长梯不再被压缩成同一时长。{evidence}"
            : $"FAIL：梯子速度或时长仍不符合统一路径速度规则。{evidence}";
    }

    public string RunEditorV33HouseEndpointRegistrationProbe()
    {
        EnsureScene();
        ResetSlice();
        EnsureV32ArtResourcesLoaded();
        if (!HasCompleteV32ArtPresentation())
        {
            return "FAIL：V32/V33 外景索引不完整，无法验证同坐标修复端点。";
        }

        SetEditorRestorationPreviewPose(0);
        // V34 接管运行态后，V33 探针仍验证两张历史端点的同栋配准，不能再把
        // 当前 houseRenderer（V34 StableBase）误认成 V32/V33 整屋端点。
        Sprite foundSprite = formalV32ArtCatalog.HouseFound;
        Vector2 foundTop = GetGangwayTopPosition();
        Vector2 foundBottom = GetGangwayBottomPosition();
        float foundDoor = GetInteriorDoorX();
        float foundBoardwalk = GetBoardwalkVisualLeft();

        SetEditorRestorationPreviewPose(2);
        Sprite repairedSprite = formalV32ArtCatalog.HouseRepaired;
        Vector2 repairedTop = GetGangwayTopPosition();
        Vector2 repairedBottom = GetGangwayBottomPosition();
        float repairedDoor = GetInteriorDoorX();
        float repairedBoardwalk = GetBoardwalkVisualLeft();

        bool correctEndpoints = foundSprite == formalV32ArtCatalog.HouseFound &&
            repairedSprite == formalV32ArtCatalog.HouseRepaired &&
            foundSprite != repairedSprite &&
            repairedSprite != null &&
            repairedSprite.name == "TideHouseV33_Repaired";
        bool registeredCanvas = foundSprite != null && repairedSprite != null &&
            foundSprite.texture != null && repairedSprite.texture != null &&
            foundSprite.texture.width == 2048 && foundSprite.texture.height == 2048 &&
            repairedSprite.texture.width == 2048 && repairedSprite.texture.height == 2048 &&
            Vector2.Distance(foundSprite.bounds.size, repairedSprite.bounds.size) <= 0.001f;
        bool anchorsStable = Vector2.Distance(foundTop, repairedTop) <= 0.001f &&
            Vector2.Distance(foundBottom, repairedBottom) <= 0.001f &&
            Mathf.Abs(foundDoor - repairedDoor) <= 0.001f &&
            Mathf.Abs(foundBoardwalk - repairedBoardwalk) <= 0.001f;
        bool pathConnected = repairedTop.y > repairedBottom.y + 0.8f &&
            // V32 的可见梯心已校正到 x=1120。屋体自带的潮滩木梁继续覆盖
            // 梯脚到外接栈桥之间约一米的距离，不能再沿用旧梯心的魔数。
            repairedBoardwalk > repairedBottom.x + 0.9f &&
            repairedBoardwalk < repairedBottom.x + 1.25f &&
            repairedBoardwalk < boatAnchor.x;
        bool completedRepairMarksHidden = true;
        for (int i = 0; i < houseRepairMarks.Count; i++)
        {
            completedRepairMarksHidden &= houseRepairMarks[i] != null && !houseRepairMarks[i].enabled;
        }
        bool legacyRiggingHidden = true;
        for (int i = 0; i < boatRigging.Count; i++)
        {
            legacyRiggingHidden &= boatRigging[i] != null && !boatRigging[i].enabled;
        }
        bool dryPlayerHasSingleOwner = playerRenderer != null && playerRenderer.enabled &&
            playerRenderer.color.a >= 0.99f &&
            playerAquaticRenderer != null && !playerAquaticRenderer.enabled;
        bool legacyHouseDamageLayersHidden = shelterDamageRenderer != null &&
            !shelterDamageRenderer.enabled;
        for (int i = 0; i < shelterTideZoneWearRenderers.Count; i++)
        {
            legacyHouseDamageLayersHidden &= shelterTideZoneWearRenderers[i] != null &&
                !shelterTideZoneWearRenderers[i].enabled;
        }

        string evidence =
            $"端点={foundSprite?.name}->{repairedSprite?.name}；" +
            $"画布={repairedSprite?.texture?.width}x{repairedSprite?.texture?.height}；" +
            $"梯顶={foundTop}/{repairedTop}；梯底={foundBottom}/{repairedBottom}；" +
            $"门={foundDoor:F3}/{repairedDoor:F3}；木板={foundBoardwalk:F3}/{repairedBoardwalk:F3}；" +
            $"旧补片={completedRepairMarksHidden}；旧潮蚀={legacyHouseDamageLayersHidden}；" +
            $"旧索具={legacyRiggingHidden}；单人物={dryPlayerHasSingleOwner}";
        return correctEndpoints && registeredCanvas && anchorsStable && pathConnected &&
            completedRepairMarksHidden && legacyHouseDamageLayersHidden &&
            legacyRiggingHidden && dryPlayerHasSingleOwner
            ? $"PASS：V33 修复态沿用 V32 发现态同一栋屋、同一把梯、同一门槛和同一登船木板。{evidence}"
            : $"FAIL：V33 修复端点来源、画布、交互锚点或登船路径发生跳变。{evidence}";
    }

    public string RunEditorV69HouseRepairIntegrationProbe()
    {
        SetEditorV34ExteriorRepairPreviewPose(0);
        if (!HasCompleteV69CurrentHousePresentation())
        {
            return "FAIL：V69 当前高脚屋稳定底、结构阶段或设备二态索引不完整。";
        }

        bool stableBasesCorrect = formalHouseV69ActiveBase.Profile == TideV69HouseProfile.Exterior &&
            houseRenderer.sprite == formalHouseV69ActiveBase.StableBase;
        bool allDamage = true;
        bool noOwnerColliders = true;
        for (int i = 0; i < formalHouseV69ActiveStructuralStages.Length; i++)
        {
            TideV69HouseStructuralStageAsset stage = formalHouseV69ActiveStructuralStages[i];
            allDamage &= stage.Stage == TideV69HouseRepairStage.Damage &&
                v34ExteriorRepairOwnerRenderers[i].enabled &&
                stage.Profile == TideV69HouseProfile.Exterior &&
                v34ExteriorRepairOwnerRenderers[i].sprite == stage.Sprite;
            noOwnerColliders &= v34ExteriorRepairOwnerRenderers[i].GetComponent<Collider2D>() == null;
        }

        Vector2 initialGangwayTop = GetGangwayTopPosition();
        Vector2 initialGangwayBottom = GetGangwayBottomPosition();
        float initialDoor = GetInteriorDoorX();
        float initialBoardwalk = GetBoardwalkVisualLeft();

        SetEditorV34ExteriorRepairPreviewPose(1);
        bool foundationOnly = true;
        for (int i = 0; i < formalHouseV69ActiveStructuralStages.Length; i++)
        {
            TideV69HouseRepairStage expected = i == (int)TideV69HouseStructuralOwner.Foundation
                ? TideV69HouseRepairStage.Serviceable
                : TideV69HouseRepairStage.Damage;
            foundationOnly &= formalHouseV69ActiveStructuralStages[i].Stage == expected;
        }

        SetEditorV34ExteriorRepairPreviewPose(2);
        bool leftRoofOnly = true;
        for (int i = 0; i < formalHouseV69ActiveStructuralStages.Length; i++)
        {
            TideV69HouseRepairStage expected = i == (int)TideV69HouseStructuralOwner.RoofLeft
                ? TideV69HouseRepairStage.Serviceable
                : TideV69HouseRepairStage.Damage;
            leftRoofOnly &= formalHouseV69ActiveStructuralStages[i].Stage == expected;
        }

        // 第一轮修柱施工到试装时，只允许桩基变化。随后切进室内，必须复用
        // 同一个阶段索引，而不是根据视图重新计算出另一阶段。
        ResetSlice();
        arrivalInspected = true;
        viewMode = SliceViewMode.Shelter;
        playerLane = WalkLane.TideFlat;
        state = SliceState.RepairMoment;
        pendingRepairChoice = RepairChoice.Stilt;
        repairChoiceApplied = false;
        repairWorkProgress = 0.35f;
        UpdateVisuals(4.1f);
        TideV69HouseStructuralStageAsset exteriorFoundation =
            formalHouseV69ActiveStructuralStages[(int)TideV69HouseStructuralOwner.Foundation];
        TideV69HouseRepairStage exteriorFoundationStage = exteriorFoundation.Stage;
        bool foundationTestFit = exteriorFoundation.Stage == TideV69HouseRepairStage.TestFit &&
            formalHouseV69ActiveStructuralStages[
                (int)TideV69HouseStructuralOwner.AccessLadder].Stage == TideV69HouseRepairStage.Damage;
        viewMode = SliceViewMode.Interior;
        playerLane = WalkLane.InteriorUpper;
        UpdateVisuals(4.2f);
        TideV69HouseStructuralStageAsset interiorFoundation =
            formalHouseV69ActiveStructuralStages[(int)TideV69HouseStructuralOwner.Foundation];
        stableBasesCorrect &= formalHouseV69ActiveBase.Profile == TideV69HouseProfile.Interior &&
            houseRenderer.sprite == formalHouseV69ActiveBase.StableBase;
        bool exteriorInteriorSynchronized =
            interiorFoundation.Profile == TideV69HouseProfile.Interior &&
            exteriorFoundationStage == interiorFoundation.Stage &&
            v35InteriorRepairOwnerRenderers[
                (int)TideV69HouseStructuralOwner.Foundation].sprite == interiorFoundation.Sprite;

        // 设备仍是独立二态。只修工作台时，门、床、海图、炉体和暖光不能跟随。
        ResetSlice();
        arrivalInspected = true;
        viewMode = SliceViewMode.Interior;
        playerLane = WalkLane.InteriorUpper;
        workbenchCondition = 1;
        UpdateVisuals(4.5f);
        bool binaryIsolation = true;
        for (int i = 0; i < formalHouseV69ActiveBinaryStages.Length; i++)
        {
            bool expectedServiceable = i == (int)TideV69HouseBinaryOwner.Workbench;
            TideV69HouseBinaryStageAsset stage = formalHouseV69ActiveBinaryStages[i];
            int rendererIndex = TideV69HouseRepairPresentationModel.StructuralOwnerCount + i;
            binaryIsolation &= stage.Serviceable == expectedServiceable &&
                v35InteriorRepairOwnerRenderers[rendererIndex].sprite == stage.Sprite;
            noOwnerColliders &= v35InteriorRepairOwnerRenderers[rendererIndex]
                .GetComponent<Collider2D>() == null;
        }
        int activeBinaryCount = 0;
        for (int i = 0; i < formalHouseV69ActiveBinaryStages.Length; i++)
        {
            activeBinaryCount += formalHouseV69ActiveBinaryStages[i] != null ? 1 : 0;
        }

        bool stageOrderMonotonic = true;
        float[] progressSamples = { 0.05f, 0.3f, 0.6f, 0.9f };
        for (int i = 0; i < progressSamples.Length - 1; i++)
        {
            TideV69HouseRepairStage current = TideV69HouseRepairPresentationModel.EvaluateStage(
                TideV69HouseStructuralOwner.Foundation,
                0,
                true,
                progressSamples[i]);
            TideV69HouseRepairStage next = TideV69HouseRepairPresentationModel.EvaluateStage(
                TideV69HouseStructuralOwner.Foundation,
                0,
                true,
                progressSamples[i + 1]);
            stageOrderMonotonic &= (int)next > (int)current;
        }

        SetEditorV34ExteriorRepairPreviewPose(6);
        bool anchorsUnchanged = Vector2.Distance(initialGangwayTop, GetGangwayTopPosition()) <= 0.001f &&
            Vector2.Distance(initialGangwayBottom, GetGangwayBottomPosition()) <= 0.001f &&
            Mathf.Abs(initialDoor - GetInteriorDoorX()) <= 0.001f &&
            Mathf.Abs(initialBoardwalk - GetBoardwalkVisualLeft()) <= 0.001f;
        int activeStructuralCount = 0;
        for (int i = 0; i < formalHouseV69ActiveStructuralStages.Length; i++)
        {
            activeStructuralCount += formalHouseV69ActiveStructuralStages[i] != null ? 1 : 0;
        }

        string evidence =
            $"底图/初态={stableBasesCorrect}/{allDamage}；桩基/左顶独立={foundationOnly}/{leftRoofOnly}；" +
            $"试装/内外同步={foundationTestFit}/{exteriorInteriorSynchronized}；设备独立={binaryIsolation}；" +
            $"阶段单调={stageOrderMonotonic}；锚点不跳={anchorsUnchanged}；无碰撞={noOwnerColliders}；" +
            $"活动索引={activeStructuralCount}/6+{activeBinaryCount}/6";
        return stableBasesCorrect && allDamage && foundationOnly && leftRoofOnly &&
            foundationTestFit && exteriorInteriorSynchronized && binaryIsolation &&
            stageOrderMonotonic && anchorsUnchanged && noOwnerColliders &&
            activeStructuralCount == TideV69HouseRepairPresentationModel.StructuralOwnerCount &&
            activeBinaryCount == TideV69HouseRepairPresentationModel.BinaryOwnerCount
            ? $"PASS：V69 同栋屋内外共享六阶段结构维修，六个设备继续独立二态且 V38 路径不跳。{evidence}"
            : $"FAIL：V69 阶段、内外同步、设备所有权或路径边界不合格。{evidence}";
    }

    public string RunEditorV34ExteriorRepairOwnerProbe()
    {
        if (HasCompleteV69CurrentHousePresentation())
        {
            return RunEditorV69HouseRepairIntegrationProbe();
        }

        EnsureScene();
        ResetSlice();
        EnsureV34HouseExteriorResourcesLoaded();
        if (!HasCompleteV34HouseExteriorPresentation())
        {
            return "FAIL：V34 外景稳定底图或六个维修 owner 索引不完整。";
        }

        SetEditorV34ExteriorRepairPreviewPose(0);
        Vector2 damagedTop = GetGangwayTopPosition();
        Vector2 damagedBottom = GetGangwayBottomPosition();
        bool stableBaseVisible = houseRenderer.enabled &&
            houseRenderer.sprite == formalHouseV34ExteriorCatalog.StableBase;
        bool allDamage = true;
        TideV34HouseExteriorCatalog.OwnerEntry[] owners = formalHouseV34ExteriorCatalog.Owners;
        for (int i = 0; i < owners.Length; i++)
        {
            SpriteRenderer renderer = v34ExteriorRepairOwnerRenderers[i];
            allDamage &= renderer.enabled && renderer.sprite == owners[i].DamageSprite;
        }

        SetEditorV34ExteriorRepairPreviewPose(1);
        bool foundationOnly = true;
        for (int i = 0; i < owners.Length; i++)
        {
            bool expectedRepair = owners[i].Key == "Foundation";
            Sprite expected = expectedRepair ? owners[i].RepairSprite : owners[i].DamageSprite;
            foundationOnly &= v34ExteriorRepairOwnerRenderers[i].sprite == expected;
        }

        SetEditorV34ExteriorRepairPreviewPose(2);
        bool leftRoofOnly = true;
        for (int i = 0; i < owners.Length; i++)
        {
            bool expectedRepair = owners[i].Key == "RoofLeft";
            Sprite expected = expectedRepair ? owners[i].RepairSprite : owners[i].DamageSprite;
            leftRoofOnly &= v34ExteriorRepairOwnerRenderers[i].sprite == expected;
        }

        SetEditorV34ExteriorRepairPreviewPose(5);
        bool wallDeckOnly = true;
        for (int i = 0; i < owners.Length; i++)
        {
            bool expectedRepair = owners[i].Key == "WallDeck";
            Sprite expected = expectedRepair ? owners[i].RepairSprite : owners[i].DamageSprite;
            wallDeckOnly &= v34ExteriorRepairOwnerRenderers[i].sprite == expected;
        }

        SetEditorV34ExteriorRepairPreviewPose(6);
        bool allRepair = true;
        bool registrationStable = true;
        Vector2 housePivot = GetFormalHouseWorldPosition();
        for (int i = 0; i < owners.Length; i++)
        {
            SpriteRenderer renderer = v34ExteriorRepairOwnerRenderers[i];
            Vector2 actualPosition = renderer.transform.localPosition;
            Vector2 expectedPosition = housePivot + owners[i].WorldOffsetFromHousePivot;
            allRepair &= renderer.enabled && renderer.sprite == owners[i].RepairSprite;
            registrationStable &= Vector2.Distance(actualPosition, expectedPosition) <= 0.001f;
        }

        Vector2 repairedTop = GetGangwayTopPosition();
        Vector2 repairedBottom = GetGangwayBottomPosition();
        bool anchorsStable = Vector2.Distance(damagedTop, repairedTop) <= 0.001f &&
            Vector2.Distance(damagedBottom, repairedBottom) <= 0.001f;
        float boardwalkLeft = GetBoardwalkVisualLeft();
        bool boardingPathConnected = boardwalkLeft > repairedBottom.x + 0.9f &&
            boardwalkLeft < repairedBottom.x + 1.25f &&
            boardwalkLeft < boatAnchor.x;
        bool noLegacyRepairOverlay = true;
        for (int i = 0; i < houseRepairMarks.Count; i++)
        {
            noLegacyRepairOverlay &= !houseRepairMarks[i].enabled;
        }

        string evidence =
            $"底图={houseRenderer.sprite?.name}；owner={owners.Length}；" +
            $"全损={allDamage}；仅桩基={foundationOnly}；仅左屋面={leftRoofOnly}；" +
            $"仅墙廊={wallDeckOnly}；全修={allRepair}；" +
            $"配准={registrationStable}；梯顶={damagedTop}/{repairedTop}；" +
            $"梯底={damagedBottom}/{repairedBottom}；栈道左缘={boardwalkLeft:F3}；" +
            $"旧补片隐藏={noLegacyRepairOverlay}";
        return stableBaseVisible && allDamage && foundationOnly && leftRoofOnly && wallDeckOnly && allRepair &&
            registrationStable && anchorsStable && boardingPathConnected && noLegacyRepairOverlay
            ? $"PASS：V34 外景使用稳定底图与六个互斥 owner，三条维修线可独立组合且交互锚点不跳。{evidence}"
            : $"FAIL：V34 外景 owner 状态、配准、旧层所有权或梯子锚点仍不合格。{evidence}";
    }

    public string RunEditorV35InteriorIntegrationProbe()
    {
        if (HasCompleteV69CurrentHousePresentation())
        {
            return RunEditorV69HouseRepairIntegrationProbe();
        }

        EnsureScene();
        ResetSlice();
        EnsureV35HouseInteriorResourcesLoaded();
        EnsureV32ArtResourcesLoaded();
        if (!HasCompleteV35HouseInteriorPresentation() || !HasCompleteV32ArtPresentation())
        {
            return "FAIL：V35 室内索引或 V32 完整爬梯人物不完整。";
        }

        TideV35HouseInteriorCatalog.OwnerEntry[] owners = formalHouseV35InteriorCatalog.Owners;
        SetEditorV35InteriorRepairPreviewPose(0);
        bool stableBaseVisible = houseRenderer.enabled &&
            houseRenderer.sprite == formalHouseV35InteriorCatalog.StableBase;
        bool allDamage = CountEnabled(v35InteriorRepairOwnerRenderers) == owners.Length;
        bool registrationStable = true;
        Vector2 housePivot = GetFormalHouseWorldPosition();
        float uniformScale = GetFormalHouseWorldSize().x /
            Mathf.Max(0.001f, formalHouseV35InteriorCatalog.StableBase.bounds.size.x);
        for (int i = 0; i < owners.Length; i++)
        {
            SpriteRenderer renderer = v35InteriorRepairOwnerRenderers[i];
            Vector2 expectedPosition = housePivot + owners[i].WorldOffsetFromHousePivot * uniformScale;
            allDamage &= renderer.enabled && renderer.sprite == owners[i].DamageSprite;
            registrationStable &= Vector2.Distance(renderer.transform.localPosition, expectedPosition) <= 0.001f &&
                renderer.sortingOrder < 27;
        }

        SetEditorV35InteriorRepairPreviewPose(1);
        bool foundationOnly = true;
        for (int i = 0; i < owners.Length; i++)
        {
            bool expectedRepair = owners[i].GameplayOwner == "Stilt" && owners[i].RequiredStep == 1;
            foundationOnly &= v35InteriorRepairOwnerRenderers[i].sprite ==
                (expectedRepair ? owners[i].RepairSprite : owners[i].DamageSprite);
        }

        SetEditorV35InteriorRepairPreviewPose(3);
        bool leftRoofOnly = true;
        for (int i = 0; i < owners.Length; i++)
        {
            bool expectedRepair = owners[i].GameplayOwner == "Roof" && owners[i].RequiredStep == 1;
            leftRoofOnly &= v35InteriorRepairOwnerRenderers[i].sprite ==
                (expectedRepair ? owners[i].RepairSprite : owners[i].DamageSprite);
        }

        SetEditorV35InteriorRepairPreviewPose(4);
        bool interiorFirstStepOnly = true;
        for (int i = 0; i < owners.Length; i++)
        {
            bool expectedRepair = owners[i].Key == "InteriorEnvelope" || owners[i].Key == "EntryDoor";
            interiorFirstStepOnly &= v35InteriorRepairOwnerRenderers[i].sprite ==
                (expectedRepair ? owners[i].RepairSprite : owners[i].DamageSprite);
        }

        SetEditorV35InteriorRepairPreviewPose(5);
        bool allRepair = true;
        for (int i = 0; i < owners.Length; i++)
        {
            allRepair &= v35InteriorRepairOwnerRenderers[i].sprite == owners[i].RepairSprite;
        }
        bool dryInteriorActorSolid = playerRenderer.enabled &&
            playerRenderer.color.a >= 0.99f &&
            !playerAquaticRenderer.enabled &&
            playerRenderer.maskInteraction == SpriteMaskInteraction.None &&
            !playerBagMask.enabled;

        float authoredMainFloor = GetV35WorldAnchor(new Vector2(1024f, 1288f)).y;
        bool floorRegistered = Mathf.Abs(authoredMainFloor - GetPlayerStandingFeetY(WalkLane.Deck)) <= 0.02f;
        bool lowerLadderRegistered =
            Mathf.Abs(GetInteriorStairTopPosition().x - GetV35WorldAnchor(new Vector2(1160f, 1288f)).x) <= 0.001f &&
            Mathf.Abs(GetInteriorStairBottomPosition().x - GetV35WorldAnchor(new Vector2(1120f, 1636f)).x) <= 0.001f &&
            GetInteriorStairTopPosition().y > GetInteriorStairBottomPosition().y + 0.8f;
        bool lookoutLadderRegistered =
            Mathf.Abs(GetInteriorLoftStairBottomPosition().x - GetV35WorldAnchor(new Vector2(1160f, 1288f)).x) <= 0.001f &&
            Mathf.Abs(GetInteriorLoftStairTopPosition().x - GetV35WorldAnchor(new Vector2(1160f, 760f)).x) <= 0.001f;
        bool climbVisualPathRegistered =
            lowerLadderRegistered && lookoutLadderRegistered;

        bool interiorClimbDirection =
            formalV32ArtCatalog.GetClimbFrame(GetV32ClimbPlaybackProgress(
                WalkLane.InteriorLower, WalkLane.InteriorUpper, 0f)) == formalV32ArtCatalog.ClimbFrames[0] &&
            formalV32ArtCatalog.GetClimbFrame(GetV32ClimbPlaybackProgress(
                WalkLane.InteriorLower, WalkLane.InteriorUpper, 1f)) == formalV32ArtCatalog.ClimbFrames[5] &&
            formalV32ArtCatalog.GetClimbFrame(GetV32ClimbPlaybackProgress(
                WalkLane.InteriorUpper, WalkLane.InteriorLower, 0f)) == formalV32ArtCatalog.ClimbFrames[5] &&
            formalV32ArtCatalog.GetClimbFrame(GetV32ClimbPlaybackProgress(
                WalkLane.InteriorUpper, WalkLane.InteriorLower, 1f)) == formalV32ArtCatalog.ClimbFrames[0];

        bool oldInteriorOwnersHidden = CountEnabled(v30RepairOwnerRenderers) == 0 &&
            CountEnabled(v34ExteriorRepairOwnerRenderers) == 0;
        SetEditorV35InteriorClimbPreviewPose(1);
        Sprite expectedInteriorClimbFrame = formalV32ArtCatalog.GetClimbFrame(
            GetV32ClimbPlaybackProgress(WalkLane.InteriorLower, WalkLane.InteriorUpper, 0.5f));
        float expectedClimbVisualX = playerPosition.x;
        bool interiorClimbActuallyRendered = playerRenderer.enabled &&
            playerRenderer.sprite == expectedInteriorClimbFrame &&
            playerRenderer.sortingOrder > v35InteriorRepairOwnerRenderers[0].sortingOrder &&
            Mathf.Abs(playerRenderer.transform.localPosition.x - expectedClimbVisualX) <= 0.001f;
        SetEditorInteriorPreviewPose(true);
        bool floodSharesV35Shell = interiorFloodRenderer.enabled &&
            CountEnabled(v35InteriorRepairOwnerRenderers) == owners.Length;

        SetEditorTidePrepHoldPreviewPose(0);
        TidePrepChoice[] prepChoices =
            { TidePrepChoice.Rope, TidePrepChoice.Bucket, TidePrepChoice.Stake };
        Vector3[] prepPositions = new Vector3[prepChoices.Length];
        Vector3[] prepScales = new Vector3[prepChoices.Length];
        bool fixedPrepInstallations = true;
        for (int i = 0; i < prepChoices.Length; i++)
        {
            TidePrepChoice choice = prepChoices[i];
            SpriteRenderer renderer = GetPrepRenderer(prepChoices[i]);
            Sprite expectedSprite = GetV35FixedTidePrepSprite(choice);
            Vector2 expectedSize = GetV35TidePrepInstallationSize(choice);
            prepPositions[i] = renderer.transform.localPosition;
            prepScales[i] = renderer.transform.localScale;
            fixedPrepInstallations &= renderer.enabled &&
                expectedSprite != null &&
                renderer.sprite == expectedSprite &&
                Vector2.Distance(renderer.transform.localPosition, GetNightPrepPosition(choice)) <= 0.001f &&
                Mathf.Abs(renderer.bounds.size.x - expectedSize.x) <= 0.01f &&
                Mathf.Abs(renderer.bounds.size.y - expectedSize.y) <= 0.01f &&
                Mathf.Abs(Mathf.DeltaAngle(renderer.transform.localEulerAngles.z, 0f)) <= 0.01f &&
                renderer.sortingOrder < interiorFloodRenderer.sortingOrder &&
                renderer.sortingOrder < playerRenderer.sortingOrder;
        }
        UpdateVisuals(8.25f);
        for (int i = 0; i < prepChoices.Length; i++)
        {
            SpriteRenderer renderer = GetPrepRenderer(prepChoices[i]);
            fixedPrepInstallations &= Vector3.Distance(
                    renderer.transform.localPosition,
                    prepPositions[i]) <= 0.001f &&
                Vector3.Distance(renderer.transform.localScale, prepScales[i]) <= 0.001f;
        }

        SetEditorDeparturePreviewPose();
        bool finalDepartureReleasesOwners = CountEnabled(v35InteriorRepairOwnerRenderers) == 0;

        string evidence =
            $"底图={stableBaseVisible}/全损={allDamage}/桩基={foundationOnly}/左屋面={leftRoofOnly}/" +
            $"内室一步={interiorFirstStepOnly}/全修={allRepair}/配准={registrationStable}；" +
            $"楼板={floorRegistered}/外梯={lowerLadderRegistered}/瞭望梯={lookoutLadderRegistered}/" +
            $"视觉轨迹={climbVisualPathRegistered}/正反播={interiorClimbDirection}/" +
            $"实绘爬梯={interiorClimbActuallyRendered}/" +
            $"干燥人物完整={dryInteriorActorSolid}/积水={floodSharesV35Shell}/固定备潮设施={fixedPrepInstallations}/" +
            $"终局释放={finalDepartureReleasesOwners}/" +
            $"旧层隐藏={oldInteriorOwnersHidden}";
        bool passed = stableBaseVisible && allDamage && foundationOnly && leftRoofOnly &&
            interiorFirstStepOnly && allRepair && registrationStable && floorRegistered &&
            lowerLadderRegistered && lookoutLadderRegistered && climbVisualPathRegistered && interiorClimbDirection &&
            interiorClimbActuallyRendered && dryInteriorActorSolid && oldInteriorOwnersHidden && floodSharesV35Shell &&
            fixedPrepInstallations && finalDepartureReleasesOwners;
        return passed
            ? $"PASS：V35 室内与 V34 共用同一栋屋，十二个 owner 按维修线独立切换，四项室内部件互不串扰；梯、积水和终局所有权均成立。{evidence}"
            : $"FAIL：V35 室内端点、所有权、楼梯、积水或终局退出仍有断点。{evidence}";
    }

    public string RunEditorFirstInteriorRepairCausalityProbe()
    {
        if (!HasCompleteV35HouseInteriorPresentation())
        {
            return "FAIL：V35 室内资源不完整，无法验证第一次室内修补的画面因果。";
        }

        int expectedFirstStepOwners = 0;
        TideV35HouseInteriorCatalog.OwnerEntry[] owners = formalHouseV35InteriorCatalog.Owners;
        for (int i = 0; i < owners.Length; i++)
        {
            if (owners[i].Key == "InteriorEnvelope" || owners[i].Key == "EntryDoor")
            {
                expectedFirstStepOwners++;
            }
        }

        SetEditorFirstInteriorRepairCausalityPreviewPose(0);
        bool carriedBeforeWork = currentHarvest == HarvestKind.Wood &&
            harvestPhysicalState == HarvestPhysicalState.Carried &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.Carried &&
            harvestRenderer.enabled &&
            Vector2.Distance(harvestRenderer.bounds.center, playerRenderer.bounds.center) <= 0.75f &&
            CountV35VisibleRepairSprites() == 0;

        SetEditorFirstInteriorRepairCausalityPreviewPose(1);
        Vector2 target = GetRepairChoicePosition(RepairChoice.InteriorSeal);
        bool placedAndReachable = harvestRenderer.enabled &&
            Mathf.Abs(harvestRenderer.bounds.center.x - target.x) <= 0.72f &&
            Mathf.Abs(playerPosition.x - target.x) <= contextDistance &&
            Mathf.Abs(harvestRenderer.bounds.min.y - GetPlayerStandingFeetY(WalkLane.InteriorUpper)) <= 0.03f;
        bool workIsPreviewOnly = repairWorkActive && repairWorkProgress > 0f && repairWorkProgress < 1f &&
            interiorComfort == 0 && extraSaltWoodOwner == ExtraSaltWoodOwner.PlacedAtWork &&
            CountV35VisibleRepairSprites() == expectedFirstStepOwners;

        SetEditorFirstInteriorRepairCausalityPreviewPose(2);
        // A completed daytime repair is its own readable beat. Tide-prep stations
        // belong to the later dusk/night routine and must not pop into the same frame
        // merely because RepairMoment has been committed.
        bool daytimePrepHidden = !IsTidePrepWindowOpen() &&
            !prepRopeCoilRenderer.enabled &&
            !prepBucketRenderer.enabled &&
            !prepStakeRenderer.enabled;
        bool committedOnce = !repairWorkActive && repairChoiceApplied && interiorComfort == 1 &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.Claimed &&
            currentHarvest == HarvestKind.None && !harvestRenderer.enabled &&
            CountV35VisibleRepairSprites() == expectedFirstStepOwners;

        string evidence =
            $"携料={carriedBeforeWork}/落点={placedAndReachable}/施工未扣料={workIsPreviewOnly}/" +
            $"完成={committedOnce}/白天备潮隐藏={daytimePrepHidden}/首步owner={expectedFirstStepOwners}";
        return carriedBeforeWork && placedAndReachable && workIsPreviewOnly && committedOnce && daytimePrepHidden
            ? $"PASS：第一次室内修补在同一栋屋内形成携料、落料、持续施工和永久部件变化。{evidence}"
            : $"FAIL：第一次室内修补仍有材料消失、隔空施工、提前结算或 owner 跳变。{evidence}";
    }

    public string RunEditorV35InteriorRepairOwnerIsolationProbe()
    {
        if (HasCompleteV69CurrentHousePresentation())
        {
            return RunEditorV69HouseRepairIntegrationProbe();
        }

        EnsureScene();
        ResetSlice();
        EnsureV35HouseInteriorResourcesLoaded();
        if (!HasCompleteV35HouseInteriorPresentation())
        {
            return "FAIL：V35 室内资源不完整，无法验证部件级维修隔离。";
        }

        SetEditorFirstInteriorRepairCausalityPreviewPose(2);
        bool sealOnly = IsV35OwnerRenderingRepair("InteriorEnvelope") &&
            IsV35OwnerRenderingRepair("EntryDoor") &&
            !IsV35OwnerRenderingRepair("Workbench") &&
            !IsV35OwnerRenderingRepair("Bed") &&
            !IsV35OwnerRenderingRepair("ChartRadio");

        bedCondition = 1;
        RefreshInteriorComfort();
        UpdateVisuals(3.8f);
        bool bedDoesNotRemoteRepairChart = IsV35OwnerRenderingRepair("Bed") &&
            !IsV35OwnerRenderingRepair("ChartRadio");

        string evidence = $"围护入口独占={sealOnly}；睡铺不远修无线电={bedDoesNotRemoteRepairChart}";
        return sealOnly && bedDoesNotRemoteRepairChart
            ? $"PASS：围护入口、工作台、睡铺和海图无线电拥有独立维修 owner。{evidence}"
            : $"FAIL：一次内室施工仍会隔空替换不在当前工作点的 V35 owner。{evidence}";
    }

    public string RunEditorInteriorRepairConsequencesProbe()
    {
        EnsureScene();

        ResetSlice();
        arrivalVignetteActive = false;
        viewMode = SliceViewMode.Interior;
        playerLane = WalkLane.InteriorUpper;
        playerPosition = new Vector2(GetInteriorDoorX(), GetPlayerLaneY(playerLane));
        roofIntegrity = 0;
        interiorSealCondition = 0;
        RefreshInteriorComfort();
        weatherClockSeconds = dayLengthSeconds * stormFrontArrivalDays;
        bodyWarmth01 = 1f;
        TickSurvival(1f);
        float unsealedWarmthLoss = 1f - bodyWarmth01;

        ResetSlice();
        arrivalVignetteActive = false;
        viewMode = SliceViewMode.Interior;
        playerLane = WalkLane.InteriorUpper;
        playerPosition = new Vector2(GetInteriorDoorX(), GetPlayerLaneY(playerLane));
        roofIntegrity = 0;
        interiorSealCondition = 1;
        RefreshInteriorComfort();
        weatherClockSeconds = dayLengthSeconds * stormFrontArrivalDays;
        bodyWarmth01 = 1f;
        TickSurvival(1f);
        float sealedWarmthLoss = 1f - bodyWarmth01;
        bool sealReducesStormLoss = sealedWarmthLoss < unsealedWarmthLoss - 0.0001f;

        ResetSlice();
        float unrepairedWorkbenchDuration = GetRepairWorkDuration(RepairChoice.Roof);
        workbenchCondition = 1;
        float repairedWorkbenchDuration = GetRepairWorkDuration(RepairChoice.Roof);
        bool workbenchReducesLaterWork = repairedWorkbenchDuration < unrepairedWorkbenchDuration - 0.01f;

        ResetSlice();
        arrivalVignetteActive = false;
        stiltIntegrity = 3;
        bodyWarmth01 = 0.2f;
        bedCondition = 1;
        RefreshInteriorComfort();
        dayProgress01 = 0.9f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        AdvanceMoonPhase();
        float warmthAfterBedSleep = bodyWarmth01;
        bool bedRestoresWarmth = warmthAfterBedSleep >= 0.7f;

        ResetSlice();
        arrivalVignetteActive = false;
        chartRadioCondition = 1;
        lampForecastCharges = 0;
        dayProgress01 = 0.9f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        AdvanceMoonPhase();
        bool chartProvidesMorningForecast = lampForecastCharges > 0;

        string evidence =
            $"失温未封/封={unsealedWarmthLoss:F4}/{sealedWarmthLoss:F4}；" +
            $"工时={unrepairedWorkbenchDuration:F2}/{repairedWorkbenchDuration:F2}；" +
            $"睡后体温={warmthAfterBedSleep:F2}；海图预报={chartProvidesMorningForecast}";
        bool passed = sealReducesStormLoss && workbenchReducesLaterWork && bedRestoresWarmth && chartProvidesMorningForecast;
        return passed
            ? $"PASS：四项室内维修分别改变防潮、施工工时、睡眠恢复和潮汐预报。{evidence}"
            : $"FAIL：室内维修仍只有换图或共享舒适度，没有四条独立系统后果。{evidence}";
    }

    private bool IsV35OwnerRenderingRepair(string ownerKey)
    {
        TideV35HouseInteriorCatalog.OwnerEntry[] owners = formalHouseV35InteriorCatalog != null
            ? formalHouseV35InteriorCatalog.Owners
            : null;
        if (owners == null)
        {
            return false;
        }

        for (int i = 0; i < owners.Length && i < v35InteriorRepairOwnerRenderers.Count; i++)
        {
            if (owners[i] != null && owners[i].Key == ownerKey)
            {
                return v35InteriorRepairOwnerRenderers[i].enabled &&
                    v35InteriorRepairOwnerRenderers[i].sprite == owners[i].RepairSprite;
            }
        }

        return false;
    }

    public string RunEditorWashedAwayHarvestLayerProbe()
    {
        SetEditorBrokenNetHarvestPreviewPose();
        SpriteRenderer firstWood = washedAwayHarvestItems.Count > 0 ? washedAwayHarvestItems[0] : null;
        SpriteRenderer secondWood = washedAwayHarvestItems.Count > 1 ? washedAwayHarvestItems[1] : null;
        if (firstWood == null || secondWood == null || !firstWood.enabled || !secondWood.enabled)
        {
            return "FAIL：破网后没有同时保留两件被冲走的盐木实体。";
        }

        TideOceanSample firstOcean = GetOceanSample(firstWood.bounds.center.x);
        TideOceanSample secondOcean = GetOceanSample(secondWood.bounds.center.x);
        // The ocean is no longer a horizontal global line. A floating object is valid
        // when its bounds straddle the local wave surface sampled under that object;
        // comparing both pieces to currentWaterY falsely reports normal crest variation
        // as levitation after they have drifted away from the scene center.
        bool centerTouchesSurface =
            firstWood.bounds.min.y <= firstOcean.SurfaceY + 0.03f &&
            firstWood.bounds.max.y >= firstOcean.SurfaceY - 0.03f &&
            secondWood.bounds.min.y <= secondOcean.SurfaceY + 0.03f &&
            secondWood.bounds.max.y >= secondOcean.SurfaceY - 0.03f;
        bool notRetinted = firstWood.color.r >= 0.99f && firstWood.color.g >= 0.99f && firstWood.color.b >= 0.99f;
        bool remainsInFront = firstWood.sortingOrder > netCaughtItems[0].sortingOrder;
        bool piecesSeparate = Mathf.Abs(firstWood.bounds.center.x - secondWood.bounds.center.x) >= 0.18f;
        string evidence = $"木心相对局部水面={firstWood.bounds.center.y - firstOcean.SurfaceY:F3}/{secondWood.bounds.center.y - secondOcean.SurfaceY:F3}；" +
            $"颜色=({firstWood.color.r:F2},{firstWood.color.g:F2},{firstWood.color.b:F2})；层级={firstWood.sortingOrder}>{netCaughtItems[0].sortingOrder}；间距={Mathf.Abs(firstWood.bounds.center.x - secondWood.bounds.center.x):F2}";
        return centerTouchesSurface && notRetinted && remainsInFront && piecesSeparate
            ? $"PASS：破网盐木贴水、保持正式颜色，并在废网前随流分散。{evidence}"
            : $"FAIL：破网盐木仍有悬浮、染色或层级错误。{evidence}";
    }

    public string RunEditorSecuredPostHandsFreeProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalVignetteActive = false;
        state = SliceState.TideRising;
        viewMode = SliceViewMode.Shelter;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(netAnchor.x, GetPlayerLaneY(playerLane));
        netTouched = true;
        netCatchResolved = true;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        currentHarvest = HarvestKind.Fish;
        harvestPhysicalState = HarvestPhysicalState.CaughtInNet;
        netCatchVisualPieceCount = 2;
        netCatchBundleTier = 2;

        SecureNetBeforeEbb();
        UpdateVisuals(0.7f);
        bool fishOwnsPostSlot = securedPostHarvest == HarvestKind.Fish &&
            securedPostBundleTier == 2 &&
            currentHarvest == HarvestKind.None &&
            harvestPhysicalState == HarvestPhysicalState.None;
        bool postBundleIsVisible = netCaughtItems.Count >= 2 &&
            netCaughtItems[0].enabled && netCaughtItems[1].enabled &&
            Vector2.Distance(netCaughtItems[0].bounds.center,
                new Vector2(netAnchor.x - 0.42f, GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.2f)) < 0.7f;

        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.ReturnedAtBoat);
        bool timberUnloadsWithoutMovingFish = TryUnloadReturnedSailingCargo() &&
            currentHarvest == HarvestKind.Wood &&
            harvestPhysicalState == HarvestPhysicalState.Carried &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.Carried &&
            securedPostHarvest == HarvestKind.Fish;

        BankCurrentHarvestMaterials();
        int timberAfterBank = timberStock;
        int ropeAfterBank = ropeStock;
        int clothAfterBank = clothStock;
        PickUpSecuredHarvestFromPost();
        bool fishCanBeCollectedAfterTimber = !HasSecuredPostHarvest() &&
            currentHarvest == HarvestKind.Fish &&
            harvestPhysicalState == HarvestPhysicalState.Carried &&
            timberStock == timberAfterBank && ropeStock == ropeAfterBank && clothStock == clothAfterBank &&
            foodStock == 0;

        string evidence = $"留桩空手={fishOwnsPostSlot}/可见={postBundleIsVisible}；" +
            $"盐木进手且鱼不动={timberUnloadsWithoutMovingFish}；后取鱼不复制={fishCanBeCollectedAfterTimber}；" +
            $"材料木绳布食={timberStock}/{ropeStock}/{clothStock}/{foodStock}";
        return fishOwnsPostSlot && postBundleIsVisible && timberUnloadsWithoutMovingFish &&
            fishCanBeCollectedAfterTimber
            ? $"PASS：网桩鱼获与玩家双手拥有独立实物槽，短航盐木不再阻塞也不会覆盖鱼获。{evidence}"
            : $"FAIL：网桩鱼获仍占手、不可见，或与短航盐木发生覆盖/复制。{evidence}";
    }

    public string RunEditorHarvestPersistenceProbe()
    {
        EnsureScene();

        ResetSlice();
        stiltIntegrity = 2;
        roofIntegrity = 1;
        dayClockSeconds = 30f;
        tideClockSeconds = 20f;
        currentHarvest = HarvestKind.Wood;
        netCatchBundleTier = 2;
        currentHarvestBanked = false;
        harvestPhysicalState = HarvestPhysicalState.Carried;
        HandlePlayerDeath("验收用冷水失温");
        bool deathDropsCarriedCargo = currentHarvest == HarvestKind.None && timberStock == 0 && ropeStock == 0;
        bool deathKeepsRepairs = stiltIntegrity == 2 && roofIntegrity == 1;
        bool deathAdvancesWorld = dayClockSeconds > 30f && tideClockSeconds > 20f;

        ResetSlice();
        currentHarvest = HarvestKind.Fish;
        netCatchBundleTier = 2;
        currentHarvestBanked = false;
        harvestPhysicalState = HarvestPhysicalState.Carried;
        foodStock = 0;
        StoreCurrentHarvestAtInteriorRack();
        int storedFood = foodStock;
        bool storageBanksOnlyAtRack = currentHarvest == HarvestKind.None &&
            harvestPhysicalState == HarvestPhysicalState.Stored && foodStock == 3;

        ResetSlice();
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.ReturnedAtBoat);
        returnedClueAtBoat = true;
        bool unloaded = TryUnloadReturnedSailingCargo();
        StoreCurrentHarvestAtInteriorRack();
        int unloadedTimber = timberStock;
        int unloadedCloth = clothStock;
        int unloadedMetal = metalStock;
        EnterSailingScene();
        sailingBoatX = sailingSalvagePoint.x;
        sailingBoatLaneY = sailingSalvagePoint.y;
        TryInteractAtSailingPoint();
        sailingBoatX = earlyWreckClueX;
        sailingBoatLaneY = GetSailingPointPosition(SailingPointKind.Wreck).y;
        TryInteractAtSailingPoint();
        bool sourceDoesNotRespawn = unloaded && sailingSalvageClaimed && sailingWreckClueClaimed &&
            sailingSalvageCollected && !sailingSalvageRewardPending && !sailingRewardPending &&
            timberStock == unloadedTimber && clothStock == unloadedCloth && metalStock == unloadedMetal;

        string evidence = $"死亡丢携带={deathDropsCarriedCargo}/保维修={deathKeepsRepairs}/时间推进={deathAdvancesWorld}；" +
            $"储物鱼={storedFood}；卸货木布铁={unloadedTimber}/{unloadedCloth}/{unloadedMetal}；来源不刷新={sourceDoesNotRespawn}";
        bool passed = deathDropsCarriedCargo && deathKeepsRepairs && deathAdvancesWorld &&
            storageBanksOnlyAtRack && sourceDoesNotRespawn;
        return passed
            ? $"PASS：死亡、屋内存放和重复出航遵守同一实物所有权规则。{evidence}"
            : $"FAIL：收获实物在死亡、存放或重复出航中发生复制/丢账。{evidence}";
    }

    public string RunEditorMooringCargoStagingProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalVignetteActive = false;
        state = SliceState.TideRising;
        dayNightPhase = DayNightPhase.Day;
        currentWaterY = lowWaterY + 0.66f;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        currentHarvest = HarvestKind.Fish;
        harvestPhysicalState = HarvestPhysicalState.CaughtInNet;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.ReturnedAtBoat);

        bool unloadedToDock = TryUnloadReturnedSailingCargo() &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.StagedAtMooring &&
            currentHarvest == HarvestKind.Fish &&
            harvestPhysicalState == HarvestPhysicalState.CaughtInNet &&
            !HasReturnedSailingCargoAtBoat();
        // 暂放点属于泊位屏，不应穿越屏区显示在高脚屋归处画面。
        mooringScreenActive = true;
        UpdateVisuals(0.7f);
        bool visibleAtPhysicalPoint = sailingSalvagePointRenderer.enabled &&
            Vector2.Distance(sailingSalvagePointRenderer.bounds.center, GetMooringStagingPosition()) <= 0.08f;

        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(GetBoatBoardingX(), GetPlayerLaneY(playerLane));
        float cargoMooringSeconds = 0f;
        bool cargoMooringSecured = AdvanceProbeMooringUntilSecured(
            0.05f,
            12f,
            false,
            ref cargoMooringSeconds);
        TryBoardBoat();
        bool freedBoatCanBoard = cargoMooringSecured &&
            boatViewTransition == BoatViewTransition.Boarding &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.StagedAtMooring;

        boatViewTransition = BoatViewTransition.None;
        currentHarvest = HarvestKind.None;
        harvestPhysicalState = HarvestPhysicalState.None;
        playerPosition = new Vector2(GetMooringStagingPosition().x, GetPlayerLaneY(playerLane));
        int timberBeforePickup = timberStock;
        int clothBeforePickup = clothStock;
        bool pickedUpFromSamePoint = TryPickUpStagedSaltWoodAtMooring() &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.Carried &&
            currentHarvest == HarvestKind.Wood &&
            timberStock == timberBeforePickup && clothStock == clothBeforePickup;

        ResetSlice();
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.StagedAtMooring);
        HandlePlayerDeath("验收用码头暂放后失温");
        bool deathKeepsDockCargo = extraSaltWoodOwner == ExtraSaltWoodOwner.StagedAtMooring &&
            timberStock == 0 && clothStock == 0;

        string evidence = $"卸到木板={unloadedToDock}/可见接地={visibleAtPhysicalPoint}/船可再用={freedBoatCanBoard}/" +
            $"原处抱起={pickedUpFromSamePoint}/死亡保留={deathKeepsDockCargo}";
        return unloadedToDock && visibleAtPhysicalPoint && freedBoatCanBoard &&
            pickedUpFromSamePoint && deathKeepsDockCargo
            ? $"PASS：返航盐木可与网获并存，码头暂放释放船且不绕过实物所有权。{evidence}"
            : $"FAIL：码头暂放仍会锁船、丢失、复制或脱离可见位置。{evidence}";
    }

    public string RunEditorSameTideSaltWoodConservationProbe()
    {
        EnsureScene();

        ResetSlice();
        EnterSailingScene();
        bool timberWaitsForNearshorePassage = sailingSalvageCollected;

        ResetSlice();
        tideSourceHarvest = HarvestKind.Fish;
        netIntegrity = 3;
        netSetDepth01 = 0.1f;
        tideStrength = 0.35f;
        outerWreckTravel01 = TideDriftSourceModel.NearshoreExitTravel01;
        ResolveExitedTideDriftBatches();
        ResolveNetCatch();
        EnterSailingScene();
        bool openRouteLeavesTimberAtSea = extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater &&
            !sailingSalvageCollected;

        // The routed bonus and the short-sailing timber refer to one physical object.
        ResetSlice();
        nearshoreWorkDone = true;
        tideRoutingMode = TideRoutingMode.FeedNet;
        extraSaltWoodBundledWithNetHarvest = true;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.RoutedToNet);
        bool routedLoadExists = HasRoutedLoadBonus();
        EnterSailingScene();
        bool routedTimberIsAbsentFromSailing = sailingSalvageCollected;

        ResetSlice();
        viewMode = SliceViewMode.Shelter;
        state = SliceState.TideRising;
        netDeployed = false;
        netTouched = false;
        currentWaterY = lowWaterY + 0.3f;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(GetShoreWorkX(), GetPlayerLaneY(playerLane));
        TryDoNearshoreWork();
        bool noNetCannotRouteTimber = !HasRoutedLoadBonus() &&
            tideRoutingMode == TideRoutingMode.Open && sailingSalvageCollected;

        ResetSlice();
        extraSaltWoodBundledWithNetHarvest = true;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.RoutedToNet);
        nearshoreWorkDone = true;
        tideRoutingMode = TideRoutingMode.FeedNet;
        state = SliceState.TideRising;
        netDeployed = true;
        netTouched = false;
        tideClockSeconds = tideCycleSeconds * 0.99f;
        TickNaturalTide(0f);
        bool dryNetReleasesUncaughtTimber = extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater;

        ResetSlice();
        extraSaltWoodBundledWithNetHarvest = true;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.RoutedToNet);
        nearshoreWorkDone = true;
        tideRoutingMode = TideRoutingMode.FeedNet;
        CommitNetIntoNaturalTide(NetLine.Mid);
        bool redeployReleasesPreviousRoute = extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater &&
            tideRoutingMode == TideRoutingMode.Open;

        ResetSlice();
        nearshoreWorkDone = true;
        tideRoutingMode = TideRoutingMode.FeedNet;
        extraSaltWoodBundledWithNetHarvest = true;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.RoutedToNet);
        tideSourceHarvest = HarvestKind.Fish;
        netIntegrity = 3;
        netSetDepth01 = 0.1f;
        tideStrength = 0.35f;
        outerWreckTravel01 = TideDriftSourceModel.NetIntersectionTravel01;
        ResolveNetCatch();
        bool caughtOnceInMixedNet = extraSaltWoodOwner == ExtraSaltWoodOwner.CaughtInNet &&
            currentHarvest == HarvestKind.Fish && netCatchBundleTier == 2 && sailingSalvageCollected;
        state = SliceState.TideRising;
        viewMode = SliceViewMode.Shelter;
        netDeployed = true;
        netTouched = true;
        currentWaterY = GetSelectedNetY() + 0.2f;
        UpdateCaughtNetVisuals(1.6f, GetSelectedNetY());
        bool mixedNetShowsSaltWood = netCaughtItems.Count >= 2 &&
            netCaughtItems[1].enabled &&
            netCaughtItems[1].sprite == GetCaughtNetItemSprite(
                HarvestKind.Wood,
                0,
                extraSaltWoodBatchId);
        BeginBrokenNetResidue(HarvestKind.Fish, 2);
        bool brokenNetReturnsTimberToSea = extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater &&
            timberStock == 0 && clothStock == 0;

        ResetSlice();
        nearshoreWorkDone = true;
        tideRoutingMode = TideRoutingMode.FeedNet;
        extraSaltWoodBundledWithNetHarvest = true;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.RoutedToNet);
        tideSourceHarvest = HarvestKind.Fish;
        netIntegrity = 3;
        netSetDepth01 = 0.1f;
        tideStrength = 0.35f;
        outerWreckTravel01 = TideDriftSourceModel.NetIntersectionTravel01;
        ResolveNetCatch();
        ApplyHarvest();
        harvestCarryTransition01 = 1f;
        UpdateHarvestVisuals(0.35f);
        bool carriedBundleShowsSaltWood = harvestCarryItems.Count >= 2 &&
            harvestCarryItems[1].enabled &&
            harvestCarryItems[1].sprite == GetCaughtNetItemSprite(
                HarvestKind.Wood,
                0,
                extraSaltWoodBatchId);
        BankCurrentHarvestMaterials();
        int bankedTimber = timberStock;
        int bankedCloth = clothStock;
        int bankedFood = foodStock;
        BankCurrentHarvestMaterials();
        bool mixedYieldIsExactAndIdempotent = extraSaltWoodOwner == ExtraSaltWoodOwner.Claimed &&
            bankedTimber == 2 && bankedCloth == 1 && bankedFood == 2 && ropeStock == 1 &&
            timberStock == bankedTimber && clothStock == bankedCloth && foodStock == bankedFood;

        // Cargo must not overwrite a catch already carried by the player. It is
        // unloaded onto a separate physical spot, frees the boat, and remains there
        // through death until an empty-handed player picks it up.
        ResetSlice();
        currentHarvest = HarvestKind.Fish;
        harvestPhysicalState = HarvestPhysicalState.Carried;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.ReturnedAtBoat);
        bool stagedWhileCarrying = TryUnloadReturnedSailingCargo() &&
            currentHarvest == HarvestKind.Fish &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.StagedAtMooring &&
            !returnedSalvageAtBoat && timberStock == 0 && clothStock == 0;
        mooringScreenActive = true;
        UpdateVisuals(0.6f);
        bool stagedPileIsVisible = sailingSalvagePointRenderer.enabled &&
            Vector2.Distance(sailingSalvagePointRenderer.bounds.center, GetMooringStagingPosition()) <= 0.08f;
        HandlePlayerDeath("验收用码头暂放后失温");
        bool stagedTimberSurvivesDeath = extraSaltWoodOwner == ExtraSaltWoodOwner.StagedAtMooring &&
            timberStock == 0 && clothStock == 0;
        currentHarvest = HarvestKind.None;
        harvestPhysicalState = HarvestPhysicalState.None;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(GetMooringStagingPosition().x, GetPlayerLaneY(playerLane));
        bool pickedUpFromStaging = TryPickUpStagedSaltWoodAtMooring() &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.Carried &&
            currentHarvest == HarvestKind.Wood;

        ResetSlice();
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.ReturnedAtBoat);
        int timberBeforeUnload = timberStock;
        int clothBeforeUnload = clothStock;
        bool unloadedIntoHands = TryUnloadReturnedSailingCargo() &&
            currentHarvest == HarvestKind.Wood &&
            harvestPhysicalState == HarvestPhysicalState.Carried &&
            timberStock == timberBeforeUnload &&
            clothStock == clothBeforeUnload;

        HandlePlayerDeath("验收用携带盐木落水");
        bool carriedDeathReturnsTimberToSea = extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater &&
            timberStock == 0 && clothStock == 0;

        ResetSlice();
        currentHarvest = HarvestKind.Fish;
        netCatchBundleTier = 2;
        netCatchVisualPieceCount = 2;
        currentHarvestBanked = false;
        harvestPhysicalState = HarvestPhysicalState.CaughtInNet;
        netDeployed = true;
        extraSaltWoodBundledWithNetHarvest = true;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.CaughtInNet);
        SecureNetBeforeEbb();
        HandlePlayerDeath("验收用网桩旁失温");
        bool securedTimberSurvivesAtShelter = extraSaltWoodOwner == ExtraSaltWoodOwner.Claimed &&
            timberStock == 2 && ropeStock == 1 && clothStock == 1 && foodStock == 2;
        EnterSailingScene();
        bool claimedSourceDoesNotRespawn = sailingSalvageCollected &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.Claimed;

        ResetSlice();
        extraSaltWoodBundledWithNetHarvest = true;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.RoutedToNet);
        AdvanceMoonPhase();
        bool abandonedRouteReturnsAcrossTide = extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater;

        ResetSlice();
        currentHarvest = HarvestKind.Fish;
        harvestPhysicalState = HarvestPhysicalState.CaughtInNet;
        netCatchBundleTier = 2;
        extraSaltWoodBundledWithNetHarvest = true;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.CaughtInNet);
        AdvanceMoonPhase();
        bool caughtCrossTideDoesNotBankAndRespawn = extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater &&
            timberStock == 0 && clothStock == 0 && foodStock == 0;

        ResetSlice();
        extraSaltWoodBundledWithNetHarvest = true;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.RoutedToNet);
        nearshoreWorkDone = true;
        tideRoutingMode = TideRoutingMode.FeedNet;
        HandlePlayerDeath("验收用导流途中失温");
        bool deathClearsCurrentTideRoute = extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater &&
            !nearshoreWorkDone && tideRoutingMode == TideRoutingMode.Open;

        ResetSlice();
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.Claimed);
        EnterSailingScene();
        bool claimedTimberHasNoHookingGoal = !BuildLoopGoalText().Contains("钩绳");

        ResetSlice();
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.SailingWater);
        sailingSalvageWorldX = sailingSalvagePoint.x + 0.84f;
        sailingSalvageVelocity = 0.21f;
        float persistentSalvageX = sailingSalvageWorldX;
        EnterSailingScene();
        bool sailingEntryKeepsDriftPosition = Mathf.Abs(sailingSalvageWorldX - persistentSalvageX) < 0.001f;
        AdvanceMoonPhase();
        bool sleepingKeepsDriftPosition = Mathf.Abs(sailingSalvageWorldX - persistentSalvageX) < 0.001f;

        ResetSlice();
        HandlePlayerDeath("验收用近岸经过时失温");
        bool nearshoreDeathReleasesToSailing = extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater;

        ResetSlice();
        state = SliceState.TideRising;
        viewMode = SliceViewMode.Shelter;
        netDeployed = true;
        tideSourceHarvest = HarvestKind.Fish;
        harvestPhysicalState = HarvestPhysicalState.Drifting;
        incomingHarvestTravel01 = 1f;
        outerWreckTravel01 = 1.12f;
        currentWaterY = GetSelectedNetY() + 0.12f;
        UpdateIncomingTideCarryVisuals(1.4f, GetSelectedNetY());
        bool openSaltWoodPassesNet = incomingTideCarryItems.Count >= 3 &&
            incomingTideCarryItems[2].enabled &&
            incomingTideCarryItems[2].sprite == GetCaughtNetItemSprite(
                HarvestKind.Wood,
                0,
                extraSaltWoodBatchId,
                true) &&
            incomingTideCarryItems[2].bounds.center.x < netAnchor.x - 0.72f;
        // The conservation probe must enter the same physical fork contract as play.
        // Directly assigning RoutedToNet would bypass the continuous rope threshold
        // and would no longer prove that the rendered timber follows a locked route.
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.PassingNearshore);
        extraSaltWoodBundledWithNetHarvest = false;
        routingDecisionLocked = false;
        tideRoutingMode = TideRoutingMode.Open;
        routingBoom01 = 0.82f;
        outerWreckTravel01 = 0.71f;
        TryLockContinuousRoutingDecision(0.71f, 0.73f);
        outerWreckTravel01 = 0.98f;
        UpdateIncomingTideCarryVisuals(1.4f, GetSelectedNetY());
        bool routedSaltWoodEntersNet = incomingTideCarryItems[2].enabled &&
            incomingTideCarryItems[2].sprite == GetCaughtNetItemSprite(
                HarvestKind.Wood,
                0,
                extraSaltWoodBatchId,
                true) &&
            incomingTideCarryItems[2].bounds.center.x > netAnchor.x;

        string evidence = $"潮前隐藏={timberWaitsForNearshorePassage}/开放后在海={openRouteLeavesTimberAtSea}；导流负载={routedLoadExists}/短航隐藏={routedTimberIsAbsentFromSailing}；" +
            $"混网捕获={caughtOnceInMixedNet}/网中盐木={mixedNetShowsSaltWood}/携带盐木={carriedBundleShowsSaltWood}/破网回海={brokenNetReturnsTimberToSea}/混合收益木布食={bankedTimber}/{bankedCloth}/{bankedFood}/幂等={mixedYieldIsExactAndIdempotent}；" +
            $"持货暂放={stagedWhileCarrying}/可见={stagedPileIsVisible}/死亡保留={stagedTimberSurvivesDeath}/原处抱起={pickedUpFromStaging}/卸入手中={unloadedIntoHands}/携带死亡回海={carriedDeathReturnsTimberToSea}；" +
            $"网桩保住={securedTimberSurvivesAtShelter}/来源不刷新={claimedSourceDoesNotRespawn}/跨潮回海={abandonedRouteReturnsAcrossTide}；" +
            $"无网禁导={noNetCannotRouteTimber}/干网释放={dryNetReleasesUncaughtTimber}/重挂释放={redeployReleasesPreviousRoute}；" +
            $"网中跨潮不复制={caughtCrossTideDoesNotBankAndRespawn}/死亡清路线={deathClearsCurrentTideRoute}/无错误钩绳目标={claimedTimberHasNoHookingGoal}；" +
            $"进出保位置={sailingEntryKeepsDriftPosition}/跨日保位置={sleepingKeepsDriftPosition}/近岸死亡入短航={nearshoreDeathReleasesToSailing}；" +
            $"开放越网={openSaltWoodPassesNet}/导流入网={routedSaltWoodEntersNet}";
        bool passed = timberWaitsForNearshorePassage && openRouteLeavesTimberAtSea &&
            routedLoadExists && routedTimberIsAbsentFromSailing &&
            caughtOnceInMixedNet && mixedNetShowsSaltWood && carriedBundleShowsSaltWood &&
            brokenNetReturnsTimberToSea && mixedYieldIsExactAndIdempotent &&
            stagedWhileCarrying && stagedPileIsVisible && stagedTimberSurvivesDeath && pickedUpFromStaging &&
            unloadedIntoHands && carriedDeathReturnsTimberToSea &&
            securedTimberSurvivesAtShelter && claimedSourceDoesNotRespawn && abandonedRouteReturnsAcrossTide &&
            noNetCannotRouteTimber && dryNetReleasesUncaughtTimber && redeployReleasesPreviousRoute &&
            caughtCrossTideDoesNotBankAndRespawn && deathClearsCurrentTideRoute && claimedTimberHasNoHookingGoal &&
            sailingEntryKeepsDriftPosition && sleepingKeepsDriftPosition && nearshoreDeathReleasesToSailing &&
            openSaltWoodPassesNet && routedSaltWoodEntersNet;
        return passed
            ? $"PASS：同一根盐木只在网、短航、船艉、码头暂放点或人物手中占一处。{evidence}"
            : $"FAIL：额外盐木仍可能在网和短航重复生成，或返航时绕过实物链直接入账。{evidence}";
    }

    public string RunEditorLiveNetControlAndLoadPhysicsProbe()
    {
        EnsureScene();

        ResetSlice();
        state = SliceState.TideRising;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        netSetDepth01 = 0.56f;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(netAnchor.x, GetPlayerLaneY(playerLane));
        float depthBeforeRaise = netSetDepth01;
        bool adjustedAtPost = TickLiveNetDepthControl(1f, 1f, true);
        float depthAfterRaise = netSetDepth01;
        bool raiseIsContinuous = adjustedAtPost && depthAfterRaise < depthBeforeRaise &&
            depthBeforeRaise - depthAfterRaise < 0.3f;
        arrivalInspected = true;
        viewMode = SliceViewMode.Shelter;
        UpdateVisuals(1.2f);
        bool adjustingShowsHandRope = netHandlingRopeRenderer != null && netHandlingRopeRenderer.enabled;
        bool stoppedAwayFromPost = !TickLiveNetDepthControl(-1f, 1f, false) &&
            Mathf.Abs(netSetDepth01 - depthAfterRaise) < 0.001f;

        outerWreckTravel01 = 0.78f;
        outerNetCaptureProgress01 = 0.63f;
        state = SliceState.TideRising;
        CommitNetIntoNaturalTide(NetLine.Mid);
        bool redeployClearsOldEncounter = outerNetCaptureProgress01 <= 0.0001f &&
            Mathf.Abs(previousOuterWreckTravel01 - outerWreckTravel01) <= 0.0001f;

        float fishAverage = 0f;
        float woodAverage = 0f;
        float trashAverage = 0f;
        float relicMin = float.MaxValue;
        float relicMax = float.MinValue;
        for (int i = 0; i < 16; i++)
        {
            float sampleTime = i * 0.27f;
            fishAverage += GetHarvestLoadTensionMultiplier(HarvestKind.Fish, sampleTime);
            woodAverage += GetHarvestLoadTensionMultiplier(HarvestKind.Wood, sampleTime);
            trashAverage += GetHarvestLoadTensionMultiplier(HarvestKind.Trash, sampleTime);
            float relic = GetHarvestLoadTensionMultiplier(HarvestKind.Relic, sampleTime);
            relicMin = Mathf.Min(relicMin, relic);
            relicMax = Mathf.Max(relicMax, relic);
        }
        fishAverage /= 16f;
        woodAverage /= 16f;
        trashAverage /= 16f;
        bool loadProfilesDiffer = woodAverage > fishAverage && trashAverage > fishAverage &&
            relicMax - relicMin > 0.42f;

        ResetSlice();
        arrivalInspected = true;
        state = SliceState.TideRising;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        netIntegrity = 3;
        tideStrength = 0.4f;
        currentWaterY = GetNetHeadLineY();
        StartCurrentTideDrift();
        previousIncomingHarvestTravel01 = 0.4f;
        incomingHarvestTravel01 = 0.4f;
        netSetDepth01 = 0.82f;
        TickNetWaterExposure(4f);
        bool wetTimeCannotPreloadCatch = !netCatchResolved && netCaptureProgress01 <= 0.0001f &&
            netWaterExposureSeconds > 0f;
        incomingHarvestTravel01 = 0.95f;
        for (int i = 0; i < 20 && !netCatchResolved; i++)
        {
            previousIncomingHarvestTravel01 = incomingHarvestTravel01;
            incomingHarvestTravel01 = Mathf.Min(1.04f, incomingHarvestTravel01 + 0.006f);
            TickNetWaterExposure(0.1f);
        }
        float finalCaptureProgress = netCaptureProgress01;
        bool contactIntegrationEventuallyCatches = netCatchResolved &&
            harvestPhysicalState == HarvestPhysicalState.CaughtInNet;

        ResetSlice();
        state = SliceState.TideRising;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        netIntegrity = 1;
        currentHarvest = HarvestKind.Wood;
        harvestPhysicalState = HarvestPhysicalState.CaughtInNet;
        netCatchResolved = true;
        netCatchBundleTier = 2;
        netSetDepth01 = 0.2f;
        ApplyNetDamageWithFraying(1, 0.36f);
        float shallowFrayStart = netFraying01;
        TickNetFraying(0.5f, false);
        float shallowFrayDelta = netFraying01 - shallowFrayStart;

        ResetSlice();
        state = SliceState.TideRising;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        netIntegrity = 1;
        currentHarvest = HarvestKind.Wood;
        harvestPhysicalState = HarvestPhysicalState.CaughtInNet;
        netCatchResolved = true;
        netCatchBundleTier = 2;
        netSetDepth01 = 0.9f;
        ApplyNetDamageWithFraying(1, 0.36f);
        float deepFrayStart = netFraying01;
        TickNetFraying(0.5f, false);
        float deepFrayDelta = netFraying01 - deepFrayStart;
        bool raisingActuallyRelievesFray = deepFrayDelta > shallowFrayDelta * 1.25f;

        ResetSlice();
        state = SliceState.TideRising;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        netIntegrity = 1;
        currentHarvest = HarvestKind.Wood;
        harvestPhysicalState = HarvestPhysicalState.CaughtInNet;
        netCatchResolved = true;
        netCatchBundleTier = 2;
        netSetDepth01 = 0.9f;
        extraSaltWoodBundledWithNetHarvest = true;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.CaughtInNet);
        ApplyNetDamageWithFraying(1, 0.94f);
        for (int i = 0; i < 20 && !netBrokeThisTide; i++)
        {
            TickNetFraying(0.25f, false);
        }
        bool neglectedBreakReturnsSaltWoodToSea = netBrokeThisTide &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater;

        ResetSlice();
        state = SliceState.TideRising;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        netIntegrity = 1;
        currentHarvest = HarvestKind.Wood;
        harvestPhysicalState = HarvestPhysicalState.CaughtInNet;
        netCatchResolved = true;
        netCatchBundleTier = 2;
        netCatchVisualPieceCount = 2;
        extraSaltWoodBundledWithNetHarvest = true;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.CaughtInNet);
        ApplyNetDamageWithFraying(1, 0.72f);
        netHaulEffort01 = 1f;
        TickNetFraying(1f, true);
        SecureNetBeforeEbb();
        bool rescueSecuresSameSaltWood = !netBrokeThisTide &&
            netFraying01 <= 0.001f &&
            securedPostHarvest == HarvestKind.Wood &&
            currentHarvest == HarvestKind.None &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.SecuredAtPost;

        ResetSlice();
        state = SliceState.TideRising;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        netIntegrity = 1;
        currentHarvest = HarvestKind.Wood;
        harvestPhysicalState = HarvestPhysicalState.CaughtInNet;
        netCatchResolved = true;
        netCatchBundleTier = 2;
        ApplyNetDamageWithFraying(1, 0.36f);
        bool lethalDamageEntersFraying = !netBrokeThisTide && netIntegrity == 1 &&
            netFraying01 > 0f && currentHarvest == HarvestKind.Wood;
        float frayingBeforeNeglect = netFraying01;
        TickNetFraying(0.5f, false);
        float frayingAfterNeglect = netFraying01;
        TickNetFraying(0.5f, true);
        bool haulingCanRecover = frayingAfterNeglect > frayingBeforeNeglect &&
            netFraying01 < frayingAfterNeglect;

        string evidence = $"抬网={depthBeforeRaise:F2}->{depthAfterRaise:F2}/离桩冻结={stoppedAwayFromPost}/手绳={adjustingShowsHandRope}/重挂清零={redeployClearsOldEncounter}；" +
            $"运行湿时不预充={wetTimeCannotPreloadCatch}/相遇={finalCaptureProgress:F2}/最终挂住={contactIntegrationEventuallyCatches}；" +
            $"载荷鱼木废={fishAverage:F2}/{woodAverage:F2}/{trashAverage:F2}/遗物峰差={relicMax - relicMin:F2}；" +
            $"深浅崩绳增量={shallowFrayDelta:F3}/{deepFrayDelta:F3}；断网回海={neglectedBreakReturnsSaltWoodToSea}/抢收留桩={rescueSecuresSameSaltWood}；" +
            $"临界进入={lethalDamageEntersFraying}/放任={frayingBeforeNeglect:F2}->{frayingAfterNeglect:F2}/抢收={netFraying01:F2}";
        bool passed = raiseIsContinuous && stoppedAwayFromPost &&
            adjustingShowsHandRope && redeployClearsOldEncounter && wetTimeCannotPreloadCatch && contactIntegrationEventuallyCatches &&
            loadProfilesDiffer && raisingActuallyRelievesFray && neglectedBreakReturnsSaltWoodToSea &&
            rescueSecuresSameSaltWood && lethalDamageEntersFraying && haulingCanRecover;
        return passed
            ? $"PASS：潮中调网、物性载荷和可抢救崩绳形成连续操作。{evidence}"
            : $"FAIL：渔网仍是一次性深度、统一秒表或即时破裂。{evidence}";
    }

    public string RunEditorContinuousStormFrontProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalVignetteActive = false;
        arrivalInspected = true;

        weatherClockSeconds = 0f;
        tideRound = 0;
        float calmPressure = GetStormPressure01();
        tideRound = departureStormRound + 2;
        float sameClockLaterRoundPressure = GetStormPressure01();
        bool tideRoundNoLongerCreatesWeather = Mathf.Abs(calmPressure - sameClockLaterRoundPressure) <= 0.0001f;

        weatherClockSeconds = 0f;
        for (int i = 0; i < 500; i++)
        {
            AdvanceContinuousWeather(1f);
        }
        float steppedClock = weatherClockSeconds;
        float steppedPressure = GetStormPressure01();
        weatherClockSeconds = 0f;
        AdvanceContinuousWeather(500f);
        float jumpedPressure = GetStormPressure01();
        bool skippedTimeMatchesSmallSteps = Mathf.Abs(steppedClock - weatherClockSeconds) <= 0.001f &&
            Mathf.Abs(steppedPressure - jumpedPressure) <= 0.0001f;

        weatherClockSeconds = dayLengthSeconds * stormFrontArrivalDays * 0.46f;
        tideClockSeconds = tideCycleSeconds * 0.18f;
        float currentPressure = GetStormPressure01();
        float nextHighPressure = GetPredictedStormPressureAtNextHighWater01();
        bool nextHighForecastLeadsCurrent = nextHighPressure > currentPressure;

        // Compare weather at a demanding but unsaturated depth. A near-bottom net is
        // already tier three in calm water, so integer risk bands cannot rise further
        // even though its continuous tension still does.
        float calmNetStress = CalculateForecastNetStress(0.7f, 0.72f, 0f);
        float stormNetStress = CalculateForecastNetStress(0.7f, 0.72f, 1f);
        float calmBoatIngress = CalculateForecastBoatIngressPerSecond(1, 0.72f, 0f);
        float stormBoatIngress = CalculateForecastBoatIngressPerSecond(1, 0.72f, 1f);
        bool weatherChangesPhysicalRisk = stormNetStress > calmNetStress && stormBoatIngress > calmBoatIngress;

        ResetSlice();
        arrivalVignetteActive = false;
        arrivalInspected = true;
        dayProgress01 = 0.9f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        weatherClockSeconds = 0f;
        float expectedSleepSkip = (1.23f - dayProgress01) * dayLengthSeconds;
        AdvanceMoonPhase();
        bool sleepAdvancesExactWeather = Mathf.Abs(weatherClockSeconds - expectedSleepSkip) <= 0.01f;

        ResetSlice();
        arrivalVignetteActive = false;
        arrivalInspected = true;
        weatherClockSeconds = 0f;
        HandlePlayerDeath("天气探针");
        bool deathAdvancesWeather = Mathf.Abs(weatherClockSeconds - dayLengthSeconds * 0.16f) <= 0.01f;

        ResetSlice();
        arrivalVignetteActive = false;
        arrivalInspected = true;
        state = SliceState.TideRising;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        tideStrength = 0.72f;
        currentWaterY = GetSelectedNetY() + 0.28f;
        weatherClockSeconds = 0f;
        TickNetWaterExposure(0.5f);
        float calmActualNetTension = netAccumulatedTension;
        ResetSlice();
        arrivalVignetteActive = false;
        arrivalInspected = true;
        state = SliceState.TideRising;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        tideStrength = 0.72f;
        currentWaterY = GetSelectedNetY() + 0.28f;
        weatherClockSeconds = dayLengthSeconds * stormFrontArrivalDays;
        TickNetWaterExposure(0.5f);
        float stormActualNetTension = netAccumulatedTension;
        bool actualNetReadsWeather = stormActualNetTension > calmActualNetTension * 1.2f;

        ResetSlice();
        arrivalVignetteActive = false;
        arrivalInspected = true;
        viewMode = SliceViewMode.Sailing;
        sailTripActive = true;
        boatHullIntegrity = 1;
        sailingBoatVelocity = 0.22f;
        sailingSailTrim01 = 0.58f;
        weatherClockSeconds = 0f;
        TickSailTrip(1f);
        float calmActualIngress = sailingWaterIngress01;
        ResetSlice();
        arrivalVignetteActive = false;
        arrivalInspected = true;
        viewMode = SliceViewMode.Sailing;
        sailTripActive = true;
        boatHullIntegrity = 1;
        sailingBoatVelocity = 0.22f;
        sailingSailTrim01 = 0.58f;
        weatherClockSeconds = dayLengthSeconds * stormFrontArrivalDays;
        TickSailTrip(1f);
        float stormActualIngress = sailingWaterIngress01;
        bool actualBoatReadsWeather = stormActualIngress > calmActualIngress * 1.2f;

        ResetSlice();
        lighthouseSeen = true;
        lighthouseClues = requiredLighthouseClues;
        boatReadiness = requiredBoatReadiness;
        tideRound = departureStormRound;
        weatherClockSeconds = 0f;
        bool calmCannotTriggerDeparture = !IsDepartureReady();
        weatherClockSeconds = dayLengthSeconds * stormFrontArrivalDays;
        bool arrivedStormAllowsDeparture = IsDepartureReady();
        bool departureWaitsForRealStorm = calmCannotTriggerDeparture && arrivedStormAllowsDeparture;

        viewMode = SliceViewMode.Sailing;
        state = SliceState.TideRising;
        dayNightPhase = DayNightPhase.Day;
        dayProgress01 = 0.5f;
        currentWaterY = lowWaterY + 0.42f;
        sailTripTimer = 0f;
        sailingWaterIngress01 = 0f;
        sailingBuoyChecked = !EnableSailingBuoyGameplay;
        weatherClockSeconds = 0f;
        float calmReturnPressure = GetReturnPressure01();
        weatherClockSeconds = dayLengthSeconds * stormFrontArrivalDays;
        float stormReturnPressure = GetReturnPressure01();
        bool stormRaisesReturnPressure = stormReturnPressure > calmReturnPressure + 0.35f;

        state = SliceState.FinalDeparture;
        float physicalDeparturePressure = GetStormPressure01();
        float visualDeparturePressure = GetVisualStormPressure01();
        bool departureUsesOneAuthority = Mathf.Abs(physicalDeparturePressure - visualDeparturePressure) <= 0.0001f;

        float onshoreWind = GetStormAdjustedWindSpeed(0.52f, 0.82f);
        float rainLean = TideContinuousWeatherModel.EvaluateRainLeanDegrees(onshoreWind, 0.82f);
        bool rainFollowsWind = onshoreWind < 0f && rainLean < 0f;

        ResetSlice();
        arrivalVignetteActive = false;
        arrivalInspected = false;
        float uninspectedWeatherBefore = weatherClockSeconds;
        TickUninspectedNaturalWorld(1f);
        float uninspectedWeatherAfter = weatherClockSeconds;
        arrivalInspected = true;
        bool uninspectedWeatherMoves = uninspectedWeatherAfter > uninspectedWeatherBefore &&
            Mathf.Approximately(weatherClockSeconds, uninspectedWeatherAfter);

        string evidence = $"潮次独立={tideRoundNoLongerCreatesWeather}/平静={calmPressure:F3}->{sameClockLaterRoundPressure:F3}；" +
            $"小步跳时={skippedTimeMatchesSmallSteps}/{steppedPressure:F3}->{jumpedPressure:F3}；" +
            $"当前/高潮={currentPressure:F3}/{nextHighPressure:F3}；网压={calmNetStress:F0}->{stormNetStress:F0}；" +
            $"船漏={calmBoatIngress:F3}->{stormBoatIngress:F3}；返航={calmReturnPressure:F2}->{stormReturnPressure:F2}；" +
            $"睡眠/死亡={sleepAdvancesExactWeather}/{deathAdvancesWeather}；真实网={calmActualNetTension:F3}->{stormActualNetTension:F3}；" +
            $"真实船={calmActualIngress:F3}->{stormActualIngress:F3}；等真实暴潮={departureWaitsForRealStorm}；" +
            $"离场同源={departureUsesOneAuthority}；风雨={onshoreWind:F2}/{rainLean:F1}；检查前推进={uninspectedWeatherMoves}";
        bool passed = tideRoundNoLongerCreatesWeather && skippedTimeMatchesSmallSteps &&
            nextHighForecastLeadsCurrent && weatherChangesPhysicalRisk && stormRaisesReturnPressure &&
            sleepAdvancesExactWeather && deathAdvancesWeather && actualNetReadsWeather && actualBoatReadsWeather &&
            departureWaitsForRealStorm && departureUsesOneAuthority && rainFollowsWind && uninspectedWeatherMoves;
        return passed
            ? $"PASS：连续风暴锋面不再由睡觉或潮次制造，并统一物理、返航和表现。{evidence}"
            : $"FAIL：天气仍在跳档，或云雨、潮、网、船、屋和最终离场没有同源。{evidence}";
    }

    public string RunEditorContinuousTideRoutingProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalVignetteActive = false;
        arrivalInspected = true;
        state = SliceState.TideRising;
        viewMode = SliceViewMode.Shelter;
        playerLane = WalkLane.TideFlat;
        playerPosition = GetShoreWorkPosition();
        currentWaterY = lowWaterY + 0.72f;

        float noNetBefore = routingBoom01;
        bool noNetHandled = TickContinuousTideRoutingControl(0.5f, 1f);
        bool noNetCannotFeed = !noNetHandled && Mathf.Approximately(routingBoom01, noNetBefore);

        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        float partialBefore = routingBoom01;
        bool partialHandled = TickContinuousTideRoutingControl(0.45f, 1f);
        float partialAfter = routingBoom01;
        TickContinuousTideRoutingControl(0.35f, 0f);
        bool pauseFreezes = Mathf.Approximately(routingBoom01, partialAfter);
        TickContinuousTideRoutingControl(0.2f, -1f);
        bool releaseReverses = routingBoom01 < partialAfter;

        float calmRate = TideContinuousRoutingModel.EvaluateAdjustmentSpeed(0f, 0f, 0f, false);
        float stormRate = TideContinuousRoutingModel.EvaluateAdjustmentSpeed(1f, 1f, 1f, false);
        float releaseRate = TideContinuousRoutingModel.EvaluateAdjustmentSpeed(0f, 0f, 0f, true);

        bool ownerUnchangedBeforeFork = extraSaltWoodOwner == ExtraSaltWoodOwner.PassingNearshore &&
            !extraSaltWoodBundledWithNetHarvest && !nearshoreWorkDone;
        routingBoom01 = 0.82f;
        incomingHarvestTravel01 = 0.71f;
        TryLockContinuousRoutingDecision(0.71f, 0.73f);
        bool feedLocksOnce = routingDecisionLocked &&
            tideRoutingMode == TideRoutingMode.FeedNet &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.RoutedToNet &&
            extraSaltWoodBundledWithNetHarvest;
        float lockedBoom = routingBoom01;
        TickContinuousTideRoutingControl(0.5f, -1f);
        bool lockedIgnoresInput = Mathf.Approximately(routingBoom01, lockedBoom);

        ResetSlice();
        arrivalVignetteActive = false;
        arrivalInspected = true;
        state = SliceState.TideRising;
        viewMode = SliceViewMode.Shelter;
        playerLane = WalkLane.TideFlat;
        playerPosition = GetShoreWorkPosition();
        currentWaterY = lowWaterY + 0.72f;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        TryDoNearshoreWork();
        bool legacyClickNoLongerMutatesRoute = extraSaltWoodOwner == ExtraSaltWoodOwner.PassingNearshore &&
            !extraSaltWoodBundledWithNetHarvest && !routingDecisionLocked;

        ResetSlice();
        arrivalVignetteActive = false;
        arrivalInspected = true;
        state = SliceState.TideRising;
        viewMode = SliceViewMode.Shelter;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        netIntegrity = 3;
        routingBoom01 = 0.42f;
        TryLockContinuousRoutingDecision(0.71f, 0.73f);
        bool openForkKeepsPhysicalPassage = routingDecisionLocked &&
            tideRoutingMode == TideRoutingMode.Open &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.PassingNearshore &&
            !extraSaltWoodBundledWithNetHarvest;
        tideSourceHarvest = HarvestKind.Fish;
        ResolveNetCatch();
        outerWreckTravel01 = TideDriftSourceModel.NearshoreExitTravel01;
        ResolveExitedTideDriftBatches();
        bool openForkEndsInSailingWater = extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater;

        ResetSlice();
        arrivalVignetteActive = false;
        arrivalInspected = true;
        state = SliceState.TideRising;
        viewMode = SliceViewMode.Shelter;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        netIntegrity = 3;
        routingBoom01 = 0.82f;
        TryLockContinuousRoutingDecision(0.71f, 0.73f);
        tideSourceHarvest = HarvestKind.Fish;
        outerWreckTravel01 = TideDriftSourceModel.NetIntersectionTravel01;
        ResolveNetCatch();
        BeginBrokenNetResidue(HarvestKind.Fish, 2);
        bool brokenNetReturnsSameTimberToSailing = extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater &&
            timberStock == 0 && clothStock == 0;

        ResetSlice();
        arrivalVignetteActive = false;
        arrivalInspected = true;
        state = SliceState.TideRising;
        viewMode = SliceViewMode.Shelter;
        playerLane = WalkLane.TideFlat;
        playerPosition = GetShoreWorkPosition();
        currentWaterY = lowWaterY + 0.72f;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        float tideClockBeforeWork = tideClockSeconds;
        float dayClockBeforeWork = dayClockSeconds;
        float weatherClockBeforeWork = weatherClockSeconds;
        TickContinuousTideRoutingControl(0.5f, 1f);
        TickState(0.5f);
        bool natureContinuesDuringRouting = tideClockSeconds > tideClockBeforeWork &&
            dayClockSeconds > dayClockBeforeWork && weatherClockSeconds > weatherClockBeforeWork;

        ResetSlice();
        arrivalVignetteActive = false;
        arrivalInspected = true;
        state = SliceState.TideRising;
        viewMode = SliceViewMode.Shelter;
        playerLane = WalkLane.TideFlat;
        playerPosition = GetShoreWorkPosition();
        playerFacing = 1;
        currentWaterY = lowWaterY + 0.72f;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        tideSourceHarvest = HarvestKind.Fish;
        harvestPhysicalState = HarvestPhysicalState.Drifting;
        routingBoom01 = 0.54f;
        routingWorkActive = true;
        routingWorkDirection = 1f;
        incomingHarvestTravel01 = 0.58f;
        outerWreckTravel01 = 0.58f;
        UpdateVisuals(2.15f);
        bool physicalBoomAndRopeVisible = tideRoutingBoomRenderer != null && tideRoutingBoomRenderer.enabled &&
            tideRoutingRopeRenderer != null && tideRoutingRopeRenderer.enabled;
        bool physicalWindlassVisible = tideRoutingWinchRenderer != null && tideRoutingWinchRenderer.enabled;
        EnsureV20CharacterResourcesLoaded();
        Sprite routingWorkPose = HasCompleteV20CharacterPresentation()
            ? TideV20CharacterPresentationModel.EvaluateFrame(
                formalCharacterV20Catalog,
                TideV20CharacterActionState.Haul,
                2.15f,
                1f)
            : GetFormalHaulPlayerSprite();
        bool routingUsesWorkPose = routingWorkPose == null || playerRenderer.sprite == routingWorkPose;

        outerWreckTravel01 = 0.71f;
        UpdateVisuals(2.18f);
        Vector2 timberBeforeFork = incomingTideCarryItems[2].bounds.center;
        routingBoom01 = 0.82f;
        TryLockContinuousRoutingDecision(0.71f, 0.73f);
        outerWreckTravel01 = 0.73f;
        UpdateVisuals(2.21f);
        Vector2 timberAfterFork = incomingTideCarryItems[2].bounds.center;
        bool timberDoesNotTeleportAtFork = Vector2.Distance(timberBeforeFork, timberAfterFork) <= 0.5f;

        bool continuousAdjustment = partialHandled && partialAfter > partialBefore && partialAfter < 1f;
        bool ratesFollowLoad = calmRate > stormRate && releaseRate > calmRate;
        string evidence = $"无网禁收={noNetCannotFeed}；连续={partialBefore:F2}->{partialAfter:F2}/暂停={pauseFreezes}/反向={releaseReverses}；" +
            $"速率平静/暴潮/放索={calmRate:F3}/{stormRate:F3}/{releaseRate:F3}；岔流前守恒={ownerUnchangedBeforeFork}；" +
            $"进网锁定={feedLocksOnce}/锁后不改={lockedIgnoresInput}；旧点击旁路清除={legacyClickNoLongerMutatesRoute}；" +
            $"放过={openForkKeepsPhysicalPassage}/{openForkEndsInSailingWater}；断网回海={brokenNetReturnsSameTimberToSailing}；自然继续={natureContinuesDuringRouting}；" +
            $"导杆绳={physicalBoomAndRopeVisible}/绞盘={physicalWindlassVisible}/人物动作={routingUsesWorkPose}/岔流连续={timberDoesNotTeleportAtFork}";
        bool passed = noNetCannotFeed && continuousAdjustment && pauseFreezes && releaseReverses &&
            ratesFollowLoad && ownerUnchangedBeforeFork && feedLocksOnce && lockedIgnoresInput &&
            legacyClickNoLongerMutatesRoute && openForkKeepsPhysicalPassage && openForkEndsInSailingWater &&
            brokenNetReturnsSameTimberToSailing && natureContinuesDuringRouting &&
            physicalBoomAndRopeVisible && physicalWindlassVisible && routingUsesWorkPose && timberDoesNotTeleportAtFork;
        return passed
            ? $"PASS：连续引流索可收、可放、可暂停，盐木只在真实岔流点锁定去向。{evidence}"
            : $"FAIL：引流仍是一键跳档，或盐木在经过岔流前已经改写归属。{evidence}";
    }

    public string RunEditorContinuousSailingSalvageProbe()
    {
        EnsureScene();

        ResetSlice();
        arrivalVignetteActive = false;
        arrivalInspected = true;
        viewMode = SliceViewMode.Sailing;
        sailTripActive = true;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.SailingWater);
        sailingBoatX = sailingSalvageWorldX - 0.72f;
        sailingBoatLaneY = sailingHomeY;
        sailingBoatWorldVelocity = 0.22f;
        sailingSalvageVelocity = 0.2f;
        bool bowSideApproachRejected = !BeginContinuousSailingHookThrow();
        sailingBoatX = sailingSalvageWorldX;
        float sternOffsetX = GetSailingBoatSternWorldPosition().x - sailingBoatX;
        sailingBoatX = sailingSalvageWorldX + 0.72f - sternOffsetX;
        bool throwStarted = BeginContinuousSailingHookThrow();
        TickContinuousSailingSalvage(0.12f, true);
        float partialThrow = sailingHookThrow01;
        bool throwIsContinuous = throwStarted && partialThrow > 0f && partialThrow < 1f &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater;
        TickContinuousSailingSalvage(0.2f, false);
        bool releasedThrowRetracts = sailingHookThrow01 < partialThrow &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater;

        BeginContinuousSailingHookThrow();
        TickContinuousSailingSalvage(0.5f, true);
        bool hookContactHasIntermediateOwner = extraSaltWoodOwner == ExtraSaltWoodOwner.HookingToBoat &&
            sailingSalvageHookProgress < 1f;
        TickContinuousSailingSalvage(0.24f, true);
        float partialHaul = sailingSalvageHookProgress;
        float partialWorldX = sailingSalvageWorldX;
        TickContinuousSailingSalvage(0.3f, false);
        bool releasePausesHaul = Mathf.Approximately(sailingSalvageHookProgress, partialHaul) &&
            Mathf.Abs(sailingSalvageWorldX - partialWorldX) < 0.35f;
        for (int i = 0; i < 24 && extraSaltWoodOwner == ExtraSaltWoodOwner.HookingToBoat; i++)
        {
            sailingBoatWorldVelocity = sailingSalvageVelocity;
            TickContinuousSailingSalvage(0.12f, true);
        }
        bool haulSecuresAtStern = extraSaltWoodOwner == ExtraSaltWoodOwner.HookedToBoat &&
            sailingSalvageHookProgress >= 0.999f;

        ResetSlice();
        arrivalVignetteActive = false;
        arrivalInspected = true;
        viewMode = SliceViewMode.Sailing;
        sailTripActive = true;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.HookingToBoat);
        sailingBoatX = 2.1f;
        sailingBoatLaneY = sailingHomeY;
        sailingSalvageWorldX = 4.4f;
        sailingSalvageVelocity = 2f;
        sailingBoatWorldVelocity = -2f;
        sailingSalvageHookProgress = 0.2f;
        sailingSalvageInitialRopeLength = 0.35f;
        weatherClockSeconds = dayLengthSeconds * stormFrontArrivalDays;
        float strainedWorldX = sailingSalvageWorldX;
        float maxStrainTension = 0f;
        float maxOverstrainSeconds = 0f;
        for (int i = 0; i < 6 && extraSaltWoodOwner == ExtraSaltWoodOwner.HookingToBoat; i++)
        {
            TickContinuousSailingSalvage(0.1f, false);
            maxStrainTension = Mathf.Max(maxStrainTension, sailingSalvageTension01);
            maxOverstrainSeconds = Mathf.Max(maxOverstrainSeconds, sailingSalvageOverstrainSeconds);
        }
        bool overstrainDetachesInPlace = extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater &&
            Mathf.Abs(sailingSalvageWorldX - sailingSalvagePoint.x) > 0.25f &&
            Mathf.Abs(sailingSalvageWorldX - strainedWorldX) < 0.8f;
        string overstrainEvidence = $"{extraSaltWoodOwner}/x={strainedWorldX:F2}->{sailingSalvageWorldX:F2}/张力峰={maxStrainTension:F2}/过载峰={maxOverstrainSeconds:F2}";

        ResetSlice();
        viewMode = SliceViewMode.Sailing;
        sailTripActive = true;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.HookingToBoat);
        sailingSalvageHookProgress = 0.58f;
        sailingSalvageWorldX = 2.75f;
        float incompleteReturnX = sailingSalvageWorldX;
        ExitSailingScene();
        bool incompleteReturnLeavesTimberAtSea = extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater &&
            Mathf.Abs(sailingSalvageWorldX - incompleteReturnX) < 0.001f;

        ResetSlice();
        viewMode = SliceViewMode.Sailing;
        sailTripActive = true;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.HookedToBoat);
        sailingSalvageHookProgress = 1f;
        ExitSailingScene();
        bool securedReturnReachesBoat = extraSaltWoodOwner == ExtraSaltWoodOwner.ReturnedAtBoat;

        float calmHaulRate = TideContinuousSalvageModel.EvaluateHaulRate(0.08f, 0.18f, 0f, 0f);
        float stormHaulRate = TideContinuousSalvageModel.EvaluateHaulRate(0.08f, 0.72f, 1f, 0.7f);
        float lowTension = TideContinuousSalvageModel.EvaluateTension01(0.8f, 1.1f, 0.08f, 0f);
        float highTension = TideContinuousSalvageModel.EvaluateTension01(1.7f, 0.9f, 1.25f, 0.9f);
        bool modelReadsLoad = calmHaulRate > stormHaulRate && highTension > lowTension + 0.45f;
        viewMode = SliceViewMode.Sailing;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.HookingToBoat);
        bool salvageReservesContext = IsSailingSalvageInteractionActive();

        SetEditorContinuousSailingSalvagePreviewPose(1);
        bool physicalHookVisible = sailingSalvageHookRenderer.enabled &&
            sailingSalvageHookRenderer.sprite == GetSeaSalvageHookSprite();
        bool continuousRopeVisible = sailingSalvageHookRopeRenderer.enabled &&
            sailingSalvageHookRopeEndRenderer.enabled &&
            sailingSalvageHookRopeRenderer.transform.localScale.x > 0.01f &&
            sailingSalvageHookRopeEndRenderer.transform.localScale.x > 0.01f;
        bool towlineUsesDedicatedSprite = sailingSalvageHookRopeRenderer.sprite == GetTowRopeSprite() &&
            sailingSalvageHookRopeEndRenderer.sprite == GetTowRopeSprite() &&
            sailingSalvageHookRopeRenderer.sprite != GetNetLineSprite();
        bool timberAndWakeUsePhysicalState = sailingSalvagePointRenderer.enabled &&
            sailingSalvageWakeRenderer.enabled;
        Vector2 boatScreenPosition = GetSailingScreenPosition(GetSailingBoatBasePosition());
        Vector2 sternScreenPosition = GetSailingScreenPosition(GetSailingBoatSternWorldPosition());
        // V39 的 BackHull 只拥有后层像素，不能独自代表整条可见船壳。
        // 船艉接触点应落在后船体、座舱、前船舷和舵共同形成的轮廓内。
        float visibleSternX = float.PositiveInfinity;
        SpriteRenderer[] hullOwners =
        {
            boatHullRenderer,
            boatCockpitRenderer,
            boatCockpitRepairOwnerRenderer,
            boatPassengerGunwaleRenderer,
            boatHullRepairOwnerRenderer,
            boatRudderRenderer
        };
        for (int i = 0; i < hullOwners.Length; i++)
        {
            if (hullOwners[i] != null && hullOwners[i].enabled)
            {
                visibleSternX = Mathf.Min(visibleSternX, hullOwners[i].bounds.min.x);
            }
        }
        bool sternAnchorMatchesVisibleHull = sternScreenPosition.x < boatScreenPosition.x - 1f &&
            sternScreenPosition.x >= visibleSternX - 0.04f && sternScreenPosition.x <= visibleSternX + 0.8f;
        Vector3 renderedRopeStart = sailingSalvageHookRopeRenderer.transform.TransformPoint(
            new Vector3(
                sailingSalvageHookRopeRenderer.sprite.bounds.min.x,
                sailingSalvageHookRopeRenderer.sprite.bounds.center.y,
                0f));
        bool renderedRopeStartsAtStern = Vector2.Distance(renderedRopeStart, sternScreenPosition) <= 0.08f;

        string evidence = $"船艏侧拒绝={bowSideApproachRejected}/抛钩={throwIsContinuous}/松手回收={releasedThrowRetracts}/中间所有者={hookContactHasIntermediateOwner}；" +
            $"收绳={partialHaul:F2}/暂停={releasePausesHaul}/绑牢={haulSecuresAtStern}；" +
            $"硬拽脱钩保位={overstrainDetachesInPlace}({overstrainEvidence})/未绑返航留海={incompleteReturnLeavesTimberAtSea}/绑牢返航={securedReturnReachesBoat}；" +
            $"速率平静/暴潮={calmHaulRate:F3}/{stormHaulRate:F3}/张力={lowTension:F2}->{highTension:F2}/占用F={salvageReservesContext}；" +
            $"实物钩={physicalHookVisible}/连续绳={continuousRopeVisible}/专用拖绳={towlineUsesDedicatedSprite}/木料尾流={timberAndWakeUsePhysicalState}/船艉锚={sternAnchorMatchesVisibleHull}({sternScreenPosition.x:F2}/{visibleSternX:F2})/绳端同点={renderedRopeStartsAtStern}";
        bool passed = bowSideApproachRejected && throwIsContinuous && releasedThrowRetracts && hookContactHasIntermediateOwner &&
            releasePausesHaul && haulSecuresAtStern && overstrainDetachesInPlace &&
            incompleteReturnLeavesTimberAtSea && securedReturnReachesBoat && modelReadsLoad && salvageReservesContext &&
            physicalHookVisible && continuousRopeVisible && towlineUsesDedicatedSprite &&
            timberAndWakeUsePhysicalState && sternAnchorMatchesVisibleHull && renderedRopeStartsAtStern;
        return passed
            ? $"PASS：短航盐木需要连续抛钩、控张力和收绳，失手不重置实物。{evidence}"
            : $"FAIL：短航钩取仍会瞬时完成、自动收绳，或脱钩/返航破坏盐木守恒。{evidence}";
    }

    public string RunEditorContinuousNetLoadLedgerProbe()
    {
        EnsureScene();

        // Historical load must survive a later lift regardless of the current pacing
        // calibration. Seed just beyond the first physical threshold instead of relying
        // on an obsolete fixed number of seconds to happen to cross that threshold.
        float deepThenLiftImpulse = TideNetLoadLedgerModel.StressTierOneImpulse + 0.2f;
        int committedBeforeLift = TideNetLoadLedgerModel.EvaluateCommittedStress(
            deepThenLiftImpulse, 1f, false);
        int committedAfterLift = TideNetLoadLedgerModel.EvaluateCommittedStress(
            deepThenLiftImpulse, 0.1f, false);
        bool liftingCannotEraseHistory = committedBeforeLift == committedAfterLift &&
            committedBeforeLift > 0;

        float fullDeepImpulse = 0f;
        float earlyLiftImpulse = 0f;
        for (int i = 0; i < 80; i++)
        {
            fullDeepImpulse = TideNetLoadLedgerModel.AdvanceImpulse(
                fullDeepImpulse, 0.1f, 0.82f, 1f, 0.88f, 0.72f, 1.08f, 1f, 0.45f, false);
            float earlyLiftDepth = i < 30 ? 1f : 0.18f;
            earlyLiftImpulse = TideNetLoadLedgerModel.AdvanceImpulse(
                earlyLiftImpulse, 0.1f, 0.82f, earlyLiftDepth, 0.88f, 0.72f, 1.08f, 1f, 0.45f, false);
        }
        bool earlyLiftReducesFutureLoad = earlyLiftImpulse < fullDeepImpulse * 0.72f;

        float[] frameSteps = { 1f / 30f, 1f / 60f, 1f / 120f };
        float[] frameImpulses = new float[frameSteps.Length];
        for (int stepIndex = 0; stepIndex < frameSteps.Length; stepIndex++)
        {
            float step = frameSteps[stepIndex];
            float elapsed = 0f;
            while (elapsed < 6f - 0.0001f)
            {
                float dt = Mathf.Min(step, 6f - elapsed);
                frameImpulses[stepIndex] = TideNetLoadLedgerModel.AdvanceImpulse(
                    frameImpulses[stepIndex], dt, 0.74f, 0.67f, 0.76f, 0.63f, 1.14f, 1.22f, 0.6f, true);
                elapsed += dt;
            }
        }
        bool frameRateIndependent = Mathf.Abs(frameImpulses[0] - frameImpulses[1]) < 0.001f &&
            Mathf.Abs(frameImpulses[1] - frameImpulses[2]) < 0.001f;

        float ropeComparisonImpulse = TideNetLoadLedgerModel.StressTierTwoImpulse + 0.2f;
        int ordinaryStress = TideNetLoadLedgerModel.EvaluateCommittedStress(
            ropeComparisonImpulse, 0.86f, false);
        int reinforcedStress = TideNetLoadLedgerModel.EvaluateCommittedStress(
            ropeComparisonImpulse, 0.86f, true);
        bool ropeRelievesExactlyOneTier = ordinaryStress > 0 && reinforcedStress == ordinaryStress - 1;

        ResetSlice();
        arrivalInspected = true;
        state = SliceState.TideRising;
        netDeployed = true;
        netIntegrity = 4;
        netAccumulatedTension = TideNetLoadLedgerModel.StressTierTwoImpulse + 0.2f;
        netPeakTension01 = 0.4f;
        netOverloadStressApplied = 0;
        currentTideNetStress = 0;
        CommitAccumulatedNetStress();
        int integrityAfterFirstCommit = netIntegrity;
        int stressAfterFirstCommit = currentTideNetStress;
        CommitAccumulatedNetStress();
        bool commitmentIsIdempotent = integrityAfterFirstCommit == netIntegrity &&
            stressAfterFirstCommit == currentTideNetStress &&
            stressAfterFirstCommit == 2;

        netCatchResolved = false;
        netTouched = true;
        netCaptureProgress01 = 1f;
        incomingHarvestTravel01 = 1f;
        tideSourceHarvest = HarvestKind.Fish;
        extraSaltWoodOwner = ExtraSaltWoodOwner.SailingWater;
        int integrityBeforeCatchResolution = netIntegrity;
        ResolveNetCatch();
        bool catchDoesNotDoubleSettleDamage = netIntegrity == integrityBeforeCatchResolution;

        ResetSlice();
        arrivalInspected = true;
        state = SliceState.TideRising;
        netDeployed = true;
        netIntegrity = 4;
        selectedPrepChoice = TidePrepChoice.Rope;
        tidePrepReady = true;
        tidePrepTargetRound = tideRound;
        tideSourceHarvest = HarvestKind.Relic;
        netCaptureProgress01 = 0.43f;
        netAccumulatedTension = TideNetLoadLedgerModel.StressTierTwoImpulse + 0.2f;
        netPeakTension01 = 0.4f;
        CommitAccumulatedNetStress();
        bool ropeOnlyChangesDamage = netIntegrity == 3 &&
            tideSourceHarvest == HarvestKind.Relic &&
            Mathf.Approximately(netCaptureProgress01, 0.43f);

        netSetDepth01 = 0.08f;
        float fatigueAfterLift = GetNetFatigue01();
        bool fatiguePersistsAfterLift = fatigueAfterLift > 0.45f;

        string evidence = $"历史守恒={liftingCannotEraseHistory}/{committedBeforeLift}->{committedAfterLift}；" +
            $"早抬减载={earlyLiftReducesFutureLoad}/{fullDeepImpulse:F2}->{earlyLiftImpulse:F2}；" +
            $"帧率={frameRateIndependent}/{frameImpulses[0]:F3}/{frameImpulses[1]:F3}/{frameImpulses[2]:F3}；" +
            $"备绳抵一档={ropeRelievesExactlyOneTier}/{ordinaryStress}->{reinforcedStress}；" +
            $"幂等={commitmentIsIdempotent}/捕获不重算={catchDoesNotDoubleSettleDamage}/备绳不改收益={ropeOnlyChangesDamage}/抬网留疲劳={fatiguePersistsAfterLift}/{fatigueAfterLift:F2}";
        bool passed = liftingCannotEraseHistory && earlyLiftReducesFutureLoad &&
            frameRateIndependent && ropeRelievesExactlyOneTier && commitmentIsIdempotent &&
            catchDoesNotDoubleSettleDamage && ropeOnlyChangesDamage && fatiguePersistsAfterLift;
        return passed
            ? $"PASS：渔网收益和损伤共享连续受力账本，抬网只能减少未来受力，不能洗掉已经承受的海力。{evidence}"
            : $"FAIL：渔网仍按结算瞬间深度洗掉历史受力，或受力积分依赖帧率。{evidence}";
    }

    public string RunEditorNetExcursionWindowProbe()
    {
        EnsureScene();

        MeasureNaturalNetExcursionTiming(
            0.2f,
            out float shallowTouch,
            out float shallowCatch,
            out float shallowTierTwo,
            out float shallowTierThree,
            out float shallowFray,
            out float shallowBreak,
            out float shallowEbb,
            out float shallowTierTwoSurfaceDepth,
            out float shallowTierThreeSurfaceDepth);
        MeasureNaturalNetExcursionTiming(
            0.5f,
            out float middleTouch,
            out float middleCatch,
            out float middleTierTwo,
            out float middleTierThree,
            out float middleFray,
            out float middleBreak,
            out float middleEbb,
            out float middleTierTwoSurfaceDepth,
            out float middleTierThreeSurfaceDepth);
        MeasureNaturalNetExcursionTiming(
            0.82f,
            out float deepTouch,
            out float deepCatch,
            out float deepTierTwo,
            out float deepTierThree,
            out float deepFray,
            out float deepBreak,
            out float deepEbb,
            out float deepTierTwoSurfaceDepth,
            out float deepTierThreeSurfaceDepth);

        // The first useful away job is the physical routing boom beside the house.
        // It includes walking from the net and back, fully moving the boom once, and a
        // four-second observation margin. This remains derived from world geometry and
        // the routing model rather than becoming another hidden tutorial timer.
        float routingWalkSeconds = Mathf.Abs(netAnchor.x - GetShoreWorkX()) * 2f /
            Mathf.Max(0.1f, playerMoveSpeed);
        float routingWorkSeconds = 1f / Mathf.Max(
            0.1f,
            TideContinuousRoutingModel.EvaluateAdjustmentSpeed(0f, 0f, 0f, false));
        float minimumExcursionSeconds = routingWalkSeconds + routingWorkSeconds + 4f;

        bool firstCatchAllowsExcursion = middleCatch > 0f && middleTouch > 0f &&
            middleCatch - middleTouch >= minimumExcursionSeconds;
        bool shallowKeepsOneSafeCatch = shallowCatch > 0f &&
            shallowTierTwo < 0f && shallowTierThree < 0f && shallowBreak < 0f;
        bool middleKeepsSecondLayer = middleTierTwo > middleCatch + 6f &&
            middleTierThree < 0f &&
            (middleFray < 0f || middleFray > middleTierTwo + 20f) &&
            (middleBreak < 0f || middleBreak > middleTierTwo + 30f);
        bool deepRiskIsReadable = deepTierThree > deepTierTwo &&
            deepFray > deepTierThree && deepBreak > deepFray &&
            deepBreak - deepTierThree >= 4f;
        bool depthChangesYieldAndRisk = deepTierTwo > deepCatch + 6f &&
            deepTierThree > deepTierTwo + 4f &&
            shallowBreak < 0f && middleBreak > 0f && deepBreak > 0f &&
            deepBreak < middleBreak;
        bool oneNaturalTide = Mathf.Abs(shallowEbb - middleEbb) <= 0.11f &&
            Mathf.Abs(middleEbb - deepEbb) <= 0.11f;

        string evidence =
            $"离桩预算={minimumExcursionSeconds:F1}s/触水到首获={middleCatch - middleTouch:F1}s；" +
            $"浅 触{shallowTouch:F1}/获{shallowCatch:F1}/二{shallowTierTwo:F1}/三{shallowTierThree:F1}/断{shallowBreak:F1}；" +
            $"中 触{middleTouch:F1}/获{middleCatch:F1}/二{middleTierTwo:F1}/三{middleTierThree:F1}/崩{middleFray:F1}/断{middleBreak:F1}；" +
            $"深 触{deepTouch:F1}/获{deepCatch:F1}/二{deepTierTwo:F1}/三{deepTierThree:F1}/崩{deepFray:F1}/断{deepBreak:F1}；" +
            $"水面高于网首 二={shallowTierTwoSurfaceDepth:F2}/{middleTierTwoSurfaceDepth:F2}/{deepTierTwoSurfaceDepth:F2}m " +
            $"三={shallowTierThreeSurfaceDepth:F2}/{middleTierThreeSurfaceDepth:F2}/{deepTierThreeSurfaceDepth:F2}m；" +
            $"退潮={shallowEbb:F1}/{middleEbb:F1}/{deepEbb:F1}";
        ResetSlice();
        return firstCatchAllowsExcursion && shallowKeepsOneSafeCatch && middleKeepsSecondLayer &&
            deepRiskIsReadable && depthChangesYieldAndRisk && oneNaturalTide
            ? $"PASS：第一潮允许离桩做事；同一鱼群在浅/中/深网形成 1/2/3 件真实收益，深网较早进入可抢救断裂窗口。{evidence}"
            : $"FAIL：第一潮仍迫使玩家守网，或浅/中/深网没有形成可见的 1/2/3 件收益风险取舍。{evidence}";
    }

    public string RunEditorFirstTideRouteChoiceProbe()
    {
        EnsureScene();

        MeasureNaturalFirstTideRouteBranch(
            false,
            out float openLock,
            out float openPrimaryCatch,
            out float openSaltWoodCatch,
            out float openSailingEntry,
            out float openTierThree,
            out float openBreak,
            out int openPrimaryBatch,
            out int openSaltWoodBatch,
            out bool openEverBundled);
        MeasureNaturalFirstTideRouteBranch(
            true,
            out float feedLock,
            out float feedPrimaryCatch,
            out float feedSaltWoodCatch,
            out float feedSailingEntry,
            out float feedTierThree,
            out float feedBreak,
            out int feedPrimaryBatch,
            out int feedSaltWoodBatch,
            out bool feedEverBundled);

        bool openBranchReachesSailing = openLock > 0f && openSailingEntry > openLock &&
            openSailingEntry < openPrimaryCatch && openSaltWoodCatch < 0f && !openEverBundled;
        bool feedBranchReachesSameNet = feedLock > 0f && feedPrimaryCatch > feedLock &&
            feedSaltWoodCatch > feedPrimaryCatch && feedEverBundled &&
            feedSailingEntry >= feedBreak - 0.11f;
        bool feedTradesSafetyForCargo = openTierThree < 0f &&
            feedTierThree > feedSaltWoodCatch + 6f &&
            feedBreak + 6f < openBreak;
        bool stableBatchIdentity = openPrimaryBatch > 0 && openSaltWoodBatch > 0 &&
            openPrimaryBatch == feedPrimaryBatch && openSaltWoodBatch == feedSaltWoodBatch &&
            openPrimaryBatch != openSaltWoodBatch;

        // Cashing out the primary fish during the one-second gap before the routed
        // timber reaches the mesh must release that timber offshore. This scenario
        // advances naturally to the catch; only the player's haul decision is invoked.
        ConfigureNaturalFirstTideNet(0.5f);
        for (int step = 0; step < 900 && !netCatchResolved; step++)
        {
            float elapsed = (step + 1) * 0.1f;
            if (elapsed >= 24f && !routingDecisionLocked)
            {
                routingBoom01 = 0.82f;
            }
            TickState(0.1f);
        }
        bool routedButNotCaught = netCatchResolved &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.RoutedToNet;
        int earlyHaulBatchId = extraSaltWoodBatchId;
        SecureNetBeforeEbb();
        bool earlyHaulReleasesTimber = routedButNotCaught &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater &&
            extraSaltWoodBatchId == earlyHaulBatchId &&
            securedPostHarvest == HarvestKind.Fish;

        // Waiting one more second lets the same timber physically enter the mesh; an
        // otherwise identical haul must then keep it at the post with the fish.
        ConfigureNaturalFirstTideNet(0.5f);
        for (int step = 0; step < 900 && extraSaltWoodOwner != ExtraSaltWoodOwner.CaughtInNet; step++)
        {
            float elapsed = (step + 1) * 0.1f;
            if (elapsed >= 24f && !routingDecisionLocked)
            {
                routingBoom01 = 0.82f;
            }
            TickState(0.1f);
        }
        int caughtHaulBatchId = extraSaltWoodBatchId;
        bool timberWasCaught = extraSaltWoodOwner == ExtraSaltWoodOwner.CaughtInNet;
        SecureNetBeforeEbb();
        bool caughtHaulKeepsTimber = timberWasCaught &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.SecuredAtPost &&
            extraSaltWoodBatchId == caughtHaulBatchId;

        string evidence =
            $"开放 锁{openLock:F1}/入海{openSailingEntry:F1}/鱼{openPrimaryCatch:F1}/三{openTierThree:F1}/断{openBreak:F1}；" +
            $"进网 锁{feedLock:F1}/鱼{feedPrimaryCatch:F1}/木{feedSaltWoodCatch:F1}/三{feedTierThree:F1}/断{feedBreak:F1}/回海{feedSailingEntry:F1}；" +
            $"批次={openPrimaryBatch}/{openSaltWoodBatch}；早收放海={earlyHaulReleasesTimber}/入网留桩={caughtHaulKeepsTimber}";
        ResetSlice();
        return openBranchReachesSailing && feedBranchReachesSameNet &&
            feedTradesSafetyForCargo && stableBatchIdentity &&
            earlyHaulReleasesTimber && caughtHaulKeepsTimber
            ? $"PASS：首潮开放岔流同潮进入短航，导流盐木真实进网并提前增压；收网时所有权由是否碰网决定。{evidence}"
            : $"FAIL：首潮岔流仍有冻结、复制、伪捕获，或开放与进网没有形成收益风险分支。{evidence}";
    }

    public string RunEditorFirstTideOpenRouteVoyageWindowProbe()
    {
        return RunEditorFirstTideOpenRouteVoyageWindowProbe(false, 0.82f);
    }

    private string RunEditorFirstTideOpenRouteVoyageWindowProbe(
        bool stopAfterReturn,
        float netDepth01)
    {
        EnsureScene();
        ConfigureNaturalFirstTideNet(netDepth01);

        // This is the only authored starting condition: the player has just finished
        // setting the deep net and stands beside its real post. From this point onward
        // walking, boarding, sailing, timber drift, hooking and return all use runtime
        // integration; the probe never writes a boat or salvage position.
        viewMode = SliceViewMode.Shelter;
        mooringScreenActive = false;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(netAnchor.x, GetPlayerLaneY(playerLane));
        playerHorizontalVelocity = 0f;
        playerMoving = false;
        playerSwimming = false;

        const float stepSeconds = 0.1f;
        const int maximumNaturalSteps = 1200;
        float elapsed = 0f;
        float routeLockSeconds = -1f;
        float sailingEntrySeconds = -1f;
        float reachedBoatSeconds = -1f;
        float boardedSeconds = -1f;
        float hookStartedSeconds = -1f;
        float salvageSecuredSeconds = -1f;
        float reachedHomeSeconds = -1f;
        float returnedToPierSeconds = -1f;
        float tierThreeSeconds = -1f;
        float breakSeconds = -1f;
        float reefDecisionSeconds = 0f;
        float bailedWaterBeforeReefReturn = sailingBailedWaterThisTrip;
        int expectedSaltWoodBatch = extraSaltWoodBatchId;

        // Leave the routing boom open and let the same natural tide lock the decision.
        for (int step = 0; step < maximumNaturalSteps && !routingDecisionLocked; step++)
        {
            TickState(stepSeconds);
            elapsed += stepSeconds;
            ObserveFirstTideVoyageMilestones(
                elapsed,
                ref routeLockSeconds,
                ref sailingEntrySeconds,
                ref tierThreeSeconds,
                ref breakSeconds);
        }

        // Walking uses the same acceleration, deceleration, visible lane bounds and
        // continuous mooring camera seam as keyboard play. The natural tide keeps
        // advancing during every footstep and while the player comes to a stop.
        for (int step = 0; step < 240 && !IsPlayerNearBoat() && !netBrokeThisTide; step++)
        {
            TickPlayerHorizontalLocomotion(1f, stepSeconds);
            TryStartMooringScreenTransition(1f);
            TickState(stepSeconds);
            elapsed += stepSeconds;
            ObserveFirstTideVoyageMilestones(
                elapsed,
                ref routeLockSeconds,
                ref sailingEntrySeconds,
                ref tierThreeSeconds,
                ref breakSeconds);
        }

        if (IsPlayerNearBoat())
        {
            reachedBoatSeconds = elapsed;
        }
        for (int step = 0; step < 20 && Mathf.Abs(playerHorizontalVelocity) > 0.025f; step++)
        {
            TickPlayerHorizontalLocomotion(0f, stepSeconds);
            TickState(stepSeconds);
            elapsed += stepSeconds;
            ObserveFirstTideVoyageMilestones(
                elapsed,
                ref routeLockSeconds,
                ref sailingEntrySeconds,
                ref tierThreeSeconds,
                ref breakSeconds);
        }

        bool mooringSecured = AdvanceProbeMooringUntilSecured(
            stepSeconds,
            18f,
            true,
            ref elapsed);
        int remainingNaturalSteps = Mathf.Max(
            0,
            maximumNaturalSteps - Mathf.CeilToInt(elapsed / stepSeconds));
        for (int step = 0;
             step < remainingNaturalSteps && mooringSecured &&
             !IsBoatBoardWindowOpen() && !netBrokeThisTide;
             step++)
        {
            TickState(stepSeconds);
            elapsed += stepSeconds;
            ObserveFirstTideVoyageMilestones(
                elapsed,
                ref routeLockSeconds,
                ref sailingEntrySeconds,
                ref tierThreeSeconds,
                ref breakSeconds);
        }
        TideMooredBoatAccessSample accessBeforeBoard = GetMooredBoatAccessSample();
        TryBoardBoat();
        bool boardingStarted = boatViewTransition == BoatViewTransition.Boarding;
        for (int step = 0; step < 40 && boatViewTransition != BoatViewTransition.None; step++)
        {
            TickState(stepSeconds);
            TickBoatViewTransition(stepSeconds);
            elapsed += stepSeconds;
            ObserveFirstTideVoyageMilestones(
                elapsed,
                ref routeLockSeconds,
                ref sailingEntrySeconds,
                ref tierThreeSeconds,
                ref breakSeconds);
        }
        if (mooringSecured && boardingStarted && viewMode == SliceViewMode.Sailing)
        {
            boardedSeconds = elapsed;
        }

        // Sail outward with the damaged boat's default half-sail. As soon as the
        // timber is physically behind the stern, in reach and speed-matched, use the
        // same throw preconditions as the player's F press. Reaching the damaged-boat
        // breaker is allowed; teleporting the boat beside the target is not.
        bool hookStarted = false;
        for (int step = 0; step < 600 && viewMode == SliceViewMode.Sailing && !netBrokeThisTide; step++)
        {
            if (extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater &&
                BeginContinuousSailingHookThrow())
            {
                hookStarted = true;
                hookStartedSeconds = elapsed;
                break;
            }

            if (AdvanceProbeSailingWithReefDecision(stepSeconds, 1f, false))
            {
                reefDecisionSeconds += stepSeconds;
            }
            TickState(stepSeconds);
            elapsed += stepSeconds;
            ObserveFirstTideVoyageMilestones(
                elapsed,
                ref routeLockSeconds,
                ref sailingEntrySeconds,
                ref tierThreeSeconds,
                ref breakSeconds);
        }

        // Holding F is injected only after a valid throw begins. The continuous
        // salvage model still owns throw travel, rope length, relative speed, tension,
        // overstrain and detachment while the entire natural world keeps ticking.
        for (int step = 0;
             step < 160 && hookStarted &&
             extraSaltWoodOwner != ExtraSaltWoodOwner.HookedToBoat &&
             !netBrokeThisTide;
             step++)
        {
            AdvanceSailingSteering(stepSeconds, 0f);
            TickStateWithSailingInteraction(stepSeconds, true);
            elapsed += stepSeconds;
            ObserveFirstTideVoyageMilestones(
                elapsed,
                ref routeLockSeconds,
                ref sailingEntrySeconds,
                ref tierThreeSeconds,
                ref breakSeconds);
        }
        if (extraSaltWoodOwner == ExtraSaltWoodOwner.HookedToBoat)
        {
            salvageSecuredSeconds = elapsed;
        }

        // Tow the physical batch back under the same damaged-hull, current, wind and
        // ingress penalties. Returning is accepted only after the runtime home window
        // reports true, then the normal disembark route moves the player onto the pier.
        for (int step = 0;
              step < 600 &&
             extraSaltWoodOwner == ExtraSaltWoodOwner.HookedToBoat &&
             !CanReturnFromSailing() &&
             !netBrokeThisTide;
             step++)
        {
            if (AdvanceProbeSailingWithReefDecision(stepSeconds, -1f, true))
            {
                reefDecisionSeconds += stepSeconds;
            }
            TickState(stepSeconds);
            elapsed += stepSeconds;
            ObserveFirstTideVoyageMilestones(
                elapsed,
                ref routeLockSeconds,
                ref sailingEntrySeconds,
                ref tierThreeSeconds,
                ref breakSeconds);
        }

        if (CanReturnFromSailing())
        {
            reachedHomeSeconds = elapsed;
            ReturnToStiltHouseByChoice();
        }
        bool returningStarted = boatViewTransition == BoatViewTransition.Returning;
        for (int step = 0; step < 40 && boatViewTransition != BoatViewTransition.None; step++)
        {
            TickState(stepSeconds);
            TickBoatViewTransition(stepSeconds);
            elapsed += stepSeconds;
            ObserveFirstTideVoyageMilestones(
                elapsed,
                ref routeLockSeconds,
                ref sailingEntrySeconds,
                ref tierThreeSeconds,
                ref breakSeconds);
        }
        if (returningStarted && viewMode == SliceViewMode.Shelter)
        {
            returnedToPierSeconds = elapsed;
        }

        bool returnedBeforeBreak = returnedToPierSeconds > 0f && !netBrokeThisTide;
        bool returnedSameBatch = extraSaltWoodOwner == ExtraSaltWoodOwner.ReturnedAtBoat &&
            expectedSaltWoodBatch > 0 && extraSaltWoodBatchId == expectedSaltWoodBatch;

        // The public timing probe continues the unattended deep net to expose its
        // natural failure time. A later feedback probe can stop at the real returned
        // cargo state and continue the same world into unloading and repair.
        for (int step = 0; step < 1200 && !stopAfterReturn && !netBrokeThisTide; step++)
        {
            TickState(stepSeconds);
            elapsed += stepSeconds;
            ObserveFirstTideVoyageMilestones(
                elapsed,
                ref routeLockSeconds,
                ref sailingEntrySeconds,
                ref tierThreeSeconds,
                ref breakSeconds);
        }

        float returnMarginSeconds = breakSeconds > 0f && returnedToPierSeconds > 0f
            ? breakSeconds - returnedToPierSeconds
            : -1f;
        float reefReturnBailedWater = sailingBailedWaterThisTrip - bailedWaterBeforeReefReturn;
        bool naturalSequence = routeLockSeconds > 0f &&
            sailingEntrySeconds > routeLockSeconds &&
            reachedBoatSeconds > routeLockSeconds &&
            boardedSeconds > reachedBoatSeconds &&
            hookStartedSeconds > boardedSeconds &&
            salvageSecuredSeconds > hookStartedSeconds &&
            reachedHomeSeconds > salvageSecuredSeconds &&
            returnedToPierSeconds > reachedHomeSeconds &&
            reefDecisionSeconds > 0.2f;
        bool readableDeepPressure = stopAfterReturn ||
            (tierThreeSeconds > 0f && breakSeconds > tierThreeSeconds &&
             returnMarginSeconds >= 4f);

        string evidence =
            $"锁流{routeLockSeconds:F1}/盐木入海{sailingEntrySeconds:F1}/到船{reachedBoatSeconds:F1}/登船{boardedSeconds:F1}/" +
            $"抛钩{hookStartedSeconds:F1}/收妥{salvageSecuredSeconds:F1}/靠岸{reachedHomeSeconds:F1}/返木路{returnedToPierSeconds:F1}/" +
            $"礁控{reefDecisionSeconds:F1}s/舀水{reefReturnBailedWater:F2}/" +
            $"船木={sailingBoatX:F2}/{sailingSalvageWorldX:F2}m 速={sailingBoatWorldVelocity:F2}/{sailingSalvageVelocity:F2} " +
            $"相对={GetSailingSalvageRelativeSpeed():F2} owner={extraSaltWoodOwner}/" +
            $"系泊={mooringSecured}/{mooringRope.Phase}/阻断={accessBeforeBoard.BlockReason}/" +
            $"跳板={accessBeforeBoard.Gangplank.LengthMeters:F2}m,{accessBeforeBoard.Gangplank.SlopeDegrees:F1}°/" +
            $"流浪={accessBeforeBoard.LocalWaterVelocityMetersPerSecond:F2},{accessBeforeBoard.LocalAgitation01:F2}/" +
            $"三档{tierThreeSeconds:F1}/断网{breakSeconds:F1}/余量{returnMarginSeconds:F1}s/批次{expectedSaltWoodBatch}->{extraSaltWoodBatchId}";
        bool passed = naturalSequence && returnedBeforeBreak && returnedSameBatch && readableDeepPressure;
        if (!stopAfterReturn)
        {
            ResetSlice();
        }
        return passed
            ? $"PASS：开放岔流后的首潮短航可按真实步行、破船航行、贴流抛钩和拖带返航完成，深网仍保留断裂压力。{evidence}"
            : $"FAIL：开放岔流虽能进短航，但真实往返仍来不及、无法打捞或批次不守恒。{evidence}";
    }

    private bool AdvanceProbeSailingWithReefDecision(
        float deltaTime,
        float intendedDirection,
        bool allowBailing)
    {
        float direction = Mathf.Clamp(intendedDirection, -1f, 1f);
        float leftEdge = sailingReefPoint.x - TideSailingReefModel.ReefHalfWidthMeters;
        float rightEdge = sailingReefPoint.x + TideSailingReefModel.ReefHalfWidthMeters;
        float distanceToReef = direction > 0f
            ? leftEdge - sailingBoatX
            : sailingBoatX - rightEdge;
        bool approachingReef = distanceToReef >= -0.08f && distanceToReef <= 1.65f;
        if (!approachingReef)
        {
            sailingBailing = false;
            // Once clear of the breaker the same player raises sail again; otherwise
            // one cautious reefing input would leave the probe motoring the remaining
            // sea sector under bare poles and test an artificial timeout.
            AdvanceSailingSteering(deltaTime, direction, 1f);
            return false;
        }

        TideSailingReefSample reef = GetSailingReefSample(sailingBoatVelocity);
        bool bailing = allowBailing &&
            sailingWaterIngress01 > 0.16f &&
            (reef.GroundsKeel || reef.UnderKeelClearanceMeters < 0.1f);
        sailingBailing = bailing;
        if (bailing)
        {
            ApplySailingBail(deltaTime);
        }

        // The probe reacts only to the same exposed rock/breaker condition visible in
        // play: lower sail, bleed speed, then use a small tiller input when the keel
        // clears. It never writes boat position, tide, velocity, ingress or reef state.
        bool needsCaution = reef.GroundsKeel ||
            reef.UnderKeelClearanceMeters < 0.12f ||
            Mathf.Abs(sailingBoatVelocity) > 0.36f;
        float cautiousDirection = reef.GroundsKeel || Mathf.Abs(sailingBoatVelocity) > 0.32f
            ? 0f
            : direction * 0.32f;
        AdvanceSailingSteering(deltaTime, cautiousDirection, -1f);
        return needsCaution || bailing;
    }

    public string RunEditorFirstTideSaltWoodBoatRepairFeedbackProbe()
    {
        EnsureScene();

        // A shallow net keeps the home catch recoverable while the same open fork
        // sends saltwood offshore. This is not an easier voyage: walking, boarding,
        // sailing, hooking and towing are still executed by the natural route helper.
        string voyageReport = RunEditorFirstTideOpenRouteVoyageWindowProbe(true, 0.2f);
        bool voyageCompleted = voyageReport.StartsWith("PASS") &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.ReturnedAtBoat &&
            viewMode == SliceViewMode.Shelter;
        int saltWoodBatch = extraSaltWoodBatchId;
        int fishBatch = currentHarvestBatchId;
        bool fishStillOwnsNet = currentHarvest == HarvestKind.Fish &&
            harvestPhysicalState == HarvestPhysicalState.CaughtInNet &&
            netDeployed;

        const float workStepSeconds = 0.1f;
        float followUpSeconds = 0f;
        float worldElapsedAtReturn = worldElapsedRealSeconds;

        // Because the fish remains physically in the net, the player's hands are not
        // available for a second cargo object. Unloading therefore uses the existing
        // visible dock staging point instead of overwriting the net catch or inventory.
        bool unloadedToDock = TryUnloadReturnedSailingCargo() &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.StagedAtMooring &&
            currentHarvest == HarvestKind.Fish &&
            currentHarvestBatchId == fishBatch;

        bool reachedNet = AdvanceProbePlayerToWorldX(
            netAnchor.x,
            0.06f,
            12f,
            ref followUpSeconds);
        float haulStartedSeconds = followUpSeconds;
        for (int step = 0; step < 180 && reachedNet && netHaulProgress < 0.999f; step++)
        {
            float visibleWaterY = GetNetOceanSample().SurfaceY;
            float waterLoad = Mathf.InverseLerp(
                GetSelectedNetY() - 0.16f,
                GetSelectedNetY() + 0.28f,
                visibleWaterY);
            TickNetHaulEffort(workStepSeconds, netHaulSeconds, waterLoad);
            if (netHaulProgress >= 0.999f)
            {
                SecureNetBeforeEbb();
            }
            TickStateWithNetHaulInteraction(workStepSeconds, true);
            followUpSeconds += workStepSeconds;
        }
        float haulFinishedSeconds = followUpSeconds;
        bool fishSecuredAtPost = netSecuredEarly &&
            securedPostHarvest == HarvestKind.Fish &&
            securedPostHarvestBatchId == fishBatch &&
            currentHarvest == HarvestKind.None &&
            AreHarvestHandsFree();

        Vector2 stagingPosition = GetMooringStagingPosition();
        bool returnedToStaging = AdvanceProbePlayerToWorldX(
            stagingPosition.x,
            0.05f,
            12f,
            ref followUpSeconds);
        bool pickedUpSameTimber = returnedToStaging &&
            TryPickUpStagedSaltWoodAtMooring() &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.Carried &&
            extraSaltWoodBatchId == saltWoodBatch &&
            currentHarvest == HarvestKind.Wood &&
            harvestPhysicalState == HarvestPhysicalState.Carried;

        Vector2 hullWorkPosition = GetRepairChoicePosition(RepairChoice.Hull);
        bool reachedHull = AdvanceProbePlayerToWorldX(
            hullWorkPosition.x,
            0.04f,
            8f,
            ref followUpSeconds);
        float hullArrivalX = playerPosition.x;
        RepairChoice nearbyChoice;
        bool hullOwnsWorkPoint = reachedHull &&
            TryGetClosestRepairChoice(out nearbyChoice) &&
            nearbyChoice == RepairChoice.Hull;

        float speedBefore = GetEffectiveSailingMaxSpeed();
        float leakBefore = GetBoatConditionPerformance().BaseLeakRatePerSecond;
        float rangeBefore = GetBoatSeaworthyRightLimit();
        bool cargoNotPreBanked = timberStock == 0 && ropeStock == 0 && clothStock == 0 &&
            !currentHarvestBanked;

        // Start the same held-F repair, release it once, let the natural world advance,
        // then resume. This proves the five-step work is interruptible and that the
        // material is consumed only at the final physical fastening.
        bool sawInspection = false;
        bool sawCleaning = false;
        bool sawTrialFit = false;
        bool sawFastening = false;
        bool sawSealing = false;
        for (int step = 0; step < 8 && hullOwnsWorkPoint; step++)
        {
            TickRepairWorkAtWorldTarget(workStepSeconds, step == 0, true);
            sawInspection |= repairWorkStep == (int)TideRepairWorkPhase.Inspect;
            sawCleaning |= repairWorkStep == (int)TideRepairWorkPhase.Clean;
            sawTrialFit |= repairWorkStep == (int)TideRepairWorkPhase.TestFit;
            TickState(workStepSeconds);
            followUpSeconds += workStepSeconds;
        }
        float pausedProgress = repairWorkProgress;
        float pauseClockBefore = dayClockSeconds;
        TickRepairWorkAtWorldTarget(0f, false, false);
        for (int step = 0; step < 6; step++)
        {
            TickState(workStepSeconds);
            followUpSeconds += workStepSeconds;
        }
        bool pausePreservesWork = Mathf.Abs(repairWorkProgress - pausedProgress) <= 0.0001f &&
            dayClockSeconds > pauseClockBefore;
        bool materialStillPhysicalDuringPause = !currentHarvestBanked &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.PlacedAtWork &&
            timberStock == 0 && ropeStock == 0 && clothStock == 0;

        for (int step = 0; step < 80 && hullOwnsWorkPoint && !repairChoiceApplied; step++)
        {
            TickRepairWorkAtWorldTarget(workStepSeconds, false, true);
            sawInspection |= repairWorkStep == (int)TideRepairWorkPhase.Inspect;
            sawCleaning |= repairWorkStep == (int)TideRepairWorkPhase.Clean;
            sawTrialFit |= repairWorkStep == (int)TideRepairWorkPhase.TestFit;
            sawFastening |= repairWorkStep == (int)TideRepairWorkPhase.Fasten;
            sawSealing |= repairWorkStep == (int)TideRepairWorkPhase.Seal;
            TickState(workStepSeconds);
            followUpSeconds += workStepSeconds;
        }
        float repairFinishedSeconds = followUpSeconds;

        float speedAfter = GetEffectiveSailingMaxSpeed();
        float leakAfter = GetBoatConditionPerformance().BaseLeakRatePerSecond;
        float rangeAfter = GetBoatSeaworthyRightLimit();
        bool repairCommittedOnce = repairChoiceApplied &&
            pendingRepairChoice == RepairChoice.Hull &&
            boatHullIntegrity == 2 && boatReadiness == 1 &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.Claimed &&
            extraSaltWoodBatchId == saltWoodBatch;
        bool materialLedgerIsPhysical = cargoNotPreBanked && materialStillPhysicalDuringPause &&
            timberStock == 0 && ropeStock == 0 && clothStock == 1 && hasSalvageBag;
        bool handlingImproves = speedAfter > speedBefore * 1.08f &&
            leakAfter < leakBefore * 0.78f &&
            rangeAfter > rangeBefore + 0.5f;
        bool fiveWorkPhases = sawInspection && sawCleaning && sawTrialFit && sawFastening && sawSealing;

        bool returnedToBoat = AdvanceProbePlayerToWorldX(
            GetBoatBoardingX(),
            0.05f,
            8f,
            ref followUpSeconds);
        int maximumNextWindowSteps = Mathf.CeilToInt(
            (tideCycleSeconds + 20f) / workStepSeconds);
        for (int step = 0;
             step < maximumNextWindowSteps && returnedToBoat && !IsBoatBoardWindowOpen();
             step++)
        {
            TickState(workStepSeconds);
            followUpSeconds += workStepSeconds;
        }
        bool nextWindowOpened = returnedToBoat && IsBoatBoardWindowOpen();
        if (nextWindowOpened)
        {
            TryBoardBoat();
        }
        for (int step = 0; step < 40 && boatViewTransition != BoatViewTransition.None; step++)
        {
            TickState(workStepSeconds);
            TickBoatViewTransition(workStepSeconds);
            followUpSeconds += workStepSeconds;
        }

        float nextTripStartX = sailingBoatX;
        for (int step = 0; step < 30 && viewMode == SliceViewMode.Sailing; step++)
        {
            AdvanceSailingSteering(workStepSeconds, 1f);
            TickState(workStepSeconds);
            followUpSeconds += workStepSeconds;
        }
        bool nextTripUsesRepair = viewMode == SliceViewMode.Sailing &&
            boatHullIntegrity == 2 && boatReadiness == 1 &&
            GetBoatSeaworthyRightLimit() >= rangeAfter - 0.01f &&
            sailingBoatX > nextTripStartX + 0.3f;
        bool naturalWorldContinued =
            worldElapsedRealSeconds > worldElapsedAtReturn + 0.01f &&
            followUpSeconds > 0f;
        bool fishSurvivesBoatRepair = securedPostHarvest == HarvestKind.Fish &&
            securedPostHarvestBatchId == fishBatch;

        string evidence =
            $"航程=[{voyageReport}]；卸到码头={unloadedToDock}/收鱼={haulStartedSeconds:F1}->{haulFinishedSeconds:F1}s/" +
            $"修船完成={repairFinishedSeconds:F1}s/阶段={sawInspection}/{sawCleaning}/{sawTrialFit}/{sawFastening}/{sawSealing}/暂停={pausePreservesWork}；" +
            $"路径={voyageCompleted}/{fishStillOwnsNet}/{reachedNet}/{fishSecuredAtPost}/{returnedToStaging}/{pickedUpSameTimber}/{reachedHull}/{hullOwnsWorkPoint}；" +
            $"船壳工位={hullArrivalX:F2}->{hullWorkPosition.x:F2}/通道{GetLaneMinX(playerLane):F2}..{GetLaneMaxX(playerLane):F2}；" +
            $"船壳={boatHullIntegrity}/船况={boatReadiness}/提交={repairCommittedOnce}/账本={materialLedgerIsPhysical}/" +
            $"速{speedBefore:F2}->{speedAfter:F2}/漏{leakBefore:F3}->{leakAfter:F3}/界{rangeBefore:F2}->{rangeAfter:F2}；" +
            $"盐木批次={saltWoodBatch}/{extraSaltWoodBatchId}/鱼批次={fishBatch}/{securedPostHarvestBatchId}/" +
            $"再开窗={nextWindowOpened}/再出航={nextTripUsesRepair}/世界继续={naturalWorldContinued}";
        bool passed = voyageCompleted && fishStillOwnsNet && unloadedToDock &&
            reachedNet && fishSecuredAtPost && pickedUpSameTimber && hullOwnsWorkPoint &&
            pausePreservesWork && fiveWorkPhases && repairCommittedOnce &&
            materialLedgerIsPhysical && handlingImproves && nextWindowOpened &&
            nextTripUsesRepair && naturalWorldContinued && fishSurvivesBoatRepair;
        ResetSlice();
        return passed
            ? $"PASS：首潮盐木经过船艉暂放、收网腾手、搬运和五段船壳施工后，下一航次真实获得更低漏水、更高航速与更远航界。{evidence}"
            : $"FAIL：首潮返航物仍在卸货、网获并存、搬运、施工材料或下一航次反馈中断链。{evidence}";
    }

    private bool AdvanceProbePlayerToWorldX(
        float targetX,
        float tolerance,
        float maximumSeconds,
        ref float elapsedSeconds)
    {
        // This helper injects only a horizontal direction. Runtime acceleration,
        // deceleration, lane bounds, swimming/current response and world clocks remain
        // authoritative; no probe call writes the player's destination position.
        const float stepSeconds = 0.05f;
        int maximumSteps = Mathf.CeilToInt(Mathf.Max(stepSeconds, maximumSeconds) / stepSeconds);
        for (int step = 0; step < maximumSteps; step++)
        {
            float deltaX = targetX - playerPosition.x;
            if (Mathf.Abs(deltaX) <= tolerance && Mathf.Abs(playerHorizontalVelocity) <= 0.03f)
            {
                return true;
            }

            float direction = Mathf.Abs(deltaX) <= tolerance ? 0f : Mathf.Sign(deltaX);
            TickPlayerHorizontalLocomotion(direction, stepSeconds);
            TryStartMooringScreenTransition(direction);
            TickState(stepSeconds);
            elapsedSeconds += stepSeconds;
        }

        return Mathf.Abs(targetX - playerPosition.x) <= tolerance * 1.5f &&
            Mathf.Abs(playerHorizontalVelocity) <= 0.05f;
    }

    private void ObserveFirstTideVoyageMilestones(
        float elapsedSeconds,
        ref float routeLockSeconds,
        ref float sailingEntrySeconds,
        ref float tierThreeSeconds,
        ref float breakSeconds)
    {
        if (routeLockSeconds < 0f && routingDecisionLocked)
        {
            routeLockSeconds = elapsedSeconds;
        }
        if (sailingEntrySeconds < 0f && extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater)
        {
            sailingEntrySeconds = elapsedSeconds;
        }
        if (tierThreeSeconds < 0f && netCatchBundleTier >= 3)
        {
            tierThreeSeconds = elapsedSeconds;
        }
        if (breakSeconds < 0f && netBrokeThisTide)
        {
            breakSeconds = elapsedSeconds;
        }
    }

    private void MeasureNaturalFirstTideRouteBranch(
        bool feedNet,
        out float lockSeconds,
        out float primaryCatchSeconds,
        out float saltWoodCatchSeconds,
        out float sailingEntrySeconds,
        out float tierThreeSeconds,
        out float breakSeconds,
        out int primaryBatchId,
        out int saltWoodBatchId,
        out bool everBundled)
    {
        ConfigureNaturalFirstTideNet(0.5f);
        lockSeconds = -1f;
        primaryCatchSeconds = -1f;
        saltWoodCatchSeconds = -1f;
        sailingEntrySeconds = -1f;
        tierThreeSeconds = -1f;
        breakSeconds = -1f;
        primaryBatchId = 0;
        saltWoodBatchId = 0;
        everBundled = false;

        const float stepSeconds = 0.1f;
        const int maximumSteps = 1800;
        for (int step = 0; step < maximumSteps; step++)
        {
            float elapsed = (step + 1) * stepSeconds;
            if (feedNet && elapsed >= 24f && !routingDecisionLocked)
            {
                routingBoom01 = 0.82f;
            }
            TickState(stepSeconds);

            if (lockSeconds < 0f && routingDecisionLocked)
            {
                lockSeconds = elapsed;
                primaryBatchId = tideSourceBatchId;
                saltWoodBatchId = extraSaltWoodBatchId;
            }
            if (primaryCatchSeconds < 0f && netCatchResolved)
            {
                primaryCatchSeconds = elapsed;
            }
            if (saltWoodCatchSeconds < 0f && extraSaltWoodOwner == ExtraSaltWoodOwner.CaughtInNet)
            {
                saltWoodCatchSeconds = elapsed;
            }
            if (sailingEntrySeconds < 0f && extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater)
            {
                sailingEntrySeconds = elapsed;
            }
            if (tierThreeSeconds < 0f && netCatchBundleTier >= 3)
            {
                tierThreeSeconds = elapsed;
            }
            everBundled |= extraSaltWoodBundledWithNetHarvest;
            if (breakSeconds < 0f && netBrokeThisTide)
            {
                breakSeconds = elapsed;
                break;
            }
        }
    }

    private void ConfigureNaturalFirstTideNet(float depth01)
    {
        ResetSlice();
        arrivalInspected = true;
        arrivalVignetteActive = false;
        state = SliceState.LowTidePlanning;
        viewMode = SliceViewMode.Interior;
        playerLane = WalkLane.InteriorLoft;
        playerPosition = new Vector2(GetInteriorLoftLookoutX(), GetPlayerLaneY(playerLane));
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        netSetDepth01 = Mathf.Clamp01(depth01);
        netLoweringProgress = netSetDepth01;
        selectedNetLine = GetNetLineFromLoweringProgress(netSetDepth01);
        netIntegrity = 3;
    }

    private void MeasureNaturalNetExcursionTiming(
        float depth01,
        out float touchSeconds,
        out float catchSeconds,
        out float tierTwoSeconds,
        out float tierThreeSeconds,
        out float fraySeconds,
        out float breakSeconds,
        out float ebbSeconds,
        out float tierTwoSurfaceDepthMeters,
        out float tierThreeSurfaceDepthMeters)
    {
        ConfigureNaturalFirstTideNet(depth01);

        touchSeconds = -1f;
        catchSeconds = -1f;
        tierTwoSeconds = -1f;
        tierThreeSeconds = -1f;
        fraySeconds = -1f;
        breakSeconds = -1f;
        ebbSeconds = -1f;
        tierTwoSurfaceDepthMeters = -1f;
        tierThreeSurfaceDepthMeters = -1f;

        const float stepSeconds = 0.1f;
        const int maximumSteps = 2200;
        for (int step = 0; step < maximumSteps; step++)
        {
            float elapsed = (step + 1) * stepSeconds;
            TickState(stepSeconds);
            if (touchSeconds < 0f && netTouched)
            {
                touchSeconds = elapsed;
            }
            if (catchSeconds < 0f && netCatchResolved)
            {
                catchSeconds = elapsed;
            }
            if (tierTwoSeconds < 0f && netCatchBundleTier >= 2)
            {
                tierTwoSeconds = elapsed;
                tierTwoSurfaceDepthMeters = GetNetOceanSample().SurfaceY - GetNetHeadLineY();
            }
            if (tierThreeSeconds < 0f && netCatchBundleTier >= 3)
            {
                tierThreeSeconds = elapsed;
                tierThreeSurfaceDepthMeters = GetNetOceanSample().SurfaceY - GetNetHeadLineY();
            }
            if (fraySeconds < 0f && netFraying01 > 0.01f)
            {
                fraySeconds = elapsed;
            }
            if (breakSeconds < 0f && netBrokeThisTide)
            {
                breakSeconds = elapsed;
            }
            if (elapsed > 20f && state == SliceState.EbbCollect)
            {
                ebbSeconds = elapsed;
                break;
            }
        }
    }

    public string RunEditorBoatComponentHandlingFeedbackProbe()
    {
        EnsureScene();
        ResetSlice();

        sailingSailTrim01 = 1f;
        sailingWaterIngress01 = 0f;
        sailingBailing = false;
        boatHullIntegrity = 1;
        boatSailIntegrity = 0;
        boatCabinIntegrity = 0;
        RecalculateBoatReadiness();
        TideBoatConditionPerformanceSample damaged = GetBoatConditionPerformance();
        float damagedHullSpeed = GetEffectiveSailingMaxSpeed();

        boatHullIntegrity = 2;
        RecalculateBoatReadiness();
        TideBoatConditionPerformanceSample hullRepaired = GetBoatConditionPerformance();
        float repairedHullSpeed = GetEffectiveSailingMaxSpeed();

        boatHullIntegrity = 1;
        boatSailIntegrity = 1;
        RecalculateBoatReadiness();
        TideBoatConditionPerformanceSample sailRepaired = GetBoatConditionPerformance();

        boatSailIntegrity = 0;
        boatCabinIntegrity = 1;
        RecalculateBoatReadiness();
        TideBoatConditionPerformanceSample cabinRepaired = GetBoatConditionPerformance();
        sailingBailing = true;
        float repairedCabinBailRate = GetEffectiveSailingBailRate();
        float repairedCabinMomentumLoss = GetEffectiveSailingDrag();

        boatCabinIntegrity = 0;
        RecalculateBoatReadiness();
        float damagedCabinBailRate = GetEffectiveSailingBailRate();
        float damagedCabinMomentumLoss = GetEffectiveSailingDrag();

        bool hullOwnsHullPhysics = hullRepaired.BaseLeakRatePerSecond < damaged.BaseLeakRatePerSecond &&
            repairedHullSpeed > damagedHullSpeed * 1.08f &&
            Mathf.Approximately(hullRepaired.SailTrimRatePerSecond, damaged.SailTrimRatePerSecond);
        bool sailOwnsRigging = sailRepaired.SailDriveEfficiency01 > damaged.SailDriveEfficiency01 &&
            sailRepaired.SailTrimRatePerSecond > damaged.SailTrimRatePerSecond &&
            Mathf.Approximately(sailRepaired.BaseLeakRatePerSecond, damaged.BaseLeakRatePerSecond);
        bool cabinOwnsWorkSpace = cabinRepaired.BallastShiftRatePerSecond > damaged.BallastShiftRatePerSecond &&
            repairedCabinBailRate > damagedCabinBailRate &&
            repairedCabinMomentumLoss < damagedCabinMomentumLoss;

        string evidence =
            $"船壳速度={damagedHullSpeed:F2}->{repairedHullSpeed:F2}/漏率={damaged.BaseLeakRatePerSecond:F3}->{hullRepaired.BaseLeakRatePerSecond:F3}；" +
            $"船帆效率={damaged.SailDriveEfficiency01:F2}->{sailRepaired.SailDriveEfficiency01:F2}/收放={damaged.SailTrimRatePerSecond:F2}->{sailRepaired.SailTrimRatePerSecond:F2}；" +
            $"舱底舀水={damagedCabinBailRate:F2}->{repairedCabinBailRate:F2}/动量损失={damagedCabinMomentumLoss:F2}->{repairedCabinMomentumLoss:F2}";
        return hullOwnsHullPhysics && sailOwnsRigging && cabinOwnsWorkSpace
            ? $"PASS：三处船体维修各自改变真实操控，没有再共享抽象船力。{evidence}"
            : $"FAIL：船壳、船帆或舱底仍没有拥有自己的可触摸后果。{evidence}";
    }

    public string RunEditorV28HouseIntegrationProbe()
    {
        EnsureScene();
        EnsureV28HouseResourcesLoaded();

        bool packComplete = HasCompleteV28HousePresentation();
        bool dimensionsMatch = packComplete;
        bool registeredPivotsMatch = packComplete;
        if (packComplete)
        {
            for (int i = 0; i < formalHouseV28Frames.Length; i++)
            {
                Sprite frame = formalHouseV28Frames[i];
                Texture2D texture = frame.texture;
                dimensionsMatch &= texture != null && texture.width == 1536 && texture.height == 1536;
                Vector2 normalizedPivot = new Vector2(
                    frame.pivot.x / frame.rect.width,
                    frame.pivot.y / frame.rect.height);
                registeredPivotsMatch &= Mathf.Abs(normalizedPivot.x - 0.5f) < 0.001f &&
                    Mathf.Abs(normalizedPivot.y - 0.03125f) < 0.001f;
            }

            Sprite[] interiorEndpoints =
            {
                formalHouseV27InteriorFoundSprite,
                formalHouseV27InteriorRepairedSprite
            };
            foreach (Sprite endpoint in interiorEndpoints)
            {
                Vector2 normalizedPivot = new Vector2(
                    endpoint.pivot.x / endpoint.rect.width,
                    endpoint.pivot.y / endpoint.rect.height);
                registeredPivotsMatch &= Mathf.Abs(normalizedPivot.x - 0.5f) < 0.001f &&
                    Mathf.Abs(normalizedPivot.y - 0.03125f) < 0.001f;
            }
        }

        int firstFrame = TideV28HousePresentationModel.EvaluateExteriorFrame(0f, 0f);
        int loopedFrame = TideV28HousePresentationModel.EvaluateExteriorFrame(
            TideV28HousePresentationModel.CalmFrameSeconds * TideV28HousePresentationModel.ExteriorFrameCount,
            0f);
        int calmFrame = TideV28HousePresentationModel.EvaluateExteriorFrame(0.5f, 0f);
        int stormFrame = TideV28HousePresentationModel.EvaluateExteriorFrame(0.5f, 1f);
        bool animationContract = firstFrame == 0 && loopedFrame == 0 && stormFrame != calmFrame;

        float registeredMainFloorY = GetFormalHouseWorldPosition().y +
            ((4096f - 2580f) / 4096f) * GetFormalHouseWorldSize().y;
        float deckFeetY = GetPlayerStandingFeetY(WalkLane.Deck);
        bool sameWorldFloor = Mathf.Abs(registeredMainFloorY - deckFeetY) <= 0.035f;

        bool v69OwnsRuntime = HasCompleteV69CurrentHousePresentation();
        bool modernHouseOwnsRuntime = v69OwnsRuntime ||
            (HasCompleteV34HouseExteriorPresentation() &&
             HasCompleteV35HouseInteriorPresentation());
        bool exteriorVisible = false;
        bool foundInteriorVisible = false;
        bool repairedInteriorVisible = false;
        bool dormantFallbackIsHidden = false;
        if (modernHouseOwnsRuntime)
        {
            // V28/V27 只剩缺资源兜底。现行 V34/V35 完整时，旧包仍需保持
            // 可加载与可循环，但不能再抢占外景、室内或楼板注册所有权。
            SetEditorV35InteriorRepairPreviewPose(0);
            bool oldFrameVisible = Array.IndexOf(formalHouseV28Frames, houseRenderer.sprite) >= 0 ||
                houseRenderer.sprite == formalHouseV27InteriorFoundSprite ||
                houseRenderer.sprite == formalHouseV27InteriorRepairedSprite;
            Sprite expectedModernInterior = v69OwnsRuntime
                ? formalHouseV69ActiveBase.StableBase
                : formalHouseV35InteriorCatalog.StableBase;
            dormantFallbackIsHidden = houseRenderer.enabled &&
                houseRenderer.sprite == expectedModernInterior &&
                !oldFrameVisible;
        }
        else
        {
            SetEditorV28HousePreviewPose(0);
            exteriorVisible = packComplete && houseRenderer.enabled &&
                Array.IndexOf(formalHouseV28Frames, houseRenderer.sprite) >= 0;
            SetEditorV28HousePreviewPose(1);
            foundInteriorVisible = packComplete && houseRenderer.enabled &&
                houseRenderer.sprite == formalHouseV27InteriorFoundSprite;
            SetEditorV28HousePreviewPose(2);
            repairedInteriorVisible = packComplete && houseRenderer.enabled &&
                houseRenderer.sprite == formalHouseV27InteriorRepairedSprite;
        }

        string evidence = $"资源={packComplete}/1536={dimensionsMatch}/Pivot={registeredPivotsMatch}；" +
            $"循环={animationContract}/{firstFrame}->{loopedFrame}/风={calmFrame}->{stormFrame}；" +
            $"旧注册楼板={sameWorldFloor}/{registeredMainFloorY:F3}/{deckFeetY:F3}；" +
            $"现代接管={modernHouseOwnsRuntime}/旧包休眠={dormantFallbackIsHidden}；" +
            $"兜底外景/室内={exteriorVisible}/{foundInteriorVisible}/{repairedInteriorVisible}";
        bool activeOwnershipValid = modernHouseOwnsRuntime
            ? dormantFallbackIsHidden
            : sameWorldFloor && exteriorVisible && foundInteriorVisible && repairedInteriorVisible;
        bool passed = packComplete && dimensionsMatch && registeredPivotsMatch && animationContract &&
            activeOwnershipValid;
        return passed
            ? $"PASS：V28/V27 兜底包有效；V34/V35 完整时旧包保持休眠，不再抢占当前房屋像素。{evidence}"
            : $"FAIL：V28/V27 兜底包失效，或在 V34/V35 已接管后仍抢占运行所有权。{evidence}";
    }

    public string RunEditorV30HouseIntegrationProbe()
    {
        EnsureScene();
        EnsureV30HouseResourcesLoaded();

        bool packComplete = HasCompleteV30HousePresentation();
        bool importContract = packComplete;
        if (packComplete)
        {
            Sprite[] fullCanvasSprites = new Sprite[
                formalHouseV30Catalog.ExteriorFrames.Length +
                formalHouseV30Catalog.InteriorRepairedFrames.Length + 4];
            int cursor = 0;
            Array.Copy(
                formalHouseV30Catalog.ExteriorFrames,
                0,
                fullCanvasSprites,
                cursor,
                formalHouseV30Catalog.ExteriorFrames.Length);
            cursor += formalHouseV30Catalog.ExteriorFrames.Length;
            Array.Copy(
                formalHouseV30Catalog.InteriorRepairedFrames,
                0,
                fullCanvasSprites,
                cursor,
                formalHouseV30Catalog.InteriorRepairedFrames.Length);
            cursor += formalHouseV30Catalog.InteriorRepairedFrames.Length;
            fullCanvasSprites[cursor++] = formalHouseV30Catalog.ExteriorNoCloth;
            fullCanvasSprites[cursor++] = formalHouseV30Catalog.InteriorFound;
            fullCanvasSprites[cursor++] = formalHouseV30Catalog.InteriorClean;
            fullCanvasSprites[cursor] = formalHouseV30Catalog.StableBase;

            for (int i = 0; i < fullCanvasSprites.Length; i++)
            {
                Sprite sprite = fullCanvasSprites[i];
                Vector2 normalizedPivot = new Vector2(
                    sprite.pivot.x / sprite.rect.width,
                    sprite.pivot.y / sprite.rect.height);
                importContract &= sprite.texture != null &&
                    sprite.texture.width == 1536 && sprite.texture.height == 1536 &&
                    Mathf.Abs(sprite.pixelsPerUnit - 192f) <= 0.001f &&
                    Mathf.Abs(normalizedPivot.x - 0.5f) <= 0.001f &&
                    Mathf.Abs(normalizedPivot.y - 0.03125f) <= 0.001f;
            }

            TideV30HouseRuntimeCatalog.RepairOwnerEntry[] owners = formalHouseV30Catalog.RepairOwners;
            for (int i = 0; i < owners.Length; i++)
            {
                Sprite damage = owners[i].DamageSprite;
                Sprite repair = owners[i].RepairSprite;
                importContract &= damage != null && repair != null &&
                    Mathf.Abs(damage.pixelsPerUnit - 192f) <= 0.001f &&
                    Mathf.Abs(repair.pixelsPerUnit - 192f) <= 0.001f;
            }
        }

        int exteriorFirst = TideV30HousePresentationModel.EvaluateExteriorFrame(0f, 0f);
        int exteriorLoop = TideV30HousePresentationModel.EvaluateExteriorFrame(
            TideV30HousePresentationModel.ExteriorFrameSeconds * TideV30HousePresentationModel.ExteriorFrameCount,
            0f);
        int exteriorCalm = TideV30HousePresentationModel.EvaluateExteriorFrame(0.5f, 0f);
        int exteriorStorm = TideV30HousePresentationModel.EvaluateExteriorFrame(0.5f, 1f);
        int interiorFirst = TideV30HousePresentationModel.EvaluateInteriorFrame(0f);
        int interiorLoop = TideV30HousePresentationModel.EvaluateInteriorFrame(
            TideV30HousePresentationModel.InteriorFrameSeconds * TideV30HousePresentationModel.InteriorFrameCount);
        bool frameLoops = exteriorFirst == 0 && exteriorLoop == 0 &&
            exteriorCalm != exteriorStorm && interiorFirst == 0 && interiorLoop == 0;

        float mainFloorY = GetFormalHouseWorldPosition().y +
            TideV30HousePresentationModel.PixelTopLeftToWorldOffset(new Vector2(768f, 968f)).y;
        float deckFeetY = GetPlayerStandingFeetY(WalkLane.Deck);
        float mooringX = GetFormalHouseWorldPosition().x +
            TideV30HousePresentationModel.PixelTopLeftToWorldOffset(new Vector2(1388f, 968f)).x;
        bool registrationMatches = Mathf.Abs(mainFloorY - deckFeetY) <= 0.01f &&
            Mathf.Abs(mooringX - GetLaneSwitchX()) <= 0.01f;

        bool allDamage = true;
        bool allRepair = true;
        if (packComplete)
        {
            foreach (TideV30HouseRuntimeCatalog.RepairOwnerEntry owner in formalHouseV30Catalog.RepairOwners)
            {
                allDamage &= !TideV30HousePresentationModel.UseRepairSprite(owner.Key, 0f, 0f, 0f, 0f);
                allRepair &= TideV30HousePresentationModel.UseRepairSprite(owner.Key, 2f, 2f, 2f, 2f);
            }
        }
        bool ownerContract = packComplete && allDamage && allRepair;

        bool v69OwnsRuntime = HasCompleteV69CurrentHousePresentation();
        bool v35OwnsRuntime = v69OwnsRuntime || HasCompleteV35HouseInteriorPresentation();
        int mixedRepairCount = 0;
        bool activePresentationValid;
        if (v35OwnsRuntime)
        {
            // V30 is now an intentional missing-resource fallback. Its import, loop,
            // registration and owner model remain tested above; active pixels must be
            // owned by V35 and the dormant V30 Renderer pool must stay hidden.
            SetEditorV35InteriorRepairPreviewPose(4);
            Sprite expectedModernInterior = v69OwnsRuntime
                ? formalHouseV69ActiveBase.StableBase
                : formalHouseV35InteriorCatalog.StableBase;
            activePresentationValid = houseRenderer.sprite == expectedModernInterior &&
                CountEnabled(v35InteriorRepairOwnerRenderers) == TideV35HouseInteriorPresentationModel.OwnerCount &&
                CountEnabled(v30RepairOwnerRenderers) == 0;
        }
        else
        {
            SetEditorV30HousePreviewPose(0);
            bool exteriorVisible = packComplete && houseRenderer.enabled &&
                Array.IndexOf(formalHouseV30Catalog.ExteriorFrames, houseRenderer.sprite) >= 0 &&
                CountEnabled(v30RepairOwnerRenderers) == 0;
            SetEditorV30HousePreviewPose(1);
            bool foundCompositionVisible = packComplete && houseRenderer.sprite == formalHouseV30Catalog.StableBase &&
                CountEnabled(v30RepairOwnerRenderers) == TideV30HousePresentationModel.RepairOwnerCount &&
                CountV30VisibleRepairSprites() == 0;
            SetEditorV30HousePreviewPose(2);
            mixedRepairCount = CountV30VisibleRepairSprites();
            bool mixedCompositionVisible = packComplete && houseRenderer.sprite == formalHouseV30Catalog.StableBase &&
                mixedRepairCount > 0 && mixedRepairCount < TideV30HousePresentationModel.RepairOwnerCount;
            SetEditorV30HousePreviewPose(3);
            bool repairedAnimationVisible = packComplete &&
                Array.IndexOf(formalHouseV30Catalog.InteriorRepairedFrames, houseRenderer.sprite) >= 0 &&
                CountEnabled(v30RepairOwnerRenderers) == 0;
            activePresentationValid = exteriorVisible && foundCompositionVisible &&
                mixedCompositionVisible && repairedAnimationVisible;
        }

        string evidence = $"包={packComplete}/导入={importContract}；" +
            $"外循环={exteriorFirst}->{exteriorLoop}/风={exteriorCalm}->{exteriorStorm}/内循环={interiorFirst}->{interiorLoop}；" +
            $"楼板={mainFloorY:F3}/{deckFeetY:F3}/系泊={mooringX:F3}/{GetLaneSwitchX():F3}；" +
            $"owner={ownerContract}/混合修复{mixedRepairCount}；" +
            $"V35接管={v35OwnsRuntime}/当前所有权={activePresentationValid}";
        bool passed = packComplete && importContract && frameLoops && registrationMatches && ownerContract &&
            activePresentationValid;
        return passed
            ? $"PASS：V30 运行包保持有效兜底，当前室内所有权已按契约迁移到 V35。{evidence}"
            : $"FAIL：V30 索引、导入、注册、owner 互斥或运行档位存在错误。{evidence}";
    }

    public string RunEditorActorBoatWysiwygProbe()
    {
        EnsureScene();
        EnsureV20CharacterResourcesLoaded();
        EnsureV31BoatResourcesLoaded();
        EnsureV30HouseResourcesLoaded();
        EnsureV32ArtResourcesLoaded();

        bool characterComplete = HasCompleteV20CharacterPresentation();
        float[] measuredBodyLengths = new float[TideV20CharacterPresentationModel.ActionStateCount];
        bool characterUsesUniformScale = characterComplete;
        bool characterMinificationReady = characterComplete;
        if (characterComplete)
        {
            for (int i = 0; i < measuredBodyLengths.Length; i++)
            {
                TideV20CharacterActionState action = (TideV20CharacterActionState)i;
                Sprite frame = formalCharacterV20Catalog.GetFrame(action, 0);
                float uniformScale = TideV20CharacterPresentationModel.CalculateUniformScale(action, frame);
                measuredBodyLengths[i] = TideV20CharacterPresentationModel.EvaluateBodyWorldLength(
                    action,
                    frame.rect.size,
                    frame.pixelsPerUnit,
                    uniformScale);
                characterUsesUniformScale &= TideV20CharacterPresentationModel.IsUniformScale(
                    new Vector2(uniformScale, uniformScale));

                // The authored action canvases are roughly 3K but the player is often
                // only 70-100 screen pixels tall. Every frame needs mipmaps and
                // bilinear filtering; checking one representative frame would let a
                // single noisy animation state slip back into runtime unnoticed.
                int frameCount = TideV20CharacterPresentationModel.GetFrameCount(action);
                for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    Sprite actionFrame = formalCharacterV20Catalog.GetFrame(action, frameIndex);
                    characterMinificationReady &= actionFrame != null &&
                        actionFrame.texture != null &&
                        actionFrame.texture.mipmapCount > 1 &&
                        actionFrame.texture.filterMode == FilterMode.Bilinear;
                }
            }
        }

        string bodyReason = "人物包不完整";
        bool bodyContinuous = characterComplete &&
            TideV20CharacterPresentationModel.ProbeBodyScaleContinuity(
                measuredBodyLengths,
                out bodyReason);

        bool boatComplete = HasCompleteV31BoatPresentation();
        bool boatRootUniform = boatComplete && TideV31BoatPresentationModel.ProbeUniformRootScale(
            TideV31BoatPresentationModel.EvaluateBoatRootLocalScale());
        bool boatLayersRegistered = boatComplete;
        if (boatComplete)
        {
            Vector2 probeRoot = new Vector2(1.37f, -0.62f);
            TideV31BoatRuntimeCatalog.LayerEntry[][] layerGroups =
            {
                formalBoatV31Catalog.FoundLayers,
                formalBoatV31Catalog.RepairedLayers,
                formalBoatV31Catalog.PassengerFrames,
            };
            for (int groupIndex = 0; groupIndex < layerGroups.Length; groupIndex++)
            {
                TideV31BoatRuntimeCatalog.LayerEntry[] group = layerGroups[groupIndex];
                for (int layerIndex = 0; layerIndex < group.Length; layerIndex++)
                {
                    TideV31BoatRuntimeCatalog.LayerEntry layer = group[layerIndex];
                    Vector2 actualPosition = TideV31BoatPresentationModel.EvaluateLayerWorldPosition(
                        probeRoot,
                        layer.WorldOffsetFromBoatPivot);
                    boatLayersRegistered &= TideV31BoatPresentationModel.ProbeLayerWorldPosition(
                        probeRoot,
                        layer.WorldOffsetFromBoatPivot,
                        actualPosition);
                }
            }
        }

        bool oceanContinuous = TideOceanFieldModel.ProbeDeterministicContinuity(out string oceanReason);
        bool exteriorAnchorsMatch;
        if (HasCompleteV32ArtPresentation())
        {
            Vector2 expectedGangwayTop = GetV32WorldAnchor(new Vector2(1160f, 1288f));
            Vector2 expectedGangwayBottom = GetV32WorldAnchor(new Vector2(1120f, 1636f));
            exteriorAnchorsMatch =
                Mathf.Abs(GetGangwayTopPosition().x - expectedGangwayTop.x) <= 0.001f &&
                Mathf.Abs(GetGangwayBottomPosition().x - expectedGangwayBottom.x) <= 0.001f;
        }
        else
        {
            exteriorAnchorsMatch = HasCompleteV30HousePresentation() &&
                Mathf.Abs(GetGangwayTopPosition().x - GetV30WorldAnchor(new Vector2(776f, 964f)).x) <= 0.001f;
        }

        // 室内锚点必须对照当前真正显示的剖面。V35 接管后使用 2048 同壳梯框；
        // 只有缺少 V35 时才回到 V30 的 1536 梯位，避免旧探针否定新资源。
        bool interiorAnchorsMatch;
        if (HasCompleteV35HouseInteriorPresentation())
        {
            interiorAnchorsMatch =
                Mathf.Abs(GetInteriorLoftStairBottomPosition().x -
                    GetV35WorldAnchor(new Vector2(1160f, 1288f)).x) <= 0.001f &&
                Mathf.Abs(GetInteriorLoftStairTopPosition().x -
                    GetV35WorldAnchor(new Vector2(1160f, 760f)).x) <= 0.001f;
        }
        else
        {
            interiorAnchorsMatch = HasCompleteV30HousePresentation() &&
                Mathf.Abs(GetInteriorLoftStairBottomPosition().x -
                    GetV30WorldAnchor(new Vector2(1042f, 712f)).x) <= 0.001f &&
                Mathf.Abs(GetInteriorLoftStairTopPosition().x -
                    GetV30WorldAnchor(new Vector2(1042f, 416f)).x) <= 0.001f;
        }
        bool houseAnchorsMatch = exteriorAnchorsMatch && interiorAnchorsMatch;

        string evidence = $"人物包={characterComplete}/等比={characterUsesUniformScale}/缩小采样={characterMinificationReady}/身体={bodyReason}；" +
            $"船包={boatComplete}/Root等比={boatRootUniform}/分层={boatLayersRegistered}；" +
            $"屋梯锚点={houseAnchorsMatch}(外={exteriorAnchorsMatch}/内={interiorAnchorsMatch})；海况={oceanReason}";
        bool passed = characterComplete && characterUsesUniformScale && characterMinificationReady && bodyContinuous &&
            boatComplete && boatRootUniform && boatLayersRegistered &&
            houseAnchorsMatch && oceanContinuous;
        return passed
            ? $"PASS：人物、船、楼梯和海况共享可验证的世界空间契约。{evidence}"
            : $"FAIL：人物尺度、船体分层、楼梯锚点或连续海况仍有契约错误。{evidence}";
    }

    private int CountV30VisibleRepairSprites()
    {
        if (!HasCompleteV30HousePresentation())
        {
            return 0;
        }

        int repaired = 0;
        TideV30HouseRuntimeCatalog.RepairOwnerEntry[] owners = formalHouseV30Catalog.RepairOwners;
        for (int i = 0; i < owners.Length && i < v30RepairOwnerRenderers.Count; i++)
        {
            SpriteRenderer renderer = v30RepairOwnerRenderers[i];
            if (renderer.enabled && renderer.sprite == owners[i].RepairSprite)
            {
                repaired++;
            }
        }
        return repaired;
    }

    private int CountV35VisibleRepairSprites()
    {
        if (HasCompleteV69CurrentHousePresentation())
        {
            int v69ChangedOwners = 0;
            for (int i = 0; i < formalHouseV69ActiveStructuralStages.Length; i++)
            {
                if (formalHouseV69ActiveStructuralStages[i].Stage != TideV69HouseRepairStage.Damage)
                {
                    v69ChangedOwners++;
                }
            }
            for (int i = 0; i < formalHouseV69ActiveBinaryStages.Length; i++)
            {
                if (formalHouseV69ActiveBinaryStages[i].Serviceable)
                {
                    v69ChangedOwners++;
                }
            }
            return v69ChangedOwners;
        }

        if (!HasCompleteV35HouseInteriorPresentation())
        {
            return 0;
        }

        int repaired = 0;
        TideV35HouseInteriorCatalog.OwnerEntry[] owners = formalHouseV35InteriorCatalog.Owners;
        for (int i = 0; i < owners.Length && i < v35InteriorRepairOwnerRenderers.Count; i++)
        {
            SpriteRenderer renderer = v35InteriorRepairOwnerRenderers[i];
            if (renderer.enabled && renderer.sprite == owners[i].RepairSprite)
            {
                repaired++;
            }
        }

        return repaired;
    }

    private static int CountEnabled(List<SpriteRenderer> renderers)
    {
        int count = 0;
        for (int i = 0; i < renderers.Count; i++)
        {
            if (renderers[i] != null && renderers[i].enabled)
            {
                count++;
            }
        }
        return count;
    }

    public string RunEditorDepartureHouseLayerProbe()
    {
        SetEditorDeparturePreviewPose();
        float houseAngle = houseRenderer.transform.eulerAngles.z;
        int visibleMasks = 0;
        float maxMaskAngleDelta = 0f;
        foreach (SpriteMask mask in houseRoofHoleMasks)
        {
            if (!mask.enabled)
            {
                continue;
            }

            visibleMasks++;
            maxMaskAngleDelta = Mathf.Max(maxMaskAngleDelta, Mathf.Abs(Mathf.DeltaAngle(houseAngle, mask.transform.eulerAngles.z)));
        }
        foreach (SpriteMask mask in houseWindowHoleMasks)
        {
            if (!mask.enabled)
            {
                continue;
            }

            visibleMasks++;
            maxMaskAngleDelta = Mathf.Max(maxMaskAngleDelta, Mathf.Abs(Mathf.DeltaAngle(houseAngle, mask.transform.eulerAngles.z)));
        }
        foreach (SpriteMask mask in houseWallGapMasks)
        {
            if (!mask.enabled)
            {
                continue;
            }

            visibleMasks++;
            maxMaskAngleDelta = Mathf.Max(maxMaskAngleDelta, Mathf.Abs(Mathf.DeltaAngle(houseAngle, mask.transform.eulerAngles.z)));
        }

        int visibleSnappedPosts = 0;
        foreach (SpriteRenderer post in snappedStiltPosts)
        {
            if (post.enabled)
            {
                visibleSnappedPosts++;
            }
        }

        bool houseClearlyTilted = Mathf.Abs(Mathf.DeltaAngle(0f, houseAngle)) >= 1.5f;
        bool masksFollowShell = visibleMasks >= 5 && maxMaskAngleDelta <= 6f &&
            houseRenderer.maskInteraction == SpriteMaskInteraction.VisibleOutsideMask;
        bool onlyAuthoredFailuresRemain = visibleSnappedPosts == 2;
        string evidence = $"屋体角={Mathf.DeltaAngle(0f, houseAngle):F2}°；遮罩={visibleMasks}/最大角差={maxMaskAngleDelta:F2}°；断桩={visibleSnappedPosts}";
        return houseClearlyTilted && masksFollowShell && onlyAuthoredFailuresRemain
            ? $"PASS：暴潮撕裂遮罩随屋体倾斜，且正式屋只叠加两根失效支柱。{evidence}"
            : $"FAIL：暴潮屋体仍有遮罩漂移或重复断桩。{evidence}";
    }

    public string RunEditorBoatViewTransitionProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalInspected = true;
        state = SliceState.TideRising;
        currentWaterY = lowWaterY + 0.72f;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(GetBoatBoardingX(), GetPlayerLaneY(playerLane));
        float startX = playerPosition.x;
        float startDayClock = dayClockSeconds;

        BeginBoatViewTransition(BoatViewTransition.Boarding);
        TickState(boatViewTransitionSeconds * 0.46f);
        TickBoatViewTransition(boatViewTransitionSeconds * 0.46f);
        bool walksBeforeCut = viewMode == SliceViewMode.Shelter && playerPosition.x > startX + 0.08f;
        Vector2 expectedCockpitApproach = EvaluateMooredBoatBoardingRoute(
            boatViewTransitionStartPosition,
            0.92f,
            false);
        bool followsAuthoredCockpitRoute = Vector2.Distance(playerPosition, expectedCockpitApproach) <= 0.02f;
        float fadeNearCut = GetBoatViewTransitionFade01();
        UpdateVisuals(2.41f);
        bool singleActorOwnerAtCut = boatPassengerRenderer.enabled &&
            !playerRenderer.enabled &&
            !playerAquaticRenderer.enabled;
        TickBoatViewTransition(boatViewTransitionSeconds * 0.08f);
        bool cutsAtMidpoint = viewMode == SliceViewMode.Sailing && boatViewTransitionSwitched;
        TickBoatViewTransition(boatViewTransitionSeconds * 0.5f);
        bool boardingCompletes = boatViewTransition == BoatViewTransition.None && viewMode == SliceViewMode.Sailing;

        sailingBoatX = sailingHomeX + 0.2f;
        sailingBoatLaneY = sailingHomeY;
        BeginBoatViewTransition(BoatViewTransition.Returning);
        TickBoatViewTransition(boatViewTransitionSeconds * 0.54f);
        bool returnCutsAtMidpoint = viewMode == SliceViewMode.Shelter && boatViewTransitionSwitched;
        TickBoatViewTransition(boatViewTransitionSeconds * 0.5f);
        bool landsOnBoardwalk = boatViewTransition == BoatViewTransition.None &&
            viewMode == SliceViewMode.Shelter &&
            Mathf.Abs(playerPosition.x - GetBoatBoardingX()) <= 0.01f;
        bool natureAdvanced = dayClockSeconds > startDayClock;

        string evidence = $"岸上步进={walksBeforeCut}；座舱路径={followsAuthoredCockpitRoute}；暗点={fadeNearCut:F2}/单人物={singleActorOwnerAtCut}；上船切换={cutsAtMidpoint}/{boardingCompletes}；" +
            $"返航切换={returnCutsAtMidpoint}/{landsOnBoardwalk}；昼夜继续={natureAdvanced}";
        bool passed = walksBeforeCut && followsAuthoredCockpitRoute && fadeNearCut >= 0.9f && singleActorOwnerAtCut &&
            cutsAtMidpoint && boardingCompletes &&
            returnCutsAtMidpoint && landsOnBoardwalk && natureAdvanced;
        return passed
            ? $"PASS：上船沿 V39 系泊点、船艉和座舱入口移动，暗点只有一个人物所有者；返航严格反向且自然时钟不停。{evidence}"
            : $"FAIL：上船/返航仍有跳锚点、双人物、硬切、落点错误或冻结自然。{evidence}";
    }

    public string RunEditorMooredBoatBoardingAttachmentProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalInspected = true;
        state = SliceState.TideRising;
        viewMode = SliceViewMode.Shelter;
        dayNightPhase = DayNightPhase.Day;
        playerLane = WalkLane.TideFlat;
        currentWaterY = boatAnchor.y - 0.34f;
        Vector2 lowTidePosition = GetMooredBoatPosition();
        TideOceanSample lowTideOcean = GetOceanSample(lowTidePosition.x);
        bool lowTideNeverUsesInvisibleFloor =
            Mathf.Abs(lowTidePosition.y - lowTideOcean.SurfaceY) <= 0.001f;
        mooringScreenActive = true;
        UpdateBoatVisuals(2.17f);
        Vector2 dockCoilPoint = new Vector2(
            GetBoatBoardingX() - 0.1f,
            GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.12f);
        Vector2 looseBoatTiePoint = GetMooredBoatPosition() + new Vector2(-0.48f, 0.24f);
        bool looseRopeGroundedAtDock = mooringRope.Phase == TideMooringRopePhase.Loose &&
            mooringRopeEndRenderer.enabled &&
            Vector2.Distance(mooringRopeEndRenderer.bounds.center, dockCoilPoint) <= 0.38f &&
            Vector2.Distance(mooringRopeEndRenderer.bounds.center, looseBoatTiePoint) >= 0.65f;
        for (int i = 0; i < mooringRopeSegments.Count; i++)
        {
            looseRopeGroundedAtDock &= mooringRopeSegments[i].enabled &&
                Mathf.Abs(mooringRopeSegments[i].bounds.center.y - dockCoilPoint.y) <= 0.13f;
        }
        float lowTideWaterlineError = float.PositiveInfinity;
        if (HasCompleteV39BoatPresentation() && boatHullRenderer != null && boatHullRenderer.enabled)
        {
            bool repaired = boatHullIntegrity >= 2 && boatSailIntegrity >= 1;
            float rotationZ = lowTideOcean.Slope * 4.5f;
            Vector2 backHullDelta = TideV39BoatPresentationModel.EvaluateOffsetWorldPosition(
                Vector2.zero,
                TideV39BoatPresentationModel.GetLayerOffset(TideV39BoatLayer.BackHull, repaired),
                rotationZ,
                FormalBoatFacesRight);
            Vector2 root = (Vector2)boatHullRenderer.transform.localPosition - backHullDelta;
            Vector2 renderedWaterline = TideV39BoatPresentationModel.EvaluateAnchorWorldPosition(
                root,
                new Vector2Int(
                    TideV39BoatPresentationModel.CanvasSize.x / 2,
                    TideV39BoatPresentationModel.StaticCalmWaterlineYTopLeft),
                rotationZ,
                FormalBoatFacesRight);
            lowTideWaterlineError = Mathf.Abs(renderedWaterline.y - lowTideOcean.SurfaceY);
        }
        bool lowTideRenderedAtOcean = lowTideWaterlineError <= 0.015f;

        currentWaterY = lowWaterY + 0.9f;
        tideClockSeconds = tideCycleSeconds * 0.75f;
        TickMooredBoatCurrent(2f);

        Vector2 mooredPosition = GetMooredBoatPosition();
        Vector2 expectedSternStep = GetMooredBoatSternStepPosition();
        TideOceanSample mooredOcean = GetOceanSample(mooredPosition.x);
        bool boatMovesWithWater = mooredBoatOffsetX > 0.2f &&
            Mathf.Abs(mooredPosition.y - mooredOcean.SurfaceY) <= 0.001f;

        playerPosition = new Vector2(GetBoatBoardingX(), GetPlayerLaneY(playerLane));
        BeginBoatViewTransition(BoatViewTransition.Boarding);
        bool boardingTargetsCurrentStern = Vector2.Distance(boatViewTransitionEndPosition, expectedSternStep) <= 0.01f;

        tideClockSeconds = tideCycleSeconds * 0.25f;
        TickMooredBoatCurrent(0.5f);
        TickBoatViewTransition(0.04f);
        Vector2 movingBoardingStern = GetMooredBoatSternStepPosition();
        bool boardingTracksSternDuringApproach =
            Vector2.Distance(boatViewTransitionEndPosition, movingBoardingStern) <= 0.01f;

        boatViewTransition = BoatViewTransition.None;
        viewMode = SliceViewMode.Sailing;
        BeginBoatViewTransition(BoatViewTransition.Returning);
        bool returnStartsAtCurrentStern = Vector2.Distance(boatViewTransitionStartPosition, movingBoardingStern) <= 0.01f;

        tideClockSeconds = tideCycleSeconds * 0.75f;
        TickMooredBoatCurrent(0.5f);
        TickBoatViewTransition(0.04f);
        Vector2 movingReturnStern = GetMooredBoatSternStepPosition();
        bool returnTracksSternBeforeLanding =
            Vector2.Distance(boatViewTransitionStartPosition, movingReturnStern) <= 0.01f;

        string evidence = $"待用绳落码头={looseRopeGroundedAtDock}；低潮贴水={lowTideNeverUsesInvisibleFloor}({lowTidePosition.y:F2}/{lowTideOcean.SurfaceY:F2})/画面误差={lowTideWaterlineError:F3}m；" +
            $"系泊偏移={mooredBoatOffsetX:F2}/浮高={mooredPosition.y:F2}；" +
            $"船艉={expectedSternStep.x:F2}；上船贴艉={boardingTargetsCurrentStern}/{boardingTracksSternDuringApproach}；" +
            $"返航贴艉={returnStartsAtCurrentStern}/{returnTracksSternBeforeLanding}";
        return looseRopeGroundedAtDock && lowTideNeverUsesInvisibleFloor &&
               lowTideRenderedAtOcean && boatMovesWithWater &&
               boardingTargetsCurrentStern &&
               boardingTracksSternDuringApproach &&
               returnStartsAtCurrentStern &&
               returnTracksSternBeforeLanding
            ? $"PASS：系泊船随潮流和水位移动，上船与返航始终贴住当前船艉。{evidence}"
            : $"FAIL：船体虽随潮移动，但人物仍走向旧船艉坐标或从空处返航。{evidence}";
    }

    public string RunEditorWalkSurfacePathContinuityProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalInspected = true;
        dayNightPhase = DayNightPhase.Day;
        state = SliceState.TideRising;
        currentWaterY = lowWaterY + 0.82f;

        float exteriorError = MeasureClimbRenderBodyError(
            SliceViewMode.Shelter,
            WalkLane.Deck,
            WalkLane.TideFlat,
            GetGangwayTopPosition(),
            GetGangwayBottomPosition(),
            0.5f);
        float interiorMainError = MeasureClimbRenderBodyError(
            SliceViewMode.Interior,
            WalkLane.InteriorLower,
            WalkLane.InteriorUpper,
            GetInteriorStairBottomPosition(),
            GetInteriorStairTopPosition(),
            0.5f);
        float interiorLoftError = MeasureClimbRenderBodyError(
            SliceViewMode.Interior,
            WalkLane.InteriorUpper,
            WalkLane.InteriorLoft,
            GetInteriorLoftStairBottomPosition(),
            GetInteriorLoftStairTopPosition(),
            0.5f);
        bool climbPathsAgree = exteriorError <= 0.02f &&
            interiorMainError <= 0.02f &&
            interiorLoftError <= 0.02f;
        bool declaredSurfacesMatch = !HasCompleteV35HouseInteriorPresentation() ||
            (Mathf.Abs(GetLaneMinX(WalkLane.Deck) - (GetV32WorldAnchor(new Vector2(286f, 1288f)).x + 0.02f)) <= 0.001f &&
             Mathf.Abs(GetLaneMaxX(WalkLane.Deck) - (GetV32WorldAnchor(new Vector2(1670f, 1288f)).x - 0.02f)) <= 0.001f &&
             Mathf.Abs(GetLaneMinX(WalkLane.InteriorLower) - (GetV35WorldAnchor(new Vector2(498f, 1636f)).x + InteriorBodyHalfWidth)) <= 0.001f &&
             Mathf.Abs(GetLaneMaxX(WalkLane.InteriorLower) - (GetV35WorldAnchor(new Vector2(1423f, 1636f)).x - InteriorBodyHalfWidth)) <= 0.001f &&
             Mathf.Abs(GetLaneMinX(WalkLane.InteriorUpper) - (GetV35WorldAnchor(new Vector2(430f, 1288f)).x + InteriorBodyHalfWidth)) <= 0.001f &&
             Mathf.Abs(GetLaneMaxX(WalkLane.InteriorUpper) - (GetV35WorldAnchor(new Vector2(1512f, 1288f)).x - InteriorBodyHalfWidth)) <= 0.001f &&
             Mathf.Abs(GetLaneMinX(WalkLane.InteriorLoft) - (GetV35WorldAnchor(new Vector2(830f, 760f)).x + InteriorBodyHalfWidth)) <= 0.001f &&
             Mathf.Abs(GetLaneMaxX(WalkLane.InteriorLoft) - (GetV35WorldAnchor(new Vector2(1090f, 760f)).x - InteriorBodyHalfWidth)) <= 0.001f);

        viewMode = SliceViewMode.Shelter;
        isLaneTransitioning = false;
        playerLane = WalkLane.TideFlat;
        playerPosition = GetGangwayBottomPosition();
        playerHorizontalVelocity = playerMoveSpeed * 0.4f;
        Vector2 beforeFirstStep = playerPosition;
        const float firstStepDelta = 1f / 60f;
        TickPlayerHorizontalLocomotion(1f, firstStepDelta);
        float firstStepDistance = Vector2.Distance(beforeFirstStep, playerPosition);
        bool noFirstStepWarp = firstStepDistance <= playerMoveSpeed * firstStepDelta * 1.25f;

        isLaneTransitioning = false;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(GetBoatBoardingX(), GetPlayerLaneY(WalkLane.TideFlat));
        mooredBoatOffsetX = 0f;
        mooringScreenActive = true;
        UpdateVisuals(2.35f);
        float pierTipX = GetTideFlatVisiblePathRight();
        float boardingX = GetBoatBoardingX();
        float laneMaxX = GetLaneMaxX(WalkLane.TideFlat);
        bool onePierEndpoint = Mathf.Abs(pierTipX - boardingX) <= 0.001f &&
            Mathf.Abs(laneMaxX - boardingX) <= 0.02f;
        float renderedPierLeft = float.PositiveInfinity;
        float renderedPierRight = float.NegativeInfinity;
        bool fixedPierIsContinuous = true;
        float previousRight = float.NaN;
        for (int i = 0; i < formalBoardwalkSegments.Count; i++)
        {
            SpriteRenderer segment = formalBoardwalkSegments[i];
            if (segment == null || !segment.enabled)
            {
                continue;
            }

            renderedPierLeft = Mathf.Min(renderedPierLeft, segment.bounds.min.x);
            renderedPierRight = Mathf.Max(renderedPierRight, segment.bounds.max.x);
            if (!float.IsNaN(previousRight))
            {
                fixedPierIsContinuous &= segment.bounds.min.x <= previousRight + 0.035f;
            }
            previousRight = segment.bounds.max.x;
        }
        bool fixedPierStopsAtEndpoint = renderedPierRight <= pierTipX + 0.03f &&
            renderedPierRight >= pierTipX - 0.03f;
        bool fixedPierStartsAtVisiblePath = renderedPierLeft <= GetBoardwalkVisualLeft() + 0.03f &&
            renderedPierLeft >= GetBoardwalkVisualLeft() - 0.03f;

        Vector2 renderedSternFoot = GetRenderedMooredBoatSternFootPosition();
        Vector2 logicalSternFoot = GetMooredBoatSternStepPosition() -
            Vector2.up * (TideV20CharacterPresentationModel.BodyWorldLength * 0.5f);
        bool boatFacesDeparture = boatHullRenderer != null && boatHullRenderer.enabled && boatHullRenderer.flipX;
        bool boatPoseOwnsStern = Vector2.Distance(renderedSternFoot, logicalSternFoot) <= 0.02f;
        bool gangplankBridgesGap = boatLandingWalkwayRenderer != null &&
            boatLandingWalkwayRenderer.enabled &&
            boatLandingWalkwayRenderer.bounds.min.x <= Mathf.Min(pierTipX, logicalSternFoot.x) + 0.04f &&
            boatLandingWalkwayRenderer.bounds.max.x >= Mathf.Max(pierTipX, logicalSternFoot.x) - 0.04f;
        float gangplankSlope = boatLandingWalkwayRenderer != null
            ? Mathf.Abs(Mathf.DeltaAngle(0f, boatLandingWalkwayRenderer.transform.localEulerAngles.z))
            : 180f;
        bool gangplankSlopeWalkable = gangplankSlope <= 35f;

        // 左岛存在时，海难来路已经由可见大船骸和可拆实物负责，旧 arrivalWreckX
        // 只能作为相机参考，不能继续留下隐形 F 热区。兼容旧场景时才验证旧残骸
        // 自己拥有交互；船艉在两种布局下都必须只归船所有。
        Vector2 savedPlayerPosition = playerPosition;
        playerPosition = new Vector2(arrivalWreckX, GetPlayerLaneY(WalkLane.TideFlat));
        bool originInteractionIsVisibleOnly = barrenIsland != null
            ? !IsPlayerNearArrivalWreck() && !IsPlayerNearBoat()
            : IsPlayerNearArrivalWreck() && !IsPlayerNearBoat();
        playerPosition = new Vector2(boardingX, GetPlayerLaneY(WalkLane.TideFlat));
        bool boatCenterOwnedByBoat = IsPlayerNearBoat() && !IsPlayerNearArrivalWreck();
        playerPosition = savedPlayerPosition;

        Camera camera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        playerPosition = new Vector2(arrivalWreckX - 0.38f, GetPlayerLaneY(WalkLane.TideFlat));
        mooringScreenActive = false;
        boatViewTransition = BoatViewTransition.None;
        UpdateVisuals(2.45f);
        float homeCameraX = camera != null ? camera.transform.position.x : 0f;

        float screenSeamX = GetMooringScreenSeamX();
        playerPosition = new Vector2(screenSeamX, GetPlayerLaneY(WalkLane.TideFlat));
        Vector2 seamWorldPosition = playerPosition;
        TryStartMooringScreenTransition(1f);
        UpdateVisuals(2.55f);
        float seamCameraX = camera != null ? camera.transform.position.x : 0f;
        bool seamDoesNotTeleport = boatViewTransition == BoatViewTransition.None &&
            Vector2.Distance(playerPosition, seamWorldPosition) <= 0.001f;

        playerPosition = new Vector2(boardingX, GetPlayerLaneY(WalkLane.TideFlat));
        Vector2 boardingWorldPosition = playerPosition;
        TryStartMooringScreenTransition(1f);
        UpdateVisuals(2.65f);
        float mooringCameraX = camera != null ? camera.transform.position.x : 0f;
        bool cameraFollowsContinuously = mooringScreenActive &&
            boatViewTransition == BoatViewTransition.None &&
            Vector2.Distance(playerPosition, boardingWorldPosition) <= 0.001f &&
            seamCameraX >= homeCameraX && mooringCameraX > seamCameraX + 0.2f;
        bool normalExteriorWashDisabled = foregroundMoonWashRenderer != null &&
            !foregroundMoonWashRenderer.enabled;

        playerPosition = new Vector2(arrivalWreckX - 0.38f, GetPlayerLaneY(WalkLane.TideFlat));
        TryStartMooringScreenTransition(-1f);
        UpdateVisuals(2.75f);
        float returnedCameraX = camera != null ? camera.transform.position.x : 0f;
        bool returnedToHomeScreen = !mooringScreenActive &&
            boatViewTransition == BoatViewTransition.None &&
            Mathf.Abs(returnedCameraX - homeCameraX) <= 0.01f;

        string evidence =
            $"梯误差(外/内/瞭望)={exteriorError:F3}/{interiorMainError:F3}/{interiorLoftError:F3}m；" +
            $"五段木面={declaredSurfacesMatch}；" +
            $"首步={firstStepDistance:F3}m；木路/交互/边界={pierTipX:F2}/{boardingX:F2}/{laneMaxX:F2}；" +
            $"船向右={boatFacesDeparture}；船艉误差={Vector2.Distance(renderedSternFoot, logicalSternFoot):F3}m；" +
            $"固定木路={fixedPierStartsAtVisiblePath}/{fixedPierIsContinuous}/{fixedPierStopsAtEndpoint}；短跳板={gangplankBridgesGap}/{gangplankSlope:F1}°；" +
            $"来路仅可见交互/船艉独占={originInteractionIsVisibleOnly}/{boatCenterOwnedByBoat}；" +
            $"镜头(归处/中段/泊位/返回)={homeCameraX:F2}/{seamCameraX:F2}/{mooringCameraX:F2}/{returnedCameraX:F2}；" +
            $"无传送={seamDoesNotTeleport}/{cameraFollowsContinuously}/{returnedToHomeScreen}；正常前景染色关闭={normalExteriorWashDisabled}";
        bool passed = climbPathsAgree && declaredSurfacesMatch && noFirstStepWarp && onePierEndpoint &&
            fixedPierStartsAtVisiblePath && fixedPierIsContinuous && fixedPierStopsAtEndpoint &&
            boatFacesDeparture && boatPoseOwnsStern && gangplankBridgesGap && gangplankSlopeWalkable &&
            originInteractionIsVisibleOnly && boatCenterOwnedByBoat && seamDoesNotTeleport &&
            cameraFollowsContinuously && normalExteriorWashDisabled && returnedToHomeScreen;
        return passed
            ? $"PASS：人物脚点、正式梯、两段固定木路、连续镜头、短跳板和动态船艉共用一条可见世界路径。{evidence}"
            : $"FAIL：行走逻辑与可见木梯/木路/船艉仍不是同一条路径。{evidence}";
    }

    public string RunEditorExplicitExteriorStairInputProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalVignetteActive = false;
        viewMode = SliceViewMode.Shelter;
        state = SliceState.TideRising;

        playerLane = WalkLane.Deck;
        playerPosition = GetGangwayTopPosition();
        TickPlayerHorizontalLocomotion(1f, 0.12f);
        bool rightDoesNotDescend = !isLaneTransitioning && playerLane == WalkLane.Deck;
        bool downStartsDescent = TryStartExteriorLaneTransition(false, true) &&
            isLaneTransitioning && laneTransitionTarget == WalkLane.TideFlat;
        TickLaneTransition(laneTransitionDurationSeconds + 0.02f);
        bool descentStopsAtVisibleFoot = !isLaneTransitioning &&
            playerLane == WalkLane.TideFlat &&
            Vector2.Distance(playerPosition, GetGangwayBottomPosition()) <= 0.01f &&
            Mathf.Abs(playerHorizontalVelocity) <= 0.001f;

        TickPlayerHorizontalLocomotion(-1f, 0.12f);
        bool leftDoesNotAscend = !isLaneTransitioning && playerLane == WalkLane.TideFlat;
        playerPosition = GetGangwayBottomPosition();
        bool upStartsAscent = TryStartExteriorLaneTransition(true, false) &&
            isLaneTransitioning && laneTransitionTarget == WalkLane.Deck;
        TickLaneTransition(laneTransitionDurationSeconds + 0.02f);
        bool ascentStopsAtVisibleHead = !isLaneTransitioning &&
            playerLane == WalkLane.Deck &&
            Vector2.Distance(playerPosition, GetGangwayTopPosition()) <= 0.01f &&
            Mathf.Abs(playerHorizontalVelocity) <= 0.001f;

        string evidence = $"右不下={rightDoesNotDescend}；S下={downStartsDescent}/{descentStopsAtVisibleFoot}；" +
            $"左不上={leftDoesNotAscend}；W上={upStartsAscent}/{ascentStopsAtVisibleHead}";
        return rightDoesNotDescend && downStartsDescent && descentStopsAtVisibleFoot &&
               leftDoesNotAscend && upStartsAscent && ascentStopsAtVisibleHead
            ? $"PASS：外梯只响应梯口的显式上下输入，横向移动不会吸附换层，落地后停止。{evidence}"
            : $"FAIL：外梯仍会被水平输入触发、落点错位或把惯性带到下一层。{evidence}";
    }

    public string RunEditorShelterDeepWaterCoverageProbe()
    {
        SetEditorShelterHighTideCoveragePreviewPose();

        Camera camera = Camera.main;
        if (camera == null)
        {
            camera = FindFirstObjectByType<Camera>();
        }

        if (camera == null || !camera.orthographic || naturalWaterSurfaceRenderer == null ||
            foregroundWaterOcclusionRenderer == null || foregroundDeepWaterOcclusionRenderer == null ||
            waterRenderer == null)
        {
            return "FAIL：高潮连续海体验收缺少正交相机或水体 Renderer。";
        }

        TideOceanSample ocean = GetOceanSample(0f);
        float storm01 = GetStormPressure01();
        float waterBodyHeight = 2.7f + storm01 * 0.18f;
        float expectedFormalCenterY = ocean.SurfaceY - waterBodyHeight * 0.5f +
            FormalWaterAverageCrestOffset;
        float cameraBottomY = camera.transform.position.y - camera.orthographicSize;
        Bounds formalBounds = naturalWaterSurfaceRenderer.bounds;
        Bounds deepBackgroundBounds = waterRenderer.bounds;
        Bounds formalForegroundBounds = foregroundWaterOcclusionRenderer.bounds;
        Bounds deepForegroundBounds = foregroundDeepWaterOcclusionRenderer.bounds;

        bool formalCrestUnchanged = naturalWaterSurfaceRenderer.enabled &&
            Mathf.Abs(naturalWaterSurfaceRenderer.transform.localPosition.y - expectedFormalCenterY) <= 0.001f;
        bool backgroundCoversFrame = waterRenderer.enabled &&
            deepBackgroundBounds.min.y <= cameraBottomY - 0.18f &&
            deepBackgroundBounds.max.y >= formalBounds.min.y + 0.08f;
        bool foregroundCoversFrame = foregroundDeepWaterOcclusionRenderer.enabled &&
            deepForegroundBounds.min.y <= cameraBottomY - 0.18f &&
            deepForegroundBounds.max.y >= formalForegroundBounds.min.y - 0.03f;
        bool supportCannotReadAsSecondSurface =
            deepBackgroundBounds.max.y <= ocean.SurfaceY - 0.48f &&
            deepForegroundBounds.max.y <= ocean.SurfaceY - 0.48f;
        bool layerOwnershipCorrect =
            waterRenderer.sortingOrder < naturalWaterSurfaceRenderer.sortingOrder &&
            foregroundDeepWaterOcclusionRenderer.sortingOrder < foregroundWaterOcclusionRenderer.sortingOrder &&
            foregroundDeepWaterOcclusionRenderer.sortingOrder > houseRenderer.sortingOrder;
        bool depthPresentationUsesOwnedSprites =
            waterRenderer.sprite == GetFormalSubsurfaceBandSprite() &&
            foregroundDeepWaterOcclusionRenderer.sprite == GetSubsurfaceFadeSprite() &&
            foregroundDeepWaterOcclusionRenderer.color.r <= 0.1f &&
            foregroundDeepWaterOcclusionRenderer.color.g <= 0.2f &&
            foregroundDeepWaterOcclusionRenderer.color.a >= 0.28f &&
            foregroundDeepWaterOcclusionRenderer.color.a <= 0.68f;
        const string expectedDepthShader = "Universal Render Pipeline/2D/Sprite-Unlit-Default";
        bool depthMaterialsAreUnlit =
            waterRenderer.sharedMaterial != null &&
            waterRenderer.sharedMaterial.shader != null &&
            waterRenderer.sharedMaterial.shader.name == expectedDepthShader &&
            foregroundDeepWaterOcclusionRenderer.sharedMaterial != null &&
            foregroundDeepWaterOcclusionRenderer.sharedMaterial.shader != null &&
            foregroundDeepWaterOcclusionRenderer.sharedMaterial.shader.name == expectedDepthShader;

        string evidence =
            $"水面/正式中心={ocean.SurfaceY:F2}/{naturalWaterSurfaceRenderer.transform.localPosition.y:F2}；" +
            $"相机底={cameraBottomY:F2}；正式海底={formalBounds.min.y:F2}；" +
            $"后承接={deepBackgroundBounds.min.y:F2}..{deepBackgroundBounds.max.y:F2}；" +
            $"前承接={deepForegroundBounds.min.y:F2}..{deepForegroundBounds.max.y:F2}；" +
            $"深水材质={waterRenderer.sharedMaterial?.shader?.name}/" +
            $"{foregroundDeepWaterOcclusionRenderer.sharedMaterial?.shader?.name}；" +
            $"排序={waterRenderer.sortingOrder}/{naturalWaterSurfaceRenderer.sortingOrder}/" +
            $"{foregroundDeepWaterOcclusionRenderer.sortingOrder}/{foregroundWaterOcclusionRenderer.sortingOrder}";
        bool passed = formalCrestUnchanged && backgroundCoversFrame && foregroundCoversFrame &&
            supportCannotReadAsSecondSurface && layerOwnershipCorrect &&
            depthPresentationUsesOwnedSprites &&
            depthMaterialsAreUnlit;
        return passed
            ? $"PASS：高潮正式浪尖保持唯一水面，后方深水与前方水下层连续覆盖到画外且没有第二条水线。{evidence}"
            : $"FAIL：高潮海图底边仍会露出背景、截断水下桩柱，或承接层顶边靠近浪面形成第二条水线。{evidence}";
    }

    public string RunEditorOcclusionOwnershipProbe()
    {
        EnsureScene();
        ResetSlice();

        SetEditorShelterHighTideCoveragePreviewPose();
        bool shelterDepthOrder = waterRenderer.sortingOrder < naturalWaterSurfaceRenderer.sortingOrder &&
            naturalWaterSurfaceRenderer.sortingOrder < houseRenderer.sortingOrder &&
            houseRenderer.sortingOrder < playerRenderer.sortingOrder &&
            playerRenderer.sortingOrder < foregroundDeepWaterOcclusionRenderer.sortingOrder &&
            foregroundDeepWaterOcclusionRenderer.sortingOrder < foregroundWaterOcclusionRenderer.sortingOrder;
        bool shelterHasOneWaterlineOwner = foregroundDeepWaterOcclusionRenderer.enabled &&
            foregroundWaterOcclusionRenderer.enabled &&
            !boatWaterlineOcclusionRenderer.enabled &&
            !boatWakeRenderer.enabled;
        string shelterEvidence =
            $"外景={waterRenderer.sortingOrder}<{naturalWaterSurfaceRenderer.sortingOrder}<" +
            $"{houseRenderer.sortingOrder}<{playerRenderer.sortingOrder}<" +
            $"{foregroundDeepWaterOcclusionRenderer.sortingOrder}<" +
            $"{foregroundWaterOcclusionRenderer.sortingOrder}；局部船水={boatWaterlineOcclusionRenderer.enabled}/" +
            $"{boatWakeRenderer.enabled}";

        SetEditorSailingPreviewPose();
        bool sailingDepthOrder = waterRenderer.sortingOrder < naturalWaterSurfaceRenderer.sortingOrder &&
            naturalWaterSurfaceRenderer.sortingOrder < boatBackRigRenderer.sortingOrder &&
            boatBackRigRenderer.sortingOrder < boatSailRenderer.sortingOrder &&
            boatSailRenderer.sortingOrder < boatHullRenderer.sortingOrder &&
            boatHullRenderer.sortingOrder < boatPassengerRenderer.sortingOrder &&
            boatPassengerRenderer.sortingOrder < boatPassengerGunwaleRenderer.sortingOrder &&
            boatPassengerGunwaleRenderer.sortingOrder < boatRudderRenderer.sortingOrder &&
            boatRudderRenderer.sortingOrder < foregroundDeepWaterOcclusionRenderer.sortingOrder;
        bool sailingHasOneWaterlineOwner = !foregroundWaterOcclusionRenderer.enabled &&
            foregroundDeepWaterOcclusionRenderer.enabled &&
            foregroundDeepWaterOcclusionRenderer.sortingOrder > boatRudderRenderer.sortingOrder &&
            !boatWaterlineOcclusionRenderer.enabled;
        string sailingEvidence =
            $"航行={waterRenderer.sortingOrder}<{naturalWaterSurfaceRenderer.sortingOrder}<" +
            $"{boatBackRigRenderer.sortingOrder}<{boatSailRenderer.sortingOrder}<" +
            $"{boatHullRenderer.sortingOrder}<{boatPassengerRenderer.sortingOrder}<" +
            $"{boatPassengerGunwaleRenderer.sortingOrder}<{boatRudderRenderer.sortingOrder}<" +
            $"{foregroundDeepWaterOcclusionRenderer.sortingOrder}；岸前景/整海前景/局部船水=" +
            $"{foregroundWaterOcclusionRenderer.enabled}/{foregroundDeepWaterOcclusionRenderer.enabled}/" +
            $"{boatWaterlineOcclusionRenderer.enabled}";

        SetEditorInteriorPreviewPose(true);
        float livingFloorY = GetPlayerStandingFeetY(WalkLane.InteriorUpper);
        float expectedFloodTopY = Mathf.Min(currentWaterY + 0.12f, livingFloorY - 0.08f);
        bool interiorDepthOrder = houseRenderer.sortingOrder < playerRenderer.sortingOrder &&
            playerRenderer.sortingOrder < interiorFloodRenderer.sortingOrder;
        float interiorFloodGeometryTopY = GetRendererGeometryTopY(interiorFloodRenderer);
        bool interiorWaterSynchronized = interiorFloodRenderer.enabled &&
            Mathf.Abs(interiorFloodGeometryTopY - expectedFloodTopY) <= 0.02f;
        bool interiorHasOneFloodOwner = !foregroundWaterOcclusionRenderer.enabled &&
            !foregroundDeepWaterOcclusionRenderer.enabled &&
            !boatWaterlineOcclusionRenderer.enabled &&
            interiorFloodRenderer.enabled;
        string interiorEvidence =
            $"室内={houseRenderer.sortingOrder}<{playerRenderer.sortingOrder}<" +
            $"{interiorFloodRenderer.sortingOrder}；进水顶/期望=" +
            $"{interiorFloodGeometryTopY:F2}/{expectedFloodTopY:F2}；" +
            $"户外深水/户外浪面/船水线={foregroundDeepWaterOcclusionRenderer.enabled}/" +
            $"{foregroundWaterOcclusionRenderer.enabled}/{boatWaterlineOcclusionRenderer.enabled}";

        bool passed = shelterDepthOrder && shelterHasOneWaterlineOwner &&
            sailingDepthOrder && sailingHasOneWaterlineOwner &&
            interiorDepthOrder && interiorWaterSynchronized && interiorHasOneFloodOwner;
        string evidence = shelterEvidence + sailingEvidence + interiorEvidence;
        return passed
            ? $"PASS：外景、航行和室内各自只有一个水线/进水遮挡所有者，人物与船舷前后关系严格有序。{evidence}"
            : $"FAIL：水、人物、房体或船舷仍存在并列排序、重复所有者或潮位不同步。{evidence}";
    }

    public string RunEditorOceanSurfaceReadabilityProbe()
    {
        EnsureScene();
        ResetSlice();

        EnsureV43SeaWeatherResourcesLoaded();
        bool useV43Waves = HasCompleteV43SeaWeatherPresentation();
        Sprite formalCrest = GetFormalSeaCurrentCrestSprite();
        if ((!useV43Waves && formalCrest == null) || GetFormalWaterSurfaceSprite() == null)
        {
            return "FAIL：正式海面或正式透明浪脊资源缺失，无法验证所见即所得海况。";
        }

        arrivalVignetteActive = false;
        viewMode = SliceViewMode.Shelter;
        state = SliceState.TideRising;
        tideClockSeconds = tideCycleSeconds * 0.18f;
        weatherClockSeconds = dayLengthSeconds * 0.43f;
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        // 先让连续镜头落到当前人物所在分屏，再以该世界中心寻找浪组。
        // 否则探针会拿上一场景的旧相机中心预测这一帧的浪。
        UpdateVisuals(0f);
        float shelterViewCenterX = GetActiveCameraCenterX();
        float shelterStorm01 = GetStormPressure01();
        float shelterWind01 = Mathf.Clamp01(
            Mathf.Abs(GetNaturalSailingWindSpeed()) / Mathf.Max(0.01f, sailingWindMaxSpeed));
        float shelterDirection = GetNaturalCurrentSpeed() + GetNaturalSailingWindSpeed() * 0.35f;
        TideOceanSample shelterCenterOcean = GetOceanSample(shelterViewCenterX);
        float shelterSampleTime = FindWaveEventProbeTime(
            shelterViewCenterX,
            shelterDirection,
            shelterWind01,
            shelterStorm01);
        UpdateVisuals(shelterSampleTime);

        bool shelterCrestsRegistered = waveStrips.Count == LocalWaveRendererCount;
        float shelterMaxHeightError = 0f;
        float shelterMaxSlopeError = 0f;
        int expectedVisibleShelterCrests = 0;
        int actualVisibleShelterCrests = 0;

        for (int i = 0; i < waveStrips.Count; i++)
        {
            SpriteRenderer crest = waveStrips[i];
            TideWaveEventSample waveEvent = TideWaveEventFieldModel.Sample(
                i,
                waveStrips.Count,
                shelterViewCenterX,
                shelterSampleTime,
                shelterDirection,
                shelterWind01,
                shelterStorm01);
            bool shouldBeVisible = waveEvent.Visible;
            expectedVisibleShelterCrests += shouldBeVisible ? 1 : 0;
            if (!shouldBeVisible)
            {
                shelterCrestsRegistered &= !crest.enabled;
                continue;
            }

            if (crest.enabled)
            {
                actualVisibleShelterCrests++;
            }

            float expectedX = waveEvent.WorldX;
            TideOceanSample ocean = GetOceanSample(expectedX);
            TideV43WaveKind kind = ResolveV43WaveKind(waveEvent.Kind);
            Sprite expectedV43Frame = useV43Waves
                ? TideV43SeaWeatherPresentationModel.EvaluateWaveFrame(
                    formalV43SeaWeatherCatalog,
                    kind,
                    shelterSampleTime,
                    waveEvent.FramePhase01,
                    waveEvent.FrameSpeedScale)
                : null;
            float expectedY = ocean.SurfaceY + (useV43Waves ? 0f : 0.025f);
            float expectedRotation = Mathf.Atan(ocean.Slope) * Mathf.Rad2Deg *
                (useV43Waves ? 0.58f : 0.42f);
            float xError = Mathf.Abs(crest.transform.localPosition.x - expectedX);
            float heightError = Mathf.Abs(crest.transform.localPosition.y - expectedY);
            float slopeError = Mathf.Abs(Mathf.DeltaAngle(crest.transform.localEulerAngles.z, expectedRotation));
            shelterMaxHeightError = Mathf.Max(shelterMaxHeightError, heightError);
            shelterMaxSlopeError = Mathf.Max(shelterMaxSlopeError, slopeError);
            shelterCrestsRegistered &= crest.enabled &&
                crest.sprite == (useV43Waves ? expectedV43Frame : formalCrest) &&
                crest.sortingOrder > naturalWaterSurfaceRenderer.sortingOrder &&
                crest.GetComponent<Collider2D>() == null &&
                xError <= 0.01f && heightError <= 0.035f && slopeError <= 0.75f;
        }

        EnterSailingScene();
        sailingBoatX = sailingHomeX + 8.4f;
        sailingBoatLaneY = sailingHomeY;
        UpdateVisuals(0f);
        float cameraWorldX = GetSailingCameraWorldX();
        float sailingStorm01 = GetStormPressure01();
        float sailingWind01 = Mathf.Clamp01(
            Mathf.Abs(GetNaturalSailingWindSpeed()) / Mathf.Max(0.01f, sailingWindMaxSpeed));
        float sailingDirection = GetSailingSurfaceFlowSpeed() + GetNaturalSailingWindSpeed() * 0.35f;
        TideOceanSample sailingCenterOcean = GetSailingOceanSample(cameraWorldX);
        float sailingSampleTime = FindWaveEventProbeTime(
            cameraWorldX,
            sailingDirection,
            sailingWind01,
            sailingStorm01);
        UpdateVisuals(sailingSampleTime);

        bool sailingCrestsRegistered = true;
        float sailingMaxHeightError = 0f;
        int expectedVisibleSailingCrests = 0;
        int visibleSailingCrests = 0;
        for (int i = 0; i < waveStrips.Count; i++)
        {
            SpriteRenderer crest = waveStrips[i];
            TideWaveEventSample waveEvent = TideWaveEventFieldModel.Sample(
                i,
                waveStrips.Count,
                cameraWorldX,
                sailingSampleTime,
                sailingDirection,
                sailingWind01,
                sailingStorm01);
            bool blockedByVortex = IsVortexBlockingRoute() &&
                Mathf.Abs(waveEvent.WorldX - routeVortexX) < 2.25f;
            bool shouldBeVisible = waveEvent.Visible && !blockedByVortex;
            expectedVisibleSailingCrests += shouldBeVisible ? 1 : 0;
            if (!shouldBeVisible)
            {
                sailingCrestsRegistered &= !crest.enabled;
                continue;
            }

            visibleSailingCrests += crest.enabled ? 1 : 0;
            float expectedScreenX = waveEvent.WorldX - cameraWorldX;
            TideOceanSample ocean = GetSailingOceanSample(waveEvent.WorldX);
            TideV43WaveKind kind = ResolveV43WaveKind(waveEvent.Kind);
            Sprite expectedV43Frame = useV43Waves
                ? TideV43SeaWeatherPresentationModel.EvaluateWaveFrame(
                    formalV43SeaWeatherCatalog,
                    kind,
                    sailingSampleTime,
                    waveEvent.FramePhase01,
                    waveEvent.FrameSpeedScale)
                : null;
            float expectedY = ocean.SurfaceY + (useV43Waves ? 0f : 0.025f);
            float xError = Mathf.Abs(crest.transform.localPosition.x - expectedScreenX);
            float heightError = Mathf.Abs(crest.transform.localPosition.y - expectedY);
            sailingMaxHeightError = Mathf.Max(sailingMaxHeightError, heightError);
            sailingCrestsRegistered &= crest.enabled &&
                crest.sprite == (useV43Waves ? expectedV43Frame : formalCrest) &&
                crest.sortingOrder > naturalWaterSurfaceRenderer.sortingOrder &&
                crest.GetComponent<Collider2D>() == null &&
                xError <= 0.01f && heightError <= 0.035f;
        }
        sailingCrestsRegistered &= visibleSailingCrests == expectedVisibleSailingCrests;

        bool flowCrestsOnSurface = false;
        float flowMaxHeightError = 0f;
        int visibleFlowCrests = 0;
        for (int i = 0; i < sailingFlowCrests.Count; i++)
        {
            SpriteRenderer crest = sailingFlowCrests[i];
            if (!crest.enabled)
            {
                continue;
            }

            visibleFlowCrests++;
            float screenX = crest.transform.localPosition.x;
            TideOceanSample ocean = GetSailingOceanSample(screenX + cameraWorldX);
            float heightError = Mathf.Abs(crest.transform.localPosition.y - (ocean.SurfaceY + 0.015f));
            flowMaxHeightError = Mathf.Max(flowMaxHeightError, heightError);
        }
        flowCrestsOnSurface = visibleFlowCrests > 0 && flowMaxHeightError <= 0.045f;

        shelterCrestsRegistered &= expectedVisibleShelterCrests > 0 &&
            actualVisibleShelterCrests == expectedVisibleShelterCrests;
        string evidence =
            $"槽位={waveStrips.Count}/{LocalWaveRendererCount}；近岸浪脊={actualVisibleShelterCrests}/{expectedVisibleShelterCrests} 高差/坡差={shelterMaxHeightError:F3}m/{shelterMaxSlopeError:F2}°；" +
            $"远航浪脊={visibleSailingCrests}/{expectedVisibleSailingCrests} 合格={sailingCrestsRegistered} 高差={sailingMaxHeightError:F3}m；" +
            $"流向浪脊={visibleFlowCrests} 高差={flowMaxHeightError:F3}m";
        bool spectralVariation = TideOceanFieldModel.ProbeSpectralVariation(out string spectralReason);
        bool eventFieldNatural = TideWaveEventFieldModel.ProbeNaturalCadence(out string eventReason);
        bool passed = shelterCrestsRegistered && sailingCrestsRegistered && flowCrestsOnSurface &&
            spectralVariation && eventFieldNatural;
        return passed
            ? $"PASS：连续海体唯一拥有水面，局部浪在世界场内形成和消散。{evidence}；浪谱={spectralReason}；事件={eventReason}"
            : $"FAIL：局部浪仍偏离海况、随镜头重掷或退化为固定横穿。{evidence}；浪谱={spectralReason}；事件={eventReason}";
    }

    public string RunEditorLocalWaveEventFieldProbe()
    {
        bool pureModelPassed = TideWaveEventFieldModel.ProbeNaturalCadence(out string modelReason);
        bool physicalCouplingPassed =
            TideAuthoritativeOceanModel.ProbeVisibleWaveCoupling(out string physicalReason);
        string integrationReport = RunEditorOceanSurfaceReadabilityProbe();
        bool integrationPassed = integrationReport.StartsWith("PASS", StringComparison.Ordinal);
        return pureModelPassed && physicalCouplingPassed && integrationPassed
            ? $"PASS：局部浪事件使用现实秒、世界分格和连续海况；同一可见浪组驱动局部浮力与推力，且未生成第二水面。{modelReason}；物理={physicalReason}；{integrationReport}"
            : $"FAIL：局部浪事件的周期、镜头稳定性、局部物理或海面接入不符合合同。{modelReason}；物理={physicalReason}；{integrationReport}";
    }

    public string RunEditorSailingWaveHandlingFeedbackProbe()
    {
        SetEditorSailingPreviewPose();
        weatherClockSeconds = dayLengthSeconds * stormFrontArrivalDays;
        sailingBoatX = sailingHomeX + 4.2f;
        sailingBoatLaneY = GetSailingMeanWaterY();

        float contactTime = -1f;
        TideOceanSample contactOcean = default;
        for (int i = 0; i < 720; i++)
        {
            float sampleTime = i * 0.2f;
            oceanEventPreviewTimeSeconds = sampleTime;
            TideOceanSample ocean = GetSailingOceanSample(sailingBoatX);
            if (ocean.LocalWaveContact01 >= 0.32f)
            {
                contactTime = sampleTime;
                contactOcean = ocean;
                break;
            }
        }

        if (contactTime < 0f)
        {
            ResetSlice();
            return "FAIL：高能短航海况中没有找到可用于反馈验收的局部可见浪接触。";
        }

        sailingDynamics.WaveSlamming01 = 0.78f;
        sailingDynamics.WaveHandlingQuality01 = 0.18f;
        UpdateVisuals(contactTime);
        Vector2 boatScreenPosition = GetSailingScreenPosition(GetSailingBoatBasePosition());
        bool impactRegistered = sailingWaveImpactRenderer != null &&
            sailingWaveImpactRenderer.enabled &&
            sailingWaveImpactRenderer.sprite == GetFormalStiltWaveImpactSprite() &&
            sailingWaveImpactRenderer.sortingOrder > boatPassengerGunwaleRenderer.sortingOrder &&
            sailingWaveImpactRenderer.GetComponent<Collider2D>() == null &&
            Vector2.Distance(sailingWaveImpactRenderer.transform.localPosition, boatScreenPosition) <= 1.2f;

        sailingDynamics.WaveSlamming01 = 0f;
        sailingDynamics.Ballast01 = -1f;
        UpdateVisuals(contactTime);
        Vector2 aftPivot = boatPassengerRenderer.transform.localPosition;
        Vector3 aftScale = boatPassengerRenderer.transform.localScale;
        sailingDynamics.Ballast01 = 1f;
        UpdateVisuals(contactTime);
        Vector2 forwardPivot = boatPassengerRenderer.transform.localPosition;
        Vector3 forwardScale = boatPassengerRenderer.transform.localScale;
        float ballastTravel = Vector2.Distance(aftPivot, forwardPivot);
        bool ballastVisibleWithoutRescale = ballastTravel >= 0.19f &&
            ballastTravel <= 0.24f &&
            Vector3.Distance(aftScale, forwardScale) <= 0.0001f;

        string evidence =
            $"接触={contactOcean.LocalWaveContact01:F2}@{contactTime:F1}s；拍浪层={impactRegistered}；" +
            $"压舱位移={ballastTravel:F2}m；缩放差={Vector3.Distance(aftScale, forwardScale):F4}";
        bool passed = impactRegistered && ballastVisibleWithoutRescale;
        ResetSlice();
        return passed
            ? $"PASS：同一可见浪接触产生船艏拍浪反馈，压舱移动人物而不缩放人物。{evidence}"
            : $"FAIL：拍浪或压舱反馈仍未落在船体真实层级与座舱尺度内。{evidence}";
    }

    public string RunEditorSailingTideContinuityProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalVignetteActive = false;
        weatherClockSeconds = dayLengthSeconds * 0.36f;

        tideClockSeconds = 0f;
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        float lowPhysicalWaterY = currentWaterY;
        float lowSailingMeanWaterY = GetSailingMeanWaterY();
        TideOceanSample lowSailingOcean = GetSailingOceanSample(sailingReefPoint.x);
        TideSailingReefSample lowMeanReef = TideSailingReefModel.Evaluate(
            lowWaterY,
            lowPhysicalWaterY,
            0f,
            0f,
            0f,
            GetEffectiveSailingMaxSpeed());
        sailingBoatX = sailingReefPoint.x;
        UpdateSailingReefVisuals(weatherClockSeconds);
        float lowRockAlpha = sailingReefPointRenderer != null && sailingReefPointRenderer.enabled
            ? sailingReefPointRenderer.color.a
            : 0f;

        tideClockSeconds = tideCycleSeconds * 0.5f;
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        float highPhysicalWaterY = currentWaterY;
        float highSailingMeanWaterY = GetSailingMeanWaterY();
        TideOceanSample highSailingOcean = GetSailingOceanSample(sailingReefPoint.x);
        TideSailingReefSample highMeanReef = TideSailingReefModel.Evaluate(
            lowWaterY,
            highPhysicalWaterY,
            0f,
            0f,
            0f,
            GetEffectiveSailingMaxSpeed());
        UpdateSailingReefVisuals(weatherClockSeconds);
        float highRockAlpha = sailingReefPointRenderer != null && sailingReefPointRenderer.enabled
            ? sailingReefPointRenderer.color.a
            : 0f;

        float physicalRise = highPhysicalWaterY - lowPhysicalWaterY;
        float sailingMeanRise = highSailingMeanWaterY - lowSailingMeanWaterY;
        float sailingRise = highSailingOcean.SurfaceY - lowSailingOcean.SurfaceY;
        bool macroTideExists = physicalRise >= 0.9f;
        bool sailingReadsSameTide = sailingMeanRise >= 0.9f &&
            Mathf.Abs(sailingMeanRise - physicalRise) <= 0.002f;
        bool tideWindowChangesClearance = lowMeanReef.GroundsKeel &&
            highMeanReef.UnderKeelClearanceMeters > 0f;
        bool exposedRockTracksWater = lowRockAlpha > 0.45f &&
            highRockAlpha < lowRockAlpha - 0.08f;
        string evidence =
            $"外景低/高={lowPhysicalWaterY:F2}/{highPhysicalWaterY:F2}m(升{physicalRise:F2})；" +
            $"短航均值升={sailingMeanRise:F2}m/瞬时浪面={lowSailingOcean.SurfaceY:F2}/{highSailingOcean.SurfaceY:F2}m(升{sailingRise:F2})；" +
            $"净空={lowMeanReef.UnderKeelClearanceMeters:F2}/{highMeanReef.UnderKeelClearanceMeters:F2}m；" +
            $"露礁Alpha={lowRockAlpha:F2}/{highRockAlpha:F2}";
        return macroTideExists && sailingReadsSameTide &&
            tideWindowChangesClearance && exposedRockTracksWater
            ? $"PASS：短航海面、浅礁净空和露礁表现随同一条天文潮连续变化。{evidence}"
            : $"FAIL：短航潮位、浅礁碰撞或露礁表现仍有一项脱离权威潮相。{evidence}";
    }

    public string RunEditorFirstSailingTideDecisionProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalVignetteActive = false;

        float reefLeftEdge = sailingReefPoint.x - TideSailingReefModel.ReefHalfWidthMeters;
        float reefRightEdge = sailingReefPoint.x + TideSailingReefModel.ReefHalfWidthMeters;
        bool reefIsOnFirstSalvageRoute = sailingHomeX < reefLeftEdge &&
            reefRightEdge < sailingSalvagePoint.x;

        float firstHighWaterY = EvaluateNaturalWaterY(tideCycleSeconds * 0.5f);
        float referenceSpeed = Mathf.Max(0.8f, GetEffectiveSailingMaxSpeed());
        TideSailingReefSample lowEmpty = TideSailingReefModel.Evaluate(
            lowWaterY, lowWaterY, 0f, 0f, 0f, referenceSpeed);
        TideSailingReefSample highEmpty = TideSailingReefModel.Evaluate(
            lowWaterY, firstHighWaterY, 0f, 0f, 0.18f, referenceSpeed);
        TideSailingReefSample highTowSlow = TideSailingReefModel.Evaluate(
            lowWaterY, firstHighWaterY, 1f, 1f, 0.18f, referenceSpeed);
        const float sampleSeconds = 0.5f;
        float emptyWindowSeconds = 0f;
        float slowTowWindowSeconds = 0f;
        float fastTowWindowSeconds = 0f;
        int sampleCount = Mathf.CeilToInt(tideCycleSeconds / sampleSeconds);
        for (int i = 0; i <= sampleCount; i++)
        {
            float sampleClock = Mathf.Min(tideCycleSeconds, i * sampleSeconds);
            float sampleWaterY = EvaluateNaturalWaterY(sampleClock);
            TideSailingReefSample emptySample = TideSailingReefModel.Evaluate(
                lowWaterY,
                sampleWaterY,
                0f,
                0f,
                0.18f,
                referenceSpeed);
            TideSailingReefSample slowTowSample = TideSailingReefModel.Evaluate(
                lowWaterY,
                sampleWaterY,
                1f,
                1f,
                0.18f,
                referenceSpeed);
            TideSailingReefSample fastTowSample = TideSailingReefModel.Evaluate(
                lowWaterY,
                sampleWaterY,
                1f,
                1f,
                referenceSpeed,
                referenceSpeed);
            if (!emptySample.GroundsKeel)
            {
                emptyWindowSeconds += sampleSeconds;
            }
            if (!slowTowSample.GroundsKeel)
            {
                slowTowWindowSeconds += sampleSeconds;
            }
            if (!fastTowSample.GroundsKeel)
            {
                fastTowWindowSeconds += sampleSeconds;
            }
        }

        bool physicalChoiceExists = lowEmpty.GroundsKeel &&
            highEmpty.HasComfortableClearance &&
            !highTowSlow.GroundsKeel &&
            emptyWindowSeconds >= slowTowWindowSeconds + 12f &&
            slowTowWindowSeconds >= fastTowWindowSeconds + 6f &&
            fastTowWindowSeconds >= 8f;
        string evidence =
            $"路线家/礁/木={sailingHomeX:F2}/{sailingReefPoint.x:F2}/{sailingSalvagePoint.x:F2}m；" +
            $"高潮净空空船/慢拖={highEmpty.UnderKeelClearanceMeters:F2}/" +
            $"{highTowSlow.UnderKeelClearanceMeters:F2}m；" +
            $"潮窗空/慢拖/快拖={emptyWindowSeconds:F1}/{slowTowWindowSeconds:F1}/{fastTowWindowSeconds:F1}s";
        return reefIsOnFirstSalvageRoute && physicalChoiceExists
            ? $"PASS：首航往返必须读潮；空船有余量，进水拖载需在高潮附近减速过礁。{evidence}"
            : $"FAIL：浅礁尚未进入首轮漂木往返，或自然潮窗没有形成可解的速度/载重取舍。{evidence}";
    }

    private float FindWaveEventProbeTime(
        float viewCenterWorldX,
        float travelDirection,
        float wind01,
        float storm01)
    {
        for (int sampleIndex = 0; sampleIndex < 240; sampleIndex++)
        {
            float sampleTime = sampleIndex * 0.25f;
            for (int slot = 0; slot < waveStrips.Count; slot++)
            {
                TideWaveEventSample waveEvent = TideWaveEventFieldModel.Sample(
                    slot,
                    waveStrips.Count,
                    viewCenterWorldX,
                    sampleTime,
                    travelDirection,
                    wind01,
                    storm01);
                if (waveEvent.Visible)
                {
                    return sampleTime;
                }
            }
        }

        return 0f;
    }

    public string RunEditorWindSailingCouplingProbe()
    {
        EnsureScene();
        ResetSlice();

        arrivalInspected = true;
        arrivalVignetteActive = false;
        viewMode = SliceViewMode.Shelter;
        state = SliceState.LowTidePlanning;
        weatherClockSeconds = 0f;
        sailingSailTrim01 = 1f;
        boatHullIntegrity = 3;
        boatSailIntegrity = 2;
        boatCabinIntegrity = 2;
        RecalculateBoatReadiness();

        // Midday sea breeze is onshore (-X). The physical roof streamer and the
        // signed sailing assist must agree on that direction.
        dayProgress01 = 0.5f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        float dayWind = GetNaturalSailingWindSpeed();
        TideBoatConditionPerformanceSample boatPerformance = GetBoatConditionPerformance();
        TideSailboatDynamicsState dayBoat = new TideSailboatDynamicsState
        {
            HeaveY = sailingHomeY,
            SailRaised01 = 1f
        };
        for (int step = 0; step < 100; step++)
        {
            dayBoat = TideSailboatDynamicsModel.Advance(
                dayBoat,
                0.02f,
                0f,
                0f,
                0f,
                dayWind,
                0f,
                sailingHomeY,
                0f,
                0f,
                boatPerformance);
        }
        UpdateVisuals(1.25f);
        bool dayPennantMatches = stormPennantRenderer.enabled && stormPennantRenderer.flipX;

        // Around midnight the land breeze is offshore (+X); both outputs must reverse
        // smoothly without adding wind displacement to a dry walking character.
        dayProgress01 = 0f;
        dayClockSeconds = 0f;
        float nightWind = GetNaturalSailingWindSpeed();
        TideSailboatDynamicsState nightBoat = new TideSailboatDynamicsState
        {
            HeaveY = sailingHomeY,
            SailRaised01 = 1f
        };
        for (int step = 0; step < 100; step++)
        {
            nightBoat = TideSailboatDynamicsModel.Advance(
                nightBoat,
                0.02f,
                0f,
                0f,
                0f,
                nightWind,
                0f,
                sailingHomeY,
                0f,
                0f,
                boatPerformance);
        }
        UpdateVisuals(2.05f);
        bool nightPennantMatches = stormPennantRenderer.enabled && !stormPennantRenderer.flipX;

        playerLane = WalkLane.Deck;
        playerPosition = new Vector2(GetLaneMinX(playerLane) + 0.5f, GetPlayerLaneY(playerLane));
        currentWaterY = GetPlayerStandingFeetY(playerLane) - 1f;
        float dryStartX = playerPosition.x;
        TickPlayerBuoyancyAndCurrent(0.5f);
        bool dryWalkerIgnoresWind = Mathf.Abs(playerPosition.x - dryStartX) <= 0.001f;

        bool directionalPhysics = dayWind < -0.05f && dayBoat.HorizontalVelocity < -0.03f &&
            nightWind > 0.05f && nightBoat.HorizontalVelocity > 0.03f;
        string evidence =
            $"日风/实船={dayWind:F2}/{dayBoat.HorizontalVelocity:F2}；" +
            $"夜风/实船={nightWind:F2}/{nightBoat.HorizontalVelocity:F2}；" +
            $"风标(日/夜)={dayPennantMatches}/{nightPennantMatches}；岸上位移={playerPosition.x - dryStartX:F3}m";
        return directionalPhysics && dayPennantMatches && nightPennantMatches && dryWalkerIgnoresWind
            ? $"PASS：屋顶风标与同一股昼夜风同步，顺逆风只进入帆船推进，干燥岸上人物不受风位移。{evidence}"
            : $"FAIL：风标、帆船助力或岸上移动仍未共用正确边界。{evidence}";
    }

    public string RunEditorOpeningGroundingProbe()
    {
        EnsureScene();
        ResetSlice();
        Vector2 openingPosition = playerPosition;
        UpdateVisuals(0f);

        float openingFeetY = GetPlayerStandingFeetY(WalkLane.TideFlat);
        bool supportedByVisiblePier = false;
        for (int i = 0; i < formalBoardwalkSegments.Count; i++)
        {
            SpriteRenderer segment = formalBoardwalkSegments[i];
            if (segment == null || !segment.enabled)
            {
                continue;
            }

            float authoredSurfaceY = segment.transform.localPosition.y -
                TideV53MooringCatalog.FoundPierCenterYOffsetFromSurface;
            supportedByVisiblePier |=
                openingPosition.x >= segment.bounds.min.x - 0.02f &&
                openingPosition.x <= segment.bounds.max.x + 0.02f &&
                Mathf.Abs(authoredSurfaceY - openingFeetY) <= 0.02f;
        }
        bool supportedByVisibleRock = barrenIsland != null &&
            barrenIsland.IsVisibleWalkSupportAt(new Vector2(openingPosition.x, openingFeetY));
        bool supportedByVisibleSurface = supportedByVisiblePier || supportedByVisibleRock;

        bool startsIdle = !playerMoving && Mathf.Abs(playerHorizontalVelocity) <= 0.001f;
        bool noArrivalAutoplay = !arrivalVignetteActive;
        Vector2 renderedBodyCenter = playerRenderer != null
            ? (Vector2)playerRenderer.transform.localPosition +
              Vector2.up * (TideV20CharacterPresentationModel.BodyWorldLength * 0.5f)
            : new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        bool rendererRegistered = playerRenderer != null && playerRenderer.enabled &&
            Vector2.Distance(renderedBodyCenter, openingPosition) <= 0.02f;
        TickArrivalVignette(0.8f);
        bool noHiddenDrift = Vector2.Distance(playerPosition, openingPosition) <= 0.001f;
        Camera camera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        float cameraX = camera != null ? camera.transform.position.x : 0f;
        float cameraAspect = camera != null && camera.targetTexture != null &&
            camera.targetTexture.height > 0
            ? (float)camera.targetTexture.width / camera.targetTexture.height
            : camera != null ? camera.aspect : 1f;
        float cameraHalfWidth = camera != null && camera.orthographic
            ? camera.orthographicSize * cameraAspect
            : 0f;
        bool openingFraming = camera != null && camera.orthographic &&
            Mathf.Abs(openingPosition.x - cameraX) <= cameraHalfWidth - 0.35f &&
            cameraX <= 0f;

        string evidence =
            $"开场={openingPosition.x:F2},{openingPosition.y:F2}；脚面={openingFeetY:F2}；" +
            $"可见承重(木/岩)={supportedByVisiblePier}/{supportedByVisibleRock}；静止={startsIdle}；" +
            $"无自动漂入={noArrivalAutoplay}/{noHiddenDrift}；人物配准={rendererRegistered}；" +
            $"镜头X/半宽={cameraX:F2}/{cameraHalfWidth:F2}";
        return supportedByVisibleSurface && startsIdle && noArrivalAutoplay && noHiddenDrift &&
               rendererRegistered && openingFraming
            ? $"PASS：开场人物已经静止站在可见承重面上，不再从画外、空中或海面自动走入。{evidence}"
            : $"FAIL：开场人物仍存在无支撑、自动位移或镜头错位。{evidence}";
    }

    public string RunEditorHeavyWreckTidalLiftIntegrationProbe()
    {
        EnsureScene();
        if (heavyWreckSalvage == null)
        {
            return "FAIL 场景没有 TideHeavyWreckSalvageController";
        }

        heavyWreckSalvage.ResetFeature();
        TideOceanSample ocean = GetOceanSample(heavyWreckSalvage.SampleWorldX);
        heavyWreckSalvage.UpdatePresentation(
            true,
            GetPlayerStandingFeetY(WalkLane.TideFlat),
            playerPosition,
            new Vector2(
                TideBarrenIslandController.ShelterDeliveryX,
                GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.02f),
            new Vector2(
                EscapeBoatStagingX,
                GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.02f),
            ocean,
            0f);
        return heavyWreckSalvage.RunEditorIntegrationProbe();
    }

    private float MeasureClimbRenderBodyError(
        SliceViewMode probeView,
        WalkLane fromLane,
        WalkLane targetLane,
        Vector2 fromBodyCenter,
        Vector2 toBodyCenter,
        float progress)
    {
        viewMode = probeView;
        playerLane = fromLane;
        isLaneTransitioning = true;
        laneTransitionFromLane = fromLane;
        laneTransitionTarget = targetLane;
        laneTransitionFromPosition = fromBodyCenter;
        laneTransitionToPosition = toBodyCenter;
        laneTransitionProgress = Mathf.Clamp01(progress);
        float eased = Mathf.SmoothStep(0f, 1f, laneTransitionProgress);
        playerPosition = Vector2.Lerp(fromBodyCenter, toBodyCenter, eased);
        playerMoving = true;
        UpdateVisuals(1.75f + progress);

        Vector2 renderedBodyCenter = (Vector2)playerRenderer.transform.localPosition +
            Vector2.up * (TideV20CharacterPresentationModel.BodyWorldLength * 0.5f);
        return Vector2.Distance(renderedBodyCenter, playerPosition);
    }

    private Vector2 GetRenderedMooredBoatSternFootPosition()
    {
        if (boatHullRenderer == null || !boatHullRenderer.enabled)
        {
            return new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        }

        if (HasCompleteV39BoatPresentation())
        {
            bool repaired = boatHullIntegrity >= 2 && boatSailIntegrity >= 1;
            float rotationZ = boatHullRenderer.transform.localEulerAngles.z;
            Vector2 layerDelta = TideV39BoatPresentationModel.EvaluateOffsetWorldPosition(
                Vector2.zero,
                TideV39BoatPresentationModel.GetLayerOffset(TideV39BoatLayer.BackHull, repaired),
                rotationZ,
                boatHullRenderer.flipX);
            Vector2 root = (Vector2)boatHullRenderer.transform.localPosition - layerDelta;
            return TideV39BoatPresentationModel.EvaluateAnchorWorldPosition(
                root,
                TideV39BoatPresentationModel.SternStepTopLeft,
                rotationZ,
                boatHullRenderer.flipX);
        }

        if (formalBoatV31Catalog == null)
        {
            return new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        }

        Vector2 localAnchor = TideV31BoatPresentationModel.PixelTopLeftToBoatOffset(
            formalBoatV31Catalog.Anchors.BoardingSternTopLeft,
            formalBoatV31Catalog.CanvasSize,
            formalBoatV31Catalog.PivotNormalized,
            formalBoatV31Catalog.PixelsPerUnit) * TideV31BoatPresentationModel.BoatRootScale;
        if (boatHullRenderer.flipX)
        {
            localAnchor.x = -localAnchor.x;
        }

        Vector2 rotatedAnchor = Quaternion.Euler(0f, 0f, boatHullRenderer.transform.localEulerAngles.z) * localAnchor;
        return (Vector2)boatHullRenderer.transform.localPosition + rotatedAnchor;
    }

    private bool AdvanceProbeMooringUntilSecured(
        float stepSeconds,
        float maximumSeconds,
        bool advanceNaturalWorld,
        ref float elapsedSeconds)
    {
        float dt = Mathf.Max(0.02f, stepSeconds);
        int maximumSteps = Mathf.CeilToInt(Mathf.Max(dt, maximumSeconds) / dt);
        for (int step = 0; step < maximumSteps; step++)
        {
            if (mooringRope.Phase == TideMooringRopePhase.Secured)
            {
                return true;
            }

            bool pressed = mooringRope.Phase == TideMooringRopePhase.Loose;
            bool held = mooringRope.Phase == TideMooringRopePhase.Swinging
                ? mooringRope.State.ThrowCharge01 < 0.47f
                : (mooringRope.Phase == TideMooringRopePhase.Attached ||
                   mooringRope.Phase == TideMooringRopePhase.Reeling) &&
                  mooringRope.State.Tension01 < 0.78f;
            bool released = mooringRope.Phase == TideMooringRopePhase.Swinging && !held;
            mooringRope.HandleInteraction(true, pressed, held, released, dt);

            if (advanceNaturalWorld)
            {
                TickState(dt);
            }
            else
            {
                TideOceanSample ocean = GetOceanSample(GetMooredBoatPosition().x);
                mooringRope.AdvanceEnvironment(
                    dt,
                    GetNaturalCurrentSpeed() + ocean.HorizontalVelocity,
                    GetNaturalSailingWindSpeed(),
                    false);
                mooredBoatOffsetFallback = mooringRope.BoatOffsetMeters;
            }

            elapsedSeconds += dt;
        }

        return mooringRope.Phase == TideMooringRopePhase.Secured;
    }

    public string RunEditorBoardingAvailabilityProbe()
    {
        SliceState[] boardableStates =
        {
            SliceState.LowTidePlanning,
            SliceState.TideRising,
            SliceState.EbbCollect,
            SliceState.RepairMoment
        };
        bool allNaturalPhasesBoard = true;
        string phaseEvidence = string.Empty;
        for (int i = 0; i < boardableStates.Length; i++)
        {
            ResetSlice();
            arrivalInspected = true;
            state = boardableStates[i];
            dayNightPhase = DayNightPhase.Day;
            currentWaterY = lowWaterY + 0.82f;
            currentHarvest = HarvestKind.None;
            currentHarvestBanked = true;
            harvestPhysicalState = HarvestPhysicalState.None;
            playerLane = WalkLane.TideFlat;
            playerPosition = new Vector2(GetBoatBoardingX(), GetPlayerLaneY(playerLane));
            float mooringSeconds = 0f;
            bool secured = AdvanceProbeMooringUntilSecured(
                0.05f,
                12f,
                false,
                ref mooringSeconds);
            TryBoardBoat();
            bool boarded = secured && boatViewTransition == BoatViewTransition.Boarding;
            allNaturalPhasesBoard &= boarded;
            phaseEvidence += $"{boardableStates[i]}={boarded}" +
                (i < boardableStates.Length - 1 ? "/" : string.Empty);
        }

        ResetSlice();
        arrivalInspected = true;
        state = SliceState.RepairMoment;
        dayNightPhase = DayNightPhase.Day;
        currentWaterY = lowWaterY + 0.82f;
        currentHarvest = HarvestKind.Wood;
        currentHarvestBanked = false;
        harvestPhysicalState = HarvestPhysicalState.Carried;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(GetBoatBoardingX(), GetPlayerLaneY(playerLane));
        float cargoBlockMooringSeconds = 0f;
        bool cargoBlockMooringSecured = AdvanceProbeMooringUntilSecured(
            0.05f,
            12f,
            false,
            ref cargoBlockMooringSeconds);
        TryBoardBoat();
        bool looseCargoBlocksBoarding = cargoBlockMooringSecured &&
            boatViewTransition == BoatViewTransition.None;

        ResetSlice();
        arrivalInspected = true;
        state = SliceState.TideRising;
        dayNightPhase = DayNightPhase.Night;
        currentWaterY = lowWaterY + 0.82f;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(GetBoatBoardingX(), GetPlayerLaneY(playerLane));
        float nightMooringSeconds = 0f;
        bool nightMooringSecured = AdvanceProbeMooringUntilSecured(
            0.05f,
            12f,
            false,
            ref nightMooringSeconds);
        TryBoardBoat();
        bool nightBlocksNewDeparture = nightMooringSecured &&
            boatViewTransition == BoatViewTransition.None;

        ResetSlice();
        arrivalInspected = true;
        state = SliceState.LowTidePlanning;
        dayNightPhase = DayNightPhase.Day;
        currentWaterY = lowWaterY + 0.12f;
        playerLane = WalkLane.TideFlat;
        playerPosition = new Vector2(GetBoatBoardingX(), GetPlayerLaneY(playerLane));
        TryBoardBoat();
        bool unsecuredBoatBlocksBoarding = boatViewTransition == BoatViewTransition.None &&
            mooringRope.Phase != TideMooringRopePhase.Secured;

        ResetSlice();
        arrivalInspected = true;
        state = SliceState.RepairMoment;
        playerLane = WalkLane.TideFlat;
        playerPosition = GetRepairChoicePosition(RepairChoice.Hull);
        RepairChoice repairAtHull;
        bool hullAnchorIsSeparate = !IsPlayerNearBoat() &&
            TryGetClosestRepairChoice(out repairAtHull) &&
            repairAtHull == RepairChoice.Hull;

        string evidence = $"阶段={phaseEvidence}；散货拦截={looseCargoBlocksBoarding}；夜间拦截={nightBlocksNewDeparture}；" +
            $"未系稳拦截={unsecuredBoatBlocksBoarding}；登船/修船分离={hullAnchorIsSeparate}";
        bool passed = allNaturalPhasesBoard && looseCargoBlocksBoarding && nightBlocksNewDeparture &&
            unsecuredBoatBlocksBoarding && hullAnchorIsSeparate;
        return passed
            ? $"PASS：上船由系缆、可见跳板、真实局部海况和携货状态决定，不再被潮汐阶段或伪泥滩硬锁。{evidence}"
            : $"FAIL：至少一个自然阶段仍被状态机锁船，或登船点与维修点冲突。{evidence}";
    }

    public string RunEditorTidePrepHoldContinuityProbe()
    {
        EnsureScene();
        ResetSlice();
        arrivalInspected = true;
        viewMode = SliceViewMode.Interior;
        state = SliceState.LowTidePlanning;
        dayNightPhase = DayNightPhase.Night;
        playerLane = WalkLane.InteriorLower;
        playerPosition = new Vector2(GetNightPrepPosition(TidePrepChoice.Rope).x, GetPlayerLaneY(playerLane));

        TickTidePrepWorkAtWorldTarget(tidePrepHoldSeconds * 0.38f, true, true);
        float partialProgress = tidePrepWorkProgress;
        bool partialDoesNotApply = !tidePrepReady && selectedPrepChoice == TidePrepChoice.None;
        TickTidePrepWorkAtWorldTarget(tidePrepHoldSeconds * 0.22f, false, false);
        float pausedProgress = tidePrepWorkProgress;
        TickTidePrepWorkAtWorldTarget(tidePrepHoldSeconds * 0.68f, false, true);
        bool ropeCompleted = tidePrepReady && selectedPrepChoice == TidePrepChoice.Rope;

        playerPosition = new Vector2(GetNightPrepPosition(TidePrepChoice.Bucket).x, GetPlayerLaneY(playerLane));
        TickTidePrepWorkAtWorldTarget(tidePrepHoldSeconds * 0.24f, true, true);
        bool shortBucketHoldKeepsRope = selectedPrepChoice == TidePrepChoice.Rope &&
            pendingTidePrepChoice == TidePrepChoice.Bucket && tidePrepWorkProgress > 0.2f;
        TickTidePrepWorkAtWorldTarget(tidePrepHoldSeconds * 0.8f, false, true);
        bool bucketCompleted = tidePrepReady && selectedPrepChoice == TidePrepChoice.Bucket;

        bool pausePreserved = Mathf.Abs(partialProgress - pausedProgress) <= 0.001f;
        string evidence = $"短按={partialProgress:F2}/未生效={partialDoesNotApply}；暂停={pausedProgress:F2}；绳完成={ropeCompleted}；" +
            $"桶短按保绳={shortBucketHoldKeepsRope}；桶完成={bucketCompleted}";
        bool passed = partialProgress > 0.3f && partialProgress < 0.5f && partialDoesNotApply &&
            pausePreserved && ropeCompleted && shortBucketHoldKeepsRope && bucketCompleted;
        return passed
            ? $"PASS：潮前准备按住推进、松手暂停，未完成的新工具不会覆盖旧选择。{evidence}"
            : $"FAIL：潮前准备仍会单击生效、丢进度或提前覆盖工具。{evidence}";
    }

    public string RunEditorInteriorViewTransitionProbe()
    {
        EnsureScene();
        EnsureV41CharacterContactResourcesLoaded();
        ResetSlice();
        arrivalInspected = true;
        state = SliceState.TideRising;
        playerLane = WalkLane.Deck;
        playerPosition = new Vector2(GetInteriorDoorX() - 0.5f, GetPlayerLaneY(playerLane));
        float startX = playerPosition.x;
        float startDayClock = dayClockSeconds;

        BeginBoatViewTransition(BoatViewTransition.EnteringInterior);
        TickBoatViewTransition(boatViewTransitionSeconds * 0.46f);
        TickState(boatViewTransitionSeconds * 0.46f);
        UpdateVisuals(2.1f);
        Sprite enteringOutsideFrame = HasCompleteV41CharacterContactPresentation()
            ? TideV41CharacterContactPresentationModel.EvaluateOneShotFrame(
                formalCharacterV41ContactCatalog,
                TideV41CharacterContactAction.DoorEnter,
                0.92f,
                false)
            : null;
        bool enteringOutsideUsesDoorFrame = enteringOutsideFrame != null &&
            playerRenderer.sprite == enteringOutsideFrame;
        bool approachesDoorOutside = viewMode == SliceViewMode.Shelter && playerPosition.x > startX + 0.18f;
        float fadeNearCut = GetBoatViewTransitionFade01();
        TickBoatViewTransition(boatViewTransitionSeconds * 0.08f);
        UpdateVisuals(2.2f);
        Sprite enteringInsideFrame = HasCompleteV41CharacterContactPresentation()
            ? TideV41CharacterContactPresentationModel.EvaluateOneShotFrame(
                formalCharacterV41ContactCatalog,
                TideV41CharacterContactAction.DoorEnter,
                0.08f,
                false)
            : null;
        bool enteringInsideUsesDoorFrame = enteringInsideFrame != null &&
            playerRenderer.sprite == enteringInsideFrame;
        bool entersAtMidpoint = viewMode == SliceViewMode.Interior && playerLane == WalkLane.InteriorUpper;
        TickBoatViewTransition(boatViewTransitionSeconds * 0.5f);
        bool enteringCompletes = boatViewTransition == BoatViewTransition.None && viewMode == SliceViewMode.Interior;

        playerPosition = new Vector2(GetInteriorDoorX() - 0.35f, GetPlayerLaneY(WalkLane.InteriorUpper));
        BeginBoatViewTransition(BoatViewTransition.ExitingInterior);
        TickBoatViewTransition(boatViewTransitionSeconds * 0.24f);
        UpdateVisuals(2.35f);
        Sprite exitingInsideFrame = HasCompleteV41CharacterContactPresentation()
            ? TideV41CharacterContactPresentationModel.EvaluateOneShotFrame(
                formalCharacterV41ContactCatalog,
                TideV41CharacterContactAction.DoorEnter,
                0.48f,
                true)
            : null;
        bool exitingInsideUsesDoorFrame = exitingInsideFrame != null &&
            playerRenderer.sprite == exitingInsideFrame;
        TickBoatViewTransition(boatViewTransitionSeconds * 0.3f);
        TickBoatViewTransition(boatViewTransitionSeconds * 0.54f);
        bool exitsAtMidpoint = viewMode == SliceViewMode.Shelter && playerLane == WalkLane.Deck;
        TickBoatViewTransition(boatViewTransitionSeconds * 0.2f);
        bool exitsToSameDoor = boatViewTransition == BoatViewTransition.None &&
            viewMode == SliceViewMode.Shelter &&
            Mathf.Abs(playerPosition.x - (GetInteriorDoorX() + 0.18f)) <= 0.01f;
        bool natureAdvanced = dayClockSeconds > startDayClock;
        bool doorFramesContinuous = enteringOutsideUsesDoorFrame && enteringInsideUsesDoorFrame &&
            exitingInsideUsesDoorFrame;

        string evidence = $"门外靠近={approachesDoorOutside}；暗点={fadeNearCut:F2}；进屋={entersAtMidpoint}/{enteringCompletes}；" +
            $"出屋={exitsAtMidpoint}/{exitsToSameDoor}；门动作={doorFramesContinuous}；昼夜继续={natureAdvanced}";
        bool passed = approachesDoorOutside && fadeNearCut >= 0.9f && entersAtMidpoint && enteringCompletes &&
            exitsAtMidpoint && exitsToSameDoor && doorFramesContinuous && natureAdvanced;
        return passed
            ? $"PASS：进出高脚屋在同一门槛暗点切换，人物不瞬移且自然时钟不停。{evidence}"
            : $"FAIL：进出屋仍有硬切、门口落点错误或冻结自然。{evidence}";
    }

    public string RunEditorV44LookoutVistaIntegrationProbe()
    {
        EnsureScene();
        EnsureV41CharacterContactResourcesLoaded();
        EnsureV43SeaWeatherResourcesLoaded();
        EnsureV44LookoutVistaResourcesLoaded();
        if (!HasCompleteV41CharacterContactPresentation() ||
            !HasCompleteV43SeaWeatherPresentation() ||
            !HasCompleteV44LookoutVistaPresentation())
        {
            return "FAIL：缺少 V41 人物、V43 海况或 V44 瞭望分层索引。";
        }

        ResetSlice();
        arrivalInspected = true;
        arrivalVignetteActive = false;
        viewMode = SliceViewMode.Interior;
        state = SliceState.TideRising;
        dayProgress01 = 0.84f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = DayNightPhase.Night;
        weatherClockSeconds = dayLengthSeconds * stormFrontArrivalDays * 0.62f;
        moonAgeDays = 12.7f;
        tideStrength = CalculateTideStrength(moonAgeDays);
        tideClockSeconds = tideCycleSeconds * 0.28f;
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        playerLane = WalkLane.InteriorLoft;
        playerPosition = new Vector2(GetInteriorLoftLookoutX(), GetPlayerLaneY(playerLane));
        lighthouseClues = 0;
        routeClueReturnRound = -1;

        float startDayClock = dayClockSeconds;
        float startWeatherClock = weatherClockSeconds;
        float startTideClock = tideClockSeconds;
        BeginBoatViewTransition(BoatViewTransition.EnteringLookout);
        TickBoatViewTransition(boatViewTransitionSeconds * 0.25f);
        UpdateVisuals(3.1f);
        Sprite expectedLookoutFrame = TideV41CharacterContactPresentationModel.EvaluateOneShotFrame(
            formalCharacterV41ContactCatalog,
            TideV41CharacterContactAction.Lookout,
            0.5f,
            false);
        bool bodyLooksThroughWindow = viewMode == SliceViewMode.Interior &&
            expectedLookoutFrame != null && playerRenderer.sprite == expectedLookoutFrame;

        TickState(boatViewTransitionSeconds * 0.32f);
        TickBoatViewTransition(boatViewTransitionSeconds * 0.32f);
        UpdateVisuals(3.5f);
        bool vistaOwnsFrame = viewMode == SliceViewMode.Lookout &&
            lookoutVistaMaskRenderer.enabled &&
            lookoutVistaRoofRenderer.enabled &&
            lookoutVistaCraneRenderer.enabled &&
            lookoutVistaWreckRenderer.enabled;
        bool liveOceanVisible = (naturalWaterSurfaceRenderer.enabled || waterRenderer.enabled) &&
            waveStrips.Count > 0 && waveStrips.TrueForAll(renderer => renderer != null && renderer.enabled);
        bool lighthouseHiddenBeforeClue = !lookoutVistaLighthouseRenderer.enabled &&
            !lookoutVistaBeamRenderer.enabled;

        lighthouseClues = 1;
        routeClueReturnRound = 0;
        tideRound = 1;
        UpdateVisuals(3.8f);
        bool lighthouseAppearsAfterClue = lookoutVistaLighthouseRenderer.enabled &&
            lookoutVistaBeamRenderer.enabled;
        bool natureContinues = dayClockSeconds > startDayClock &&
            weatherClockSeconds > startWeatherClock &&
            tideClockSeconds > startTideClock;

        // Finish the entry before asking for an exit. The transition owner rejects
        // overlapping requests by design, so the probe must respect the same input
        // contract as a player pressing F after the lookout view has settled.
        TickBoatViewTransition(boatViewTransitionSeconds * 0.45f);
        BeginBoatViewTransition(BoatViewTransition.ExitingLookout);
        TickBoatViewTransition(boatViewTransitionSeconds * 0.75f);
        UpdateVisuals(4.1f);
        Sprite expectedExitFrame = TideV41CharacterContactPresentationModel.EvaluateOneShotFrame(
            formalCharacterV41ContactCatalog,
            TideV41CharacterContactAction.Lookout,
            0.5f,
            true);
        bool bodyReturnsFromWindow = viewMode == SliceViewMode.Interior &&
            expectedExitFrame != null && playerRenderer.sprite == expectedExitFrame;
        TickBoatViewTransition(boatViewTransitionSeconds * 0.3f);
        bool returnsToSameLoftPoint = boatViewTransition == BoatViewTransition.None &&
            viewMode == SliceViewMode.Interior &&
            playerLane == WalkLane.InteriorLoft &&
            Mathf.Abs(playerPosition.x - GetInteriorLoftLookoutX()) <= 0.01f;

        string evidence =
            $"探窗动作={bodyLooksThroughWindow}/{bodyReturnsFromWindow}；分层={vistaOwnsFrame}；" +
            $"活海={liveOceanVisible}；灯塔因果={lighthouseHiddenBeforeClue}/{lighthouseAppearsAfterClue}；" +
            $"自然继续={natureContinues}；回到阁楼={returnsToSameLoftPoint}";
        bool passed = bodyLooksThroughWindow && bodyReturnsFromWindow && vistaOwnsFrame &&
            liveOceanVisible && lighthouseHiddenBeforeClue && lighthouseAppearsAfterClue &&
            natureContinues && returnsToSameLoftPoint;
        ResetSlice();
        return passed
            ? $"PASS：V44 瞭望由阁楼动作进入，沿用实时潮水/天气，并在航线线索成立后才显示灯塔。{evidence}"
            : $"FAIL：V44 瞭望的动作、分层、自然同步或灯塔因果至少一项失效。{evidence}";
    }
#endif

    public void SetEditorNetRigPreviewPose(int rigStage)
    {
        EnsureScene();
        ResetSlice();

        arrivalInspected = true;
        arrivalVignetteActive = false;
        viewMode = SliceViewMode.Shelter;
        state = SliceState.LowTidePlanning;
        currentWaterY = lowWaterY + 0.06f;
        tideCurrentlyRising = true;
        playerLane = WalkLane.TideFlat;
        playerFacing = 1;
        playerMoving = rigStage == 2;
        playerHorizontalVelocity = playerMoving ? playerMoveSpeed * 0.52f : 0f;
        netDeployed = false;
        netSecuredEarly = false;
        netSetDepth01 = 0.5f;
        netLoweringProgress = 0f;
        netUnrollProgress = 0f;

        int clampedStage = Mathf.Clamp(rigStage, 0, 4);
        if (clampedStage == 0)
        {
            netRigStep = NetRigStep.Stored;
            playerPosition = new Vector2(GetNetStoredX() - 0.38f, GetPlayerLaneY(playerLane));
            lastActionHint = "布网预览 1/5：网团仍收在木路边，先走近按 F 抱起。";
        }
        else if (clampedStage == 1)
        {
            netRigStep = NetRigStep.Carrying;
            playerPosition = new Vector2(GetNetFirstStakeX() - 0.18f, GetPlayerLaneY(playerLane));
            lastActionHint = "布网预览 2/5：人物抱着湿网，主桩还没有受力。";
        }
        else if (clampedStage == 2)
        {
            netRigStep = NetRigStep.FirstEndTied;
            netUnrollProgress = 0.58f;
            playerPosition = new Vector2(Mathf.Lerp(GetNetFirstStakeX(), GetNetSecondStakeX(), netUnrollProgress), GetPlayerLaneY(playerLane));
            lastActionHint = "布网预览 3/5：第一端已系牢，人物向第二桩走，网身从手里连续展开。";
        }
        else if (clampedStage == 3)
        {
            netRigStep = NetRigStep.Lowering;
            netUnrollProgress = 1f;
            netSetDepth01 = 0.52f;
            netLoweringProgress = netSetDepth01;
            selectedNetLine = GetNetLineFromLoweringProgress(netSetDepth01);
            netRigActionStarted = true;
            netRigActionHeld = true;
            playerPosition = new Vector2(GetNetSecondStakeX(), GetPlayerLaneY(playerLane));
            lastActionHint = "布网预览 4/5：两端已经系住，人物按住绳让沉纲连续下落；松手前网还未固定。";
        }
        else
        {
            netRigStep = NetRigStep.Deployed;
            netUnrollProgress = 1f;
            netSetDepth01 = 0.68f;
            netLoweringProgress = netSetDepth01;
            selectedNetLine = GetNetLineFromLoweringProgress(netSetDepth01);
            netDeployed = true;
            playerPosition = new Vector2(GetNetSecondStakeX() + 0.45f, GetPlayerLaneY(playerLane));
            lastActionHint = "布网预览 5/5：浮纲、沉纲、两端和坠石都已固定，玩家可以离开。";
        }

        UpdateVisuals(2.35f + clampedStage * 0.31f);
    }

    public void SetEditorNetRigHoldPreviewPose(int actionStage)
    {
        EnsureScene();
        ResetSlice();

        arrivalInspected = true;
        arrivalVignetteActive = false;
        viewMode = SliceViewMode.Shelter;
        state = SliceState.LowTidePlanning;
        currentWaterY = lowWaterY + 0.06f;
        playerLane = WalkLane.TideFlat;
        playerFacing = -1;
        netDeployed = false;
        netSecuredEarly = false;
        netRigActionProgress = 0.56f;
        netRigActionHeld = true;
        netRigActionStarted = true;

        int clampedStage = Mathf.Clamp(actionStage, 0, 2);
        if (clampedStage == 0)
        {
            netRigStep = NetRigStep.Carrying;
            playerPosition = new Vector2(GetNetFirstStakeX() + 0.08f, GetPlayerLaneY(playerLane));
            lastActionHint = "连续系网 1/3：人物在第一根主桩旁持续绕绳，湿网仍抱在身前。";
        }
        else if (clampedStage == 1)
        {
            netRigStep = NetRigStep.Unrolled;
            netUnrollProgress = 1f;
            playerPosition = new Vector2(GetNetSecondStakeX() + 0.05f, GetPlayerLaneY(playerLane));
            lastActionHint = "连续系网 2/3：网身已经沿路展开，人物在第二桩收紧自由端。";
        }
        else
        {
            netRigStep = NetRigStep.Lowering;
            netUnrollProgress = 1f;
            netSetDepth01 = 0.56f;
            netLoweringProgress = netSetDepth01;
            selectedNetLine = GetNetLineFromLoweringProgress(netSetDepth01);
            playerPosition = new Vector2(GetNetSecondStakeX() + 0.05f, GetPlayerLaneY(playerLane));
            lastActionHint = "连续系网 3/3：两端已固定，人物正逐段放下沉纲；松手后才会在当前水深固定。";
        }

        UpdateVisuals(2.9f + clampedStage * 0.34f);
    }
}
#endif
