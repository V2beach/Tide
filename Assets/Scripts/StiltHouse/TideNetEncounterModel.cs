using UnityEngine;

/// <summary>
/// 漂物与竖挂潮网相遇时的纯物理近似。
///
/// 网提前泡在水里只会增加湿重和受力，不会预先积攒“捕获进度”。只有同一件
/// 漂物真实穿过网口窗口，并且它在水面下的吃水带与可见网面重叠时，才累计缠挂。
/// 这里不读取场景、输入、库存或 Renderer，运行、预报和验收必须复用这套语义。
/// </summary>
public static class TideNetEncounterModel
{
    public readonly struct MaterialProfile
    {
        public MaterialProfile(
            float centerDepthBelowSurfaceMeters,
            float halfThicknessMeters,
            float minimumCoverage01,
            float requiredEffectiveContactSeconds)
        {
            CenterDepthBelowSurfaceMeters = Mathf.Max(0f, centerDepthBelowSurfaceMeters);
            HalfThicknessMeters = Mathf.Max(0.02f, halfThicknessMeters);
            MinimumCoverage01 = Mathf.Clamp01(minimumCoverage01);
            RequiredEffectiveContactSeconds = Mathf.Max(0.1f, requiredEffectiveContactSeconds);
        }

        /// <summary>实物中心在当地瞬时水面下的深度。</summary>
        public float CenterDepthBelowSurfaceMeters { get; }

        /// <summary>实物吃水带的半高，用于计算它与网面的竖向覆盖。</summary>
        public float HalfThicknessMeters { get; }

        /// <summary>低于此覆盖时，实物只是擦过网缘，不会凭概率被吸进网里。</summary>
        public float MinimumCoverage01 { get; }

        /// <summary>完整覆盖、完好网面下挂稳所需的有效接触秒数。</summary>
        public float RequiredEffectiveContactSeconds { get; }
    }

    public readonly struct Step
    {
        public Step(
            float progress01,
            float meshCoverage01,
            float windowOverlap01,
            float effectiveContactSeconds,
            bool captured,
            bool contactLost)
        {
            Progress01 = Mathf.Clamp01(progress01);
            MeshCoverage01 = Mathf.Clamp01(meshCoverage01);
            WindowOverlap01 = Mathf.Clamp01(windowOverlap01);
            EffectiveContactSeconds = Mathf.Max(0f, effectiveContactSeconds);
            Captured = captured;
            ContactLost = contactLost;
        }

        public float Progress01 { get; }
        public float MeshCoverage01 { get; }
        public float WindowOverlap01 { get; }
        public float EffectiveContactSeconds { get; }
        public bool Captured { get; }
        public bool ContactLost { get; }
        public bool HasPhysicalContact => WindowOverlap01 > 0f && MeshCoverage01 > 0f;
    }

    public static MaterialProfile GetProfile(TideDriftMaterial material)
    {
        return GetProfile(material, 0);
    }

    /// <summary>
    /// 返回同一潮源批次中第 <paramref name="pieceIndex"/> 件实物的真实吃水带。
    /// 鱼群会沿不同水层通过；这改变网面能否碰到它们，却不会改变批次身份、
    /// 材质或来源。木料和纸包仍是单件实物，不能靠深网复制。
    /// </summary>
    public static MaterialProfile GetProfile(TideDriftMaterial material, int pieceIndex)
    {
        int index = Mathf.Max(0, pieceIndex);
        switch (material)
        {
            case TideDriftMaterial.SaltWood:
                // 盐木大多贴水漂，浅网也可能截住；圆滑湿木需要更长时间卡稳。
                return new MaterialProfile(0.1f, 0.1f, 0.18f, 1.25f);
            case TideDriftMaterial.ChartParcel:
                // 油布纸包浮得最高，容易越过被高潮完全淹没的网顶。
                return new MaterialProfile(0.055f, 0.055f, 0.22f, 0.78f);
            case TideDriftMaterial.TangledDebris:
                // 缠结废网竖向范围大、容易挂住，但会显著增加后续水阻。
                return index <= 0
                    ? new MaterialProfile(0.19f, 0.19f, 0.12f, 0.72f)
                    : new MaterialProfile(0.31f, 0.17f, 0.16f, 0.82f);
            default:
                // 同一鱼群分三层通过：浅层鱼让任何合理布网都有保底，中层鱼要求
                // 中网，最深一尾只有深网能碰到。收益与风险因此都来自可见网面。
                if (index == 1)
                {
                    return new MaterialProfile(1f, 0.15f, 0.3f, 1.04f);
                }

                if (index >= 2)
                {
                    return new MaterialProfile(1.38f, 0.15f, 0.34f, 1.12f);
                }

                return new MaterialProfile(0.43f, 0.17f, 0.28f, 0.96f);
        }
    }

    /// <summary>
    /// 一次潮源批次中最多存在多少件可独立看见、独立碰网的实物。
    /// 单根木料和单个纸包不能因为等待更久而增殖；鱼群和风暴缠结物才有后续成员。
    /// </summary>
    public static int GetNaturalPieceCount(TideDriftMaterial material)
    {
        switch (material)
        {
            case TideDriftMaterial.Fish:
                return 3;
            case TideDriftMaterial.TangledDebris:
                return 2;
            default:
                return 1;
        }
    }

    /// <summary>
    /// 首件挂网后，后续成员抵达近岸可见水路前需要经历的湿网秒数。
    /// 这表示鱼群在流向上的真实间距，不是直接增加奖励的隐藏计时器。
    /// </summary>
    public static float GetFollowupReleaseExposureSeconds(
        TideDriftMaterial material,
        int pieceIndex)
    {
        int index = Mathf.Max(0, pieceIndex);
        if (index <= 0)
        {
            return 0f;
        }

        if (material == TideDriftMaterial.Fish)
        {
            return index == 1 ? 5.8f : 13.2f;
        }

        return material == TideDriftMaterial.TangledDebris ? 8.6f : float.PositiveInfinity;
    }

