using UnityEngine;

/// <summary>
/// 潮水中一批实物的材质。这里故意不使用“稀有度”或“奖励等级”：
/// 网放得更深只会接触更多水，不会把鱼变成木料或遗物。
/// </summary>
public enum TideDriftMaterial
{
    Fish,
    SaltWood,
    ChartParcel,
    TangledDebris
}

/// <summary>
/// 玩家可以从世界中推断的现实来路。来源决定材质，而不是一次隐藏抽奖。
/// </summary>
public enum TideDriftProvenance
{
    TidalFlatSchool,
    OuterWreckWrack,
    OuterWreckParcel,
    StormWrackLine
}

/// <summary>
/// 第一切片只消费两条真实水路。后续增加潮沟或邻屋时扩展这里，
/// 不要再为每个新物件写一套独立 owner 状态机。
/// </summary>
public enum TideDriftLane
{
    NearshoreMain,
    OuterWreckFork
}

/// <summary>
/// 一批潮源实物的不可变描述。运行位置和所有权由场景保存，
/// 但身份、材质、来源和输运响应在生成后不能因玩家操作而改变。
/// </summary>
public readonly struct TideDriftBatch
{
    public TideDriftBatch(
        int stableId,
        TideDriftMaterial material,
        TideDriftProvenance provenance,
        TideDriftLane lane,
        float transportResponse01,
        bool canRouteToNet,
        bool canContinueToSailing)
    {
        StableId = stableId;
        Material = material;
        Provenance = provenance;
        Lane = lane;
        TransportResponse01 = Mathf.Clamp01(transportResponse01);
        CanRouteToNet = canRouteToNet;
        CanContinueToSailing = canContinueToSailing;
    }

    public int StableId { get; }
    public TideDriftMaterial Material { get; }
    public TideDriftProvenance Provenance { get; }
    public TideDriftLane Lane { get; }
    public float TransportResponse01 { get; }
    public bool CanRouteToNet { get; }
    public bool CanContinueToSailing { get; }
    public bool IsValid => StableId > 0;
}

/// <summary>
/// 当前潮次会经过高脚屋附近的最小实物集合。
/// NearshoreBatch 是自然穿过网路的主流批次；OuterWreckBatch 是玩家可以
/// 用导流索改变去向、也可以在短航中追取的外海残骸批次。
/// </summary>
public readonly struct TideDriftField
{
    public TideDriftField(TideDriftBatch nearshoreBatch, TideDriftBatch outerWreckBatch)
    {
        NearshoreBatch = nearshoreBatch;
        OuterWreckBatch = outerWreckBatch;
    }

    public TideDriftBatch NearshoreBatch { get; }
    public TideDriftBatch OuterWreckBatch { get; }
    public bool IsValid => NearshoreBatch.IsValid && OuterWreckBatch.IsValid;
}

/// <summary>
/// 潮源批次的确定性生成与近岸输运模型。它不读取场景、输入、网深、库存或 Renderer，
/// 因而同一潮次的来源不会因为玩家晚放网、切画面或重进短航而重新开奖。
/// </summary>
public static class TideDriftSourceModel
{
    public const float NetCaptureEntryTravel01 = 0.94f;
    public const float NetIntersectionTravel01 = 1f;
    public const float NetCaptureExitTravel01 = 1.08f;
    public const float NearshoreExitTravel01 = 1.24f;
    // The outer wreck lane forks away from the net before reaching the nearshore main
    // line. An open boom therefore enters the short-sailing water shortly after the
    // 0.72 fork decision; it must not be forced through the net's 0.94-1.08 window.
    public const float OuterOpenRouteExitTravel01 = 0.84f;

    public static TideDriftField BuildField(
        int astronomicalCycleOrdinal,
        float moonAgeDays,
        float tideStrength01,
        float stormPressure01,
        bool outerWreckRouteKnown)
    {
        TideDriftBatch nearshore = BuildNearshoreBatch(
            astronomicalCycleOrdinal,
            moonAgeDays,
            tideStrength01,
            stormPressure01,
            outerWreckRouteKnown);
        TideDriftBatch wreck = new TideDriftBatch(
            BuildStableId(astronomicalCycleOrdinal, TideDriftLane.OuterWreckFork, TideDriftMaterial.SaltWood),
            TideDriftMaterial.SaltWood,
            TideDriftProvenance.OuterWreckWrack,
            TideDriftLane.OuterWreckFork,
            0.62f,
            true,
            true);
        return new TideDriftField(nearshore, wreck);
    }

    public static TideDriftBatch BuildNearshoreBatch(
        int astronomicalCycleOrdinal,
        float moonAgeDays,
        float tideStrength01,
        float stormPressure01,
        bool outerWreckRouteKnown)
    {
        float tide = Mathf.Clamp01(tideStrength01);
        float storm = Mathf.Clamp01(stormPressure01);
        TideDriftMaterial material;
        TideDriftProvenance provenance;
        float response;

        // 第一潮必须稳定教会“潮滩鱼群进入旧网路”，不能因启动时刻的微小
        // 月龄差或天气插值变成另一份教程。后续来源才由实际海况决定。
        if (astronomicalCycleOrdinal <= 0)
        {
            material = TideDriftMaterial.Fish;
            provenance = TideDriftProvenance.TidalFlatSchool;
            response = 0.94f;
        }
        else if (storm >= 0.72f)
        {
            // 强风浪先把沿岸废网、断绳和轻垃圾卷成一条可见漂积线。
            material = TideDriftMaterial.TangledDebris;
            provenance = TideDriftProvenance.StormWrackLine;
            response = 0.78f;
        }
        else if (tide >= 0.82f && outerWreckRouteKnown)
        {
            // 只有玩家已经确认外海残骸方向，且大潮真正触及残骸高位时，
            // 被油布包住的方位纸才可能进入近岸主流。
            material = TideDriftMaterial.ChartParcel;
            provenance = TideDriftProvenance.OuterWreckParcel;
            response = 0.54f;
        }
        else if (tide >= 0.82f || storm >= 0.38f)
        {
            // 大潮或正在逼近的风浪会松动残骸外缘，但还不足以说明纸包来源。
            material = TideDriftMaterial.SaltWood;
            provenance = TideDriftProvenance.OuterWreckWrack;
            response = 0.64f;
        }
        else
        {
            material = TideDriftMaterial.Fish;
            provenance = TideDriftProvenance.TidalFlatSchool;
            response = 0.94f;
        }

        return new TideDriftBatch(
            BuildStableId(astronomicalCycleOrdinal, TideDriftLane.NearshoreMain, material),
            material,
            provenance,
            TideDriftLane.NearshoreMain,
            response,
            false,
            false);
    }

