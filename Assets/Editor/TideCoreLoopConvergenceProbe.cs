using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 第一轮主循环的无画面回归门。这里验证状态守恒和输入响应；清晰度、风格和
/// 构图仍只接受用户提供的原始 Game View，不用自动截图冒充肉眼验收。
/// </summary>
public static class TideCoreLoopConvergenceProbe
{
    [MenuItem("Tide/Validation/Run Core Loop Convergence Probe")]
    public static void RunFromMenu()
    {
        Debug.Log(RunAll());
    }

    public static void RunFromCommandLine()
    {
        Debug.Log(RunAll());
        TideRepairSceneConvergenceProbe.RunFromCommandLine();
        TideVisualSceneConvergenceProbe.RunFromCommandLine();
    }

    private static string RunAll()
    {
        string cistern = ProbeCistern();
        string wreckWork = ProbeWreckDismantle();
        string island = ProbeIslandOwnership();
        string context = ProbeIslandContextPriority();
        string salvage = ProbeSalvageMaterialCommit();
        string repairPhases = ProbeRepairWorkPhases();
        string repairSession = ProbeRepairWorkSession();
        string heavyWreck = ProbeHeavyWreckTidalLift();
        string rope = ProbeMooringRope();
        string mooredBoatAccess = ProbeMooredBoatAccess();
        string sailing = ProbeSailingDynamics();
        string boatRepairs = ProbeBoatConditionPerformance();
        string visibleWavePhysics = ProbeVisibleWavePhysicalCoupling();
        string waveHandling = ProbeSailingWaveHandling();
        string sailingReef = ProbeSailingReefClearance();
        string sailingReefRuntime = ProbeSailingReefRuntime();
        string sailingSalvageRuntime = ProbeSailingSalvageRuntime();
        string storm = ProbeStormRescue();
        string stormRuntime = ProbeStormRescueRuntime();
        string forecast = ProbeForecastSnapshot();
        string netEncounter = ProbeNetEncounter();
        string wrack = ProbeWrackDeposit();
        return $"TIDE_CORE_LOOP_PROBE PASS | {cistern} | {wreckWork} | {island} | {context} | {salvage} | {repairPhases} | {repairSession} | {heavyWreck} | {rope} | {mooredBoatAccess} | {sailing} | {boatRepairs} | {visibleWavePhysics} | {waveHandling} | {sailingReef} | {sailingReefRuntime} | {sailingSalvageRuntime} | {storm} | {stormRuntime} | {forecast} | {netEncounter} | {wrack}";
    }

    private static string ProbeWreckDismantle()
    {
        TideWreckDismantleStep firstPress = TideWreckDismantleModel.Advance(
            TideIslandSalvagePart.HullPlank,
            0f,
            0.02f,
            true,
            true,
            0f,
            0.08f);
        Require(firstPress.Worked && !firstPress.Completed && firstPress.Progress01 < 0.01f,
            "船骸原物仍可被单次按键瞬间拆走");

        TideWreckDismantleStep released = TideWreckDismantleModel.Advance(
            TideIslandSalvagePart.HullPlank,
            firstPress.Progress01,
            1f,
            false,
            true,
            0f,
            0.08f);
        Require(Mathf.Abs(released.Progress01 - firstPress.Progress01) <= 0.0001f &&
            released.BlockReason == TideWreckDismantleBlockReason.Released,
            "松手后船骸拆卸进度没有留在原物上");

        TideWreckDismantleStep flooded = TideWreckDismantleModel.Advance(
            TideIslandSalvagePart.HullPlank,
            firstPress.Progress01,
            1f,
            true,
            false,
            0.62f,
            0.12f);
        TideWreckDismantleStep breaker = TideWreckDismantleModel.Advance(
            TideIslandSalvagePart.HullPlank,
            firstPress.Progress01,
            1f,
            true,
            true,
            0.08f,
            0.92f);
        Require(!flooded.Worked &&
            flooded.BlockReason == TideWreckDismantleBlockReason.NoStableFooting &&
            !breaker.Worked && breaker.BlockReason == TideWreckDismantleBlockReason.BreakingWave,
            "失去岩面支撑或破浪压住船骸时仍能隔水推进拆卸");

        TideWreckDismantleStep dry = TideWreckDismantleModel.Advance(
            TideIslandSalvagePart.HullPlank,
            firstPress.Progress01,
            1f,
            true,
            true,
            0f,
            0.08f);
        TideWreckDismantleStep wet = TideWreckDismantleModel.Advance(
            TideIslandSalvagePart.HullPlank,
            firstPress.Progress01,
            1f,
            true,
            true,
            0.36f,
            0.55f);
        Require(dry.Progress01 > wet.Progress01 + 0.05f,
            "脚边进水和浪载没有降低实际拆卸效率");
        Require(TideWreckDismantleModel.GetRequiredWorkSeconds(TideIslandSalvagePart.Sailcloth) <
            TideWreckDismantleModel.GetRequiredWorkSeconds(TideIslandSalvagePart.HullPlank) &&
            TideWreckDismantleModel.GetRequiredWorkSeconds(TideIslandSalvagePart.HullPlank) <
            TideWreckDismantleModel.GetRequiredWorkSeconds(TideIslandSalvagePart.RivetedPlate),
            "割帆布、撬木板和拆铆板仍使用同一虚假工时");
        return $"船骸拆卸=按压{firstPress.Progress01:P1}/松手保留/水深{TideWreckDismantleModel.MaximumWorkableWaterDepthMeters:F2}m停工/破浪停工/干湿速率{dry.WorkRate01:F2}->{wet.WorkRate01:F2}";
    }

    private static string ProbeNetEncounter()
    {
        const float headY = 1f;
        const float surfaceY = 1f;
        float shallowBottomY = headY - 0.34f;
        float deepBottomY = headY - 1.2f;
        TideNetEncounterModel.MaterialProfile fish = TideNetEncounterModel.GetProfile(
            TideDriftMaterial.Fish);

        TideNetEncounterModel.Step noArrivalPreload = TideNetEncounterModel.Advance(
            0f, 8f, 0.4f, 0.4f, headY, deepBottomY, surfaceY, 1f, TideDriftMaterial.Fish);
        Require(noArrivalPreload.Progress01 <= 0.0001f,
            "漂物尚未进入网口，湿网却提前积攒了捕获进度");

        TideNetEncounterModel.Step shallow = TideNetEncounterModel.Advance(
            0f, 1.2f, 0.95f, 1.02f, headY, shallowBottomY, surfaceY, 1f, TideDriftMaterial.Fish);
        TideNetEncounterModel.Step deep = TideNetEncounterModel.Advance(
            0f, 1.2f, 0.95f, 1.02f, headY, deepBottomY, surfaceY, 1f, TideDriftMaterial.Fish);
        Require(shallow.MeshCoverage01 < fish.MinimumCoverage01 && !shallow.Captured,
            "浅网没有从网底漏过低走鱼群，网深失去物理意义");
        Require(deep.MeshCoverage01 > 0.99f && deep.Captured,
            "深网完整覆盖鱼群后仍不能在同一次可见相遇中挂稳");

        TideNetEncounterModel.Step overtoppedParcel = TideNetEncounterModel.Advance(
            0f, 1.2f, 0.95f, 1.02f, headY, deepBottomY, headY + 0.28f, 1f,
            TideDriftMaterial.ChartParcel);
        Require(overtoppedParcel.MeshCoverage01 <= 0.0001f && !overtoppedParcel.Captured,
            "漂在高潮表面的纸包不能从已经没顶的网绳上方越过");

        TideNetEncounterModel.Step freeSaltWood = TideNetEncounterModel.Advance(
            0f, 1.4f, 0.95f, 1.02f, headY, deepBottomY, headY + 0.28f, 1f,
            TideDriftMaterial.SaltWood);
        TideNetEncounterModel.Step guidedSaltWood = TideNetEncounterModel.Advance(
            0f, 1.4f, 0.95f, 1.02f, headY, deepBottomY, headY + 0.28f, 1f,
            TideDriftMaterial.SaltWood, 0.42f);
        Require(!freeSaltWood.Captured && guidedSaltWood.Captured &&
            guidedSaltWood.MeshCoverage01 > freeSaltWood.MeshCoverage01 + 0.8f,
            "可见导流索没有把同一根浮木压入网面，或自由浮木也获得了隐藏下压");

        TideNetEncounterModel.Step partial = TideNetEncounterModel.Advance(
            0f, 0.1f, 0.95f, 1.01f, headY, deepBottomY, surfaceY, 1f, TideDriftMaterial.Fish);
        TideNetEncounterModel.Step slipped = TideNetEncounterModel.Advance(
            partial.Progress01, 0.05f, 1.01f, 1.2f, headY, deepBottomY, surfaceY, 1f,
            TideDriftMaterial.Fish);
        Require(partial.Progress01 > 0f && slipped.ContactLost && slipped.Progress01 <= 0.0001f,
            "擦网漏过的实物把旧接触秒数带进了下一次相遇");

        float lowFrameOverlap = TideNetEncounterModel.EvaluateWindowOverlap01(0.82f, 1.2f);
        Require(lowFrameOverlap > 0.3f && lowFrameOverlap < 0.4f,
            "低帧率一步跨过网口时没有积分真实线段重叠");
        return $"网遭遇=不预充/鱼覆盖{shallow.MeshCoverage01:F2}->{deep.MeshCoverage01:F2}/纸包越顶/盐木导压{freeSaltWood.MeshCoverage01:F2}->{guidedSaltWood.MeshCoverage01:F2}/擦过清零/跨帧{lowFrameOverlap:F2}";
    }

