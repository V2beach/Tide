using UnityEngine;

/// <summary>
/// V54 网态的契约尺寸和语义锚点换算。
///
/// 所有锚点均来自 runtime-contract.json 的左上原点归一化坐标。运行时先把
/// 锚点换到 Sprite 中心坐标，再把它精确贴到木桩、人物手或存放架上，禁止用
/// 肉眼偏移掩盖资源和碰撞不一致。
/// </summary>
public static class TideV54NetPresentationModel
{
    public const int CatalogVersion = 54;
    public const string RuntimeProfile = "Balanced";
    public const int VisualStateCount = 7;

    public static bool IsDeployedState(TideV54NetVisualState state)
    {
        return state == TideV54NetVisualState.DeployedDry ||
            state == TideV54NetVisualState.DeployedWet ||
            state == TideV54NetVisualState.DeployedFrayed ||
            state == TideV54NetVisualState.BrokenResidue;
    }

    public static Vector2 GetContractWorldSize(TideV54NetVisualState state)
    {
        switch (state)
        {
            case TideV54NetVisualState.DeployedWet:
                return new Vector2(1.62f, 0.75f);
            case TideV54NetVisualState.DeployedFrayed:
                return new Vector2(1.66f, 0.73f);
            case TideV54NetVisualState.BrokenResidue:
                return new Vector2(1.46f, 0.82f);
            case TideV54NetVisualState.StoredDry:
                return new Vector2(0.64f, 0.3f);
            case TideV54NetVisualState.CarriedDry:
                return new Vector2(0.48f, 0.32f);
            case TideV54NetVisualState.HauledWet:
                return new Vector2(0.52f, 0.3f);
            default:
                return new Vector2(1.55f, 0.78f);
        }
    }

    public static Vector2 GetLeftAttachmentTopLeft01(TideV54NetVisualState state)
    {
        switch (state)
        {
            case TideV54NetVisualState.DeployedWet:
                return new Vector2(0.070964f, 0.193359f);
            case TideV54NetVisualState.DeployedFrayed:
                return new Vector2(0.075521f, 0.192383f);
            case TideV54NetVisualState.BrokenResidue:
                return new Vector2(0.076172f, 0.201172f);
            default:
                return new Vector2(0.076823f, 0.19043f);
        }
    }

    public static Vector2 GetRightAttachmentTopLeft01(TideV54NetVisualState state)
    {
        switch (state)
        {
            case TideV54NetVisualState.DeployedWet:
                return new Vector2(0.917318f, 0.193359f);
            case TideV54NetVisualState.DeployedFrayed:
                return new Vector2(0.91862f, 0.193359f);
            default:
                return new Vector2(0.920573f, 0.189453f);
        }
    }

    public static Vector2 GetSinkCenterTopLeft01(TideV54NetVisualState state)
    {
        switch (state)
        {
            case TideV54NetVisualState.DeployedWet:
                return new Vector2(0.505859f, 0.84375f);
            case TideV54NetVisualState.DeployedFrayed:
                return new Vector2(0.511068f, 0.842773f);
            case TideV54NetVisualState.BrokenResidue:
                return new Vector2(0.470703f, 0.84082f);
            default:
                return new Vector2(0.502604f, 0.84082f);
        }
    }

    public static Vector2 GetBundleAnchorTopLeft01(TideV54NetVisualState state)
    {
        if (state == TideV54NetVisualState.CarriedDry)
        {
            return new Vector2(0.481442f, 0.507368f);
        }

        if (state == TideV54NetVisualState.HauledWet)
        {
            return new Vector2(0.407407f, 0.172285f);
        }

        return new Vector2(0.504018f, 0.641026f);
    }

    /// <summary>
    /// 把完整网的两端绑点贴到两根悬绳终点，并让沉纲中心落在真实深度。
    /// 横向尺寸由两端实际距离决定；纵向尺寸由浮纲到沉纲的真实距离决定。
    /// 这正是“放沉纲”的形变，不是把整张图缩成另一种姿态。
    /// </summary>
    public static void ResolveDeployedTransform(
        TideV54NetVisualState state,
        Vector2 leftAttachment,
        Vector2 rightAttachment,
        float sinkCenterY,
        out Vector2 position,
        out Vector2 worldSize)
    {
        Vector2 left01 = GetLeftAttachmentTopLeft01(state);
        Vector2 right01 = GetRightAttachmentTopLeft01(state);
        Vector2 sink01 = GetSinkCenterTopLeft01(state);
        Vector2 contractSize = GetContractWorldSize(state);

        float anchorSpan01 = Mathf.Max(0.05f, right01.x - left01.x);
        float worldSpan = Mathf.Max(0.08f, rightAttachment.x - leftAttachment.x);
        float width = worldSpan / anchorSpan01;

        float headY = (leftAttachment.y + rightAttachment.y) * 0.5f;
        float sinkSpan01 = Mathf.Max(0.08f, sink01.y - (left01.y + right01.y) * 0.5f);
        float desiredHeight = Mathf.Max(0.12f, headY - sinkCenterY) / sinkSpan01;
        float widthScale = width / Mathf.Max(0.01f, contractSize.x);
        float contractHeightAtWidth = contractSize.y * widthScale;
        // 深度选择拥有最终解释权。曾经给高度设置 1.35 倍上限会让画面中的
        // 沉纲停在逻辑深度上方，玩家看到“浅网”却按“深网”结算，违反所见
        // 即所得。这里只保留最小折叠高度，不再截断真实下放距离。
        float height = Mathf.Max(desiredHeight, contractHeightAtWidth * 0.48f);

        worldSize = new Vector2(width, height);
        position = leftAttachment - TopLeft01ToLocal(left01, worldSize);
    }

    public static void ResolveBrokenTransform(
        Vector2 leftAttachment,
        float sinkCenterY,
        float deployedWidthScale,
        out Vector2 position,
        out Vector2 worldSize)
    {
        TideV54NetVisualState state = TideV54NetVisualState.BrokenResidue;
        Vector2 left01 = GetLeftAttachmentTopLeft01(state);
        Vector2 sink01 = GetSinkCenterTopLeft01(state);
        Vector2 contractSize = GetContractWorldSize(state) * Mathf.Max(0.6f, deployedWidthScale);
        float desiredHeight = Mathf.Max(0.16f, leftAttachment.y - sinkCenterY) /
            Mathf.Max(0.08f, sink01.y - left01.y);
        worldSize = new Vector2(
            contractSize.x,
            Mathf.Max(desiredHeight, contractSize.y * 0.55f));
        position = leftAttachment - TopLeft01ToLocal(left01, worldSize);
    }

    public static Vector2 ResolveBundlePosition(
        TideV54NetVisualState state,
        Vector2 targetAnchor,
        Vector2 worldSize,
        bool flippedX)
    {
        Vector2 anchor01 = GetBundleAnchorTopLeft01(state);
        if (flippedX)
        {
            anchor01.x = 1f - anchor01.x;
        }

        return targetAnchor - TopLeft01ToLocal(anchor01, worldSize);
    }

    public static Vector2 EvaluateWorldAnchor(
        Vector2 spritePosition,
        Vector2 worldSize,
        Vector2 topLeft01)
    {
        return spritePosition + TopLeft01ToLocal(topLeft01, worldSize);
    }

    private static Vector2 TopLeft01ToLocal(Vector2 topLeft01, Vector2 worldSize)
    {
        return new Vector2(
            (topLeft01.x - 0.5f) * worldSize.x,
            (0.5f - topLeft01.y) * worldSize.y);
    }
}