    /// <summary>
    /// 返回一件实物在“首潮参考峰值”下走完整条归一化水路所需的标尺秒数。
    /// referenceTideStrength01 只用于兼容已经验收的首潮路线长度；调用方不得
    /// 把当前潮强传进来，因为当前月相已经包含在实际 m/s 潮流中。
    /// </summary>
    public static float EvaluateReferenceTransportSeconds(
        TideDriftBatch batch,
        float referenceTideStrength01,
        float stormPressure01)
    {
        float baseSeconds;
        switch (batch.Material)
        {
            case TideDriftMaterial.ChartParcel:
                baseSeconds = 42f;
                break;
            case TideDriftMaterial.SaltWood:
                baseSeconds = 38f;
                break;
            case TideDriftMaterial.TangledDebris:
                baseSeconds = 35f;
                break;
            default:
                baseSeconds = 32f;
                break;
        }

        float referenceCurrentFactor = Mathf.Lerp(
            1.16f,
            0.8f,
            Mathf.Clamp01(referenceTideStrength01));
        float stormFactor = Mathf.Lerp(1f, 0.9f, Mathf.Clamp01(stormPressure01));
        float responseFactor = Mathf.Lerp(1.12f, 0.9f, batch.TransportResponse01);
        return Mathf.Clamp(
            baseSeconds * referenceCurrentFactor * stormFactor * responseFactor,
            22f,
            48f);
    }

    /// <summary>
    /// 把实际流速换算成相对首潮峰值的带方向倍率。大潮可以自然超过 1；
    /// 这里只做极端坏数据保护，不能把真实大潮重新钳回首潮强度。
    /// </summary>
    public static float EvaluateRelativePhysicalCurrent(
        float signedSpeedMetersPerSecond,
        float referenceFloodPeakSpeedMetersPerSecond)
    {
        float referenceSpeed = Mathf.Max(0.01f, Mathf.Abs(referenceFloodPeakSpeedMetersPerSecond));
        return Mathf.Clamp(signedSpeedMetersPerSecond / referenceSpeed, -2.5f, 2.5f);
    }

    public static float EvaluateReachableTravel01(float nearshoreWaterGate01)
    {
        float gate = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(nearshoreWaterGate01));
        return Mathf.Lerp(0.08f, NearshoreExitTravel01, gate);
    }

    public static float AdvanceNearshoreTravel01(
        float currentTravel01,
        float deltaTime,
        float signedTowardNetSpeedMetersPerSecond,
        float referenceFloodPeakSpeedMetersPerSecond,
        float nearshoreWaterGate01,
        TideDriftBatch batch,
        float referenceTideStrength01,
        float stormPressure01)
    {
        float duration = EvaluateReferenceTransportSeconds(
            batch,
            referenceTideStrength01,
            stormPressure01);
        float waterGate = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(nearshoreWaterGate01));
        float relativePhysicalCurrent = EvaluateRelativePhysicalCurrent(
            signedTowardNetSpeedMetersPerSecond,
            referenceFloodPeakSpeedMetersPerSecond);
        float signedRate = relativePhysicalCurrent *
            Mathf.Lerp(0.46f, 1.08f, waterGate) /
            Mathf.Max(0.1f, duration);
        float next = Mathf.Max(0f, currentTravel01) + Mathf.Max(0f, deltaTime) * signedRate;
        if (signedRate > 0f)
        {
            // Rising water opens progressively farther parts of the nearshore route.
            // A falling water gate must never clamp an already visible object backward;
            // only the signed current is allowed to reverse its physical travel.
            float reachableTravel01 = EvaluateReachableTravel01(nearshoreWaterGate01);
            next = Mathf.Min(next, Mathf.Max(currentTravel01, reachableTravel01));
        }

        return Mathf.Clamp(next, 0f, NearshoreExitTravel01);
    }

    public static bool HasReachedNet(float travel01)
    {
        return travel01 >= NetIntersectionTravel01 - 0.001f;
    }

    public static bool IsInsideNetCaptureWindow(float travel01)
    {
        return travel01 >= NetCaptureEntryTravel01 - 0.001f &&
            travel01 <= NetCaptureExitTravel01 + 0.001f;
    }

    public static bool HasExitedNearshore(float travel01)
    {
        return travel01 >= NearshoreExitTravel01 - 0.001f;
    }

    public static bool HasExitedOpenOuterRoute(float travel01)
    {
        return travel01 >= OuterOpenRouteExitTravel01 - 0.001f;
    }

    private static int BuildStableId(
        int astronomicalCycleOrdinal,
        TideDriftLane lane,
        TideDriftMaterial material)
    {
        int cycle = Mathf.Max(0, astronomicalCycleOrdinal) + 1;
        return cycle * 100 + ((int)lane + 1) * 10 + (int)material + 1;
    }
}
