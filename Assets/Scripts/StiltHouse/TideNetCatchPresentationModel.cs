using UnityEngine;

public enum TideNetCatchMaterial
{
    Fish,
    Wood,
    Relic,
    Debris
}

/// <summary>
/// 潮获在展开网和收回网兜中的视觉受力契约。
///
/// 展开网坐标使用左上原点归一化值，和 V54 锚点契约一致。这里不决定掉落、
/// 收益或网损，只回答同一件实物在网眼哪里受力、收网后怎样聚拢。这样物件
/// 不会再由世界坐标索引间距堆成一排，也不会在换网深度时脱离网体。
/// </summary>
public static class TideNetCatchPresentationModel
{
    public struct Pose
    {
        public Vector2 AnchorTopLeft01;
        public float RotationDegrees;
        public float SizeScale;
        public bool FlipX;

        public Pose(Vector2 anchorTopLeft01, float rotationDegrees, float sizeScale, bool flipX)
        {
            AnchorTopLeft01 = anchorTopLeft01;
            RotationDegrees = rotationDegrees;
            SizeScale = sizeScale;
            FlipX = flipX;
        }
    }

    public static Pose GetInNetPose(TideNetCatchMaterial material, int pieceIndex, int pieceCount)
    {
        int count = Mathf.Clamp(pieceCount, 1, 3);
        int index = Mathf.Clamp(pieceIndex, 0, count - 1);
        Vector2 anchor = GetBaseNetAnchor(index, count);

        switch (material)
        {
            case TideNetCatchMaterial.Fish:
                anchor.y += index == 1 ? 0.035f : -0.01f;
                return new Pose(anchor, index % 2 == 0 ? -7f : 6f, 0.82f, (index & 1) == 1);
            case TideNetCatchMaterial.Wood:
                anchor.y += index == 1 ? -0.035f : 0.035f;
                return new Pose(anchor, index == 0 ? -7f : index == 1 ? 9f : -3f, count >= 2 ? 0.86f : 0.94f, false);
            case TideNetCatchMaterial.Relic:
                anchor.x = Mathf.Lerp(anchor.x, 0.5f, 0.22f);
                anchor.y += index == 0 ? 0.035f : -0.045f;
                return new Pose(anchor, index == 0 ? -4f : index == 1 ? 11f : -9f, index == 0 ? 0.92f : 0.82f, false);
            default:
                anchor.x = Mathf.Lerp(anchor.x, 0.5f, 0.14f);
                anchor.y += index == 0 ? 0.025f : -0.025f;
                return new Pose(anchor, index == 0 ? -6f : index == 1 ? 13f : -11f, index == 0 ? 0.88f : 0.76f, (index & 1) == 1);
        }
    }

    public static Vector2 GetBundledOffset(TideNetCatchMaterial material, int pieceIndex, int pieceCount)
    {
        int count = Mathf.Clamp(pieceCount, 1, 3);
        int index = Mathf.Clamp(pieceIndex, 0, count - 1);
        float centered = index - (count - 1) * 0.5f;

        switch (material)
        {
            case TideNetCatchMaterial.Fish:
                return new Vector2(centered * 0.13f, index == 1 ? 0.045f : -0.005f);
            case TideNetCatchMaterial.Wood:
                return new Vector2(centered * 0.11f, index * 0.035f);
            case TideNetCatchMaterial.Relic:
                return new Vector2(centered * 0.09f, index == 0 ? -0.01f : 0.045f);
            default:
                return new Vector2(centered * 0.08f, index * 0.03f);
        }
    }

    public static float GetBundledRotation(TideNetCatchMaterial material, int pieceIndex)
    {
        if (material == TideNetCatchMaterial.Wood)
        {
            return pieceIndex == 0 ? -4f : 5f;
        }

        if (material == TideNetCatchMaterial.Fish)
        {
            return pieceIndex % 2 == 0 ? -8f : 7f;
        }

        return pieceIndex % 2 == 0 ? -5f : 8f;
    }

    public static Vector2 EvaluateLoadMotion(
        TideNetCatchMaterial material,
        float time,
        int pieceIndex,
        float load01,
        float bundled01)
    {
        float free01 = (1f - Mathf.Clamp01(bundled01)) * Mathf.Clamp01(0.25f + load01);
        switch (material)
        {
            case TideNetCatchMaterial.Fish:
                return new Vector2(
                    Mathf.Sin(time * 7.8f + pieceIndex * 1.7f) * 0.018f,
                    Mathf.Sin(time * 10.4f + pieceIndex) * 0.012f) * free01;
            case TideNetCatchMaterial.Wood:
                return new Vector2(
                    Mathf.Sin(time * 1.25f + pieceIndex) * 0.016f,
                    Mathf.Sin(time * 1.8f + pieceIndex * 0.7f) * 0.006f) * free01;
            case TideNetCatchMaterial.Relic:
                return new Vector2(
                    Mathf.Sin(time * 3.2f + pieceIndex) * 0.008f,
                    Mathf.Abs(Mathf.Sin(time * 2.4f + pieceIndex)) * -0.01f) * free01;
            default:
                return new Vector2(
                    Mathf.Sin(time * 2.8f + pieceIndex * 1.3f) * 0.02f,
                    Mathf.Sin(time * 4.1f + pieceIndex) * 0.009f) * free01;
        }
    }

    private static Vector2 GetBaseNetAnchor(int index, int count)
    {
        if (count == 1)
        {
            return new Vector2(0.5f, 0.7f);
        }

        if (count == 2)
        {
            return index == 0
                ? new Vector2(0.39f, 0.69f)
                : new Vector2(0.63f, 0.72f);
        }

        if (index == 0)
        {
            return new Vector2(0.31f, 0.67f);
        }

        return index == 1
            ? new Vector2(0.5f, 0.75f)
            : new Vector2(0.7f, 0.68f);
    }
}
