using UnityEngine;

/// <summary>
/// 一次退潮留在裸岩上、仍有明确来源的单件漂积物。
/// 它不是额外开奖：BatchId 和 Material 都来自已经沿近岸水路移动的原批次。
/// </summary>
public readonly struct TideWrackDepositState
{
    public TideWrackDepositState(
        int batchId,
        TideDriftMaterial material,
        int astronomicalCycleOrdinal,
        float peakWaterY,
        float worldX,
        float groundY,
        float refloatWaterY,
        float rotationDegrees)
    {
        BatchId = batchId;
        Material = material;
        AstronomicalCycleOrdinal = astronomicalCycleOrdinal;
        PeakWaterY = peakWaterY;
        WorldX = worldX;
        GroundY = groundY;
        RefloatWaterY = refloatWaterY;
        RotationDegrees = rotationDegrees;
    }

    public int BatchId { get; }
    public TideDriftMaterial Material { get; }
    public int AstronomicalCycleOrdinal { get; }
    public float PeakWaterY { get; }
    public float WorldX { get; }
    public float GroundY { get; }
    public float RefloatWaterY { get; }
    public float RotationDegrees { get; }
    public bool IsPresent => BatchId > 0 && AstronomicalCycleOrdinal >= 0;
}

/// <summary>
/// 高潮线漂积的纯规则。只有没有被网捕获、退潮时仍在近岸的原批次才可能搁浅；
/// 下一次天文潮真正重新淹到该岩面时，旧物才会被卷回水中。
/// </summary>
public static class TideWrackDepositModel
{
    public const float MinimumPeakAboveGroundMeters = -0.22f;
    public const float RefloatDepthBelowGroundMeters = 0.06f;

    public static TideWrackDepositState TrySettle(
        TideWrackDepositState existing,
        TideDriftBatch batch,
        int astronomicalCycleOrdinal,
        float peakWaterY,
        float groundY,
        float seawardX,
        float inlandX,
        bool captured,
        bool stillNearshore)
    {
        if (!batch.IsValid || astronomicalCycleOrdinal < 0 || captured || !stillNearshore ||
            peakWaterY < groundY + MinimumPeakAboveGroundMeters)
        {
            return existing;
        }

        if (existing.IsPresent)
        {
            if (existing.BatchId == batch.StableId)
            {
                return existing;
            }

            // A lower later tide cannot erase a dry, higher strandline. The bounded
            // first slice keeps the dominant accessible line instead of stacking UI-like rows.
            if (existing.PeakWaterY > peakWaterY + 0.08f)
            {
                return existing;
            }
        }

        float inlandReach01 = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(
                groundY + MinimumPeakAboveGroundMeters,
                groundY + 0.95f,
                peakWaterY));
        float worldX = Mathf.Lerp(seawardX, inlandX, inlandReach01);
        int rotationBucket = PositiveModulo(batch.StableId * 17, 13) - 6;
        return new TideWrackDepositState(
            batch.StableId,
            batch.Material,
            astronomicalCycleOrdinal,
            peakWaterY,
            worldX,
            groundY,
            groundY - RefloatDepthBelowGroundMeters,
            rotationBucket);
    }

    public static bool ShouldRefloat(
        TideWrackDepositState deposit,
        int currentAstronomicalCycleOrdinal,
        float localWaterSurfaceY)
    {
        return deposit.IsPresent &&
            currentAstronomicalCycleOrdinal > deposit.AstronomicalCycleOrdinal &&
            localWaterSurfaceY >= deposit.RefloatWaterY;
    }

    private static int PositiveModulo(int value, int modulo)
    {
        int result = value % modulo;
        return result < 0 ? result + modulo : result;
    }
}
