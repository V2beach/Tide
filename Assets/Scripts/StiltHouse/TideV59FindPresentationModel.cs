using UnityEngine;

public enum TideV59FindKind
{
    Fish,
    Wood,
    Trash,
    Relic,
}

/// <summary>
/// V59 单件潮带物的米制呈现契约。
///
/// 所有坐标都是相对 Sprite 中心的世界米，来源于
/// ProductionTideBorneFindsV59/tide-finds-contract.json。资源本体不烘焙海水、
/// 网、手或泡沫；运行侧用这些锚点把同一张 Sprite 放进不同物理状态。
/// </summary>
public readonly struct TideV59FindSpec
{
    public TideV59FindSpec(
        Vector2 visibleWorldSize,
        Vector2 waterlineFromSpriteCenter,
        Vector2 centerOfMassFromSpriteCenter,
        Vector2 grabPointFromSpriteCenter,
        float carryRotationDegrees,
        bool canFreeFloat)
    {
        VisibleWorldSize = visibleWorldSize;
        WaterlineFromSpriteCenter = waterlineFromSpriteCenter;
        CenterOfMassFromSpriteCenter = centerOfMassFromSpriteCenter;
        GrabPointFromSpriteCenter = grabPointFromSpriteCenter;
        CarryRotationDegrees = carryRotationDegrees;
        CanFreeFloat = canFreeFloat;
    }

    public Vector2 VisibleWorldSize { get; }
    public Vector2 WaterlineFromSpriteCenter { get; }
    public Vector2 CenterOfMassFromSpriteCenter { get; }
    public Vector2 GrabPointFromSpriteCenter { get; }
    public float CarryRotationDegrees { get; }
    public bool CanFreeFloat { get; }
}

/// <summary>
/// V59 潮带物的纯呈现模型。它不读取场景、Renderer、库存或输入。
/// 批次 ID 只决定同类物件的稳定款式；生命周期状态不得重新抽取款式。
/// </summary>
public static class TideV59FindPresentationModel
{
    public const int CatalogVersion = 59;
    public const string RuntimeProfile = "High";
    public const int KindCount = 4;
    public const int VariantsPerKind = 3;

    private static readonly TideV59FindSpec[,] Specs =
    {
        {
            new TideV59FindSpec(
                new Vector2(0.560547f, 0.1875f),
                new Vector2(0f, -0.01875f),
                new Vector2(-0.005605f, -0.001875f),
                new Vector2(0.06166f, 0.028125f),
                -4f,
                true),
            new TideV59FindSpec(
                new Vector2(0.519531f, 0.208984f),
                new Vector2(-0.000977f, -0.023965f),
                new Vector2(-0.000977f, -0.005156f),
                new Vector2(0.045781f, 0.032461f),
                3f,
                true),
            new TideV59FindSpec(
                new Vector2(0.580078f, 0.232422f),
                new Vector2(0f, -0.020918f),
                new Vector2(-0.011602f, 0f),
                new Vector2(0.058008f, 0.037187f),
                -2f,
                true),
        },
        {
            new TideV59FindSpec(
                new Vector2(0.775391f, 0.193359f),
                new Vector2(0f, -0.034805f),
                new Vector2(0f, -0.003867f),
                new Vector2(-0.116309f, 0.023203f),
                -10f,
                true),
            new TideV59FindSpec(
                new Vector2(0.679688f, 0.267578f),
                new Vector2(0f, -0.042813f),
                new Vector2(-0.006797f, -0.005352f),
                new Vector2(-0.088359f, 0.037461f),
                8f,
                true),
            new TideV59FindSpec(
                new Vector2(0.660156f, 0.263672f),
                new Vector2(0f, -0.050098f),
                new Vector2(0f, -0.010547f),
                new Vector2(-0.026406f, 0.039551f),
                -5f,
                true),
        },
        {
            new TideV59FindSpec(
                new Vector2(0.460938f, 0.308594f),
                new Vector2(0f, -0.026641f),
                new Vector2(-0.004609f, -0.017383f),
                new Vector2(0.032266f, 0.062852f),
                7f,
                true),
            new TideV59FindSpec(
                new Vector2(0.449219f, 0.291016f),
                new Vector2(-0.000977f, -0.032012f),
                new Vector2(0.008008f, -0.014551f),
                new Vector2(-0.036914f, 0.055293f),
                -6f,
                true),
            new TideV59FindSpec(
                new Vector2(0.441406f, 0.28125f),
                new Vector2(0.000977f, -0.044141f),
                new Vector2(0.009805f, -0.013203f),
                new Vector2(-0.043164f, 0.043047f),
                5f,
                true),
        },
        {
            new TideV59FindSpec(
                new Vector2(0.300781f, 0.222656f),
                new Vector2(0f, -0.040078f),
                new Vector2(0f, -0.00668f),
                new Vector2(-0.009023f, 0.051211f),
                -3f,
                true),
            new TideV59FindSpec(
                new Vector2(0.25f, 0.244141f),
                new Vector2(-0.000977f, -0.063477f),
                new Vector2(-0.000977f, -0.009766f),
                new Vector2(-0.000977f, 0.068359f),
                0f,
                false),
            new TideV59FindSpec(
                new Vector2(0.279297f, 0.255859f),
                new Vector2(0.000977f, -0.063359f),
                new Vector2(0.00377f, -0.012188f),
                new Vector2(-0.015781f, 0.069687f),
                2f,
                false),
        },
    };

    public static int ResolveVariantIndex(
        TideV59FindKind kind,
        int pieceIndex,
        int stableBatchId,
        bool freeDrifting)
    {
        int clampedPiece = PositiveModulo(pieceIndex, VariantsPerKind);
        if (kind == TideV59FindKind.Relic)
        {
            // R01 是可短时漂浮的油布包；R02/R03 都会下沉，只能在网或残骸中出现。
            return freeDrifting ? 0 : clampedPiece;
        }

        int batchOffset = stableBatchId > 0
            ? PositiveModulo(stableBatchId, VariantsPerKind)
            : 0;
        return PositiveModulo(batchOffset + clampedPiece, VariantsPerKind);
    }

    public static TideV59FindSpec GetSpec(TideV59FindKind kind, int variantIndex)
    {
        int kindIndex = Mathf.Clamp((int)kind, 0, KindCount - 1);
        int safeVariant = PositiveModulo(variantIndex, VariantsPerKind);
        return Specs[kindIndex, safeVariant];
    }

    public static Vector2 GetSpriteCenterForWaterline(
        Vector2 worldWaterline,
        TideV59FindSpec spec,
        float rotationDegrees)
    {
        Vector2 rotatedAnchor = Quaternion.Euler(0f, 0f, rotationDegrees) *
            spec.WaterlineFromSpriteCenter;
        return worldWaterline - rotatedAnchor;
    }

    public static Vector2 GetSpriteCenterForGrabPoint(
        Vector2 worldGrabPoint,
        TideV59FindSpec spec,
        float rotationDegrees,
        bool flipX)
    {
        Vector2 localGrab = spec.GrabPointFromSpriteCenter;
        if (flipX)
        {
            localGrab.x *= -1f;
        }

        Vector2 rotatedAnchor = Quaternion.Euler(0f, 0f, rotationDegrees) * localGrab;
        return worldGrabPoint - rotatedAnchor;
    }

    private static int PositiveModulo(int value, int modulo)
    {
        int result = value % modulo;
        return result < 0 ? result + modulo : result;
    }
}
