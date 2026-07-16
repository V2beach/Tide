using UnityEngine;

/// <summary>
/// 把现有四条房屋维修线映射到 V35 十二个室内美术所有者。
/// 这里只选择 Damage/Repair，不持有场景对象，也不改变材料或结算规则。
/// </summary>
public static class TideV35HouseInteriorPresentationModel
{
    public const int Version = 35;
    public const int OwnerCount = 12;
    public const float PixelsPerUnit = 256f;
    public const float CanvasPixels = 2048f;
    public const float PivotNormalizedY = 0.03125f;

    public static bool UseRepairSprite(
        string gameplayOwner,
        int requiredStep,
        float foundationSteps,
        float roofSteps,
        float interiorSteps,
        float heatSteps)
    {
        float channelSteps;
        switch (gameplayOwner)
        {
            case "Stilt":
                channelSteps = foundationSteps;
                break;
            case "Roof":
                channelSteps = roofSteps;
                break;
            case "Interior":
                channelSteps = interiorSteps;
                break;
            case "Lamp":
                channelSteps = heatSteps;
                break;
            default:
                return false;
        }

        // requiredStep=1 在第一次施工过半时替换；requiredStep=2 同理在第二次
        // 施工过半时替换。Damage 与 Repair 永远互斥，不做双图透明叠加。
        return channelSteps >= Mathf.Clamp(requiredStep, 1, 2) - 0.5f;
    }

    public static Vector2 PixelTopLeftToWorldOffset(Vector2 pixelTopLeft)
    {
        float pivotPixelsY = CanvasPixels * PivotNormalizedY;
        return new Vector2(
            (pixelTopLeft.x - CanvasPixels * 0.5f) / PixelsPerUnit,
            (CanvasPixels - pixelTopLeft.y - pivotPixelsY) / PixelsPerUnit);
    }
}