    private static string ProbeWrackDeposit()
    {
        const int cycle = 4;
        const float groundY = -1.98f;
        TideDriftField field = TideDriftSourceModel.BuildField(
            cycle,
            8.2f,
            0.58f,
            0.2f,
            false);
        TideDriftField sameCycle = TideDriftSourceModel.BuildField(
            cycle,
            8.2f,
            0.58f,
            0.2f,
            false);
        TideDriftField nextCycle = TideDriftSourceModel.BuildField(
            cycle + 1,
            8.2f,
            0.58f,
            0.2f,
            false);
        Require(field.NearshoreBatch.StableId == sameCycle.NearshoreBatch.StableId,
            "同一天文潮次重建漂流场时批次身份变化");
        Require(field.NearshoreBatch.StableId != nextCycle.NearshoreBatch.StableId,
            "跨过真实潮次后仍沿用旧漂流批次");

        TideWrackDepositState captured = TideWrackDepositModel.TrySettle(
            default,
            field.NearshoreBatch,
            cycle,
            groundY + 0.72f,
            groundY,
            TideWrackLineController.SeawardWorldX,
            TideWrackLineController.InlandWorldX,
            true,
            true);
        Require(!captured.IsPresent, "已经入网的实物又在岸上复制一份");

        TideWrackDepositState stranded = TideWrackDepositModel.TrySettle(
            default,
            field.NearshoreBatch,
            cycle,
            groundY + 0.72f,
            groundY,
            TideWrackLineController.SeawardWorldX,
            TideWrackLineController.InlandWorldX,
            false,
            true);
        Require(stranded.IsPresent &&
            stranded.BatchId == field.NearshoreBatch.StableId &&
            stranded.WorldX <= TideWrackLineController.SeawardWorldX &&
            stranded.WorldX >= TideWrackLineController.InlandWorldX,
            "漏过网口的近岸实物没有在可见右岸高潮线搁浅");
        Require(!TideWrackDepositModel.ShouldRefloat(
                stranded,
                cycle,
                groundY + 0.2f) &&
            !TideWrackDepositModel.ShouldRefloat(
                stranded,
                cycle + 1,
                groundY - 0.2f),
            "同潮或尚未淹到岩面的下一潮提前卷走漂积物");
        Require(TideWrackDepositModel.ShouldRefloat(
                stranded,
                cycle + 1,
                groundY),
            "下一次够高的潮没有重新卷走漂积物");
        return $"漂积=天文潮{cycle}->{cycle + 1}/批次{stranded.BatchId}/岸位{stranded.WorldX:F2}m/再浸卷走";
    }

    private static string ProbeForecastSnapshot()
    {
        const int targetCycle = 4;
        const float observedHighWaterY = 1.8f;
        TideForecastSnapshot lookout = TideForecastSnapshotModel.Capture(
            targetCycle,
            observedHighWaterY,
            false);
        TideForecastSnapshot repaired = TideForecastSnapshotModel.Capture(
            targetCycle,
            observedHighWaterY,
            true);

        Require(Mathf.Abs(lookout.WidthMeters - 0.44f) <= 0.0001f,
            "阁楼粗观测没有保留 44cm 的物理不确定区间");
        Require(Mathf.Abs(repaired.WidthMeters - 0.16f) <= 0.0001f,
            "修复潮尺没有把物理不确定区间缩到 16cm");
        Require(TideForecastSnapshotModel.IsCurrent(lookout, 0.2f, targetCycle) &&
            TideForecastSnapshotModel.IsCurrent(lookout, 0.5f, targetCycle),
            "目标高潮到达前观测快照提前失效");
        Require(!TideForecastSnapshotModel.IsCurrent(lookout, 0.5001f, targetCycle),
            "高潮过后旧观测仍冒充下一潮预报");

        TideForecastSnapshot laterWeather = TideForecastSnapshotModel.Capture(
            targetCycle,
            observedHighWaterY + 0.7f,
            false);
        Require(Mathf.Abs(lookout.LowerY - (observedHighWaterY - 0.22f)) <= 0.0001f &&
            Mathf.Abs(lookout.UpperY - (observedHighWaterY + 0.22f)) <= 0.0001f &&
            Mathf.Abs(laterWeather.LowerY - lookout.LowerY) > 0.6f,
            "后续天气预测反向改写了已经留下的旧绳结");
        return $"潮预报=潮次{lookout.TargetCycleOrdinal}/粗修{lookout.WidthMeters:F2}/{repaired.WidthMeters:F2}m/过高潮失效";
    }

    private static string ProbeCistern()
    {
        TideRainCisternState initial = TideRainCisternModel.CreateDamaged();
        TideRainCisternState rainy = TideRainCisternModel.Advance(initial, 1800f, 22f, 1f, 0f);
        TideRainCisternState dry = TideRainCisternModel.Advance(rainy, 1800f, 0f, 1f, 0f);
        TideRainCisternState salted = TideRainCisternModel.Advance(dry, 80f, 0f, 1f, 1f);
        TideRainCisternState repaired = TideRainCisternModel.RepairCrack(initial, 1f);
        TideRainCisternState damagedLeak = TideRainCisternModel.Advance(initial, 1800f, 0f, 1f, 0f);
        TideRainCisternState repairedLeak = TideRainCisternModel.Advance(repaired, 1800f, 0f, 1f, 0f);
        TideRainCisternState afterFill = TideRainCisternModel.WithdrawPotableWater(
            initial,
            4f,
            true,
            out TidePortableWaterState portable);
        Require(rainy.StoredLiters > initial.StoredLiters, "雨槽没有增加蓄水");
        Require(dry.StoredLiters < rainy.StoredLiters, "裂池在无雨时没有漏水");
        Require(salted.SaltFraction01 > dry.SaltFraction01, "暴潮越池没有提高盐分");
        Require(repaired.Crack01 <= 0.1f &&
            repairedLeak.StoredLiters > damagedLeak.StoredLiters + 0.4f,
            "铆接补片没有显著降低同一段现实时间内的裂池漏水");
        Require(TideRainCisternModel.GetDrinkableLiters(initial) >= initial.StoredLiters - 0.001f,
            "初始雨水被错误标成不可饮用盐水");
        Require(TideRainCisternModel.GetDrinkableLiters(salted) <= 0.001f,
            "暴潮盐水倒灌后仍被当作可饮水");
        Require(Mathf.Abs(portable.Liters - 4f) <= 0.001f &&
            Mathf.Abs(initial.StoredLiters - afterFill.StoredLiters - portable.Liters) <= 0.001f,
            "蓄水池装入便携容器时没有守恒");
        return $"蓄水 {initial.StoredLiters:F1}->{rainy.StoredLiters:F1}->{dry.StoredLiters:F1}L/补片裂缝{initial.Crack01:F2}->{repaired.Crack01:F2}/暴潮盐{salted.SaltFraction01:P2}/装罐{portable.Liters:F1}L";
    }

