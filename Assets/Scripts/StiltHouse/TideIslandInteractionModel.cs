using UnityEngine;

public enum TideIslandContextAction
{
    None,
    TakeWreckPart,
    DrinkFromCistern,
    StageAtShelter,
    StageAtEscapeBoat
}

/// <summary>
/// 岩礁岛的上下文交互优先级。这里不读取 Input、Transform 或 Renderer，
/// 因而同一组距离与携带状态在 Play、探针和以后接入 Input System 时结果一致。
/// </summary>
public static class TideIslandInteractionModel
{
    public static TideIslandContextAction Resolve(
        TideIslandSalvagePart carriedPart,
        bool nearShelterStaging,
        bool nearBoatStaging,
        bool nearWreck,
        bool nearCistern)
    {
        if (carriedPart != TideIslandSalvagePart.None)
        {
            if (nearShelterStaging)
            {
                return TideIslandContextAction.StageAtShelter;
            }

            if (nearBoatStaging)
            {
                return TideIslandContextAction.StageAtEscapeBoat;
            }

            // 携带大型原物时不能顺手饮水或再拆一件；玩家必须先把手中实物放到
            // 两个可见施工位之一，这也避免同一个 F 在同帧触发泊船绳。
            return TideIslandContextAction.None;
        }

        if (nearWreck)
        {
            return TideIslandContextAction.TakeWreckPart;
        }

        if (nearCistern)
        {
            return TideIslandContextAction.DrinkFromCistern;
        }

        return TideIslandContextAction.None;
    }
}
