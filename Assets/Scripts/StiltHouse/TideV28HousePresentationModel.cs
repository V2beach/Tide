using UnityEngine;

/// <summary>
/// Owns the deterministic presentation choices for the registered V28 exterior
/// and V27 interior.  World placement and gameplay state remain in the slice
/// controller; this class only chooses which authored endpoint is visible.
/// </summary>
public static class TideV28HousePresentationModel
{
    public const int ExteriorFrameCount = 12;
    public const float CalmFrameSeconds = 0.1f;
    public const float StormFrameSeconds = 0.068f;
    public const float RepairedInteriorThreshold = 0.66f;

    public static int EvaluateExteriorFrame(float worldTime, float stormPressure01)
    {
        float frameSeconds = Mathf.Lerp(
            CalmFrameSeconds,
            StormFrameSeconds,
            Mathf.Clamp01(stormPressure01));
        int absoluteFrame = Mathf.FloorToInt(Mathf.Max(0f, worldTime) / frameSeconds);
        return absoluteFrame % ExteriorFrameCount;
    }

    public static bool UseRepairedInterior(float shelterRestoration01)
    {
        return shelterRestoration01 >= RepairedInteriorThreshold;
    }

    public static bool IsCompleteRuntimePack(
        Sprite[] exteriorFrames,
        Sprite interiorFound,
        Sprite interiorRepaired)
    {
        if (exteriorFrames == null || exteriorFrames.Length != ExteriorFrameCount ||
            interiorFound == null || interiorRepaired == null)
        {
            return false;
        }

        for (int i = 0; i < exteriorFrames.Length; i++)
        {
            if (exteriorFrames[i] == null)
            {
                return false;
            }
        }

        return true;
    }
}
