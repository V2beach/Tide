using UnityEngine;

public enum TideStormRescueItemKind
{
    DrinkingWater,
    BoatMaterial,
    StoveFuel,
    LighthouseChart
}

[System.Serializable]
public struct TideStormRescueItemState
{
    public TideStormRescueItemKind Kind;
    public bool Present;
    public float Buoyancy01;
    public float SecuringProgress01;
    public float WashoutProgress01;
    public bool Secured;
    public bool Lost;
}

public struct TideStormRescueLayout
{
    public Vector2 PlayerStart;
    public Vector2[] BasePositions;
    public Vector2[] DryRackPositions;
    public float PlayerMoveSpeed;
    public int HoistRopeOwnerCount;
}

public struct TideStormRescueEnvironmentSample
{
    public float LocalWaterDepthMeters;
    public float CurrentSpeedMetersPerSecond;
}

public struct TideStormRescueFloodProfile
{
    public float StepSeconds;
    public TideStormRescueEnvironmentSample[] Samples;
}

/// <summary>
/// 暴潮抢救模型。物件是否冲失由水深、流速、浮力和系固决定，不按剧情顺序写死。
/// 玩家可以中断施工，但时间不足时无法保住所有东西。
/// </summary>
public static class TideStormRescueModel
{
    public static bool ShouldReleaseCargo(
        bool alreadyReleased,
        float localWaterDepthMeters,
        float currentSpeedMetersPerSecond)
    {
        if (alreadyReleased)
        {
            return true;
        }

        float flow01 = Mathf.InverseLerp(
            0.18f,
            0.78f,
            Mathf.Abs(currentSpeedMetersPerSecond));
        float shelfFailureDepth = Mathf.Lerp(0.38f, 0.28f, flow01);
        return localWaterDepthMeters >= shelfFailureDepth;
    }

    public static TideStormRescueItemState Create(TideStormRescueItemKind kind)
    {
        return new TideStormRescueItemState
        {
            Kind = kind,
            Present = true,
            Buoyancy01 = kind == TideStormRescueItemKind.LighthouseChart ? 0.88f :
                kind == TideStormRescueItemKind.StoveFuel ? 0.72f :
                kind == TideStormRescueItemKind.BoatMaterial ? 0.34f : 0.18f
        };
    }

    public static TideStormRescueItemState Advance(
        TideStormRescueItemState state,
        float deltaSeconds,
        float localWaterDepthMeters,
        float currentSpeedMetersPerSecond,
        bool playerSecuring)
    {
        if (!state.Present || state.Lost || state.Secured)
        {
            return state;
        }

        float dt = Mathf.Max(0f, deltaSeconds);
        if (playerSecuring)
        {
            float baseWorkRate = Mathf.Lerp(
                0.22f,
                0.13f,
                Mathf.Clamp01(localWaterDepthMeters / 0.8f));
            float handlingEffort = state.Kind == TideStormRescueItemKind.BoatMaterial ? 1.45f :
                state.Kind == TideStormRescueItemKind.DrinkingWater ? 1f :
                state.Kind == TideStormRescueItemKind.StoveFuel ? 0.9f : 0.72f;
            float workRate = baseWorkRate / handlingEffort;
            state.SecuringProgress01 = Mathf.Clamp01(state.SecuringProgress01 + workRate * dt);
            if (state.SecuringProgress01 >= 1f)
            {
                state.Secured = true;
                state.WashoutProgress01 = 0f;
                return state;
            }
        }

        float wet01 = Mathf.InverseLerp(0.04f, 0.82f, Mathf.Max(0f, localWaterDepthMeters));
        float flow01 = Mathf.InverseLerp(0.08f, 0.9f, Mathf.Abs(currentSpeedMetersPerSecond));
        float washRate = wet01 * Mathf.Lerp(0.06f, 0.2f, flow01) * Mathf.Lerp(0.55f, 1.35f, state.Buoyancy01);
        state.WashoutProgress01 = Mathf.Clamp01(state.WashoutProgress01 + washRate * dt);
        state.Lost = state.WashoutProgress01 >= 1f;
        return state;
    }

    public static Vector2 EvaluateWorldPosition(
        Vector2 basePosition,
        Vector2 dryRackPosition,
        TideStormRescueItemState item,
        float currentSpeedMetersPerSecond)
    {
        float direction = Mathf.Abs(currentSpeedMetersPerSecond) < 0.03f
            ? 1f
            : Mathf.Sign(currentSpeedMetersPerSecond);
        float horizontalDrift = direction * item.WashoutProgress01 *
            Mathf.Lerp(0.7f, 1.55f, item.Buoyancy01);
        float lift = item.WashoutProgress01 * Mathf.Lerp(0.04f, 0.42f, item.Buoyancy01);
        Vector2 floodedPosition = basePosition + new Vector2(horizontalDrift, lift);

        // 固定不是结算瞬移。施工进度同时代表吊绳提升进度，因此物件从当前
        // 漂移位置连续升到同一开间的干燥搁架，完成前后共用一条轨迹。
        float hoist01 = Mathf.SmoothStep(0f, 1f, item.SecuringProgress01);
        return Vector2.Lerp(floodedPosition, dryRackPosition, hoist01);
    }

    public static Vector2 EvaluateInteractionPosition(
        Vector2 basePosition,
        Vector2 dryRackPosition,
        TideStormRescueItemState item,
        float currentSpeedMetersPerSecond)
    {
        // 第一次必须追到仍在漂移的实物。绳挂上以后玩家留在吊点下收绳，
        // 不需要跟着逐渐升高的物件一起浮到半空。
        return item.SecuringProgress01 > 0.001f
            ? basePosition
            : EvaluateWorldPosition(basePosition, dryRackPosition, item, currentSpeedMetersPerSecond);
    }
}
