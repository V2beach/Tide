using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 暴潮峰值的场景级取舍门。穷举属于编辑器验证，不进入运行控制器；运行时只
/// 保留浮力、冲失、吊升公式和房内布局，避免为了测试继续膨胀主 MonoBehaviour。
/// </summary>
public static class TideStormRescueTradeoffConvergenceProbe
{
    private const float TestDepthMeters = 0.7f;
    private const float TestCurrentMetersPerSecond = 0.65f;
    private const float InteractionDistance = 0.46f;
    private const float StepSeconds = 0.02f;
    private const float MaximumScenarioSeconds = 24f;

    public static string Run(TideStiltHouseFirstSliceController controller)
    {
        TideStormRescueLayout layout = controller.GetStormRescueLayout();
        int itemCount = layout.BasePositions != null ? layout.BasePositions.Length : 0;
        if (itemCount != 4 || layout.DryRackPositions == null ||
            layout.DryRackPositions.Length != itemCount)
        {
            return "FAIL：暴潮抢救没有提供四件实物的完整房内布局。";
        }

        HashSet<int> savedMasks = new HashSet<int>();
        int bestSavedCount = 0;
        int worstSavedCount = itemCount;
        float longestResolutionSeconds = 0f;

        // 四件物资只有 24 种完整优先级。全部跑过能证明“选什么”确实改变损失，
        // 而不是只为某条推荐路线写测试。当前模型没有并行动作；中途暂停不会减少
        // 总施工量，因此完整顺序覆盖了有意义的最优顺序。
        for (int first = 0; first < itemCount; first++)
        {
            for (int second = 0; second < itemCount; second++)
            {
                if (second == first)
                {
                    continue;
                }

                for (int third = 0; third < itemCount; third++)
                {
                    if (third == first || third == second)
                    {
                        continue;
                    }

                    for (int fourth = 0; fourth < itemCount; fourth++)
                    {
                        if (fourth == first || fourth == second || fourth == third)
                        {
                            continue;
                        }

                        int[] priority = { first, second, third, fourth };
                        int savedMask = SimulateRoute(
                            layout,
                            priority,
                            out int lostMask,
                            out float resolutionSeconds);
                        int savedCount = CountSetBits(savedMask);
                        bestSavedCount = Mathf.Max(bestSavedCount, savedCount);
                        worstSavedCount = Mathf.Min(worstSavedCount, savedCount);
                        longestResolutionSeconds = Mathf.Max(longestResolutionSeconds, resolutionSeconds);
                        if (lostMask != 0)
                        {
                            savedMasks.Add(savedMask);
                        }
                    }
                }
            }
        }

        bool hoistFinishesContinuously = true;
        bool rackStaysInSameBay = true;
        bool rackClearsTestFlood = true;
        float lowerFeetY = layout.BasePositions[0].y - 0.13f;
        for (int i = 0; i < itemCount; i++)
        {
            TideStormRescueItemState almostSecured = TideStormRescueModel.Create(
                (TideStormRescueItemKind)i);
            almostSecured.WashoutProgress01 = 0.42f;
            almostSecured.SecuringProgress01 = 0.999f;
            Vector2 beforeCommit = TideStormRescueModel.EvaluateWorldPosition(
                layout.BasePositions[i],
                layout.DryRackPositions[i],
                almostSecured,
                TestCurrentMetersPerSecond);
            almostSecured.SecuringProgress01 = 1f;
            almostSecured.WashoutProgress01 = 0f;
            almostSecured.Secured = true;
            Vector2 afterCommit = TideStormRescueModel.EvaluateWorldPosition(
                layout.BasePositions[i],
                layout.DryRackPositions[i],
                almostSecured,
                TestCurrentMetersPerSecond);
            hoistFinishesContinuously &= Vector2.Distance(beforeCommit, afterCommit) <= 0.01f;
            rackStaysInSameBay &= Mathf.Abs(
                layout.BasePositions[i].x - layout.DryRackPositions[i].x) <= 0.001f;
            rackClearsTestFlood &= layout.DryRackPositions[i].y >=
                lowerFeetY + TestDepthMeters + 0.18f;
        }

        bool atLeastOneCanBeSaved = bestSavedCount >= 1;
        bool cannotSaveEverything = bestSavedCount < itemCount;
        bool prioritiesChangeOutcome = savedMasks.Count >= 2;
        bool hoistRopesRegistered = layout.HoistRopeOwnerCount == itemCount;
        string evidence =
            $"积水={TestDepthMeters:F2}m/流速={TestCurrentMetersPerSecond:F2}m/s；" +
            $"24条顺序最多/最少={bestSavedCount}/{worstSavedCount}件；" +
            $"不同保留组合={savedMasks.Count}；最慢结算={longestResolutionSeconds:F1}s；" +
            $"吊升连续/同开间/离水/绳owner={hoistFinishesContinuously}/{rackStaysInSameBay}/{rackClearsTestFlood}/{hoistRopesRegistered}";
        return atLeastOneCanBeSaved && cannotSaveEverything && prioritiesChangeOutcome &&
            hoistFinishesContinuously && rackStaysInSameBay && rackClearsTestFlood && hoistRopesRegistered
            ? $"PASS：第一次暴潮形成可执行但不可全收的物资取舍，优先级会改变实际损失。{evidence}"
            : $"FAIL：暴潮取舍或从积水层吊升到干燥搁架的实物路径仍不成立。{evidence}";
    }