    private static string ProbeIslandOwnership()
    {
        GameObject root = new GameObject("TideCoreLoopIslandProbe");
        try
        {
            TideBarrenIslandController island = root.AddComponent<TideBarrenIslandController>();
            island.ResetIsland();
            island.UpdatePresentation(
                true,
                -1f,
                new Vector2(-12.73f, -0.42f),
                new Vector2(-5.65f, -0.98f),
                new Vector2(8.23f, -0.98f),
                -0.3f,
                -1.5f,
                1f);
            bool took = CompleteIslandDismantle(
                island,
                new Vector2(-12.73f, -0.42f),
                out TideIslandSalvagePart part);
            bool stagedAtShelter = island.TryStageCarriedPart(
                TideIslandSalvageUse.Shelter,
                out TideIslandSalvagePart shelterPart);
            Require(took && part != TideIslandSalvagePart.None, "船骸可拆件无法取得");
            Require(stagedAtShelter && shelterPart == part, "搬运时实物身份发生变化");
            Require(island.CarriedPart == TideIslandSalvagePart.None && island.ShelterStagedParts == 1,
                "拆件没有从手中转移到住所施工位");
            Require(island.GetDestination(part) == TideIslandSalvageDestination.ShelterStaging,
                "住所施工位没有继续拥有同一件原物");
            island.UpdatePresentation(
                true,
                -1f,
                new Vector2(-5.65f, -0.98f),
                new Vector2(-5.65f, -0.98f),
                new Vector2(8.23f, -0.98f),
                -0.3f,
                -1.5f,
                1.1f);
            RequireSingleVisibleOwner(root.transform, part, TideIslandSalvageDestination.ShelterStaging);
            bool integrated = island.TryIntegrateStagedPart(
                part,
                TideIslandSalvageDestination.ShelterStaging);
            Require(integrated && island.ShelterStagedParts == 0,
                "最终固定后原物仍滞留在住所暂存计数中");
            Require(island.GetDestination(part) == TideIslandSalvageDestination.IntegratedIntoShelter,
                "最终固定后原物没有归入住所正式 owner");
            RequireNoLooseOwner(root.transform, part);

            bool tookCloth = CompleteIslandDismantle(
                island,
                new Vector2(-11.83f, -0.04f),
                out TideIslandSalvagePart secondPart);
            bool stagedAtBoat = island.TryStageCarriedPart(
                TideIslandSalvageUse.EscapeBoat,
                out TideIslandSalvagePart boatPart);
            Require(tookCloth && stagedAtBoat && boatPart == secondPart,
                "第二件原物无法独立转移到逃生船施工位");
            Require(island.GetDestination(secondPart) == TideIslandSalvageDestination.EscapeBoatStaging,
                "逃生船施工位没有继续拥有同一件原物");
            island.UpdatePresentation(
                true,
                -1f,
                new Vector2(8.23f, -0.98f),
                new Vector2(-5.65f, -0.98f),
                new Vector2(8.23f, -0.98f),
                -0.3f,
                -1.5f,
                1.2f);
            RequireSingleVisibleOwner(root.transform, secondPart, TideIslandSalvageDestination.EscapeBoatStaging);
            return $"船骸 {part}->住所/{secondPart}->船/实物未入账";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static bool CompleteIslandDismantle(
        TideBarrenIslandController island,
        Vector2 playerPosition,
        out TideIslandSalvagePart part)
    {
        part = TideIslandSalvagePart.None;
        for (int i = 0; i < 100; i++)
        {
            bool consumed = island.TickDismantleNearestPart(
                playerPosition,
                0.1f,
                i == 0,
                true,
                true,
                -1.35f,
                0.08f,
                out TideIslandDismantleFeedback feedback);
            if (!consumed)
            {
                return false;
            }

            if (feedback.Completed)
            {
                part = feedback.Part;
                return island.CarriedPart == part;
            }
        }

        return false;
    }

    private static void RequireNoLooseOwner(Transform root, TideIslandSalvagePart part)
    {
        Transform visualRoot = root.Find("GeneratedBarrenIslandRoot");
        string sourceName = part == TideIslandSalvagePart.HullPlank ? "SalvageHullPlank" :
            part == TideIslandSalvagePart.Sailcloth ? "SalvageSailcloth" : "SalvageRivetedPlate";
        Require(!FindRenderer(visualRoot, sourceName).enabled, $"{part} 已固定却仍在船骸显示");
        Require(!FindRenderer(visualRoot, "CarriedWreckPart").enabled, $"{part} 已固定却仍在玩家手中显示");
        Require(!FindRenderer(visualRoot, $"StagedAtShelter_{part}").enabled, $"{part} 已固定却仍在住所施工位显示");
        Require(!FindRenderer(visualRoot, $"StagedAtBoat_{part}").enabled, $"{part} 已固定却仍在船施工位显示");
    }

    private static void RequireSingleVisibleOwner(
        Transform root,
        TideIslandSalvagePart part,
        TideIslandSalvageDestination expectedDestination)
    {
        Transform visualRoot = root.Find("GeneratedBarrenIslandRoot");
        Require(visualRoot != null, "岩礁岛表现根节点不存在");

        string sourceName = part == TideIslandSalvagePart.HullPlank ? "SalvageHullPlank" :
            part == TideIslandSalvagePart.Sailcloth ? "SalvageSailcloth" : "SalvageRivetedPlate";
        SpriteRenderer source = FindRenderer(visualRoot, sourceName);
        SpriteRenderer carried = FindRenderer(visualRoot, "CarriedWreckPart");
        SpriteRenderer shelter = FindRenderer(visualRoot, $"StagedAtShelter_{part}");
        SpriteRenderer boat = FindRenderer(visualRoot, $"StagedAtBoat_{part}");

        Require(!source.enabled && !carried.enabled, $"{part} 在原船骸或玩家手中仍有重复显示");
        Require(shelter.enabled == (expectedDestination == TideIslandSalvageDestination.ShelterStaging),
            $"{part} 的住所施工位显示归属错误");
        Require(boat.enabled == (expectedDestination == TideIslandSalvageDestination.EscapeBoatStaging),
            $"{part} 的逃生船施工位显示归属错误");
    }

    private static SpriteRenderer FindRenderer(Transform visualRoot, string childName)
    {
        Transform child = visualRoot.Find(childName);
        Require(child != null, $"缺少表现节点 {childName}");
        SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
        Require(renderer != null, $"表现节点 {childName} 缺少 SpriteRenderer");
        return renderer;
    }

    private static string ProbeIslandContextPriority()
    {
        TideIslandContextAction carriedAtBoat = TideIslandInteractionModel.Resolve(
            TideIslandSalvagePart.HullPlank,
            false,
            true,
            false,
            false);
        TideIslandContextAction emptyAtBoat = TideIslandInteractionModel.Resolve(
            TideIslandSalvagePart.None,
            false,
            true,
            false,
            false);
        TideIslandContextAction carriedAtCistern = TideIslandInteractionModel.Resolve(
            TideIslandSalvagePart.Sailcloth,
            false,
            false,
            false,
            true);
        TideIslandContextAction emptyAtCistern = TideIslandInteractionModel.Resolve(
            TideIslandSalvagePart.None,
            false,
            false,
            false,
            true);

        Require(carriedAtBoat == TideIslandContextAction.StageAtEscapeBoat,
            "携带船骸原物时泊船绳抢走了施工位交互");
        Require(emptyAtBoat == TideIslandContextAction.None,
            "空手经过船边时岩礁岛错误吞掉泊船交互");
        Require(carriedAtCistern == TideIslandContextAction.None,
            "携带大型原物时仍能顺手饮水或复制操作");
        Require(emptyAtCistern == TideIslandContextAction.DrinkFromCistern,
            "空手靠近蓄水池时无法饮水");
        return "交互优先级=施工位>绳船/携物禁饮水";
    }

    private static string ProbeSalvageMaterialCommit()
    {
        TideMaterialBundle hullNeeds = TideRepairRecipeModel.GetMaterialNeeds(
            TideRepairTarget.Hull,
            0);
        int hullSelection = TideSalvageMaterialModel.SelectMinimumParts(
            TideSalvageMaterialModel.HullPlankBit |
            TideSalvageMaterialModel.SailclothBit |
            TideSalvageMaterialModel.RivetedPlateBit,
            new TideMaterialBundle(),
            hullNeeds);
        Require(hullSelection == TideSalvageMaterialModel.HullPlankBit,
            "补船体没有选择正好满足木料与短索的单块船板");

        int stockOnlySelection = TideSalvageMaterialModel.SelectMinimumParts(
            TideSalvageMaterialModel.HullPlankBit,
            hullNeeds,
            hullNeeds);
        Require(stockOnlySelection == 0, "库存已足时仍误吞可见船骸原物");

        Require(TideRepairRecipeModel.GetArrivalRepairTarget(
                TideIslandSalvagePart.RivetedPlate,
                TideIslandSalvageUse.Shelter) == TideRepairTarget.Cistern &&
            TideRepairRecipeModel.GetArrivalRepairTarget(
                TideIslandSalvagePart.RivetedPlate,
                TideIslandSalvageUse.EscapeBoat) == TideRepairTarget.Cabin,
            "铆接板的住所/逃生船首件去向没有由纯配方模型唯一决定");
        TideMaterialBundle cabinNeeds = TideRepairRecipeModel.GetMaterialNeeds(
            TideRepairTarget.Cabin,
            0);
        int plateAlone = TideSalvageMaterialModel.SelectMinimumParts(
            TideSalvageMaterialModel.RivetedPlateBit,
            new TideMaterialBundle(),
            cabinNeeds);
        Require(plateAlone == TideSalvageMaterialModel.RivetedPlateBit,
            "开场铆接板放到船边仍不能独立完成金属舱盖与排水口第一阶段");
        TideMaterialBundle reinforcedCabinNeeds = TideRepairRecipeModel.GetMaterialNeeds(
            TideRepairTarget.Cabin,
            1);
        Require(reinforcedCabinNeeds.Timber == 1 && reinforcedCabinNeeds.Metal == 1,
            "第二阶段船舱加固没有从纯金属舱盖过渡到木框加固");

        int curvedRibSelection = TideSalvageMaterialModel.SelectMinimumParts(
            TideSalvageMaterialModel.HeavyKeelRibPieceABit |
            TideSalvageMaterialModel.HeavyKeelRibPieceBBit,
            new TideMaterialBundle(),
            hullNeeds);
        Require(curvedRibSelection == TideSalvageMaterialModel.HeavyKeelRibPieceABit,
            "成形弯肋不能作为一处柱脚斜撑或船体肋骨的最小原物提交");
        return "原物最终固定=最少组合/库存足不误吞/铆板首修不生木/弯肋保形";
    }

    private static string ProbeRepairWorkPhases()
    {
        Require(TideRepairWorkPhaseModel.Evaluate(0.05f) == TideRepairWorkPhase.Inspect,
            "施工开始时跳过了检查");
        Require(TideRepairWorkPhaseModel.Evaluate(0.2f) == TideRepairWorkPhase.Clean,
            "检查后没有进入清理");
        Require(TideRepairWorkPhaseModel.Evaluate(0.42f) == TideRepairWorkPhase.TestFit,
            "清理后没有进入试装");
        Require(TideRepairWorkPhaseModel.Evaluate(0.66f) == TideRepairWorkPhase.Fasten,
            "试装后没有进入固定");
        Require(TideRepairWorkPhaseModel.Evaluate(0.9f) == TideRepairWorkPhase.Seal,
            "最终提交前没有密封与复核");
        return "施工=检查>清理>试装>固定>密封";
    }

    private static string ProbeRepairWorkSession()
    {
        TideRepairWorkController work = new TideRepairWorkController();
        work.Begin(TideRepairTarget.Stilt);
        Require(work.PendingChoice == TideRepairTarget.Stilt &&
            work.Step == (int)TideRepairWorkPhase.Inspect &&
            !work.Active && !work.ChoiceApplied,
            "维修会话开工时没有原子进入目标和检查阶段");

        bool earlyFinished = work.Advance(1f, 4f);
        float pausedProgress = work.Progress01;
        work.Pause();
        Require(!earlyFinished && !work.Active &&
            Mathf.Abs(pausedProgress - 0.25f) <= 0.001f,
            "维修会话松手后没有保留已完成的现实工时");

        work.Advance(1f, 4f);
        Require(work.Step == (int)TideRepairWorkPhase.TestFit &&
            Mathf.Abs(work.Progress01 - 0.5f) <= 0.001f,
            "维修会话恢复后没有从原进度继续进入试装");

        work.Begin(TideRepairTarget.Hull);
        Require(work.PendingChoice == TideRepairTarget.Hull &&
            work.Progress01 <= 0.001f &&
            work.Step == (int)TideRepairWorkPhase.Inspect,
            "明确切换真实施工点后仍串用了上一个部位的半成品进度");

        bool workFinished = work.Advance(4f, 4f);
        Require(workFinished && !work.ChoiceApplied,
            "工序刚完成就绕过材料与世界 owner 提前生成维修收益");
        work.Complete();
        Require(work.ChoiceApplied && !work.Active &&
            work.Progress01 >= 0.999f &&
            work.Step == (int)TideRepairWorkPhase.Seal,
            "材料提交后维修会话没有封账到密封阶段");
        return $"施工会话=暂停{pausedProgress:P0}/换点重开/提交后封账";
    }

    private static string ProbeHeavyWreckTidalLift()
    {
        TideHeavyWreckState single = TideHeavyWreckTidalLiftModel.CreateInitial();
        single = TideHeavyWreckTidalLiftModel.TrySecurePoint(single, 0, 0.1f, out bool firstSecured);
        Require(firstSecured, "低潮无法系住第一处重型残骸绳箍");
        for (int i = 0; i < 12; i++)
        {
            single = TideHeavyWreckTidalLiftModel.AdvanceNatural(
                single, 0.1f, 1f, 0.95f, 0.08f, false);
        }
        Require(single.SecuredPointMask == 0,
            "单点系缆在强流中没有扭转载荷或失效风险");

        TideHeavyWreckState secured = TideHeavyWreckTidalLiftModel.CreateInitial();
        secured = TideHeavyWreckTidalLiftModel.TrySecurePoint(secured, 0, 0.1f, out _);
        secured = TideHeavyWreckTidalLiftModel.TrySecurePoint(secured, 1, 0.1f, out bool secondSecured);
        Require(secondSecured && secured.SecuredPointMask == 3,
            "低潮双点系稳没有建立唯一重物 owner");

        TideHeavyWreckState groundedPull = TideHeavyWreckTidalLiftModel.AdvanceNatural(
            secured, 4f, 0.1f, 0f, 0f, true);
        Require(groundedPull.TowProgress01 <= 0.001f,
            "未涨潮时重物在没有浮力的情况下被拖过岩床");

        TideHeavyWreckState peakCurrent = secured;
        TideHeavyWreckState slackCurrent = secured;
        for (int i = 0; i < 80; i++)
        {
            peakCurrent = TideHeavyWreckTidalLiftModel.AdvanceNatural(
                peakCurrent, 0.05f, 1f, 0.75f, 0.04f, true);
            slackCurrent = TideHeavyWreckTidalLiftModel.AdvanceNatural(
                slackCurrent, 0.05f, 1f, 0.02f, 0.01f, true);
        }
        Require(slackCurrent.TowProgress01 >= peakCurrent.TowProgress01 + 0.35f,
            "平流窗口没有显著优于急流硬拉");
        for (int i = 0; i < 180 && slackCurrent.TowProgress01 < 0.999f; i++)
        {
            slackCurrent = TideHeavyWreckTidalLiftModel.AdvanceNatural(
                slackCurrent, 0.05f, 1f, 0.01f, 0f, true);
        }
        Require(slackCurrent.Phase == TideHeavyWreckPhase.AtCradleAfloat,
            "涨潮平流时没有把重物带到作业架");
        slackCurrent = TideHeavyWreckTidalLiftModel.AdvanceNatural(
            slackCurrent, 0.1f, 0.05f, 0f, 0f, false);
        Require(slackCurrent.Phase == TideHeavyWreckPhase.RecoveredIntact,
            "退潮后重物没有在作业架落底");

        slackCurrent = TideHeavyWreckTidalLiftModel.TryBeginWork(slackCurrent, out bool exposing);
        slackCurrent = TideHeavyWreckTidalLiftModel.AdvanceWork(
            slackCurrent, TideHeavyWreckTidalLiftModel.ExposeJointSeconds + 0.1f, true);
        Require(exposing && slackCurrent.Phase == TideHeavyWreckPhase.JointsExposed,
            "原物落底后没有先显露接缝");
        slackCurrent = TideHeavyWreckTidalLiftModel.TryBeginWork(slackCurrent, out bool separating);
        slackCurrent = TideHeavyWreckTidalLiftModel.AdvanceWork(
            slackCurrent, TideHeavyWreckTidalLiftModel.SeparateSeconds + 0.1f, true);
        Require(separating && slackCurrent.Phase == TideHeavyWreckPhase.Separated,
            "显缝后没有原子切换为三件拆解 owner");

        TideHeavyWreckPieceOwnershipState pieces =
            TideHeavyWreckPieceOwnershipModel.CreateSeparated();
        pieces = TideHeavyWreckPieceOwnershipModel.TryPickUp(
            pieces,
            TideHeavyWreckPiece.PieceA,
            out bool pickedA);
        Require(pickedA && pieces.CarriedPiece == TideHeavyWreckPiece.PieceA &&
            pieces.PieceBOwner == TideHeavyWreckPieceOwner.Worksite,
            "拖走左弯肋时右弯肋没有继续留在原作业位");
        pieces = TideHeavyWreckPieceOwnershipModel.TryStageCarried(
            pieces,
            TideIslandSalvageDestination.ShelterStaging,
            out TideHeavyWreckPiece stagedA);
        Require(stagedA == TideHeavyWreckPiece.PieceA &&
            TideHeavyWreckPieceOwnershipModel.GetStagedMask(
                pieces,
                TideIslandSalvageDestination.ShelterStaging) ==
            TideSalvageMaterialModel.HeavyKeelRibPieceABit,
            "左弯肋没有从拖行 owner 转移到住所施工位");
        pieces = TideHeavyWreckPieceOwnershipModel.TryIntegrate(
            pieces,
            TideHeavyWreckPiece.PieceA,
            TideIslandSalvageDestination.ShelterStaging,
            out bool integratedA);
        Require(integratedA &&
            pieces.PieceAOwner == TideHeavyWreckPieceOwner.IntegratedIntoShelter &&
            TideHeavyWreckPieceOwnershipModel.GetStagedMask(
                pieces,
                TideIslandSalvageDestination.ShelterStaging) == 0,
            "最终固定后左弯肋仍重复留在施工位");
        return $"借潮=双系>浮起>平流拖{peakCurrent.TowProgress01:P0}/{slackCurrent.TowProgress01:P0}>退潮落架>拆件拖运>最终固定";
    }

    private static string ProbeMooringRope()
    {
        GameObject root = new GameObject("TideMooringRopeRuntimeProbe");
        Texture2D probeTexture = null;
        Sprite probeSprite = null;
        try
        {
            TideMooringRopeController rope = root.AddComponent<TideMooringRopeController>();
            rope.ResetRuntime(1.08f);

            TideMooringRopeInteractionResult start = rope.HandleInteraction(
                true, true, true, false, 0f);
            for (int i = 0; i < 28; i++)
            {
                rope.HandleInteraction(true, false, true, false, 0.02f);
            }
            TideMooringRopeInteractionResult release = rope.HandleInteraction(
                true, false, false, true, 0f);
            Require(start.Handled &&
                start.Outcome == TideMooringRopeInteractionOutcome.SwingStarted,
                "新泊位组件没有独占甩绳起手输入");
            Require(release.Outcome == TideMooringRopeInteractionOutcome.ThrowAttached &&
                rope.Phase == TideMooringRopePhase.Attached,
                "正确松手时新泊位组件没有套住船艉");

            probeTexture = new Texture2D(1, 1);
            probeTexture.SetPixel(0, 0, Color.white);
            probeTexture.Apply();
            probeSprite = Sprite.Create(
                probeTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                100f);
            SpriteRenderer[] segments = new SpriteRenderer[4];
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = new GameObject($"ProbeSegment_{i}").AddComponent<SpriteRenderer>();
                segments[i].transform.SetParent(root.transform, false);
                segments[i].sprite = probeSprite;
                segments[i].sortingOrder = 13;
            }
            SpriteRenderer ropeEnd = new GameObject("ProbeRopeEnd").AddComponent<SpriteRenderer>();
            ropeEnd.transform.SetParent(root.transform, false);
            ropeEnd.sprite = probeSprite;
            ropeEnd.sortingOrder = 14;
            Vector2 expectedTie = new Vector2(1.08f, 0.24f);
            rope.UpdatePresentation(
                segments,
                ropeEnd,
                true,
                Vector2.zero,
                new Vector2(-0.1f, 0.12f),
                expectedTie,
                probeSprite,
                0f);
            bool presentationBound = ropeEnd.enabled &&
                Vector2.Distance(ropeEnd.transform.localPosition, expectedTie) <= 0.001f;
            for (int i = 0; i < segments.Length; i++)
            {
                presentationBound &= segments[i].enabled &&
                    segments[i].transform.localScale.x > 0.001f;
            }
            Require(presentationBound,
                "泊位组件推进了状态，但没有把同一状态绑定到连续绳段与船艉端点");

            TideMooringRopeController looseRope = new GameObject(
                "TideLooseMooringRopePresentationProbe").AddComponent<TideMooringRopeController>();
            looseRope.transform.SetParent(root.transform, false);
            looseRope.ResetRuntime(1.08f);
            Vector2 dockCoilPoint = new Vector2(-0.1f, 0.12f);
            looseRope.UpdatePresentation(
                segments,
                ropeEnd,
                true,
                Vector2.zero,
                dockCoilPoint,
                expectedTie,
                probeSprite,
                0f);
            bool looseCoilGrounded = ropeEnd.enabled &&
                Vector2.Distance(ropeEnd.transform.localPosition, dockCoilPoint) <= 0.38f &&
                Vector2.Distance(ropeEnd.transform.localPosition, expectedTie) >= 0.65f;
            for (int i = 0; i < segments.Length; i++)
            {
                looseCoilGrounded &= segments[i].enabled &&
                    Mathf.Abs(segments[i].transform.localPosition.y - dockCoilPoint.y) <= 0.13f;
            }
            Require(looseCoilGrounded,
                "Loose 引缆仍然不可见，或被误画成已经连接船艉的绳");

            TideMooringRopeEnvironmentOutcome finalOutcome = TideMooringRopeEnvironmentOutcome.None;
            for (int i = 0; i < 1200 && rope.Phase != TideMooringRopePhase.Secured; i++)
            {
                bool reelWithoutOverstrain = rope.State.Tension01 < 0.82f;
                rope.HandleInteraction(true, false, reelWithoutOverstrain, false, 0f);
                TideMooringRopeEnvironmentOutcome outcome = rope.AdvanceEnvironment(
                    0.02f, 0.05f, 0f, false);
                if (outcome != TideMooringRopeEnvironmentOutcome.None)
                {
                    finalOutcome = outcome;
                }
            }

            Require(rope.Phase == TideMooringRopePhase.Secured,
                "收绳没有通过有限张力把船拉回泊位");
            Require(finalOutcome == TideMooringRopeEnvironmentOutcome.BoatSecured,
                "船靠稳时新泊位组件没有向主控制器发出一次性结果");
            Require(Mathf.Abs(rope.BoatOffsetMeters) <=
                TideMooringRopeModel.SecuredOffsetMeters + 0.02f,
                "固定后船仍离跳板过远");
            TideMooringRopeState edge = TideMooringRopeModel.CreateLoose(
                TideMooringRopeModel.MaximumBoatDriftMeters);
            edge = TideMooringRopeModel.BeginSwing(edge);
            edge = TideMooringRopeModel.AdvanceSwing(edge, 0.5f, true);
            edge = TideMooringRopeModel.ReleaseThrow(edge);
            Require(edge.Phase == TideMooringRopePhase.Attached,
                "船漂到自然边界后仍要求甩绳在数学峰值单帧松手");
            return $"绳相={rope.Phase}/离岸{rope.BoatOffsetMeters:F2}m/张力{rope.State.Tension01:F2}/组件结果={finalOutcome}";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            if (probeSprite != null)
            {
                UnityEngine.Object.DestroyImmediate(probeSprite);
            }
            if (probeTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(probeTexture);
            }
        }
    }

    private static string ProbeMooredBoatAccess()
    {
        Vector2 pier = Vector2.zero;
        TideMooringGangplankSample connected =
            TideMooredBoatAccessModel.EvaluateGangplank(pier, new Vector2(1.1f, 0.18f));
        TideMooringGangplankSample steep =
            TideMooredBoatAccessModel.EvaluateGangplank(pier, new Vector2(0.42f, 0.72f));
        TideMooringGangplankSample disconnected =
            TideMooredBoatAccessModel.EvaluateGangplank(pier, new Vector2(2.7f, 0.1f));
        Require(connected.CanSpan && connected.IsWalkable,
            "可见跳板已经平缓接上船艉，却仍被判为不可通行");
        Require(steep.CanSpan && !steep.IsWalkable && disconnected.CanSpan == false,
            "跳板过陡与根本够不到仍没有形成两种不同物理结果");

        TideMooredBoatAccessSample unsecured = TideMooredBoatAccessModel.Evaluate(
            TideMooringRopePhase.Loose, connected, 0.04f, 0.08f, false, false);
        TideMooredBoatAccessSample slack = TideMooredBoatAccessModel.Evaluate(
            TideMooringRopePhase.Secured, connected, 0.04f, 0.08f, false, false);
        TideMooredBoatAccessSample sameHeightFastCurrent = TideMooredBoatAccessModel.Evaluate(
            TideMooringRopePhase.Secured, connected, 0.86f, 0.08f, false, false);
        TideMooredBoatAccessSample roughSea = TideMooredBoatAccessModel.Evaluate(
            TideMooringRopePhase.Secured, connected, 0.04f, 0.92f, false, false);
        TideMooredBoatAccessSample night = TideMooredBoatAccessModel.Evaluate(
            TideMooringRopePhase.Secured, connected, 0.04f, 0.08f, true, false);
        TideMooredBoatAccessSample finalNight = TideMooredBoatAccessModel.Evaluate(
            TideMooringRopePhase.Secured, connected, 0.04f, 0.08f, true, true);

        Require(unsecured.BlockReason == TideMooredBoatAccessBlockReason.RopeUnsecured,
            "未系稳的船仍能绕过甩绳与收绳直接登船");
        Require(slack.IsOpen,
            "绳已固定、跳板接通且处于平流时仍不能登船");
        Require(sameHeightFastCurrent.BlockReason == TideMooredBoatAccessBlockReason.StrongCurrent,
            "相同水位下真实横流变快却没有关闭登船窗口");
        Require(roughSea.BlockReason == TideMooredBoatAccessBlockReason.RoughSea,
            "局部浪况已经危险却仍能从跳板登船");
        Require(night.BlockReason == TideMooredBoatAccessBlockReason.Night && finalNight.IsOpen,
            "普通夜航与剧情最终离开没有保持既定差异");
        return $"登船=系稳>{connected.LengthMeters:F2}m/{connected.SlopeDegrees:F1}°跳板>平流，流急/破浪/断板分别拦截";
    }

    private static string ProbeSailingDynamics()
    {
        TideBoatConditionPerformanceSample boatPerformance = CreateBoatPerformance();
        TideSailboatDynamicsState right = CreateBoatState();
        TideSailboatDynamicsState left = CreateBoatState();
        for (int i = 0; i < 300; i++)
        {
            right = TideSailboatDynamicsModel.Advance(right, 0.02f, 1f, 0f, 0f, 0f, 0f, -1.18f, 0f, 0.1f, boatPerformance);
            left = TideSailboatDynamicsModel.Advance(left, 0.02f, -1f, 0f, 0f, 0f, 0f, -1.18f, 0f, 0.1f, boatPerformance);
        }

        Require(Mathf.Abs(right.HorizontalVelocity + left.HorizontalVelocity) <= 0.025f,
            "静风时左右手动推进不对称");

        TideSailboatDynamicsState windDriven = CreateBoatState();
        for (int i = 0; i < 240; i++)
        {
            windDriven = TideSailboatDynamicsModel.Advance(
                windDriven, 0.02f, 0f, 1f, 0f, 0.8f, 0f, -1.18f, 0f, 0.15f, boatPerformance);
        }
        Require(windDriven.HorizontalVelocity > 0.2f, "升帆后没有读取有符号风场");

        TideSailboatDynamicsState localBreakerDriven = CreateBoatState();
        for (int i = 0; i < 80; i++)
        {
            localBreakerDriven = TideSailboatDynamicsModel.Advance(
                localBreakerDriven,
                0.02f,
                0f,
                0f,
                0f,
                0f,
                0.58f,
                -1.18f,
                0f,
                0.9f,
                boatPerformance);
        }
        Require(localBreakerDriven.HorizontalVelocity > 0.14f,
            "权威海况提供局部浪推力后，帆船动力仍把它丢在当前耦合之外");

        TideSailboatDynamicsState neutral = CreateBoatState();
        TideSailboatDynamicsState trimmed = CreateBoatState();
        for (int i = 0; i < 160; i++)
        {
            neutral = TideSailboatDynamicsModel.Advance(neutral, 0.02f, 0f, 0f, 0f, 0f, 0f, -1.08f, 0.12f, 0.4f, boatPerformance);
            trimmed = TideSailboatDynamicsModel.Advance(trimmed, 0.02f, 0f, 0f, 0.38f, 0f, 0f, -1.08f, 0.12f, 0.4f, boatPerformance);
        }
        Require(Mathf.Abs(trimmed.PitchDegrees) < Mathf.Abs(neutral.PitchDegrees),
            "移动压舱物不能抵消当前浪坡纵倾");
        return $"静风左右{left.HorizontalVelocity:F2}/{right.HorizontalVelocity:F2}m/s/顺风{windDriven.HorizontalVelocity:F2}/破浪推移{localBreakerDriven.HorizontalVelocity:F2}/压舱{neutral.PitchDegrees:F1}->{trimmed.PitchDegrees:F1}deg";
    }

    private static string ProbeBoatConditionPerformance()
    {
        TideBoatConditionPerformanceSample damaged = CreateBoatPerformance(1, 0, 0);
        TideBoatConditionPerformanceSample hullRepaired = CreateBoatPerformance(2, 0, 0);
        TideBoatConditionPerformanceSample sailRepaired = CreateBoatPerformance(1, 1, 0);
        TideBoatConditionPerformanceSample cabinRepaired = CreateBoatPerformance(1, 0, 1);

        Require(hullRepaired.BaseLeakRatePerSecond < damaged.BaseLeakRatePerSecond * 0.7f &&
            hullRepaired.HullSpeedMultiplier > damaged.HullSpeedMultiplier * 1.08f,
            "船壳维修没有同时降低真实漏率并提高可承受航速");
        Require(Mathf.Approximately(hullRepaired.SailTrimRatePerSecond, damaged.SailTrimRatePerSecond),
            "船壳维修越权改变了升降帆速度");
        Require(sailRepaired.SailDriveEfficiency01 >= damaged.SailDriveEfficiency01 + 0.2f &&
            sailRepaired.SailTrimRatePerSecond >= damaged.SailTrimRatePerSecond + 0.08f,
            "船帆维修没有改善真实风力利用和升降帆速度");
        Require(Mathf.Approximately(sailRepaired.BaseLeakRatePerSecond, damaged.BaseLeakRatePerSecond),
            "船帆维修越权堵住了船壳漏水");
        Require(cabinRepaired.BallastShiftRatePerSecond >= damaged.BallastShiftRatePerSecond + 0.1f &&
            cabinRepaired.BailRateMultiplier >= damaged.BailRateMultiplier + 0.2f &&
            cabinRepaired.BailingDragMultiplier < damaged.BailingDragMultiplier,
            "舱底维修没有改善压舱移动、舀水和操作时的动量保留");
        Require(Mathf.Approximately(cabinRepaired.SailDriveEfficiency01, damaged.SailDriveEfficiency01),
            "舱底维修越权提高了船帆效率");

        TideSailboatDynamicsState tornSail = CreateBoatState();
        TideSailboatDynamicsState soundSail = CreateBoatState();
        tornSail.SailRaised01 = 0f;
        soundSail.SailRaised01 = 0f;
        TideBoatConditionPerformanceSample tornSailPerformance = CreateBoatPerformance(2, 0, 1);
        TideBoatConditionPerformanceSample soundSailPerformance = CreateBoatPerformance(2, 2, 1);
        for (int step = 0; step < 150; step++)
        {
            tornSail = TideSailboatDynamicsModel.Advance(
                tornSail, 0.02f, 0f, 1f, 0f, 0.65f, 0f, -1.18f, 0f, 0.1f, tornSailPerformance);
            soundSail = TideSailboatDynamicsModel.Advance(
                soundSail, 0.02f, 0f, 1f, 0f, 0.65f, 0f, -1.18f, 0f, 0.1f, soundSailPerformance);
        }
        Require(soundSail.SailRaised01 >= tornSail.SailRaised01 + 0.25f &&
            soundSail.HorizontalVelocity >= tornSail.HorizontalVelocity + 0.2f,
            "修复后的船帆没有在真实积分器里更快升起并形成可辨认推进差");

        TideSailboatDynamicsState damagedHull = CreateBoatState();
        TideSailboatDynamicsState soundHull = CreateBoatState();
        for (int step = 0; step < 300; step++)
        {
            damagedHull = TideSailboatDynamicsModel.Advance(
                damagedHull, 0.02f, 0f, 0f, 0f, 0f, 0f, -1.18f, 0.12f, 0.8f,
                CreateBoatPerformance(1, 1, 1), 0.9f);
            soundHull = TideSailboatDynamicsModel.Advance(
                soundHull, 0.02f, 0f, 0f, 0f, 0f, 0f, -1.18f, 0.12f, 0.8f,
                CreateBoatPerformance(2, 1, 1), 0.9f);
        }
        Require(damagedHull.Ingress01 >= soundHull.Ingress01 + 0.02f,
            "船壳维修没有在真实越浪积分中形成可辨认的进水差");

        TideSailboatDynamicsState blockedCabin = CreateBoatState();
        TideSailboatDynamicsState clearedCabin = CreateBoatState();
        for (int step = 0; step < 80; step++)
        {
            blockedCabin = TideSailboatDynamicsModel.Advance(
                blockedCabin, 0.02f, 0f, 0f, 1f, 0f, 0f, -1.18f, 0f, 0f,
                CreateBoatPerformance(1, 0, 0));
            clearedCabin = TideSailboatDynamicsModel.Advance(
                clearedCabin, 0.02f, 0f, 0f, 1f, 0f, 0f, -1.18f, 0f, 0f,
                CreateBoatPerformance(1, 0, 2));
        }
        Require(clearedCabin.Ballast01 >= blockedCabin.Ballast01 + 0.25f,
            "清理修复舱底后，压舱物仍以同样迟缓的速度移动");

        return $"部件维修=船壳漏率{damaged.BaseLeakRatePerSecond:F3}->{hullRepaired.BaseLeakRatePerSecond:F3}/" +
            $"帆速{tornSail.SailRaised01:F2}->{soundSail.SailRaised01:F2}/" +
            $"进水{damagedHull.Ingress01:F2}->{soundHull.Ingress01:F2}/" +
            $"压舱{blockedCabin.Ballast01:F2}->{clearedCabin.Ballast01:F2}";
    }

    private static string ProbeVisibleWavePhysicalCoupling()
    {
        bool passed = TideAuthoritativeOceanModel.ProbeVisibleWaveCoupling(out string reason);
        Require(passed, $"可见破浪与局部物理未同源：{reason}");
        return $"可见破浪={reason}";
    }

    private static string ProbeSailingWaveHandling()
    {
        TideSailingWaveHandlingSample calm = TideSailingWaveHandlingModel.Evaluate(
            0f, 0.9f, 0.12f, -1f, 1f, 1.2f, 0.4f);
        TideSailingWaveHandlingSample prepared = TideSailingWaveHandlingModel.Evaluate(
            0.95f, 0.9f, 0.12f, 1f, 0.22f, 1.2f, 0.4f);
        TideSailingWaveHandlingSample exposed = TideSailingWaveHandlingModel.Evaluate(
            0.95f, 0.9f, 0.12f, -1f, 1f, 1.2f, 0.4f);
        Require(calm.Slamming01 <= 0.0001f && calm.IngressMultiplier <= 1.0001f,
            "没有局部浪接触时仍凭空产生拍击惩罚");
        Require(prepared.HandlingQuality01 >= 0.75f && exposed.HandlingQuality01 <= 0.25f,
            "收帆与顺坡压舱没有形成清晰可学的处理质量差");
        Require(exposed.Slamming01 >= prepared.Slamming01 + 0.42f,
            "同一浪头下错误帆量和压舱没有显著增加拍击");

        TideSailboatDynamicsState preparedBoat = CreateBoatState();
        preparedBoat.HorizontalVelocity = 1.2f;
        preparedBoat.SailRaised01 = 0.22f;
        preparedBoat.Ballast01 = 1f;
        TideSailboatDynamicsState exposedBoat = CreateBoatState();
        exposedBoat.HorizontalVelocity = 1.2f;
        exposedBoat.SailRaised01 = 1f;
        exposedBoat.Ballast01 = -1f;
        for (int i = 0; i < 120; i++)
        {
            preparedBoat = TideSailboatDynamicsModel.Advance(
                preparedBoat, 0.02f, 0f, 0f, 0f, 0f, 0.4f,
                -1.18f, 0.12f, 0.9f, CreateBoatPerformance(1, 2, 2), 0.95f);
            exposedBoat = TideSailboatDynamicsModel.Advance(
                exposedBoat, 0.02f, 0f, 0f, 0f, 0f, 0.4f,
                -1.18f, 0.12f, 0.9f, CreateBoatPerformance(1, 2, 2), 0.95f);
        }

        Require(preparedBoat.HorizontalVelocity >= exposedBoat.HorizontalVelocity + 0.16f,
            "正确处理可见浪没有保住可辨认的航速");
        Require(exposedBoat.Ingress01 >= preparedBoat.Ingress01 + 0.045f,
            "错误处理可见浪没有产生可辨认的额外舱水");
        return $"读浪=处理{prepared.HandlingQuality01:F2}/{exposed.HandlingQuality01:F2} " +
            $"拍击{prepared.Slamming01:F2}/{exposed.Slamming01:F2} " +
            $"速度{preparedBoat.HorizontalVelocity:F2}/{exposedBoat.HorizontalVelocity:F2} " +
            $"进水{preparedBoat.Ingress01:F2}/{exposedBoat.Ingress01:F2}";
    }

    private static string ProbeSailingReefClearance()
    {
        const float lowestWaterY = -2.82f;
        TideSailingReefSample lowTide = TideSailingReefModel.Evaluate(
            lowestWaterY, lowestWaterY, 0f, 0f, 0f, 1.2f);
        TideSailingReefSample firstHighTide = TideSailingReefModel.Evaluate(
            lowestWaterY, lowestWaterY + 1.11f, 0f, 0f, 0f, 1.2f);
        TideSailingReefSample loadedAtSameTide = TideSailingReefModel.Evaluate(
            lowestWaterY, lowestWaterY + 1.11f, 1f, 1f, 1.2f, 1.2f);
        TideSailingReefSample marginalSlowTow = TideSailingReefModel.Evaluate(
            lowestWaterY, lowestWaterY + 0.93f, 1f, 1f, 0.18f, 1.2f);
        TideSailingReefSample marginalFastTow = TideSailingReefModel.Evaluate(
            lowestWaterY, lowestWaterY + 0.93f, 1f, 1f, 1.2f, 1.2f);
        TideSailingReefSample deeperWindow = TideSailingReefModel.Evaluate(
            lowestWaterY, lowestWaterY + 1.3f, 0f, 0f, 0.18f, 1.2f);

        Require(lowTide.GroundsKeel && lowTide.ExposedRock01 > 0.8f,
            "最低潮时浅礁没有露出并阻挡船底");
        Require(!firstHighTide.GroundsKeel && firstHighTide.UnderKeelClearanceMeters > 0f,
            "同一浅礁在首个高潮窗仍然无条件封路");
        Require(loadedAtSameTide.BoatDraftMeters > firstHighTide.BoatDraftMeters &&
            loadedAtSameTide.UnderKeelClearanceMeters < firstHighTide.UnderKeelClearanceMeters,
            "舱水、拖载和高速没有真实增加吃水");
        Require(!marginalSlowTow.GroundsKeel && marginalFastTow.GroundsKeel,
            "边缘潮窗下减速与高速拖载没有形成不同船底净空");
        Require(deeperWindow.HasComfortableClearance,
            "更深潮窗仍没有提供可辨认的安全净空");

        const float reefX = 8f;
        bool groundedSegmentBlocked = TideSailingReefModel.SegmentEntersGroundedReef(
            reefX - 1.1f, reefX - 0.2f, reefX, lowTide);
        bool clearSegmentPasses = !TideSailingReefModel.SegmentEntersGroundedReef(
            reefX - 1.1f, reefX - 0.2f, reefX, firstHighTide);
        Require(groundedSegmentBlocked && clearSegmentPasses,
            "位移段没有按船底净空决定是否穿过固定礁脊");
        float groundedInPlace = TideSailingReefModel.ConstrainOutsideGroundedReef(
            reefX, reefX + 0.2f, reefX);
        Require(Mathf.Abs(groundedInPlace - reefX) <= 0.001f,
            "船在礁顶遇到退潮时被瞬移到礁石边缘");
        Require(!TideSailingReefModel.ShouldDamageHull(lowTide, 0.3f) &&
            TideSailingReefModel.ShouldDamageHull(lowTide, 0.9f),
            "轻触搁浅与高速撞击没有形成不同后果");

        return $"浅礁净空低/高/边缘慢拖快拖={lowTide.UnderKeelClearanceMeters:F2}/" +
            $"{firstHighTide.UnderKeelClearanceMeters:F2}/" +
            $"{marginalSlowTow.UnderKeelClearanceMeters:F2}/{marginalFastTow.UnderKeelClearanceMeters:F2}m";
    }

    private static string ProbeSailingReefRuntime()
    {
        GameObject root = new GameObject("TideSailingReefRuntimeProbe");
        Texture2D probeTexture = null;
        Sprite probeSprite = null;
        try
        {
            TideSailingReefController reef = root.AddComponent<TideSailingReefController>();
            const float lowestWaterY = -2.82f;
            const float reefCenterX = 2f;
            TideSailingReefSample grounded = TideSailingReefModel.Evaluate(
                lowestWaterY,
                lowestWaterY,
                0f,
                0f,
                0.2f,
                1.2f);
            reef.ResetRuntime();
            TideSailingReefMovementResult lightContact = reef.ResolveMovement(
                1f,
                3f,
                0.2f,
                reefCenterX,
                grounded);
            Require(lightContact.ContactedReef && !lightContact.DamagesHull &&
                lightContact.ResolvedBoatX <= reefCenterX - TideSailingReefModel.ReefHalfWidthMeters + 0.001f,
                "浅礁运行组件没有在轻触时约束连续位移");

            reef.ResetRuntime();
            TideSailingReefMovementResult hardContact = reef.ResolveMovement(
                1f,
                3f,
                0.9f,
                reefCenterX,
                grounded);
            TideSailingReefMovementResult repeatedContact = reef.ResolveMovement(
                1f,
                3f,
                0.9f,
                reefCenterX,
                grounded);
            float strikeCooldown = reef.StrikeCooldownRemainingSeconds;
            Require(hardContact.DamagesHull && !repeatedContact.DamagesHull &&
                strikeCooldown > 3f,
                "浅礁运行组件没有把高速撞击限制为一次性船体后果");

            TideSailingReefSample highWater = TideSailingReefModel.Evaluate(
                lowestWaterY,
                lowestWaterY + 1.11f,
                0f,
                0f,
                0.18f,
                1.2f);
            reef.ResetRuntime();
            TideSailingReefMovementResult clearPass = reef.ResolveMovement(
                1f,
                3f,
                0.18f,
                reefCenterX,
                highWater);
            Require(!clearPass.ContactedReef && clearPass.NewlyPassedOutbound && reef.PassedOutbound,
                "高潮净空成立后仍不能按真实位移越过浅礁");

            probeTexture = new Texture2D(1, 1);
            probeTexture.SetPixel(0, 0, Color.white);
            probeTexture.Apply();
            probeSprite = Sprite.Create(
                probeTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                100f);
            SpriteRenderer rock = new GameObject("ProbeReefRock").AddComponent<SpriteRenderer>();
            SpriteRenderer foam = new GameObject("ProbeReefFoam").AddComponent<SpriteRenderer>();
            rock.transform.SetParent(root.transform, false);
            foam.transform.SetParent(root.transform, false);
            TideOceanSample ocean = new TideOceanSample(-1.08f, 0.08f, 0.2f, 0.5f);
            reef.UpdatePresentation(
                rock,
                foam,
                true,
                new Vector2(0f, -0.73f),
                grounded,
                ocean,
                2f,
                probeSprite,
                probeSprite);
            float presentedRockWidth = rock.sprite.bounds.size.x * rock.transform.localScale.x;
            Require(rock.enabled && foam.enabled &&
                Mathf.Abs(presentedRockWidth - TideSailingReefModel.ReefHalfWidthMeters * 2f) <= 0.001f,
                "浅礁运行组件没有把同一净空样本绑定到岩脊和碎浪表现");

            return $"礁组件=轻触停/重撞一次/高潮越过/物理表现同源，冷却{strikeCooldown:F1}s";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            if (probeSprite != null)
            {
                UnityEngine.Object.DestroyImmediate(probeSprite);
            }
            if (probeTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(probeTexture);
            }
        }
    }

    private static string ProbeSailingSalvageRuntime()
    {
        GameObject root = new GameObject("TideSailingSalvageRuntimeProbe");
        try
        {
            TideSailingSalvageController salvage = root.AddComponent<TideSailingSalvageController>();
            TideOceanSample calmOcean = new TideOceanSample(-1.1f, 0.03f, 0.18f, 0.24f);
            salvage.ResetRuntime(4.4f, new Vector2(5f, -0.9f));
            float driftStartX = salvage.WorldX;
            salvage.Advance(
                1f,
                false,
                TideSailingSalvageAttachmentPhase.Free,
                new Vector2(5f, -0.9f),
                -1.1f,
                4.4f,
                calmOcean,
                0.42f,
                0f,
                0f,
                0f,
                0f,
                0f);
            Require(salvage.WorldX > driftStartX && salvage.Velocity > 0f,
                "漂木运行组件没有读取统一海流形成连续漂移");

            salvage.WorldX = 4.4f;
            salvage.Velocity = 0.2f;
            TideSailingSalvageThrowResult bowSide = salvage.BeginThrow(
                new Vector2(4f, -0.9f),
                -1.1f,
                1.38f,
                0.58f,
                0.2f);
            Require(!bowSide.Started && bowSide.Failure == TideSailingSalvageThrowFailure.AheadOfStern,
                "漂物仍在船艏时抛钩没有被真实船体关系拒绝");

            TideSailingSalvageThrowResult throwStarted = salvage.BeginThrow(
                new Vector2(5f, -0.9f),
                -1.1f,
                1.38f,
                0.58f,
                0.2f);
            salvage.Advance(
                0.16f,
                true,
                TideSailingSalvageAttachmentPhase.Free,
                new Vector2(5f, -0.9f),
                -1.1f,
                4.4f,
                calmOcean,
                0f,
                0f,
                0f,
                0f,
                0.2f,
                0.2f);
            float partialThrow = salvage.Throw01;
            TideSailingSalvageAdvanceResult retracted = salvage.Advance(
                0.24f,
                false,
                TideSailingSalvageAttachmentPhase.Free,
                new Vector2(5f, -0.9f),
                -1.1f,
                4.4f,
                calmOcean,
                0f,
                0f,
                0f,
                0f,
                0.2f,
                0.2f);
            Require(throwStarted.Started && partialThrow > 0f && partialThrow < 1f &&
                retracted.Outcome == TideSailingSalvageAdvanceOutcome.ThrowRetracted,
                "连续抛钩松手后没有沿原绳路收回");

            salvage.BeginThrow(
                new Vector2(5f, -0.9f),
                -1.1f,
                1.38f,
                0.58f,
                0.2f);
            TideSailingSalvageAdvanceResult attached = salvage.Advance(
                0.4f,
                true,
                TideSailingSalvageAttachmentPhase.Free,
                new Vector2(5f, -0.9f),
                -1.1f,
                4.4f,
                calmOcean,
                0f,
                0f,
                0f,
                0f,
                0.2f,
                0.2f);
            Require(attached.Outcome == TideSailingSalvageAdvanceOutcome.HookAttached,
                "钩头抵达漂木后没有产生唯一附着事件");

            TideSailingSalvageAdvanceResult hauling = default;
            for (int i = 0; i < 40 && hauling.Outcome != TideSailingSalvageAdvanceOutcome.Secured; i++)
            {
                float matchedVelocity = salvage.Velocity;
                hauling = salvage.Advance(
                    0.1f,
                    true,
                    TideSailingSalvageAttachmentPhase.Hooking,
                    new Vector2(5f, -0.9f),
                    -1.1f,
                    4.4f,
                    calmOcean,
                    0f,
                    0f,
                    0f,
                    0f,
                    matchedVelocity,
                    matchedVelocity);
            }
            Require(hauling.Outcome == TideSailingSalvageAdvanceOutcome.Secured &&
                salvage.HookProgress01 >= TideContinuousSalvageModel.SecuredProgress01,
                "匹配流速后持续收绳仍不能把漂木收妥到船艉");

            salvage.ResetRuntime(5.8f, new Vector2(3.4f, -0.9f));
            salvage.Velocity = 2f;
            salvage.HookProgress01 = 0.2f;
            salvage.InitialRopeLength = 0.35f;
            float strainedX = salvage.WorldX;
            TideSailingSalvageAdvanceResult detached = default;
            for (int i = 0; i < 6 && detached.Outcome != TideSailingSalvageAdvanceOutcome.Detached; i++)
            {
                detached = salvage.Advance(
                    0.1f,
                    false,
                    TideSailingSalvageAttachmentPhase.Hooking,
                    new Vector2(3.4f, -0.9f),
                    -1.1f,
                    4.4f,
                    calmOcean,
                    0f,
                    0f,
                    1f,
                    0f,
                    -2f,
                    -2f);
            }
            Require(detached.Outcome == TideSailingSalvageAdvanceOutcome.Detached &&
                Mathf.Abs(salvage.WorldX - strainedX) < 0.8f &&
                Mathf.Abs(salvage.WorldX - 4.4f) > 0.25f,
                "绳索过载脱钩后漂木没有保留失手处的真实位置");

            return $"打捞组件=随流漂/艏侧拒绝/抛钩可撤/匹配收妥/过载原位脱钩，终点{salvage.WorldX:F2}m";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static string ProbeStormRescue()
    {
        Require(!TideStormRescueModel.ShouldReleaseCargo(false, 0.08f, 0.12f),
            "刚漫脚的浅水就把仍在搁架上的暴潮物资凭空释放了");
        Require(TideStormRescueModel.ShouldReleaseCargo(false, 0.34f, 0.65f),
            "有明显冲击的膝下积水仍没有打坏低层搁架");

        TideStormRescueItemState water = TideStormRescueModel.Create(TideStormRescueItemKind.DrinkingWater);
        TideStormRescueItemState chart = TideStormRescueModel.Create(TideStormRescueItemKind.LighthouseChart);
        for (int i = 0; i < 120; i++)
        {
            water = TideStormRescueModel.Advance(water, 0.02f, 0.7f, 0.65f, false);
            chart = TideStormRescueModel.Advance(chart, 0.02f, 0.7f, 0.65f, false);
        }
        Require(chart.WashoutProgress01 > water.WashoutProgress01,
            "高浮力海图没有比低浮力水桶更快被带走");

        TideStormRescueItemState absent = TideStormRescueModel.Create(TideStormRescueItemKind.BoatMaterial);
        absent.Present = false;
        TideStormRescueItemState absentAfterFlood = TideStormRescueModel.Advance(
            absent, 12f, 0.7f, 0.65f, false);
        Require(absentAfterFlood.WashoutProgress01 <= 0f && !absentAfterFlood.Lost,
            "不存在的暴潮物资仍被水流推进或记成损失");

        TideStormRescueItemState secured = TideStormRescueModel.Create(TideStormRescueItemKind.BoatMaterial);
        for (int i = 0; i < 720 && !secured.Secured; i++)
        {
            secured = TideStormRescueModel.Advance(secured, 0.02f, 0.35f, 0.3f, true);
        }
        Require(secured.Secured && !secured.Lost, "持续固定物资后仍被判定冲失");
        return $"冲失水桶/海图={water.WashoutProgress01:F2}/{chart.WashoutProgress01:F2}/固定={secured.Secured}";
    }

    private static string ProbeStormRescueRuntime()
    {
        GameObject root = new GameObject("TideStormRescueRuntimeProbe");
        try
        {
            TideStormRescueController rescue = root.AddComponent<TideStormRescueController>();
            rescue.ResetRuntime();
            rescue.SetItemPresent(TideStormRescueItemKind.DrinkingWater, true);
            rescue.SetItemPresent(TideStormRescueItemKind.LighthouseChart, true);

            bool heldBeforeRelease = rescue.TryHoldItem((int)TideStormRescueItemKind.DrinkingWater);
            TideStormRescueAdvanceResult shallow = rescue.Advance(1f, 0.08f, 0.12f);
            Require(!heldBeforeRelease && !shallow.CargoReleasedThisStep &&
                !rescue.CargoReleased && rescue.FloodStarted,
                "暴潮组件没有区分浅水已经进屋与搁架真正失效，或允许提前固定");

            TideStormRescueAdvanceResult release = rescue.Advance(0.02f, 0.42f, 0.65f);
            Require(release.CargoReleasedThisStep && rescue.CargoReleased && rescue.FloodStarted,
                "真实水深和涌浪达到搁架失效条件时组件没有产生唯一释放事件");
            TideStormRescueAdvanceResult releaseAgain = rescue.Advance(0.02f, 0.42f, 0.65f);
            Require(!releaseAgain.CargoReleasedThisStep,
                "搁架失效事件在持续洪水中重复触发");

            int securedMask = 0;
            int lostMask = 0;
            int waterBit = 1 << (int)TideStormRescueItemKind.DrinkingWater;
            int chartBit = 1 << (int)TideStormRescueItemKind.LighthouseChart;
            for (int i = 0; i < 160 &&
                ((securedMask & waterBit) == 0 || (lostMask & chartBit) == 0); i++)
            {
                if ((securedMask & waterBit) == 0)
                {
                    bool holding = rescue.TryHoldItem((int)TideStormRescueItemKind.DrinkingWater);
                    Require(holding, "玩家仍在水桶旁持续交互时组件拒绝保持拉绳目标");
                }
                TideStormRescueAdvanceResult step = rescue.Advance(0.1f, 0.42f, 0.65f);
                securedMask |= step.SecuredMask;
                lostMask |= step.LostMask;
            }

            Require((securedMask & waterBit) != 0 && rescue.GetItem(TideStormRescueItemKind.DrinkingWater).Secured,
                "持续在真实距离内收绳仍不能把水桶吊到高处");
            Require((lostMask & chartBit) != 0 && rescue.GetItem(TideStormRescueItemKind.LighthouseChart).Lost,
                "未处理的高浮力海图没有在同一股暴潮中更快冲失");

            rescue.SetItemPresent(TideStormRescueItemKind.BoatMaterial, true);
            int recoveredMask = rescue.SecureSurvivorsAfterRecede(0f);
            int timberBit = 1 << (int)TideStormRescueItemKind.BoatMaterial;
            Require((recoveredMask & timberBit) != 0 &&
                rescue.GetItem(TideStormRescueItemKind.BoatMaterial).Secured,
                "真实经历洪水并在退水后幸存的物资没有进入可整理状态");

            rescue.ResetRuntime();
            rescue.SetItemPresent(TideStormRescueItemKind.StoveFuel, true);
            Require(rescue.SecureSurvivorsAfterRecede(0f) == 0,
                "只有警戒清单、未经历洪水的干柴被睡眠白送到高处");

            return $"暴潮组件=浅水不放/失效一次/水桶收妥/海图冲失/退水守恒，掩码{securedMask:X}/{lostMask:X}/{recoveredMask:X}";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static TideSailboatDynamicsState CreateBoatState()
    {
        return new TideSailboatDynamicsState
        {
            HeaveY = -1.18f,
            SailRaised01 = 0.58f
        };
    }

    private static TideBoatConditionPerformanceSample CreateBoatPerformance(
        int hullIntegrity = 3,
        int sailIntegrity = 2,
        int cabinIntegrity = 2)
    {
        return TideBoatConditionPerformanceModel.Evaluate(
            hullIntegrity,
            sailIntegrity,
            cabinIntegrity);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"TIDE_CORE_LOOP_PROBE FAIL: {message}");
        }
    }
}
