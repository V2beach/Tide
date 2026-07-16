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
    public float Buoyancy01;
    public float SecuringProgress01;
    public float WashoutProgress01;
    public bool Secured;
    public bool Lost;
}

/// <summary>
/// 暴潮抢救模型。物件是否冲失由水深、流速、浮力和系固决定，不按剧情顺序写死。
/// 玩家可以中断施工，但时间不足时无法保住所有东西。
/// </summary>
public static class TideStormRescueModel
{
    public static TideStormRescueItemState Create(TideStormRescueItemKind kind)
    {
        return new TideStormRescueItemState
        {
            Kind = kind,
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
        if (state.Lost || state.Secured)
        {
            return state;
        }

        float dt = Mathf.Max(0f, deltaSeconds);
        if (playerSecuring)
        {
            float workRate = Mathf.Lerp(0.34f, 0.2f, Mathf.Clamp01(localWaterDepthMeters / 0.8f));
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
}
