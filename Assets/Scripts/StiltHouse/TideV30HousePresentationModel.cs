using UnityEngine;

/// <summary>
/// V30 的纯状态选择器。它不持有场景对象，只把天气时间和现有四条房屋维修线
/// 转换为外景帧、完整室内帧和十二个互斥维修 owner 的状态。
/// </summary>
public static class TideV30HousePresentationModel
{
    public const int ExteriorFrameCount = 12;
    public const int InteriorFrameCount = 12;
    public const int RepairOwnerCount = 12;
    public const float ExteriorFrameSeconds = 0.095f;
    public const float StormExteriorFrameSeconds = 0.066f;
    public const float InteriorFrameSeconds = 0.09f;
    public const float PixelsPerUnit = 192f;
    public const float CanvasPixels = 1536f;
    public const float PivotNormalizedY = 0.03125f;

    public static int EvaluateExteriorFrame(float worldTime, float stormPressure01)
    {
        float frameSeconds = Mathf.Lerp(
            ExteriorFrameSeconds,
            StormExteriorFrameSeconds,
            Mathf.Clamp01(stormPressure01));
        return EvaluateLoopFrame(worldTime, frameSeconds, ExteriorFrameCount);
    }

    public static int EvaluateInteriorFrame(float worldTime)
    {
        return EvaluateLoopFrame(worldTime, InteriorFrameSeconds, InteriorFrameCount);
    }

    public static bool IsFullyRepaired(
        float foundationSteps,
        float roofSteps,
        float interiorSteps,
        float heatSteps)
    {
        return foundationSteps >= 2f && roofSteps >= 2f && interiorSteps >= 2f && heatSteps >= 2f;
    }

    /// <summary>
    /// 每条现有维修线有两次真实施工。V30 的 12 个美术 owner 被分配到这八次施工，
    /// 因而不需要新增隐藏库存或伪造一次点击修完整栋屋。返回值用于在施工中段把同一
    /// owner 从 Damage 切到 Repair；契约要求二者互斥，所以这里不做双层淡入淡出。
    /// </summary>
    public static bool UseRepairSprite(
        string ownerKey,
        float foundationSteps,
        float roofSteps,
        float interiorSteps,
        float heatSteps)
    {
        float channelSteps;
        int requiredStep;
        if (!TryGetOwnerMilestone(ownerKey, out int channel, out requiredStep))
        {
            return false;
        }

        switch (channel)
        {
            case 0:
                channelSteps = foundationSteps;
                break;
            case 1:
                channelSteps = roofSteps;
                break;
            case 2:
                channelSteps = interiorSteps;
                break;
            default:
                channelSteps = heatSteps;
                break;
        }

        return channelSteps >= requiredStep - 0.5f;
    }

    public static Vector2 PixelTopLeftToWorldOffset(Vector2 pixelTopLeft)
    {
        float pivotPixelsY = CanvasPixels * PivotNormalizedY;
        return new Vector2(
            (pixelTopLeft.x - CanvasPixels * 0.5f) / PixelsPerUnit,
            (CanvasPixels - pixelTopLeft.y - pivotPixelsY) / PixelsPerUnit);
    }

    private static int EvaluateLoopFrame(float worldTime, float frameSeconds, int frameCount)
    {
        int absoluteFrame = Mathf.FloorToInt(Mathf.Max(0f, worldTime) / Mathf.Max(0.001f, frameSeconds));
        return absoluteFrame % frameCount;
    }

    private static bool TryGetOwnerMilestone(string ownerKey, out int channel, out int requiredStep)
    {
        channel = 0;
        requiredStep = 1;
        switch (ownerKey)
        {
            case "InteriorEnvelope":
                return true;
            case "MainFloor":
                requiredStep = 2;
                return true;
            case "RoofLeft":
                channel = 1;
                return true;
            case "RoofCenter":
            case "RoofRight":
                channel = 1;
                requiredStep = 2;
                return true;
            case "EntryDoor":
            case "Workbench":
                channel = 2;
                return true;
            case "Bed":
            case "ChartDesk":
            case "Lookout":
                channel = 2;
                requiredStep = 2;
                return true;
            case "Stove":
                channel = 3;
                return true;
            case "LightAndHeat":
                channel = 3;
                requiredStep = 2;
                return true;
            default:
                return false;
        }
    }
}
