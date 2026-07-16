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
        string island = ProbeIslandOwnership();
        string context = ProbeIslandContextPriority();
        string salvage = ProbeSalvageMaterialCommit();
        string repairPhases = ProbeRepairWorkPhases();
        string heavyWreck = ProbeHeavyWreckTidalLift();
        string rope = ProbeMooringRope();
        string sailing = ProbeSailingDynamics();
        string storm = ProbeStormRescue();
        return $"TIDE_CORE_LOOP_PROBE PASS | {cistern} | {island} | {context} | {salvage} | {repairPhases} | {heavyWreck} | {rope} | {sailing} | {storm}";
    }

    private static string ProbeCistern()
    {
        TideRainCisternState initial = TideRainCisternModel.CreateDamaged();
        TideRainCisternState rainy = TideRainCisternModel.Advance(initial, 1800f, 22f, 1f, 0f);
        TideRainCisternState dry = TideRainCisternModel.Advance(rainy, 1800f, 0f, 1f, 0f);
        TideRainCisternState salted = TideRainCisternModel.Advance(dry, 80f, 0f, 1f, 1f);
        TideRainCisternState afterFill = TideRainCisternModel.WithdrawPotableWater(
            initial,
            4f,
            true,
            out TidePortableWaterState portable);
        Require(rainy.StoredLiters > initial.StoredLiters, "雨槽没有增加蓄水");
        Require(dry.StoredLiters < rainy.StoredLiters, "裂池在无雨时没有漏水");
        Require(salted.SaltFraction01 > dry.SaltFraction01, "暴潮越池没有提高盐分");
        Require(TideRainCisternModel.GetDrinkableLiters(initial) >= initial.StoredLiters - 0.001f,
            "初始雨水被错误标成不可饮用盐水");
        Require(TideRainCisternModel.GetDrinkableLiters(salted) <= 0.001f,
            "暴潮盐水倒灌后仍被当作可饮水");
        Require(Mathf.Abs(portable.Liters - 4f) <= 0.001f &&
            Mathf.Abs(initial.StoredLiters - afterFill.StoredLiters - portable.Liters) <= 0.001f,
            "蓄水池装入便携容器时没有守恒");
        return $"蓄水 {initial.StoredLiters:F1}->{rainy.StoredLiters:F1}->{dry.StoredLiters:F1}L/暴潮盐{salted.SaltFraction01:P2}/装罐{portable.Liters:F1}L";
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
            bool took = island.TryTakeNearestPart(
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

            bool tookCloth = island.TryTakeNearestPart(
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
        TideMaterialBundle hullNeeds = new TideMaterialBundle(2, 1, 0, 0, 0);
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

        TideMaterialBundle cabinNeeds = new TideMaterialBundle(1, 0, 0, 1, 0);
        int plateAlone = TideSalvageMaterialModel.SelectMinimumParts(
            TideSalvageMaterialModel.RivetedPlateBit,
            new TideMaterialBundle(),
            cabinNeeds);
        Require(plateAlone < 0, "铆接板凭空提供了不存在的木料");
        int plateWithStoredWood = TideSalvageMaterialModel.SelectMinimumParts(
            TideSalvageMaterialModel.RivetedPlateBit,
            new TideMaterialBundle(1, 0, 0, 0, 0),
            cabinNeeds);
        Require(plateWithStoredWood == TideSalvageMaterialModel.RivetedPlateBit,
            "已有木料时铆接板仍不能进入船舱维修");

        int curvedRibSelection = TideSalvageMaterialModel.SelectMinimumParts(
            TideSalvageMaterialModel.HeavyKeelRibPieceABit |
            TideSalvageMaterialModel.HeavyKeelRibPieceBBit,
            new TideMaterialBundle(),
            hullNeeds);
        Require(curvedRibSelection == TideSalvageMaterialModel.HeavyKeelRibPieceABit,
            "成形弯肋不能作为一处柱脚斜撑或船体肋骨的最小原物提交");
        return "原物最终固定=最少组合/库存足不误吞/铆板不生木/弯肋保形";
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
        TideMooringRopeState rope = TideMooringRopeModel.CreateLoose(1.08f);
        rope = TideMooringRopeModel.BeginSwing(rope);
        rope = TideMooringRopeModel.AdvanceSwing(rope, 0.55f, true);
        rope = TideMooringRopeModel.ReleaseThrow(rope);
        Require(rope.Phase == TideMooringRopePhase.Attached, "正确甩绳时没有套住船艉");
        for (int i = 0; i < 1200 && rope.Phase != TideMooringRopePhase.Secured; i++)
        {
            bool reelWithoutOverstrain = rope.Tension01 < 0.82f;
            rope = TideMooringRopeModel.Advance(rope, 0.02f, 0.05f, 0f, reelWithoutOverstrain);
        }

        Require(rope.Phase == TideMooringRopePhase.Secured, "收绳没有通过有限张力把船拉回泊位");
        Require(Mathf.Abs(rope.BoatOffsetMeters) <= TideMooringRopeModel.SecuredOffsetMeters + 0.02f,
            "固定后船仍离跳板过远");
        return $"绳相={rope.Phase}/离岸{rope.BoatOffsetMeters:F2}m/张力{rope.Tension01:F2}";
    }

    private static string ProbeSailingDynamics()
    {
        TideSailboatDynamicsState right = CreateBoatState();
        TideSailboatDynamicsState left = CreateBoatState();
        for (int i = 0; i < 300; i++)
        {
            right = TideSailboatDynamicsModel.Advance(right, 0.02f, 1f, 0f, 0f, 0f, 0f, -1.18f, 0f, 0.1f, 0.5f);
            left = TideSailboatDynamicsModel.Advance(left, 0.02f, -1f, 0f, 0f, 0f, 0f, -1.18f, 0f, 0.1f, 0.5f);
        }

        Require(Mathf.Abs(right.HorizontalVelocity + left.HorizontalVelocity) <= 0.025f,
            "静风时左右手动推进不对称");

        TideSailboatDynamicsState windDriven = CreateBoatState();
        for (int i = 0; i < 240; i++)
        {
            windDriven = TideSailboatDynamicsModel.Advance(
                windDriven, 0.02f, 0f, 1f, 0f, 0.8f, 0f, -1.18f, 0f, 0.15f, 0.7f);
        }
        Require(windDriven.HorizontalVelocity > 0.2f, "升帆后没有读取有符号风场");

        TideSailboatDynamicsState neutral = CreateBoatState();
        TideSailboatDynamicsState trimmed = CreateBoatState();
        for (int i = 0; i < 160; i++)
        {
            neutral = TideSailboatDynamicsModel.Advance(neutral, 0.02f, 0f, 0f, 0f, 0f, 0f, -1.08f, 0.12f, 0.4f, 0.7f);
            trimmed = TideSailboatDynamicsModel.Advance(trimmed, 0.02f, 0f, 0f, 0.38f, 0f, 0f, -1.08f, 0.12f, 0.4f, 0.7f);
        }
        Require(Mathf.Abs(trimmed.PitchDegrees) < Mathf.Abs(neutral.PitchDegrees),
            "移动压舱物不能抵消当前浪坡纵倾");
        return $"静风左右{left.HorizontalVelocity:F2}/{right.HorizontalVelocity:F2}m/s/顺风{windDriven.HorizontalVelocity:F2}/压舱{neutral.PitchDegrees:F1}->{trimmed.PitchDegrees:F1}deg";
    }

    private static string ProbeStormRescue()
    {
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
        for (int i = 0; i < 320 && !secured.Secured; i++)
        {
            secured = TideStormRescueModel.Advance(secured, 0.02f, 0.35f, 0.3f, true);
        }
        Require(secured.Secured && !secured.Lost, "持续固定物资后仍被判定冲失");
        return $"冲失水桶/海图={water.WashoutProgress01:F2}/{chart.WashoutProgress01:F2}/固定={secured.Secured}";
    }

    private static TideSailboatDynamicsState CreateBoatState()
    {
        return new TideSailboatDynamicsState
        {
            HeaveY = -1.18f,
            SailRaised01 = 0.58f
        };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"TIDE_CORE_LOOP_PROBE FAIL: {message}");
        }
    }
}
