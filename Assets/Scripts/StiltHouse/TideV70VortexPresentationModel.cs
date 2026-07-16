using UnityEngine;

public enum TideV70VortexLayer
{
    ThroatDepression,
    UnderwaterReturnFlow,
    SurfaceConvergenceFoam,
}

/// <summary>
/// V70 侧视漩涡单层在某一现实秒的表现采样。
///
/// 资源只提供透明像素；这里把契约偏移贴到运行时给出的真实海面切线。所有层
/// 共用同一低频位移和压缩，避免每层各自随机后在中央喉口撕开。
/// </summary>
public readonly struct TideV70VortexPose
{
    public TideV70VortexPose(
        int frameIndex,
        Vector2 position,
        float rotationDegrees,
        float uniformScale,
        float horizontalCompression)
    {
        FrameIndex = frameIndex;
        Position = position;
        RotationDegrees = rotationDegrees;
        UniformScale = uniformScale;
        HorizontalCompression = horizontalCompression;
    }

    public int FrameIndex { get; }
    public Vector2 Position { get; }
    public float RotationDegrees { get; }
    public float UniformScale { get; }
    public float HorizontalCompression { get; }
}

/// <summary>
/// V70 Balanced 三层逐帧资源的时间、尺寸和海面配准契约。
///
/// 宏观潮位、浪高、吸力、碰撞和路线封锁不属于这个模型。调用方必须把同一
/// <see cref="TideOceanSample"/> 的 SurfaceY 与 Slope 传入，漩涡才会和船、漂物
/// 以及连续海体共享一条水线。
/// </summary>
public static class TideV70VortexPresentationModel
{
    public const int CatalogVersion = 70;
    public const string RuntimeProfile = "Balanced";
    public const int LayerCount = 3;
    public const int FrameCount = 12;
    public const float PixelsPerUnit = 256f;
    public const float UniformScale = 0.875f;

    public static float GetFrameDurationSeconds(TideV70VortexLayer layer)
    {
        switch (layer)
        {
            case TideV70VortexLayer.ThroatDepression:
                return 0.132f;
            case TideV70VortexLayer.UnderwaterReturnFlow:
                return 0.165f;
            default:
                return 0.11f;
        }
    }

    public static Vector2 GetContractWorldOffset(TideV70VortexLayer layer)
    {
        switch (layer)
        {
            case TideV70VortexLayer.ThroatDepression:
                return new Vector2(0.001953f, -0.394531f);
            case TideV70VortexLayer.UnderwaterReturnFlow:
                return new Vector2(0f, -0.388672f);
            default:
                return new Vector2(0f, 0.263672f);
        }
    }

    public static int EvaluateFrameIndex(TideV70VortexLayer layer, float realSeconds)
    {
        // 三层故意使用不同的固定起相和帧长。它们仍然完全可复现，但不会像一张
        // 十二帧合成图那样同时翻页，海水因此更接近多个连续流场叠加的节奏。
        float phaseSeconds;
        switch (layer)
        {
            case TideV70VortexLayer.ThroatDepression:
                phaseSeconds = 0.037f;
                break;
            case TideV70VortexLayer.UnderwaterReturnFlow:
                phaseSeconds = 0.281f;
                break;
            default:
                phaseSeconds = 0.163f;
                break;
        }

        float duration = GetFrameDurationSeconds(layer);
        int absoluteFrame = Mathf.FloorToInt((Mathf.Max(0f, realSeconds) + phaseSeconds) / duration);
        return ((absoluteFrame % FrameCount) + FrameCount) % FrameCount;
    }

    public static TideV70VortexPose EvaluatePose(
        TideV70VortexLayer layer,
        Vector2 surfacePivot,
        float localSurfaceSlope,
        float agitation01,
        float realSeconds)
    {
        float agitation = Mathf.Clamp01(agitation01);
        float rotationDegrees = Mathf.Clamp(
            Mathf.Atan(localSurfaceSlope) * Mathf.Rad2Deg,
            -8f,
            8f);

        // Perlin 只控制缓慢包络，不逐帧抽随机数。三个层读取同一个包络，因此
        // 中央喉口、水下回流和汇浪泡沫不会彼此滑开，也不会随帧率抖动。
        float lateralNoise = Mathf.PerlinNoise(realSeconds * 0.071f + 5.7f, 3.19f) - 0.5f;
        float compressionNoise = Mathf.PerlinNoise(realSeconds * 0.089f + 11.2f, 7.43f) - 0.5f;
        float lateralOffset = lateralNoise * Mathf.Lerp(0.018f, 0.056f, agitation);
        float horizontalCompression = 1f +
            compressionNoise * Mathf.Lerp(0.01f, 0.035f, agitation);

        Quaternion surfaceRotation = Quaternion.Euler(0f, 0f, rotationDegrees);
        Vector2 contractOffset = GetContractWorldOffset(layer) * UniformScale;
        Vector2 rotatedOffset = surfaceRotation * contractOffset;
        Vector2 position = surfacePivot + new Vector2(lateralOffset, 0f) + rotatedOffset;
        return new TideV70VortexPose(
            EvaluateFrameIndex(layer, realSeconds),
            position,
            rotationDegrees,
            UniformScale,
            horizontalCompression);
    }
}