    private static int SimulateRoute(
        TideStormRescueLayout layout,
        int[] priority,
        out int lostMask,
        out float elapsedSeconds)
    {
        TideStormRescueItemState[] items =
        {
            TideStormRescueModel.Create(TideStormRescueItemKind.DrinkingWater),
            TideStormRescueModel.Create(TideStormRescueItemKind.BoatMaterial),
            TideStormRescueModel.Create(TideStormRescueItemKind.StoveFuel),
            TideStormRescueModel.Create(TideStormRescueItemKind.LighthouseChart)
        };
        Vector2 simulatedPlayer = layout.PlayerStart;
        int priorityIndex = 0;
        elapsedSeconds = 0f;

        while (elapsedSeconds < MaximumScenarioSeconds)
        {
            while (priorityIndex < priority.Length &&
                   (items[priority[priorityIndex]].Lost || items[priority[priorityIndex]].Secured))
            {
                priorityIndex++;
            }

            int targetIndex = priorityIndex < priority.Length ? priority[priorityIndex] : -1;
            bool securingTarget = false;
            if (targetIndex >= 0)
            {
                Vector2 target = TideStormRescueModel.EvaluateInteractionPosition(
                    layout.BasePositions[targetIndex],
                    layout.DryRackPositions[targetIndex],
                    items[targetIndex],
                    TestCurrentMetersPerSecond);
                if (Vector2.Distance(simulatedPlayer, target) <= InteractionDistance)
                {
                    securingTarget = true;
                }
                else
                {
                    // 正常干地步速是对玩家最有利的上界。真实积水、逆流和转身只会
                    // 更慢；即使此前提仍无法全救，取舍就不是靠暗中削弱操作制造的。
                    float move = Mathf.Min(
                        layout.PlayerMoveSpeed * StepSeconds,
                        Mathf.Abs(target.x - simulatedPlayer.x));
                    simulatedPlayer.x += Mathf.Sign(target.x - simulatedPlayer.x) * move;
                }
            }

            bool anyUnresolved = false;
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = TideStormRescueModel.Advance(
                    items[i],
                    StepSeconds,
                    TestDepthMeters,
                    TestCurrentMetersPerSecond,
                    securingTarget && i == targetIndex);
                anyUnresolved |= !items[i].Lost && !items[i].Secured;
            }

            elapsedSeconds += StepSeconds;
            if (!anyUnresolved)
            {
                break;
            }
        }

        int savedMask = 0;
        lostMask = 0;
        for (int i = 0; i < items.Length; i++)
        {
            savedMask |= items[i].Secured ? 1 << i : 0;
            lostMask |= items[i].Lost ? 1 << i : 0;
        }
        return savedMask;
    }

    private static int CountSetBits(int value)
    {
        int count = 0;
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }
        return count;
    }
}
