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
    }

    private static string RunAll()
    {
        string cistern = ProbeCistern();
        string island = ProbeIslandOwnership();
        string context = ProbeIslandContextPriority();
        string rope = ProbeMooringRope();
        string sailing = ProbeSailingDynamics();
        string storm = ProbeStormRescue();
        return $"TIDE_CORE_LOOP_PROBE PASS | {cistern} | {island} | {context} | {rope} | {sailing} | {storm}";
    }

    private static string ProbeCistern()
    {
        TideRainCisternState initial = TideRainCisternModel.CreateDamaged();
        TideRainCisternState rainy = TideRainCisternModel.Advance(initial, 1800f, 22f, 1f, 0f);
        TideRainCisternState dry = TideRainCisternModel.Advance(rainy, 1800f, 0f, 1f, 0f);
        TideRainCisternState salted = TideRainCisternModel.Advance(dry, 80f, 0f, 1f, 1f);
        Require(rainy.StoredLiters > initial.StoredLiters, "雨槽没有增加蓄水");
        Require(dry.StoredLiters < rainy.StoredLiters, "裂池在无雨时没有漏水");
        Require(salted.SaltFraction01 > dry.SaltFraction01, "暴潮越池没有提高盐分");
        return $"蓄水 {initial.StoredLiters:F1}->{rainy.StoredLiters:F1}->{dry.StoredLiters:F1}L/盐{salted.SaltFraction01:P1}";
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