    /// <summary>
    /// 计算一帧运动线段有多少比例位于可见网口窗口内。它同时支持涨潮正向通过、
    /// 退潮反向通过和高水平流停留，避免低帧率时一步跨过网口而漏判。
    /// </summary>
    public static float EvaluateWindowOverlap01(float previousTravel01, float currentTravel01)
    {
        float entry = TideDriftSourceModel.NetCaptureEntryTravel01;
        float exit = TideDriftSourceModel.NetCaptureExitTravel01;
        float delta = currentTravel01 - previousTravel01;
        if (Mathf.Abs(delta) <= 0.00001f)
        {
            return TideDriftSourceModel.IsInsideNetCaptureWindow(currentTravel01) ? 1f : 0f;
        }

        float entryT = (entry - previousTravel01) / delta;
        float exitT = (exit - previousTravel01) / delta;
        float overlapStart = Mathf.Max(0f, Mathf.Min(entryT, exitT));
        float overlapEnd = Mathf.Min(1f, Mathf.Max(entryT, exitT));
        return Mathf.Clamp01(overlapEnd - overlapStart);
    }

    /// <summary>
    /// 网面在水下不是“一个接触点”，而是网顶到网底的竖向区间。高潮超过网顶时，
    /// 漂在表面的木头和纸包可以从网顶上方越过；网底太浅时，低走鱼群会从下方漏过。
    /// </summary>
    public static float EvaluateMeshCoverage01(
        float netHeadWorldY,
        float netBottomWorldY,
        float surfaceWorldY,
        MaterialProfile profile)
    {
        float topWorldY = Mathf.Max(netHeadWorldY, netBottomWorldY);
        float bottomWorldY = Mathf.Min(netHeadWorldY, netBottomWorldY);
        if (surfaceWorldY <= bottomWorldY)
        {
            return 0f;
        }

        float meshShallowDepth = Mathf.Max(0f, surfaceWorldY - topWorldY);
        float meshDeepDepth = Mathf.Max(0f, surfaceWorldY - bottomWorldY);
        if (meshDeepDepth <= meshShallowDepth + 0.0001f)
        {
            return 0f;
        }

        float objectShallowDepth = Mathf.Max(
            0f,
            profile.CenterDepthBelowSurfaceMeters - profile.HalfThicknessMeters);
        float objectDeepDepth = profile.CenterDepthBelowSurfaceMeters + profile.HalfThicknessMeters;
        float overlapMeters = Mathf.Max(
            0f,
            Mathf.Min(meshDeepDepth, objectDeepDepth) -
            Mathf.Max(meshShallowDepth, objectShallowDepth));
        float objectBandMeters = Mathf.Max(0.04f, objectDeepDepth - objectShallowDepth);
        return Mathf.Clamp01(overlapMeters / objectBandMeters);
    }

    public static Step Advance(
        float previousProgress01,
        float deltaTime,
        float previousTravel01,
        float currentTravel01,
        float netHeadWorldY,
        float netBottomWorldY,
        float surfaceWorldY,
        float netIntegrity01,
        TideDriftMaterial material,
        float guidedSubmergenceMeters = 0f,
        int pieceIndex = 0)
    {
        MaterialProfile profile = GetProfile(material, pieceIndex);
        if (guidedSubmergenceMeters > 0f)
        {
            // A visible boom/guide rope can press a floating object below its free-drift
            // waterline. This is an explicit physical input used only by that routed
            // object; net depth itself never changes the batch's material or source.
            profile = new MaterialProfile(
                profile.CenterDepthBelowSurfaceMeters + Mathf.Max(0f, guidedSubmergenceMeters),
                profile.HalfThicknessMeters,
                profile.MinimumCoverage01,
                profile.RequiredEffectiveContactSeconds);
        }
        float windowOverlap01 = EvaluateWindowOverlap01(previousTravel01, currentTravel01);
        float coverage01 = EvaluateMeshCoverage01(
            netHeadWorldY,
            netBottomWorldY,
            surfaceWorldY,
            profile);

        float coverageEfficiency01 = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(profile.MinimumCoverage01, 1f, coverage01));
        float integrityEfficiency01 = Mathf.Lerp(0.58f, 1f, Mathf.Clamp01(netIntegrity01));
        float effectiveSeconds = Mathf.Max(0f, deltaTime) *
            windowOverlap01 * coverageEfficiency01 * integrityEfficiency01;
        float candidateProgress01 = Mathf.Clamp01(
            Mathf.Max(0f, previousProgress01) +
            effectiveSeconds / profile.RequiredEffectiveContactSeconds);
        bool captured = candidateProgress01 >= 0.999f;

        // 部分缠挂在实物完整离开网口后不会保存在一条隐藏秒表里。若同一实物随
        // 退潮再次回到网口，它会从新的物理接触重新开始，而不是兑现上次的旧进度。
        bool currentInsideWindow = TideDriftSourceModel.IsInsideNetCaptureWindow(currentTravel01);
        bool contactLost = !captured && !currentInsideWindow &&
            (previousProgress01 > 0.0001f || windowOverlap01 > 0f);
        float resolvedProgress01 = contactLost ? 0f : candidateProgress01;

        return new Step(
            resolvedProgress01,
            coverage01,
            windowOverlap01,
            effectiveSeconds,
            captured,
            contactLost);
    }
}
