using System;
using System.Collections.Generic;
using UnityEngine;
using RepairChoice = TideRepairTarget;

/// <summary>
/// Tide 新方向的独立第一版场景控制器。
/// 这个场景从低潮布网开始，不复用旧平台关卡的返窗闭环。
/// 核心循环是：选网线 -> 等潮水冲网 -> 退潮回收 -> 用收获修屋/修船 -> 月相推进。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public partial class TideStiltHouseFirstSliceController : MonoBehaviour
{
    private const string FirstSliceResourceRoot = "StiltFirstSlice/";
    private const string FormalAiResourceRoot = "StiltFirstSliceAI/";
    // TideWaterBodyHDChromaV1 keeps transparent sky in the upper ~47% of its canvas.
    // At the 2.7 m runtime height, the first substantial painted crest therefore sits
    // about 1.26 m above the sprite centre. 1.30 m aligns the authored average crest
    // with TideOceanSample.SurfaceY; the former 0.45 m value drew the sea 0.85 m below
    // every boat, swimmer and floating object and made them visibly hover in the air.
    private const float FormalWaterAverageCrestOffset = 1.30f;
    private const bool FormalBoatFacesRight = true;
    // The fixed pier must stop far enough from the moving stern to leave a shallow
    // working gangplank. A 0.12 m horizontal gap combined with the normal 0.4 m
    // water-level difference turned the plank almost vertical, so it read as an
    // unexplained post instead of a surface a person could actually cross.
    private const float PierToRestingSternGap = 0.78f;
    // 归处与泊位属于同一连续世界。固定木路按两段 V53 原生跨度铺设，
    // 船艉位于第二段末端之外，最后只留一块会随船升沉的活动跳板。
    private const float OutdoorBoatAnchorX = 9.65f;
    private const float EscapeBoatStagingX = OutdoorBoatAnchorX - 1.42f;
    private const float OutdoorCameraFollowStartX = 2.9f;
    private const float OutdoorCameraFollowSpeed = 6.5f;
    private const float OutdoorWaterCenterX = 3.1f;
    private const float OutdoorWaterWidth = 27.6f;
    // 首潮已经完成布网、岔流、短航和修船的自然时序验收。它作为潮流兼容
    // 校准点：月相更接近弦月时流速更小，接近朔望时更大，但首潮本身不跳变。
    private const float OpeningMoonAgeDays = 3.5f;
    private const float OpeningTidePhase01 = 0.04f;
    // 月相和月球赤纬是两个独立周期。这个开场相位让本月满月附近出现明显
    // 日不等潮，同时保持连续天数推进，不能用会回绕的 moonAgeDays 代替。
    private const float OpeningLunarDeclinationAgeDays = 22.887125f;
    private const float MeanTidalTransportSpeed = 0.3f;
    private const bool EnableSailingBuoyGameplay = false;
    private const float EbbCurrentBoost = 1.12f;
    private const float StormRescueWaterContainerLiters = 4f;
    private const int StormRescueBoatTimberUnits = 2;
    private const int StartingDryFuelBundles = 1;
    private const float DailyRestWaterNeedLiters = 2.4f;
    // 盐湿线记住的是一段潮汐的平均最高水位，不是某一朵随机浪尖。睡眠跳时
    // 用 0.5 秒小步补采样，足以捕捉 248.4 秒潮周期的平滑峰值，同时不把
    // 八分钟游戏日的宏观压缩误用到呼吸、动作或局部海浪上。
    private const float HighWaterMemorySampleStepSeconds = 0.5f;
    // 与正式 V34 高脚屋四根可见主桩对齐。潮痕只贴在这些已有木面上，
    // 不跨空隙画横线，也不创建能被误读成机关或平台的世界物件。
    private static readonly float[] PreviousSaltMarkPostX = { -5.36f, -4.37f, -2.4f, -0.42f };
    // Interior lane endpoints describe visible floor edges. The logical pivot may not
    // reach those edges because the survivor still has shoulders and a carried bundle.
    private const float InteriorBodyHalfWidth = 0.24f;
    // V32/V37 use a 1024px character canvas, not the 6144px boat canvas. Reusing the
    // boat's 0.42 root scale made a seated adult child-sized. The visual pivot also
    // sits below the authored hip, so it needs a small rotated lift above the seat pin.
    private const float BoatPassengerUniformScale = 0.68f;
    // 深船舷应遮住小腿和脚，但不能把人物截在腰部。略抬高座位视觉 Pivot，
    // 让髋部与近侧膝部越过舷缘可读，仍保持双脚位于真实船舱内。
    private const float BoatPassengerSeatLift = 0.24f;
    private const float BoatPassengerSeatForward = 0.05f;
    private static readonly float[] V40WreckWaterlineTopLeftY = { 1056f, 1066f, 1066f, 1073f, 1082f, 1052f };
    private static readonly int[] V40ArrivalWreckVariants = { 0, 2, 5 };

    private enum SliceState
    {
        LowTidePlanning,
        TideRising,
        EbbCollect,
        RepairMoment,
        FinalDeparture
    }

    private enum NetLine
    {
        Low,
        Mid,
        High
    }

    private enum SliceViewMode
    {
        Shelter,
        Interior,
        Sailing,
        Lookout
    }

    // 船与房屋都通过同一套“走到真实门槛 -> 暗点换视图 -> 从对应门槛落地”过渡。
    // 旧名字保留是为了避免无行为收益的序列化字段迁移；职责已经是通用视图交接器。
    private enum BoatViewTransition
    {
        None,
        EnteringMooring,
        ExitingMooring,
        Boarding,
        Returning,
        EnteringInterior,
        ExitingInterior,
        EnteringLookout,
        ExitingLookout
    }

    private enum SurvivalPresentationState
    {
        None,
        Sleeping,
        Drowning,
        ColdCollapse
    }

    private enum WalkLane
    {
        Deck,
        TideFlat,
        InteriorLower,
        InteriorUpper,
        InteriorLoft
    }

    private enum DayNightPhase
    {
        Dawn,
        Day,
        Dusk,
        Night
    }

    private enum HarvestKind
    {
        None,
        Fish,
        Wood,
        Relic,
        Trash
    }

    private enum HarvestPhysicalState
    {
        None,
        Drifting,
        CaughtInNet,
        SecuredAtPost,
        Carried,
        PlacedAtWork,
        Stored,
        Lost
    }

    private enum NetRigStep
    {
        Stored,
        Carrying,
        FirstEndTied,
        Unrolled,
        SecondEndTied,
        Lowering,
        Deployed
    }

    private enum TidePrepChoice
    {
        None,
        Rope,
        Bucket,
        Stake
    }

    private enum TideRoutingMode
    {
        Open,
        FeedNet
    }

    private enum ExtraSaltWoodOwner
    {
        PassingNearshore,
        SailingWater,
        RoutedToNet,
        CaughtInNet,
        SecuredAtPost,
        Carried,
        PlacedAtWork,
        HookingToBoat,
        HookedToBoat,
        ReturnedAtBoat,
        StagedAtMooring,
        Claimed
    }

    private enum SailingPointKind
    {
        Buoy,
        Salvage,
        Wreck,
        Reef
    }

    [Header("Scene Layout")]
    [SerializeField] private Vector2 houseAnchor = new Vector2(-3.5f, 0.82f);
    [SerializeField] private Vector2 netAnchor = new Vector2(1.85f, 0.34f);
    [SerializeField] private Vector2 boatAnchor = new Vector2(OutdoorBoatAnchorX, -1.82f);
    [SerializeField] private Vector2 moonAnchor = new Vector2(4.55f, 2.85f);
    [SerializeField] private Vector2 lighthouseAnchor = new Vector2(5.8f, 0.48f);
    [SerializeField] private float lowWaterY = -2.82f;
    [SerializeField] private float highWaterY = 0.03f;
    [SerializeField] private float visualZ = 0f;

    [Header("Timing")]
    [SerializeField] private float tideCycleSeconds = 248.4f;
    [SerializeField] private float repairSeconds = 2.25f;
    [SerializeField] private float dayLengthSeconds = 480f;
    [SerializeField] private float arrivalVignetteSeconds = 5.8f;
    [SerializeField] private float boatViewTransitionSeconds = 1.2f;

    [Header("Player")]
    [SerializeField] private float playerMoveSpeed = 2.4f;
    [SerializeField] private float playerGroundAcceleration = 14f;
    [SerializeField] private float playerGroundDeceleration = 18f;
    [SerializeField] private float playerTurnAcceleration = 24f;
    [SerializeField] private float playerSwimAcceleration = 4.8f;
    [SerializeField] private float playerSwimDeceleration = 3.2f;
    [SerializeField] private float ladderTravelSpeed = 0.72f;
    [SerializeField] private float netRigHoldSeconds = 0.8f;
    [SerializeField] private float netLoweringSeconds = 1.35f;
    [SerializeField] private float netHaulSeconds = 3.4f;
    [SerializeField] private float netPostCatchFullLoadSeconds = 24f;
    [SerializeField] private float liveNetDepthAdjustSpeed = 0.22f;
    [SerializeField] private float tidePrepHoldSeconds = 1.15f;
    [SerializeField] private float contextDistance = 0.72f;
    [SerializeField] private float netRigInteractionDistance = 0.3f;
    [SerializeField] private float arrivalWreckX = 2.66f;
    [SerializeField] private float arrivalWreckInteractionDistance = 0.28f;
    [SerializeField] private Sprite[] v40ShipwreckOriginSprites = Array.Empty<Sprite>();
    [SerializeField] private Sprite[] v39DamagedBoatLayers = Array.Empty<Sprite>();
    [SerializeField] private Sprite[] v39RepairedBoatLayers = Array.Empty<Sprite>();
    [SerializeField] private float playerMinX = -5.15f;
    [SerializeField] private float playerMaxX = 10.6f;
    [SerializeField] private float laneSwitchX = -1.78f;
    [SerializeField] private float deckLaneMinX = -5.15f;
    [SerializeField] private float deckLaneMaxX = -1.68f;
    [SerializeField] private float tideFlatLaneMinX = -1.2f;
    [SerializeField] private float tideFlatLaneMaxX = 10.6f;
    [SerializeField] private float boatBoardDistance = 0.48f;
    [SerializeField] private float boatRepairDistance = 0.34f;
    [SerializeField] private float interiorRepairDistance = 0.32f;
    [SerializeField] private float shoreWorkX = -0.48f;
    [SerializeField] private float shoreWorkDistance = 0.64f;
    [SerializeField] private float shoreWorkMaxWaterOffset = 1.22f;
    [SerializeField] private float sailingAcceleration = 4.8f;
    [SerializeField] private float sailingDrag = 3.4f;
    [SerializeField] private float sailingMaxSpeed = 2.75f;
    [SerializeField] private float sailingHomeX = -4.8f;
    [SerializeField] private float sailingHomeY = -1.18f;
    [SerializeField] private float sailingTargetX = 30.2f;
    [SerializeField] private float sailingMinX = -5.25f;
    [SerializeField] private float sailingMaxX = 31.4f;
    [SerializeField] private float routeVortexX = 24.6f;
    [SerializeField] private float earlyWreckClueX = 12.2f;
    [SerializeField] private Vector2 sailingBuoyPoint = new Vector2(-0.85f, -0.72f);
    [SerializeField] private Vector2 sailingSalvagePoint = new Vector2(5.15f, -0.88f);
    [SerializeField] private Vector2 sailingReefPoint = new Vector2(2.15f, -1.08f);
    [SerializeField] private float sailTripSeconds = 75f;
    [SerializeField] private float sailingTrimSpeed = 0.9f;
    [SerializeField] private float sailingBailRate = 0.22f;
    [SerializeField] private float sailingWindMaxSpeed = 0.58f;
    [SerializeField] private float stormFrontArrivalDays = 3f;
    [SerializeField] private float finalDepartureStormThreshold = 0.88f;
    [SerializeField] private float sailingHookMaxRelativeSpeed = 0.58f;
    [SerializeField] private float sailingHookReach = 1.38f;

    [Header("Survival")]
    [SerializeField] private float coldWaterWarmthLossPerSecond = 0.018f;
    [SerializeField] private float dryWarmthRecoveryPerSecond = 0.012f;
    [SerializeField] private float stoveWarmthRecoveryPerSecond = 0.034f;
    [SerializeField] private float drowningGraceSeconds = 7f;

    [Header("First Slice Goal")]
    [SerializeField] private int requiredLighthouseClues = 2;
    [SerializeField] private int requiredBoatReadiness = 3;
    [SerializeField] private int departureStormRound = 2;
    [SerializeField] private float finalDepartureSeconds = 5.6f;

    [Header("Tone")]
    [SerializeField] private Color deepSeaColor = new Color(0.055f, 0.14f, 0.15f, 1f);
    [SerializeField] private Color wetWoodColor = new Color(0.5f, 0.3f, 0.18f, 1f);
    [SerializeField] private Color saltWoodColor = new Color(0.76f, 0.66f, 0.5f, 1f);
    [SerializeField] private Color warmLampColor = new Color(1f, 0.58f, 0.2f, 1f);
    [SerializeField] private Color tideColor = new Color(0.1f, 0.57f, 0.68f, 0.82f);
    [SerializeField] private Color dangerTideColor = new Color(0.52f, 0.09f, 0.06f, 0.66f);

    private readonly List<SpriteRenderer> generatedRenderers = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> stiltPosts = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> netLines = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> netWeights = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> netKnots = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> netFloats = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> netSuspensionRopes = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> netCaughtItems = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> incomingTideCarryItems = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> harvestCarryItems = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> washedAwayHarvestItems = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> netDamageMarkers = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> waveStrips = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> houseRepairMarks = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> houseSaltStreaks = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> shelterTideZoneWearRenderers = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> previousHighWaterSaltMarks = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> shelterWarmRays = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> houseDiagonalBraces = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> houseGableRoofs = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> denseStiltPosts = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> v30RepairOwnerRenderers = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> v34ExteriorRepairOwnerRenderers = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> v35InteriorRepairOwnerRenderers = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> brokenHousePieces = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> snappedStiltPosts = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> departureRouteWakes = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> laundryCloths = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> pillarCheckKnots = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> shipwreckRibs = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> boatRigging = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> boatPatchStitches = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> waterWracks = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> sailingFlowCrests = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> vortexCrests = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> cloudRenderers = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> horizonCloudBanks = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> farSeaBands = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> nearSeaColorBands = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> dayNightSkyBands = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> weatherRainStreaks = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> mooringRopeSegments = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> stormRescueRenderers = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> stormRescueRopeRenderers = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> formalBoardwalkSegments = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> interiorWallPlanks = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> interiorWindowRenderers = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> interiorStairTreads = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> interiorLoftStairTreads = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> interiorLoftRafters = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> interiorStoredItems = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> houseRoofRepairMarks = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> houseWallRepairMarks = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> houseInteriorVoidRenderers = new List<SpriteRenderer>();
    private readonly List<SpriteMask> houseRoofHoleMasks = new List<SpriteMask>();
    private readonly List<SpriteMask> houseWindowHoleMasks = new List<SpriteMask>();
    private readonly List<SpriteMask> houseWallGapMasks = new List<SpriteMask>();
    private SpriteMask netRevealMask;
    private bool v54DeployedPoseValid;
    private Vector2 v54DeployedPosition;
    private Vector2 v54DeployedWorldSize;

    private SpriteRenderer backdropRenderer;
    private SpriteRenderer daySeaSkyRenderer;
    private SpriteRenderer nightSeaSkyRenderer;
    private SpriteRenderer ambientMoonWashRenderer;
    private SpriteRenderer foregroundMoonWashRenderer;
    private SpriteRenderer boatViewTransitionRenderer;
    private SpriteRenderer waterRenderer;
    private SpriteRenderer waterFoamRenderer;
    private SpriteRenderer naturalWaterSurfaceRenderer;
    private SpriteRenderer foregroundWaterOcclusionRenderer;
    private SpriteRenderer foregroundDeepWaterOcclusionRenderer;
    private SpriteRenderer shelterWaveImpactRenderer;
    private SpriteRenderer shelterDamageRenderer;
    private SpriteRenderer netWaterContactRenderer;
    private SpriteRenderer formalNetRenderer;
    private SpriteRenderer formalNetBundleRenderer;
    private SpriteRenderer netHandlingRopeRenderer;
    private SpriteRenderer houseRenderer;
    private SpriteRenderer verandaRenderer;
    private SpriteRenderer roofRenderer;
    private SpriteRenderer porchRoofRenderer;
    private SpriteRenderer longEaveRenderer;
    private SpriteRenderer underHouseShadowRenderer;
    private SpriteRenderer stormSurgeWallRenderer;
    private SpriteRenderer deckRenderer;
    private SpriteRenderer windowGlowRenderer;
    private SpriteRenderer playerRenderer;
    private SpriteRenderer playerAquaticRenderer;
    private SpriteRenderer playerGlowRenderer;
    private SpriteRenderer playerSwimWakeRenderer;
    private SpriteRenderer boatGlowRenderer;
    private SpriteRenderer boatBackRigRenderer;
    private SpriteRenderer boatHullRenderer;
    private SpriteRenderer boatSailRenderer;
    private SpriteRenderer boatLandingWalkwayRenderer;
    private SpriteRenderer boatWakeRenderer;
    private SpriteRenderer boatWaterlineOcclusionRenderer;
    private SpriteRenderer boatPassengerGunwaleRenderer;
    private SpriteRenderer boatRudderRenderer;
    private SpriteRenderer boatCockpitRenderer;
    private SpriteRenderer boatHullRepairOwnerRenderer;
    private SpriteRenderer boatCockpitRepairOwnerRenderer;
    private SpriteRenderer boatPassengerRenderer;
    private SpriteRenderer mooringRopeEndRenderer;
    private SpriteRenderer lighthouseRenderer;
    private SpriteRenderer lighthouseBeamRenderer;
    private SpriteRenderer moonRenderer;
    private SpriteRenderer moonShadowRenderer;
    private SpriteRenderer sunRenderer;
    private SpriteRenderer harvestRenderer;
    private SpriteRenderer stormLineRenderer;
    private SpriteRenderer lighthouseChartClueRenderer;
    private SpriteRenderer stormPennantRenderer;
    private SpriteRenderer departureSignalRenderer;
    private SpriteRenderer tideRoutingRopeRenderer;
    private SpriteRenderer tideRoutingBoomRenderer;
    private SpriteRenderer tideRoutingWinchRenderer;
    private SpriteRenderer horizonSeaRenderer;
    private SpriteRenderer boardwalkPathRenderer;
    private SpriteRenderer tideFlatPathRenderer;
    private SpriteRenderer sailingSeaRenderer;
    private SpriteRenderer sailingWreckPointRenderer;
    private SpriteRenderer sailingBuoyPointRenderer;
    private SpriteRenderer sailingBuoyTetherRenderer;
    private SpriteRenderer sailingBuoySinkerRenderer;
    private SpriteRenderer sailingSalvagePointRenderer;
    private SpriteRenderer sailingSalvageWakeRenderer;
    private SpriteRenderer sailingSalvageHookRopeRenderer;
    private SpriteRenderer sailingSalvageHookRopeEndRenderer;
    private SpriteRenderer sailingSalvageHookRenderer;
    private SpriteRenderer sailingReefPointRenderer;
    private SpriteRenderer sailingReefFoamRenderer;
    private SpriteRenderer sailingRangeBreakerRenderer;
    private SpriteRenderer boatIngressWaterRenderer;
    private SpriteRenderer sailingBailBucketRenderer;
    private SpriteRenderer sailingBailSplashRenderer;
    private SpriteRenderer routeVortexRenderer;
    private SpriteRenderer routeVortexInnerRenderer;
    private SpriteRenderer routeVortexSurfaceRenderer;
    private SpriteRenderer returnPressureWashRenderer;
    private SpriteRenderer prepRopeCoilRenderer;
    private SpriteRenderer prepBucketRenderer;
    private SpriteRenderer prepStakeRenderer;
    private SpriteRenderer interiorBackdropRenderer;
    private SpriteRenderer interiorLowerFloorRenderer;
    private SpriteRenderer interiorUpperFloorRenderer;
    private SpriteRenderer interiorLoftBackdropRenderer;
    private SpriteRenderer interiorLoftFloorRenderer;
    private SpriteRenderer interiorStairRenderer;
    private SpriteRenderer interiorLoftStairRenderer;
    private SpriteRenderer interiorLoftLookoutTableRenderer;
    private SpriteRenderer interiorLoftLookoutRenderer;
    private SpriteRenderer interiorUpperWarmthRenderer;
    private SpriteRenderer interiorRoofCapRenderer;
    private SpriteRenderer interiorBedFrameRenderer;
    private SpriteRenderer interiorBedHeadboardRenderer;
    private SpriteRenderer interiorBedRenderer;
    private SpriteRenderer interiorLampRenderer;
    private SpriteRenderer interiorStoveRenderer;
    private SpriteRenderer interiorStoveGlowRenderer;
    private SpriteRenderer interiorRoofPatchRenderer;
    private SpriteRenderer interiorStorageRenderer;
    private SpriteRenderer interiorFloodRenderer;
    private SpriteRenderer lookoutVistaMaskRenderer;
    private SpriteRenderer lookoutVistaRoofRenderer;
    private SpriteRenderer lookoutVistaCraneRenderer;
    private SpriteRenderer lookoutVistaWreckRenderer;
    private SpriteRenderer lookoutVistaLighthouseRenderer;
    private SpriteRenderer lookoutVistaBeamRenderer;
    private SpriteMask interiorCutawayMask;
    private SpriteMask playerBagMask;
    private SpriteMask boatPassengerBagMask;
    private SpriteMask boatWaterlineMask;
    private TextMesh titleText;
    private TextMesh statusText;
    private TextMesh loopGoalText;
    private TextMesh controlText;
    private TextMesh boatBoardPromptText;

    private SliceState state = SliceState.LowTidePlanning;
    private SliceViewMode viewMode = SliceViewMode.Shelter;
    private BoatViewTransition boatViewTransition = BoatViewTransition.None;
    private float boatViewTransitionTimer;
    private bool boatViewTransitionSwitched;
    private Vector2 boatViewTransitionStartPosition;
    private Vector2 boatViewTransitionEndPosition;
    // The shelter and its mooring are two camera sectors of one physical outdoor world.
    // Keeping this separate from SliceViewMode means tide, cargo, repair state and player
    // coordinates never get duplicated merely because a narrow frame cannot show both.
    private bool mooringScreenActive;
    private SurvivalPresentationState survivalPresentationState;
    private float survivalPresentationTimer;
    private Vector2 survivalPresentationStartPosition;
    private SliceViewMode survivalPresentationStartView;
    private string pendingDeathReason = string.Empty;
    private WalkLane playerLane = WalkLane.Deck;
    private DayNightPhase dayNightPhase = DayNightPhase.Day;
    private NetLine selectedNetLine = NetLine.Mid;
    private TidePrepChoice selectedPrepChoice = TidePrepChoice.None;
    private HarvestKind currentHarvest = HarvestKind.None;
    // A catch tied to the home net post is no longer in the player's hands. Keep its
    // identity and bundle size in a dedicated world slot so a separately salvaged
    // timber bundle can be carried without overwriting or silently banking the catch.
    private HarvestKind securedPostHarvest = HarvestKind.None;
    private int securedPostBundleTier;
    private int securedPostVisualPieceCount;
    // 潮源模型拥有“它原本是什么”；场景状态只拥有“它现在在哪里”。
    // 主流批次和外海残骸批次各有独立 ID，混装进同一张网时也不能相互覆盖。
    private TideDriftField currentTideDriftField;
    private bool tideDriftFieldInitialized;
    private int tideDriftFieldCycleOrdinal = -1;
    private int tideSourceBatchId;
    private int currentHarvestBatchId;
    private int securedPostHarvestBatchId;
    private int extraSaltWoodBatchId;
    private int washedAwayHarvestBatchId;
    private bool primarySourcePassedNearshore;
    private float previousOuterWreckTravel01;
    private float outerWreckTravel01;
    private float outerNetCaptureProgress01;
    private bool outerWreckPassedNearshore;
    private HarvestKind tideSourceHarvest = HarvestKind.None;
    private HarvestPhysicalState harvestPhysicalState = HarvestPhysicalState.None;
    private HarvestKind washedAwayHarvestKind = HarvestKind.None;
    private float stateTimer;
    private float finalDepartureStartWaterY;
    private float tideClockSeconds;
    private float currentWaterY;
    private bool tideCurrentlyRising;
    private TideHighWaterMemoryModel.State highWaterMemory;
    private float dayProgress01 = 0.28f;
    private float dayClockSeconds = 9.5f;
    private float weatherClockSeconds;
    private float worldElapsedRealSeconds;
    private float moonAgeDays = OpeningMoonAgeDays;
    private float tideStrength = 0.55f;
    private int tideRound;
    private int houseWarmth;
    private int boatReadiness;
    private int stiltIntegrity = 1;
    private int roofIntegrity;
    private int interiorComfort;
    private int interiorSealCondition;
    private int workbenchCondition;
    private int bedCondition;
    private int chartRadioCondition;
    private int stoveCondition;
    private int boatHullIntegrity = 1;
    private int boatSailIntegrity;
    private int boatCabinIntegrity;
    private int timberStock;
    private int ropeStock;
    private int clothStock;
    private int metalStock;
    private int foodStock;
    private bool currentHarvestBanked;
    private bool currentHarvestFromWrack;
    private bool hasSalvageBag;
    private int lighthouseClues;
    private bool lighthouseSeen;
    private int routeClueReturnRound = -1;
    private int netIntegrity = 3;
    private int currentTideNetStress;
    private bool netBrokeThisTide;
    private bool netTouched;
    private Vector2 playerPosition;
    private int playerFacing = 1;
    private bool playerMoving;
    private float playerWalkCycle;
    private float playerHorizontalVelocity;
    private float playerCurrentDriftVelocity;
    private float laneTransitionEntrySpeed01;
    private bool playerSwimming;
    private float playerSubmersion01;
    private float playerAquaticBlend01;
    private float bodyWarmth01 = 1f;
    private float drowningSeconds;
    private int deathCount;
    private float deathFeedbackTimer;
    private string lastDeathReason = string.Empty;
    private bool arrivalInspected;
    private bool netDeployed;
    private NetRigStep netRigStep = NetRigStep.Stored;
    private float netUnrollProgress;
    private float netRigActionProgress;
    private bool netRigActionHeld;
    private bool netRigActionStarted;
    private float netLoweringProgress;
    private float netHaulProgress;
    private float netHaulStrokePhase;
    private float netHaulEffort01;
    private float netHaulLoad01;
    private bool editorNetHaulPreviewActive;
    // Stored/preview state needs a stable geometric reference before the player starts
    // lowering the weighted line. It is not a recommended depth: picking up the net
    // resets the real lowering progress to zero and only the release point is committed.
    private const float StoredNetReferenceDepth01 = 0.16f;
    private float netSetDepth01 = StoredNetReferenceDepth01;
    private float netWaterExposureSeconds;
    private float netCaptureProgress01;
    private float previousIncomingHarvestTravel01;
    private float netAccumulatedTension;
    private float netPeakTension01;
    private float netFraying01;
    private bool netDepthAdjustmentActive;
    private float netDepthAdjustmentDirection;
    private bool netCatchResolved;
    private float netPostCatchExposureSeconds;
    private int netCatchBundleTier = 1;
    private int netCatchVisualPieceCount;
    private float incomingHarvestTravel01;
    private float addedHarvestPieceTravel01;
    private int washedAwayHarvestPieceCount;
    private float washedAwayHarvestTimer;
    private float washedAwayHarvestDriftX;
    private int netOverloadStressApplied;
    private bool netSecuredEarly;
    private bool isLaneTransitioning;
    private WalkLane laneTransitionFromLane;
    private WalkLane laneTransitionTarget;
    private float laneTransitionProgress;
    private float laneTransitionDurationSeconds = 1f;
    private Vector2 laneTransitionFromPosition;
    private Vector2 laneTransitionToPosition;
    private bool sailTripActive;
    private float sailTripTimer;
    private float sailingBoatX;
    private float sailingBoatLaneY;
    private float sailingBoatVelocity;
    private float sailingBoatWorldVelocity;
    private float sailingFlowCrestTravelWorld;
    private float sailingWaterIngress01;
    private float sailingSailTrim01;
    private TideSailboatDynamicsState sailingDynamics;
    private float sailingBallastInput;
    private float sailingBailCycle;
    private float sailingTrimActionTimer;
    private float sailingTrimCycle;
    private float sailingBailedWaterThisTrip;
    private bool sailingBailing;
    private bool sailingIngressMidWarned;
    private bool sailingIngressHighWarned;
    private bool sailingRangeBlockedThisTrip;
    private float mooredBoatOffsetFallback;
    private bool sailingClueCollected;
    private bool sailingRewardPending;
    private bool sailingClueWasLighthouse;
    private bool sailingBuoyChecked;
    private bool sailingSalvageCollected;
    private bool sailingSalvageRewardPending;
    private bool sailingSalvageClaimed;
    private ExtraSaltWoodOwner extraSaltWoodOwner = ExtraSaltWoodOwner.PassingNearshore;
    private bool extraSaltWoodBundledWithNetHarvest;
    private bool sailingWreckClueClaimed;
    private bool returnedSalvageAtBoat;
    private bool returnedClueAtBoat;
    private bool returnedLighthouseConfirmationAtBoat;
    private bool nearshoreWorkDone;
    private TideRoutingMode tideRoutingMode = TideRoutingMode.Open;
    private float routingBoom01;
    private bool routingWorkActive;
    private float routingWorkDirection;
    private bool routingDecisionLocked;
    private bool repairChoiceApplied;
    private RepairChoice pendingRepairChoice = RepairChoice.None;
    private int repairWorkStep;
    private float repairWorkProgress;
    private bool repairWorkActive;
    private float harvestCarryTransition01;
    private float harvestPlacementTransition01;
    private Vector2 harvestCarryStartPosition;
    private RepairChoice harvestPlacedRepairChoice = RepairChoice.None;
    private bool tidePrepReady;
    private int tidePrepTargetRound = -1;
    private float tidePrepActionTimer;
    private TidePrepChoice pendingTidePrepChoice = TidePrepChoice.None;
    private float tidePrepWorkProgress;
    private bool tidePrepWorkActive;
    private bool shelterStressAppliedThisTide;
    private int shelterRawStressThisTide;
    private int shelterResolvedStressThisTide;
    private bool shelterBreachThisTide;
    private float shelterImpactTimer;
    private TidePortableWaterState stormRescueWater;
    private bool stormRescueManifestPrepared;
    private int stormRescueReservedTimber;
    private int stormRescueReservedFuelBundles;
    private int stormRescueReservedChartClues;
    private int dryFuelBundles;
    private float waterConsumedSinceLastRest;
    private float lastRestWaterShortfallLiters;
    private int repairStartStiltIntegrity;
    private int repairStartBoatReadiness;
    private int repairStartHouseWarmth;
    private TideAudioController firstSliceAudio;
    private TideBarrenIslandController barrenIsland;
    private TideHeavyWreckSalvageController heavyWreckSalvage;
    private TideMooringRopeController mooringRope;
    private TideSailingReefController sailingReef;
    private TideSailingSalvageController sailingSalvage;
    private TideStormRescueController stormRescue;
    private TideForecastTideNotchController forecastTideNotches;
    private TideWrackLineController wrackLine;

    // 旧的 Scene 预览和几何探针仍使用这些语义名称。实际存储已集中到
    // TideSailingSalvageController；这些属性只是过渡期的单源投影，不保留副本。
    private float sailingSalvageWorldX
    {
        get => sailingSalvage != null ? sailingSalvage.WorldX : sailingSalvagePoint.x;
        set { if (sailingSalvage != null) sailingSalvage.WorldX = value; }
    }
    private float sailingSalvageVelocity
    {
        get => sailingSalvage != null ? sailingSalvage.Velocity : 0f;
        set { if (sailingSalvage != null) sailingSalvage.Velocity = value; }
    }
    private float sailingSalvageHookProgress
    {
        get => sailingSalvage != null ? sailingSalvage.HookProgress01 : 0f;
        set { if (sailingSalvage != null) sailingSalvage.HookProgress01 = value; }
    }
    private float sailingHookThrow01
    {
        get => sailingSalvage != null ? sailingSalvage.Throw01 : 0f;
        set { if (sailingSalvage != null) sailingSalvage.Throw01 = value; }
    }
    private bool sailingHookThrowActive
    {
        get => sailingSalvage != null && sailingSalvage.ThrowActive;
        set { if (sailingSalvage != null) sailingSalvage.ThrowActive = value; }
    }
    private bool sailingSalvageHauling
    {
        get => sailingSalvage != null && sailingSalvage.Hauling;
        set { if (sailingSalvage != null) sailingSalvage.Hauling = value; }
    }
    private float sailingSalvageTension01
    {
        get => sailingSalvage != null ? sailingSalvage.Tension01 : 0f;
        set { if (sailingSalvage != null) sailingSalvage.Tension01 = value; }
    }
    private float sailingSalvageOverstrainSeconds
    {
        get => sailingSalvage != null ? sailingSalvage.OverstrainSeconds : 0f;
        set { if (sailingSalvage != null) sailingSalvage.OverstrainSeconds = value; }
    }
    private Vector2 sailingHookWorldPosition
    {
        get => sailingSalvage != null ? sailingSalvage.HookWorldPosition : sailingSalvagePoint;
        set { if (sailingSalvage != null) sailingSalvage.HookWorldPosition = value; }
    }
    private float sailingSalvageInitialRopeLength
    {
        get => sailingSalvage != null ? sailingSalvage.InitialRopeLength : 0f;
        set { if (sailingSalvage != null) sailingSalvage.InitialRopeLength = value; }
    }

    // 旧场景与几何探针仍以这个名字读取船位。实际权威状态已经移入泊位绳
    // 组件；fallback 只覆盖组件尚未由 EnsureScene 建立的反序列化瞬间。
    private float mooredBoatOffsetX
    {
        get => mooringRope != null
            ? mooringRope.BoatOffsetMeters
            : mooredBoatOffsetFallback;
        set
        {
            mooredBoatOffsetFallback = value;
            if (mooringRope != null)
            {
                mooringRope.SetBoatOffsetForEditor(value);
            }
        }
    }
    private int lampForecastCharges;
    private TideForecastSnapshot loftForecastSnapshot;
    private TideForecastSnapshot chartForecastSnapshot;
    private int shortSailCount;
    private int moonPhaseShadowBucket = -1;
    private Sprite moonPhaseShadowSprite;
    private bool retiredHierarchyCleaned;
    private bool showDebugHud;
    private bool arrivalVignetteActive;
    private float arrivalVignetteTimer;
    private string lastActionHint = "把网挂好，然后趁潮来之前走动看看。";

    // These objects came from the debug-heavy route/goal pass. They are removed
    // from authored scenes and prefabs instead of merely being hidden, so stale
    // diamonds and straight guide rails cannot reappear after a domain reload.
    private static readonly string[] RetiredGeneratedObjectPrefixes =
    {
        "GeneratedStiltFirstPlayerBeacon",
        "GeneratedStiltFirstSailingTarget",
        "GeneratedStiltFirstSailingHomeMarker",
        "GeneratedStiltFirstRouteOpeningSignal",
        "GeneratedStiltFirstCurrentObjectiveMarker",
        "GeneratedStiltFirstSailingRouteRibbon",
        "GeneratedStiltFirstSailingRouteBuoy",
        "GeneratedStiltFirstSailingBuoy",
        "GeneratedStiltFirstSailingReturnBeacon",
        "GeneratedStiltFirstLoopStepBeacon",
        "GeneratedStiltFirstRepairChoiceMarker",
        "GeneratedStiltFirstNearshoreWorkMarker",
        "GeneratedStiltFirstLampForecastMarker",
        "GeneratedStiltFirstSailingClueCargo",
        "GeneratedStiltFirstReturnedRouteClue",
        "GeneratedStiltFirstRouteVortexCutoutMask"
    };

    private static Sprite backdropSprite;
    private static Sprite moonWashSprite;
    private static Sprite subsurfaceFadeSprite;
    private static Sprite depthBlendBandSprite;
    private static Sprite formalSubsurfaceBandSprite;
    private static Material spriteUnlitMaterial;
    private static Sprite waterSprite;
    private static Sprite foamSprite;
    private static Sprite swimWakeSprite;
    private static Sprite cloudMassSprite;
    private static Sprite playerHaloSprite;
    private static Sprite vortexSprite;
    private static Sprite sideViewVortexDepressionSprite;
    private static Sprite sideViewVortexUndertowSprite;
    private static Sprite verandaSprite;
    private static Sprite porchRoofSprite;
    private static Sprite houseGableRoofSprite;
    private static Sprite longEaveSprite;
    private static Sprite underHouseShadowSprite;
    private static Sprite denseStiltPostSprite;
    private static Sprite stormSurgeWallSprite;
    private static Sprite brokenHousePieceSprite;
    private static Sprite snappedStiltPostSprite;
    private static Sprite departureRouteWakeSprite;
    private static Sprite diagonalBraceSprite;
    private static Sprite laundryClothSprite;
    private static Sprite landingWalkwaySprite;
    private static Sprite boatWakeSprite;
    private static Sprite houseSprite;
    private static Sprite roofSprite;
    private static Sprite deckSprite;
    private static Sprite postSprite;
    private static Sprite houseSaltStreakSprite;
    private static Sprite shelterWarmRaySprite;
    private static Sprite shelterLampSprite;
    private static Sprite playerSprite;
    private static Sprite netLineSprite;
    private static Sprite towRopeSprite;
    private static Sprite netWeightSprite;
    private static Sprite netKnotSprite;
    private static Sprite corkFloatSprite;
    private static Sprite boatHullSprite;
    private static Sprite sailSprite;
    private static Sprite boatRiggingSprite;
    private static Sprite boatSailPatchStitchSprite;
    private static Sprite boatCockpitSprite;
    private static Sprite boatPassengerSprite;
    private static Sprite sailingRouteBuoySprite;
    private static Sprite lighthouseSprite;
    private static Sprite lighthouseBeamSprite;
    private static Sprite moonSprite;
    private static Sprite moonShadowSprite;
    private static Sprite shipwreckRibSprite;
    private static Sprite fishSprite;
    private static Sprite woodSprite;
    private static Sprite relicSprite;
    private static Sprite trashSprite;
    private static Sprite repairPatchSprite;
    private static Sprite stormLineSprite;
    private static Sprite waterWrackSprite;
    private static Sprite incomingTideCarryRippleSprite;
    private static Sprite lighthouseChartClueSprite;
    private static Sprite stormWarningPennantSprite;
    private static Sprite prepRopeCoilSprite;
    private static Sprite prepBucketSprite;
    private static Sprite prepStakeSprite;
    private static Sprite storyEllipseMaskSprite;
    private static Sprite storyRaggedMaskSprite;
    private static Sprite interiorThreeLevelCutawayMaskSprite;
    private static Sprite formalInteriorWallSprite;
    private static Sprite formalInteriorLoftWallSprite;
    private static Sprite formalInteriorRoofCapSprite;
    private static TideV30HouseRuntimeCatalog formalHouseV30Catalog;
    private static TideV34HouseExteriorCatalog formalHouseV34ExteriorCatalog;
    private static TideV35HouseInteriorCatalog formalHouseV35InteriorCatalog;
    private static TideV69HouseProfileBaseAsset formalHouseV69ActiveBase;
    private static readonly TideV69HouseStructuralStageAsset[] formalHouseV69ActiveStructuralStages =
        new TideV69HouseStructuralStageAsset[TideV69HouseRepairPresentationModel.StructuralOwnerCount];
    private static readonly TideV69HouseBinaryStageAsset[] formalHouseV69ActiveBinaryStages =
        new TideV69HouseBinaryStageAsset[TideV69HouseRepairPresentationModel.BinaryOwnerCount];
    private static TideV20CharacterRuntimeCatalog formalCharacterV20Catalog;
    private static TideV41CharacterContactCatalog formalCharacterV41ContactCatalog;
    private static TideV42CharacterSurvivalCatalog formalCharacterV42SurvivalCatalog;
    private static TideV31BoatRuntimeCatalog formalBoatV31Catalog;
    private static TideV52BoatRepairBaseAsset formalBoatV52BaseAsset;
    private static readonly TideV52BoatRepairStageAsset[] formalBoatV52ActiveStageAssets =
        new TideV52BoatRepairStageAsset[TideV52BoatRepairPresentationModel.OwnerCount];
    private static TideV32FirstSliceArtCatalog formalV32ArtCatalog;
    private static TideV37BoatCharacterCatalog formalV37BoatCharacterCatalog;
    private static TideV43SeaWeatherCatalog formalV43SeaWeatherCatalog;
    private static TideV44LookoutVistaCatalog formalV44LookoutVistaCatalog;
    private static TideV54NetCatalog formalV54NetCatalog;
    private static TideV53MooringCatalog formalV53MooringCatalog;
    private static TideV56OceanCatalog formalV56OceanCatalog;
    private static TideV59FindCatalog formalV59FindCatalog;
    private static TideV70VortexCatalog formalV70VortexCatalog;
    private static readonly Sprite[] formalHouseV28Frames = new Sprite[TideV28HousePresentationModel.ExteriorFrameCount];
    private static Sprite formalHouseV27InteriorFoundSprite;
    private static Sprite formalHouseV27InteriorRepairedSprite;
    private static bool formalHouseV28ResourcesLoaded;
    private static Sprite formalHouseHdSprite;
    private static Sprite formalHouseSprite;
    private static Sprite formalBoatHdSprite;
    private static Sprite formalDamagedBoatHdSprite;
    private static Sprite formalReefedBoatHdSprite;
    private static Sprite formalDamagedReefedBoatHdSprite;
    private static Sprite formalBoatSprite;
    private static Sprite formalBoatPassengerHdSprite;
    private static readonly Dictionary<Sprite, Sprite> formalBoatGunwaleSprites = new Dictionary<Sprite, Sprite>();
    private static readonly Dictionary<Sprite, Sprite> formalBoatRigSprites = new Dictionary<Sprite, Sprite>();
    private static Sprite formalSwimPlayerSprite;
    private static Sprite formalHaulPlayerSprite;
    private static Sprite formalRepairPlayerSprite;
    private static Sprite formalWalkPlayerSprite;
    private static Sprite formalWalkContactRightSprite;
    private static Sprite formalWalkPassRightSprite;
    private static Sprite formalWalkContactLeftSprite;
    private static Sprite formalWalkPassLeftSprite;
    private static Sprite formalPlayerSprite;
    private static Sprite formalNetHdSprite;
    private static Sprite formalNetSprite;
    private static Sprite formalShipwreckSprite;
    private static Sprite formalLighthouseSprite;
    private static Sprite formalMoonSprite;
    private static Sprite formalCloudBankSprite;
    private static Sprite formalDaySeaSkyHdSprite;
    private static Sprite formalNightSeaSkyHdSprite;
    private static Sprite formalDaySeaSkySprite;
    private static Sprite formalNightSeaSkySprite;
    private static Sprite formalWaterBodyHdSprite;
    private static Sprite formalWaterSurfaceSprite;
    private static Sprite formalVortexSprite;
    private static Sprite formalBoardwalkHdSprite;
    private static Sprite formalBoardwalkSprite;
    private static Sprite formalFishSprite;
    private static Sprite formalRepairTimberSprite;
    private static Sprite formalLooseSaltWoodSprite;
    private static Sprite formalSeaSalvageHookFullSprite;
    private static Sprite formalSeaSalvageHookSprite;
    private static Sprite formalRoutingBoomSprite;
    private static Sprite formalRoutingWindlassFoundSprite;
    private static Sprite formalRoutingWindlassWorkingSprite;
    private static Sprite formalRouteRelicSprite;
    private static Sprite formalRelicMapPieceSprite;
    private static Sprite formalRelicLargeCompassSprite;
    private static Sprite formalRelicSmallCompassSprite;
    private static Sprite formalWetDebrisSprite;
    private static Sprite harvestCarrySlingSprite;
    private static Sprite formalNightPrepRopeSprite;
    private static Sprite formalNightPrepBucketSprite;
    private static Sprite formalNightPrepStakeSprite;
    private static Sprite formalV36TidePrepRopeFixedSprite;
    private static Sprite formalV36TidePrepBucketFixedSprite;
    private static Sprite formalV36TidePrepStakeFixedSprite;
    private static Sprite formalStiltWaveImpactSprite;
    private static Sprite formalStiltDamageSprite;
    private static Sprite formalStiltTideZoneWearSprite;
    private static Sprite formalSeaCurrentCrestSprite;

    private void OnEnable()
    {
        retiredHierarchyCleaned = false;
        // 调试阶段默认把时间、天黑/天亮倒计时和离家距离放出来，避免必须先
        // 猜到 F3 才能验证昼夜与航行节奏。玩家仍可随时按 F3 关闭整层信息。
        showDebugHud = Application.isEditor;
        EnsureScene();
        ResetSlice();
    }

    private void Update()
    {
        EnsureScene();
        if (Application.isPlaying)
        {
            if (arrivalVignetteActive)
            {
                TickUninspectedNaturalWorld(Time.deltaTime);
                if (Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape))
                {
                    CompleteArrivalVignette();
                }
                else
                {
                    TickArrivalVignette(Time.deltaTime);
                }

                UpdateVisuals(Time.time);
                return;
            }

            if (survivalPresentationState != SurvivalPresentationState.None)
            {
                // 受难过程锁住玩家输入，但世界不会停表。潮位、昼夜、天气、
                // 屋体压力和漂移仍按同一自然时钟推进；结算只在动作结束后发生。
                UpdateFirstSliceAudio();
                TickState(Time.deltaTime);
                TickSurvivalPresentation(Time.deltaTime);
                UpdateVisuals(Time.time);
                return;
            }

            if (boatViewTransition != BoatViewTransition.None)
            {
                // Nature keeps moving during the short camera handoff, but input is
                // locked so one key press cannot steer, interact and board at once.
                UpdateFirstSliceAudio();
                TickState(Time.deltaTime);
                // Sample the route after the current and waterline moved this frame;
                // otherwise the actor trails the authored stern anchor by one frame.
                TickBoatViewTransition(Time.deltaTime);

                UpdateVisuals(Time.time);
                return;
            }

            HandlePlayerMovement(Time.deltaTime);
            UpdateNetUnrollFromPlayerPosition();
            UpdateFirstSliceAudio();
            // Interaction code sets this back to true only while F is held at the
            // actual stake. Leaving the stake therefore pauses the work pose while
            // preserving netRigActionProgress for a later return.
            netRigActionHeld = false;
            HandleContextInteraction(Time.deltaTime);
            HandleInput();

            // Inspecting the wreck changes knowledge only. Tide, exposure, the net and
            // every other physical system already exist before the player looks at it.
            TickState(Time.deltaTime);
        }

        UpdateVisuals(Application.isPlaying ? Time.time : 0f);
    }

    private void TickUninspectedNaturalWorld(float deltaTime)
    {
        worldElapsedRealSeconds += Mathf.Max(0f, deltaTime);
        AdvanceDayNight(deltaTime);
        AdvanceContinuousWeather(deltaTime);
        TickNaturalTide(deltaTime);
        TickBarrenIslandNaturalState(deltaTime);
        TickMooredBoatCurrent(deltaTime);
    }

    private void TickArrivalVignette(float deltaTime)
    {
        // 正式流程默认关闭这段旧序章。即使诊断或编辑器误调用本方法，未显式
        // 开启时也绝不能把已经站在木面上的人物重新拖回海里。
        if (!arrivalVignetteActive)
        {
            return;
        }

        arrivalVignetteTimer += deltaTime;
        float progress01 = Mathf.Clamp01(arrivalVignetteTimer / Mathf.Max(0.1f, arrivalVignetteSeconds));
        float drift01 = Mathf.SmoothStep(0f, 1f, progress01);
        playerSwimming = progress01 < 0.82f;
        playerAquaticBlend01 = 1f - Mathf.SmoothStep(0.64f, 0.94f, progress01);
        playerMoving = playerSwimming;
        playerFacing = -1;
        playerHorizontalVelocity = playerSwimming ? -playerMoveSpeed * 0.34f : 0f;
        playerWalkCycle += deltaTime * 1.1f;
        float arrivalX = arrivalWreckX - 0.38f;
        // 开场不是人物从画面外斜飞到木板，而是海难后已经随断木漂到浅水：
        // 前段身体贴着局部浪面向岸边移动，只有浪把脚送到潮滩后才短距离站起。
        // 起点与终点都在同一段可见浅水中，保证世界尺度和因果连续。
        float arrivalStartX = Mathf.Min(GetLaneMaxX(WalkLane.TideFlat), arrivalWreckX + 0.86f);
        float arrivalWorldX = Mathf.Lerp(arrivalStartX, arrivalX, drift01);
        TideOceanSample arrivalOcean = GetOceanSample(arrivalWorldX);
        float arrivalGroundY = GetPlayerLaneY(WalkLane.TideFlat);
        float beaching01 = Mathf.SmoothStep(0.76f, 0.96f, progress01);
        playerPosition = new Vector2(
            arrivalWorldX,
            Mathf.Lerp(
                arrivalOcean.SurfaceY - 0.04f,
                arrivalGroundY,
                beaching01));

        lastActionHint = progress01 < 0.48f
            ? "船没有把你带到这里。海把剩下的你和断木一起推了过来。"
            : progress01 < 0.82f
                ? "浪后露出一座旧高脚屋；窗里没有人，柱子却还撑着。"
                : "你踩到浅处。先看看一起漂来的断木，也许上面留着来路。";

        if (progress01 >= 1f)
        {
            CompleteArrivalVignette();
        }
    }

    private void CompleteArrivalVignette()
    {
        arrivalVignetteActive = false;
        arrivalVignetteTimer = arrivalVignetteSeconds;
        playerSwimming = false;
        playerAquaticBlend01 = 0f;
        playerMoving = false;
        playerHorizontalVelocity = 0f;
        playerLane = WalkLane.TideFlat;
        playerPosition = GetOpeningPlayerPosition();
        playerFacing = 1;
        lastActionHint = "潮把你和残骸留在旧高脚屋旁。断木可以检查，旧屋、渔网和船也都已经在那里。";
    }

    private void UpdateFirstSliceAudio()
    {
        if (firstSliceAudio == null)
        {
            firstSliceAudio = GetComponent<TideAudioController>();
            if (firstSliceAudio == null)
            {
                firstSliceAudio = gameObject.AddComponent<TideAudioController>();
            }
        }

        float storm01 = GetStormPressure01();
        float warmth01 = Mathf.Clamp01(houseWarmth / 4f);
        float ingressPressure = viewMode == SliceViewMode.Sailing ? sailingWaterIngress01 : 0f;
        float unease01 = Mathf.Clamp01(0.22f + storm01 * 0.46f + netPeakTension01 * 0.2f + ingressPressure * 0.24f);
        float muffle01 = dayNightPhase == DayNightPhase.Night ? 0.32f : dayNightPhase == DayNightPhase.Dusk ? 0.22f : 0.12f;
        float cueBrightness01 = dayNightPhase == DayNightPhase.Night ? 0.42f : 0.58f;

        firstSliceAudio.ConfigureStandalone(warmth01, unease01, muffle01, cueBrightness01);
        firstSliceAudio.SetStandaloneTideState(tideStrength, GetNaturalTideHeight01(), storm01);
        float locomotionMaxSpeed = playerMoveSpeed * (playerSwimming ? 0.58f : 1f);
        float audibleLocomotionVelocity = playerSwimming
            ? playerHorizontalVelocity + playerCurrentDriftVelocity
            : playerHorizontalVelocity;
        float locomotionSpeed01 = locomotionMaxSpeed > 0.01f
            ? Mathf.Clamp01(Mathf.Abs(audibleLocomotionVelocity) / locomotionMaxSpeed)
            : 0f;
        bool locomotionAudible = (viewMode == SliceViewMode.Shelter || viewMode == SliceViewMode.Interior) &&
            state != SliceState.FinalDeparture &&
            !IsActivelyHaulingNet();
        firstSliceAudio.SetStandaloneLocomotionState(
            locomotionAudible ? locomotionSpeed01 : 0f,
            playerSubmersion01,
            playerSwimming,
            isLaneTransitioning);
    }

    private void ResetSlice()
    {
        state = SliceState.LowTidePlanning;
        viewMode = SliceViewMode.Shelter;
        mooringScreenActive = false;
        boatViewTransition = BoatViewTransition.None;
        boatViewTransitionTimer = 0f;
        boatViewTransitionSwitched = false;
        boatViewTransitionStartPosition = Vector2.zero;
        boatViewTransitionEndPosition = Vector2.zero;
        survivalPresentationState = SurvivalPresentationState.None;
        survivalPresentationTimer = 0f;
        survivalPresentationStartPosition = Vector2.zero;
        survivalPresentationStartView = SliceViewMode.Shelter;
        pendingDeathReason = string.Empty;
        playerLane = WalkLane.TideFlat;
        selectedNetLine = NetLine.Mid;
        selectedPrepChoice = TidePrepChoice.None;
        currentHarvest = HarvestKind.None;
        securedPostHarvest = HarvestKind.None;
        securedPostBundleTier = 0;
        securedPostVisualPieceCount = 0;
        currentTideDriftField = default;
        tideDriftFieldInitialized = false;
        tideDriftFieldCycleOrdinal = -1;
        tideSourceBatchId = 0;
        currentHarvestBatchId = 0;
        securedPostHarvestBatchId = 0;
        extraSaltWoodBatchId = 0;
        washedAwayHarvestBatchId = 0;
        primarySourcePassedNearshore = false;
        previousOuterWreckTravel01 = 0f;
        outerWreckTravel01 = 0f;
        outerNetCaptureProgress01 = 0f;
        outerWreckPassedNearshore = false;
        tideSourceHarvest = HarvestKind.None;
        harvestPhysicalState = HarvestPhysicalState.None;
        washedAwayHarvestKind = HarvestKind.None;
        stateTimer = 0f;
        finalDepartureStartWaterY = lowWaterY;
        tideClockSeconds = tideCycleSeconds * OpeningTidePhase01;
        weatherClockSeconds = 0f;
        worldElapsedRealSeconds = 0f;
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        tideCurrentlyRising = true;
        dayProgress01 = 0.4f;
        dayClockSeconds = dayProgress01 * dayLengthSeconds;
        dayNightPhase = DayNightPhase.Day;
        netTouched = false;
        tideRound = 0;
        houseWarmth = 0;
        boatReadiness = 0;
        stiltIntegrity = 1;
        roofIntegrity = 0;
        interiorComfort = 0;
        interiorSealCondition = 0;
        workbenchCondition = 0;
        bedCondition = 0;
        chartRadioCondition = 0;
        stoveCondition = 0;
        boatHullIntegrity = 1;
        boatSailIntegrity = 0;
        boatCabinIntegrity = 0;
        timberStock = 0;
        ropeStock = 0;
        clothStock = 0;
        metalStock = 0;
        foodStock = 0;
        currentHarvestBanked = false;
        currentHarvestFromWrack = false;
        hasSalvageBag = false;
        lighthouseClues = 0;
        lighthouseSeen = false;
        routeClueReturnRound = -1;
        netIntegrity = 3;
        currentTideNetStress = 0;
        netBrokeThisTide = false;
        moonAgeDays = OpeningMoonAgeDays;
        tideStrength = CalculateTideStrength(moonAgeDays);
        netSetDepth01 = StoredNetReferenceDepth01;
        selectedNetLine = GetNetLineFromLoweringProgress(netSetDepth01);
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        // A new run starts midway through an unfinished natural tide. It may begin
        // accumulating that tide immediately, but no “previous tide” exists until a
        // complete low-to-low cycle has actually crossed the boundary.
        highWaterMemory = TideHighWaterMemoryModel.Begin(currentWaterY);
        playerPosition = GetOpeningPlayerPosition();
        playerFacing = 1;
        playerMoving = false;
        playerWalkCycle = 0f;
        playerHorizontalVelocity = 0f;
        playerCurrentDriftVelocity = 0f;
        laneTransitionEntrySpeed01 = 0f;
        playerSwimming = false;
        playerAquaticBlend01 = 0f;
        bodyWarmth01 = 1f;
        drowningSeconds = 0f;
        deathCount = 0;
        deathFeedbackTimer = 0f;
        lastDeathReason = string.Empty;
        arrivalInspected = false;
        netDeployed = false;
        netRigStep = NetRigStep.Stored;
        netUnrollProgress = 0f;
        netRigActionProgress = 0f;
        netRigActionHeld = false;
        netRigActionStarted = false;
        netLoweringProgress = 0f;
        netHaulProgress = 0f;
        netHaulStrokePhase = 0f;
        netHaulEffort01 = 0f;
        netHaulLoad01 = 0f;
        editorNetHaulPreviewActive = false;
        netSetDepth01 = StoredNetReferenceDepth01;
        selectedNetLine = GetNetLineFromLoweringProgress(netSetDepth01);
        netWaterExposureSeconds = 0f;
        netCaptureProgress01 = 0f;
        previousIncomingHarvestTravel01 = 0f;
        previousOuterWreckTravel01 = outerWreckTravel01;
        outerNetCaptureProgress01 = 0f;
        netAccumulatedTension = 0f;
        netPeakTension01 = 0f;
        netFraying01 = 0f;
        netDepthAdjustmentActive = false;
        netDepthAdjustmentDirection = 0f;
        netCatchResolved = false;
        netPostCatchExposureSeconds = 0f;
        netCatchBundleTier = 1;
        netCatchVisualPieceCount = 0;
        incomingHarvestTravel01 = 0f;
        addedHarvestPieceTravel01 = 0f;
        washedAwayHarvestPieceCount = 0;
        washedAwayHarvestTimer = 0f;
        washedAwayHarvestDriftX = 0f;
        washedAwayHarvestBatchId = 0;
        netOverloadStressApplied = 0;
        netSecuredEarly = false;
        isLaneTransitioning = false;
        laneTransitionTarget = WalkLane.TideFlat;
        laneTransitionProgress = 0f;
        laneTransitionFromPosition = playerPosition;
        laneTransitionToPosition = playerPosition;
        sailTripActive = false;
        sailTripTimer = 0f;
        sailingBoatX = sailingHomeX;
        sailingBoatLaneY = sailingHomeY;
        sailingBoatVelocity = 0f;
        sailingBoatWorldVelocity = 0f;
        sailingFlowCrestTravelWorld = 0f;
        sailingWaterIngress01 = 0f;
        sailingSailTrim01 = 0.58f;
        sailingDynamics = new TideSailboatDynamicsState
        {
            HeaveY = sailingHomeY,
            SailRaised01 = sailingSailTrim01
        };
        sailingBallastInput = 0f;
        sailingBailCycle = 0f;
        sailingTrimActionTimer = 0f;
        sailingTrimCycle = 0f;
        sailingBailedWaterThisTrip = 0f;
        sailingBailing = false;
        sailingIngressMidWarned = false;
        sailingIngressHighWarned = false;
        sailingRangeBlockedThisTrip = false;
        mooringRope.ResetRuntime(1.08f);
        mooredBoatOffsetFallback = mooringRope.BoatOffsetMeters;
        sailingClueCollected = false;
        sailingRewardPending = false;
        sailingClueWasLighthouse = false;
        sailingBuoyChecked = !EnableSailingBuoyGameplay;
        extraSaltWoodBundledWithNetHarvest = false;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.PassingNearshore);
        sailingWreckClueClaimed = false;
        returnedClueAtBoat = false;
        returnedLighthouseConfirmationAtBoat = false;
        sailingSalvageWorldX = sailingSalvagePoint.x;
        sailingSalvageVelocity = 0f;
        sailingSalvageHookProgress = 0f;
        sailingHookThrow01 = 0f;
        sailingHookThrowActive = false;
        sailingSalvageHauling = false;
        sailingSalvageTension01 = 0f;
        sailingSalvageOverstrainSeconds = 0f;
        sailingHookWorldPosition = sailingSalvagePoint;
        sailingSalvageInitialRopeLength = 0f;
        sailingReef?.ResetRuntime();
        nearshoreWorkDone = false;
        tideRoutingMode = TideRoutingMode.Open;
        routingBoom01 = 0f;
        routingWorkActive = false;
        routingWorkDirection = 0f;
        routingDecisionLocked = false;
        shelterStressAppliedThisTide = false;
        shelterRawStressThisTide = 0;
        shelterResolvedStressThisTide = 0;
        shelterBreachThisTide = false;
        shelterImpactTimer = 0f;
        stormRescue.ResetRuntime();
        stormRescueWater = default;
        stormRescueManifestPrepared = false;
        stormRescueReservedTimber = 0;
        stormRescueReservedFuelBundles = 0;
        stormRescueReservedChartClues = 0;
        dryFuelBundles = StartingDryFuelBundles;
        waterConsumedSinceLastRest = 0f;
        lastRestWaterShortfallLiters = 0f;
        repairChoiceApplied = false;
        pendingRepairChoice = RepairChoice.None;
        repairWorkStep = 0;
        repairWorkProgress = 0f;
        repairWorkActive = false;
        harvestCarryTransition01 = 0f;
        harvestPlacementTransition01 = 0f;
        harvestCarryStartPosition = Vector2.zero;
        harvestPlacedRepairChoice = RepairChoice.None;
        tidePrepReady = false;
        tidePrepTargetRound = -1;
        tidePrepActionTimer = 0f;
        pendingTidePrepChoice = TidePrepChoice.None;
        tidePrepWorkProgress = 0f;
        tidePrepWorkActive = false;
        repairStartStiltIntegrity = stiltIntegrity;
        repairStartBoatReadiness = boatReadiness;
        repairStartHouseWarmth = houseWarmth;
        lampForecastCharges = 0;
        loftForecastSnapshot = default;
        chartForecastSnapshot = default;
        shortSailCount = 0;
        if (barrenIsland != null)
        {
            barrenIsland.ResetIsland();
        }
        wrackLine?.ResetFeature();
        if (heavyWreckSalvage != null)
        {
            heavyWreckSalvage.ResetFeature();
        }
        // 正式开场直接把人物放在左岛裸岩与同源船骸共同提供的可见承重面上。
        // 旧短镜头会从木路右端外侧自动游入，即使脚点最后正确，第一眼仍像
        // 从虚空补位。故事镜头保留为编辑器预览，但不再强制阻塞玩家开局。
        arrivalVignetteActive = false;
        arrivalVignetteTimer = 0f;
        InitializeCurrentTideDriftField(true);
        lastActionHint = "退潮把你和残骸留在外海岩礁。你已经踩稳裸岩，可以自己决定先拆船、看屋，还是赶在潮前布网。";
    }

    private void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.F3))
        {
            showDebugHud = !showDebugHud;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetSlice();
            return;
        }

        // 数字键只保留给开发调试，玩家流程始终使用世界内 F 交互。
        if (Input.GetKey(KeyCode.LeftShift) && state == SliceState.LowTidePlanning)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                CommitNetIntoNaturalTide(NetLine.Low);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                CommitNetIntoNaturalTide(NetLine.Mid);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                CommitNetIntoNaturalTide(NetLine.High);
            }
        }
    }

    /// <summary>
    /// 玩家侧只有一个交互键。动作由角色所处位置和当前潮汐阶段决定，
    /// 避免同一件事同时出现在数字菜单、世界物体和 HUD 三个地方。
    /// </summary>
    private void HandleContextInteraction(float deltaTime)
    {
        stormRescue.ClearInteraction();
        if (state == SliceState.FinalDeparture || isLaneTransitioning ||
            boatViewTransition != BoatViewTransition.None)
        {
            return;
        }

        if (viewMode == SliceViewMode.Lookout)
        {
            if (Input.GetKeyDown(KeyCode.F))
            {
                BeginBoatViewTransition(BoatViewTransition.ExitingLookout);
            }

            return;
        }

        if (viewMode == SliceViewMode.Sailing)
        {
            if (Input.GetKeyDown(KeyCode.F))
            {
                if (CanReturnFromSailing())
                {
                    ReturnToStiltHouseByChoice();
                }
                else if (CanInteractAtSailingPoint())
                {
                    TryInteractAtSailingPoint();
                }
            }

            return;
        }

        if (viewMode == SliceViewMode.Interior)
        {
            if (Input.GetKeyDown(KeyCode.F) && IsPlayerNearInteriorExit())
            {
                BeginBoatViewTransition(BoatViewTransition.ExitingInterior);
                return;
            }

            if (Input.GetKeyDown(KeyCode.F) && IsPlayerNearLoftLookout())
            {
                BeginBoatViewTransition(BoatViewTransition.EnteringLookout);
                return;
            }

            if (HandleStormRescueInteraction())
            {
                return;
            }

            if (CanChooseTidePrep() && HandleTidePrepWorkAtWorldTarget(deltaTime))
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.F) &&
                state == SliceState.RepairMoment &&
                harvestPhysicalState == HarvestPhysicalState.Carried &&
                IsPlayerNearInteriorStorage())
            {
                StoreCurrentHarvestAtInteriorRack();
                return;
            }

            if (state == SliceState.RepairMoment && !repairChoiceApplied && HandleRepairWorkAtWorldTarget(deltaTime))
            {
                return;
            }

            bool restWindowOpen = IsPlayerNearRestPoint() &&
                (currentHarvest == HarvestKind.None || currentHarvestBanked) &&
                ((state == SliceState.LowTidePlanning && dayNightPhase == DayNightPhase.Night) ||
                 state == SliceState.RepairMoment);
            if (Input.GetKeyDown(KeyCode.F) && restWindowOpen)
            {
                BeginSleepPresentation();
            }

            return;
        }

        if (TryHandleBarrenIslandInteraction(deltaTime))
        {
            return;
        }

        if (HandleMooringRopeInput(deltaTime))
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.F) && IsPlayerNearInteriorDoor())
        {
            BeginBoatViewTransition(BoatViewTransition.EnteringInterior);
            return;
        }

        if (!arrivalInspected && Input.GetKeyDown(KeyCode.F) && IsPlayerNearArrivalWreck() &&
            !IsPlayerNearBoat() && !IsPlayerNearStagedSaltWoodAtMooring())
        {
            InspectArrivalWreck();
            return;
        }

        // The post owns its catch independently of the current tide phase. It can be
        // collected later, but only with empty hands; carrying timber past the post
        // must never overwrite either physical object.
        if (HasSecuredPostHarvest() &&
            AreHarvestHandsFree() &&
            Input.GetKeyDown(KeyCode.F) &&
            IsPlayerNearNet())
        {
            PickUpSecuredHarvestFromPost();
            return;
        }

        // 船骸启动物不必先熬完整个潮次才产生价值。只要同一件原物已经被玩家
        // 搬到可见施工位，就允许在首潮前投入一个外部工位；这会与布网、观潮
        // 争夺真实低潮时间，而不是由剧情状态机发放一次免费修理。
        bool hasStagedArrivalPart = barrenIsland != null &&
            (barrenIsland.ShelterStagedParts > 0 || barrenIsland.BoatStagedParts > 0);
        if (state != SliceState.RepairMoment && !repairChoiceApplied &&
            hasStagedArrivalPart && HandleRepairWorkAtWorldTarget(deltaTime))
        {
            return;
        }

        if (state == SliceState.LowTidePlanning)
        {
            if (dayNightPhase == DayNightPhase.Night && Input.GetKeyDown(KeyCode.F) && IsPlayerNearRestPoint())
            {
                lastActionHint = "不能站在屋外把整夜跳过去。先从真实门槛进屋，走到干燥层的床边。";
                return;
            }

            if (IsPlayerNearNet())
            {
                HandleNetRigging(deltaTime);
                return;
            }

            if (TryHandleMooringInteraction())
            {
                return;
            }

            return;
        }

        if (state == SliceState.TideRising)
        {
            if (!netDeployed && !netSecuredEarly && IsPlayerNearNet())
            {
                HandleNetRigging(deltaTime);
                return;
            }

            if (netDeployed && HandleContinuousTideRoutingInput(deltaTime))
            {
                return;
            }

            if (netDeployed && HandleLiveNetDepthInput(deltaTime))
            {
                return;
            }

            // Once the first catch is visible, the player may cash out at any time.
            // Leaving it in the tide grows the bundle but also keeps adding tension.
            if (netDeployed && netCatchResolved && IsPlayerNearNet())
            {
                HandleNetHauling(deltaTime);
                return;
            }

            if (TryHandleMooringInteraction())
            {
                return;
            }

            return;
        }

        if (state == SliceState.EbbCollect)
        {
            if (netDeployed && HandleLiveNetDepthInput(deltaTime))
            {
                return;
            }

            if (IsPlayerNearNet())
            {
                HandleNetHauling(deltaTime);
                return;
            }

            if (TryHandleMooringInteraction())
            {
                return;
            }

            return;
        }

        if (state != SliceState.RepairMoment)
        {
            return;
        }

        // RepairMoment describes the ownership of the latest catch, not a curfew.
        // The stern has its own narrow interaction point, separate from hull, sail,
        // and cabin work anchors. Stored materials therefore never lock the boat.
        if (TryHandleMooringInteraction())
        {
            return;
        }

        if (!repairChoiceApplied && HandleRepairWorkAtWorldTarget(deltaTime))
        {
            return;
        }

        if (repairChoiceApplied && Input.GetKeyDown(KeyCode.F) && IsPlayerNearRestPoint())
        {
            lastActionHint = "床在屋内干燥层。先从门槛进屋，再在床边躺下。";
        }
    }

    private bool HandleLiveNetDepthInput(float deltaTime)
    {
        float raiseInput = 0f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            raiseInput += 1f;
        }
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
        {
            raiseInput -= 1f;
        }

        bool adjusted = TickLiveNetDepthControl(raiseInput, deltaTime, IsPlayerNearNet());
        if (adjusted)
        {
            string action = netDepthAdjustmentDirection > 0f ? "收沉纲、抬高网口" : "放沉纲、降低网口";
            lastActionHint = $"正在{action}：当前 {Mathf.RoundToInt(netSetDepth01 * 100f)}% 深。抬网减载保货，下网吃流增收。";
        }

        return adjusted;
    }

    private bool TickLiveNetDepthControl(float raiseInput, float deltaTime, bool nearNetPost)
    {
        bool canAdjust = nearNetPost &&
            netDeployed &&
            !netSecuredEarly &&
            (state == SliceState.TideRising || state == SliceState.EbbCollect) &&
            harvestPhysicalState != HarvestPhysicalState.SecuredAtPost &&
            harvestPhysicalState != HarvestPhysicalState.Carried &&
            harvestPhysicalState != HarvestPhysicalState.PlacedAtWork &&
            netHaulProgress <= 0.001f;
        if (!canAdjust || Mathf.Abs(raiseInput) <= 0.01f)
        {
            netDepthAdjustmentActive = false;
            netDepthAdjustmentDirection = 0f;
            return false;
        }

        float previousDepth = netSetDepth01;
        netSetDepth01 = Mathf.Clamp01(
            netSetDepth01 - Mathf.Clamp(raiseInput, -1f, 1f) *
            Mathf.Max(0f, deltaTime) * liveNetDepthAdjustSpeed);
        netLoweringProgress = netSetDepth01;
        selectedNetLine = GetNetLineFromLoweringProgress(netSetDepth01);
        netDepthAdjustmentDirection = Mathf.Sign(raiseInput);
        netDepthAdjustmentActive = Mathf.Abs(netSetDepth01 - previousDepth) > 0.0001f;
        return netDepthAdjustmentActive;
    }

    private void HandleNetRigging(float deltaTime)
    {
        if (netDeployed || netSecuredEarly)
        {
            return;
        }

        if (!CanStartNewNetRigAtCurrentTime())
        {
            if (Input.GetKeyDown(KeyCode.F))
            {
                lastActionHint = "夜里看不清网目和脚下潮沟，不能重新展开一张干网；已经下水的网仍可抢收。回屋存放、内修或备下一潮。";
            }

            return;
        }

        if (currentWaterY > lowWaterY + 0.72f)
        {
            if (Input.GetKeyDown(KeyCode.F))
            {
                lastActionHint = "水已经没过网桩。没有站稳、展开和压坠的空间，这一潮不能再安全布网。";
            }

            return;
        }

        TickNetRiggingInput(
            deltaTime,
            Input.GetKeyDown(KeyCode.F),
            Input.GetKey(KeyCode.F),
            Input.GetKeyUp(KeyCode.F));
    }

    private void TickNetRiggingInput(float deltaTime, bool pressed, bool held, bool released)
    {
        if (netRigStep == NetRigStep.Stored && pressed)
        {
            netRigStep = NetRigStep.Carrying;
            netUnrollProgress = 0f;
            netLoweringProgress = 0f;
            netSetDepth01 = 0f;
            ResetNetRigHoldAction();
            lastActionHint = "你把湿网抱起来了。先走到左侧主桩，按住 F 把第一端绕桩收紧。";
            return;
        }

        if (netRigStep == NetRigStep.Carrying)
        {
            bool completed = TickNetRigHoldProgress(deltaTime, pressed, held);
            if (completed)
            {
                netRigStep = NetRigStep.FirstEndTied;
                netUnrollProgress = 0.08f;
                netLoweringProgress = 0f;
                netSetDepth01 = 0f;
                ResetNetRigHoldAction();
                lastActionHint = "第一端已经绕桩收紧。向右走，把网沿潮沟连续展开；别让网团拖在脚下。";
            }
            else if (netRigActionHeld)
            {
                lastActionHint = $"正在系第一端：{Mathf.RoundToInt(netRigActionProgress * 100f)}%。松开会停在这里，继续按住即可。";
            }

            return;
        }

        if (netRigStep == NetRigStep.FirstEndTied)
        {
            if (netUnrollProgress < 0.96f)
            {
                ResetNetRigHoldAction();
                if (pressed)
                {
                    lastActionHint = "自由端还没到第二根桩。继续向右走，让网身从怀里自然展开。";
                }

                return;
            }

            netRigStep = NetRigStep.Unrolled;
            ResetNetRigHoldAction();
            lastActionHint = "网身已经展开到第二根桩。松开刚才的动作，再按住 F 把自由端单独系牢。";
        }

        if (netRigStep == NetRigStep.Unrolled)
        {
            bool completed = TickNetRigHoldProgress(deltaTime, pressed, held);
            if (completed)
            {
                netRigStep = NetRigStep.SecondEndTied;
                netSetDepth01 = 0f;
                netLoweringProgress = 0f;
                selectedNetLine = NetLine.High;
                ResetNetRigHoldAction();
                lastActionHint = "第二端已系住，沉纲仍在手边。旧湿线只说明潮曾到过哪里；浅挂省网，深挂覆盖更低水层也更吃流。重新按住 F 连续放下，松手固定。";
            }
            else if (netRigActionHeld)
            {
                lastActionHint = $"正在系第二端：{Mathf.RoundToInt(netRigActionProgress * 100f)}%。网身仍保持刚才展开的位置。";
            }

            return;
        }

        if (netRigStep == NetRigStep.SecondEndTied)
        {
            TickNetLoweringInput(deltaTime, pressed, held, released);
            return;
        }

        if (netRigStep == NetRigStep.Lowering)
        {
            TickNetLoweringInput(deltaTime, pressed, held, released);
        }
    }

    private bool TickNetRigHoldProgress(float deltaTime, bool pressed, bool held)
    {
        // A semantic job owns only the press that began it. Clearing this owner on
        // every state transition prevents one long hold from picking up, tying both
        // ends and lowering the net without the player's explicit consent.
        if (!netRigActionStarted)
        {
            if (!pressed)
            {
                netRigActionHeld = false;
                return false;
            }

            netRigActionStarted = true;
        }

        netRigActionHeld = held;
        if (!held)
        {
            netRigActionStarted = false;
            return false;
        }

        netRigActionProgress = Mathf.Clamp01(
            netRigActionProgress + Mathf.Max(0f, deltaTime) / Mathf.Max(0.1f, netRigHoldSeconds));
        return netRigActionProgress >= 0.999f;
    }

    private void TickNetLoweringInput(float deltaTime, bool pressed, bool held, bool released)
    {
        if (netRigStep == NetRigStep.SecondEndTied)
        {
            if (!pressed)
            {
                netRigActionHeld = false;
                return;
            }

            netRigStep = NetRigStep.Lowering;
            netRigActionStarted = true;
        }

        if (netRigStep != NetRigStep.Lowering)
        {
            return;
        }

        bool endingActiveLowering = released || (netRigActionStarted && !held);
        if (endingActiveLowering)
        {
            netRigActionHeld = false;
            netRigActionStarted = false;
            if (netLoweringProgress < 0.12f)
            {
                netLoweringProgress = 0f;
                netSetDepth01 = 0f;
                selectedNetLine = NetLine.High;
                netRigStep = NetRigStep.SecondEndTied;
                lastActionHint = "沉纲还没有真正离开木路边。重新按住 F 连续下放，松手时才固定。";
                return;
            }

            CommitLoweredNet();
            ResetNetRigHoldAction();
            lastActionHint = $"两根悬绳、浮纲和沉纲都已固定，网底 {Mathf.RoundToInt(netSetDepth01 * 100f)}% 深。潮水会自己来，可以离开做别的事。";
            return;
        }

        if (!netRigActionStarted)
        {
            if (!pressed)
            {
                netRigActionHeld = false;
                return;
            }

            netRigActionStarted = true;
        }

        netRigActionHeld = held;
        if (!held)
        {
            return;
        }

        netLoweringProgress = Mathf.Clamp01(
            netLoweringProgress + Mathf.Max(0f, deltaTime) / Mathf.Max(0.1f, netLoweringSeconds));
        netSetDepth01 = netLoweringProgress;
        selectedNetLine = GetNetLineFromLoweringProgress(netLoweringProgress);
        lastActionHint = $"正在连续放沉纲：{Mathf.RoundToInt(netSetDepth01 * 100f)}% 深。{GetNetChoiceForecastText(netSetDepth01)}；松开 F 就固定在这里。";
    }

    private void ResetNetRigHoldAction()
    {
        netRigActionProgress = 0f;
        netRigActionHeld = false;
        netRigActionStarted = false;
    }

    private bool CanStartNewNetRigAtCurrentTime()
    {
        // Darkness blocks only the first commitment. If dusk turns to night while the
        // player is carrying or tying the net, abandoning the half-rigged gear would be
        // less natural than letting that visible job finish.
        return dayNightPhase != DayNightPhase.Night || netRigStep != NetRigStep.Stored;
    }

    private void UpdateNetUnrollFromPlayerPosition()
    {
        if (netDeployed || netRigStep != NetRigStep.FirstEndTied || playerLane != WalkLane.TideFlat)
        {
            return;
        }

        float span = Mathf.InverseLerp(GetNetFirstStakeX(), GetNetSecondStakeX(), playerPosition.x);
        netUnrollProgress = Mathf.Max(netUnrollProgress, Mathf.Clamp01(span));
    }

    private void CommitLoweredNet()
    {
        if (netDeployed)
        {
            return;
        }

        netSetDepth01 = Mathf.Clamp01(netLoweringProgress);
        selectedNetLine = GetNetLineFromLoweringProgress(netSetDepth01);
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        netUnrollProgress = 1f;
        CommitNetIntoNaturalTide(selectedNetLine);
    }

    private void HandleNetHauling(float deltaTime)
    {
        if (Input.GetKey(KeyCode.F))
        {
            float visibleWaterY = GetNetOceanSample().SurfaceY;
            float waterLoad = Mathf.InverseLerp(
                GetSelectedNetY() - 0.16f,
                GetSelectedNetY() + 0.28f,
                visibleWaterY);
            TickNetHaulEffort(deltaTime, netHaulSeconds, waterLoad);
            int percent = Mathf.RoundToInt(netHaulProgress * 100f);
            float currentStrength01 = GetNaturalCurrentStrength01();
            string flowText = currentStrength01 <= 0.18f
                ? "水正在转流，湿网仍沉，但横向拖力很小"
                : currentStrength01 >= 0.72f
                    ? "潮流正急，每一把只能收回很短一段"
                    : "水还在走，网面持续吃流";
            lastActionHint = $"{flowText}…… {percent}%";
            if (netHaulProgress >= 0.999f)
            {
                if (state == SliceState.TideRising)
                {
                    SecureNetBeforeEbb();
                }
                else
                {
                    ApplyHarvest();
                }
            }

            return;
        }

        if (Input.GetKeyUp(KeyCode.F) && netHaulProgress > 0f)
        {
            netHaulStrokePhase = 0f;
            netHaulEffort01 = 0f;
            netHaulLoad01 = 0f;
            lastActionHint = "绳子很沉。靠在网桩旁继续按住 F，把整张网拉回来。";
        }
    }

    private void TickNetHaulEffort(float deltaTime, float baseHaulDuration, float waterLoad)
    {
        TickNetHaulEffort(
            deltaTime,
            baseHaulDuration,
            waterLoad,
            GetNaturalCurrentStrength01());
    }

    private void TickNetHaulEffort(
        float deltaTime,
        float baseHaulDuration,
        float waterLoad,
        float currentStrength01)
    {
        TideNetHaulModel.Step step = TideNetHaulModel.EvaluateStep(
            netHaulStrokePhase,
            deltaTime,
            baseHaulDuration,
            waterLoad,
            currentStrength01,
            GetNetCatchLoad01(),
            netSetDepth01);
        netHaulStrokePhase = step.Phase01;
        netHaulEffort01 = step.Effort01;
        netHaulLoad01 = step.Load01;
        netHaulProgress = Mathf.Clamp01(
            netHaulProgress + step.ProgressDelta);
    }

    private float GetNetHaulVisualProgress01()
    {
        float strokeNudge = netHaulEffort01 * 0.024f * (1f - netHaulProgress);
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(netHaulProgress + strokeNudge));
    }

    private float GetVisibleNetCurrentDrag01(float activeNetY)
    {
        TideOceanSample ocean = GetNetOceanSample();
        float submersion01 = EvaluateNetWaterContact01(activeNetY, ocean.SurfaceY);
        return TideNetHaulModel.EvaluateSignedCurrentDrag01(
            submersion01,
            GetNaturalCurrentSigned01(),
            GetNetCatchLoad01(),
            netFraying01);
    }

    private float GetVisibleNetCurrentShiftX(float activeNetY)
    {
        float haul01 = netHaulProgress > 0.001f
            ? GetNetHaulVisualProgress01()
            : 0f;
        return TideNetHaulModel.EvaluateVisibleShiftWorldX(
            GetVisibleNetCurrentDrag01(activeNetY),
            haul01,
            0.2f);
    }

    private void SecureNetBeforeEbb()
    {
        if (currentHarvest == HarvestKind.None)
        {
            return;
        }

        netCatchBundleTier = Mathf.Clamp(netCatchVisualPieceCount, 1, 3);
        string securedName = GetHarvestName(currentHarvest);
        securedPostHarvest = currentHarvest;
        securedPostHarvestBatchId = currentHarvestBatchId;
        securedPostBundleTier = netCatchBundleTier;
        securedPostVisualPieceCount = netCatchVisualPieceCount;
        netSecuredEarly = true;
        netDeployed = false;
        if (extraSaltWoodOwner == ExtraSaltWoodOwner.CaughtInNet)
        {
            SetExtraSaltWoodOwner(ExtraSaltWoodOwner.SecuredAtPost);
        }
        else if (extraSaltWoodOwner == ExtraSaltWoodOwner.RoutedToNet)
        {
            // Pulling the interception net removes the surface that was going to catch
            // this already-diverted timber. The same physical piece continues into the
            // short-sailing water now; it cannot remain frozen in a route until sleep.
            ReleaseUncaughtRoutedSaltWood();
        }
        currentHarvest = HarvestKind.None;
        currentHarvestBatchId = 0;
        currentHarvestBanked = false;
        currentHarvestFromWrack = false;
        harvestPhysicalState = HarvestPhysicalState.None;
        netLoweringProgress = 0f;
        netHaulProgress = 0f;
        netHaulStrokePhase = 0f;
        netHaulEffort01 = 0f;
        netHaulLoad01 = 0f;
        netFraying01 = 0f;
        netDepthAdjustmentActive = false;
        netDepthAdjustmentDirection = 0f;
        TideAudioController.PlayPickupCueInScene(0.56f);
        lastActionHint = $"你在涨潮里收住了{securedName}，负载 {securedPostBundleTier}/3。收获已绑在网桩，双手空出来，潮还在走，可以去出船或照看屋子。";
    }

    private void PickUpSecuredHarvestFromPost()
    {
        if (!HasSecuredPostHarvest() || !AreHarvestHandsFree())
        {
            return;
        }

        currentHarvest = securedPostHarvest;
        currentHarvestBatchId = securedPostHarvestBatchId;
        currentHarvestBanked = false;
        currentHarvestFromWrack = false;
        netCatchBundleTier = Mathf.Clamp(securedPostBundleTier, 1, 3);
        netCatchVisualPieceCount = Mathf.Clamp(securedPostVisualPieceCount, 1, 3);
        harvestPhysicalState = HarvestPhysicalState.SecuredAtPost;
        ApplyHarvest();
        ClearSecuredPostHarvest();
        lastActionHint = $"你回到网桩，亲手解下{GetHarvestName()}并抱在身前。它现在会随你移动；带到维修部位，或带进上层干燥处存放。";
    }

    private bool HasSecuredPostHarvest()
    {
        return securedPostHarvest != HarvestKind.None;
    }

    private bool AreHarvestHandsFree()
    {
        return currentHarvest == HarvestKind.None ||
            currentHarvestBanked ||
            harvestPhysicalState == HarvestPhysicalState.Stored ||
            harvestPhysicalState == HarvestPhysicalState.Lost;
    }

    private void ClearSecuredPostHarvest()
    {
        securedPostHarvest = HarvestKind.None;
        securedPostHarvestBatchId = 0;
        securedPostBundleTier = 0;
        securedPostVisualPieceCount = 0;
    }

    private bool BankSecuredPostHarvestMaterials()
    {
        if (!HasSecuredPostHarvest())
        {
            return false;
        }

        // Material banking still uses the common yield rules. Temporarily project the
        // post bundle through that ledger, then restore the player's hand state so the
        // two physical objects cannot overwrite each other during death or sleep.
        HarvestKind handHarvest = currentHarvest;
        HarvestPhysicalState handPhysicalState = harvestPhysicalState;
        bool handHarvestBanked = currentHarvestBanked;
        int handBundleTier = netCatchBundleTier;
        int handVisualPieceCount = netCatchVisualPieceCount;
        int handHarvestBatchId = currentHarvestBatchId;

        currentHarvest = securedPostHarvest;
        currentHarvestBatchId = securedPostHarvestBatchId;
        harvestPhysicalState = HarvestPhysicalState.SecuredAtPost;
        currentHarvestBanked = false;
        netCatchBundleTier = Mathf.Clamp(securedPostBundleTier, 1, 3);
        netCatchVisualPieceCount = Mathf.Clamp(securedPostVisualPieceCount, 1, 3);
        BankCurrentHarvestMaterials();
        ClearSecuredPostHarvest();

        currentHarvest = handHarvest;
        currentHarvestBatchId = handHarvestBatchId;
        harvestPhysicalState = handPhysicalState;
        currentHarvestBanked = handHarvestBanked;
        netCatchBundleTier = handBundleTier;
        netCatchVisualPieceCount = handVisualPieceCount;
        return true;
    }

    private NetLine GetNetLineFromLoweringProgress(float progress01)
    {
        if (progress01 < 0.34f)
        {
            return NetLine.High;
        }

        if (progress01 < 0.72f)
        {
            return NetLine.Mid;
        }

        return NetLine.Low;
    }

    private bool HandleRepairWorkAtWorldTarget(float deltaTime)
    {
        return TickRepairWorkAtWorldTarget(
            deltaTime,
            Input.GetKeyDown(KeyCode.F),
            Input.GetKey(KeyCode.F));
    }

    private bool TickRepairWorkAtWorldTarget(float deltaTime, bool pressedThisFrame, bool held)
    {
        // The repair result is a one-way commit. Runtime input normally stops calling
        // this method after completion, but the guard also protects editor probes and
        // future callers from consuming the same physical materials twice.
        if (repairChoiceApplied)
        {
            repairWorkActive = false;
            repairWorkProgress = 1f;
            return false;
        }

        RepairChoice choice;
        if (!TryGetClosestRepairChoice(out choice))
        {
            repairWorkActive = false;
            if (IsPlayerNearRestPoint())
            {
                return false;
            }
            if (pressedThisFrame)
            {
                lastActionHint = "把材料带到真实部位：屋底承重柱、收纳网、屋内睡铺/灶台/漏雨处，或船边的船体/帆/舱。";
                return true;
            }

            return false;
        }

        if (!CanStartRepairAtCurrentTime(choice))
        {
            repairWorkActive = false;
            if (pressedThisFrame)
            {
                lastActionHint = $"夜里无法安全开始{GetRepairChoiceName(choice)}：看不清受力点，也容易把工具掉进海里。已经开工的外修可以继续；屋面、内室和灶台可在屋内处理。";
            }

            return pressedThisFrame;
        }

        string missingMaterials;
        bool hasMaterials = HasRepairMaterials(choice, out missingMaterials);
        if (!hasMaterials && choice == RepairChoice.Bed && IsPlayerNearRestPoint())
        {
            repairWorkActive = false;
            return false;
        }

        if (harvestPhysicalState == HarvestPhysicalState.PlacedAtWork &&
            harvestPlacedRepairChoice != RepairChoice.None &&
            harvestPlacedRepairChoice != choice)
        {
            repairWorkActive = false;
            if (pressedThisFrame)
            {
                lastActionHint = $"这束材料还实际放在{GetRepairChoiceName(harvestPlacedRepairChoice)}旁，不能隔空跳到{GetRepairChoiceName(choice)}。先回原处完成这项修补。";
            }
            return pressedThisFrame;
        }

        if (pressedThisFrame && pendingRepairChoice != choice)
        {
            if (!hasMaterials)
            {
                lastActionHint = $"{GetRepairChoiceName(choice)}暂时开不了工：{missingMaterials}。现有 {GetMaterialStockText()}；材料可留到以后，也可以回床边空手休息推进下一潮。";
                return true;
            }

            pendingRepairChoice = choice;
            repairWorkProgress = 0f;
            repairWorkStep = (int)TideRepairWorkPhase.Inspect;
            repairWorkActive = false;
            if (!currentHarvestBanked && currentHarvest != HarvestKind.None)
            {
                harvestPhysicalState = HarvestPhysicalState.PlacedAtWork;
                harvestPlacedRepairChoice = choice;
                harvestPlacementTransition01 = 0f;
                if (extraSaltWoodOwner == ExtraSaltWoodOwner.Carried)
                {
                    SetExtraSaltWoodOwner(ExtraSaltWoodOwner.PlacedAtWork);
                }
            }

            lastActionHint = $"先检查{GetRepairChoiceName(choice)}：{GetRepairWorkInstruction(choice, 1)} 按住 F 开工，松开会停在当前步骤。";
        }

        if (pendingRepairChoice != choice)
        {
            repairWorkActive = false;
            return pressedThisFrame;
        }

        if (!held)
        {
            if (repairWorkActive)
            {
                int pausedPercent = Mathf.RoundToInt(repairWorkProgress * 100f);
                lastActionHint = $"{GetRepairChoiceName(choice)}停在 {pausedPercent}%；材料和部件都留在原位，靠近后继续按住 F。";
            }
            repairWorkActive = false;
            return false;
        }

        if (!hasMaterials)
        {
            repairWorkActive = false;
            lastActionHint = $"{GetRepairChoiceName(choice)}缺料：{missingMaterials}。现有 {GetMaterialStockText()}；可回床边休息，不会被这一项卡住。";
            return true;
        }

        repairWorkActive = true;
        repairWorkProgress = Mathf.Clamp01(repairWorkProgress + deltaTime / GetRepairWorkDuration(choice));
        repairWorkStep = (int)TideRepairWorkPhaseModel.Evaluate(repairWorkProgress);
        int percent = Mathf.RoundToInt(repairWorkProgress * 100f);
        lastActionHint = $"{GetRepairChoiceName(choice)} {percent}%：{GetRepairWorkInstruction(choice, repairWorkStep)}";
        if (repairWorkProgress < 0.999f)
        {
            return true;
        }

        if (!TryConsumeRepairMaterials(choice, out missingMaterials))
        {
            repairWorkActive = false;
            lastActionHint = $"刚要固定时发现材料不足：{missingMaterials}。进度保留，补足后继续。";
            return true;
        }

        repairWorkActive = false;
        repairWorkStep = (int)TideRepairWorkPhase.Seal;
        repairWorkProgress = 1f;
        repairChoiceApplied = true;
        harvestPhysicalState = currentHarvestBanked
            ? HarvestPhysicalState.Stored
            : HarvestPhysicalState.None;
        harvestPlacedRepairChoice = RepairChoice.None;
        stateTimer = 0f;
        repairStartStiltIntegrity = stiltIntegrity;
        repairStartBoatReadiness = boatReadiness;
        repairStartHouseWarmth = houseWarmth;
        string resultText = ApplyRepairChoiceEffect(choice);
        lastActionHint = IsDepartureReady()
            ? $"{GetRepairChoiceName(choice)}完成：{resultText} 灯塔线和船都够了，下一次走到船边按 F。"
            : $"{GetRepairChoiceName(choice)}完成：{resultText} 回屋内床边按 F 休息，材料和修复都会保留。";
        return true;
    }

    private bool CanStartRepairAtCurrentTime(RepairChoice choice)
    {
        if (dayNightPhase != DayNightPhase.Night || !IsOutdoorRepairChoice(choice))
        {
            return true;
        }

        // Preserve a job whose material is already placed at the real component.
        // Night changes what can be started; it never discards visible repair progress.
        return pendingRepairChoice == choice;
    }

    private bool IsOutdoorRepairChoice(RepairChoice choice)
    {
        return choice == RepairChoice.Stilt ||
            choice == RepairChoice.Cistern ||
            choice == RepairChoice.Net ||
            choice == RepairChoice.Hull ||
            choice == RepairChoice.Sail ||
            choice == RepairChoice.Cabin;
    }

    private bool TryGetClosestRepairChoice(out RepairChoice closestChoice)
    {
        closestChoice = RepairChoice.None;
        float closestDistance = float.PositiveInfinity;
        if (playerLane == WalkLane.TideFlat)
        {
            ConsiderRepairChoice(RepairChoice.Net, contextDistance, ref closestChoice, ref closestDistance);
            ConsiderRepairChoice(RepairChoice.Stilt, shoreWorkDistance, ref closestChoice, ref closestDistance);
            ConsiderRepairChoice(RepairChoice.Cistern, contextDistance, ref closestChoice, ref closestDistance);
            // The boat exposes three physical work areas. Choosing only the first
            // affordable recommendation made the hull steal input even while the
            // survivor stood beside the mast or cabin. Proximity now owns selection.
            ConsiderRepairChoice(RepairChoice.Hull, boatRepairDistance, ref closestChoice, ref closestDistance);
            ConsiderRepairChoice(RepairChoice.Sail, boatRepairDistance, ref closestChoice, ref closestDistance);
            ConsiderRepairChoice(RepairChoice.Cabin, boatRepairDistance, ref closestChoice, ref closestDistance);
        }
        else if (viewMode == SliceViewMode.Interior && playerLane == WalkLane.InteriorUpper)
        {
            // V35 gives these four components separate authored owners. A deliberately
            // small radius prevents one item from stealing input while the survivor is
            // visibly standing at another component.
            ConsiderRepairChoice(RepairChoice.InteriorSeal, interiorRepairDistance, ref closestChoice, ref closestDistance);
            ConsiderRepairChoice(RepairChoice.Workbench, interiorRepairDistance, ref closestChoice, ref closestDistance);
            ConsiderRepairChoice(RepairChoice.Bed, interiorRepairDistance, ref closestChoice, ref closestDistance);
            ConsiderRepairChoice(RepairChoice.ChartRadio, interiorRepairDistance, ref closestChoice, ref closestDistance);
            ConsiderRepairChoice(RepairChoice.Lamp, contextDistance, ref closestChoice, ref closestDistance);
        }
        else if (viewMode == SliceViewMode.Interior && playerLane == WalkLane.InteriorLoft)
        {
            ConsiderRepairChoice(RepairChoice.Roof, contextDistance, ref closestChoice, ref closestDistance);
        }

        return closestChoice != RepairChoice.None;
    }

    private void ConsiderRepairChoice(
        RepairChoice candidate,
        float interactionDistance,
        ref RepairChoice closestChoice,
        ref float closestDistance)
    {
        if (candidate == RepairChoice.None || IsRepairComplete(candidate))
        {
            return;
        }

        float distance = Mathf.Abs(playerPosition.x - GetRepairChoicePosition(candidate).x);
        if (distance <= interactionDistance && distance < closestDistance)
        {
            closestChoice = candidate;
            closestDistance = distance;
        }
    }

    private float GetRepairWorkDuration(RepairChoice choice)
    {
        float baseDuration;
        if (choice == RepairChoice.Stilt || choice == RepairChoice.Hull || choice == RepairChoice.Cistern)
        {
            baseDuration = 4.8f;
        }
        else if (choice == RepairChoice.Sail || choice == RepairChoice.Roof || choice == RepairChoice.Cabin)
        {
            baseDuration = 4.1f;
        }
        else
        {
            baseDuration = 3.4f;
        }

        // A usable bench improves every later job, but cannot retroactively speed up
        // the work needed to rebuild the bench itself.
        return workbenchCondition > 0 && choice != RepairChoice.Workbench
            ? baseDuration * 0.8f
            : baseDuration;
    }

    private RepairChoice GetRecommendedBoatRepair()
    {
        RepairChoice[] choices = { RepairChoice.Hull, RepairChoice.Sail, RepairChoice.Cabin };
        for (int i = 0; i < choices.Length; i++)
        {
            string missing;
            if (!IsRepairComplete(choices[i]) && HasRepairMaterials(choices[i], out missing))
            {
                return choices[i];
            }
        }

        for (int i = 0; i < choices.Length; i++)
        {
            if (!IsRepairComplete(choices[i]))
            {
                return choices[i];
            }
        }

        return RepairChoice.None;
    }

    private bool IsRepairComplete(RepairChoice choice)
    {
        if (choice == RepairChoice.Stilt)
        {
            return stiltIntegrity >= 3;
        }
        if (choice == RepairChoice.Cistern)
        {
            return barrenIsland == null || barrenIsland.Cistern.Crack01 <= 0.1f;
        }
        if (choice == RepairChoice.Net)
        {
            return netIntegrity >= 3;
        }
        if (choice == RepairChoice.Roof)
        {
            return roofIntegrity >= 2;
        }
        if (choice == RepairChoice.InteriorSeal)
        {
            return interiorSealCondition >= 1;
        }
        if (choice == RepairChoice.Workbench)
        {
            return workbenchCondition >= 1;
        }
        if (choice == RepairChoice.Bed)
        {
            return bedCondition >= 1;
        }
        if (choice == RepairChoice.ChartRadio)
        {
            return chartRadioCondition >= 1;
        }
        if (choice == RepairChoice.Lamp)
        {
            return stoveCondition >= 2;
        }
        if (choice == RepairChoice.Hull)
        {
            return boatHullIntegrity >= 3;
        }
        if (choice == RepairChoice.Sail)
        {
            return boatSailIntegrity >= 2;
        }
        if (choice == RepairChoice.Cabin)
        {
            return boatCabinIntegrity >= 2;
        }

        return true;
    }

    private void TickBarrenIslandNaturalState(float deltaTime)
    {
        if (barrenIsland == null && heavyWreckSalvage == null && wrackLine == null)
        {
            return;
        }

        float storm01 = GetStormPressure01();
        float rain01 = TideContinuousWeatherModel.EvaluateRain01(storm01);
        float rainMillimetersPerHour = Mathf.Lerp(0f, 38f, rain01 * rain01);
        float roofCatchIntegrity01 = Mathf.Clamp01(roofIntegrity / 2f);
        float cisternRimY = GetPlayerStandingFeetY(WalkLane.TideFlat) + 1.05f;
        float localSurfaceY = GetOceanSample(TideBarrenIslandController.CisternX).SurfaceY;
        float overtopping01 = Mathf.InverseLerp(cisternRimY - 0.04f, cisternRimY + 0.48f, localSurfaceY) *
            Mathf.SmoothStep(0.55f, 1f, storm01);
        if (barrenIsland != null)
        {
            barrenIsland.TickNaturalState(
                deltaTime,
                rainMillimetersPerHour,
                roofCatchIntegrity01,
                overtopping01);
        }

        if (heavyWreckSalvage != null)
        {
            TideOceanSample heavyOcean = GetOceanSample(heavyWreckSalvage.SampleWorldX);
            heavyWreckSalvage.TickNaturalState(
                deltaTime,
                GetPlayerStandingFeetY(WalkLane.TideFlat),
                heavyOcean,
                EvaluateNaturalCurrentSpeed(tideClockSeconds));
        }

        if (wrackLine != null)
        {
            float localWaterY = GetOceanSample(wrackLine.SampleWorldX).SurfaceY;
            wrackLine.TickNaturalState(
                GetAstronomicalCycleOrdinal(weatherClockSeconds),
                localWaterY);
        }
    }

    private bool TryHandleBarrenIslandInteraction(float deltaTime)
    {
        if ((barrenIsland == null && heavyWreckSalvage == null && wrackLine == null) ||
            viewMode != SliceViewMode.Shelter || playerLane != WalkLane.TideFlat)
        {
            return false;
        }

        bool interactionPressed = Input.GetKeyDown(KeyCode.F);
        bool interactionHeld = Input.GetKey(KeyCode.F);
        bool handsFreeForWrack = AreHarvestHandsFree() &&
            (barrenIsland == null || barrenIsland.CarriedPart == TideIslandSalvagePart.None) &&
            (heavyWreckSalvage == null || !heavyWreckSalvage.IsCarryingPiece);
        if (interactionPressed && handsFreeForWrack && wrackLine != null &&
            wrackLine.TryCollect(
                new Vector2(playerPosition.x, GetPlayerStandingFeetY(WalkLane.TideFlat)),
                GetOceanSample(wrackLine.SampleWorldX).SurfaceY,
                out TideWrackDepositState collectedWrack))
        {
            BeginCarryingWrackDeposit(collectedWrack);
            return true;
        }

        if (heavyWreckSalvage != null &&
            heavyWreckSalvage.TryHandleInteraction(
                playerPosition,
                interactionPressed,
                interactionHeld,
                out string heavyWreckFeedback))
        {
            if (!string.IsNullOrEmpty(heavyWreckFeedback))
            {
                lastActionHint = heavyWreckFeedback;
            }
            return true;
        }

        if (barrenIsland == null)
        {
            return false;
        }

        bool nearCistern = barrenIsland.IsNearCistern(playerPosition);
        bool carryingCisternPlate = TideRepairRecipeModel.GetArrivalRepairTarget(
            barrenIsland.CarriedPart,
            TideIslandSalvageUse.Shelter) == RepairChoice.Cistern;
        bool nearShelterStaging = carryingCisternPlate
            ? nearCistern
            : Mathf.Abs(playerPosition.x - TideBarrenIslandController.ShelterDeliveryX) <= 0.52f;
        bool nearBoatStaging = Mathf.Abs(playerPosition.x - EscapeBoatStagingX) <= 0.52f;
        TideIslandContextAction action = TideIslandInteractionModel.Resolve(
            barrenIsland.CarriedPart,
            nearShelterStaging,
            nearBoatStaging,
            barrenIsland.IsNearWreck(playerPosition),
            nearCistern);

        bool cisternPlateReady = barrenIsland.GetDestination(TideIslandSalvagePart.RivetedPlate) ==
            TideIslandSalvageDestination.ShelterStaging &&
            barrenIsland.Cistern.Crack01 > 0.1f;
        if (action == TideIslandContextAction.DrinkFromCistern && cisternPlateReady)
        {
            // 一旦原板已经放在裂口旁，同一个 F 优先继续这件可见工作。完成后
            // 裂口不再抢占输入，蓄水池恢复普通饮水交互。
            return false;
        }

        if (interactionPressed &&
            (action == TideIslandContextAction.StageAtShelter ||
             action == TideIslandContextAction.StageAtEscapeBoat))
        {
            TideIslandSalvageUse use = action == TideIslandContextAction.StageAtShelter
                ? TideIslandSalvageUse.Shelter
                : TideIslandSalvageUse.EscapeBoat;
            if (!barrenIsland.TryStageCarriedPart(use, out TideIslandSalvagePart stagedPart))
            {
                return false;
            }

            lastActionHint = use == TideIslandSalvageUse.Shelter
                ? stagedPart == TideIslandSalvagePart.RivetedPlate
                    ? "铆接板放在裂蓄水池旁；它尚未固定，池壁和原船骸仍只拥有这一块板。"
                    : $"{stagedPart} 留在屋侧施工位；它尚未固定，也没有变成材料数字。"
                : $"{stagedPart} 留在船边检修位；检查、试装和固定后才会成为船的一部分。";
            return true;
        }

        bool continuingDismantle = barrenIsland.ActiveDismantlePart != TideIslandSalvagePart.None;
        if ((interactionPressed && action == TideIslandContextAction.TakeWreckPart) ||
            continuingDismantle)
        {
            TideOceanSample wreckOcean = GetOceanSample(playerPosition.x);
            float localHorizontalWaterSpeed =
                GetNaturalCurrentSpeed() + wreckOcean.HorizontalVelocity;
            float localWaveLoad01 = Mathf.Clamp01(
                wreckOcean.Agitation01 * 0.64f +
                Mathf.Abs(localHorizontalWaterSpeed) / 1.2f * 0.36f);
            Vector2 visibleFeet = new Vector2(
                playerPosition.x,
                GetPlayerStandingFeetY(WalkLane.TideFlat));
            bool hasStableFooting = !playerSwimming &&
                barrenIsland.IsVisibleWalkSupportAt(visibleFeet);
            bool consumed = barrenIsland.TickDismantleNearestPart(
                playerPosition,
                deltaTime,
                interactionPressed,
                interactionHeld,
                hasStableFooting,
                wreckOcean.SurfaceY,
                localWaveLoad01,
                out TideIslandDismantleFeedback dismantle);
            if (consumed)
            {
                arrivalInspected = true;
                Vector2 partPosition = barrenIsland.GetPartWorldPosition(dismantle.Part);
                if (Mathf.Abs(partPosition.x - playerPosition.x) > 0.02f)
                {
                    playerFacing = partPosition.x > playerPosition.x ? 1 : -1;
                }
                int percent = Mathf.RoundToInt(dismantle.Progress01 * 100f);
                if (dismantle.Completed)
                {
                    lastActionHint = dismantle.Part == TideIslandSalvagePart.HullPlank
                        ? "最后一枚旧钉松开；外板离开船骸，缺口和手中是同一件原物。"
                        : dismantle.Part == TideIslandSalvagePart.Sailcloth
                            ? "残存边绳割断；帆布离开母体，没有变成库存数字。"
                            : "铆边终于撬开；运输编号随原板一起留在手中。";
                }
                else if (dismantle.BlockReason == TideWreckDismantleBlockReason.NoStableFooting)
                {
                    lastActionHint = $"{dismantle.Part} 松动 {percent}%：水已经让脚下失去支撑，回到岩面露出的潮窗再继续。";
                }
                else if (dismantle.BlockReason == TideWreckDismantleBlockReason.BreakingWave)
                {
                    lastActionHint = $"{dismantle.Part} 松动 {percent}%：这道浪正在压船骸，等浪肩过去再用力。";
                }
                else if (dismantle.Worked)
                {
                    lastActionHint = $"正在拆 {dismantle.Part}：{percent}%。松手后它仍保持现在的松动位置。";
                }

                return true;
            }
        }

        if (interactionPressed && action == TideIslandContextAction.DrinkFromCistern)
        {
            float remainingNeed = Mathf.Max(0f, DailyRestWaterNeedLiters - waterConsumedSinceLastRest);
            if (remainingNeed <= 0.001f)
            {
                lastActionHint = "今天已经喝够了。裂池里的水要留给下一次无雨和下一场暴潮。";
                return true;
            }

            bool drank = barrenIsland.TryDrink(
                playerPosition,
                Mathf.Min(0.45f, remainingNeed),
                out float consumedLiters);
            waterConsumedSinceLastRest += consumedLiters;
            lastActionHint = drank
                ? "你喝了一小口。它会算进今天的饮水，不会在夜里再扣一遍。"
                : "池里的水太少或已经被盐水污染，不能直接喝。";
            return true;
        }

        return false;
    }

    private void BeginCarryingWrackDeposit(TideWrackDepositState collected)
    {
        currentHarvest = ToHarvestKind(collected.Material);
        currentHarvestBatchId = collected.BatchId;
        currentHarvestBanked = false;
        currentHarvestFromWrack = true;
        netCatchBundleTier = 1;
        netCatchVisualPieceCount = 1;
        harvestPhysicalState = HarvestPhysicalState.Carried;
        harvestCarryStartPosition = new Vector2(
            collected.WorldX,
            collected.GroundY + 0.12f);
        harvestCarryTransition01 = 0f;
        harvestPlacementTransition01 = 0f;
        harvestPlacedRepairChoice = RepairChoice.None;
        repairChoiceApplied = false;
        pendingRepairChoice = RepairChoice.None;
        repairWorkProgress = 0f;
        repairWorkActive = false;
        state = SliceState.RepairMoment;
        stateTimer = 0f;
        lastActionHint = $"你从退潮后的岩缝拾起一件{GetHarvestName()}。它只是漏过网口的同一批实物，数量少，但仍可带回储物架或施工位。";
    }

    private bool HandleStormRescueInteraction()
    {
        if (!IsStormRescueActive() || !stormRescue.CargoReleased ||
            playerLane != WalkLane.InteriorLower ||
            !Input.GetKey(KeyCode.F))
        {
            return false;
        }

        int nearestIndex = -1;
        float nearestDistance = 0.46f;
        float localCurrentSpeed = GetStormRescueLocalCurrentSpeed();
        for (int i = 0; i < TideStormRescueController.ItemCount; i++)
        {
            TideStormRescueItemState item = stormRescue.GetItem(i);
            if (!item.Present || item.Lost || item.Secured)
            {
                continue;
            }

            float distance = Vector2.Distance(
                playerPosition,
                GetStormRescueInteractionPosition(i, item, localCurrentSpeed));
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = i;
            }
        }

        if (nearestIndex < 0)
        {
            return false;
        }

        return stormRescue.TryHoldItem(nearestIndex);
    }

    private void TickStormRescue(float deltaTime)
    {
        if (!stormRescueManifestPrepared &&
            (GetStormPressure01() >= 0.34f || IsStormRescueActive()))
        {
            PrepareStormRescueManifest();
        }

        float localWaterDepth = GetStormRescueLocalWaterDepth();
        float currentSpeed = GetStormRescueLocalCurrentSpeed();
        TideStormRescueAdvanceResult result = stormRescue.Advance(
            deltaTime,
            localWaterDepth,
            currentSpeed);
        if (result.CargoReleasedThisStep)
        {
            float releaseImpact01 = Mathf.Clamp01(
                localWaterDepth / 0.8f + Mathf.Abs(currentSpeed) / 1.4f);
            shelterImpactTimer = Mathf.Max(shelterImpactTimer, 0.9f);
            TideAudioController.PlayStormShelfBreakCueInScene(releaseImpact01);
            lastActionHint = "第一股横浪撞塌了低层搁架，松动的实物开始沿破口水路移动。";
        }

        for (int i = 0; i < TideStormRescueController.ItemCount; i++)
        {
            int bit = 1 << i;
            TideStormRescueItemKind kind = (TideStormRescueItemKind)i;
            if ((result.SecuredMask & bit) != 0)
            {
                RestoreSecuredStormRescueCargo(kind);
                TideAudioController.PlayPickupCueInScene(0.72f);
                lastActionHint = "你把这件东西绑上了高处横梁。它离开水路，不会再被这一场潮带走。";
            }
            else if ((result.LostMask & bit) != 0)
            {
                ApplyStormRescueLoss(kind);
            }
        }
    }

    private bool IsStormRescueActive()
    {
        return GetStormRescueLocalWaterDepth() > TideStormRescueController.FloodDepthThresholdMeters;
    }

    private float GetStormRescueLocalWaterDepth()
    {
        return EvaluateStormRescueLocalWaterDepth(
            currentWaterY,
            GetStormPressure01(),
            shelterBreachThisTide);
    }

    private float EvaluateStormRescueLocalWaterDepth(
        float waterY,
        float stormPressure01,
        bool shelterBreached)
    {
        float rawDepth = Mathf.Max(
            0f,
            waterY - GetPlayerStandingFeetY(WalkLane.InteriorLower));
        float floodGate01 = shelterBreached
            ? 1f
            : Mathf.SmoothStep(0.58f, 0.86f, Mathf.Clamp01(stormPressure01));
        return rawDepth * floodGate01;
    }

    private float GetStormRescueLocalCurrentSpeed()
    {
        return EvaluateStormRescueLocalCurrentSpeed(
            tideClockSeconds,
            weatherClockSeconds,
            GetStormRescueLocalWaterDepth());
    }

    private float EvaluateStormRescueLocalCurrentSpeed(
        float sampleTideClock,
        float sampleWeatherClock,
        float localWaterDepthMeters)
    {
        float pressure01 = TideContinuousWeatherModel.EvaluatePressure01(
            sampleWeatherClock,
            dayLengthSeconds,
            stormFrontArrivalDays);
        float sampleMoonAgeDays = Mathf.Repeat(
            OpeningMoonAgeDays + sampleWeatherClock / Mathf.Max(30f, dayLengthSeconds),
            29.53f);
        float sampleTideStrength = CalculateTideStrength(sampleMoonAgeDays);
        float wind01 = TideContinuousWeatherModel.EvaluateStormOnshoreWind01(pressure01);
        TideOceanSample outsideSea = TideOceanFieldModel.Sample(
            0f,
            houseAnchor.x,
            sampleWeatherClock,
            sampleTideStrength,
            pressure01,
            wind01);

        // The astronomical current slows near high water, but a breached wall still
        // admits the orbital push and pull of each storm wave. The opening accelerates
        // that shared sea motion only while there is enough depth to carry an object.
        float depthCoupling01 = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(
                TideStormRescueController.FloodDepthThresholdMeters,
                0.42f,
                localWaterDepthMeters));
        float breachContraction = Mathf.Lerp(0.55f, 1.55f, depthCoupling01);
        float waveDrivenCurrent = outsideSea.HorizontalVelocity * breachContraction;
        return EvaluateNaturalCurrentSpeed(sampleTideClock) + waveDrivenCurrent;
    }

    private bool HasUnresolvedStormRescueCargo()
    {
        return stormRescueManifestPrepared && stormRescue.HasUnresolvedCargo();
    }

    private float GetSleepSkipSeconds()
    {
        float normalizedSleep = dayProgress01 < 0.23f
            ? 0.23f - dayProgress01
            : 1.23f - dayProgress01;
        return Mathf.Max(0f, normalizedSleep * dayLengthSeconds);
    }

    private bool WouldSleepIntervalFloodLooseStormCargo()
    {
        if (!HasUnresolvedStormRescueCargo())
        {
            return false;
        }

        if (GetStormRescueLocalWaterDepth() > TideStormRescueController.FloodDepthThresholdMeters)
        {
            return true;
        }

        float skippedSeconds = GetSleepSkipSeconds();
        if (skippedSeconds <= 0.001f)
        {
            return false;
        }

        bool projectedBreach = shelterBreachThisTide;
        if (!projectedBreach && !shelterStressAppliedThisTide &&
            SleepIntervalCrossesTidePeak(skippedSeconds))
        {
            int rawStress = CalculateRawShelterTideStress();
            bool stakeMitigated = HasActiveTidePrep() && selectedPrepChoice == TidePrepChoice.Stake;
            int resolvedStress = Mathf.Max(0, rawStress - (stakeMitigated ? 1 : 0));
            projectedBreach = resolvedStress > stiltIntegrity;
        }

        const int sampleCount = 72;
        for (int sample = 1; sample <= sampleCount; sample++)
        {
            float elapsed = skippedSeconds * sample / sampleCount;
            float futureWeatherClock = weatherClockSeconds + elapsed;
            float futureWaterY = EvaluateNaturalWaterY(
                tideClockSeconds + elapsed,
                futureWeatherClock);
            float futurePressure01 = TideContinuousWeatherModel.EvaluatePressure01(
                futureWeatherClock,
                dayLengthSeconds,
                stormFrontArrivalDays);
            float futureDepth = EvaluateStormRescueLocalWaterDepth(
                futureWaterY,
                futurePressure01,
                projectedBreach);
            if (futureDepth > TideStormRescueController.FloodDepthThresholdMeters)
            {
                return true;
            }
        }

        return false;
    }

    private bool CanRestThroughCurrentStormState()
    {
        return !HasUnresolvedStormRescueCargo() ||
            (stormRescue.CargoReleased &&
             GetStormRescueLocalWaterDepth() <= TideStormRescueController.FloodDepthThresholdMeters) ||
            !WouldSleepIntervalFloodLooseStormCargo();
    }

    private Vector2 GetStormRescueBasePosition(int index)
    {
        float left = GetLaneMinX(WalkLane.InteriorLower) + 0.44f;
        float right = GetLaneMaxX(WalkLane.InteriorLower) - 0.44f;
        float t = (Mathf.Clamp(index, 0, 3) + 0.5f) / 4f;
        return new Vector2(
            Mathf.Lerp(left, right, t),
            GetPlayerStandingFeetY(WalkLane.InteriorLower) + 0.13f);
    }

    private Vector2 GetStormRescueInteractionPosition(
        int index,
        TideStormRescueItemState item,
        float currentSpeedMetersPerSecond)
    {
        return TideStormRescueModel.EvaluateInteractionPosition(
            GetStormRescueBasePosition(index),
            GetStormRescueDryRackPosition(index),
            item,
            currentSpeedMetersPerSecond);
    }

    private Vector2 EvaluateStormRescueWorldPosition(
        int index,
        TideStormRescueItemState item,
        float currentSpeedMetersPerSecond)
    {
        return TideStormRescueModel.EvaluateWorldPosition(
            GetStormRescueBasePosition(index),
            GetStormRescueDryRackPosition(index),
            item,
            currentSpeedMetersPerSecond);
    }

    private int CountUnresolvedStormRescueCargo()
    {
        return stormRescue.CountUnresolvedCargo();
    }

    private void PrepareStormRescueManifest()
    {
        if (stormRescueManifestPrepared)
        {
            return;
        }

        stormRescueManifestPrepared = true;
        bool filled = barrenIsland != null && barrenIsland.TryFillPortableWaterContainer(
            StormRescueWaterContainerLiters,
            out stormRescueWater);
        SetStormRescueItemPresent(TideStormRescueItemKind.DrinkingWater, filled);

        bool hasBoatTimber = timberStock >= StormRescueBoatTimberUnits;
        if (hasBoatTimber)
        {
            timberStock -= StormRescueBoatTimberUnits;
            stormRescueReservedTimber = StormRescueBoatTimberUnits;
        }
        SetStormRescueItemPresent(TideStormRescueItemKind.BoatMaterial, hasBoatTimber);

        bool hasDryFuel = dryFuelBundles > 0;
        if (hasDryFuel)
        {
            dryFuelBundles--;
            stormRescueReservedFuelBundles = 1;
        }
        SetStormRescueItemPresent(TideStormRescueItemKind.StoveFuel, hasDryFuel);

        bool hasChart = lighthouseClues > 0;
        if (hasChart)
        {
            lighthouseClues--;
            stormRescueReservedChartClues = 1;
        }
        SetStormRescueItemPresent(TideStormRescueItemKind.LighthouseChart, hasChart);
    }

    private void SetStormRescueItemPresent(TideStormRescueItemKind kind, bool present)
    {
        stormRescue.SetItemPresent(kind, present);
    }

    private int GetStormRescuePresentMask()
    {
        return stormRescue.GetPresentMask();
    }

    private void RestoreSecuredStormRescueCargo(TideStormRescueItemKind kind)
    {
        if (kind == TideStormRescueItemKind.BoatMaterial && stormRescueReservedTimber > 0)
        {
            timberStock += stormRescueReservedTimber;
            stormRescueReservedTimber = 0;
        }
        else if (kind == TideStormRescueItemKind.StoveFuel && stormRescueReservedFuelBundles > 0)
        {
            dryFuelBundles += stormRescueReservedFuelBundles;
            stormRescueReservedFuelBundles = 0;
        }
        else if (kind == TideStormRescueItemKind.LighthouseChart && stormRescueReservedChartClues > 0)
        {
            lighthouseClues += stormRescueReservedChartClues;
            stormRescueReservedChartClues = 0;
        }
    }

    private void RecoverSurvivingStormCargoAtRest()
    {
        if (!stormRescueManifestPrepared)
        {
            return;
        }

        // Only cargo that physically survived an actual flood can be tidied away
        // during rest. A warning manifest cannot turn into secured stock before the
        // water arrives, and active floodwater can never move cargo upstairs for free.
        int securedMask = stormRescue.SecureSurvivorsAfterRecede(
            GetStormRescueLocalWaterDepth());
        for (int i = 0; i < TideStormRescueController.ItemCount; i++)
        {
            if ((securedMask & (1 << i)) == 0)
            {
                continue;
            }

            RestoreSecuredStormRescueCargo((TideStormRescueItemKind)i);
        }
    }

    private float ConsumeRestWater(float requestedLiters)
    {
        float request = Mathf.Max(0f, requestedLiters);
        stormRescueWater = TideRainCisternModel.ConsumePortableWater(
            stormRescueWater,
            request,
            out float fromContainer);
        float remaining = Mathf.Max(0f, request - fromContainer);
        float fromCistern = remaining > 0f && barrenIsland != null
            ? barrenIsland.WithdrawPotableWater(remaining)
            : 0f;
        return fromContainer + fromCistern;
    }

    private Vector2 GetStormRescueDryRackPosition(int index)
    {
        Vector2 basePosition = GetStormRescueBasePosition(index);
        float lowerFeetY = GetPlayerStandingFeetY(WalkLane.InteriorLower);
        float upperFloorUndersideY = GetPlayerStandingFeetY(WalkLane.InteriorUpper) - 0.24f;
        return new Vector2(
            basePosition.x,
            Mathf.Min(lowerFeetY + 1.08f, upperFloorUndersideY));
    }

    private Vector2 GetStormRescueBeamPosition(int index)
    {
        Vector2 rack = GetStormRescueDryRackPosition(index);
        return new Vector2(
            rack.x,
            GetPlayerStandingFeetY(WalkLane.InteriorUpper) - 0.035f);
    }

    private void ApplyStormRescueLoss(TideStormRescueItemKind kind)
    {
        if (kind == TideStormRescueItemKind.DrinkingWater)
        {
            // The can was already filled by transferring real litres out of the
            // cistern. Losing it destroys that single owner; it must not also deduct
            // the same litres from the tank a second time.
            stormRescueWater = default;
        }
        else if (kind == TideStormRescueItemKind.BoatMaterial)
        {
            stormRescueReservedTimber = 0;
        }
        else if (kind == TideStormRescueItemKind.StoveFuel)
        {
            stormRescueReservedFuelBundles = 0;
        }
        else if (kind == TideStormRescueItemKind.LighthouseChart)
        {
            stormRescueReservedChartClues = 0;
        }

        lastActionHint = "水把一件没有固定的东西从低层带走了；这一场不可能什么都保住。";
    }

    private bool IsPlayerNearArrivalWreck()
    {
        // 新左岛已经拥有可见的大船骸和三件唯一实物。旧 arrivalWreckX 仅保留
        // 为相机中段参考；对应三张小残骸在运行时已隐藏，不能留下看不见的 F 热区。
        return barrenIsland == null &&
            playerLane == WalkLane.TideFlat &&
            Mathf.Abs(playerPosition.x - arrivalWreckX) <= arrivalWreckInteractionDistance;
    }

    private void InspectArrivalWreck()
    {
        arrivalInspected = true;
        // This is a knowledge event, not a world-state gate. In particular, do not
        // reset stateTimer, tideClockSeconds, dayClockSeconds, weather or net progress.
        lastActionHint = "断桅上只剩一段湿绳和陌生刻痕。它说明你是被海难带到这里的，却没有让潮水从这一刻才开始。";
    }

    private bool IsPlayerNearNet()
    {
        if (playerLane != WalkLane.TideFlat)
        {
            return false;
        }

        float targetX = netAnchor.x;
        if (!netDeployed && !netSecuredEarly)
        {
            if (netRigStep == NetRigStep.Stored)
            {
                targetX = GetNetStoredX();
            }
            else if (netRigStep == NetRigStep.Carrying)
            {
                targetX = GetNetFirstStakeX();
            }
            else if (netRigStep == NetRigStep.FirstEndTied ||
                     netRigStep == NetRigStep.Unrolled ||
                     netRigStep == NetRigStep.SecondEndTied ||
                     netRigStep == NetRigStep.Lowering)
            {
                targetX = GetNetSecondStakeX();
            }
        }

        float interactionDistance = !netDeployed && !netSecuredEarly
            ? netRigInteractionDistance
            : contextDistance;
        return Mathf.Abs(playerPosition.x - targetX) <= interactionDistance;
    }

    private float GetNetStoredX()
    {
        // Keep the stored bundle visibly separate from the first stake. The pickup
        // press therefore cannot also count as the first knot while the player stands
        // inside one broad context radius.
        return netAnchor.x - 1.12f;
    }

    private float GetNetFirstStakeX()
    {
        return netAnchor.x - 0.62f;
    }

    private float GetNetSecondStakeX()
    {
        return netAnchor.x + 0.82f;
    }

    private bool IsPlayerNearRestPoint()
    {
        return viewMode == SliceViewMode.Interior &&
            playerLane == (HasRegisteredHousePresentation() ? WalkLane.InteriorUpper : WalkLane.InteriorLoft) &&
            Mathf.Abs(playerPosition.x - GetInteriorBedX()) <= contextDistance;
    }

    private bool IsPlayerNearInteriorStorage()
    {
        return viewMode == SliceViewMode.Interior &&
            playerLane == WalkLane.InteriorUpper &&
            Mathf.Abs(playerPosition.x - GetInteriorStorageX()) <= contextDistance * 0.82f;
    }

    private void StoreCurrentHarvestAtInteriorRack()
    {
        if (currentHarvest == HarvestKind.None || currentHarvestBanked)
        {
            return;
        }

        string storedName = GetHarvestName();
        BankCurrentHarvestMaterials();
        currentHarvest = HarvestKind.None;
        currentHarvestBanked = false;
        harvestPhysicalState = HarvestPhysicalState.Stored;
        harvestCarryTransition01 = 0f;
        harvestPlacementTransition01 = 0f;
        harvestCarryStartPosition = Vector2.zero;
        harvestPlacedRepairChoice = RepairChoice.None;
        lastActionHint = $"你把{storedName}放上生活层储物架，实物从手里移到屋内后才入账。现在可以用库存修补，也可以上阁楼休息。";
    }

    private bool IsPlayerNearLoftLookout()
    {
        return viewMode == SliceViewMode.Interior &&
            playerLane == WalkLane.InteriorLoft &&
            Mathf.Abs(playerPosition.x - GetInteriorLoftLookoutX()) <= contextDistance * 0.86f;
    }

    private void InspectTideFromLoft()
    {
        CaptureLoftForecast();
        lastActionHint = $"你从阁楼风口看了云脚、浪列和桩上的湿线：{GetLoftForecastText()} 这是粗略观察；修好海图潮尺后能缩小高潮范围和转流时差，但不会替你决定网深。";
    }

    private bool HasCurrentLoftForecast()
    {
        return IsForecastSnapshotCurrent(loftForecastSnapshot);
    }

    private string GetLoftForecastText()
    {
        if (!HasCurrentLoftForecast())
        {
            return string.Empty;
        }

        int tidePercent = Mathf.RoundToInt(tideStrength * 100f);
        return $"{GetPreviousHighWaterComparisonText(loftForecastSnapshot)} · {GetWeatherFrontName()} · {GetForecastHighWaterRangeText(loftForecastSnapshot)} · 潮强{tidePercent}% · {GetSailingWindText()} · {GetTidePrepRiskForecastText()}";
    }

    private void CaptureLoftForecast()
    {
        loftForecastSnapshot = CaptureCurrentForecast(chartRadioCondition > 0);
    }

    private void CaptureChartForecast()
    {
        chartForecastSnapshot = lampForecastCharges > 0
            ? CaptureCurrentForecast(true)
            : default;
    }

    private TideForecastSnapshot CaptureCurrentForecast(bool repairedChart)
    {
        float cycle = Mathf.Max(8f, tideCycleSeconds);
        float phase01 = Mathf.Repeat(tideClockSeconds / cycle, 1f);
        int currentCycleOrdinal = GetAstronomicalCycleOrdinal(weatherClockSeconds);
        int targetCycleOrdinal = TideMixedSemidiurnalModel.GetNextHighWaterCycleOrdinal(
            phase01,
            currentCycleOrdinal);
        return TideForecastSnapshotModel.Capture(
            targetCycleOrdinal,
            GetPredictedHighWaterY(),
            repairedChart);
    }

    private bool IsForecastSnapshotCurrent(TideForecastSnapshot snapshot)
    {
        float cycle = Mathf.Max(8f, tideCycleSeconds);
        float phase01 = Mathf.Repeat(tideClockSeconds / cycle, 1f);
        int currentCycleOrdinal = GetAstronomicalCycleOrdinal(weatherClockSeconds);
        return TideForecastSnapshotModel.IsCurrent(
            snapshot,
            phase01,
            currentCycleOrdinal);
    }

    private string GetPreviousHighWaterComparisonText(bool repairedChart)
    {
        return GetPreviousHighWaterComparisonText(
            GetPredictedHighWaterY(),
            repairedChart);
    }

    private string GetPreviousHighWaterComparisonText(TideForecastSnapshot snapshot)
    {
        return GetPreviousHighWaterComparisonText(
            (snapshot.LowerY + snapshot.UpperY) * 0.5f,
            snapshot.RepairedChart);
    }

    private string GetPreviousHighWaterComparisonText(
        float predictedHighWaterY,
        bool repairedChart)
    {
        if (!highWaterMemory.HasPreviousCycle)
        {
            return "桩上还没有完整上一潮的湿线";
        }

        float difference = predictedHighWaterY - highWaterMemory.PreviousCyclePeakY;
        float comparisonThreshold = repairedChart ? 0.08f : 0.16f;
        if (difference > comparisonThreshold)
        {
            return "下一高潮可能越过旧湿线";
        }

        if (difference < -comparisonThreshold)
        {
            return "下一高潮可能低于旧湿线";
        }

        return "下一高潮可能贴近旧湿线";
    }

    private string GetTidePrepRiskForecastText()
    {
        // The lookout forecasts physical pressure, not a prescribed answer. A player
        // planning to sail may still take the bucket while the house is under modest
        // pressure; showing all three risks preserves that decision instead of turning
        // the workbench into a single highlighted "correct" button.
        // Show the pressure of a greedy near-bottom setup without calling it the answer.
        // The repaired chart narrows the predicted high-water range, while the player
        // still chooses whether extra contact is worth spending the only prep slot on rope.
        int netStress = CalculateForecastNetStress(0.95f, tideStrength);
        int shelterStress = CalculateRawShelterTideStress();
        float ingressPerSecond = CalculateForecastBoatIngressPerSecond(boatHullIntegrity, tideStrength);
        string ingressBand = ingressPerSecond >= 0.03f ? "快" : ingressPerSecond >= 0.012f ? "中" : "慢";
        return $"风险 深网压{netStress}/3 · 屋压{shelterStress}/2 · 船漏{ingressBand}";
    }

    private int CalculateForecastNetStress(float depth01, float forecastTideStrength)
    {
        return CalculateForecastNetStress(depth01, forecastTideStrength, GetPredictedStormPressureAtNextHighWater01());
    }

    private int CalculateForecastNetStress(float depth01, float forecastTideStrength, float forecastStormPressure01)
    {
        // Forecast and runtime share the same impulse thresholds. The forecast uses a
        // representative full catch window; real play still depends on when the
        // player lifts, what enters the mesh and how the weather front develops.
        float weatherLoad = TideContinuousWeatherModel.EvaluateWaveLoadMultiplier(forecastStormPressure01);
        float forecastImpulse = TideNetLoadLedgerModel.EstimateFullTideImpulse(
            depth01,
            forecastTideStrength,
            weatherLoad);
        float forecastPeak01 = Mathf.Clamp01(
            Mathf.Clamp01(forecastTideStrength) * weatherLoad * 0.72f);
        return TideNetLoadLedgerModel.EvaluateCommittedStress(
            forecastImpulse,
            forecastPeak01,
            false);
    }

    private float CalculateForecastBoatIngressPerSecond(int forecastHullIntegrity, float forecastTideStrength)
    {
        return CalculateForecastBoatIngressPerSecond(
            forecastHullIntegrity,
            forecastTideStrength,
            GetPredictedStormPressureAtNextHighWater01());
    }

    private float CalculateForecastBoatIngressPerSecond(
        int forecastHullIntegrity,
        float forecastTideStrength,
        float forecastStormPressure01)
    {
        // Use a representative half-sail, half-speed crossing. The player can improve
        // the real result by reefing and slowing down, while a damaged hull remains
        // visibly riskier than a repaired one under the same sea state.
        float hullTightness01 = Mathf.Clamp01(forecastHullIntegrity / 3f);
        float leakRate = Mathf.Lerp(0.052f, 0.005f, hullTightness01);
        const float representativeTrim01 = 0.58f;
        const float representativeSpeed01 = 0.5f;
        float sailExposure = Mathf.Lerp(0.58f, 1.28f, representativeTrim01);
        float weatherLoad = TideContinuousWeatherModel.EvaluateWaveLoadMultiplier(forecastStormPressure01);
        float roughness = (0.82f + Mathf.Clamp01(forecastTideStrength) * 0.32f + representativeSpeed01 * 0.18f) *
            sailExposure * weatherLoad;
        return leakRate * roughness;
    }

    private bool CanChooseTidePrep()
    {
        return viewMode == SliceViewMode.Interior && playerLane == WalkLane.InteriorLower && IsTidePrepWindowOpen();
    }

    private bool IsTidePrepWindowOpen()
    {
        if (viewMode != SliceViewMode.Interior || state == SliceState.FinalDeparture)
        {
            return false;
        }

        bool eveningOrNight = dayNightPhase == DayNightPhase.Dusk ||
            dayNightPhase == DayNightPhase.Night;
        bool planningWindow = state == SliceState.LowTidePlanning && eveningOrNight;
        bool postRepairWindow = state == SliceState.RepairMoment && repairChoiceApplied && eveningOrNight;
        // Finishing a daytime repair should leave the changed house readable. The
        // three next-tide workstations become available with the evening routine,
        // instead of all popping into the completion frame like reward icons.
        return planningWindow || postRepairWindow;
    }

    private bool HandleTidePrepWorkAtWorldTarget(float deltaTime)
    {
        return TickTidePrepWorkAtWorldTarget(
            deltaTime,
            Input.GetKeyDown(KeyCode.F),
            Input.GetKey(KeyCode.F));
    }

    private bool TickTidePrepWorkAtWorldTarget(float deltaTime, bool pressedThisFrame, bool held)
    {
        TidePrepChoice closestChoice = GetNearbyTidePrepChoice();
        if (closestChoice == TidePrepChoice.None)
        {
            if (tidePrepWorkActive)
            {
                int pausedPercent = Mathf.RoundToInt(tidePrepWorkProgress * 100f);
                lastActionHint = $"{GetPrepChoiceName(pendingTidePrepChoice)}准备停在 {pausedPercent}%；材料留在原工作台，回来继续按住 F。";
            }
            tidePrepWorkActive = false;
            return false;
        }

        if (tidePrepReady && selectedPrepChoice == closestChoice && pendingTidePrepChoice == TidePrepChoice.None)
        {
            if (pressedThisFrame)
            {
                lastActionHint = $"{GetPrepChoiceName(closestChoice)}已经备好。要改选就走到另一座工作台按住 F。";
            }
            return pressedThisFrame;
        }

        if (pressedThisFrame && pendingTidePrepChoice != closestChoice)
        {
            pendingTidePrepChoice = closestChoice;
            tidePrepWorkProgress = 0f;
            tidePrepWorkActive = false;
            lastActionHint = $"开始准备{GetPrepChoiceName(closestChoice)}：按住 F 整理，松开会保留当前进度。完成前不会替换已备工具。";
        }

        if (pendingTidePrepChoice != closestChoice)
        {
            tidePrepWorkActive = false;
            return pressedThisFrame;
        }

        if (!held)
        {
            if (tidePrepWorkActive)
            {
                int pausedPercent = Mathf.RoundToInt(tidePrepWorkProgress * 100f);
                lastActionHint = $"{GetPrepChoiceName(closestChoice)}准备停在 {pausedPercent}%；继续按住 F 即可续作。";
            }
            tidePrepWorkActive = false;
            return false;
        }

        tidePrepWorkActive = true;
        tidePrepActionTimer = Mathf.Max(tidePrepActionTimer, 0.12f);
        tidePrepWorkProgress = Mathf.Clamp01(
            tidePrepWorkProgress + Mathf.Max(0f, deltaTime) / Mathf.Max(0.2f, tidePrepHoldSeconds));
        int percent = Mathf.RoundToInt(tidePrepWorkProgress * 100f);
        lastActionHint = $"正在准备{GetPrepChoiceName(closestChoice)} {percent}%：{GetPrepEffectText(closestChoice)}";
        if (tidePrepWorkProgress < 0.999f)
        {
            return true;
        }

        selectedPrepChoice = closestChoice;
        tidePrepReady = true;
        tidePrepTargetRound = state == SliceState.RepairMoment ? tideRound + 1 : tideRound;
        tidePrepActionTimer = 0.45f;
        pendingTidePrepChoice = TidePrepChoice.None;
        tidePrepWorkProgress = 0f;
        tidePrepWorkActive = false;
        lastActionHint = $"你在工作层备好{GetPrepChoiceName(closestChoice)}。{GetPrepEffectText(closestChoice)} 可改选，上阁楼到床边按 F 后带进下一潮。";
        return true;
    }

    private TidePrepChoice GetNearbyTidePrepChoice()
    {
        TidePrepChoice closestChoice = TidePrepChoice.None;
        float closestDistance = contextDistance * 0.72f;
        TidePrepChoice[] choices = { TidePrepChoice.Rope, TidePrepChoice.Bucket, TidePrepChoice.Stake };
        for (int i = 0; i < choices.Length; i++)
        {
            float distance = Mathf.Abs(playerPosition.x - GetNightPrepPosition(choices[i]).x);
            if (distance <= closestDistance)
            {
                closestDistance = distance;
                closestChoice = choices[i];
            }
        }

        return closestChoice;
    }

    private Vector2 GetNightPrepPosition(TidePrepChoice choice)
    {
        if (HasCompleteV35HouseInteriorPresentation())
        {
            // V35 的潮间层只允许固定耐水设施。三处交互锚点沿同一块下层
            // 木台分布，分别对应绑在桩上的绳架、倒挂桶位和束桩位；它们
            // 不再沿用旧室内把三件整套家具摊在地上的世界坐标。
            float authoredX = choice == TidePrepChoice.Rope
                ? 610f
                : choice == TidePrepChoice.Bucket
                    ? 830f
                    : 1030f;
            Sprite fixedSprite = GetV35FixedTidePrepSprite(choice);
            Vector2 installationSize = fixedSprite != null
                ? GetV35TidePrepInstallationSize(choice)
                : GetFormalNightPrepSize(choice) * 0.34f;
            float clearance = fixedSprite != null
                ? GetV35TidePrepInstallationClearance(choice)
                : 0.01f;
            float installationFloorY = GetPlayerStandingFeetY(WalkLane.InteriorLower);
            return new Vector2(
                GetV35WorldAnchorX(new Vector2(authoredX, 1636f)),
                installationFloorY + clearance + installationSize.y * 0.5f);
        }

        float x = choice == TidePrepChoice.Rope
            ? -4.2f
            : choice == TidePrepChoice.Bucket
                ? -2.65f
                : -1.45f;
        // Formal workstation sprites use a centred pivot. Place their lower edge on
        // the same catwalk surface used by the actor instead of floating them around
        // the actor's waist height.
        float workstationHeight = GetFormalNightPrepSprite(choice) != null
            ? GetFormalNightPrepSize(choice).y * 0.72f
            : GetPrepChoiceScale(choice).y;
        float floorY = GetPlayerStandingFeetY(WalkLane.InteriorLower);
        return new Vector2(x, floorY + workstationHeight * 0.5f + 0.02f);
    }

    private static Vector2 GetV35TidePrepInstallationSize(TidePrepChoice choice)
    {
        if (choice == TidePrepChoice.Bucket)
        {
            return new Vector2(0.42f, 0.68f);
        }

        if (choice == TidePrepChoice.Stake)
        {
            return new Vector2(0.34f, 0.76f);
        }

        return new Vector2(0.50f, 0.72f);
    }

    private static float GetV35TidePrepInstallationClearance(TidePrepChoice choice)
    {
        if (choice == TidePrepChoice.Bucket)
        {
            return 0.14f;
        }

        return choice == TidePrepChoice.Stake ? 0.06f : 0.03f;
    }

    private bool HasActiveTidePrep()
    {
        return HasPreparedTidePrep() && tidePrepTargetRound == tideRound;
    }

    private bool HasPreparedTidePrep()
    {
        return tidePrepReady && selectedPrepChoice != TidePrepChoice.None;
    }

    private void HandlePlayerMovement(float deltaTime)
    {
        playerMoving = false;
        if (state == SliceState.FinalDeparture)
        {
            playerHorizontalVelocity = 0f;
            return;
        }

        if (viewMode == SliceViewMode.Sailing)
        {
            playerHorizontalVelocity = 0f;
            HandleSailingInput(deltaTime);
            return;
        }

        if (viewMode == SliceViewMode.Lookout)
        {
            playerHorizontalVelocity = 0f;
            return;
        }

        if (isLaneTransitioning)
        {
            TickLaneTransition(deltaTime);
            return;
        }

        if (viewMode == SliceViewMode.Interior && TryStartInteriorLaneTransition())
        {
            return;
        }

        if (viewMode == SliceViewMode.Shelter && TryStartExteriorLaneTransition())
        {
            return;
        }

        // Pulling a wet net is a planted, two-handed action. Keeping the player at the
        // post prevents the rope from stretching across the whole shore while A/D is held.
        if (IsActivelyHaulingNet() || IsActivelyRiggingNet() ||
            IsActivelyRepairing() || IsActivelyDismantlingWreck())
        {
            playerHorizontalVelocity = 0f;
            return;
        }

        float horizontalInput = ReadPlayerHorizontalInput();
        TickPlayerHorizontalLocomotion(horizontalInput, deltaTime);
        TryStartMooringScreenTransition(horizontalInput);
    }

    private void TryStartMooringScreenTransition(float horizontalInput)
    {
        if (viewMode != SliceViewMode.Shelter || playerLane != WalkLane.TideFlat ||
            boatViewTransition != BoatViewTransition.None || Mathf.Abs(horizontalInput) <= 0.01f)
        {
            return;
        }

        // “归处/泊位”只用于调试信息和局部交互归类，不再触发黑屏或坐标交接。
        // 人物始终沿同一条世界木路移动，相机在后方连续跟随；因此画面边缘、
        // 行走边界和船艉不会再出现三个互不相干的切点。
        mooringScreenActive = playerPosition.x >= GetMooringScreenSeamX();
    }

    private float GetMooringScreenSeamX()
    {
        // 这是连续镜头的语义中点，不是传送点。放在残骸与船艉之间，确保
        // 归处侧包含布网/维修，泊位侧包含完整第二段木路和整艘船。
        return Mathf.Lerp(arrivalWreckX, GetBoatBoardingX(), 0.56f);
    }

    private float ReadPlayerHorizontalInput()
    {
        float direction = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
        {
            direction -= 1f;
        }

        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
        {
            direction += 1f;
        }

        return Mathf.Clamp(direction, -1f, 1f);
    }

    private bool TryStartInteriorLaneTransition()
    {
        const float stairUseDistance = 0.52f;
        bool wantsUp = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
        bool wantsDown = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow);
        if (wantsUp)
        {
            if (playerLane == WalkLane.InteriorLower &&
                Mathf.Abs(playerPosition.x - GetInteriorStairBottomPosition().x) <= stairUseDistance)
            {
                StartLaneTransition(WalkLane.InteriorUpper);
                return true;
            }

            if (playerLane == WalkLane.InteriorUpper &&
                Mathf.Abs(playerPosition.x - GetInteriorLoftStairBottomPosition().x) <= stairUseDistance)
            {
                StartLaneTransition(WalkLane.InteriorLoft);
                return true;
            }
        }

        if (wantsDown)
        {
            if (playerLane == WalkLane.InteriorLoft &&
                Mathf.Abs(playerPosition.x - GetInteriorLoftStairTopPosition().x) <= stairUseDistance)
            {
                StartLaneTransition(WalkLane.InteriorUpper);
                return true;
            }

            if (playerLane == WalkLane.InteriorUpper &&
                Mathf.Abs(playerPosition.x - GetInteriorStairTopPosition().x) <= stairUseDistance)
            {
                StartLaneTransition(WalkLane.InteriorLower);
                return true;
            }
        }

        return false;
    }

    private bool TryStartExteriorLaneTransition()
    {
        bool wantsUp = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
        bool wantsDown = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow);
        return TryStartExteriorLaneTransition(wantsUp, wantsDown);
    }

    private bool TryStartExteriorLaneTransition(bool wantsUp, bool wantsDown)
    {
        // A full curved hull rib is dragged along the ground, not carried upright.
        // Keeping it on the flat work lane prevents a long sprite from clipping through
        // the gangway while also making the transport cost physically legible.
        if (heavyWreckSalvage != null && heavyWreckSalvage.IsCarryingPiece)
        {
            return false;
        }

        const float stairUseDistance = 0.52f;
        if (wantsDown &&
            playerLane == WalkLane.Deck &&
            Mathf.Abs(playerPosition.x - GetGangwayTopPosition().x) <= stairUseDistance)
        {
            StartLaneTransition(WalkLane.TideFlat);
            return true;
        }

        if (wantsUp &&
            playerLane == WalkLane.TideFlat &&
            Mathf.Abs(playerPosition.x - GetGangwayBottomPosition().x) <= stairUseDistance)
        {
            StartLaneTransition(WalkLane.Deck);
            return true;
        }

        return false;
    }

    private void TickPlayerHorizontalLocomotion(float inputDirection, float deltaTime)
    {
        float heavyDragFactor = heavyWreckSalvage != null && heavyWreckSalvage.IsCarryingPiece
            ? 0.48f
            : 1f;
        float maxSpeed = playerMoveSpeed * (playerSwimming ? 0.58f : 1f) * heavyDragFactor;
        float targetVelocity = inputDirection * maxSpeed;
        bool reversing = Mathf.Abs(inputDirection) > 0.01f &&
            Mathf.Abs(playerHorizontalVelocity) > 0.05f &&
            Mathf.Sign(inputDirection) != Mathf.Sign(playerHorizontalVelocity);
        float acceleration = playerSwimming
            ? (Mathf.Abs(inputDirection) > 0.01f ? playerSwimAcceleration : playerSwimDeceleration)
            : Mathf.Abs(inputDirection) <= 0.01f
                ? playerGroundDeceleration
                : reversing
                    ? playerTurnAcceleration
                    : playerGroundAcceleration;

        playerHorizontalVelocity = Mathf.MoveTowards(
            playerHorizontalVelocity,
            targetVelocity,
            Mathf.Max(0.1f, acceleration) * deltaTime);
        if (Mathf.Abs(inputDirection) <= 0.01f && Mathf.Abs(playerHorizontalVelocity) < 0.025f)
        {
            playerHorizontalVelocity = 0f;
        }

        // Do not flip the body while momentum still carries it the other way. The sprite
        // turns as the velocity crosses zero, avoiding a single-frame moonwalk on reversal.
        if (Mathf.Abs(playerHorizontalVelocity) > 0.04f)
        {
            playerFacing = playerHorizontalVelocity > 0f ? 1 : -1;
        }
        else if (Mathf.Abs(inputDirection) > 0.01f)
        {
            playerFacing = inputDirection > 0f ? 1 : -1;
        }

        float speed01 = maxSpeed > 0.01f ? Mathf.Clamp01(Mathf.Abs(playerHorizontalVelocity) / maxSpeed) : 0f;
        playerMoving = speed01 > 0.025f;
        if (!playerMoving)
        {
            // 每次真正停稳都回到接触姿势。旧实现按全局 Time 选帧，重新按键时
            // 会从任意跨步姿势起步，人物像被瞬间换了一张图。
            playerWalkCycle = 0f;
            return;
        }

        playerWalkCycle += deltaTime * Mathf.Lerp(
            playerSwimming ? 0.55f : 0.42f,
            playerSwimming ? 1.05f : 0.92f,
            speed01);
        float nextX = Mathf.Clamp(
            playerPosition.x + playerHorizontalVelocity * deltaTime,
            GetLaneMinX(playerLane),
            GetLaneMaxX(playerLane));

        // 横向输入永远只属于当前可走面。外梯和室内梯都由梯口附近的 W/S
        // 显式进入，避免玩家只是想沿平台走动就被吸到另一层。
        float nextY = playerSwimming ? currentWaterY + 0.02f : GetPlayerLaneY(playerLane);
        playerPosition = new Vector2(nextX, nextY);
        if ((Mathf.Approximately(nextX, GetLaneMinX(playerLane)) && playerHorizontalVelocity < 0f) ||
            (Mathf.Approximately(nextX, GetLaneMaxX(playerLane)) && playerHorizontalVelocity > 0f))
        {
            playerHorizontalVelocity = 0f;
            playerMoving = false;
            playerWalkCycle = 0f;
        }
    }

    private bool IsActivelyHaulingNet()
    {
        bool haulState = state == SliceState.EbbCollect ||
            (state == SliceState.TideRising && netCatchResolved && netDeployed);
        return haulState && IsPlayerNearNet() && Input.GetKey(KeyCode.F);
    }

    private bool IsActivelyRiggingNet()
    {
        if (!netRigActionHeld || !IsPlayerNearNet())
        {
            return false;
        }

        // Walking is the work during FirstEndTied: it physically unrolls the net.
        // Every other held rigging action needs planted feet at the corresponding post.
        return netRigStep == NetRigStep.Carrying ||
            netRigStep == NetRigStep.Unrolled ||
            netRigStep == NetRigStep.Lowering;
    }

    private bool IsActivelyRepairing()
    {
        return state == SliceState.RepairMoment &&
            !repairChoiceApplied &&
            repairWorkActive &&
            Input.GetKey(KeyCode.F);
    }

    private bool IsActivelyDismantlingWreck()
    {
        return barrenIsland != null && barrenIsland.IsDismantling;
    }

    private void StartLaneTransition(WalkLane targetLane)
    {
        float maxSpeed = playerMoveSpeed * (playerSwimming ? 0.58f : 1f);
        laneTransitionEntrySpeed01 = maxSpeed > 0.01f
            ? Mathf.Clamp01(Mathf.Abs(playerHorizontalVelocity) / maxSpeed)
            : 0f;
        isLaneTransitioning = true;
        laneTransitionFromLane = playerLane;
        laneTransitionTarget = targetLane;
        laneTransitionProgress = 0f;
        laneTransitionFromPosition = playerPosition;
        if (targetLane == WalkLane.TideFlat)
        {
            laneTransitionToPosition = GetGangwayBottomPosition();
        }
        else if (targetLane == WalkLane.Deck)
        {
            laneTransitionToPosition = GetGangwayTopPosition();
        }
        else if (targetLane == WalkLane.InteriorLoft)
        {
            laneTransitionToPosition = GetInteriorLoftStairTopPosition();
        }
        else if (targetLane == WalkLane.InteriorLower)
        {
            laneTransitionToPosition = GetInteriorStairBottomPosition();
        }
        else
        {
            laneTransitionToPosition = laneTransitionFromLane == WalkLane.InteriorLoft
                ? GetInteriorLoftStairBottomPosition()
                : GetInteriorStairTopPosition();
        }
        laneTransitionDurationSeconds = CalculateLaneTransitionDuration(
            Vector2.Distance(laneTransitionFromPosition, laneTransitionToPosition));
        playerPosition = laneTransitionFromPosition;
        playerHorizontalVelocity = 0f;
    }

    private float CalculateLaneTransitionDuration(float pathDistance)
    {
        // 以米/秒作为设计参数，确保资源更换或梯子变长时，攀爬节奏仍保持一致。
        return Mathf.Max(0.45f, pathDistance / Mathf.Max(0.1f, ladderTravelSpeed));
    }

    private void TickLaneTransition(float deltaTime)
    {
        playerMoving = true;
        playerFacing = laneTransitionToPosition.x >= laneTransitionFromPosition.x ? 1 : -1;
        playerWalkCycle += deltaTime * Mathf.Lerp(1.35f, 2.15f, laneTransitionEntrySpeed01);
        laneTransitionProgress = Mathf.Clamp01(
            laneTransitionProgress + deltaTime / Mathf.Max(0.1f, laneTransitionDurationSeconds));
        float eased = Mathf.SmoothStep(0f, 1f, laneTransitionProgress);
        playerPosition = Vector2.Lerp(laneTransitionFromPosition, laneTransitionToPosition, eased);
        if (laneTransitionProgress < 0.999f)
        {
            return;
        }

        CompleteLaneTransition();
    }

    private void CompleteLaneTransition()
    {
        playerLane = laneTransitionTarget;
        playerPosition = laneTransitionToPosition;
        isLaneTransitioning = false;
        // 楼梯是一次明确的纵向动作；落地必须停稳，再读取下一帧的水平输入。
        // 旧逻辑把上梯前的 A/D 惯性带到下一层，表现成被梯口继续吸走。
        playerHorizontalVelocity = 0f;
        playerMoving = false;
        lastActionHint = playerLane == WalkLane.Deck
            ? "你顺着湿梯回到甲板。"
            : playerLane == WalkLane.TideFlat
                ? "你顺着湿梯下到潮滩，网桩和船都在右边。"
                : playerLane == WalkLane.InteriorLoft
                    ? "你沿内梯上到阁楼。这里最干燥，也最容易看清风、云和下一轮潮线。"
                : playerLane == WalkLane.InteriorUpper
                    ? laneTransitionFromLane == WalkLane.InteriorLoft
                        ? "你下到生活层，门、灶台和储物架都在这一层。"
                        : "你上到生活层，门、灶台和储物架都离开潮气。"
                    : "你下到桩间工作层，绳、桶和补桩都收在这里。";
    }

    private Vector2 GetGangwayTopPosition()
    {
        if (HasCompleteV32ArtPresentation())
        {
            // V33 的 1120/1040 是平台落脚口，V34/V35 可见梯身中心则是
            // 1160/1120。人物沿梯身中心移动，平台本身仍负责水平接近。
            Vector2 ladderTop = GetV32WorldAnchor(new Vector2(1160f, 1288f));
            return new Vector2(ladderTop.x, GetPlayerLaneY(WalkLane.Deck));
        }

        if (HasCompleteV30HousePresentation())
        {
            Vector2 exteriorStairTop = GetV30WorldAnchor(new Vector2(776f, 964f));
            return new Vector2(exteriorStairTop.x, GetPlayerLaneY(WalkLane.Deck));
        }

        return new Vector2(GetLaneSwitchX() - 0.12f, GetPlayerLaneY(WalkLane.Deck));
    }

    private Vector2 GetGangwayBottomPosition()
    {
        if (HasCompleteV32ArtPresentation())
        {
            Vector2 ladderBottom = GetV32WorldAnchor(new Vector2(1120f, 1636f));
            return new Vector2(ladderBottom.x, GetPlayerLaneY(WalkLane.TideFlat));
        }

        if (HasCompleteV30HousePresentation())
        {
            // V30 only declares the top anchor. Continue the visible diagonal down
            // to the tide-flat surface so the player follows one physical path.
            return new Vector2(
                GetGangwayTopPosition().x + 0.72f,
                GetPlayerLaneY(WalkLane.TideFlat));
        }

        return new Vector2(GetLaneSwitchX() + 0.58f, GetPlayerLaneY(WalkLane.TideFlat));
    }

    private float GetV32ClimbPlaybackProgress(
        WalkLane fromLane,
        WalkLane targetLane,
        float transitionProgress)
    {
        float progress = Mathf.Clamp01(transitionProgress);
        bool ascending = GetPlayerLaneY(targetLane) > GetPlayerLaneY(fromLane);
        return ascending ? progress : 1f - progress;
    }

    private Vector2 GetInteriorStairBottomPosition()
    {
        if (HasCompleteV35HouseInteriorPresentation())
        {
            Vector2 ladderBottom = GetV35WorldAnchor(new Vector2(1120f, 1636f));
            return new Vector2(ladderBottom.x, GetPlayerLaneY(WalkLane.InteriorLower));
        }

        return new Vector2(-0.35f, GetPlayerLaneY(WalkLane.InteriorLower));
    }

    private Vector2 GetInteriorStairTopPosition()
    {
        if (HasCompleteV35HouseInteriorPresentation())
        {
            Vector2 ladderTop = GetV35WorldAnchor(new Vector2(1160f, 1288f));
            return new Vector2(ladderTop.x, GetPlayerLaneY(WalkLane.InteriorUpper));
        }

        return new Vector2(-1.2f, GetPlayerLaneY(WalkLane.InteriorUpper));
    }

    private Vector2 GetInteriorLoftStairBottomPosition()
    {
        if (HasCompleteV35HouseInteriorPresentation())
        {
            Vector2 ladderBottom = GetV35WorldAnchor(new Vector2(1160f, 1288f));
            return new Vector2(ladderBottom.x, GetPlayerLaneY(WalkLane.InteriorUpper));
        }

        if (HasCompleteV30HousePresentation())
        {
            Vector2 ladderBottom = GetV30WorldAnchor(new Vector2(1042f, 712f));
            return new Vector2(ladderBottom.x, GetPlayerLaneY(WalkLane.InteriorUpper));
        }

        return new Vector2(-4.4f, GetPlayerLaneY(WalkLane.InteriorUpper));
    }

    private Vector2 GetInteriorLoftStairTopPosition()
    {
        if (HasCompleteV35HouseInteriorPresentation())
        {
            Vector2 ladderTop = GetV35WorldAnchor(new Vector2(1160f, 760f));
            return new Vector2(ladderTop.x, GetPlayerLaneY(WalkLane.InteriorLoft));
        }

        if (HasCompleteV30HousePresentation())
        {
            Vector2 ladderTop = GetV30WorldAnchor(new Vector2(1042f, 416f));
            return new Vector2(ladderTop.x, GetPlayerLaneY(WalkLane.InteriorLoft));
        }

        return new Vector2(-3.55f, GetPlayerLaneY(WalkLane.InteriorLoft));
    }

    private bool TryGetInteriorStairPrompt(out string prompt, out Vector2 position)
    {
        const float promptDistance = 0.62f;
        prompt = string.Empty;
        position = playerPosition;
        if (playerLane == WalkLane.InteriorLower &&
            Mathf.Abs(playerPosition.x - GetInteriorStairBottomPosition().x) <= promptDistance)
        {
            prompt = "W 上生活层";
            position = GetInteriorStairBottomPosition();
            return true;
        }

        if (playerLane == WalkLane.InteriorUpper)
        {
            if (Mathf.Abs(playerPosition.x - GetInteriorLoftStairBottomPosition().x) <= promptDistance)
            {
                prompt = "W 上阁楼";
                position = GetInteriorLoftStairBottomPosition();
                return true;
            }

            if (Mathf.Abs(playerPosition.x - GetInteriorStairTopPosition().x) <= promptDistance)
            {
                prompt = "S 下工作层";
                position = GetInteriorStairTopPosition();
                return true;
            }
        }

        if (playerLane == WalkLane.InteriorLoft &&
            Mathf.Abs(playerPosition.x - GetInteriorLoftStairTopPosition().x) <= promptDistance)
        {
            prompt = "S 下生活层";
            position = GetInteriorLoftStairTopPosition();
            return true;
        }

        return false;
    }

    private bool TryGetExteriorStairPrompt(out string prompt, out Vector2 position)
    {
        const float promptDistance = 0.62f;
        prompt = string.Empty;
        position = playerPosition;
        if (playerLane == WalkLane.Deck &&
            Mathf.Abs(playerPosition.x - GetGangwayTopPosition().x) <= promptDistance)
        {
            prompt = "S 下梯";
            position = GetGangwayTopPosition();
            return true;
        }

        if (playerLane == WalkLane.TideFlat &&
            Mathf.Abs(playerPosition.x - GetGangwayBottomPosition().x) <= promptDistance)
        {
            prompt = "W 上梯";
            position = GetGangwayBottomPosition();
            return true;
        }

        return false;
    }

    private float GetInteriorBedX()
    {
        if (HasCompleteV35HouseInteriorPresentation())
        {
            return GetV35WorldAnchorX(new Vector2(960f, 1288f));
        }

        if (HasCompleteV30HousePresentation())
        {
            return GetV30WorldAnchorX(new Vector2(735f, 915f));
        }

        return HasCompleteV28HousePresentation() ? -2.95f : -1.78f;
    }

    private float GetInteriorStorageX()
    {
        if (HasCompleteV35HouseInteriorPresentation())
        {
            return GetV35WorldAnchorX(new Vector2(575f, 1288f));
        }

        return -3.35f;
    }

    private float GetInteriorLampX()
    {
        if (HasCompleteV35HouseInteriorPresentation())
        {
            return GetV35WorldAnchorX(new Vector2(1135f, 1288f));
        }

        if (HasCompleteV30HousePresentation())
        {
            return GetV30WorldAnchorX(new Vector2(945f, 825f));
        }

        return -1.95f;
    }

    private float GetInteriorLoftLookoutX()
    {
        if (HasCompleteV35HouseInteriorPresentation())
        {
            return GetV35WorldAnchorX(new Vector2(960f, 760f));
        }

        if (HasCompleteV30HousePresentation())
        {
            return GetV30WorldAnchorX(new Vector2(1042f, 416f));
        }

        return HasCompleteV28HousePresentation() ? -2.5f : -4.72f;
    }

    private bool IsPlayerNearInteriorDoor()
    {
        return viewMode == SliceViewMode.Shelter &&
            playerLane == WalkLane.Deck &&
            Mathf.Abs(playerPosition.x - GetInteriorDoorX()) <= contextDistance;
    }

    private bool IsPlayerNearInteriorExit()
    {
        return viewMode == SliceViewMode.Interior &&
            playerLane == WalkLane.InteriorUpper &&
            Mathf.Abs(playerPosition.x - GetInteriorDoorX()) <= contextDistance;
    }

    private float GetInteriorDoorX()
    {
        if (HasCompleteV32ArtPresentation())
        {
            // 外景门只决定切换前后的站位；进入后仍由 V30 同比例剖面接管。
            Vector2 doorThreshold = GetV32WorldAnchor(new Vector2(790f, 1288f));
            return doorThreshold.x;
        }

        if (HasCompleteV30HousePresentation())
        {
            return GetV30WorldAnchorX(new Vector2(562f, 968f));
        }

        if (HasCompleteV28HousePresentation())
        {
            // The registered V26/V28 central threshold sits above the access stair.
            // It replaces the old wide-house doorway at houseAnchor + 0.65.
            return -3.5f;
        }

        return houseAnchor.x + 0.65f;
    }

    private float GetV30WorldAnchorX(Vector2 pixelTopLeft)
    {
        return GetFormalHouseWorldPosition().x +
            TideV30HousePresentationModel.PixelTopLeftToWorldOffset(pixelTopLeft).x;
    }

    private Vector2 GetV30WorldAnchor(Vector2 pixelTopLeft)
    {
        return GetFormalHouseWorldPosition() +
            TideV30HousePresentationModel.PixelTopLeftToWorldOffset(pixelTopLeft);
    }

    private float GetV35WorldAnchorX(Vector2 pixelTopLeft)
    {
        return GetFormalHouseWorldPosition().x +
            TideV35HouseInteriorPresentationModel.PixelTopLeftToWorldOffset(pixelTopLeft).x;
    }

    private float GetV35OwnerWorldX(string ownerKey, float fallbackX)
    {
        EnsureV35HouseInteriorResourcesLoaded();
        if (!HasCompleteV35HouseInteriorPresentation())
        {
            return fallbackX;
        }

        float uniformScale = GetFormalHouseWorldSize().x /
            Mathf.Max(0.001f, formalHouseV35InteriorCatalog.StableBase.bounds.size.x);
        TideV35HouseInteriorCatalog.OwnerEntry[] owners = formalHouseV35InteriorCatalog.Owners;
        for (int i = 0; i < owners.Length; i++)
        {
            TideV35HouseInteriorCatalog.OwnerEntry owner = owners[i];
            if (owner != null && owner.Key == ownerKey)
            {
                return GetFormalHouseWorldPosition().x + owner.WorldOffsetFromHousePivot.x * uniformScale;
            }
        }

        return fallbackX;
    }

    private Vector2 GetV35WorldAnchor(Vector2 pixelTopLeft)
    {
        return GetFormalHouseWorldPosition() +
            TideV35HouseInteriorPresentationModel.PixelTopLeftToWorldOffset(pixelTopLeft);
    }

    private Vector2 GetV32WorldAnchor(Vector2 pixelTopLeft)
    {
        // V32 外景是 2048 方画布、PPU 256、Pivot=(0.5, 0.03125)。
        // 坐标统一使用左上原点，便于直接对照资源契约和 QA 板。
        Vector2 offset = new Vector2(
            (pixelTopLeft.x - 1024f) / 256f,
            (2048f - pixelTopLeft.y - 64f) / 256f);
        return GetFormalHouseWorldPosition() + offset;
    }

    private void EnterStiltHouseInterior()
    {
        mooringScreenActive = false;
        viewMode = SliceViewMode.Interior;
        playerLane = WalkLane.InteriorUpper;
        playerPosition = new Vector2(GetInteriorDoorX(), GetPlayerLaneY(playerLane));
        playerHorizontalVelocity = 0f;
        playerSwimming = false;
        lastActionHint = "门内是生活层：灶台和储物架与外廊同高。右侧梯下到桩间工作层，左侧梯上到最干燥的阁楼。";
    }

    private void ExitStiltHouseInterior()
    {
        viewMode = SliceViewMode.Shelter;
        playerLane = WalkLane.Deck;
        playerPosition = new Vector2(GetInteriorDoorX(), GetPlayerLaneY(playerLane));
        playerHorizontalVelocity = 0f;
        lastActionHint = "你推门回到外廊，潮声重新变得开阔。";
    }

    private void EnterLookoutVista()
    {
        // Observation is knowledge, not a pause menu. The live tide/weather clocks
        // continue in Update while this view only changes how the same state is seen.
        InspectTideFromLoft();
        viewMode = SliceViewMode.Lookout;
        playerLane = WalkLane.InteriorLoft;
        playerPosition = new Vector2(GetInteriorLoftLookoutX(), GetPlayerLaneY(playerLane));
        playerHorizontalVelocity = 0f;
        playerMoving = false;
    }

    private void ExitLookoutVista()
    {
        viewMode = SliceViewMode.Interior;
        playerLane = WalkLane.InteriorLoft;
        playerPosition = new Vector2(GetInteriorLoftLookoutX(), GetPlayerLaneY(playerLane));
        playerHorizontalVelocity = 0f;
        playerMoving = false;
        lastActionHint = "你从风口退回阁楼；外面的潮和云仍按刚才的速度继续。";
    }

    private void HandleSailingInput(float deltaTime)
    {
        float direction = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
        {
            direction -= 1f;
        }

        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
        {
            direction += 1f;
        }

        // W/S changes one persistent sail setting instead of acting as another movement axis.
        // A fuller sail is faster but exposes the old hull to more wave pressure; reefing it
        // gives the player a deliberate way to slow down before inspecting a point or returning.
        float trimDirection = 0f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            trimDirection += 1f;
        }

        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
        {
            trimDirection -= 1f;
        }

        if (Mathf.Abs(trimDirection) > 0.01f)
        {
            sailingTrimActionTimer = 0.34f;
            sailingTrimCycle = Mathf.Repeat(sailingTrimCycle + deltaTime * 1.65f, 1f);
        }
        else
        {
            sailingTrimActionTimer = Mathf.MoveTowards(sailingTrimActionTimer, 0f, deltaTime);
        }

        float ballastDirection = 0f;
        if (Input.GetKey(KeyCode.Q))
        {
            ballastDirection -= 1f;
        }
        if (Input.GetKey(KeyCode.E))
        {
            ballastDirection += 1f;
        }
        sailingBallastInput = ballastDirection;

        // F remains the single context key. Near a point it inspects/returns; elsewhere holding
        // it means putting down the tiller and bailing. Bailing therefore costs momentum instead
        // of becoming a free heal that can be spammed while sailing at full speed.
        bool contextActionAvailable = CanReturnFromSailing() ||
            CanInteractAtSailingPoint() ||
            IsSailingSalvageInteractionActive();
        sailingBailing = Input.GetKey(KeyCode.F) && !contextActionAvailable && sailingWaterIngress01 > 0.012f;
        if (sailingBailing)
        {
            ApplySailingBail(deltaTime);
        }

        AdvanceSailingSteering(deltaTime, direction, trimDirection, ballastDirection);
    }

    private void AdvanceSailingSteering(
        float deltaTime,
        float direction,
        float sailInput = 0f,
        float ballastInput = 0f)
    {
        // Keyboard play and deterministic route probes share this exact integration.
        // The probe may choose a direction, but it cannot set position, velocity, wind,
        // current, hull leakage or range limits; those remain owned by the live world.
        float clampedDirection = Mathf.Clamp(direction, -1f, 1f);
        TideOceanSample ocean = GetSailingOceanSample(sailingBoatX);
        sailingDynamics.HorizontalVelocity = sailingBoatVelocity;
        sailingDynamics.SailRaised01 = sailingSailTrim01;
        sailingDynamics.Ingress01 = sailingWaterIngress01;
        sailingDynamics = TideSailboatDynamicsModel.Advance(
            sailingDynamics,
            deltaTime,
            clampedDirection,
            sailInput * sailingTrimSpeed / 0.48f,
            ballastInput,
            GetNaturalSailingWindSpeed(),
            GetNaturalCurrentSpeed(),
            ocean.SurfaceY,
            ocean.Slope,
            ocean.Agitation01,
            Mathf.Clamp01(boatHullIntegrity / 3f));

        float effectiveMaxSpeed = GetEffectiveSailingMaxSpeed();
        sailingDynamics.HorizontalVelocity = Mathf.Clamp(
            sailingDynamics.HorizontalVelocity,
            -effectiveMaxSpeed,
            effectiveMaxSpeed);
        sailingBoatVelocity = sailingDynamics.HorizontalVelocity;
        sailingSailTrim01 = sailingDynamics.SailRaised01;
        sailingWaterIngress01 = sailingDynamics.Ingress01;
        sailingBoatLaneY = sailingDynamics.HeaveY;
        float effectiveVelocity = sailingBoatVelocity;
        float boatXBeforeMove = sailingBoatX;
        if (Mathf.Abs(effectiveVelocity) > 0.02f)
        {
            if (Mathf.Abs(sailingBoatVelocity) > 0.05f)
            {
                playerFacing = sailingBoatVelocity > 0f ? 1 : -1;
            }

            float rightLimit = GetSailingRightLimit();
            float nextX = Mathf.Clamp(sailingBoatX + effectiveVelocity * deltaTime, sailingMinX, rightLimit);
            TideSailingReefSample reefSample = GetSailingReefSample(sailingBoatVelocity);
            TideSailingReefMovementResult reefMovement = sailingReef.ResolveMovement(
                sailingBoatX,
                nextX,
                sailingBoatVelocity,
                sailingReefPoint.x,
                reefSample);
            nextX = reefMovement.ResolvedBoatX;
            if (reefMovement.ContactedReef)
            {
                if (reefMovement.DamagesHull)
                {
                    boatHullIntegrity = Mathf.Max(0, boatHullIntegrity - 1);
                    RecalculateBoatReadiness();
                    sailingWaterIngress01 = Mathf.Clamp01(sailingWaterIngress01 + 0.28f);
                    sailingBoatVelocity *= -0.18f;
                    lastActionHint = "船底高速撞上露礁：一块旧补板被掀开，舱水猛增。下一次要等更深的潮窗，或先收帆减速。";
                }
                else
                {
                    sailingBoatVelocity = 0f;
                    lastActionHint = "船底轻轻坐上礁脊，船没有穿过岩石，也没有凭空受损；等水再涨一些才能越过去。";
                }
            }

            if (effectiveVelocity > 0f && nextX >= rightLimit - 0.02f)
            {
                // The nearest physical limit owns the feedback. A damaged boat reaches
                // its breaker before the distant story vortex; only after repairs extend
                // the seaworthy range should the vortex become the active barrier.
                if (IsBoatConditionLimitingRange())
                {
                    sailingRangeBlockedThisTrip = true;
                    lastActionHint = $"外海碎浪开始灌进船舷；当前船况 {boatReadiness}/{requiredBoatReadiness} 只能到这里，带东西回去补船。";
                }
                else if (IsVortexBlockingRoute())
                {
                    lastActionHint = "漩涡挡住远航，灯塔还在雾后；先在近处残骸找可带回的航线线索。";
                }

                sailingBoatVelocity = 0f;
            }

            if (nextX <= sailingMinX + 0.01f && effectiveVelocity < 0f)
            {
                sailingBoatVelocity = 0f;
            }

            sailingBoatX = nextX;
        }

        sailingBoatWorldVelocity = deltaTime > 0.0001f
            ? (sailingBoatX - boatXBeforeMove) / deltaTime
            : 0f;
    }

    private void ApplySailingBail(float deltaTime)
    {
        float waterBefore = sailingWaterIngress01;
        sailingWaterIngress01 = Mathf.MoveTowards(sailingWaterIngress01, 0f, GetEffectiveSailingBailRate() * deltaTime);
        sailingBailedWaterThisTrip += waterBefore - sailingWaterIngress01;
        sailingBailCycle += deltaTime * 3.2f;
        sailingBoatVelocity = Mathf.MoveTowards(sailingBoatVelocity, 0f, GetEffectiveSailingDrag() * deltaTime);
        sailingDynamics.Ingress01 = sailingWaterIngress01;
        sailingDynamics.HorizontalVelocity = sailingBoatVelocity;
    }

    private float GetEffectiveSailingBailRate()
    {
        // The prepared bucket is a better tool, not an automatic pump. The player
        // must still release the tiller and hold F, so faster drainage trades sailing
        // momentum and time for safety instead of erasing the leaking-hull problem.
        bool preparedBucket = HasActiveTidePrep() && selectedPrepChoice == TidePrepChoice.Bucket;
        return sailingBailRate * (preparedBucket ? 1.65f : 1f);
    }

    private bool CanInteractAtSailingPoint()
    {
        Vector2 boatPosition = GetSailingBoatBasePosition();
        if (EnableSailingBuoyGameplay && !sailingBuoyChecked &&
            Vector2.Distance(boatPosition, GetSailingPointPosition(SailingPointKind.Buoy)) <= 0.62f)
        {
            return true;
        }

        if (extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater &&
            Vector2.Distance(GetSailingBoatSternWorldPosition(), GetSailingPointPosition(SailingPointKind.Salvage)) <= sailingHookReach)
        {
            return true;
        }

        if (!CanSeeLighthouse() && !sailingWreckClueClaimed && !sailingClueCollected && Vector2.Distance(boatPosition, GetSailingPointPosition(SailingPointKind.Wreck)) <= 0.72f)
        {
            return true;
        }

        return CanSeeLighthouse() &&
            lighthouseClues < requiredLighthouseClues &&
            !sailingClueCollected &&
            Vector2.Distance(boatPosition, GetCurrentSailingTargetPosition()) <= 0.78f;
    }

    private void TryInteractAtSailingPoint()
    {
        Vector2 boatPosition = GetSailingBoatBasePosition();
        if (EnableSailingBuoyGameplay && !sailingBuoyChecked &&
            Vector2.Distance(boatPosition, GetSailingPointPosition(SailingPointKind.Buoy)) <= 0.62f)
        {
            sailingBuoyChecked = true;
            lastActionHint = "你读懂了旧浮标的背流方向，返航点不再只是远处一束光。";
            return;
        }

        if (extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater &&
            Vector2.Distance(GetSailingBoatSternWorldPosition(), GetSailingPointPosition(SailingPointKind.Salvage)) <= sailingHookReach)
        {
            BeginContinuousSailingHookThrow();
            return;
        }

        if (!CanSeeLighthouse() && !sailingWreckClueClaimed && !sailingClueCollected && Vector2.Distance(boatPosition, GetSailingPointPosition(SailingPointKind.Wreck)) <= 0.72f)
        {
            sailingClueCollected = true;
            sailingRewardPending = true;
            sailingClueWasLighthouse = false;
            lastActionHint = "你从残骸里取下一张被盐黏住的方位纸。现在要亲自把它带回高脚屋。";
            return;
        }

        if (CanSeeLighthouse() &&
            lighthouseClues < requiredLighthouseClues &&
            Vector2.Distance(boatPosition, GetCurrentSailingTargetPosition()) <= 0.78f)
        {
            lighthouseSeen = true;
            sailingClueCollected = true;
            sailingRewardPending = true;
            sailingClueWasLighthouse = true;
            lastActionHint = "雾裂开时你终于看见灯塔。方向已经确认，接下来只差把船修到能撑过暴潮。";
            return;
        }

        lastActionHint = "这里没有能带走或确认的实物。靠近漂物、残骸或灯塔后再试。";
    }

    private bool HasReturnedSailingCargoAtBoat()
    {
        return extraSaltWoodOwner == ExtraSaltWoodOwner.ReturnedAtBoat || returnedClueAtBoat;
    }

    private bool HasStagedSaltWoodAtMooring()
    {
        return extraSaltWoodOwner == ExtraSaltWoodOwner.StagedAtMooring;
    }

    private Vector2 GetMooringStagingPosition()
    {
        return new Vector2(
            GetBoatBoardingX() - 0.72f,
            GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.12f);
    }

    private bool IsPlayerNearStagedSaltWoodAtMooring()
    {
        return HasStagedSaltWoodAtMooring() &&
            playerLane == WalkLane.TideFlat &&
            Mathf.Abs(playerPosition.x - GetMooringStagingPosition().x) <= contextDistance * 0.78f;
    }

    private bool HandleMooringRopeInput(float deltaTime)
    {
        bool canInteract = viewMode == SliceViewMode.Shelter &&
            playerLane == WalkLane.TideFlat &&
            Mathf.Abs(playerPosition.x - GetBoatBoardingX()) <= boatBoardDistance * 1.35f;
        TideMooringRopeInteractionResult result = mooringRope.HandleInteraction(
            canInteract,
            Input.GetKeyDown(KeyCode.F),
            Input.GetKey(KeyCode.F),
            Input.GetKeyUp(KeyCode.F),
            deltaTime);

        if (result.Outcome == TideMooringRopeInteractionOutcome.SwingStarted)
        {
            lastActionHint = "你握住绳圈开始甩动；松手时绳头会按这一圈的长度飞出去。";
        }
        else if (result.Outcome == TideMooringRopeInteractionOutcome.ThrowAttached)
        {
            lastActionHint = "绳圈套住了船艉系柱。继续按住 F 收绳，浪大时别让张力一直绷满。";
        }
        else if (result.Outcome == TideMooringRopeInteractionOutcome.ThrowMissed)
        {
            lastActionHint = "绳头落进水里，没有够到船艉；等船和潮流改变距离后再甩。";
        }

        return result.Handled;
    }

    private bool TryHandleMooringInteraction()
    {
        if (!Input.GetKeyDown(KeyCode.F))
        {
            return false;
        }

        // The staged pile owns a separate point on the dock. It can be picked up
        // without turning the whole boat into a modal cargo screen.
        if (IsPlayerNearStagedSaltWoodAtMooring() && TryPickUpStagedSaltWoodAtMooring())
        {
            return true;
        }

        if (!IsPlayerNearBoat())
        {
            return false;
        }

        if (!TryUnloadReturnedSailingCargo())
        {
            TryBoardBoat();
        }
        return true;
    }

    private bool TryUnloadReturnedSailingCargo()
    {
        if (!HasReturnedSailingCargoAtBoat())
        {
            return false;
        }

        bool salvageAtBoat = extraSaltWoodOwner == ExtraSaltWoodOwner.ReturnedAtBoat;
        bool unloadedClue = returnedClueAtBoat;
        bool unloadedIntoHands = salvageAtBoat && AreHarvestHandsFree();
        bool stagedAtMooring = salvageAtBoat && !unloadedIntoHands;
        if (unloadedIntoHands)
        {
            BeginCarryingSaltWoodFromMooring(GetBoatBoardingX());
        }
        else if (stagedAtMooring)
        {
            // The player can physically free the boat even while carrying another
            // catch. The timber remains a visible world object and is not inventory.
            extraSaltWoodBundledWithNetHarvest = false;
            SetExtraSaltWoodOwner(ExtraSaltWoodOwner.StagedAtMooring);
        }

        if (unloadedClue)
        {
            shortSailCount++;
            metalStock += 1;
            clothStock += 1;
            if (returnedLighthouseConfirmationAtBoat)
            {
                lighthouseClues = requiredLighthouseClues;
            }
            else
            {
                lighthouseClues = Mathf.Min(requiredLighthouseClues, lighthouseClues + 1);
                sailingWreckClueClaimed = true;
                if (routeClueReturnRound < 0)
                {
                    routeClueReturnRound = tideRound;
                }
            }
            returnedClueAtBoat = false;
            returnedLighthouseConfirmationAtBoat = false;
        }

        string salvageText = unloadedIntoHands
            ? "你把盐木、缠索和旧油布从船艉解下，抱在身前；现在还没有进入库存。"
            : stagedAtMooring
                ? "你腾不开手，便把盐木从船艉卸到旁边的木板上。船已经空出来，材料仍在原处。"
                : string.Empty;
        string clueText = unloadedClue
            ? "你把盐湿方位纸压进干燥处，航线和可拆的布、铁现在才真正入账。"
            : string.Empty;
        lastActionHint = $"{salvageText}{clueText}";
        return true;
    }

    private bool TryPickUpStagedSaltWoodAtMooring()
    {
        if (!HasStagedSaltWoodAtMooring() || !AreHarvestHandsFree())
        {
            return false;
        }

        BeginCarryingSaltWoodFromMooring(GetMooringStagingPosition().x);
        lastActionHint = "你从码头木板上重新抱起盐木。它仍是手里的实物，只有存放或完成维修后才会入账。";
        return true;
    }

    private void BeginCarryingSaltWoodFromMooring(float sourceX)
    {
        currentHarvest = HarvestKind.Wood;
        currentHarvestBanked = false;
        netCatchBundleTier = 1;
        netCatchVisualPieceCount = 1;
        harvestPhysicalState = HarvestPhysicalState.Carried;
        harvestCarryStartPosition = new Vector2(
            sourceX,
            GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.18f);
        harvestCarryTransition01 = 0f;
        harvestPlacementTransition01 = 0f;
        harvestPlacedRepairChoice = RepairChoice.None;
        repairChoiceApplied = false;
        pendingRepairChoice = RepairChoice.None;
        state = SliceState.RepairMoment;
        extraSaltWoodBundledWithNetHarvest = false;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.Carried);
    }

    private void TryBoardBoat()
    {
        if (viewMode == SliceViewMode.Sailing)
        {
            lastActionHint = "你已经在海上了，靠左侧归航点按 F 返航。";
            return;
        }

        if (state == SliceState.FinalDeparture)
        {
            return;
        }

        if (HasReturnedSailingCargoAtBoat())
        {
            lastActionHint = "船舱里还有刚带回的实物。先在船边按 F 卸货，不能让它随切屏自动入库。";
            return;
        }

        if (HasUnsecuredHarvestForBoarding())
        {
            lastActionHint = "手上的潮获还没有归处。先在真实部位完成维修，或带进生活层储物架存下，再从船艉上船。";
            return;
        }

        if (!IsPlayerNearBoat())
        {
            lastActionHint = "先下到潮滩，用 A/D 走到小帆船边，再按 F。";
            return;
        }

        if (!IsBoatBoardWindowOpen())
        {
            lastActionHint = dayNightPhase == DayNightPhase.Night
                ? "夜里看不清浮标，不能再出船；回屋点灯、整理收获或休息到黎明。"
                : currentWaterY < lowWaterY + 0.38f
                    ? "船还搁在泥滩上，等潮水自己把它托起来。"
                    : "水流太急，短航窗口关了；等潮势转缓。";
            return;
        }

        if (IsDepartureReady())
        {
            BeginFinalDeparture();
            return;
        }

        BeginBoatViewTransition(BoatViewTransition.Boarding);
    }

    private void EnterSailingScene()
    {
        viewMode = SliceViewMode.Sailing;
        sailTripActive = true;
        sailTripTimer = 0f;
        sailingBoatX = sailingHomeX;
        sailingBoatLaneY = sailingHomeY;
        sailingBoatVelocity = 0f;
        sailingBoatWorldVelocity = 0f;
        sailingWaterIngress01 = 0f;
        sailingSailTrim01 = 0.58f;
        TideOceanSample launchOcean = GetSailingOceanSample(sailingBoatX);
        sailingDynamics = new TideSailboatDynamicsState
        {
            HeaveY = launchOcean.SurfaceY,
            SailRaised01 = sailingSailTrim01,
            Ingress01 = sailingWaterIngress01
        };
        sailingBallastInput = 0f;
        sailingBailCycle = 0f;
        sailingTrimActionTimer = 0f;
        sailingTrimCycle = 0f;
        sailingBailedWaterThisTrip = 0f;
        sailingBailing = false;
        sailingIngressMidWarned = false;
        sailingIngressHighWarned = false;
        sailingRangeBlockedThisTrip = false;
        sailingClueCollected = false;
        sailingRewardPending = false;
        sailingClueWasLighthouse = false;
        sailingBuoyChecked = !EnableSailingBuoyGameplay;
        SetExtraSaltWoodOwner(extraSaltWoodOwner);
        sailingSalvageHookProgress = 0f;
        sailingHookThrow01 = 0f;
        sailingHookThrowActive = false;
        sailingSalvageHauling = false;
        sailingSalvageTension01 = 0f;
        sailingSalvageOverstrainSeconds = 0f;
        sailingHookWorldPosition = GetSailingBoatSternWorldPosition();
        sailingSalvageInitialRopeLength = 0f;
        sailingReef?.ResetRuntime();
        playerFacing = 1;
        lastActionHint = CanSeeLighthouse()
            ? $"A/D 控方向，W/S 张帆或收帆；{GetBoatHandlingDescription()} 向右确认灯塔，舱里进水时离开调查点按住 F 舀水。"
            : $"A/D 控方向，W/S 张帆或收帆；{GetBoatHandlingDescription()} 先去残骸拿线索，舱里进水时离开调查点按住 F 舀水。";
    }

    private void ReturnToStiltHouseByChoice()
    {
        if (viewMode != SliceViewMode.Sailing)
        {
            return;
        }

        if (!CanReturnFromSailing())
        {
            lastActionHint = "还没靠近左侧归航点，不能返航；天黑/涨潮前自己把船开回去。";
            return;
        }

        BeginBoatViewTransition(BoatViewTransition.Returning);
    }

    private void BeginBoatViewTransition(BoatViewTransition transition)
    {
        if (transition == BoatViewTransition.None || boatViewTransition != BoatViewTransition.None)
        {
            return;
        }

        boatViewTransition = transition;
        boatViewTransitionTimer = 0f;
        boatViewTransitionSwitched = false;
        playerHorizontalVelocity = 0f;
        playerMoving = transition == BoatViewTransition.Boarding ||
            transition == BoatViewTransition.EnteringInterior ||
            transition == BoatViewTransition.ExitingInterior;
        sailingBailing = false;

        Vector2 boardwalkPoint = new Vector2(GetBoatBoardingX(), GetPlayerLaneY(WalkLane.TideFlat));
        Vector2 sternStepPoint = GetMooredBoatSternStepPosition();
        if (transition == BoatViewTransition.EnteringMooring ||
            transition == BoatViewTransition.ExitingMooring)
        {
            // This is a camera-sector hand-off, not a teleport. The same visible pier
            // point exists on both sides of the cut and remains the player's world point.
            boatViewTransitionStartPosition = playerPosition;
            boatViewTransitionEndPosition = playerPosition;
            playerFacing = transition == BoatViewTransition.EnteringMooring ? 1 : -1;
            lastActionHint = transition == BoatViewTransition.EnteringMooring
                ? "你沿最后一段湿木路走到系泊处。"
                : "你沿同一段木路离开系泊处，回到屋下。";
        }
        else if (transition == BoatViewTransition.Boarding)
        {
            mooringScreenActive = true;
            boatViewTransitionStartPosition = playerPosition;
            boatViewTransitionEndPosition = sternStepPoint;
            lastActionHint = "你踩稳湿木路，扶住船艉准备上船。";
        }
        else if (transition == BoatViewTransition.Returning)
        {
            boatViewTransitionStartPosition = sternStepPoint;
            boatViewTransitionEndPosition = boardwalkPoint;
            lastActionHint = "船靠住归航点。先扶稳船舷，再把人和货物带回木路。";
        }
        else if (transition == BoatViewTransition.EnteringInterior)
        {
            boatViewTransitionStartPosition = playerPosition;
            boatViewTransitionEndPosition = new Vector2(GetInteriorDoorX(), GetPlayerLaneY(WalkLane.InteriorUpper));
            lastActionHint = "你走到门槛，推开被盐吹涩的木门。";
        }
        else if (transition == BoatViewTransition.ExitingInterior)
        {
            boatViewTransitionStartPosition = playerPosition;
            boatViewTransitionEndPosition = new Vector2(GetInteriorDoorX() + 0.18f, GetPlayerLaneY(WalkLane.Deck));
            lastActionHint = "你从生活层走到门内，准备推门回外廊。";
        }
        else if (transition == BoatViewTransition.EnteringLookout)
        {
            boatViewTransitionStartPosition = playerPosition;
            boatViewTransitionEndPosition = playerPosition;
            lastActionHint = "你扶住盐涩的窗框，俯身把视线探到屋檐之外。";
        }
        else
        {
            boatViewTransitionStartPosition = playerPosition;
            boatViewTransitionEndPosition = playerPosition;
            lastActionHint = "你收回目光，扶着窗框退回阁楼。";
        }
    }

    private Vector2 GetMooredBoatV39AnchorPosition(Vector2Int anchorTopLeft, bool asStandingBodyCenter)
    {
        Vector2 mooredPosition = GetMooredBoatPosition() + GetBoatTripOffset();
        if (!HasCompleteV39BoatPresentation())
        {
            Vector2 fallback = GetMooredBoatSternStepPosition();
            return asStandingBodyCenter
                ? fallback
                : fallback - Vector2.up * (TideV20CharacterPresentationModel.BodyWorldLength * 0.5f);
        }

        TideOceanSample ocean = GetOceanSample(mooredPosition.x);
        Vector2 root = new Vector2(
            mooredPosition.x,
            TideV39BoatPresentationModel.EvaluateBoatRootY(ocean.SurfaceY));
        Vector2 anchor = TideV39BoatPresentationModel.EvaluateAnchorWorldPosition(
            root,
            anchorTopLeft,
            ocean.Slope * 4.5f,
            FormalBoatFacesRight);
        return asStandingBodyCenter
            ? anchor + Vector2.up * (TideV20CharacterPresentationModel.BodyWorldLength * 0.5f)
            : anchor;
    }

    private static Vector2 EvaluateDistanceWeightedRoute(
        Vector2 first,
        Vector2 second,
        Vector2 third,
        Vector2 fourth,
        float progress01)
    {
        // Each segment consumes time in proportion to its real distance. This prevents
        // a short stern step from taking as long as the whole gangplank and removes the
        // visible speed jumps produced by three independent Lerp calls.
        float firstLength = Vector2.Distance(first, second);
        float secondLength = Vector2.Distance(second, third);
        float thirdLength = Vector2.Distance(third, fourth);
        float totalLength = Mathf.Max(0.001f, firstLength + secondLength + thirdLength);
        float travel = Mathf.Clamp01(progress01) * totalLength;

        if (travel <= firstLength)
        {
            return Vector2.Lerp(first, second, Mathf.SmoothStep(0f, 1f, travel / Mathf.Max(0.001f, firstLength)));
        }

        travel -= firstLength;
        if (travel <= secondLength)
        {
            return Vector2.Lerp(second, third, Mathf.SmoothStep(0f, 1f, travel / Mathf.Max(0.001f, secondLength)));
        }

        travel -= secondLength;
        return Vector2.Lerp(third, fourth, Mathf.SmoothStep(0f, 1f, travel / Mathf.Max(0.001f, thirdLength)));
    }

    private Vector2 EvaluateMooredBoatBoardingRoute(Vector2 shorePosition, float progress01, bool returning)
    {
        Vector2 mooringBody = GetMooredBoatV39AnchorPosition(
            TideV39BoatPresentationModel.MooringPointTopLeft,
            true);
        Vector2 sternBody = GetMooredBoatSternStepPosition();
        Vector2 cockpitBody = GetMooredBoatV39AnchorPosition(
            TideV39BoatPresentationModel.CockpitEntryTopLeft,
            true);

        return returning
            ? EvaluateDistanceWeightedRoute(cockpitBody, sternBody, mooringBody, shorePosition, progress01)
            : EvaluateDistanceWeightedRoute(shorePosition, mooringBody, sternBody, cockpitBody, progress01);
    }

    private void TickBoatViewTransition(float deltaTime)
    {
        if (boatViewTransition == BoatViewTransition.None)
        {
            return;
        }

        float duration = Mathf.Max(0.2f, boatViewTransitionSeconds);
        float halfDuration = duration * 0.5f;
        boatViewTransitionTimer = Mathf.Min(duration, boatViewTransitionTimer + Mathf.Max(0f, deltaTime));

        // Nature keeps advancing during the transition. Until the midpoint hand-off,
        // keep every route anchor that belongs to the boat attached to the moving hull.
        // Once the return view has switched to shore, the start point stays frozen so
        // a player already standing on the pier is never dragged by the mooring line.
        if (!boatViewTransitionSwitched)
        {
            if (boatViewTransition == BoatViewTransition.Boarding)
            {
                boatViewTransitionEndPosition = GetMooredBoatSternStepPosition();
            }
            else if (boatViewTransition == BoatViewTransition.Returning)
            {
                boatViewTransitionStartPosition = GetMooredBoatSternStepPosition();
            }
        }

        if (!boatViewTransitionSwitched &&
            (boatViewTransition == BoatViewTransition.Boarding ||
             boatViewTransition == BoatViewTransition.EnteringInterior ||
             boatViewTransition == BoatViewTransition.ExitingInterior))
        {
            float walk01 = Mathf.Clamp01(boatViewTransitionTimer / halfDuration);
            Vector2 thresholdPosition = boatViewTransition == BoatViewTransition.Boarding
                ? EvaluateMooredBoatBoardingRoute(boatViewTransitionStartPosition, walk01, false)
                : boatViewTransition == BoatViewTransition.EnteringInterior
                    ? new Vector2(GetInteriorDoorX(), GetPlayerLaneY(WalkLane.Deck))
                    : new Vector2(GetInteriorDoorX(), GetPlayerLaneY(WalkLane.InteriorUpper));
            playerPosition = boatViewTransition == BoatViewTransition.Boarding
                ? thresholdPosition
                : Vector2.Lerp(boatViewTransitionStartPosition, thresholdPosition, Mathf.SmoothStep(0f, 1f, walk01));
            playerLane = boatViewTransition == BoatViewTransition.ExitingInterior
                ? WalkLane.InteriorUpper
                : boatViewTransition == BoatViewTransition.EnteringInterior ? WalkLane.Deck : WalkLane.TideFlat;
            playerFacing = 1;
            playerMoving = true;
            playerWalkCycle += deltaTime * 0.78f;
        }

        if (!boatViewTransitionSwitched && boatViewTransitionTimer >= halfDuration)
        {
            boatViewTransitionSwitched = true;
            if (boatViewTransition == BoatViewTransition.EnteringMooring)
            {
                mooringScreenActive = true;
            }
            else if (boatViewTransition == BoatViewTransition.ExitingMooring)
            {
                mooringScreenActive = false;
            }
            else if (boatViewTransition == BoatViewTransition.Boarding)
            {
                EnterSailingScene();
            }
            else
            {
                if (boatViewTransition == BoatViewTransition.Returning)
                {
                    ExitSailingScene();
                    playerPosition = boatViewTransitionStartPosition;
                }
                else if (boatViewTransition == BoatViewTransition.EnteringInterior)
                {
                    EnterStiltHouseInterior();
                }
                else if (boatViewTransition == BoatViewTransition.ExitingInterior)
                {
                    ExitStiltHouseInterior();
                }
                else if (boatViewTransition == BoatViewTransition.EnteringLookout)
                {
                    EnterLookoutVista();
                }
                else
                {
                    ExitLookoutVista();
                }
            }
        }

        if (boatViewTransition == BoatViewTransition.Returning && boatViewTransitionSwitched)
        {
            float walk01 = Mathf.Clamp01((boatViewTransitionTimer - halfDuration) / halfDuration);
            playerPosition = EvaluateMooredBoatBoardingRoute(boatViewTransitionEndPosition, walk01, true);
            playerLane = WalkLane.TideFlat;
            playerFacing = -1;
            playerMoving = true;
            playerWalkCycle += deltaTime * 0.78f;
        }

        if (boatViewTransition == BoatViewTransition.ExitingInterior && boatViewTransitionSwitched)
        {
            float walk01 = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((boatViewTransitionTimer - halfDuration) / halfDuration));
            Vector2 outsideThreshold = new Vector2(GetInteriorDoorX(), GetPlayerLaneY(WalkLane.Deck));
            playerPosition = Vector2.Lerp(outsideThreshold, boatViewTransitionEndPosition, walk01);
            playerLane = WalkLane.Deck;
            playerFacing = 1;
            playerMoving = true;
            playerWalkCycle += deltaTime * 0.78f;
        }

        if (boatViewTransitionTimer < duration)
        {
            return;
        }

        if (boatViewTransition == BoatViewTransition.Returning || boatViewTransition == BoatViewTransition.ExitingInterior)
        {
            playerPosition = boatViewTransitionEndPosition;
        }
        playerMoving = false;
        playerWalkCycle = 0f;
        boatViewTransition = BoatViewTransition.None;
        boatViewTransitionTimer = 0f;
        boatViewTransitionSwitched = false;
    }

    private float GetBoatViewTransitionFade01()
    {
        if (boatViewTransition == BoatViewTransition.None)
        {
            return 0f;
        }

        float progress01 = Mathf.Clamp01(boatViewTransitionTimer / Mathf.Max(0.2f, boatViewTransitionSeconds));
        // Keep most of the physical movement readable. The screen only closes around
        // the view hand-off, where the missing stand-to-seated animation is concealed.
        float distanceFromCut01 = Mathf.Abs(progress01 - 0.5f) / 0.22f;
        return Mathf.SmoothStep(0f, 1f, 1f - Mathf.Clamp01(distanceFromCut01));
    }

    private float GetSurvivalPresentationFade01()
    {
        if (survivalPresentationState == SurvivalPresentationState.None)
        {
            return 0f;
        }

        // 人物动作的大部分保持可见；只在最后一帧后半段闭合画面，
        // 让换日或复活发生在暗点，而不是用黑屏掩盖整段动作。
        float progress01 = GetSurvivalPresentationProgress01();
        float fadeStart = survivalPresentationState == SurvivalPresentationState.Sleeping
            ? 0.68f
            : 0.74f;
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeStart, 1f, progress01));
    }

    private bool CanReturnFromSailing()
    {
        return sailingBoatX <= sailingHomeX + 0.72f &&
            Mathf.Abs(sailingBoatLaneY - sailingHomeY) <= 0.42f;
    }

    // 返航规则：涨潮/天黑只加压力，不自动回屋；玩家自己选择回高脚屋。
    private float GetReturnPressure01()
    {
        if (viewMode != SliceViewMode.Sailing)
        {
            return 0f;
        }

        float nightPressure = dayNightPhase == DayNightPhase.Night
            ? 1f
            : dayNightPhase == DayNightPhase.Dusk
                ? Mathf.InverseLerp(0.62f, 0.78f, dayProgress01) * 0.78f
                : 0f;
        float tidePressure = state == SliceState.TideRising
            ? Mathf.InverseLerp(lowWaterY + 0.76f, lowWaterY + 1.55f, currentWaterY)
            : 0f;
        float lingerPressure = Mathf.InverseLerp(sailTripSeconds * 0.85f, sailTripSeconds * 1.55f, sailTripTimer) * 0.46f;
        float ingressPressure = sailingWaterIngress01 * 0.86f;
        float stormPressure = GetStormPressure01() * 0.9f;
        float adverseCurrentPressure = GetAdverseReturnCurrentPressure01();
        float buoyRelief = sailingBuoyChecked ? 0.12f : 0f;
        float environmentalPressure = Mathf.Max(
            Mathf.Max(nightPressure, tidePressure),
            Mathf.Max(lingerPressure, Mathf.Max(ingressPressure, Mathf.Max(stormPressure, adverseCurrentPressure))));
        return Mathf.Clamp01(environmentalPressure - buoyRelief);
    }

    private float GetAdverseReturnCurrentPressure01()
    {
        // 正 X 是离岸方向。只有真实离岸流增加返航风险；向岸流仍会在船的
        // 实际速度中帮助返航，但这里不把它做成负压力或自动安全奖励。
        float offshoreSpeed = Mathf.Max(0f, GetNaturalCurrentSpeed());
        if (offshoreSpeed <= 0.001f)
        {
            return 0f;
        }

        float referenceStrength = CalculateTideStrength(OpeningMoonAgeDays);
        float physicalCurrent01 = TideAstronomicalCurrentModel.EvaluateGlobalCurrentStrength01(
            offshoreSpeed,
            referenceStrength,
            MeanTidalTransportSpeed,
            EbbCurrentBoost);
        float distance01 = Mathf.InverseLerp(
            sailingHomeX + 0.72f,
            sailingMaxX,
            sailingBoatX);
        // 修船降低被流推走的程度，但一艘好船仍在海里，不会把潮流抵消为零。
        float handlingExposure01 = Mathf.Clamp01(GetSailingCurrentInfluence() / 1.08f);
        return physicalCurrent01 *
            Mathf.Lerp(0.28f, 0.94f, distance01) *
            Mathf.Lerp(0.58f, 1f, handlingExposure01);
    }

    private string GetReturnPressureText()
    {
        if (sailingWaterIngress01 >= 0.65f)
        {
            return "舱水很高，尽快返航";
        }

        if (sailingWaterIngress01 >= 0.35f)
        {
            return "舱水上升，操控变慢";
        }

        float pressure = GetReturnPressure01();
        if (pressure < 0.18f)
        {
            return "返航压力低";
        }

        if (pressure < 0.62f)
        {
            return "返航压力升高中";
        }

        return "天黑/涨潮，建议自己返航";
    }

    private void TryDoNearshoreWork()
    {
        // Keep this legacy entry harmless for old probes or serialized callbacks.
        // Production input uses continuous W/S control and never changes ownership here.
        lastActionHint = "引流索不再一键切换：站在绞盘旁按住 W 收索进网，按住 S 放索入海。";
    }

    private bool HandleContinuousTideRoutingInput(float deltaTime)
    {
        float direction = 0f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            direction += 1f;
        }
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
        {
            direction -= 1f;
        }

        bool nearWinch = IsPlayerNearShoreWork();
        bool hasDirection = Mathf.Abs(direction) > 0.01f;
        bool changed = TickContinuousTideRoutingControl(deltaTime, direction);
        if (changed)
        {
            lastActionHint = direction > 0f
                ? "你正逆着水势收索，浮木导杆逐渐转向网口。松手会停在当前角度。"
                : "你正放松导索，让浮木顺潮从网外进入短航漂浮带。";
        }
        else if (nearWinch && hasDirection && routingDecisionLocked)
        {
            lastActionHint = "盐木已经越过岔流绳，这一潮的去向不能再隔空改变。";
        }

        return nearWinch && hasDirection;
    }

    private bool TickContinuousTideRoutingControl(float deltaTime, float direction)
    {
        routingWorkActive = false;
        routingWorkDirection = 0f;
        if (Mathf.Abs(direction) <= 0.01f)
        {
            return false;
        }

        bool canOperate = viewMode == SliceViewMode.Shelter &&
            state == SliceState.TideRising &&
            netDeployed &&
            netIntegrity > 0 &&
            !routingDecisionLocked &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.PassingNearshore &&
            IsNearshoreWorkWindowOpen() &&
            IsPlayerNearShoreWork();
        if (!canOperate)
        {
            return false;
        }

        float waterLoad01 = Mathf.InverseLerp(
            lowWaterY + 0.18f,
            lowWaterY + shoreWorkMaxWaterOffset,
            currentWaterY);
        float previousBoom01 = routingBoom01;
        routingBoom01 = TideContinuousRoutingModel.AdvanceBoom01(
            routingBoom01,
            direction,
            deltaTime,
            GetNaturalCurrentStrength01(),
            GetStormPressure01(),
            waterLoad01);
        bool changed = Mathf.Abs(routingBoom01 - previousBoom01) > 0.0001f;
        if (changed)
        {
            routingWorkActive = true;
            routingWorkDirection = Mathf.Sign(direction);
        }

        return changed;
    }

    private void TryLockContinuousRoutingDecision(float previousTravel01, float currentTravel01)
    {
        if (!TideContinuousRoutingModel.ShouldLockDecision(routingDecisionLocked, currentTravel01) ||
            extraSaltWoodOwner != ExtraSaltWoodOwner.PassingNearshore)
        {
            return;
        }

        routingDecisionLocked = true;
        nearshoreWorkDone = true;
        routingWorkActive = false;
        routingWorkDirection = 0f;
        bool feedsNet = TideContinuousRoutingModel.RoutesToNet(
            routingBoom01,
            netDeployed && netIntegrity > 0);
        tideRoutingMode = feedsNet ? TideRoutingMode.FeedNet : TideRoutingMode.Open;
        if (feedsNet)
        {
            extraSaltWoodBundledWithNetHarvest = true;
            SetExtraSaltWoodOwner(ExtraSaltWoodOwner.RoutedToNet);
            lastActionHint = "盐木撞上收紧的导杆，正沿绳压向网口；这一潮的网会多吃一档真实负载。";
        }
        else
        {
            extraSaltWoodBundledWithNetHarvest = false;
            lastActionHint = "盐木已经从放开的导杆外侧越过；它会进入短航漂浮带，网只承担普通潮获。";
        }
    }

    private bool CanSeeLighthouse()
    {
        return GetLighthouseVisibilitySample().State == TideLighthouseVisibilityState.Known;
    }

    private TideLighthouseVisibilitySample GetLighthouseVisibilitySample()
    {
        return TideLighthouseVisibilityModel.Evaluate(
            lighthouseClues,
            routeClueReturnRound,
            tideRound,
            state == SliceState.FinalDeparture,
            GetDaylight01(),
            GetStormPressure01());
    }

    private void ApplyLighthouseFogToBroadWeatherBands(
        TideLighthouseVisibilitySample visibility)
    {
        // 只抬高已经由 V43 创建的宽幅云雾 Alpha；绝不在灯塔锚点创建局部雾团。
        // V43 每帧先恢复自身天气颜色，因此这里不会跨帧累积变白。
        ApplyLighthouseFogToRendererList(horizonCloudBanks, visibility.FogAlpha, 0.42f);
        ApplyLighthouseFogToRendererList(cloudRenderers, visibility.FogAlpha, 0.24f);
    }

    private static void ApplyLighthouseFogToRendererList(
        List<SpriteRenderer> renderers,
        float fogAlpha,
        float layerWeight)
    {
        float minimumAlpha = Mathf.Clamp01(fogAlpha) * Mathf.Clamp01(layerWeight);
        for (int i = 0; i < renderers.Count; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            Color color = renderer.color;
            color.a = Mathf.Max(color.a, minimumAlpha);
            renderer.color = color;
        }
    }

    private bool IsVortexBlockingRoute()
    {
        return viewMode == SliceViewMode.Sailing && !CanSeeLighthouse();
    }

    private float GetSailingRightLimit()
    {
        return Mathf.Min(GetRouteSailingRightLimit(), GetBoatSeaworthyRightLimit());
    }

    private float GetRouteSailingRightLimit()
    {
        return IsVortexBlockingRoute() ? routeVortexX - 0.42f : sailingMaxX;
    }

    private float GetBoatReadiness01()
    {
        return Mathf.Clamp01(boatReadiness / Mathf.Max(1f, requiredBoatReadiness));
    }

    private float GetBoatSeaworthyRightLimit()
    {
        float repaired01 = Mathf.SmoothStep(0f, 1f, GetBoatReadiness01());
        // Even the damaged boat can reach the near drift line. Each repair opens
        // roughly another sea sector instead of changing an invisible one-screen wall.
        return Mathf.Lerp(6.65f, sailingMaxX, repaired01);
    }

    private bool IsBoatConditionLimitingRange()
    {
        return GetBoatSeaworthyRightLimit() < GetRouteSailingRightLimit() - 0.02f;
    }

    private float GetEffectiveSailingMaxSpeed()
    {
        float repairedSpeed = Mathf.Lerp(sailingMaxSpeed * 0.58f, sailingMaxSpeed * 1.08f, GetBoatReadiness01());
        float trimSpeed = Mathf.Lerp(0.56f, 1.14f, Mathf.SmoothStep(0f, 1f, sailingSailTrim01));
        float bailingPenalty = sailingBailing ? 0.38f : 1f;
        float towLoad01 = GetCurrentSailingTowLoad01();
        float salvageWeight = Mathf.Lerp(1f, 0.82f, towLoad01);
        return repairedSpeed * trimSpeed * Mathf.Lerp(1f, 0.62f, sailingWaterIngress01) * bailingPenalty * salvageWeight;
    }

    private float GetEffectiveSailingAcceleration()
    {
        float repairedAcceleration = Mathf.Lerp(sailingAcceleration * 0.55f, sailingAcceleration * 1.1f, GetBoatReadiness01());
        float trimAcceleration = Mathf.Lerp(0.68f, 1.08f, sailingSailTrim01);
        float bailingPenalty = sailingBailing ? 0.42f : 1f;
        float towAcceleration = Mathf.Lerp(1f, 0.76f, GetCurrentSailingTowLoad01());
        return repairedAcceleration * trimAcceleration * Mathf.Lerp(1f, 0.68f, sailingWaterIngress01) * bailingPenalty * towAcceleration;
    }

    private float GetEffectiveSailingDrag()
    {
        float repairedDrag = Mathf.Lerp(sailingDrag * 0.62f, sailingDrag * 1.18f, GetBoatReadiness01());
        float reefingBrake = Mathf.Lerp(1.34f, 0.9f, sailingSailTrim01);
        float bailingBrake = sailingBailing ? 1.55f : 1f;
        float towDrag = Mathf.Lerp(1f, 1.24f, GetCurrentSailingTowLoad01());
        return repairedDrag * reefingBrake * Mathf.Lerp(1f, 0.74f, sailingWaterIngress01) * bailingBrake * towDrag;
    }

    private float GetSailingCurrentInfluence()
    {
        float repairedInfluence = Mathf.Lerp(0.9f, 0.48f, GetBoatReadiness01());
        float trimControl = Mathf.Lerp(1.18f, 0.84f, sailingSailTrim01);
        return repairedInfluence * trimControl * Mathf.Lerp(1f, 1.24f, sailingWaterIngress01);
    }

    private string GetSailTrimText()
    {
        if (sailingSailTrim01 < 0.3f)
        {
            return "收帆";
        }

        if (sailingSailTrim01 < 0.72f)
        {
            return "半帆";
        }

        return "满帆";
    }

    private string GetBoatHandlingDescription()
    {
        if (boatReadiness <= 0)
        {
            return "破船起速慢、吃潮流，久航会积水。";
        }

        if (boatReadiness < requiredBoatReadiness)
        {
            return $"船况 {boatReadiness}/{requiredBoatReadiness}，补片让它更稳，但远海仍有边界。";
        }

        return "船况已稳，能压住潮流并进入外海。";
    }

    private float GetCurrentSailingTargetX()
    {
        return CanSeeLighthouse() ? sailingTargetX : earlyWreckClueX;
    }

    private Vector2 GetCurrentSailingTargetPosition()
    {
        return CanSeeLighthouse()
            ? new Vector2(sailingTargetX, -0.66f)
            : GetSailingPointPosition(SailingPointKind.Wreck);
    }

    private Vector2 GetSailingBoatBasePosition()
    {
        return new Vector2(sailingBoatX, sailingBoatLaneY);
    }

    private float GetSailingCameraWorldX()
    {
        // Near the house the camera stays fixed so the departure point remains
        // readable. Beyond the buoy it follows continuously through several screen
        // widths; returning reverses the same trip instead of teleporting home.
        return Mathf.Clamp(sailingBoatX - 0.35f, 0f, sailingMaxX - 0.35f);
    }

    private Vector2 GetSailingScreenPosition(Vector2 worldPosition)
    {
        return new Vector2(worldPosition.x - GetSailingCameraWorldX(), worldPosition.y);
    }

    private Vector2 GetSailingPointPosition(SailingPointKind point)
    {
        if (point == SailingPointKind.Buoy)
        {
            TideOceanSample buoyOcean = GetSailingOceanSample(sailingBuoyPoint.x);
            return new Vector2(sailingBuoyPoint.x, buoyOcean.SurfaceY + 0.08f);
        }

        if (point == SailingPointKind.Salvage)
        {
            TideOceanSample salvageOcean = GetSailingOceanSample(sailingSalvageWorldX);
            return new Vector2(sailingSalvageWorldX, salvageOcean.SurfaceY - 0.01f);
        }

        if (point == SailingPointKind.Reef)
        {
            return sailingReefPoint;
        }

        // Wreckage and reef are grounded; water moves around them instead of lifting
        // the entire silhouette with every wave.
        return new Vector2(earlyWreckClueX, -0.66f);
    }

    private void ExitSailingScene()
    {
        float returnedIngress = sailingWaterIngress01;
        float bailedWater = sailingBailedWaterThisTrip;
        viewMode = SliceViewMode.Shelter;
        mooringScreenActive = true;
        sailTripActive = false;
        sailTripTimer = 0f;
        sailingBoatVelocity = 0f;
        sailingBoatWorldVelocity = 0f;
        sailingWaterIngress01 = 0f;
        sailingBailing = false;
        sailingRangeBlockedThisTrip = false;
        playerLane = WalkLane.TideFlat;
        float returnX = GetBoatBoardingX();
        playerPosition = new Vector2(returnX, GetPlayerLaneY(playerLane));
        playerFacing = -1;
        playerHorizontalVelocity = 0f;

        bool broughtHomeClue = sailingRewardPending;
        bool incompleteTow = extraSaltWoodOwner == ExtraSaltWoodOwner.HookingToBoat;
        if (incompleteTow)
        {
            ReleaseSailingSalvageTow();
        }
        bool broughtHomeSalvage = extraSaltWoodOwner == ExtraSaltWoodOwner.HookedToBoat;
        returnedClueAtBoat |= broughtHomeClue;
        returnedLighthouseConfirmationAtBoat |= broughtHomeClue && sailingClueWasLighthouse;
        sailingRewardPending = false;
        if (broughtHomeSalvage)
        {
            SetExtraSaltWoodOwner(ExtraSaltWoodOwner.ReturnedAtBoat);
        }

        string waterNote = returnedIngress >= 0.5f
            ? " 舱里已经积了很多水，补船会明显减少下一次进水。"
            : returnedIngress >= 0.2f
                ? " 舱里带回一些水，船况还经不起长航。"
                : string.Empty;
        if (bailedWater >= 0.08f)
        {
            waterNote += $" 途中舀掉了 {Mathf.RoundToInt(bailedWater * 100f)}% 的舱水。";
        }
        string salvageNote = broughtHomeSalvage
            ? " 浮木和油布仍绑在系泊船艉，尚未进入库存。"
            : incompleteTow
                ? " 钩绳尚未收短，浮木留在你返航前的海上位置。"
            : string.Empty;
        string clueNote = broughtHomeClue
            ? " 方位纸压在船舱里，走到船边按 F 卸下后才会记入航线。"
            : string.Empty;
        lastActionHint = HasReturnedSailingCargoAtBoat()
            ? $"你自己把船开回来了。{salvageNote}{clueNote}{waterNote}"
            : $"你没有带回新实物，但安全返航了。{waterNote}";
    }

    private void BeginFinalDeparture()
    {
        state = SliceState.FinalDeparture;
        viewMode = SliceViewMode.Shelter;
        stateTimer = 0f;
        sailTripActive = false;
        sailTripTimer = 0f;
        sailingBoatVelocity = 0f;
        sailingBoatWorldVelocity = 0f;
        playerFacing = 1;
        finalDepartureStartWaterY = currentWaterY;
        lastActionHint = "暴潮来了：屋子撑到最后，船带着灯塔线索离开。";
    }

    private void CommitNetIntoNaturalTide(NetLine line)
    {
        EnsureCurrentTideDriftField();
        if (extraSaltWoodOwner == ExtraSaltWoodOwner.RoutedToNet && !routingDecisionLocked)
        {
            // RoutedToNet without a locked physical fork can only be a stale preview,
            // interrupted legacy state or test setup. Re-hanging the net must not keep
            // an owner that no longer has a corresponding rope/boom decision.
            ReleaseUncaughtRoutedSaltWood();
        }
        bool preserveLockedSaltWoodRoute = extraSaltWoodOwner == ExtraSaltWoodOwner.RoutedToNet &&
            routingDecisionLocked;
        selectedNetLine = line;
        netDeployed = true;
        netRigStep = NetRigStep.Deployed;
        netUnrollProgress = 1f;
        if (lampForecastCharges > 0)
        {
            lampForecastCharges--;
            chartForecastSnapshot = default;
        }
        currentHarvest = HarvestKind.None;
        currentHarvestBatchId = 0;
        currentHarvestBanked = false;
        harvestPhysicalState = primarySourcePassedNearshore
            ? HarvestPhysicalState.Lost
            : state == SliceState.TideRising
                ? HarvestPhysicalState.Drifting
                : HarvestPhysicalState.None;
        washedAwayHarvestKind = HarvestKind.None;
        washedAwayHarvestBatchId = 0;
        washedAwayHarvestPieceCount = 0;
        washedAwayHarvestTimer = 0f;
        washedAwayHarvestDriftX = 0f;
        addedHarvestPieceTravel01 = 0f;
        netCatchVisualPieceCount = 0;
        netTouched = false;
        netCatchResolved = false;
        netWaterExposureSeconds = 0f;
        netCaptureProgress01 = 0f;
        previousIncomingHarvestTravel01 = incomingHarvestTravel01;
        previousOuterWreckTravel01 = outerWreckTravel01;
        outerNetCaptureProgress01 = 0f;
        netAccumulatedTension = 0f;
        netPeakTension01 = 0f;
        netFraying01 = 0f;
        netDepthAdjustmentActive = false;
        netDepthAdjustmentDirection = 0f;
        netPostCatchExposureSeconds = 0f;
        netCatchBundleTier = 1;
        netOverloadStressApplied = 0;
        netSecuredEarly = false;
        currentTideNetStress = 0;
        netBrokeThisTide = false;
        if (preserveLockedSaltWoodRoute)
        {
            nearshoreWorkDone = true;
            tideRoutingMode = TideRoutingMode.FeedNet;
            routingWorkActive = false;
            routingWorkDirection = 0f;
        }
        else
        {
            nearshoreWorkDone = false;
            tideRoutingMode = TideRoutingMode.Open;
            routingBoom01 = 0f;
            routingWorkActive = false;
            routingWorkDirection = 0f;
            routingDecisionLocked = false;
        }
        tideStrength = CalculateTideStrength(moonAgeDays);
        tideSourceHarvest = ToHarvestKind(currentTideDriftField.NearshoreBatch.Material);
        if (state == SliceState.TideRising)
        {
            StartCurrentTideDrift();
        }
        ApplyPrepBeforeTide();
        lastActionHint = $"网已经系在 {Mathf.RoundToInt(netSetDepth01 * 100f)}% 深的位置。深度只改变覆盖水层、负载和断网风险；{GetTideDriftProvenanceName(currentTideDriftField.NearshoreBatch.Provenance)}里的{GetHarvestName(tideSourceHarvest)}仍按原水路漂来。";
    }

    private void TickHarvestPhysicalLifecycle(float deltaTime)
    {
        // The encounter model consumes the actual segment travelled this frame. Keep the
        // previous position even when the current is slack, so low frame rates and ebb
        // reversals cannot skip or duplicate the visible net opening.
        previousIncomingHarvestTravel01 = incomingHarvestTravel01;
        previousOuterWreckTravel01 = outerWreckTravel01;

        if (washedAwayHarvestTimer > 0f)
        {
            // Once the net breaks, displaced objects integrate the same current used
            // by swimmers, boats and the net. Their position therefore eases through
            // slack water instead of being recomputed from a direction boolean and
            // jumping to the opposite side at high or low tide.
            float activeDeltaTime = Mathf.Min(deltaTime, washedAwayHarvestTimer);
            TideOceanSample debrisOcean = GetOceanSample(netAnchor.x + washedAwayHarvestDriftX);
            float debrisFlow = GetNaturalCurrentSpeed() * 1.55f +
                debrisOcean.HorizontalVelocity * Mathf.Lerp(0.55f, 1.05f, debrisOcean.Agitation01);
            washedAwayHarvestDriftX += debrisFlow * activeDeltaTime;
        }
        washedAwayHarvestTimer = Mathf.Max(0f, washedAwayHarvestTimer - deltaTime);

        if (state == SliceState.TideRising && currentWaterY > lowWaterY + 0.12f)
        {
            EnsureCurrentTideDriftField();
            float waterGate01 = Mathf.InverseLerp(lowWaterY + 0.12f, lowWaterY + 1.18f, currentWaterY);
            float towardNetSpeedMetersPerSecond = -GetNaturalCurrentSpeed();
            float referenceFloodSpeedMetersPerSecond = GetReferenceFloodCurrentSpeed();
            float storm01 = GetStormPressure01();

            if (harvestPhysicalState == HarvestPhysicalState.Drifting &&
                !primarySourcePassedNearshore &&
                tideSourceHarvest != HarvestKind.None)
            {
                incomingHarvestTravel01 = TideDriftSourceModel.AdvanceNearshoreTravel01(
                    incomingHarvestTravel01,
                    deltaTime,
                    towardNetSpeedMetersPerSecond,
                    referenceFloodSpeedMetersPerSecond,
                    waterGate01,
                    currentTideDriftField.NearshoreBatch,
                    CalculateTideStrength(OpeningMoonAgeDays),
                    storm01);
            }

            if (!outerWreckPassedNearshore &&
                (extraSaltWoodOwner == ExtraSaltWoodOwner.PassingNearshore ||
                 extraSaltWoodOwner == ExtraSaltWoodOwner.RoutedToNet))
            {
                float previousOuterTravel01 = outerWreckTravel01;
                outerWreckTravel01 = TideDriftSourceModel.AdvanceNearshoreTravel01(
                    outerWreckTravel01,
                    deltaTime,
                    towardNetSpeedMetersPerSecond,
                    referenceFloodSpeedMetersPerSecond,
                    waterGate01,
                    currentTideDriftField.OuterWreckBatch,
                    CalculateTideStrength(OpeningMoonAgeDays),
                    storm01);
                TryLockContinuousRoutingDecision(previousOuterTravel01, outerWreckTravel01);
                TryCatchRoutedSaltWoodInResolvedNet(deltaTime);
            }
        }

        if (harvestPhysicalState == HarvestPhysicalState.CaughtInNet &&
            netDeployed &&
            netCatchVisualPieceCount < netCatchBundleTier)
        {
            // Each later load tier is another physical object arriving from the
            // current, rather than a number changing and an item popping into the net.
            float relativePhysicalCurrent = TideDriftSourceModel.EvaluateRelativePhysicalCurrent(
                -GetNaturalCurrentSpeed(),
                GetReferenceFloodCurrentSpeed());
            addedHarvestPieceTravel01 = Mathf.Clamp01(
                addedHarvestPieceTravel01 + deltaTime * relativePhysicalCurrent / 2.1f);
            if (addedHarvestPieceTravel01 >= 0.999f)
            {
                netCatchVisualPieceCount++;
                addedHarvestPieceTravel01 = 0f;
                TideAudioController.PlayNetLoadCueInScene(netCatchVisualPieceCount, false);
                lastActionHint = netCatchVisualPieceCount >= 3
                    ? $"最后一件{GetHarvestName()}真正压进网眼，实物负载已满 3/3；继续留网只会增加断裂风险。"
                    : $"又一件{GetHarvestName()}顺着水路挂稳，网里现在有 {netCatchVisualPieceCount}/3 件实物。";
            }
        }
        else if (netCatchVisualPieceCount >= netCatchBundleTier)
        {
            addedHarvestPieceTravel01 = 0f;
        }

        if (harvestPhysicalState == HarvestPhysicalState.PlacedAtWork)
        {
            harvestPlacementTransition01 = Mathf.MoveTowards(
                harvestPlacementTransition01,
                1f,
                deltaTime / 0.42f);
        }
    }

    private void ApplyPrepBeforeTide()
    {
        if (HasActiveTidePrep() && selectedPrepChoice == TidePrepChoice.Stake)
        {
            nearshoreWorkDone = true;
        }
    }

    private void TickState(float deltaTime)
    {
        TickStateInternal(deltaTime, false, false, false, false);
    }

    private void TickStateWithSailingInteraction(float deltaTime, bool interactionHeld)
    {
        // This entry is only for deterministic editor probes. It advances the whole
        // world and changes only the held state that ordinary play reads from F.
        TickStateInternal(deltaTime, true, interactionHeld, false, false);
    }

    private void TickStateWithNetHaulInteraction(float deltaTime, bool interactionHeld)
    {
        // Runtime still reads the player's F key. The deterministic work probe only
        // supplies the same held state so the interruption model does not treat a
        // continuously pulled net as abandoned between simulated frames.
        TickStateInternal(deltaTime, false, false, true, interactionHeld);
    }

    private void TickStateInternal(
        float deltaTime,
        bool overrideSailingInteraction,
        bool sailingInteractionHeld,
        bool overrideNetHaulInteraction,
        bool netHaulInteractionHeld)
    {
        worldElapsedRealSeconds += Mathf.Max(0f, deltaTime);
        stateTimer += deltaTime;
        if (state == SliceState.RepairMoment && currentHarvest != HarvestKind.None && !repairChoiceApplied)
        {
            harvestCarryTransition01 = Mathf.MoveTowards(harvestCarryTransition01, 1f, deltaTime / 0.58f);
        }
        TickHarvestPhysicalLifecycle(deltaTime);
        tidePrepActionTimer = Mathf.Max(0f, tidePrepActionTimer - deltaTime);
        shelterImpactTimer = Mathf.Max(0f, shelterImpactTimer - deltaTime);
        TickInterruptedNetHaul(
            deltaTime,
            overrideNetHaulInteraction && netHaulInteractionHeld);
        AdvanceDayNight(deltaTime);
        AdvanceContinuousWeather(deltaTime);
        TickNaturalTide(deltaTime);
        TickBarrenIslandNaturalState(deltaTime);
        TickShelterTideStress();
        TickStormRescue(deltaTime);
        TickPlayerBuoyancyAndCurrent(deltaTime);
        if (TickSurvival(deltaTime))
        {
            return;
        }
        TickMooredBoatCurrent(deltaTime);
        if (overrideSailingInteraction)
        {
            TickSailTrip(deltaTime, sailingInteractionHeld);
        }
        else
        {
            TickSailTrip(deltaTime);
        }

        if (state == SliceState.FinalDeparture)
        {
            float depart01 = Mathf.Clamp01(stateTimer / Mathf.Max(0.1f, finalDepartureSeconds));
            float stormWaterTargetY = Mathf.Max(finalDepartureStartWaterY, GetPredictedHighWaterY());
            currentWaterY = Mathf.Lerp(finalDepartureStartWaterY, stormWaterTargetY, Mathf.SmoothStep(0f, 1f, depart01));
            return;
        }

        if (state == SliceState.RepairMoment && stateTimer > repairSeconds)
        {
            stateTimer = repairSeconds;
        }
    }

    private void TickInterruptedNetHaul(float deltaTime, bool injectedHaulHeld = false)
    {
        if (netHaulProgress <= 0f || injectedHaulHeld || IsActivelyHaulingNet())
        {
            return;
        }

        // A half-finished pull slips back through wet hands instead of remaining frozen
        // while the player walks away. The visual net follows the same eased progress.
        netHaulProgress = Mathf.Max(
            0f,
            netHaulProgress - deltaTime / Mathf.Max(0.1f, netHaulSeconds * 0.72f));
        netHaulEffort01 = Mathf.MoveTowards(netHaulEffort01, 0f, deltaTime * 4f);
        netHaulLoad01 = Mathf.MoveTowards(netHaulLoad01, 0f, deltaTime * 3f);
        if (netHaulProgress <= 0f)
        {
            netHaulStrokePhase = 0f;
            netHaulEffort01 = 0f;
            netHaulLoad01 = 0f;
        }
    }

    private void TickNaturalTide(float deltaTime)
    {
        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        float tideClockBefore = tideClockSeconds;
        // TickState advances weather immediately before tide. Reconstruct the interval
        // start so every water sample receives the pressure that existed at that time,
        // rather than repainting the whole completed tide with the newest storm value.
        float weatherClockBefore = Mathf.Max(0f, weatherClockSeconds - safeDeltaTime);
        AdvanceHighWaterMemory(tideClockBefore, weatherClockBefore, safeDeltaTime);

        tideClockSeconds = Mathf.Repeat(tideClockBefore + safeDeltaTime, Mathf.Max(8f, tideCycleSeconds));
        float phase01 = tideClockSeconds / Mathf.Max(8f, tideCycleSeconds);
        tideCurrentlyRising = phase01 < 0.5f;
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);

        if (state == SliceState.LowTidePlanning && tideCurrentlyRising && currentWaterY > lowWaterY + 0.16f)
        {
            state = SliceState.TideRising;
            stateTimer = 0f;
            StartCurrentTideDrift();
            lastActionHint = netDeployed
                ? "潮头自己漫过桩脚。网已经在水里，你仍可以转动导流绳或趁航窗出船。"
                : "潮头已经进了桩林。还没放网的话，只能趁水位尚低在网桩旁补放；海不会等人。";
        }

        if (state != SliceState.TideRising)
        {
            return;
        }

        if (netDeployed)
        {
            TickNetWaterExposure(deltaTime);
        }

        ResolveExitedTideDriftBatches();

        if (!tideCurrentlyRising && currentWaterY <= lowWaterY + 0.24f)
        {
            // The tide may leave one real object on the exposed rock only when that
            // same batch missed the net and has not already exited the nearshore route.
            // This is a weak fallback and a readable tide trace, not a second reward roll.
            TrySettleCurrentTideWrack();

            if (!netDeployed && !netSecuredEarly)
            {
                ReleaseNearshoreSaltWoodAfterTide();
                state = SliceState.LowTidePlanning;
                stateTimer = 0f;
                currentHarvest = HarvestKind.None;
                lastActionHint = "这一潮没有下网，退水只在泥滩留下盐线。下一潮仍会按时来。";
                return;
            }

            if (netSecuredEarly)
            {
                state = SliceState.EbbCollect;
                stateTimer = 0f;
                lastActionHint = $"潮已经退远，{GetHarvestName()}仍绑在网桩，没有隔空跑到你身上。回到网桩按 F 解下再带走。";
                return;
            }

            if (netCatchVisualPieceCount < netCatchBundleTier)
            {
                // A load tier that had not physically reached the mesh before the
                // water left does not survive as a number-only reward.
                netCatchBundleTier = Mathf.Max(1, netCatchVisualPieceCount);
                addedHarvestPieceTravel01 = 0f;
            }

            if (netTouched &&
                !netCatchResolved &&
                netCaptureProgress01 >= 0.999f &&
                TideDriftSourceModel.IsInsideNetCaptureWindow(incomingHarvestTravel01))
            {
                ResolveNetCatch();
            }

            state = SliceState.EbbCollect;
            stateTimer = 0f;
            if (!netTouched)
            {
                ReleaseNearshoreSaltWoodAfterTide();
                currentHarvest = HarvestKind.None;
                lastActionHint = "这一潮没有够到网：它还是干的。回网桩按住 F 收起，别让空网凭空变出东西。";
                return;
            }

            if (currentHarvest == HarvestKind.None)
            {
                ReleaseNearshoreSaltWoodAfterTide();
                lastActionHint = netTouched
                    ? "水确实穿过了网，但这一批漂物从网侧滑过去了。回网桩按住 F 收起湿网；没有实物就不会凭空结算废料。"
                    : "这一潮没有够到网：它还是干的。回网桩按住 F 收起，别让空网凭空变出东西。";
                return;
            }

            lastActionHint = "潮水自然退远了：回到网桩旁，按住 F 把湿网和挂物一起拉回来。";
        }
    }

    private void AdvanceHighWaterMemory(
        float startTideClockSeconds,
        float startWeatherClockSeconds,
        float elapsedSeconds)
    {
        float duration = Mathf.Max(0f, elapsedSeconds);
        float cycle = Mathf.Max(8f, tideCycleSeconds);
        float startPhaseSeconds = Mathf.Repeat(startTideClockSeconds, cycle);
        highWaterMemory = TideHighWaterMemoryModel.Observe(
            highWaterMemory,
            EvaluateNaturalWaterY(startPhaseSeconds, startWeatherClockSeconds));

        float elapsed = 0f;
        while (elapsed < duration - 0.0001f)
        {
            float nextElapsed = Mathf.Min(duration, elapsed + HighWaterMemorySampleStepSeconds);
            float unwrappedBefore = startPhaseSeconds + elapsed;
            float unwrappedAfter = startPhaseSeconds + nextElapsed;
            float sampleTideClock = Mathf.Repeat(unwrappedAfter, cycle);
            float sampleWeatherClock = startWeatherClockSeconds + nextElapsed;
            float sampleWaterY = EvaluateNaturalWaterY(sampleTideClock, sampleWeatherClock);
            highWaterMemory = TideHighWaterMemoryModel.Observe(highWaterMemory, sampleWaterY);

            int completedBefore = Mathf.FloorToInt(unwrappedBefore / cycle);
            int completedAfter = Mathf.FloorToInt(unwrappedAfter / cycle);
            while (completedBefore < completedAfter)
            {
                // The endpoint lies just inside the new low-water cycle. Adding that
                // low sample before settlement cannot raise the old peak; it does let
                // the new cycle begin from the actual pressure-adjusted water level.
                highWaterMemory = TideHighWaterMemoryModel.CompleteCycle(
                    highWaterMemory,
                    sampleWaterY);
                completedBefore++;
            }

            elapsed = nextElapsed;
        }
    }

    private void TickNetWaterExposure(float deltaTime)
    {
        float activeNetY = GetSelectedNetY();
        TideOceanSample netOcean = GetNetOceanSample();
        float contact01 = EvaluateNetWaterContact01(activeNetY, netOcean.SurfaceY);
        bool netIsWet = contact01 > 0.01f;
        if (netIsWet && !netTouched)
        {
            netTouched = true;
            lastActionHint = "潮头刚碰到网：浮子开始下沉。湿网只会变重，漂物真正进入网口前不会预先积攒收获。";
        }

        if (netCatchResolved)
        {
            if (!netIsWet)
            {
                return;
            }

            float materialLoad = GetHarvestLoadTensionMultiplier(
                currentHarvest,
                tideClockSeconds + netPostCatchExposureSeconds);
            float cargoLoad01 = GetNetCatchLoad01();
            float instantaneousTension = AdvanceNetLoadLedger(
                deltaTime,
                contact01,
                materialLoad,
                cargoLoad01,
                HasRoutedLoadBonus());
            TickPostCatchLoad(deltaTime, contact01, instantaneousTension);
            return;
        }

        if (netIsWet)
        {
            netWaterExposureSeconds += Mathf.Max(0f, deltaTime) * contact01;
            AdvanceNetLoadLedger(deltaTime, contact01, 1f, 0f, HasRoutedLoadBonus());
        }

        if (harvestPhysicalState != HarvestPhysicalState.Drifting || tideSourceHarvest == HarvestKind.None)
        {
            netCaptureProgress01 = 0f;
            return;
        }

        TideNetEncounterModel.Step encounter = TideNetEncounterModel.Advance(
            netCaptureProgress01,
            deltaTime,
            previousIncomingHarvestTravel01,
            incomingHarvestTravel01,
            GetNetHeadLineY(),
            activeNetY,
            netOcean.SurfaceY,
            netIntegrity / 4f,
            GetCurrentPrimaryDriftMaterial());
        netCaptureProgress01 = encounter.Progress01;

        if (encounter.Captured)
        {
            ResolveNetCatch();
            return;
        }

        if (encounter.ContactLost)
        {
            lastActionHint = "漂物擦过网缘后继续沿原水路漂走，未挂稳的接触不会留成隐藏进度。退潮后它可能在岸线上留下实物。";
        }
        else if (encounter.HasPhysicalContact)
        {
            lastActionHint = $"漂物正压进浸水网面：覆盖 {Mathf.RoundToInt(encounter.MeshCoverage01 * 100f)}%，缠挂 {Mathf.RoundToInt(netCaptureProgress01 * 100f)}%。抬浅会减载，也可能让它从网底漏过。";
        }
    }

    private void TickPostCatchLoad(float deltaTime, float contact01, float instantaneousTension)
    {
        if (netBrokeThisTide)
        {
            return;
        }

        netPostCatchExposureSeconds += deltaTime * contact01;

        int previousTier = netCatchBundleTier;
        float load01 = GetNetCatchLoad01();
        int timedTier = load01 >= 0.82f ? 3 : load01 >= 0.38f ? 2 : 1;
        netCatchBundleTier = Mathf.Min(3, timedTier + (HasRoutedLoadBonus() ? 1 : 0));

        TickNetFraying(deltaTime, IsActivelyHaulingNet());
        if (netBrokeThisTide)
        {
            lastActionHint = $"你把网留得太久，持续张力扯开了旧绳；原来的{GetHarvestName(washedAwayHarvestKind)}正被水冲散，断绳上留下的是另一团缠网废料。";
            return;
        }

        if (netFraying01 > 0.01f)
        {
            lastActionHint = netFraying01 >= 0.72f
                ? "最后一股绳正在连续崩开。立刻在网桩按住 F 抢收；先用 W 抬网能稍微减慢恶化。"
                : "旧绳开始一股股崩开。继续留网会恶化；回网桩抬网或按住 F 收住它。";
        }

        else if (netCatchBundleTier != previousTier)
        {
            lastActionHint = netCatchBundleTier >= 3
                ? $"水路里又出现了{GetHarvestName()}，正朝网面逼近；等它真正挂稳才算满载，继续留网也会增加断裂风险。"
                : $"潮里又带来一件{GetHarvestName()}，它还在贴近网面；真正挂住前不会只改一个负载数字。";
        }
    }

    private float GetNetCatchLoad01()
    {
        // Older scenes still serialize the prototype's 5.4 second value. Keep the
        // gameplay contract in code so an already-open scene cannot silently restore
        // the impossible reaction window after a domain reload.
        float pace = GetHarvestLoadPaceMultiplier(currentHarvest);
        return Mathf.Clamp01(
            netPostCatchExposureSeconds * pace /
            Mathf.Max(24f, netPostCatchFullLoadSeconds));
    }

    private float AdvanceNetLoadLedger(
        float deltaTime,
        float contact01,
        float materialLoadMultiplier,
        float cargoLoad01,
        bool routedExtraLoad)
    {
        float currentStrength01 = GetNaturalCurrentStrength01();
        float weatherLoad = TideContinuousWeatherModel.EvaluateWaveLoadMultiplier(GetStormPressure01());
        float instantaneousLoad = TideNetLoadLedgerModel.EvaluateImpulseRate(
            contact01,
            netSetDepth01,
            tideStrength,
            currentStrength01,
            weatherLoad,
            materialLoadMultiplier,
            cargoLoad01,
            routedExtraLoad);
        netAccumulatedTension = TideNetLoadLedgerModel.AdvanceImpulse(
            netAccumulatedTension,
            deltaTime,
            contact01,
            netSetDepth01,
            tideStrength,
            currentStrength01,
            weatherLoad,
            materialLoadMultiplier,
            cargoLoad01,
            routedExtraLoad);
        netPeakTension01 = Mathf.Max(netPeakTension01, Mathf.Clamp01(instantaneousLoad));
        CommitAccumulatedNetStress();
        return instantaneousLoad;
    }

    private void ResolveNetCatch()
    {
        if (netCatchResolved)
        {
            return;
        }

        netCatchResolved = true;
        currentHarvestFromWrack = false;
        netPostCatchExposureSeconds = 0f;
        // The routed saltwood is a second physical object. Merely sharing the horizontal
        // window with this catch is not enough: its own waterline/mesh overlap is advanced
        // by TryCatchRoutedSaltWoodInResolvedNet before it can join the bundle.
        netCatchBundleTier = 1;
        HarvestKind reachedHarvest = GetIncomingTideCarryKind();
        ApplyNetStress();
        if (netBrokeThisTide)
        {
            BeginBrokenNetResidue(reachedHarvest, netCatchBundleTier);
        }
        else
        {
            currentHarvest = reachedHarvest;
            currentHarvestBatchId = tideSourceBatchId;
            harvestPhysicalState = HarvestPhysicalState.CaughtInNet;
            netCatchVisualPieceCount = netCatchBundleTier;
            addedHarvestPieceTravel01 = 0f;
        }
        TideAudioController.PlayNetLoadCueInScene(netCatchBundleTier, netBrokeThisTide);
        lastActionHint = netBrokeThisTide
            ? $"持续水压最终扯裂了网，只剩{GetHarvestName()}挂在断绳上。"
            : netFraying01 > 0.01f
                ? $"{GetHarvestName()}压进网时扯开了最后几股旧绳，但网还没有断。立刻回网桩抬网或按住 F 抢收。"
            : HasRoutedLoadBonus()
            ? $"导流索仍在额外吃力；{GetHarvestName()}已经在自己的吃水高度挂稳。现在收稳，继续留会逐级加重。"
                : $"{GetHarvestName()}沿可见水路压进网面并挂稳；湿网累计受水 {netWaterExposureSeconds:0.0} 秒。{GetNetIntegrityText()}";
    }

    private void BeginBrokenNetResidue(HarvestKind lostKind, int lostPieceCount)
    {
        bool releasedExtraSaltWood = extraSaltWoodBundledWithNetHarvest &&
            (extraSaltWoodOwner == ExtraSaltWoodOwner.RoutedToNet ||
             extraSaltWoodOwner == ExtraSaltWoodOwner.CaughtInNet);
        if (releasedExtraSaltWood)
        {
            ReturnExtraSaltWoodToSailingWater();
            lostPieceCount = Mathf.Max(1, lostPieceCount - 1);
        }

        washedAwayHarvestKind = lostKind == HarvestKind.Trash ? HarvestKind.None : lostKind;
        washedAwayHarvestBatchId = washedAwayHarvestKind == HarvestKind.None
            ? 0
            : tideSourceBatchId;
        washedAwayHarvestPieceCount = washedAwayHarvestKind == HarvestKind.None
            ? 0
            : Mathf.Clamp(lostPieceCount, 1, 3);
        washedAwayHarvestTimer = washedAwayHarvestPieceCount > 0 ? 2.4f : 0f;
        washedAwayHarvestDriftX = 0f;
        netFraying01 = 1f;
        netDepthAdjustmentActive = false;
        netDepthAdjustmentDirection = 0f;

        // Trash is a new residue caught in the torn line. The previous fish, timber or
        // parcel remains identified separately long enough to visibly wash away.
        currentHarvest = HarvestKind.Trash;
        harvestPhysicalState = HarvestPhysicalState.CaughtInNet;
        netCatchBundleTier = 1;
        netCatchVisualPieceCount = 1;
        addedHarvestPieceTravel01 = 0f;
    }

    private void TickShelterTideStress()
    {
        if (state == SliceState.FinalDeparture || shelterStressAppliedThisTide || currentWaterY < GetShelterStressWaterlineY())
        {
            return;
        }

        shelterStressAppliedThisTide = true;
        ResolveShelterTideStress(true);
    }

    private void ResolveShelterTideStress(bool showImpact)
    {
        shelterRawStressThisTide = CalculateRawShelterTideStress();
        bool stakeMitigated = HasActiveTidePrep() && selectedPrepChoice == TidePrepChoice.Stake;
        shelterResolvedStressThisTide = Mathf.Max(0, shelterRawStressThisTide - (stakeMitigated ? 1 : 0));
        shelterBreachThisTide = false;

        if (shelterRawStressThisTide <= 0)
        {
            return;
        }

        shelterImpactTimer = showImpact ? 1.35f : 0f;
        int absorbed = Mathf.Min(stiltIntegrity, shelterResolvedStressThisTide);
        stiltIntegrity -= absorbed;
        int overflow = shelterResolvedStressThisTide - absorbed;
        if (overflow > 0)
        {
            shelterBreachThisTide = true;
            houseWarmth = Mathf.Max(0, houseWarmth - overflow);
            lastActionHint = houseWarmth > 0
                ? $"高潮撞松桩脚，{absorbed} 点由支柱扛住，仍有 {overflow} 点冷水冲进屋里；灯火暗了一截。"
                : $"高潮越过空掉的支撑，{absorbed} 点由支柱扛住，仍有 {overflow} 点冲进屋底；下一次收潮要优先修柱。";
            return;
        }

        if (shelterResolvedStressThisTide == 0 && stakeMitigated)
        {
            lastActionHint = "高潮撞上桩脚，昨夜备下的短桩和绑绳把这一下完整接住了。";
            return;
        }

        lastActionHint = $"高潮撞上高脚屋，柱脚吸收 {absorbed} 点压力；现在还剩 {stiltIntegrity} 点支撑。";
    }

    private bool SleepIntervalCrossesTidePeak(float skippedSeconds)
    {
        float cycle = Mathf.Max(8f, tideCycleSeconds);
        if (skippedSeconds >= cycle)
        {
            return true;
        }

        float start = Mathf.Repeat(tideClockSeconds, cycle);
        float end = start + Mathf.Max(0f, skippedSeconds);
        float peak = cycle * 0.5f;
        return (start <= peak && end >= peak) || end >= cycle + peak;
    }

    private int CalculateRawShelterTideStress()
    {
        float predictedHighWaterY = GetPredictedHighWaterY();
        if (predictedHighWaterY < GetShelterStressWaterlineY())
        {
            return 0;
        }

        int stress = 1;
        if (predictedHighWaterY >= GetPlayerStandingFeetY(WalkLane.Deck) ||
            GetPredictedStormPressureAtNextHighWater01() >= 0.9f)
        {
            stress++;
        }

        return Mathf.Clamp(stress, 0, 2);
    }

    private float EvaluateNaturalWaterY(float clockSeconds)
    {
        return EvaluateNaturalWaterY(clockSeconds, weatherClockSeconds);
    }

    private float EvaluateNaturalWaterY(float clockSeconds, float sampleWeatherClockSeconds)
    {
        float phase01 = Mathf.Repeat(clockSeconds / Mathf.Max(8f, tideCycleSeconds), 1f);
        int astronomicalCycleOrdinal = GetAstronomicalCycleOrdinal(sampleWeatherClockSeconds);
        float stormPressureAtHigh01 = EvaluateStormPressureAtCycleHigh01(
            sampleWeatherClockSeconds,
            clockSeconds,
            astronomicalCycleOrdinal);
        float cycleHighWorldClock = GetCycleHighWorldClockSeconds(
            sampleWeatherClockSeconds,
            clockSeconds,
            astronomicalCycleOrdinal);
        return EvaluateNaturalWaterYAtPhase(
            phase01,
            astronomicalCycleOrdinal,
            stormPressureAtHigh01,
            EvaluateTideInequalityRatio(cycleHighWorldClock));
    }

    private float GetNaturalTideHeight01()
    {
        float phase01 = Mathf.Repeat(tideClockSeconds / Mathf.Max(8f, tideCycleSeconds), 1f);
        int astronomicalCycleOrdinal = GetAstronomicalCycleOrdinal(weatherClockSeconds);
        float pressureAtCurrentHigh01 = EvaluateStormPressureAtCycleHigh01(
            weatherClockSeconds,
            tideClockSeconds,
            astronomicalCycleOrdinal);
        float currentCycleHighWaterY = EvaluateNaturalWaterYAtPhase(
            0.5f,
            astronomicalCycleOrdinal,
            pressureAtCurrentHigh01,
            EvaluateTideInequalityRatio(GetCycleHighWorldClockSeconds(
                weatherClockSeconds,
                tideClockSeconds,
                astronomicalCycleOrdinal)));
        return Mathf.InverseLerp(lowWaterY, currentCycleHighWaterY, currentWaterY);
    }

    private float GetPredictedHighWaterY()
    {
        return EvaluatePredictedHighWaterY(weatherClockSeconds, tideClockSeconds);
    }

    private float EvaluatePredictedHighWaterY(
        float sampleWeatherClockSeconds,
        float sampleTideClockSeconds)
    {
        float cycle = Mathf.Max(8f, tideCycleSeconds);
        float phase01 = Mathf.Repeat(sampleTideClockSeconds / cycle, 1f);
        int currentCycleOrdinal = GetAstronomicalCycleOrdinal(sampleWeatherClockSeconds);
        int nextHighCycleOrdinal = TideMixedSemidiurnalModel.GetNextHighWaterCycleOrdinal(
            phase01,
            currentCycleOrdinal);
        float pressureAtNextHighWater = EvaluateStormPressureAtCycleHigh01(
            sampleWeatherClockSeconds,
            sampleTideClockSeconds,
            nextHighCycleOrdinal);
        return EvaluateNaturalWaterYAtPhase(
            0.5f,
            nextHighCycleOrdinal,
            pressureAtNextHighWater,
            EvaluateTideInequalityRatio(GetCycleHighWorldClockSeconds(
                sampleWeatherClockSeconds,
                sampleTideClockSeconds,
                nextHighCycleOrdinal)));
    }

    private float GetPredictedStormPressureAtNextHighWater01()
    {
        float cycle = Mathf.Max(8f, tideCycleSeconds);
        float phase01 = Mathf.Repeat(tideClockSeconds / cycle, 1f);
        int currentCycleOrdinal = GetAstronomicalCycleOrdinal(weatherClockSeconds);
        int nextHighCycleOrdinal = TideMixedSemidiurnalModel.GetNextHighWaterCycleOrdinal(
            phase01,
            currentCycleOrdinal);
        return EvaluateStormPressureAtCycleHigh01(
            weatherClockSeconds,
            tideClockSeconds,
            nextHighCycleOrdinal);
    }

    private int GetAstronomicalCycleOrdinal(float sampleWorldClockSeconds)
    {
        return Mathf.FloorToInt(
            Mathf.Max(0f, sampleWorldClockSeconds) /
            Mathf.Max(8f, tideCycleSeconds));
    }

    private float EvaluateTideInequalityRatio(float sampleWorldClockSeconds)
    {
        float continuousDeclinationDays = OpeningLunarDeclinationAgeDays +
            Mathf.Max(0f, sampleWorldClockSeconds) / Mathf.Max(30f, dayLengthSeconds);
        return TideMixedSemidiurnalModel.EvaluateInequalityRatio(continuousDeclinationDays);
    }

    private float GetCycleHighWorldClockSeconds(
        float sampleWorldClockSeconds,
        float sampleTideClockSeconds,
        int targetCycleOrdinal)
    {
        float cycle = Mathf.Max(8f, tideCycleSeconds);
        float phase01 = Mathf.Repeat(sampleTideClockSeconds / cycle, 1f);
        int currentCycleOrdinal = GetAstronomicalCycleOrdinal(sampleWorldClockSeconds);
        float currentCycleStartClock = sampleWorldClockSeconds - phase01 * cycle;
        return currentCycleStartClock +
            (targetCycleOrdinal - currentCycleOrdinal + 0.5f) * cycle;
    }

    private float EvaluateStormPressureAtCycleHigh01(
        float sampleWorldClockSeconds,
        float sampleTideClockSeconds,
        int targetCycleOrdinal)
    {
        float targetHighClock = GetCycleHighWorldClockSeconds(
            sampleWorldClockSeconds,
            sampleTideClockSeconds,
            targetCycleOrdinal);
        return TideContinuousWeatherModel.EvaluatePressure01(
            Mathf.Max(0f, targetHighClock),
            dayLengthSeconds,
            stormFrontArrivalDays);
    }

    private float EvaluateNaturalWaterYAtPhase(
        float phase01,
        int astronomicalCycleOrdinal,
        float stormPressureAtHigh01,
        float inequalityRatio)
    {
        // A real stilt house is not flooded by every ordinary tide. Spring/neap range and
        // adjacent-high inequality are astronomical; weather surge remains a separate,
        // slowly changing pressure contribution that rises and falls with the same water.
        float astronomicalRange = TideAstronomicalCurrentModel.EvaluateAstronomicalRangeMeters(
            tideStrength);
        float mixedHeight01 = TideMixedSemidiurnalModel.EvaluateHeight01(
            phase01,
            astronomicalCycleOrdinal,
            inequalityRatio);
        float baseHeight01 = TideMixedSemidiurnalModel.EvaluateBaseHeight01(phase01);
        float stormSurgeMeters = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(0.42f, 1f, stormPressureAtHigh01)) * 1.15f;
        float waterY = lowWaterY +
            astronomicalRange * mixedHeight01 +
            stormSurgeMeters * baseHeight01;
        return Mathf.Min(highWaterY + 0.15f, waterY);
    }

    private float GetShelterStressWaterlineY()
    {
        return GetPlayerStandingFeetY(WalkLane.Deck) - 0.55f;
    }

    private string GetPredictedHighWaterBand()
    {
        return GetPredictedHighWaterBand(GetPredictedHighWaterY());
    }

    private string GetPredictedHighWaterBand(float predictedHigh)
    {
        float outerPierFeet = GetPlayerStandingFeetY(WalkLane.TideFlat);
        float lowerFloorFeet = GetPlayerStandingFeetY(WalkLane.Deck);
        if (predictedHigh < outerPierFeet - 0.04f)
        {
            return "高潮仅到桩脚";
        }

        if (predictedHigh < GetShelterStressWaterlineY())
        {
            return "高潮会淹外栈桥";
        }

        if (predictedHigh < lowerFloorFeet)
        {
            return "风暴增水将拍屋底";
        }

        return "极端暴潮将进下层";
    }

    private string GetForecastHighWaterRangeText(bool repairedChart)
    {
        TideNetForecastModel.HighWaterBand band = TideNetForecastModel.EvaluateHighWaterBand(
            GetPredictedHighWaterY(),
            repairedChart);
        return GetForecastHighWaterRangeText(band);
    }

    private string GetForecastHighWaterRangeText(TideForecastSnapshot snapshot)
    {
        return GetForecastHighWaterRangeText(snapshot.ToHighWaterBand());
    }

    private string GetForecastHighWaterRangeText(TideNetForecastModel.HighWaterBand band)
    {
        string lowerBand = GetHighWaterLandmarkName(band.LowerY);
        string upperBand = GetHighWaterLandmarkName(band.UpperY);
        int uncertaintyCentimeters = Mathf.RoundToInt(band.UncertaintyMeters * 100f);
        return lowerBand == upperBand
            ? $"高潮大致到{lowerBand}（误差约{uncertaintyCentimeters}厘米）"
            : $"高潮可能在{lowerBand}至{upperBand}之间（误差约{uncertaintyCentimeters}厘米）";
    }

    private string GetHighWaterLandmarkName(float predictedHigh)
    {
        float outerPierFeet = GetPlayerStandingFeetY(WalkLane.TideFlat);
        float lowerFloorFeet = GetPlayerStandingFeetY(WalkLane.Deck);
        if (predictedHigh < outerPierFeet - 0.04f)
        {
            return "桩脚水线";
        }

        if (predictedHigh < GetShelterStressWaterlineY())
        {
            return "外栈桥";
        }

        return predictedHigh < lowerFloorFeet ? "屋底" : "可淹下层";
    }

    private float GetNaturalCurrentSpeed()
    {
        return EvaluateNaturalCurrentSpeed(tideClockSeconds);
    }

    private float EvaluateNaturalCurrentSpeed(float clockSeconds)
    {
        // 水位和流速消费同一个混合半日潮解析函数。风暴增水不在这里参与：
        // 它是缓慢水位偏置和浪载，不是凭空产生的往复潮流。
        float cycle = Mathf.Max(8f, tideCycleSeconds);
        float phase01 = Mathf.Repeat(clockSeconds / cycle, 1f);
        int astronomicalCycleOrdinal = GetAstronomicalCycleOrdinal(weatherClockSeconds);
        float inequalityRatio = EvaluateTideInequalityRatio(GetCycleHighWorldClockSeconds(
            weatherClockSeconds,
            clockSeconds,
            astronomicalCycleOrdinal));
        float signedFlowWave = TideMixedSemidiurnalModel.EvaluateSignedCurrentWave(
            phase01,
            astronomicalCycleOrdinal,
            inequalityRatio);
        return TideAstronomicalCurrentModel.EvaluateSignedSpeedFromWave(
            signedFlowWave,
            tideStrength,
            CalculateTideStrength(OpeningMoonAgeDays),
            MeanTidalTransportSpeed,
            EbbCurrentBoost);
    }

    private float GetReferenceFloodCurrentSpeed()
    {
        float referenceStrength = CalculateTideStrength(OpeningMoonAgeDays);
        return Mathf.Abs(TideAstronomicalCurrentModel.EvaluateSignedSpeed(
            tideCycleSeconds * 0.25f,
            tideCycleSeconds,
            referenceStrength,
            referenceStrength,
            MeanTidalTransportSpeed,
            EbbCurrentBoost));
    }

    private float GetNaturalCurrentSigned01()
    {
        // 这里只表达当前混合潮从平流到急流的形状。网压模型已经单独读取
        // 春/小潮强度，再把米制潮差倍率塞进来会重复放大；但相邻潮不等高
        // 改变了本潮导数形状，必须和真实水位保持同相。
        float cycle = Mathf.Max(8f, tideCycleSeconds);
        float phase01 = Mathf.Repeat(tideClockSeconds / cycle, 1f);
        int astronomicalCycleOrdinal = GetAstronomicalCycleOrdinal(weatherClockSeconds);
        float inequalityRatio = EvaluateTideInequalityRatio(GetCycleHighWorldClockSeconds(
            weatherClockSeconds,
            tideClockSeconds,
            astronomicalCycleOrdinal));
        return Mathf.Clamp(
            TideMixedSemidiurnalModel.EvaluateSignedCurrentWave(
                phase01,
                astronomicalCycleOrdinal,
                inequalityRatio),
            -1f,
            1f);
    }

    private float GetNaturalCurrentStrength01()
    {
        return Mathf.Abs(GetNaturalCurrentSigned01());
    }

    // Day sea breeze pushes toward the stilt house (negative X), night land breeze
    // pushes offshore (positive X), and both pass smoothly through weak dawn/dusk air.
    // The second sine only changes gust strength, so wind never flips direction abruptly.
    private float GetNaturalSailingWindSpeed()
    {
        float dayNightBreeze = -Mathf.Cos((dayProgress01 - 0.5f) * Mathf.PI * 2f);
        float gustPhase = dayClockSeconds * 0.37f + tideClockSeconds * 0.21f + moonAgeDays * 0.13f;
        float gust01 = 0.5f + Mathf.Sin(gustPhase) * 0.5f;
        float gustStrength = Mathf.Lerp(0.58f, 1f, Mathf.SmoothStep(0f, 1f, gust01));
        // 新一局先给玩家一个近静风的观察窗。只在真实 Play 时间中渐入，
        // 编辑器探针仍可直接采样完整风场，不把测试结果绑到等待四十秒。
        float openingWind01 = Application.isPlaying
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(18f, 58f, worldElapsedRealSeconds))
            : 1f;
        float baseWind = dayNightBreeze * sailingWindMaxSpeed * gustStrength * openingWind01;
        return GetStormAdjustedWindSpeed(baseWind, GetStormPressure01());
    }

    private float GetStormAdjustedWindSpeed(float baseWindSpeed, float stormPressure01)
    {
        float onshore01 = TideContinuousWeatherModel.EvaluateStormOnshoreWind01(stormPressure01);
        float onshoreStormWind = -sailingWindMaxSpeed * Mathf.Lerp(0.74f, 1.42f, onshore01);
        return Mathf.Lerp(baseWindSpeed * Mathf.Lerp(0.86f, 1.05f, onshore01), onshoreStormWind, onshore01 * 0.78f);
    }

    private float GetSailingWindAssist()
    {
        float sailCoupling = Mathf.SmoothStep(0f, 1f, sailingSailTrim01);
        float repairedRigging = Mathf.Lerp(0.72f, 1.04f, GetBoatReadiness01());
        // Hauling leaves one hand on the wet line and one on the tiller. It reduces
        // usable sail drive, but does not freeze the boat or secretly brake it.
        float handsBusy = sailingBailing
            ? 0.22f
            : sailingSalvageHauling ? 0.62f : 1f;
        return GetNaturalSailingWindSpeed() * sailCoupling * repairedRigging * handsBusy;
    }

    private float GetSailingSurfaceFlowSpeed()
    {
        return GetNaturalCurrentSpeed() * 0.62f + GetNaturalSailingWindSpeed() * 0.78f;
    }

    private string GetSailingWindText()
    {
        float wind = GetNaturalSailingWindSpeed();
        if (Mathf.Abs(wind) < 0.06f)
        {
            return "风弱";
        }

        float intendedDirection = Mathf.Abs(sailingBoatVelocity) > 0.08f
            ? Mathf.Sign(sailingBoatVelocity)
            : playerFacing;
        bool followingWind = Mathf.Sign(wind) == intendedDirection;
        return $"{(wind < 0f ? "向岸风" : "离岸风")} · {(followingWind ? "顺风" : "顶风")}";
    }

    private void TickPlayerBuoyancyAndCurrent(float deltaTime)
    {
        bool exposedTideFlat = viewMode == SliceViewMode.Shelter && playerLane == WalkLane.TideFlat;
        bool floodedWorkFloor = viewMode == SliceViewMode.Interior && playerLane == WalkLane.InteriorLower;
        if ((!exposedTideFlat && !floodedWorkFloor) || isLaneTransitioning)
        {
            playerSwimming = false;
            playerSubmersion01 = 0f;
            playerAquaticBlend01 = Mathf.MoveTowards(
                playerAquaticBlend01,
                0f,
                Mathf.Max(0f, deltaTime) / 0.42f);
            playerCurrentDriftVelocity = 0f;
            return;
        }

        WalkLane wetLane = exposedTideFlat ? WalkLane.TideFlat : WalkLane.InteriorLower;
        float groundY = GetPlayerLaneY(wetLane);
        float standingFeetY = groundY - 0.56f;
        float swimWaterY = groundY - 0.08f;
        TideOceanSample ocean = GetOceanSample(playerPosition.x);
        playerSubmersion01 = Mathf.InverseLerp(
            standingFeetY + 0.08f,
            swimWaterY,
            ocean.SurfaceY);
        // SmoothStep 的前两个参数是输出起止值，并不是阈值。先把浸水量映射到
        // 0.34~0.92 的有效区间，才能让人物离水后真正回到 0，而不是永久残留 34% 游泳姿态。
        float targetAquaticBlend = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(0.34f, 0.92f, playerSubmersion01));
        playerAquaticBlend01 = Mathf.MoveTowards(
            playerAquaticBlend01,
            targetAquaticBlend,
            Mathf.Max(0f, deltaTime) / 0.42f);

        // 潮流只在水真正托起人物后才拖动人物。进入和退出使用不同阈值，
        // 避免角色在连续浪峰附近一帧站立、一帧游泳。
        bool shouldSwim = playerSwimming
            ? ocean.SurfaceY >= swimWaterY - 0.1f
            : ocean.SurfaceY >= swimWaterY;
        playerSwimming = shouldSwim;
        if (shouldSwim)
        {
            float driftX = (GetNaturalCurrentSpeed() + ocean.HorizontalVelocity) * deltaTime;
            float beforeDriftX = playerPosition.x;
            float afterDriftX = Mathf.Clamp(
                beforeDriftX + driftX,
                GetLaneMinX(wetLane),
                GetLaneMaxX(wetLane));
            float actualDriftX = afterDriftX - beforeDriftX;
            playerCurrentDriftVelocity = deltaTime > 0.0001f ? actualDriftX / deltaTime : 0f;
            playerPosition = new Vector2(
                afterDriftX,
                Mathf.Lerp(groundY, ocean.SurfaceY + 0.02f, playerAquaticBlend01));

            bool activelySwimming = Mathf.Abs(playerHorizontalVelocity) > 0.025f;
            bool actuallyDrifting = Mathf.Abs(playerCurrentDriftVelocity) > 0.01f;
            if (!activelySwimming && actuallyDrifting)
            {
                // Passive drift still requires the survivor to keep their head above
                // water. Advance the authored swim cycle only when the current truly
                // changes world position; clamping at a boundary must not cause air-swimming.
                playerWalkCycle += deltaTime * Mathf.Lerp(0.65f, 1.15f, GetNaturalCurrentStrength01());
            }
            playerMoving = activelySwimming || actuallyDrifting;
        }
        else
        {
            playerCurrentDriftVelocity = 0f;
            playerPosition = new Vector2(
                playerPosition.x,
                Mathf.Lerp(groundY, ocean.SurfaceY + 0.02f, playerAquaticBlend01));
            playerMoving = Mathf.Abs(playerHorizontalVelocity) > 0.025f;
        }
    }

    private TideOceanSample GetOceanSample(float worldX)
    {
        float wind01 = Mathf.Clamp01(Mathf.Abs(GetNaturalSailingWindSpeed()) /
            Mathf.Max(0.01f, sailingWindMaxSpeed));
        return TideOceanFieldModel.Sample(
            currentWaterY,
            worldX,
            weatherClockSeconds,
            tideStrength,
            GetStormPressure01(),
            wind01);
    }

    private TideOceanSample GetNetOceanSample()
    {
        return GetOceanSample(netAnchor.x);
    }

    private static float EvaluateNetWaterContact01(float activeNetY, float surfaceY)
    {
        return Mathf.InverseLerp(activeNetY - 0.18f, activeNetY + 0.2f, surfaceY);
    }

    private static float EvaluateNetWaterOvertopped01(float activeNetY, float surfaceY)
    {
        return Mathf.InverseLerp(activeNetY + 0.18f, activeNetY + 0.5f, surfaceY);
    }

    private bool TickSurvival(float deltaTime)
    {
        deathFeedbackTimer = Mathf.Max(0f, deathFeedbackTimer - deltaTime);

        if (survivalPresentationState != SurvivalPresentationState.None)
        {
            return true;
        }

        float storm01 = GetStormPressure01();
        float coldExposure01 = playerSubmersion01;
        if (viewMode == SliceViewMode.Sailing)
        {
            coldExposure01 = Mathf.Clamp01(0.12f + sailingWaterIngress01 * 0.74f + storm01 * 0.18f);
        }
        else if (viewMode == SliceViewMode.Interior &&
                 (playerLane == WalkLane.InteriorUpper || playerLane == WalkLane.InteriorLoft) &&
                 storm01 > 0.35f)
        {
            float roofLeak01 = 1f - Mathf.Clamp01(roofIntegrity / 2f);
            float exposureScale = playerLane == WalkLane.InteriorLoft ? 0.24f : 0.12f;
            float sealProtection = interiorSealCondition > 0 ? 0.58f : 1f;
            coldExposure01 = Mathf.Max(coldExposure01, storm01 * roofLeak01 * exposureScale * sealProtection);
        }

        if (coldExposure01 > 0.02f)
        {
            float nightMultiplier = dayNightPhase == DayNightPhase.Night ? 1.28f : 1f;
            bodyWarmth01 = Mathf.Max(0f, bodyWarmth01 - coldWaterWarmthLossPerSecond * coldExposure01 * nightMultiplier * deltaTime);
        }
        else
        {
            bool besideStove = viewMode == SliceViewMode.Interior &&
                playerLane == WalkLane.InteriorUpper &&
                Mathf.Abs(playerPosition.x - GetInteriorLampX()) <= 1.05f;
            float recovery = besideStove
                ? Mathf.Lerp(dryWarmthRecoveryPerSecond, stoveWarmthRecoveryPerSecond, Mathf.Clamp01(stoveCondition / 2f))
                : dryWarmthRecoveryPerSecond * 0.34f;
            bodyWarmth01 = Mathf.Min(1f, bodyWarmth01 + recovery * deltaTime);
        }

        // A character who is visibly swimming at the surface is breathing. Suffocation
        // only starts when a grounded body is actually overtopped; open-water danger
        // is represented by cold, current and the need to get back onto a ladder.
        bool headUnderWater = !playerSwimming && playerSubmersion01 >= 0.96f;
        bool boatFoundering = viewMode == SliceViewMode.Sailing && sailingWaterIngress01 >= 0.985f;
        drowningSeconds = headUnderWater || boatFoundering
            ? drowningSeconds + deltaTime
            : Mathf.Max(0f, drowningSeconds - deltaTime * 1.8f);

        if (boatFoundering && drowningSeconds >= 2.2f)
        {
            BeginDeathPresentation(
                "船舱灌满，船在离岸流里失去浮力",
                SurvivalPresentationState.Drowning);
            return true;
        }

        if (headUnderWater && drowningSeconds >= drowningGraceSeconds)
        {
            BeginDeathPresentation(
                "被涨潮压在水下太久",
                SurvivalPresentationState.Drowning);
            return true;
        }

        if (bodyWarmth01 <= 0.001f)
        {
            BeginDeathPresentation(
                "湿衣和夜风耗尽了体温",
                SurvivalPresentationState.ColdCollapse);
            return true;
        }

        if (bodyWarmth01 < 0.24f && deathFeedbackTimer <= 0f)
        {
            lastActionHint = "手指已经不听使唤。立刻离水，回干燥层靠近灶台；继续淋水会失温死亡。";
        }

        return false;
    }

    private bool BeginSleepPresentation()
    {
        if (survivalPresentationState != SurvivalPresentationState.None ||
            viewMode != SliceViewMode.Interior)
        {
            return false;
        }

        if (!CanRestThroughCurrentStormState())
        {
            lastActionHint = "楼下仍有没系牢的实物，天亮前的水会经过那里。现在睡下等于放弃这次取舍。";
            return false;
        }

        BeginSurvivalPresentation(SurvivalPresentationState.Sleeping, string.Empty);
        lastActionHint = bedCondition > 0
            ? "你在抬高的干床上躺下。潮、风和雨仍会走过这一夜。"
            : "旧床仍带着潮气，但至少离开了水面。潮、风和雨仍会走过这一夜。";
        return true;
    }

    private void BeginDeathPresentation(
        string reason,
        SurvivalPresentationState presentation)
    {
        if (survivalPresentationState != SurvivalPresentationState.None)
        {
            return;
        }

        if (presentation != SurvivalPresentationState.Drowning &&
            presentation != SurvivalPresentationState.ColdCollapse)
        {
            throw new ArgumentOutOfRangeException(nameof(presentation), presentation, null);
        }

        BeginSurvivalPresentation(presentation, reason);
        lastActionHint = presentation == SurvivalPresentationState.Drowning
            ? "水没过了呼吸。身体正在随真实水面下沉。"
            : "湿衣和风耗尽了体温。身体先倒下，惩罚不会抢在动作前结算。";
    }

    private void BeginSurvivalPresentation(
        SurvivalPresentationState presentation,
        string reason)
    {
        survivalPresentationState = presentation;
        survivalPresentationTimer = 0f;
        survivalPresentationStartPosition = playerPosition;
        survivalPresentationStartView = viewMode;
        pendingDeathReason = reason ?? string.Empty;

        // 动作开始后不允许残留的梯子、门或工作状态继续驱动身体根节点。
        boatViewTransition = BoatViewTransition.None;
        boatViewTransitionTimer = 0f;
        boatViewTransitionSwitched = false;
        isLaneTransitioning = false;
        laneTransitionProgress = 0f;
        playerMoving = false;
        playerHorizontalVelocity = 0f;
        playerCurrentDriftVelocity = 0f;
        netRigActionHeld = false;
        netDepthAdjustmentActive = false;
        repairWorkActive = false;
        tidePrepWorkActive = false;
        routingWorkActive = false;
        sailingBailing = false;
    }

    private void TickSurvivalPresentation(float deltaTime)
    {
        if (survivalPresentationState == SurvivalPresentationState.None)
        {
            return;
        }

        SurvivalPresentationState completedState = survivalPresentationState;
        survivalPresentationTimer += Mathf.Max(0f, deltaTime);
        if (survivalPresentationTimer + 0.0001f < GetSurvivalPresentationDuration(completedState))
        {
            return;
        }

        string deathReason = pendingDeathReason;
        survivalPresentationState = SurvivalPresentationState.None;
        survivalPresentationTimer = 0f;
        pendingDeathReason = string.Empty;

        if (completedState == SurvivalPresentationState.Sleeping)
        {
            AdvanceMoonPhase();
            return;
        }

        HandlePlayerDeath(deathReason);
    }

    private float GetSurvivalPresentationProgress01()
    {
        return survivalPresentationState == SurvivalPresentationState.None
            ? 0f
            : Mathf.Clamp01(
                survivalPresentationTimer /
                Mathf.Max(0.01f, GetSurvivalPresentationDuration(survivalPresentationState)));
    }

    private static float GetSurvivalPresentationDuration(
        SurvivalPresentationState presentation)
    {
        switch (presentation)
        {
            case SurvivalPresentationState.Sleeping:
                return TideV42CharacterSurvivalPresentationModel.GetOneShotDurationSeconds(
                    TideV42CharacterSurvivalAction.Sleep);
            case SurvivalPresentationState.Drowning:
                return TideV42CharacterSurvivalPresentationModel.GetOneShotDurationSeconds(
                    TideV42CharacterSurvivalAction.Drown);
            case SurvivalPresentationState.ColdCollapse:
                return TideV42CharacterSurvivalPresentationModel.GetOneShotDurationSeconds(
                    TideV42CharacterSurvivalAction.ColdCollapse);
            default:
                return 0f;
        }
    }

    private bool TryGetV42SurvivalWorldFrame(
        bool allowIdleShiver,
        float worldTime,
        out Sprite frame,
        out Vector2 pivotWorldPosition,
        out float uniformScale,
        out bool flipX)
    {
        frame = null;
        pivotWorldPosition = playerPosition;
        uniformScale = TideV42CharacterSurvivalPresentationModel.UniformScale;
        flipX = playerFacing < 0;
        EnsureV42CharacterSurvivalResourcesLoaded();
        if (!HasCompleteV42CharacterSurvivalPresentation())
        {
            return false;
        }

        if (survivalPresentationState == SurvivalPresentationState.None)
        {
            if (!allowIdleShiver || bodyWarmth01 >= 0.24f)
            {
                return false;
            }

            frame = TideV42CharacterSurvivalPresentationModel.EvaluateLoopFrame(
                formalCharacterV42SurvivalCatalog,
                TideV42CharacterSurvivalAction.ColdShiver,
                worldTime);
            pivotWorldPosition = playerPosition +
                Vector2.down * TideV20CharacterPresentationModel.BodyWorldLength * 0.5f +
                Vector2.up * TideV42CharacterSurvivalPresentationModel.SurfacePivotCorrectionWorldY;
            return frame != null;
        }

        if (survivalPresentationStartView != viewMode ||
            survivalPresentationStartView == SliceViewMode.Sailing)
        {
            return false;
        }

        float progress01 = GetSurvivalPresentationProgress01();
        TideV42CharacterSurvivalAction action;
        switch (survivalPresentationState)
        {
            case SurvivalPresentationState.Sleeping:
                action = TideV42CharacterSurvivalAction.Sleep;
                break;
            case SurvivalPresentationState.Drowning:
                action = TideV42CharacterSurvivalAction.Drown;
                break;
            case SurvivalPresentationState.ColdCollapse:
                action = TideV42CharacterSurvivalAction.ColdCollapse;
                break;
            default:
                return false;
        }

        frame = TideV42CharacterSurvivalPresentationModel.EvaluateOneShotFrame(
            formalCharacterV42SurvivalCatalog,
            action,
            progress01);
        if (frame == null)
        {
            return false;
        }

        if (survivalPresentationState == SurvivalPresentationState.Drowning)
        {
            TideOceanSample ocean = GetOceanSample(survivalPresentationStartPosition.x);
            pivotWorldPosition = TideV42CharacterSurvivalPresentationModel.EvaluateDrownPivotWorld(
                new Vector2(survivalPresentationStartPosition.x, ocean.SurfaceY),
                progress01);
            return true;
        }

        Vector2 bodyCenter = survivalPresentationStartPosition;
        if (survivalPresentationState == SurvivalPresentationState.Sleeping)
        {
            float bedFloorY = GetPlayerLaneY(WalkLane.InteriorUpper) -
                TideV20CharacterPresentationModel.BodyWorldLength * 0.5f;
            Vector2 bedBodyCenter = new Vector2(
                GetInteriorBedX(),
                bedFloorY + 0.22f + TideV20CharacterPresentationModel.BodyWorldLength * 0.5f);
            bodyCenter = Vector2.Lerp(
                survivalPresentationStartPosition,
                bedBodyCenter,
                TideV42CharacterSurvivalPresentationModel.EvaluateSleepBedEntry01(progress01));
        }
        else if (survivalPresentationState == SurvivalPresentationState.ColdCollapse)
        {
            bodyCenter.x += TideV42CharacterSurvivalPresentationModel.EvaluateCollapseForwardWorld(
                progress01,
                playerFacing);
        }

        pivotWorldPosition = bodyCenter +
            Vector2.down * TideV20CharacterPresentationModel.BodyWorldLength * 0.5f +
            Vector2.up * TideV42CharacterSurvivalPresentationModel.SurfacePivotCorrectionWorldY;
        return true;
    }

    private void HandlePlayerDeath(string reason)
    {
        survivalPresentationState = SurvivalPresentationState.None;
        survivalPresentationTimer = 0f;
        survivalPresentationStartPosition = Vector2.zero;
        pendingDeathReason = string.Empty;
        boatViewTransition = BoatViewTransition.None;
        boatViewTransitionTimer = 0f;
        boatViewTransitionSwitched = false;
        pendingTidePrepChoice = TidePrepChoice.None;
        tidePrepWorkProgress = 0f;
        tidePrepWorkActive = false;
        deathCount++;
        lastDeathReason = reason;
        deathFeedbackTimer = 3.8f;

        bool shelterSafeHarvest = currentHarvest != HarvestKind.None &&
            !currentHarvestBanked &&
            harvestPhysicalState == HarvestPhysicalState.PlacedAtWork;
        bool lostCarriedHarvest = currentHarvest != HarvestKind.None &&
            !currentHarvestBanked &&
            !shelterSafeHarvest;
        bool saltWoodReturnsToSea = extraSaltWoodOwner == ExtraSaltWoodOwner.PassingNearshore ||
            extraSaltWoodOwner == ExtraSaltWoodOwner.HookingToBoat ||
            extraSaltWoodOwner == ExtraSaltWoodOwner.HookedToBoat ||
            extraSaltWoodOwner == ExtraSaltWoodOwner.RoutedToNet ||
            extraSaltWoodOwner == ExtraSaltWoodOwner.CaughtInNet ||
            (extraSaltWoodOwner == ExtraSaltWoodOwner.Carried && lostCarriedHarvest);
        bool lostCargo = sailingRewardPending || saltWoodReturnsToSea || lostCarriedHarvest;
        if (shelterSafeHarvest)
        {
            // Material already laid beside a home repair remains at the shelter when
            // the player dies. Bank it before resetting the action state.
            BankCurrentHarvestMaterials();
        }
        if (saltWoodReturnsToSea)
        {
            if (extraSaltWoodOwner == ExtraSaltWoodOwner.HookingToBoat ||
                extraSaltWoodOwner == ExtraSaltWoodOwner.HookedToBoat)
            {
                ReleaseSailingSalvageTow();
            }
            else
            {
                ReturnExtraSaltWoodToSailingWater();
            }
            ResetCurrentTideRoutingAfterLoss();
        }
        bool securedPostBanked = BankSecuredPostHarvestMaterials();
        sailingRewardPending = false;
        sailingClueCollected = false;
        currentHarvest = HarvestKind.None;
        currentHarvestBatchId = 0;
        currentHarvestBanked = false;
        tideSourceHarvest = HarvestKind.None;
        primarySourcePassedNearshore = true;
        harvestPhysicalState = shelterSafeHarvest || securedPostBanked
            ? HarvestPhysicalState.Stored
            : lostCarriedHarvest ? HarvestPhysicalState.Lost : HarvestPhysicalState.None;
        harvestCarryTransition01 = 0f;
        harvestCarryStartPosition = Vector2.zero;
        harvestPlacedRepairChoice = RepairChoice.None;
        if (netDeployed)
        {
            netIntegrity = Mathf.Max(0, netIntegrity - 1);
        }

        netDeployed = false;
        netRigStep = NetRigStep.Stored;
        netUnrollProgress = 0f;
        netLoweringProgress = 0f;
        netHaulProgress = 0f;
        netHaulStrokePhase = 0f;
        netHaulEffort01 = 0f;
        netHaulLoad01 = 0f;
        editorNetHaulPreviewActive = false;
        netTouched = false;
        netCatchResolved = false;
        netCaptureProgress01 = 0f;
        netFraying01 = 0f;
        netDepthAdjustmentActive = false;
        netDepthAdjustmentDirection = 0f;
        netSecuredEarly = false;
        pendingRepairChoice = RepairChoice.None;
        repairWorkStep = 0;
        repairWorkProgress = 0f;
        repairWorkActive = false;

        sailTripActive = false;
        sailingWaterIngress01 = 0f;
        sailingBoatVelocity = 0f;
        sailingBoatWorldVelocity = 0f;
        mooringScreenActive = false;
        viewMode = SliceViewMode.Interior;
        state = SliceState.LowTidePlanning;
        stateTimer = 0f;
        float deathTimePenalty = dayLengthSeconds * 0.16f;
        dayClockSeconds = Mathf.Repeat(dayClockSeconds + deathTimePenalty, dayLengthSeconds);
        dayProgress01 = dayClockSeconds / Mathf.Max(1f, dayLengthSeconds);
        tideClockSeconds = Mathf.Repeat(tideClockSeconds + tideCycleSeconds * 0.18f, tideCycleSeconds);
        AdvanceContinuousWeather(deathTimePenalty);
        moonAgeDays = Mathf.Repeat(moonAgeDays + 0.16f, 29.53f);
        tideStrength = CalculateTideStrength(moonAgeDays);
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        playerLane = HasRegisteredHousePresentation()
            ? WalkLane.InteriorUpper
            : WalkLane.InteriorLoft;
        playerPosition = new Vector2(GetInteriorBedX(), GetPlayerLaneY(playerLane));
        playerHorizontalVelocity = 0f;
        playerSwimming = false;
        playerSubmersion01 = 0f;
        bodyWarmth01 = 0.58f;
        drowningSeconds = 0f;
        lastActionHint = $"死亡：{reason}。从最近一次屋内落脚处重来；{(lostCargo ? "未带回的材料和线索已丢失，" : string.Empty)}时间与潮位继续推进，设施损坏不会复原。";
    }

    private void TickMooredBoatCurrent(float deltaTime)
    {
        TideMooringRopeEnvironmentOutcome outcome = mooringRope.AdvanceEnvironment(
            deltaTime,
            GetNaturalCurrentSpeed(),
            GetNaturalSailingWindSpeed(),
            sailTripActive);
        mooredBoatOffsetFallback = mooringRope.BoatOffsetMeters;

        if (outcome == TideMooringRopeEnvironmentOutcome.RopeBroke)
        {
            lastActionHint = "浪和船速把绳子猛地绷断了；船仍在随潮漂，必须重新甩绳。";
        }
        else if (outcome == TideMooringRopeEnvironmentOutcome.BoatSecured)
        {
            lastActionHint = "船艉已经靠到跳板旁，绳长和相对速度都稳定下来，现在可以登船。";
        }
    }

    private void TickSailTrip(float deltaTime)
    {
        bool interactionHeld = Application.isPlaying && Input.GetKey(KeyCode.F);
        TickSailTrip(deltaTime, interactionHeld);
    }

    private void TickSailTrip(float deltaTime, bool interactionHeld)
    {
        if (!sailTripActive)
        {
            return;
        }

        sailTripTimer += deltaTime;
        // The visible current crests share the same integrated surface velocity as
        // floating cargo. Reversing tide or wind therefore slows and reverses them
        // continuously instead of flipping a decorative strip in place.
        sailingFlowCrestTravelWorld = Mathf.Repeat(
            sailingFlowCrestTravelWorld + GetSailingSurfaceFlowSpeed() * Mathf.Max(0f, deltaTime),
            13.8f);
        TickContinuousSailingSalvage(deltaTime, interactionHeld);
        TickSailingReefRuntime(deltaTime);
        if (!sailingIngressMidWarned && sailingWaterIngress01 >= 0.35f)
        {
            sailingIngressMidWarned = true;
            lastActionHint = "船底的旧缝开始积水，起速和回舵都变慢了；别把返航余量耗光。";
        }
        else if (!sailingIngressHighWarned && sailingWaterIngress01 >= 0.65f)
        {
            sailingIngressHighWarned = true;
            lastActionHint = "舱水已经压低船身，继续向外会越来越难返航。";
        }

        if (sailTripTimer > sailTripSeconds && !sailingClueCollected)
        {
            lastActionHint = "海面变暗了；目标在右边，但你也要记得靠左按 F 返航。";
        }
    }

    private float GetSailingLeakRatePerSecond()
    {
        float hullTightness01 = Mathf.Clamp01(boatHullIntegrity / 3f);
        float hullLeakRate = Mathf.Lerp(0.052f, 0.005f, hullTightness01);
        float speed01 = Mathf.Clamp01(
            Mathf.Abs(sailingBoatVelocity) /
            Mathf.Max(0.01f, GetEffectiveSailingMaxSpeed()));
        // A full sail drives the bow harder into chop. Reefing cannot repair a leaking
        // hull, but it lowers exposure. Keeping this formula shared makes the visible
        // ingress and the repair payoff read the exact same hull, tide and weather state.
        float sailExposure = Mathf.Lerp(0.58f, 1.28f, sailingSailTrim01);
        float weatherLoad = TideContinuousWeatherModel.EvaluateWaveLoadMultiplier(
            GetStormPressure01());
        float roughness =
            (0.82f + tideStrength * 0.32f + speed01 * 0.18f) *
            sailExposure * weatherLoad;
        return hullLeakRate * roughness;
    }

    private void TickSailingReefRuntime(float deltaTime)
    {
        sailingReef.AdvanceEnvironment(deltaTime);
    }

    private bool BeginContinuousSailingHookThrow()
    {
        if (viewMode != SliceViewMode.Sailing ||
            extraSaltWoodOwner != ExtraSaltWoodOwner.SailingWater)
        {
            return false;
        }

        Vector2 sternPosition = GetSailingBoatSternWorldPosition();
        Vector2 timberPosition = GetSailingPointPosition(SailingPointKind.Salvage);
        TideSailingSalvageThrowResult result = sailingSalvage.BeginThrow(
            sternPosition,
            timberPosition.y,
            sailingHookReach,
            sailingHookMaxRelativeSpeed,
            sailingBoatWorldVelocity);
        if (result.Started)
        {
            lastActionHint = "按住 F 抛钩；钩头命中后继续按住才会逐段收绳，松手会停。";
            return true;
        }

        if (result.Failure == TideSailingSalvageThrowFailure.OutOfReach)
        {
            lastActionHint = "浮木还在钩程外。先顺着它的尾流靠近，不要用提示范围代替真实距离。";
            return false;
        }

        if (result.Failure == TideSailingSalvageThrowFailure.AheadOfStern)
        {
            lastActionHint = "浮木还在船艏一侧。先越过它并收帆，让木束落到船艉后方，再从短缆处抛钩。";
            return false;
        }

        if (result.Failure == TideSailingSalvageThrowFailure.RelativeSpeedTooHigh)
        {
            // Failed throws must not become a hidden brake. The player has to use
            // sail trim and steering to match the drift before trying again.
            lastActionHint = sailingBoatWorldVelocity > sailingSalvageVelocity
                ? $"船从浮木旁冲得太快（相对速度 {result.RelativeSpeed:F1}）。收帆贴流后再按住 F 抛钩。"
                : $"漂物正顺流越过船艏（相对速度 {result.RelativeSpeed:F1}）。轻张帆追平后再抛钩。";
            return false;
        }

        return false;
    }

    private bool IsSailingSalvageInteractionActive()
    {
        return viewMode == SliceViewMode.Sailing &&
            (sailingHookThrowActive || extraSaltWoodOwner == ExtraSaltWoodOwner.HookingToBoat);
    }

    private TideSailingSalvageAttachmentPhase GetSailingSalvageAttachmentPhase()
    {
        if (extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater)
        {
            return TideSailingSalvageAttachmentPhase.Free;
        }

        if (extraSaltWoodOwner == ExtraSaltWoodOwner.HookingToBoat)
        {
            return TideSailingSalvageAttachmentPhase.Hooking;
        }

        return extraSaltWoodOwner == ExtraSaltWoodOwner.HookedToBoat
            ? TideSailingSalvageAttachmentPhase.Secured
            : TideSailingSalvageAttachmentPhase.Inactive;
    }

    private float GetCurrentSailingTowLoad01()
    {
        return sailingSalvage.EvaluateTowLoad01(GetSailingSalvageAttachmentPhase());
    }

    private Vector2 GetSailingBoatSternWorldPosition()
    {
        if (HasCompleteV39BoatPresentation())
        {
            TideOceanSample ocean = GetSailingOceanSample(sailingBoatX);
            float speed01 = Mathf.Clamp(
                sailingBoatVelocity / Mathf.Max(0.01f, GetEffectiveSailingMaxSpeed()),
                -1f,
                1f);
            float rotationZ = -speed01 * Mathf.Lerp(1.2f, 3.2f, sailingSailTrim01) +
                ocean.Slope * 5.5f;
            Vector2 root = new Vector2(
                sailingBoatX,
                TideV39BoatPresentationModel.EvaluateBoatRootY(
                    ocean.SurfaceY - sailingWaterIngress01 * 0.1f));
            return TideV39BoatPresentationModel.EvaluateAnchorWorldPosition(
                root,
                TideV39BoatPresentationModel.SternStepTopLeft,
                rotationZ,
                FormalBoatFacesRight);
        }

        // V31 回退船约 5.04m 宽，旧钩点位于可见船艉内侧。
        return GetSailingBoatBasePosition() + new Vector2(-2.05f, 0.1f);
    }

    private void TickContinuousSailingSalvage(float deltaTime, bool interactionHeld)
    {
        if (viewMode != SliceViewMode.Sailing || deltaTime <= 0f)
        {
            return;
        }

        Vector2 stern = GetSailingBoatSternWorldPosition();
        TideOceanSample salvageOcean = GetSailingOceanSample(sailingSalvageWorldX);
        TideSailingSalvageAdvanceResult result = sailingSalvage.Advance(
            deltaTime,
            interactionHeld,
            GetSailingSalvageAttachmentPhase(),
            stern,
            salvageOcean.SurfaceY - 0.01f,
            sailingSalvagePoint.x,
            salvageOcean,
            GetSailingSurfaceFlowSpeed(),
            GetNaturalSailingWindSpeed(),
            GetStormPressure01(),
            sailingWaterIngress01,
            sailingBoatWorldVelocity,
            sailingBoatVelocity);
        sailingBoatVelocity = result.ResolvedBoatVelocity;

        if (result.Outcome == TideSailingSalvageAdvanceOutcome.ThrowRetracted)
        {
            lastActionHint = "你松开绳，钩头收回船艉；浮木仍沿原来的水流漂。";
        }
        else if (result.Outcome == TideSailingSalvageAdvanceOutcome.HookAttached)
        {
            SetExtraSaltWoodOwner(ExtraSaltWoodOwner.HookingToBoat);
            lastActionHint = "钩头扣住湿绳结。继续按住 F 收绳；若绳拉得发白，就先收帆贴流。";
        }
        else if (result.Outcome == TideSailingSalvageAdvanceOutcome.Detached)
        {
            SetExtraSaltWoodOwner(ExtraSaltWoodOwner.SailingWater, true);
            lastActionHint = "绳被反向硬拽脱开了。浮木没有消失，仍从失手处顺流漂；收帆追平后可再试。";
        }
        else if (result.Outcome == TideSailingSalvageAdvanceOutcome.Secured)
        {
            SetExtraSaltWoodOwner(ExtraSaltWoodOwner.HookedToBoat);
            lastActionHint = "盐木已经逐段收进船艉短缆。拖带会减慢起速，但现在可以自己返航。";
        }
    }

    private float GetSailingSalvageRelativeSpeed()
    {
        return sailingSalvage.GetRelativeSpeed(sailingBoatWorldVelocity);
    }

    private void ReleaseSailingSalvageTow()
    {
        sailingSalvage.DetachPreservingWorld(GetSailingBoatSternWorldPosition());
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.SailingWater, true);
    }

    private void AdvanceDayNight(float deltaTime)
    {
        moonAgeDays = Mathf.Repeat(moonAgeDays + deltaTime / Mathf.Max(30f, dayLengthSeconds), 29.53f);
        tideStrength = CalculateTideStrength(moonAgeDays);
        dayClockSeconds = Mathf.Repeat(dayClockSeconds + deltaTime, Mathf.Max(4f, dayLengthSeconds));
        dayProgress01 = dayClockSeconds / Mathf.Max(4f, dayLengthSeconds);

        if (dayProgress01 >= 0.2f && dayProgress01 < 0.29f)
        {
            dayNightPhase = DayNightPhase.Dawn;
        }
        else if (dayProgress01 < 0.72f && dayProgress01 >= 0.29f)
        {
            dayNightPhase = DayNightPhase.Day;
        }
        else if (dayProgress01 >= 0.72f && dayProgress01 < 0.82f)
        {
            dayNightPhase = DayNightPhase.Dusk;
        }
        else
        {
            dayNightPhase = DayNightPhase.Night;
        }
    }

    private void AdvanceContinuousWeather(float deltaTime)
    {
        weatherClockSeconds = Mathf.Max(0f, weatherClockSeconds + Mathf.Max(0f, deltaTime));
    }

    private HarvestKind BuildHarvest()
    {
        if (netBrokeThisTide || netIntegrity <= 0)
        {
            return HarvestKind.Trash;
        }

        HarvestKind harvest = tideSourceHarvest != HarvestKind.None
            ? tideSourceHarvest
            : CalculateTideSourceHarvest();

        return harvest;
    }

    private bool HasRoutedLoadBonus()
    {
        return extraSaltWoodBundledWithNetHarvest &&
            (extraSaltWoodOwner == ExtraSaltWoodOwner.RoutedToNet ||
             extraSaltWoodOwner == ExtraSaltWoodOwner.CaughtInNet ||
             extraSaltWoodOwner == ExtraSaltWoodOwner.SecuredAtPost ||
             extraSaltWoodOwner == ExtraSaltWoodOwner.Carried ||
             extraSaltWoodOwner == ExtraSaltWoodOwner.PlacedAtWork);
    }

    private bool IsExtraSaltWoodInCurrentHarvest()
    {
        return extraSaltWoodOwner == ExtraSaltWoodOwner.CaughtInNet ||
            extraSaltWoodOwner == ExtraSaltWoodOwner.SecuredAtPost ||
            extraSaltWoodOwner == ExtraSaltWoodOwner.Carried ||
            extraSaltWoodOwner == ExtraSaltWoodOwner.PlacedAtWork;
    }

    private void SetExtraSaltWoodOwner(ExtraSaltWoodOwner owner, bool preserveSailingPosition = false)
    {
        bool enteringSailingWater = owner == ExtraSaltWoodOwner.SailingWater &&
            extraSaltWoodOwner != ExtraSaltWoodOwner.SailingWater;
        extraSaltWoodOwner = owner;

        if (enteringSailingWater && !preserveSailingPosition)
        {
            // The physical sea position is initialized exactly once, when the timber
            // first reaches the short-sailing sector. Re-entering the view or sleeping
            // must not reset a difficult drift into an easy center-point pickup.
            sailingSalvageWorldX = sailingSalvagePoint.x;
            sailingSalvageVelocity = 0f;
            sailingSalvageHookProgress = 0f;
        }

        // These fields predate the conservation model and are still read by the
        // existing boat/HUD renderers. They are projections only; the enum above is
        // the sole authority for where the physical timber currently lives.
        sailingSalvageCollected = owner != ExtraSaltWoodOwner.SailingWater;
        sailingSalvageRewardPending = owner == ExtraSaltWoodOwner.HookingToBoat ||
            owner == ExtraSaltWoodOwner.HookedToBoat;
        returnedSalvageAtBoat = owner == ExtraSaltWoodOwner.ReturnedAtBoat;
        sailingSalvageClaimed = owner == ExtraSaltWoodOwner.Claimed;
    }

    private void ReturnExtraSaltWoodToSailingWater()
    {
        extraSaltWoodBundledWithNetHarvest = false;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.SailingWater);
    }

    private void ReleaseUncaughtRoutedSaltWood()
    {
        if (extraSaltWoodOwner == ExtraSaltWoodOwner.RoutedToNet)
        {
            ReturnExtraSaltWoodToSailingWater();
        }

        ResetCurrentTideRoutingAfterLoss();
    }

    private void ReleaseNearshoreSaltWoodAfterTide()
    {
        if (extraSaltWoodOwner == ExtraSaltWoodOwner.PassingNearshore ||
            extraSaltWoodOwner == ExtraSaltWoodOwner.RoutedToNet)
        {
            ReturnExtraSaltWoodToSailingWater();
        }

        ResetCurrentTideRoutingAfterLoss();
    }

    private void ResetCurrentTideRoutingAfterLoss()
    {
        nearshoreWorkDone = false;
        tideRoutingMode = TideRoutingMode.Open;
        routingBoom01 = 0f;
        routingWorkActive = false;
        routingWorkDirection = 0f;
        routingDecisionLocked = false;
    }

    private void InitializeCurrentTideDriftField(bool resetTravel)
    {
        bool routeKnown = sailingWreckClueClaimed || lighthouseClues > 0 || routeClueReturnRound >= 0;
        int astronomicalCycleOrdinal = GetAstronomicalCycleOrdinal(weatherClockSeconds);
        currentTideDriftField = TideDriftSourceModel.BuildField(
            astronomicalCycleOrdinal,
            moonAgeDays,
            tideStrength,
            GetStormPressure01(),
            routeKnown);
        tideDriftFieldInitialized = currentTideDriftField.IsValid;
        tideDriftFieldCycleOrdinal = astronomicalCycleOrdinal;
        tideSourceHarvest = ToHarvestKind(currentTideDriftField.NearshoreBatch.Material);
        tideSourceBatchId = currentTideDriftField.NearshoreBatch.StableId;
        if (extraSaltWoodBatchId <= 0)
        {
            // 外海盐木是一件跨近岸、短航、系泊和维修状态保存的实物。
            // 它离开近岸后只换 owner，不因新一天或切换画面获得新的身份。
            extraSaltWoodBatchId = currentTideDriftField.OuterWreckBatch.StableId;
        }

        if (!resetTravel)
        {
            return;
        }

        incomingHarvestTravel01 = 0f;
        previousIncomingHarvestTravel01 = 0f;
        previousOuterWreckTravel01 = 0f;
        outerWreckTravel01 = 0f;
        outerNetCaptureProgress01 = 0f;
        primarySourcePassedNearshore = false;
        outerWreckPassedNearshore = false;
    }

    private void EnsureCurrentTideDriftField()
    {
        int astronomicalCycleOrdinal = GetAstronomicalCycleOrdinal(weatherClockSeconds);
        if (!tideDriftFieldInitialized ||
            tideDriftFieldCycleOrdinal != astronomicalCycleOrdinal)
        {
            InitializeCurrentTideDriftField(true);
        }
    }

    private bool TrySettleCurrentTideWrack()
    {
        if (wrackLine == null)
        {
            return false;
        }

        EnsureCurrentTideDriftField();
        bool captured = netCatchResolved ||
            currentHarvest != HarvestKind.None ||
            harvestPhysicalState == HarvestPhysicalState.CaughtInNet ||
            harvestPhysicalState == HarvestPhysicalState.SecuredAtPost;
        bool stillNearshore = !primarySourcePassedNearshore &&
            harvestPhysicalState == HarvestPhysicalState.Drifting &&
            !TideDriftSourceModel.HasExitedNearshore(incomingHarvestTravel01);
        return wrackLine.TrySettle(
            currentTideDriftField.NearshoreBatch,
            tideDriftFieldCycleOrdinal,
            highWaterMemory.CurrentCyclePeakY,
            GetPlayerStandingFeetY(WalkLane.TideFlat),
            captured,
            stillNearshore);
    }

    private void StartCurrentTideDrift()
    {
        EnsureCurrentTideDriftField();
        if (!primarySourcePassedNearshore &&
            harvestPhysicalState != HarvestPhysicalState.CaughtInNet &&
            harvestPhysicalState != HarvestPhysicalState.SecuredAtPost &&
            harvestPhysicalState != HarvestPhysicalState.Carried &&
            harvestPhysicalState != HarvestPhysicalState.PlacedAtWork &&
            harvestPhysicalState != HarvestPhysicalState.Stored)
        {
            harvestPhysicalState = HarvestPhysicalState.Drifting;
        }
    }

    private void ResolveExitedTideDriftBatches()
    {
        if (!primarySourcePassedNearshore &&
            harvestPhysicalState == HarvestPhysicalState.Drifting &&
            TideDriftSourceModel.HasExitedNearshore(incomingHarvestTravel01))
        {
            primarySourcePassedNearshore = true;
            harvestPhysicalState = HarvestPhysicalState.Lost;
            currentHarvestBatchId = 0;
        }

        bool exitsThroughOpenFork = extraSaltWoodOwner == ExtraSaltWoodOwner.PassingNearshore &&
            routingDecisionLocked &&
            tideRoutingMode == TideRoutingMode.Open &&
            TideDriftSourceModel.HasExitedOpenOuterRoute(outerWreckTravel01);
        bool exitsAtNearshoreBoundary = TideDriftSourceModel.HasExitedNearshore(outerWreckTravel01);
        if (outerWreckPassedNearshore ||
            (extraSaltWoodOwner != ExtraSaltWoodOwner.PassingNearshore &&
             extraSaltWoodOwner != ExtraSaltWoodOwner.RoutedToNet) ||
            (!exitsThroughOpenFork && !exitsAtNearshoreBoundary))
        {
            return;
        }

        // 导流失败和主动放行都通向同一片短航漂浮带。只有真正进入网眼的
        // 盐木才从这条链上消失，避免近岸和海上各复制一份。
        outerWreckPassedNearshore = true;
        ReturnExtraSaltWoodToSailingWater();
        ResetCurrentTideRoutingAfterLoss();
    }

    private void TryCatchRoutedSaltWoodInResolvedNet(float deltaTime)
    {
        if (!netDeployed ||
            !netCatchResolved ||
            currentHarvest == HarvestKind.None ||
            extraSaltWoodOwner != ExtraSaltWoodOwner.RoutedToNet)
        {
            outerNetCaptureProgress01 = 0f;
            return;
        }

        TideOceanSample netOcean = GetNetOceanSample();
        TideNetEncounterModel.Step encounter = TideNetEncounterModel.Advance(
            outerNetCaptureProgress01,
            deltaTime,
            previousOuterWreckTravel01,
            outerWreckTravel01,
            GetNetHeadLineY(),
            GetSelectedNetY(),
            netOcean.SurfaceY,
            netIntegrity / 4f,
            TideDriftMaterial.SaltWood,
            0.42f);
        outerNetCaptureProgress01 = encounter.Progress01;
        if (!encounter.Captured)
        {
            if (encounter.ContactLost)
            {
                lastActionHint = "导流盐木擦过浸水网缘，没有挂稳；它仍是同一根木料，继续进入短航水域。";
            }
            return;
        }

        extraSaltWoodBundledWithNetHarvest = true;
        SetExtraSaltWoodOwner(ExtraSaltWoodOwner.CaughtInNet);
        netCatchBundleTier = Mathf.Max(netCatchBundleTier, 2);
        netCatchVisualPieceCount = Mathf.Max(netCatchVisualPieceCount, 2);
        outerNetCaptureProgress01 = 1f;
        TideAudioController.PlayNetLoadCueInScene(netCatchBundleTier, false);
        lastActionHint = "导流盐木在自己的吃水高度上挂稳了，随后才成为网里的第二件实物负载。";
    }

    private static HarvestKind ToHarvestKind(TideDriftMaterial material)
    {
        switch (material)
        {
            case TideDriftMaterial.SaltWood:
                return HarvestKind.Wood;
            case TideDriftMaterial.ChartParcel:
                return HarvestKind.Relic;
            case TideDriftMaterial.TangledDebris:
                return HarvestKind.Trash;
            default:
                return HarvestKind.Fish;
        }
    }

    private TideDriftMaterial GetCurrentPrimaryDriftMaterial()
    {
        if (tideDriftFieldInitialized && currentTideDriftField.NearshoreBatch.IsValid)
        {
            return currentTideDriftField.NearshoreBatch.Material;
        }

        // Editor previews may project a HarvestKind without constructing a full tide
        // field. Keep that fallback deterministic; normal play always uses the batch.
        switch (tideSourceHarvest)
        {
            case HarvestKind.Wood:
                return TideDriftMaterial.SaltWood;
            case HarvestKind.Relic:
                return TideDriftMaterial.ChartParcel;
            case HarvestKind.Trash:
                return TideDriftMaterial.TangledDebris;
            default:
                return TideDriftMaterial.Fish;
        }
    }

    private static string GetTideDriftProvenanceName(TideDriftProvenance provenance)
    {
        switch (provenance)
        {
            case TideDriftProvenance.OuterWreckWrack:
                return "外海残骸带";
            case TideDriftProvenance.OuterWreckParcel:
                return "外海残骸高位";
            case TideDriftProvenance.StormWrackLine:
                return "风暴漂积线";
            default:
                return "近岸潮滩";
        }
    }

    private HarvestKind CalculateTideSourceHarvest()
    {
        EnsureCurrentTideDriftField();
        return ToHarvestKind(currentTideDriftField.NearshoreBatch.Material);
    }

    private void ApplyNetStress()
    {
        CommitAccumulatedNetStress();
    }

    private void CommitAccumulatedNetStress()
    {
        int targetStress = TideNetLoadLedgerModel.EvaluateCommittedStress(
            netAccumulatedTension,
            netPeakTension01,
            HasActiveTidePrep() && selectedPrepChoice == TidePrepChoice.Rope);
        int addedStress = Mathf.Max(0, targetStress - netOverloadStressApplied);
        if (addedStress <= 0)
        {
            return;
        }

        netOverloadStressApplied = targetStress;
        currentTideNetStress += addedStress;
        ApplyNetDamageWithFraying(
            addedStress,
            Mathf.Lerp(0.18f, 0.42f, GetNetFatigue01()));
    }

    private float GetNetFatigue01()
    {
        return TideNetLoadLedgerModel.EvaluateFatigue01(
            netAccumulatedTension,
            netPeakTension01);
    }

    private float GetHarvestLoadPaceMultiplier(HarvestKind harvest)
    {
        if (harvest == HarvestKind.Fish)
        {
            return 1.08f;
        }

        if (harvest == HarvestKind.Wood)
        {
            return 0.82f;
        }

        if (harvest == HarvestKind.Relic)
        {
            return 0.7f;
        }

        if (harvest == HarvestKind.Trash)
        {
            return 0.9f;
        }

        return 1f;
    }

    private float GetHarvestLoadTensionMultiplier(HarvestKind harvest, float time)
    {
        if (harvest == HarvestKind.Fish)
        {
            float kick = Mathf.Abs(Mathf.Sin(time * 5.4f));
            return 0.7f + kick * 0.2f;
        }

        if (harvest == HarvestKind.Wood)
        {
            float roll = 0.5f + Mathf.Sin(time * 1.15f) * 0.5f;
            return 1.12f + roll * 0.18f;
        }

        if (harvest == HarvestKind.Relic)
        {
            float snag = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(time * 2.4f)), 8f);
            return 0.74f + snag * 1.08f;
        }

        if (harvest == HarvestKind.Trash)
        {
            float drag = 0.5f + Mathf.Sin(time * 0.82f + 0.7f) * 0.5f;
            return 1.02f + drag * 0.08f;
        }

        return 1f;
    }

    private float GetCurrentNetMaterialPulse01(float time)
    {
        if (!netDeployed || !netCatchResolved || currentHarvest == HarvestKind.None)
        {
            return 0f;
        }

        // The four catches already produce distinct deterministic tension curves.
        // Visuals consume that same physical signal so fish kick, timber rolls,
        // relics snag and refuse drags without maintaining a second animation state.
        float tension = GetHarvestLoadTensionMultiplier(currentHarvest, time);
        return Mathf.InverseLerp(0.68f, 1.82f, tension);
    }

    private void ApplyNetDamageWithFraying(int damage, float initialFraying)
    {
        if (damage <= 0 || netBrokeThisTide)
        {
            return;
        }

        int damageBeforeLethal = Mathf.Min(damage, Mathf.Max(0, netIntegrity - 1));
        netIntegrity -= damageBeforeLethal;
        int lethalOverflow = damage - damageBeforeLethal;
        if (lethalOverflow <= 0)
        {
            return;
        }

        // The final rope strand remains physically present. Excess damage becomes a
        // readable rescue window instead of deleting the net and cargo in one frame.
        netIntegrity = Mathf.Max(1, netIntegrity);
        netFraying01 = Mathf.Max(
            netFraying01,
            Mathf.Clamp01(initialFraying + Mathf.Max(0, lethalOverflow - 1) * 0.16f));
        netBrokeThisTide = false;
    }

    private void TickNetFraying(float deltaTime, bool activelyHauling)
    {
        if (netFraying01 <= 0f || netBrokeThisTide || !netDeployed)
        {
            return;
        }

        if (activelyHauling)
        {
            float rescueRate = Mathf.Lerp(0.48f, 0.82f, Mathf.Max(0.25f, netHaulEffort01));
            netFraying01 = Mathf.Max(0f, netFraying01 - Mathf.Max(0f, deltaTime) * rescueRate);
            return;
        }

        float load01 = Mathf.InverseLerp(1f, 3f, netCatchBundleTier);
        float current01 = Mathf.Lerp(0.35f, 1f, GetNaturalCurrentStrength01());
        float materialLoad = GetHarvestLoadTensionMultiplier(
            currentHarvest,
            tideClockSeconds + netPostCatchExposureSeconds);
        float depthRelief = Mathf.Lerp(0.6f, 1.35f, netSetDepth01);
        float worsenRate = (0.12f + load01 * 0.12f + Mathf.Max(0f, materialLoad - 0.72f) * 0.1f) *
            current01 * depthRelief;
        netFraying01 = Mathf.Clamp01(netFraying01 + Mathf.Max(0f, deltaTime) * worsenRate);
        if (netFraying01 < 0.999f)
        {
            return;
        }

        netIntegrity = 0;
        netBrokeThisTide = true;
        BeginBrokenNetResidue(currentHarvest, netCatchBundleTier);
        TideAudioController.PlayNetLoadCueInScene(1, true);
        lastActionHint = "最后一股旧绳终于崩开：原来的挂物被潮流冲散，断网上只剩缠网废料。";
    }

    private int CalculateNetStress()
    {
        return TideNetLoadLedgerModel.EvaluateCommittedStress(
            netAccumulatedTension,
            netPeakTension01,
            false);
    }

    private int CalculatePrepAdjustedNetStress()
    {
        return TideNetLoadLedgerModel.EvaluateCommittedStress(
            netAccumulatedTension,
            netPeakTension01,
            HasActiveTidePrep() && selectedPrepChoice == TidePrepChoice.Rope);
    }

    private void ApplyHarvest()
    {
        bool hadHarvest = currentHarvest != HarvestKind.None;
        if (extraSaltWoodOwner == ExtraSaltWoodOwner.RoutedToNet)
        {
            // Ebb hauling removes the net just like an early haul. A routed piece that
            // never reached the mesh remains a real offshore target rather than being
            // silently held by a route owner with no net left in the world.
            ReleaseUncaughtRoutedSaltWood();
        }
        harvestCarryStartPosition = new Vector2(
            netAnchor.x - 0.42f,
            GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.22f);
        harvestCarryTransition01 = 0f;
        harvestPlacementTransition01 = 0f;
        harvestPlacedRepairChoice = RepairChoice.None;
        state = SliceState.RepairMoment;
        harvestPhysicalState = hadHarvest
            ? HarvestPhysicalState.Carried
            : HarvestPhysicalState.None;
        if (hadHarvest &&
            (extraSaltWoodOwner == ExtraSaltWoodOwner.CaughtInNet ||
             extraSaltWoodOwner == ExtraSaltWoodOwner.SecuredAtPost))
        {
            SetExtraSaltWoodOwner(ExtraSaltWoodOwner.Carried);
        }
        stateTimer = 0f;
        // Hauling has physically brought the net back to its post. Leaving this
        // true made the full wet net pop back into the sea on the repair frame.
        netDeployed = false;
        netSecuredEarly = false;
        netRigStep = NetRigStep.Stored;
        netUnrollProgress = 0f;
        netLoweringProgress = 0f;
        netHaulProgress = 0f;
        netHaulStrokePhase = 0f;
        netHaulEffort01 = 0f;
        netHaulLoad01 = 0f;
        netFraying01 = 0f;
        netDepthAdjustmentActive = false;
        netDepthAdjustmentDirection = 0f;
        editorNetHaulPreviewActive = false;
        // A dry, untouched net has nothing to spend. Mark the repair choice as
        // resolved so the player can simply put it away and sleep into the next tide.
        repairChoiceApplied = !hadHarvest;
        pendingRepairChoice = RepairChoice.None;
        repairWorkStep = 0;
        repairWorkProgress = 0f;
        repairWorkActive = false;
        // 夜间准备只服务一张网。潮获落袋后工具需要重新整理，避免一次选择永久生效。
        if (HasActiveTidePrep())
        {
            tidePrepReady = false;
            tidePrepTargetRound = -1;
            selectedPrepChoice = TidePrepChoice.None;
        }
        lastActionHint = hadHarvest
            ? $"已回收{GetHarvestName()}：{GetHarvestPayoffText()} 它现在还在手上；带到真实部位按住 F，或带回生活层储物架存下。"
            : "网没有碰水，也没有收获。你把干网卷回架上；回屋内床边休息，等下一次真正够得着网的潮。";
    }

    private void BankCurrentHarvestMaterials()
    {
        if (currentHarvestBanked || currentHarvest == HarvestKind.None)
        {
            return;
        }

        int timberYield;
        int ropeYield;
        int clothYield;
        int metalYield;
        int foodYield;
        bool claimsExtraSaltWood = IsExtraSaltWoodInCurrentHarvest();
        GetCurrentHarvestMaterialYield(out timberYield, out ropeYield, out clothYield, out metalYield, out foodYield);
        timberStock += timberYield;
        ropeStock += ropeYield;
        clothStock += clothYield;
        metalStock += metalYield;
        foodStock += foodYield;

        currentHarvestBanked = true;
        if (claimsExtraSaltWood)
        {
            hasSalvageBag = true;
            extraSaltWoodBundledWithNetHarvest = false;
            SetExtraSaltWoodOwner(ExtraSaltWoodOwner.Claimed);
        }
        currentHarvestFromWrack = false;
    }

    private void GetCurrentHarvestMaterialYield(
        out int timberYield,
        out int ropeYield,
        out int clothYield,
        out int metalYield,
        out int foodYield)
    {
        timberYield = 0;
        ropeYield = 0;
        clothYield = 0;
        metalYield = 0;
        foodYield = 0;

        bool includesExtraSaltWood = IsExtraSaltWoodInCurrentHarvest();
        bool saltWoodIsTheWholeCargo = includesExtraSaltWood &&
            !extraSaltWoodBundledWithNetHarvest;
        int tier = currentHarvestFromWrack
            ? 0
            : Mathf.Clamp(
                netCatchBundleTier - (extraSaltWoodBundledWithNetHarvest && includesExtraSaltWood ? 1 : 0),
                1,
                3);
        if (!saltWoodIsTheWholeCargo && currentHarvest == HarvestKind.Wood)
        {
            timberYield = 1 + tier;
            ropeYield = tier > 0 ? 1 : 0;
        }
        else if (!saltWoodIsTheWholeCargo && currentHarvest == HarvestKind.Fish)
        {
            foodYield = 1 + tier;
        }
        else if (!saltWoodIsTheWholeCargo && currentHarvest == HarvestKind.Relic)
        {
            clothYield = 1 + Mathf.CeilToInt(tier * 0.5f);
            ropeYield = tier > 0 ? 1 : 0;
            metalYield = tier >= 2 ? 1 : 0;
        }
        else if (!saltWoodIsTheWholeCargo && currentHarvest == HarvestKind.Trash)
        {
            ropeYield = 1;
            metalYield = tier >= 2 ? 1 : 0;
        }

        if (includesExtraSaltWood)
        {
            timberYield += 2;
            // The wreck timber reaches the boat as a lashed bundle, not two clean
            // planks. Its surviving wet cordage is what makes the first real post
            // brace possible without inventing a free rope in the starting inventory.
            ropeYield += 1;
            clothYield += 1;
        }
    }

    private string GetRepairWorkInstruction(RepairChoice choice, int step)
    {
        if (step == (int)TideRepairWorkPhase.Clean)
        {
            return GetRepairCleaningInstruction(choice);
        }
        if (step >= (int)TideRepairWorkPhase.Seal)
        {
            return GetRepairSealingInstruction(choice);
        }

        if (choice == RepairChoice.Stilt)
        {
            return step <= 1
                ? "刮掉柱脚软木，找到真正承重的裂口。"
                : step == 3
                    ? "把盐木楔进裂口，顶起并校正斜撑。"
                    : "钻穿补木与旧桩，收紧湿绳，再检查平台是否回平。";
        }

        if (choice == RepairChoice.Cistern)
        {
            return step <= 1
                ? "沿水迹找出仍在渗漏的裂缝，确认池壁没有继续张开。"
                : step == 3
                    ? "剪整铆接板，让它跨过裂口并贴合池壁弧面。"
                    : "穿孔压紧补片，填实板缘，再观察池外是否继续挂水。";
        }

        if (choice == RepairChoice.Net)
        {
            return step <= 1
                ? "用淡水冲掉泥沙，沿浮纲和沉纲逐眼查裂口。"
                : step == 3
                    ? "把旧纤维重新捻紧，照原网目逐结补片。"
                    : "拉开整张网检查受力，剪掉会继续散开的毛边。";
        }

        if (choice == RepairChoice.Roof)
        {
            return step <= 1
                ? "从屋内追着水痕，找出滴水缝和松动压条。"
                : step == 3
                    ? "替换漏雨板，让新板压在旧板的顺水方向。"
                    : "钉住压条并封边，再泼水确认没有倒灌。";
        }

        if (choice == RepairChoice.InteriorSeal)
        {
            return step <= 1
                ? "沿雨痕和盐霜检查墙缝、门槛与迎风面的漏口。"
                : step == 3
                    ? "裁出顺水补板，先压旧布，再把门框校回方正。"
                    : "钉紧压条并留出木料伸缩缝，从外侧泼水复查渗漏。";
        }

        if (choice == RepairChoice.Workbench)
        {
            return step <= 1
                ? "刮掉台面软木，检查松腿、断榫和还能承力的横撑。"
                : step == 3
                    ? "削平替换木，补上缺腿并让台面重新找平。"
                    : "穿绳锁住榫口，压重物确认锯切和敲击时不会晃动。";
        }

        if (choice == RepairChoice.Bed)
        {
            return step <= 1
                ? "清掉湿霉和虫鼠碎屑，拆出还能留下的干燥床条。"
                : step == 3
                    ? "铺平睡板，用旧布做隔潮层，把身体抬离湿地板。"
                    : "拉紧床布并固定边角，躺下检查承重处不会突然塌落。";
        }

        if (choice == RepairChoice.ChartRadio)
        {
            return step <= 1
                ? "擦掉盐壳，分开海图、潮尺与锈住的无线电接点。"
                : step == 3
                    ? "按旧航迹校准潮尺，用金属片接回断开的触点。"
                    : "对照月相和桩上湿线复核读数，把下一潮窗口标在图边。";
        }

        if (choice == RepairChoice.Lamp)
        {
            return step <= 1
                ? "掏净冷灰、通烟道，检查灶门和火床裂缝。"
                : step == 3
                    ? "试装灶门，重新垫平能托住火的石块。"
                    : "用湿泥填实漏烟缝，点小火确认烟确实往上走。";
        }

        if (choice == RepairChoice.Hull)
        {
            if (boatHullIntegrity >= 2)
            {
                return step <= 1
                    ? "擦干初补后的船缝，找出负重后仍会渗水和冒泡的位置。"
                    : step == 3
                        ? "把细麻重新捻进渗缝，薄涂油脂并压紧边缘。"
                        : "装入压舱物做吃水复查，确认补板和钉排都没有松动。";
            }
            return step <= 1
                ? "舀干舱水，从内侧找出透光和渗水的船缝。"
                : step == 3
                    ? "塞入捻开的麻绳，试合补板并校正船壳弧度。"
                    : "从内侧压紧钉牢，抹上油脂后再下水看是否冒泡。";
        }

        if (choice == RepairChoice.Cabin)
        {
            if (boatCabinIntegrity >= 1)
            {
                return step <= 1
                    ? "搬动现有货物，检查舱盖边缘、桶座和排水槽的受力痕。"
                    : step == 3
                        ? "给舱盖加挡水沿，补齐绑货点并留出舀水通道。"
                        : "装入同等重量摇船复查，确认货物、积水都不会偏到一舷。";
            }
            return step <= 1
                ? "清空舱底，量出舱盖、压舱格和排水位置。"
                : step == 3
                    ? "试装舱盖、桶座和不会挡住舀水的绑货横木。"
                    : "穿绳固定各处，摇船检查货物不会滑向一边。";
        }

        if (boatSailIntegrity >= 1)
        {
            return step <= 1
                ? "把初补帆升到半高，检查缝线、帆角和夹条有没有继续走形。"
                : step == 3
                    ? "在受力边加一层折边搭接，补齐帆脚绳和磨损索环。"
                    : "迎着小风逐段张紧，收放两次确认补口不会再次撕开。";
        }

        return step <= 1
            ? "拆掉腐线，摊平破帆并检查帆骨和受力边。"
            : step == 3
                ? "把补布压在背风面，从裂口两端交叉走线。"
                : "逐段张紧帆脚与升帆索，收一档确认补口不会再撕。";
    }

    private string GetRepairCleaningInstruction(RepairChoice choice)
    {
        if (choice == RepairChoice.Cistern)
        {
            return "把裂口外侧的盐霜、浮锈和湿藻刮净，擦到硬实池壁；污物不能掉回剩余淡水。";
        }
        if (choice == RepairChoice.Net || choice == RepairChoice.Sail)
        {
            return "先用少量淡水洗去盐泥，拆掉失去强度的腐线，再把纤维摊开晾到不滴水。";
        }
        if (choice == RepairChoice.Hull || choice == RepairChoice.Cabin)
        {
            return "舀走积水，刮净松动木屑、锈钉和盐壳，让接触面露出仍能承力的干净材料。";
        }
        if (choice == RepairChoice.ChartRadio)
        {
            return "用干布和细刷分开盐壳与锈蚀，擦干触点；潮湿时不接通任何电路。";
        }
        if (choice == RepairChoice.Lamp)
        {
            return "清空冷灰和烟道积碳，刷掉灶门浮锈，把仍会漏烟的裂缝全部露出来。";
        }
        return "去掉软烂木、霉层和盐壳，晾干接触面；没有清到硬实材料前不放新部件。";
    }

    private string GetRepairSealingInstruction(RepairChoice choice)
    {
        if (choice == RepairChoice.Cistern)
        {
            return "从雨槽缓慢放一小股水，沿补片四边摸查渗漏；池外不再挂水才算封住。";
        }
        if (choice == RepairChoice.Stilt || choice == RepairChoice.Workbench || choice == RepairChoice.Bed)
        {
            return "逐步加上真实负重，观察接缝和支点；确认不再位移后才收走工具与余料。";
        }
        if (choice == RepairChoice.Net)
        {
            return "把网浸湿后重新张紧，逐段摸查跳线和松结；受流后不继续散开才算完成。";
        }
        if (choice == RepairChoice.Roof || choice == RepairChoice.InteriorSeal)
        {
            return "沿顺水方向压实边缝，从外侧少量泼水；屋内不再出现新水痕才算密封。";
        }
        if (choice == RepairChoice.Hull)
        {
            return "在补缝处压入油麻并薄涂防水脂，下水负重观察；没有连续冒泡才算密封。";
        }
        if (choice == RepairChoice.Sail)
        {
            return "迎小风分档升降两次，复查受力边和索环；缝口不再扩张才收紧最后一道线。";
        }
        if (choice == RepairChoice.Cabin)
        {
            return "装回等重货物并左右摇船，确认舱盖挡水、排水槽畅通且载荷不会滑向一舷。";
        }
        if (choice == RepairChoice.ChartRadio)
        {
            return "接通后对照月相、湿线和旧航迹复核读数；三处相符才记录下一次潮窗。";
        }
        return "先点一小团火检查抽烟和裂缝，确认烟只进烟道、火床不掉灰后再逐步添柴。";
    }

    private bool TryConsumeRepairMaterials(RepairChoice choice, out string missing)
    {
        int timberNeed;
        int ropeNeed;
        int clothNeed;
        int metalNeed;
        int foodNeed;
        GetRepairMaterialNeeds(choice, out timberNeed, out ropeNeed, out clothNeed, out metalNeed, out foodNeed);
        if (!HasRepairMaterials(choice, out missing))
        {
            return false;
        }

        // A catch remains vulnerable cargo while it is visibly being carried. It is
        // only added to the shelter stock when the player commits it to a real repair.
        BankCurrentHarvestMaterials();

        TideMaterialBundle needs = new TideMaterialBundle(
            timberNeed,
            ropeNeed,
            clothNeed,
            metalNeed,
            foodNeed);
        TideMaterialBundle secured = GetSecuredMaterialBundle();
        TideIslandSalvageDestination stagingDestination = GetStagingDestinationForRepair(choice);
        int stagedMask = GetRepairStagedPartMask(choice, stagingDestination);
        int selectedMask = TideSalvageMaterialModel.SelectMinimumParts(stagedMask, secured, needs);
        if (selectedMask < 0 || !TryIntegrateSelectedSalvageParts(selectedMask, stagingDestination))
        {
            // HasRepairMaterials uses the same selector, so this branch only protects
            // a future caller that mutates the worksite between preview and commit.
            missing = "施工位上的原物已被移走，无法完成最终固定";
            return false;
        }

        timberStock -= timberNeed;
        ropeStock -= ropeNeed;
        clothStock -= clothNeed;
        metalStock -= metalNeed;
        foodStock -= foodNeed;
        missing = string.Empty;
        return true;
    }

    private bool HasRepairMaterials(RepairChoice choice, out string missing)
    {
        int timberNeed;
        int ropeNeed;
        int clothNeed;
        int metalNeed;
        int foodNeed;
        GetRepairMaterialNeeds(choice, out timberNeed, out ropeNeed, out clothNeed, out metalNeed, out foodNeed);
        int availableTimber = timberStock;
        int availableRope = ropeStock;
        int availableCloth = clothStock;
        int availableMetal = metalStock;
        int availableFood = foodStock;
        if (!currentHarvestBanked && currentHarvest != HarvestKind.None)
        {
            int timberYield;
            int ropeYield;
            int clothYield;
            int metalYield;
            int foodYield;
            GetCurrentHarvestMaterialYield(out timberYield, out ropeYield, out clothYield, out metalYield, out foodYield);
            availableTimber += timberYield;
            availableRope += ropeYield;
            availableCloth += clothYield;
            availableMetal += metalYield;
            availableFood += foodYield;
        }

        TideMaterialBundle availableMaterials = new TideMaterialBundle(
            availableTimber,
            availableRope,
            availableCloth,
            availableMetal,
            availableFood);
        TideMaterialBundle needs = new TideMaterialBundle(
            timberNeed,
            ropeNeed,
            clothNeed,
            metalNeed,
            foodNeed);
        TideIslandSalvageDestination stagingDestination = GetStagingDestinationForRepair(choice);
        int stagedMask = GetRepairStagedPartMask(choice, stagingDestination);
        bool available = TideSalvageMaterialModel.SelectMinimumParts(
            stagedMask,
            availableMaterials,
            needs) >= 0;
        missing = available
            ? string.Empty
            : $"需要 木{timberNeed}/绳{ropeNeed}/布{clothNeed}/铁{metalNeed}/食{foodNeed}";
        return available;
    }

    private TideMaterialBundle GetSecuredMaterialBundle()
    {
        return new TideMaterialBundle(timberStock, ropeStock, clothStock, metalStock, foodStock);
    }

    private TideIslandSalvageDestination GetStagingDestinationForRepair(RepairChoice choice)
    {
        return TideRepairRecipeModel.GetStagingDestination(choice);
    }

    private int GetRepairStagedPartMask(
        RepairChoice choice,
        TideIslandSalvageDestination stagingDestination)
    {
        int mask = barrenIsland != null
            ? barrenIsland.GetStagedPartMask(stagingDestination)
            : 0;

        // Curved keel ribs remain purpose-shaped objects. They may become a shelter
        // diagonal brace or a boat hull rib, but cannot silently turn into net cord,
        // sail cloth, a stove, furniture, or food through the generic material selector.
        bool acceptsCurvedRib = choice == RepairChoice.Stilt || choice == RepairChoice.Hull;
        if (acceptsCurvedRib && heavyWreckSalvage != null)
        {
            mask |= heavyWreckSalvage.GetStagedPartMask(stagingDestination);
        }
        return mask;
    }

    private bool TryIntegrateSelectedSalvageParts(
        int selectedMask,
        TideIslandSalvageDestination stagingDestination)
    {
        if (selectedMask == 0)
        {
            return true;
        }

        int islandMask = barrenIsland != null
            ? barrenIsland.GetStagedPartMask(stagingDestination)
            : 0;
        int heavyMask = heavyWreckSalvage != null
            ? heavyWreckSalvage.GetStagedPartMask(stagingDestination)
            : 0;
        if (((islandMask | heavyMask) & selectedMask) != selectedMask)
        {
            return false;
        }

        for (int partIndex = 1; partIndex <= 3; partIndex++)
        {
            TideIslandSalvagePart part = (TideIslandSalvagePart)partIndex;
            if ((selectedMask & TideSalvageMaterialModel.GetPartBit(part)) == 0)
            {
                continue;
            }

            if (!barrenIsland.TryIntegrateStagedPart(part, stagingDestination))
            {
                return false;
            }

            TideMaterialBundle materialYield = TideSalvageMaterialModel.GetYield(part);
            timberStock += materialYield.Timber;
            ropeStock += materialYield.Rope;
            clothStock += materialYield.Cloth;
            metalStock += materialYield.Metal;
            foodStock += materialYield.Food;
        }

        for (int pieceIndex = 1; pieceIndex <= 2; pieceIndex++)
        {
            TideHeavyWreckPiece piece = (TideHeavyWreckPiece)pieceIndex;
            if ((selectedMask & TideSalvageMaterialModel.GetHeavyPieceBit(piece)) == 0)
            {
                continue;
            }

            if (heavyWreckSalvage == null ||
                !heavyWreckSalvage.TryIntegrateStagedPiece(piece, stagingDestination))
            {
                return false;
            }

            TideMaterialBundle materialYield = TideSalvageMaterialModel.GetYield(piece);
            timberStock += materialYield.Timber;
            ropeStock += materialYield.Rope;
            clothStock += materialYield.Cloth;
            metalStock += materialYield.Metal;
            foodStock += materialYield.Food;
        }

        return true;
    }

    private void GetRepairMaterialNeeds(
        RepairChoice choice,
        out int timberNeed,
        out int ropeNeed,
        out int clothNeed,
        out int metalNeed,
        out int foodNeed)
    {
        TideMaterialBundle needs = TideRepairRecipeModel.GetMaterialNeeds(
            choice,
            boatCabinIntegrity);
        timberNeed = needs.Timber;
        ropeNeed = needs.Rope;
        clothNeed = needs.Cloth;
        metalNeed = needs.Metal;
        foodNeed = needs.Food;
    }

    private string GetMaterialStockText()
    {
        string secured = $"木{timberStock} 绳{ropeStock} 布{clothStock} 铁{metalStock} 食{foodStock}";
        if (currentHarvestBanked || currentHarvest == HarvestKind.None)
        {
            return secured;
        }

        int timberYield;
        int ropeYield;
        int clothYield;
        int metalYield;
        int foodYield;
        GetCurrentHarvestMaterialYield(out timberYield, out ropeYield, out clothYield, out metalYield, out foodYield);
        return $"{secured}；手上 木{timberYield} 绳{ropeYield} 布{clothYield} 铁{metalYield} 食{foodYield}";
    }

    private string ApplyRepairChoiceEffect(RepairChoice choice)
    {
        if (choice == RepairChoice.Stilt)
        {
            stiltIntegrity = Mathf.Min(3, stiltIntegrity + 1);
            netIntegrity = Mathf.Min(3, netIntegrity + 1);
            return "盐木被削掉软烂边，楔进承重桩后钻孔穿绳并加斜撑；地基多撑一截，也补了一段破网。";
        }

        if (choice == RepairChoice.Cistern)
        {
            bool patched = barrenIsland != null && barrenIsland.ApplyCisternPlatePatch();
            return patched
                ? "铆接板跨住主裂口，压条和旧铆钉把边缘锁回池壁；同样一池雨水现在会留得更久。"
                : "池壁裂口已经封住；没有重复生成第二块补片。";
        }

        if (choice == RepairChoice.Net)
        {
            netIntegrity = Mathf.Min(3, netIntegrity + 1);
            return "湿网先用淡水冲掉泥沙，再沿受力边逐眼查裂口；拆出的绳纤维被重新捻紧结回网身。";
        }

        if (choice == RepairChoice.Roof)
        {
            roofIntegrity = Mathf.Min(2, roofIntegrity + 1);
            return "薄木被劈成压条，换掉漏雨板并压住锈蚀屋面；夜里少进一层冷雨。";
        }

        if (choice == RepairChoice.InteriorSeal)
        {
            interiorSealCondition = 1;
            RefreshInteriorComfort();
            houseWarmth++;
            return "顺水补板压住迎风墙缝，门框也重新回正；暴雨穿进生活层的冷风少了一截。";
        }

        if (choice == RepairChoice.Workbench)
        {
            workbenchCondition = 1;
            return "腐烂台腿和断榫被换掉，台面重新找平并穿绳锁紧；之后的修补可以在稳固工作面上完成。";
        }

        if (choice == RepairChoice.Bed)
        {
            bedCondition = 1;
            RefreshInteriorComfort();
            houseWarmth++;
            return "床条被重新排平，旧布隔开潮湿地板；睡眠不再只是倒在冷木板上熬到天亮。";
        }

        if (choice == RepairChoice.ChartRadio)
        {
            chartRadioCondition = 1;
            lampForecastCharges = Mathf.Max(1, lampForecastCharges);
            CaptureChartForecast();
            return "潮尺重新校准，海图与无线电接点也被清理接回；每天黎明能留下下一潮的可靠窗口。";
        }

        if (choice == RepairChoice.Hull)
        {
            boatHullIntegrity = Mathf.Min(3, boatHullIntegrity + 1);
            RecalculateBoatReadiness();
            return "盐木被刨成舷板，先塞麻绳止缝，再从内侧压板钉牢；船底进水会慢下来。";
        }

        if (choice == RepairChoice.Sail)
        {
            boatSailIntegrity = Mathf.Min(2, boatSailIntegrity + 1);
            RecalculateBoatReadiness();
            return "拆掉腐线后，补布压在背风面重新走线，断帆骨也被木条夹住；帆面不再整片漏风。";
        }

        if (choice == RepairChoice.Cabin)
        {
            bool firstCabinRepair = boatCabinIntegrity <= 0;
            boatCabinIntegrity = Mathf.Min(2, boatCabinIntegrity + 1);
            RecalculateBoatReadiness();
            return firstCabinRepair
                ? "铆接板被剪成低矮舱盖、排水口护板和压舱隔片；积水能舀出，货物也不再直接泡在舱底。"
                : "木框撑住舱盖并补齐绑货点和固定桶座；远航载荷不再随每一道浪滑向一舷。";
        }

        stoveCondition = Mathf.Min(2, stoveCondition + 1);
        houseWarmth += 2;
        return "火床清灰、烟道通开，灶门填实漏烟泥缝；热食和干燥层现在能更快恢复体温。";
    }

    private void RefreshInteriorComfort()
    {
        // Older fallback presentations still consume a two-step room value. Keep it
        // as a compatibility aggregate while V35 gameplay and owners remain separate.
        interiorComfort = Mathf.Clamp(interiorSealCondition + bedCondition, 0, 2);
    }

    private bool HasExplicitInteriorComponentState()
    {
        return interiorSealCondition > 0 || workbenchCondition > 0 ||
            bedCondition > 0 || chartRadioCondition > 0;
    }

    private int GetInteriorSealVisualLevel()
    {
        return HasExplicitInteriorComponentState()
            ? interiorSealCondition
            : interiorComfort >= 1 ? 1 : 0;
    }

    private int GetInteriorBedVisualLevel()
    {
        return HasExplicitInteriorComponentState()
            ? bedCondition
            : interiorComfort >= 2 ? 1 : 0;
    }

    private void RecalculateBoatReadiness()
    {
        boatReadiness = Mathf.Min(
            requiredBoatReadiness,
            Mathf.Max(0, boatHullIntegrity - 1) + boatSailIntegrity + boatCabinIntegrity);
    }

    private float GetBoatRestoration01()
    {
        float hull01 = Mathf.Clamp01((boatHullIntegrity - 1f) / 2f);
        float sail01 = Mathf.Clamp01(boatSailIntegrity / 2f);
        float cabin01 = Mathf.Clamp01(boatCabinIntegrity / 2f);
        return (hull01 + sail01 + cabin01) / 3f;
    }

    private float GetBoatRestorationVisual01()
    {
        float hull01 = Mathf.Clamp01((boatHullIntegrity - 1f + GetUncommittedRepairPreview01(RepairChoice.Hull)) / 2f);
        float sail01 = Mathf.Clamp01((boatSailIntegrity + GetUncommittedRepairPreview01(RepairChoice.Sail)) / 2f);
        float cabin01 = Mathf.Clamp01((boatCabinIntegrity + GetUncommittedRepairPreview01(RepairChoice.Cabin)) / 2f);
        return (hull01 + sail01 + cabin01) / 3f;
    }

    private float GetRepairReveal01()
    {
        if (state == SliceState.RepairMoment &&
            !repairChoiceApplied &&
            pendingRepairChoice != RepairChoice.None)
        {
            return GetRepairPreview01(pendingRepairChoice);
        }

        // Completion is the end of the player's physical work, not the beginning of
        // a second automatic animation. Permanent repair visuals remain fully shown.
        return 1f;
    }

    private float GetRepairPreview01(RepairChoice choice)
    {
        if (state != SliceState.RepairMoment || pendingRepairChoice != choice)
        {
            return 0f;
        }

        return repairChoiceApplied
            ? 1f
            : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(repairWorkProgress));
    }

    private float GetUncommittedRepairPreview01(RepairChoice choice)
    {
        return repairChoiceApplied ? 0f : GetRepairPreview01(choice);
    }

    private void AdvanceMoonPhase()
    {
        survivalPresentationState = SurvivalPresentationState.None;
        survivalPresentationTimer = 0f;
        pendingDeathReason = string.Empty;
        // Sleeping is only possible beside the loft bed. Reaching it with an
        // unspent catch therefore means the bundle was physically brought into the
        // dry shelter and can survive the night in storage.
        bool abandonedSaltWoodOutside = extraSaltWoodOwner == ExtraSaltWoodOwner.PassingNearshore ||
            extraSaltWoodOwner == ExtraSaltWoodOwner.RoutedToNet ||
            extraSaltWoodOwner == ExtraSaltWoodOwner.CaughtInNet;
        if (abandonedSaltWoodOutside)
        {
            ReturnExtraSaltWoodToSailingWater();
            currentHarvest = HarvestKind.None;
            currentHarvestBanked = false;
            harvestPhysicalState = HarvestPhysicalState.Lost;
            ResetCurrentTideRoutingAfterLoss();
        }
        bool storedCarriedHarvest = !currentHarvestBanked && currentHarvest != HarvestKind.None;
        BankCurrentHarvestMaterials();
        bool storedPostHarvest = BankSecuredPostHarvestMaterials();
        if (storedCarriedHarvest || storedPostHarvest)
        {
            harvestPhysicalState = HarvestPhysicalState.Stored;
            harvestPlacedRepairChoice = RepairChoice.None;
        }
        float skippedSeconds = GetSleepSkipSeconds();
        float normalizedSleep = skippedSeconds / Mathf.Max(1f, dayLengthSeconds);
        string overnightShelterText = string.Empty;
        if (!shelterStressAppliedThisTide && SleepIntervalCrossesTidePeak(skippedSeconds))
        {
            int stiltBeforeSleep = stiltIntegrity;
            int warmthBeforeSleep = houseWarmth;
            shelterStressAppliedThisTide = true;
            ResolveShelterTideStress(false);
            overnightShelterText = shelterBreachThisTide
                ? $"你睡着时高潮冲过桩脚：支撑 {stiltBeforeSleep}->{stiltIntegrity}，屋暖 {warmthBeforeSleep}->{houseWarmth}。"
                : $"你睡着时高潮压过屋底：支撑 {stiltBeforeSleep}->{stiltIntegrity}。";
        }

        RecoverSurvivingStormCargoAtRest();
        bool burnedDryFuel = stoveCondition > 0 && dryFuelBundles > 0;
        if (burnedDryFuel)
        {
            dryFuelBundles--;
            string fuelText = "你把留下的干柴添进修好的灶，火一直撑到黎明。";
            overnightShelterText = string.IsNullOrEmpty(overnightShelterText)
                ? fuelText
                : $"{overnightShelterText} {fuelText}";
        }

        float remainingWaterNeed = Mathf.Max(
            0f,
            DailyRestWaterNeedLiters - waterConsumedSinceLastRest);
        float overnightWaterConsumed = ConsumeRestWater(remainingWaterNeed);
        lastRestWaterShortfallLiters = Mathf.Max(0f, remainingWaterNeed - overnightWaterConsumed);
        float restWaterFulfillment01 = Mathf.Clamp01(
            (DailyRestWaterNeedLiters - lastRestWaterShortfallLiters) /
            DailyRestWaterNeedLiters);
        waterConsumedSinceLastRest = 0f;
        if (lastRestWaterShortfallLiters > 0.01f)
        {
            string waterText = $"干净水少了 {lastRestWaterShortfallLiters:F1}L；这一夜只能浅睡，身体没有完全缓过来。";
            overnightShelterText = string.IsNullOrEmpty(overnightShelterText)
                ? waterText
                : $"{overnightShelterText} {waterText}";
        }

        // Sleep compresses hours of player time but not the world's causal history.
        // Sample the skipped tide before mutating either clock so a crossed高潮 can
        // leave the same persistent post mark as a tide watched in real time.
        AdvanceHighWaterMemory(tideClockSeconds, weatherClockSeconds, skippedSeconds);
        AdvanceContinuousWeather(skippedSeconds);
        tideRound++;
        if (HasPreparedTidePrep() && tidePrepTargetRound < tideRound)
        {
            // 低潮夜里备好后选择先睡，准备应随人带到黎明，而不是因潮次编号变化而丢失。
            tidePrepTargetRound = tideRound;
        }
        moonAgeDays = Mathf.Repeat(moonAgeDays + normalizedSleep, 29.53f);
        dayClockSeconds = dayLengthSeconds * 0.23f;
        dayProgress01 = 0.23f;
        dayNightPhase = DayNightPhase.Dawn;
        // The unrepaired shelter still offers a little rest. A raised, dry bed turns
        // the same skipped night into a meaningful recovery. Missing drinking water
        // limits that recovery but does not become an unrelated instant damage tick.
        float restedWarmthFloor = bedCondition > 0 ? 0.82f : 0.48f;
        if (burnedDryFuel)
        {
            restedWarmthFloor = Mathf.Max(restedWarmthFloor, 0.9f);
        }
        float waterLimitedWarmthFloor = Mathf.Lerp(0.38f, restedWarmthFloor, restWaterFulfillment01);
        bodyWarmth01 = Mathf.Max(bodyWarmth01, waterLimitedWarmthFloor);
        if (chartRadioCondition > 0)
        {
            lampForecastCharges = Mathf.Max(lampForecastCharges, 1);
        }
        tideClockSeconds = Mathf.Repeat(tideClockSeconds + skippedSeconds, Mathf.Max(8f, tideCycleSeconds));
        tideStrength = CalculateTideStrength(moonAgeDays);
        state = SliceState.LowTidePlanning;
        viewMode = SliceViewMode.Shelter;
        stateTimer = 0f;
        currentWaterY = EvaluateNaturalWaterY(tideClockSeconds);
        tideCurrentlyRising = tideClockSeconds / Mathf.Max(8f, tideCycleSeconds) < 0.5f;
        loftForecastSnapshot = default;
        CaptureChartForecast();
        currentHarvest = HarvestKind.None;
        currentHarvestBatchId = 0;
        harvestPhysicalState = HarvestPhysicalState.None;
        InitializeCurrentTideDriftField(true);
        currentHarvestBanked = false;
        harvestCarryTransition01 = 0f;
        harvestPlacementTransition01 = 0f;
        harvestCarryStartPosition = Vector2.zero;
        tidePrepActionTimer = 0f;
        pendingTidePrepChoice = TidePrepChoice.None;
        tidePrepWorkProgress = 0f;
        tidePrepWorkActive = false;
        netTouched = false;
        netDeployed = false;
        netRigStep = NetRigStep.Stored;
        netUnrollProgress = 0f;
        netLoweringProgress = 0f;
        netHaulProgress = 0f;
        netHaulStrokePhase = 0f;
        netHaulEffort01 = 0f;
        netHaulLoad01 = 0f;
        editorNetHaulPreviewActive = false;
        netCatchResolved = false;
        netCaptureProgress01 = 0f;
        netFraying01 = 0f;
        netDepthAdjustmentActive = false;
        netDepthAdjustmentDirection = 0f;
        netPostCatchExposureSeconds = 0f;
        netCatchBundleTier = 1;
        netOverloadStressApplied = 0;
        netSecuredEarly = false;
        playerPosition = GetHomePlayerPosition();
        playerLane = WalkLane.Deck;
        playerFacing = 1;
        playerHorizontalVelocity = 0f;
        playerSwimming = false;
        sailTripActive = false;
        sailTripTimer = 0f;
        sailingBoatX = sailingHomeX;
        sailingBoatLaneY = sailingHomeY;
        sailingBoatVelocity = 0f;
        sailingBoatWorldVelocity = 0f;
        sailingClueCollected = false;
        sailingRewardPending = false;
        sailingClueWasLighthouse = false;
        sailingBuoyChecked = !EnableSailingBuoyGameplay;
        SetExtraSaltWoodOwner(extraSaltWoodOwner);
        sailingSalvageHookProgress = 0f;
        sailingHookThrow01 = 0f;
        sailingHookThrowActive = false;
        sailingSalvageHauling = false;
        sailingSalvageTension01 = 0f;
        sailingSalvageOverstrainSeconds = 0f;
        sailingHookWorldPosition = sailingSalvagePoint;
        sailingSalvageInitialRopeLength = 0f;
        sailingReef?.ResetRuntime();
        nearshoreWorkDone = false;
        tideRoutingMode = TideRoutingMode.Open;
        routingBoom01 = 0f;
        routingWorkActive = false;
        routingWorkDirection = 0f;
        routingDecisionLocked = false;
        shelterStressAppliedThisTide = false;
        shelterRawStressThisTide = 0;
        shelterResolvedStressThisTide = 0;
        shelterBreachThisTide = false;
        shelterImpactTimer = 0f;
        repairChoiceApplied = false;
        pendingRepairChoice = RepairChoice.None;
        repairWorkStep = 0;
        repairWorkProgress = 0f;
        repairWorkActive = false;
        string morningPrepText = HasPreparedTidePrep()
            ? $"昨夜备好的{GetPrepChoiceName(selectedPrepChoice)}还在手边；{GetPrepEffectText(selectedPrepChoice)}"
            : "昨夜没有额外准备，这一潮只靠网深和现场判断。";
        string morningActionHint = IsDepartureReady()
            ? "暴潮窗口到了：走到船边按 F，离开这座临时的家。"
            : HasLampForecast()
                ? GetLampForecastText()
                : CanSeeLighthouse()
                ? "下一潮开始：带回的航线纸片对上了潮向，漩涡退成暗流；去船边按 F 可以试航灯塔。"
                : $"黎明到了。{morningPrepText} 先看水位，再决定何时放网、转导流绳或等船浮起来。";
        lastActionHint = string.IsNullOrEmpty(overnightShelterText)
            ? morningActionHint
            : $"{overnightShelterText} {morningActionHint}";
    }

    private bool IsDepartureReady()
    {
        return lighthouseSeen &&
            lighthouseClues >= requiredLighthouseClues &&
            boatReadiness >= requiredBoatReadiness &&
            tideRound >= departureStormRound &&
            GetStormPressure01() >= finalDepartureStormThreshold;
    }

    private float GetStormPressure01()
    {
        return TideContinuousWeatherModel.EvaluatePressure01(
            weatherClockSeconds,
            dayLengthSeconds,
            stormFrontArrivalDays);
    }

    private float GetVisualStormPressure01()
    {
        return GetStormPressure01();
    }

    private string GetWeatherFrontName()
    {
        float pressure01 = GetStormPressure01();
        if (pressure01 >= 0.7f)
        {
            return "暴潮已到";
        }

        if (pressure01 >= 0.35f)
        {
            return "风雨逼近";
        }

        return pressure01 >= 0.08f ? "远云压低" : "天气尚稳";
    }

    private float CalculateTideStrength(float ageDays)
    {
        float phase01 = Mathf.Repeat(ageDays / 29.53f, 1f);
        float spring01 = Mathf.Abs(Mathf.Cos(phase01 * Mathf.PI * 2f));
        return Mathf.Lerp(0.18f, 1f, Mathf.Pow(spring01, 1.65f));
    }

    private float GetSelectedNetY()
    {
        return Mathf.Lerp(GetNetLineY(NetLine.High), GetNetLineY(NetLine.Low), Mathf.Clamp01(netSetDepth01));
    }

    private float GetNetHeadLineY()
    {
        // The head rope hangs over the seaward edge of the wet boardwalk. Keeping it
        // below the standing surface is the central WYSIWYG contract: the net can no
        // longer read as a fence laid across the route the player walks on.
        return GetPlayerStandingFeetY(WalkLane.TideFlat) - 0.14f;
    }

    private float GetNetLineY(NetLine line)
    {
        float headLineY = GetNetHeadLineY();
        if (line == NetLine.Low)
        {
            return headLineY - 1.38f;
        }

        if (line == NetLine.Mid)
        {
            return headLineY - 0.86f;
        }

        return headLineY - 0.34f;
    }

    private Vector2 GetHomePlayerPosition()
    {
        float homeX = houseAnchor.x - 1.06f;
        return new Vector2(homeX, GetPlayerLaneY(WalkLane.Deck));
    }

    private Vector2 GetOpeningPlayerPosition()
    {
        // 控制权交给玩家时，角色已经搁浅在可见岩板上。以后即使补序章动画，
        // 也不能再用从虚空走进画面的实时移动冒充海难登陆。
        float openingX = Mathf.Clamp(
            TideBarrenIslandController.OpeningPlayerX,
            GetLaneMinX(WalkLane.TideFlat),
            GetLaneMaxX(WalkLane.TideFlat));
        return new Vector2(openingX, GetPlayerLaneY(WalkLane.TideFlat));
    }

    private float GetPlayerLaneY(WalkLane lane)
    {
        if (lane == WalkLane.Deck)
        {
            return GetFormalHouseSprite() != null
                ? houseAnchor.y - 0.87f
                : houseAnchor.y - 0.42f;
        }

        if (lane == WalkLane.InteriorLower)
        {
            if (HasCompleteV35HouseInteriorPresentation())
            {
                float floodablePlatformY = GetV35WorldAnchor(new Vector2(1024f, 1636f)).y;
                return floodablePlatformY + 0.58f;
            }

            if (HasCompleteV30HousePresentation())
            {
                float mechanicalPlatformY = GetFormalHouseWorldPosition().y +
                    TideV30HousePresentationModel.PixelTopLeftToWorldOffset(new Vector2(768f, 1309f)).y;
                return mechanicalPlatformY + 0.58f;
            }

            return -1.42f;
        }

        if (lane == WalkLane.InteriorUpper)
        {
            // The enclosed living floor is the same physical deck seen outside.
            // Entering the house changes the cutaway, not the building's Y scale.
            return GetPlayerLaneY(WalkLane.Deck);
        }

        if (lane == WalkLane.InteriorLoft)
        {
            if (HasCompleteV35HouseInteriorPresentation())
            {
                float lookoutFloorY = GetV35WorldAnchor(new Vector2(1024f, 760f)).y;
                return lookoutFloorY + 0.58f;
            }

            if (HasCompleteV30HousePresentation())
            {
                // V30 瞭望层楼板 y=458。人物中心比楼板高半个站立高度，
                // 这样脚落在正式地板上，而不是沿用 V28 的屋顶经验值。
                float lookoutFloorY = GetFormalHouseWorldPosition().y +
                    TideV30HousePresentationModel.PixelTopLeftToWorldOffset(new Vector2(768f, 458f)).y;
                return lookoutFloorY + 0.58f;
            }

            if (HasCompleteV28HousePresentation())
            {
                // The registered observation cabin sits above the main roof.  This
                // keeps the survivor inside it instead of walking on the roof slope.
                return 2.02f;
            }

            // The loft occupies the deep roof volume of the authored shell. The actor
            // clears the rafters at full standing height and remains above storm water.
            return 1.3f;
        }

        return lowWaterY + 1.42f;
    }

    private float GetLaneSwitchX()
    {
        return HasHdFormalHouse() ? 0.24f : laneSwitchX;
    }

    private float GetShoreWorkX()
    {
        // The first visible load-bearing post sits under the house, well left of the
        // stored net. Keeping these targets apart prevents "repair post" from being
        // stolen by the wider net interaction radius.
        return HasHdFormalHouse() ? 0.25f : shoreWorkX;
    }

    private float GetPlayerStandingFeetY(WalkLane lane)
    {
        return GetPlayerLaneY(lane) - 0.58f;
    }

    private float GetTideFlatVisiblePathLeft()
    {
        if (barrenIsland != null)
        {
            return TideBarrenIslandController.WalkableLeftX;
        }

        if (HasCompleteV32ArtPresentation())
        {
            // V38 明确声明外景潮间平台从母版 x=498 开始。旧实现只取梯底
            // 左侧 0.15m，导致画面里大段真实承重平台无法行走。
            return GetV32WorldAnchor(new Vector2(498f, 1636f)).x;
        }

        return GetGangwayBottomPosition().x - 0.15f;
    }

    private float GetBoardwalkVisualLeft()
    {
        if (HasCompleteV32ArtPresentation())
        {
            // 母版在 y=1636 附近的连续平台木梁实际止于 x≈1423；旧锚点
            // x=1660 只是资源契约中的右侧活动范围，不是可见木板边缘，导致
            // 平台与外接栈道之间出现无支撑空档。让栈道从 x=1420 左侧少量
            // 压入，且保持 sortingOrder=0，屋体梁柱和绞盘仍会画在它前面。
            return GetV32WorldAnchor(new Vector2(1420f, 1636f)).x - 0.08f;
        }

        return GetTideFlatVisiblePathLeft();
    }

    private float GetTideFlatVisiblePathRight()
    {
        return GetBoatBoardingX();
    }

    private float GetLaneMinX(WalkLane lane)
    {
        if (lane == WalkLane.Deck)
        {
            if (HasCompleteV32ArtPresentation())
            {
                return GetV32WorldAnchor(new Vector2(286f, 1288f)).x + 0.02f;
            }

            return Mathf.Max(playerMinX, deckLaneMinX);
        }

        if (lane == WalkLane.TideFlat)
        {
            if (barrenIsland != null)
            {
                // 岩礁岛把同一条潮间步行面真实延伸到左岸。旧 Scene 序列化的
                // playerMinX=-5.15 只属于房屋原型；继续取 Max 会把开场和整座岛
                // 悄悄夹回屋边，造成“画得到、走不到”的所见非所得。
                return TideBarrenIslandController.WalkableLeftX + 0.02f;
            }

            // 正式梯底比旧场景序列化的 tideFlatLaneMinX 更靠左。若继续取旧值，
            // 下梯后第一次移动会被 Clamp 瞬移 1.7m；正式资源存在时由可见路径接管。
            return HasCompleteV32ArtPresentation()
                ? Mathf.Max(playerMinX, GetTideFlatVisiblePathLeft() + 0.02f)
                : Mathf.Max(playerMinX, tideFlatLaneMinX, GetTideFlatVisiblePathLeft() + 0.02f);
        }

        if (HasCompleteV35HouseInteriorPresentation())
        {
            if (lane == WalkLane.InteriorLower)
            {
                return GetV35WorldAnchor(new Vector2(498f, 1636f)).x + InteriorBodyHalfWidth;
            }

            if (lane == WalkLane.InteriorUpper)
            {
                return GetV35WorldAnchor(new Vector2(430f, 1288f)).x + InteriorBodyHalfWidth;
            }

            if (lane == WalkLane.InteriorLoft)
            {
                return GetV35WorldAnchor(new Vector2(830f, 760f)).x + InteriorBodyHalfWidth;
            }
        }

        return playerMinX;
    }

    private float GetLaneMaxX(WalkLane lane)
    {
        if (lane == WalkLane.Deck)
        {
            if (HasCompleteV32ArtPresentation())
            {
                // 横向键可以从梯口旁走过，但脚点不能离开 V38 声明的外廊实木。
                return GetV32WorldAnchor(new Vector2(1670f, 1288f)).x - 0.02f;
            }

            return Mathf.Min(playerMaxX, deckLaneMaxX);
        }

        if (lane == WalkLane.TideFlat)
        {
            return Mathf.Min(playerMaxX, tideFlatLaneMaxX, GetTideFlatVisiblePathRight());
        }

        if (HasCompleteV35HouseInteriorPresentation())
        {
            if (lane == WalkLane.InteriorLower)
            {
                return GetV35WorldAnchor(new Vector2(1423f, 1636f)).x - InteriorBodyHalfWidth;
            }

            if (lane == WalkLane.InteriorUpper)
            {
                return GetV35WorldAnchor(new Vector2(1512f, 1288f)).x - InteriorBodyHalfWidth;
            }

            if (lane == WalkLane.InteriorLoft)
            {
                return GetV35WorldAnchor(new Vector2(1090f, 760f)).x - InteriorBodyHalfWidth;
            }
        }

        return playerMaxX;
    }

    /// <summary>
    /// 返回当前第一切片的“所见即所得”契约。这里不模拟物理碰撞，而是验证
    /// 自定义行走通道没有越出可见木路，所有关键交互点也都落在通道内。
    /// </summary>
    public string GetVisibleInteractionContractReport()
    {
        float pathLeft = GetTideFlatVisiblePathLeft();
        float pathRight = GetTideFlatVisiblePathRight();
        float laneLeft = GetLaneMinX(WalkLane.TideFlat);
        float laneRight = GetLaneMaxX(WalkLane.TideFlat);
        float boatX = GetBoatBoardingX();
        bool laneInsidePath = laneLeft >= pathLeft && laneRight <= pathRight;
        float shoreX = GetShoreWorkX();
        bool keyPointsInsideLane = netAnchor.x >= laneLeft && netAnchor.x <= laneRight &&
            arrivalWreckX >= laneLeft && arrivalWreckX <= laneRight &&
            shoreX >= laneLeft && shoreX <= laneRight &&
            boatX >= laneLeft && boatX <= laneRight;
        float visibleSurfaceY = GetPlayerLaneY(WalkLane.TideFlat) - 0.58f;
        bool feetOnSurface = Mathf.Abs(GetPlayerStandingFeetY(WalkLane.TideFlat) - visibleSurfaceY) <= 0.01f;
        bool valid = laneInsidePath && keyPointsInsideLane && feetOnSurface;
        return $"valid={valid}; path=[{pathLeft:F2},{pathRight:F2}]; lane=[{laneLeft:F2},{laneRight:F2}]; " +
            $"points(net={netAnchor.x:F2},wreck={arrivalWreckX:F2},repair={shoreX:F2},boat={boatX:F2}); " +
            $"feetY={GetPlayerStandingFeetY(WalkLane.TideFlat):F2}; surfaceY={visibleSurfaceY:F2}";
    }

    private bool IsPlayerNearBoat()
    {
        if (playerLane != WalkLane.TideFlat)
        {
            return false;
        }

        return Mathf.Abs(playerPosition.x - GetBoatBoardingX()) <= boatBoardDistance;
    }

    private bool HasUnsecuredHarvestForBoarding()
    {
        return state == SliceState.RepairMoment &&
            currentHarvest != HarvestKind.None &&
            !currentHarvestBanked;
    }

    private float GetBoatBoardingX()
    {
        // 固定木路不追随潮流伸缩。它止于静水船艉左侧，最后一小段由可见
        // 跳板连接到当前船艉；交互点、行走边界和固定木路末端共用此坐标。
        return GetRestingMooredBoatSternFootPosition().x - PierToRestingSternGap;
    }

    private bool IsBoatBoardWindowOpen()
    {
        if (state == SliceState.FinalDeparture)
        {
            return false;
        }

        bool boatFloating = currentWaterY >= lowWaterY + 0.38f;
        if (IsDepartureReady())
        {
            return boatFloating;
        }

        if (dayNightPhase == DayNightPhase.Night)
        {
            return false;
        }

        return boatFloating && currentWaterY <= lowWaterY + 1.7f;
    }

    private bool IsNearshoreWorkWindowOpen()
    {
        return state == SliceState.TideRising && currentWaterY <= lowWaterY + shoreWorkMaxWaterOffset;
    }

    private bool IsPlayerNearShoreWork()
    {
        return viewMode == SliceViewMode.Shelter &&
            playerLane == WalkLane.TideFlat &&
            Mathf.Abs(playerPosition.x - GetShoreWorkX()) <= shoreWorkDistance;
    }

    private Vector2 GetShoreWorkPosition()
    {
        return new Vector2(GetShoreWorkX(), GetPlayerLaneY(WalkLane.TideFlat) + 0.05f);
    }

    private Vector2 GetBoatTripOffset()
    {
        if (state == SliceState.FinalDeparture)
        {
            float depart01 = Mathf.Clamp01(stateTimer / Mathf.Max(0.1f, finalDepartureSeconds));
            float lift = Mathf.Sin(depart01 * Mathf.PI) * 0.22f;
            return new Vector2(Mathf.Lerp(0.18f, 3.4f, depart01), lift + depart01 * 0.2f);
        }

        if (!sailTripActive)
        {
            return Vector2.zero;
        }

        return new Vector2(sailingBoatX - sailingHomeX, Mathf.Sin(Time.time * 1.4f) * 0.08f);
    }

    private string GetLineName()
    {
        if (selectedNetLine == NetLine.Low)
        {
            return "低线：贪，接触久，风险高";
        }

        if (selectedNetLine == NetLine.Mid)
        {
            return "中线：稳，最适合第一轮";
        }

        return "高线：保守，小潮可能摸不到";
    }

    private string GetNetLineDecisionText(NetLine line)
    {
        if (line == NetLine.Low)
        {
            return "贪一点，碰水久，也更可能被重物拖坏";
        }

        if (line == NetLine.Mid)
        {
            return "最稳，第一轮先看懂潮水和船";
        }

        return "保守，小潮可能摸不到，但不容易出事";
    }

    private void EnsureScene()
    {
        CleanupRetiredGeneratedObjects();
        barrenIsland = GetComponent<TideBarrenIslandController>();
        if (barrenIsland == null)
        {
            barrenIsland = gameObject.AddComponent<TideBarrenIslandController>();
        }
        heavyWreckSalvage = GetComponent<TideHeavyWreckSalvageController>();
        if (heavyWreckSalvage == null)
        {
            heavyWreckSalvage = gameObject.AddComponent<TideHeavyWreckSalvageController>();
        }
        mooringRope = GetComponent<TideMooringRopeController>();
        if (mooringRope == null)
        {
            mooringRope = gameObject.AddComponent<TideMooringRopeController>();
        }
        sailingReef = GetComponent<TideSailingReefController>();
        if (sailingReef == null)
        {
            sailingReef = gameObject.AddComponent<TideSailingReefController>();
        }
        sailingSalvage = GetComponent<TideSailingSalvageController>();
        if (sailingSalvage == null)
        {
            sailingSalvage = gameObject.AddComponent<TideSailingSalvageController>();
        }
        stormRescue = GetComponent<TideStormRescueController>();
        if (stormRescue == null)
        {
            stormRescue = gameObject.AddComponent<TideStormRescueController>();
        }
        forecastTideNotches = GetComponent<TideForecastTideNotchController>();
        if (forecastTideNotches == null)
        {
            forecastTideNotches = gameObject.AddComponent<TideForecastTideNotchController>();
        }
        wrackLine = GetComponent<TideWrackLineController>();
        if (wrackLine == null)
        {
            wrackLine = gameObject.AddComponent<TideWrackLineController>();
        }
        backdropRenderer = EnsureRenderer("GeneratedStiltFirstBackdrop", GetBackdropSprite(), -100);
        daySeaSkyRenderer = EnsureRenderer("GeneratedStiltFirstFormalDaySeaSky", GetFormalDaySeaSkySprite(), -99);
        nightSeaSkyRenderer = EnsureRenderer("GeneratedStiltFirstFormalNightSeaSky", GetFormalNightSeaSkySprite(), -98);
        ambientMoonWashRenderer = EnsureRenderer("GeneratedStiltFirstAmbientMoonWash", GetMoonWashSprite(), -12);
        foregroundMoonWashRenderer = EnsureRenderer("GeneratedStiltFirstForegroundMoonWash", GetMoonWashSprite(), 16);
        boatViewTransitionRenderer = EnsureRenderer("GeneratedStiltFirstBoatViewTransition", GetMoonWashSprite(), 100);
        waterRenderer = EnsureRenderer("GeneratedStiltFirstTideWater", GetWaterSprite(), -20);
        waterFoamRenderer = EnsureRenderer("GeneratedStiltFirstTideFoam", GetFoamSprite(), -18);
        naturalWaterSurfaceRenderer = EnsureRenderer("GeneratedStiltFirstFormalWaterSurface", GetFormalWaterSurfaceSprite(), -17);
        foregroundWaterOcclusionRenderer = EnsureRenderer("GeneratedStiltFirstForegroundWaterOcclusion", GetFormalWaterSurfaceSprite(), 16);
        foregroundDeepWaterOcclusionRenderer = EnsureRenderer(
            "GeneratedStiltFirstForegroundDeepWaterOcclusion",
            GetMoonWashSprite(),
            15);
        shelterWaveImpactRenderer = EnsureRenderer("GeneratedStiltFirstTideShelterWaveImpact", GetFormalStiltWaveImpactSprite(), 16);
        shelterDamageRenderer = EnsureRenderer("GeneratedStiltFirstShelterDamagedPost", GetFormalStiltDamageSprite(), 13);
        netWaterContactRenderer = EnsureRenderer("GeneratedStiltFirstNetWaterContact", GetFoamSprite(), 16);
        formalNetRenderer = EnsureRenderer("GeneratedStiltFirstFormalNet", GetFormalNetSprite(), -1);
        formalNetBundleRenderer = EnsureRenderer("GeneratedStiltFirstFormalNetBundle", GetPrepRopeCoilSprite(), 16);
        netHandlingRopeRenderer = EnsureRenderer("GeneratedStiltFirstNetHandlingRope", GetNetLineSprite(), 16);
        // V54 展开态始终保持完整世界尺寸；人物向右行走时只扩大这一块遮罩，
        // 从而露出已经从手里放出的网段，绝不再把整张展开网横向压扁。
        netRevealMask = EnsureSpriteMask("GeneratedStiltFirstV54NetRevealMask", GetMoonWashSprite(), -1);
        houseRenderer = EnsureRenderer("GeneratedStiltFirstHouseBody", GetHouseSprite(), 5);
        // V30 的裁切 owner 只在室内维修组合模式启用。预先建立固定数量的
        // Renderer 可以避免玩家进屋或施工时动态创建节点造成一帧抖动。
        EnsureList(
            v30RepairOwnerRenderers,
            "GeneratedStiltFirstV30RepairOwner",
            TideV30HousePresentationModel.RepairOwnerCount,
            null,
            19);
        // V34 外景固定创建六个 owner Renderer。每个时刻只挂 Damage 或 Repair
        // 其中一张，进入室内、航行或终局破坏时统一关闭，避免跨视图残留。
        EnsureList(
            v34ExteriorRepairOwnerRenderers,
            "GeneratedStiltFirstV34ExteriorRepairOwner",
            TideV34HouseExteriorPresentationModel.OwnerCount,
            null,
            6);
        // V35 uses the same fixed Renderer pool pattern as V34, but only while the
        // cutaway is active. It replaces V30's full-house interior states.
        EnsureList(
            v35InteriorRepairOwnerRenderers,
            "GeneratedStiltFirstV35InteriorRepairOwner",
            TideV35HouseInteriorPresentationModel.OwnerCount,
            null,
            19);
        verandaRenderer = EnsureRenderer("GeneratedStiltFirstVerandaBody", GetVerandaSprite(), 6);
        roofRenderer = EnsureRenderer("GeneratedStiltFirstHouseRoof", GetRoofSprite(), 8);
        porchRoofRenderer = EnsureRenderer("GeneratedStiltFirstPorchRoof", GetPorchRoofSprite(), 9);
        longEaveRenderer = EnsureRenderer("GeneratedStiltFirstLongEave", GetLongEaveSprite(), 10);
        underHouseShadowRenderer = EnsureRenderer("GeneratedStiltFirstUnderHouseShadow", GetUnderHouseShadowSprite(), 1);
        stormSurgeWallRenderer = EnsureRenderer("GeneratedStiltFirstStormSurgeWall", GetStormSurgeWallSprite(), 17);
        deckRenderer = EnsureRenderer("GeneratedStiltFirstHouseDeck", GetDeckSprite(), 7);
        windowGlowRenderer = EnsureRenderer("GeneratedStiltFirstHouseWarmLamp", GetShelterLampSprite(), 11);
        playerRenderer = EnsureRenderer("GeneratedStiltFirstPlayer", GetPlayerSprite(), 14);
        playerAquaticRenderer = EnsureRenderer("GeneratedStiltFirstPlayerAquatic", null, 14);
        playerGlowRenderer = EnsureRenderer("GeneratedStiltFirstPlayerGlow", GetPlayerHaloSprite(), 13);
        playerSwimWakeRenderer = EnsureRenderer("GeneratedStiltFirstPlayerSwimWake", GetFormalWaterSurfaceSprite(), 15);
        boatGlowRenderer = EnsureRenderer("GeneratedStiltFirstBoatGlow", GetFoamSprite(), 3);
        boatBackRigRenderer = EnsureRenderer("GeneratedStiltFirstBoatBackRig", null, 3);
        boatHullRenderer = EnsureRenderer("GeneratedStiltFirstBoatHull", GetBoatHullSprite(), 4);
        boatSailRenderer = EnsureRenderer("GeneratedStiltFirstBoatSail", GetSailSprite(), 5);
        boatLandingWalkwayRenderer = EnsureRenderer("GeneratedStiltFirstBoatLandingWalkway", GetLandingWalkwaySprite(), 1);
        boatWakeRenderer = EnsureRenderer("GeneratedStiltFirstBoatWake", GetBoatWakeSprite(), 3);
        boatWaterlineOcclusionRenderer = EnsureRenderer("GeneratedStiltFirstBoatWaterlineOcclusion", GetFormalWaterSurfaceSprite(), 7);
        boatPassengerGunwaleRenderer = EnsureRenderer("GeneratedStiltFirstBoatPassengerGunwale", GetBoatHullSprite(), 6);
        boatRudderRenderer = EnsureRenderer("GeneratedStiltFirstBoatRudder", null, 8);
        boatCockpitRenderer = EnsureRenderer("GeneratedStiltFirstBoatCockpit", GetBoatCockpitSprite(), 7);
        // V52 的船壳破口和舱底施工层各有独立所有权。它们不能复用稳定底
        // Renderer，否则阶段切换会把整块前船舷或舱底一起替换掉。
        boatHullRepairOwnerRenderer = EnsureRenderer("GeneratedStiltFirstV52HullRepairOwner", null, 8);
        boatCockpitRepairOwnerRenderer = EnsureRenderer("GeneratedStiltFirstV52CockpitRepairOwner", null, 5);
        // The passenger is drawn behind the authored hull so the gunwale masks the
        // seated legs. Drawing the old standing sprite above the hull made the player
        // look pasted onto the front of the boat.
        boatPassengerRenderer = EnsureRenderer("GeneratedStiltFirstBoatPassenger", GetBoatPassengerSprite(), 5);
        EnsureList(
            mooringRopeSegments,
            "GeneratedStiltFirstMooringRopeSegment",
            3,
            GetNetLineSprite(),
            13);
        mooringRopeEndRenderer = EnsureRenderer(
            "GeneratedStiltFirstMooringRopeEnd",
            GetNetWeightSprite(),
            14);
        EnsureList(
            stormRescueRenderers,
            "GeneratedStiltFirstStormRescueItem",
            4,
            null,
            26);
        EnsureList(
            stormRescueRopeRenderers,
            "GeneratedStiltFirstStormRescueHoistRope",
            4,
            GetNetLineSprite(),
            26);
        lighthouseRenderer = EnsureRenderer("GeneratedStiltFirstLighthouse", GetLighthouseSprite(), -2);
        lighthouseBeamRenderer = EnsureRenderer("GeneratedStiltFirstLighthouseLensBeam", GetLighthouseBeamSprite(), -1);
        moonRenderer = EnsureRenderer("GeneratedStiltFirstMoon", GetMoonSprite(), -4);
        moonShadowRenderer = EnsureRenderer("GeneratedStiltFirstMoonShadow", GetMoonShadowSprite(), -3);
        sunRenderer = EnsureRenderer("GeneratedStiltFirstSunDisc", GetPlayerHaloSprite(), -5);
        harvestRenderer = EnsureRenderer("GeneratedStiltFirstHarvest", GetFishSprite(), 13);
        stormLineRenderer = EnsureRenderer("GeneratedStiltFirstFutureStormLine", GetStormLineSprite(), 6);
        lighthouseChartClueRenderer = EnsureRenderer("GeneratedStiltFirstLighthouseChartClue", GetLighthouseChartClueSprite(), 14);
        stormPennantRenderer = EnsureRenderer("GeneratedStiltFirstStormWarningPennant", GetStormWarningPennantSprite(), 15);
        departureSignalRenderer = EnsureRenderer("GeneratedStiltFirstDepartureSignal", GetLighthouseBeamSprite(), 15);
        tideRoutingRopeRenderer = EnsureRenderer("GeneratedStiltFirstTideRoutingRope", GetNetLineSprite(), 14);
        tideRoutingBoomRenderer = EnsureRenderer("GeneratedStiltFirstTideRoutingBoom", GetRoutingBoomSprite(), 13);
        tideRoutingWinchRenderer = EnsureRenderer("GeneratedStiltFirstTideRoutingWindlass", GetRoutingWindlassSprite(false), 13);
        horizonSeaRenderer = EnsureRenderer("GeneratedStiltFirstHorizonSea", GetWaterSprite(), -55);
        boardwalkPathRenderer = EnsureRenderer("GeneratedStiltFirstBoardwalkPath", GetFoamSprite(), 0);
        tideFlatPathRenderer = EnsureRenderer("GeneratedStiltFirstTideFlatPath", GetFoamSprite(), 0);
        sailingSeaRenderer = EnsureRenderer("GeneratedStiltFirstSailingSea", GetWaterSprite(), -40);
        sailingWreckPointRenderer = EnsureRenderer("GeneratedStiltFirstSailingWreckPoint", GetShipwreckRibSprite(), 8);
        sailingSalvagePointRenderer = EnsureRenderer("GeneratedStiltFirstSailingSalvagePoint", GetWaterWrackSprite(), 8);
        sailingSalvageWakeRenderer = EnsureRenderer("GeneratedStiltFirstSailingSalvageWake", GetSwimWakeSprite(), 7);
        // The towline floats on the surface. It must render above the broad waterline
        // occlusion pass (order 9), otherwise the simulation remains correct while the
        // entire line disappears under the sea texture in the Game view.
        sailingSalvageHookRopeRenderer = EnsureRenderer("GeneratedStiltFirstSailingSalvageHookRope", GetTowRopeSprite(), 11);
        sailingSalvageHookRopeEndRenderer = EnsureRenderer("GeneratedStiltFirstSailingSalvageHookRopeEnd", GetTowRopeSprite(), 11);
        sailingSalvageHookRenderer = EnsureRenderer("GeneratedStiltFirstSailingSalvageHook", GetSeaSalvageHookSprite(), 12);
        sailingReefPointRenderer = EnsureRenderer(
            "GeneratedStiltFirstSailingReefPoint",
            TideBarrenIslandController.GetSharedReefRockSprite(),
            8);
        sailingReefFoamRenderer = EnsureRenderer("GeneratedStiltFirstSailingReefFoam", GetFormalSeaCurrentCrestSprite(), 9);
        sailingRangeBreakerRenderer = EnsureRenderer("GeneratedStiltFirstSailingRangeBreaker", GetFormalStiltWaveImpactSprite(), 2);
        boatIngressWaterRenderer = EnsureRenderer("GeneratedStiltFirstBoatIngressWater", GetFormalStiltWaveImpactSprite(), 6);
        sailingBailBucketRenderer = EnsureRenderer("GeneratedStiltFirstSailingBailBucket", GetPrepBucketSprite(), 10);
        sailingBailSplashRenderer = EnsureRenderer("GeneratedStiltFirstSailingBailSplash", GetFormalStiltWaveImpactSprite(), 9);
        routeVortexRenderer = EnsureRenderer("GeneratedStiltFirstRouteVortex", GetVortexSprite(), -6);
        routeVortexInnerRenderer = EnsureRenderer("GeneratedStiltFirstRouteVortexInner", GetVortexSprite(), -5);
        routeVortexSurfaceRenderer = EnsureRenderer("GeneratedStiltFirstRouteVortexSurface", null, -4);
        returnPressureWashRenderer = EnsureRenderer("GeneratedStiltFirstReturnPressureWash", GetMoonWashSprite(), 17);
        prepRopeCoilRenderer = EnsureRenderer("GeneratedStiltFirstPrepRopeCoil", GetPrepRopeCoilSprite(), 18);
        prepBucketRenderer = EnsureRenderer("GeneratedStiltFirstPrepBucket", GetPrepBucketSprite(), 18);
        prepStakeRenderer = EnsureRenderer("GeneratedStiltFirstPrepStake", GetPrepStakeSprite(), 18);
        interiorBackdropRenderer = EnsureRenderer("GeneratedStiltFirstInteriorBackdrop", GetMoonWashSprite(), 20);
        interiorLowerFloorRenderer = EnsureRenderer("GeneratedStiltFirstInteriorLowerFloor", GetDeckSprite(), 22);
        interiorUpperFloorRenderer = EnsureRenderer("GeneratedStiltFirstInteriorUpperFloor", GetDeckSprite(), 22);
        interiorLoftBackdropRenderer = EnsureRenderer("GeneratedStiltFirstInteriorLoftBackdrop", GetMoonWashSprite(), 20);
        interiorLoftFloorRenderer = EnsureRenderer("GeneratedStiltFirstInteriorLoftFloor", GetDeckSprite(), 22);
        interiorStairRenderer = EnsureRenderer("GeneratedStiltFirstInteriorStair", GetLandingWalkwaySprite(), 23);
        interiorLoftStairRenderer = EnsureRenderer("GeneratedStiltFirstInteriorLoftStair", GetLandingWalkwaySprite(), 23);
        interiorLoftLookoutTableRenderer = EnsureRenderer("GeneratedStiltFirstInteriorLoftLookoutTable", GetDeckSprite(), 23);
        interiorLoftLookoutRenderer = EnsureRenderer("GeneratedStiltFirstInteriorLoftLookout", GetRelicSprite(), 24);
        interiorUpperWarmthRenderer = EnsureRenderer("GeneratedStiltFirstInteriorUpperWarmth", GetMoonWashSprite(), 21);
        interiorRoofCapRenderer = EnsureRenderer("GeneratedStiltFirstInteriorRoofCap", GetRoofSprite(), 25);
        interiorBedFrameRenderer = EnsureRenderer("GeneratedStiltFirstInteriorBedFrame", GetDeckSprite(), 23);
        interiorBedHeadboardRenderer = EnsureRenderer("GeneratedStiltFirstInteriorBedHeadboard", GetPostSprite(), 23);
        interiorBedRenderer = EnsureRenderer("GeneratedStiltFirstInteriorBed", GetLaundryClothSprite(), 24);
        interiorLampRenderer = EnsureRenderer("GeneratedStiltFirstInteriorLamp", GetShelterLampSprite(), 24);
        interiorStoveRenderer = EnsureRenderer("GeneratedStiltFirstInteriorStove", GetDeckSprite(), 24);
        interiorStoveGlowRenderer = EnsureRenderer("GeneratedStiltFirstInteriorStoveGlow", GetShelterLampSprite(), 25);
        interiorRoofPatchRenderer = EnsureRenderer("GeneratedStiltFirstInteriorRoofPatch", GetRoofSprite(), 23);
        interiorStorageRenderer = EnsureRenderer("GeneratedStiltFirstInteriorStorage", GetDeckSprite(), 23);
        // Flood water sits in front of the lower-room actor so the submerged part
        // of the body is actually hidden by water instead of appearing pasted on it.
        interiorFloodRenderer = EnsureRenderer("GeneratedStiltFirstInteriorFlood", GetFormalWaterSurfaceSprite(), 26);
        // V44 is a dedicated first-person vista. Its mask sits above ordinary world
        // renderers, while the debug HUD and the transition fade remain above it.
        // This keeps the same live world running instead of loading a disconnected
        // static scene or rebuilding hundreds of shelter renderers on every look.
        lookoutVistaMaskRenderer = EnsureRenderer("GeneratedStiltFirstLookoutVistaMask", GetMoonWashSprite(), 40);
        lookoutVistaWreckRenderer = EnsureRenderer("GeneratedStiltFirstLookoutVistaWreck", null, 43);
        lookoutVistaLighthouseRenderer = EnsureRenderer("GeneratedStiltFirstLookoutVistaLighthouse", null, 43);
        lookoutVistaBeamRenderer = EnsureRenderer("GeneratedStiltFirstLookoutVistaBeam", null, 44);
        lookoutVistaRoofRenderer = EnsureRenderer("GeneratedStiltFirstLookoutVistaRoof", null, 50);
        lookoutVistaCraneRenderer = EnsureRenderer("GeneratedStiltFirstLookoutVistaCrane", null, 51);
        interiorCutawayMask = EnsureSpriteMask("GeneratedStiltFirstInteriorCutawayMask", GetStoryRaggedMaskSprite(), 18);
        playerBagMask = EnsureSpriteMask("GeneratedStiltFirstPlayerBagStoryMask", GetStoryRaggedMaskSprite(), 14);
        boatPassengerBagMask = EnsureSpriteMask("GeneratedStiltFirstBoatPassengerBagStoryMask", GetStoryRaggedMaskSprite(), 3);
        boatWaterlineMask = EnsureSpriteMask("GeneratedStiltFirstBoatWaterlineMask", null, 8);

        EnsureList(stiltPosts, "GeneratedStiltFirstHousePost", 7, GetPostSprite(), 2);
        EnsureList(netLines, "GeneratedStiltFirstNetLine", 3, GetNetLineSprite(), -1);
        EnsureList(netWeights, "GeneratedStiltFirstNetWeight", 3, GetNetWeightSprite(), -1);
        EnsureList(netKnots, "GeneratedStiltFirstNetMeshKnot", 9, GetNetKnotSprite(), -1);
        EnsureList(netFloats, "GeneratedStiltFirstNetCorkFloat", 6, GetCorkFloatSprite(), -1);
        EnsureList(netSuspensionRopes, "GeneratedStiltFirstNetSuspensionRope", 2, GetNetLineSprite(), -1);
        EnsureList(netCaughtItems, "GeneratedStiltFirstNetCaughtItem", 4, GetFishSprite(), 14);
        EnsureList(incomingTideCarryItems, "GeneratedStiltFirstIncomingTideCarry", 5, GetIncomingTideCarryRippleSprite(), 13);
        EnsureList(harvestCarryItems, "GeneratedStiltFirstHarvestCarryItem", 3, GetFishSprite(), 15);
        EnsureList(washedAwayHarvestItems, "GeneratedStiltFirstWashedAwayHarvest", 3, GetFishSprite(), 14);
        EnsureList(netDamageMarkers, "GeneratedStiltFirstNetDamageMarker", 3, GetRepairPatchSprite(), 15);
        EnsureList(waveStrips, "GeneratedStiltFirstWaveStrip", 5, GetFoamSprite(), -17);
        EnsureList(houseRepairMarks, "GeneratedStiltFirstHouseRepairPatch", 4, GetRepairPatchSprite(), 10);
        EnsureList(houseSaltStreaks, "GeneratedStiltFirstHouseSaltStreak", 6, GetHouseSaltStreakSprite(), 10);
        EnsureList(shelterTideZoneWearRenderers, "GeneratedStiltFirstTideZonePostWear", 4, GetFormalStiltTideZoneWearSprite(), 12);
        EnsureList(previousHighWaterSaltMarks, "GeneratedStiltFirstPreviousSaltMark", 4, GetFormalStiltTideZoneWearSprite(), 12);
        EnsureList(shelterWarmRays, "GeneratedStiltFirstShelterWarmRay", 4, GetShelterWarmRaySprite(), 11);
        EnsureList(houseDiagonalBraces, "GeneratedStiltFirstDiagonalBrace", 6, GetDiagonalBraceSprite(), 4);
        EnsureList(houseGableRoofs, "GeneratedStiltFirstHouseGableRoof", 3, GetHouseGableRoofSprite(), 11);
        EnsureList(denseStiltPosts, "GeneratedStiltFirstDenseStiltPost", 12, GetDenseStiltPostSprite(), 3);
        EnsureList(brokenHousePieces, "GeneratedStiltFirstBrokenHousePiece", 7, GetBrokenHousePieceSprite(), 18);
        EnsureList(snappedStiltPosts, "GeneratedStiltFirstSnappedStiltPost", 6, GetSnappedStiltPostSprite(), 18);
        EnsureList(departureRouteWakes, "GeneratedStiltFirstDepartureRouteWake", 5, GetDepartureRouteWakeSprite(), 16);
        EnsureList(laundryCloths, "GeneratedStiltFirstLaundryCloth", 4, GetLaundryClothSprite(), 10);
        EnsureList(pillarCheckKnots, "GeneratedStiltFirstPillarCheckKnot", 3, GetRepairPatchSprite(), 14);
        EnsureList(shipwreckRibs, "GeneratedStiltFirstShipwreckRib", 5, GetShipwreckRibSprite(), 1);
        EnsureList(boatRigging, "GeneratedStiltFirstBoatRigging", 3, GetBoatRiggingSprite(), 6);
        EnsureList(boatPatchStitches, "GeneratedStiltFirstBoatSailPatchStitch", 5, GetBoatSailPatchStitchSprite(), 7);
        EnsureList(waterWracks, "GeneratedStiltFirstWaterWrack", 6, GetWaterWrackSprite(), -16);
        EnsureList(sailingFlowCrests, "GeneratedStiltFirstSailingFlowCrest", 5, GetFormalSeaCurrentCrestSprite(), 1);
        EnsureList(vortexCrests, "GeneratedStiltFirstVortexCrest", 7, GetFormalSeaCurrentCrestSprite(), -4);
        EnsureList(cloudRenderers, "GeneratedStiltFirstCloud", 5, GetCloudMassSprite(), -60);
        EnsureList(horizonCloudBanks, "GeneratedStiltFirstHorizonCloudBank", 3, GetCloudMassSprite(), -59);
        EnsureList(farSeaBands, "GeneratedStiltFirstFarSeaBand", 3, GetWaterSprite(), -54);
        EnsureList(nearSeaColorBands, "GeneratedStiltFirstNearSeaColorBand", 4, GetWaterSprite(), -46);
        EnsureList(dayNightSkyBands, "GeneratedStiltFirstDayNightSkyBand", 4, GetMoonWashSprite(), -62);
        EnsureList(weatherRainStreaks, "GeneratedStiltFirstWeatherRain", 18, GetNetLineSprite(), -10);
        EnsureList(formalBoardwalkSegments, "GeneratedStiltFirstFormalBoardwalk", 3, GetFormalBoardwalkSprite(), 0);
        EnsureList(interiorWallPlanks, "GeneratedStiltFirstInteriorWallPlank", 14, GetDeckSprite(), 21);
        EnsureList(interiorWindowRenderers, "GeneratedStiltFirstInteriorWindow", 2, GetShelterLampSprite(), 24);
        EnsureList(interiorStairTreads, "GeneratedStiltFirstInteriorStairTread", 6, GetDeckSprite(), 24);
        EnsureList(interiorLoftStairTreads, "GeneratedStiltFirstInteriorLoftStairTread", 7, GetDeckSprite(), 24);
        EnsureList(interiorLoftRafters, "GeneratedStiltFirstInteriorLoftRafter", 4, GetPostSprite(), 23);
        EnsureList(interiorStoredItems, "GeneratedStiltFirstInteriorStoredItem", 4, GetDeckSprite(), 24);
        EnsureList(houseRoofRepairMarks, "GeneratedStiltFirstHouseRoofRepair", 3, GetBrokenHousePieceSprite(), 12);
        EnsureList(houseWallRepairMarks, "GeneratedStiltFirstHouseWallRepair", 2, GetBrokenHousePieceSprite(), 12);
        EnsureList(houseInteriorVoidRenderers, "GeneratedStiltFirstHouseInteriorVoid", 4, GetStoryRaggedMaskSprite(), 4);
        EnsureMaskList(houseRoofHoleMasks, "GeneratedStiltFirstHouseRoofHoleMask", 3, GetStoryRaggedMaskSprite(), 5);
        EnsureMaskList(houseWindowHoleMasks, "GeneratedStiltFirstHouseWindowHoleMask", 2, GetStoryRaggedMaskSprite(), 5);
        EnsureMaskList(houseWallGapMasks, "GeneratedStiltFirstHouseWallGapMask", 2, GetStoryRaggedMaskSprite(), 5);

        titleText = EnsureText("GeneratedStiltFirstTitle", 0.052f, 90);
        statusText = EnsureText("GeneratedStiltFirstStatus", 0.031f, 90);
        loopGoalText = EnsureText("GeneratedStiltFirstLoopGoal", 0.029f, 90);
        controlText = EnsureText("GeneratedStiltFirstControls", 0.032f, 90);
        boatBoardPromptText = EnsureText("GeneratedStiltFirstBoatBoardPrompt", 0.032f, 91);
    }

    private SpriteRenderer EnsureRenderer(string objectName, Sprite sprite, int sortingOrder)
    {
        Transform child = FindDescendantByName(transform, objectName);
        GameObject node = child != null ? child.gameObject : new GameObject(objectName);
        node.transform.SetParent(EnsureLayerRoot(GetLayerNameForObject(objectName)), false);
        SetGeneratedNodePersistence(node);

        SpriteRenderer renderer = node.GetComponent<SpriteRenderer>();
        if (renderer == null)
        {
            renderer = node.AddComponent<SpriteRenderer>();
        }

        renderer.sprite = sprite;
        renderer.sortingOrder = sortingOrder;
        if (!generatedRenderers.Contains(renderer))
        {
            generatedRenderers.Add(renderer);
        }

        return renderer;
    }

    private void EnsureList(List<SpriteRenderer> list, string prefix, int count, Sprite sprite, int sortingOrder)
    {
        while (list.Count < count)
        {
            list.Add(EnsureRenderer($"{prefix}_{list.Count:00}", sprite, sortingOrder));
        }
    }

    private SpriteMask EnsureSpriteMask(string objectName, Sprite sprite, int sortingOrder)
    {
        Transform child = FindDescendantByName(transform, objectName);
        GameObject node = child != null ? child.gameObject : new GameObject(objectName);
        node.transform.SetParent(EnsureLayerRoot(GetLayerNameForObject(objectName)), false);
        SetGeneratedNodePersistence(node);

        SpriteMask mask = node.GetComponent<SpriteMask>();
        if (mask == null)
        {
            mask = node.AddComponent<SpriteMask>();
        }

        mask.sprite = sprite;
        mask.alphaCutoff = 0.45f;
        mask.isCustomRangeActive = true;
        mask.frontSortingOrder = sortingOrder + 1;
        mask.backSortingOrder = sortingOrder - 1;
        return mask;
    }

    private void EnsureMaskList(List<SpriteMask> list, string prefix, int count, Sprite sprite, int sortingOrder)
    {
        while (list.Count < count)
        {
            list.Add(EnsureSpriteMask($"{prefix}_{list.Count:00}", sprite, sortingOrder));
        }
    }

    private TextMesh EnsureText(string objectName, float characterSize, int sortingOrder)
    {
        Transform child = FindDescendantByName(transform, objectName);
        GameObject node = child != null ? child.gameObject : new GameObject(objectName);
        node.transform.SetParent(EnsureLayerRoot(GetLayerNameForObject(objectName)), false);
        SetGeneratedNodePersistence(node);

        TextMesh textMesh = node.GetComponent<TextMesh>();
        if (textMesh == null)
        {
            textMesh = node.AddComponent<TextMesh>();
            textMesh.anchor = TextAnchor.UpperLeft;
            textMesh.alignment = TextAlignment.Left;
            textMesh.fontSize = 48;
            textMesh.richText = false;
        }

        textMesh.characterSize = characterSize;
        MeshRenderer meshRenderer = node.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            meshRenderer.sortingOrder = sortingOrder;
        }

        return textMesh;
    }

    private static void SetGeneratedNodePersistence(GameObject node)
    {
        // Runtime render nodes are derived entirely from controller state and formal
        // Resources sprites. Persisting them duplicates hundreds of SpriteRenderers
        // and generated fallback textures inside the Scene/Prefab. In Edit mode they
        // stay rendered but hidden from the authored hierarchy and are regenerated on
        // load; in Play/build they are ordinary runtime GameObjects.
        node.hideFlags = Application.isPlaying
            ? HideFlags.None
            : HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;
    }

    private Transform EnsureLayerRoot(string layerName)
    {
        Transform layer = transform.Find(layerName);
        if (layer != null)
        {
            return layer;
        }

        GameObject layerObject = new GameObject(layerName);
        layerObject.transform.SetParent(transform, false);
        layerObject.transform.localPosition = Vector3.zero;
        layerObject.transform.localRotation = Quaternion.identity;
        layerObject.transform.localScale = Vector3.one;
        return layerObject.transform;
    }

    private static Transform FindDescendantByName(Transform root, string objectName)
    {
        Transform direct = root.Find(objectName);
        if (direct != null)
        {
            return direct;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDescendantByName(root.GetChild(i), objectName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private void CleanupRetiredGeneratedObjects()
    {
        if (retiredHierarchyCleaned)
        {
            return;
        }

        retiredHierarchyCleaned = true;
        Transform[] descendants = GetComponentsInChildren<Transform>(true);
        for (int i = descendants.Length - 1; i >= 0; i--)
        {
            Transform descendant = descendants[i];
            if (descendant == null || descendant == transform || !IsRetiredGeneratedObject(descendant.name))
            {
                continue;
            }

            if (Application.isPlaying)
            {
                descendant.gameObject.SetActive(false);
                Destroy(descendant.gameObject);
            }
            else
            {
                DestroyImmediate(descendant.gameObject);
            }
        }
    }

    private static bool IsRetiredGeneratedObject(string objectName)
    {
        for (int i = 0; i < RetiredGeneratedObjectPrefixes.Length; i++)
        {
            if (objectName.StartsWith(RetiredGeneratedObjectPrefixes[i], StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetLayerNameForObject(string objectName)
    {
        if (objectName.Contains("Backdrop") || objectName.Contains("HorizonSea") || objectName.Contains("FarSeaBand") || objectName.Contains("Cloud"))
        {
            return "GeneratedStiltFirstLayer_Backdrop";
        }

        if (objectName.Contains("Tide") || objectName.Contains("Wave") || objectName.Contains("WaterWrack") || objectName.Contains("StormLine") || objectName.Contains("SailingSea") || objectName.Contains("SailingBuoy") || objectName.Contains("Vortex"))
        {
            return "GeneratedStiltFirstLayer_Tide";
        }

        if (objectName.Contains("Net"))
        {
            return "GeneratedStiltFirstLayer_Net";
        }

        if (objectName.Contains("Boat") || objectName.Contains("Shipwreck") || objectName.Contains("Sailing"))
        {
            return "GeneratedStiltFirstLayer_Boat";
        }

        if (objectName.Contains("Moon") || objectName.Contains("Lighthouse") || objectName.Contains("RouteOpening"))
        {
            return "GeneratedStiltFirstLayer_Sky";
        }

        if (objectName.Contains("Harvest"))
        {
            return "GeneratedStiltFirstLayer_Props";
        }

        if (objectName.Contains("Title") || objectName.Contains("Status") || objectName.Contains("Controls") || objectName.Contains("Prompt") || objectName.Contains("LoopGoal"))
        {
            return "GeneratedStiltFirstLayer_Debug";
        }

        return "GeneratedStiltFirstLayer_Shelter";
    }

    private void UpdateVisuals(float time)
    {
        UpdateShelterCameraFraming();
        float presentationCenterX = GetActiveCameraCenterX();
        float pulse = Mathf.Sin(time * 2.2f) * 0.5f + 0.5f;
        float storm01 = GetVisualStormPressure01();
        float daylight01 = GetDaylight01();
        float night01 = 1f - daylight01;
        float shelterImpact01 = Mathf.Clamp01(shelterImpactTimer / 1.35f);
        float shelterImpactEnvelope = shelterImpact01 * shelterImpact01;
        Vector2 shelterShakeOffset = new Vector2(
            Mathf.Sin(time * 18f) * 0.045f * shelterImpactEnvelope,
            Mathf.Abs(Mathf.Sin(time * 13f)) * 0.012f * shelterImpactEnvelope);
        float shelterRock = Mathf.Sin(time * 16f) * 1.35f * shelterImpactEnvelope;
        Color skyColor = GetReadableSkyColor(daylight01, storm01);
        bool useFormalSeaSky = GetFormalDaySeaSkySprite() != null && GetFormalNightSeaSkySprite() != null;

        backdropRenderer.sprite = GetMoonWashSprite();
        SetWorldSize(backdropRenderer, new Vector2(presentationCenterX, 0f), new Vector2(16.2f, 7.6f), skyColor, 0f);
        if (useFormalSeaSky)
        {
            daySeaSkyRenderer.sprite = GetFormalDaySeaSkySprite();
            nightSeaSkyRenderer.sprite = GetFormalNightSeaSkySprite();
            bool useHdDay = GetFormalSprite(ref formalDaySeaSkyHdSprite, "AIDaySeaSkyHD") != null;
            bool useHdNight = GetFormalSprite(ref formalNightSeaSkyHdSprite, "AINightSeaSkyHD") != null;
            Vector2 plateSize = useHdDay ? new Vector2(16.2f, 7.76f) : new Vector2(16.2f, 10.92f);
            Vector2 platePosition = useHdDay
                ? new Vector2(presentationCenterX, 0f)
                : new Vector2(presentationCenterX, 0.48f);
            bool tintHdDayIntoNight = useHdDay && !useHdNight;
            // 用户原始画面已证明：正式日间整幅底图会把房、船和海的相对
            // 色彩压成同一层冷灰。白天改由干净基础天色和独立云层表达；
            // 若缺少正式夜图，只在夜间低强度复用该底图承接云层细节。
            float dayPlateAlpha = tintHdDayIntoNight ? night01 * 0.72f : 0f;
            float nightPlateAlpha = night01 * (useHdNight ? 0.96f : 0.48f);
            Color dayPlateTint = tintHdDayIntoNight
                // Keep the authored cloud contrast at night. The former near-black multiplier
                // flattened every cloud into one gray-purple field even though the HD plate
                // itself still contained useful detail.
                ? Color.Lerp(new Color(0.36f, 0.46f, 0.52f, dayPlateAlpha), new Color(1f, 0.98f, 0.94f, dayPlateAlpha), daylight01)
                : new Color(1f, 1f, 1f, Mathf.Clamp01(dayPlateAlpha));
            Color stormPlateTint = Color.Lerp(Color.white, new Color(0.62f, 0.72f, 0.74f, 1f), storm01 * 0.46f);
            dayPlateTint = new Color(
                dayPlateTint.r * stormPlateTint.r,
                dayPlateTint.g * stormPlateTint.g,
                dayPlateTint.b * stormPlateTint.b,
                dayPlateTint.a);
            SetWorldSize(daySeaSkyRenderer, platePosition, plateSize, dayPlateTint, 0f);
            if (tintHdDayIntoNight)
            {
                nightPlateAlpha = 0f;
            }
            SetWorldSize(
                nightSeaSkyRenderer,
                platePosition,
                plateSize,
                new Color(stormPlateTint.r, stormPlateTint.g, stormPlateTint.b, Mathf.Clamp01(nightPlateAlpha)),
                0f);
        }
        else
        {
            SetEnabled(daySeaSkyRenderer, false);
            SetEnabled(nightSeaSkyRenderer, false);
        }
        UpdateDayNightSkyBandVisuals(time, daylight01, storm01);
        if (useFormalSeaSky)
        {
            SetEnabled(horizonSeaRenderer, false);
        }
        else
        {
            SetWorldSize(horizonSeaRenderer, new Vector2(presentationCenterX, -0.92f), new Vector2(14.5f, 1.48f), GetReadableHorizonSeaColor(daylight01, storm01), 0f);
        }
        UpdateFarSeaVisuals(time, daylight01, storm01);
        UpdateNearSeaColorBandVisuals(time, daylight01, storm01);
        UpdateCloudVisuals(time, daylight01, storm01);
        UpdateHorizonCloudBankVisuals(time, daylight01, storm01);
        UpdateWeatherVisuals(time, storm01);
        // 正式昼夜天空、云层和海面已经共同拥有环境色。再叠一张覆盖整幅画面的
        // 月光染色会把白天的木头、皮肤和帆布统一压成青灰色；月亮本体仍保留，
        // 但正常状态不再使用全屏染色。受寒和死亡反馈由独立前景层负责。
        SetEnabled(ambientMoonWashRenderer, false);
        // Day/night colour is already authored by the sky and sea plates. The old
        // foreground wash sat above the character, house and boat, desaturating all
        // three even at noon. Keep this overlay off by default; survival feedback may
        // explicitly enable it later in the frame for cold or death states.
        SetEnabled(foregroundMoonWashRenderer, false);
        UpdateReturnPressureVisuals(time);
        SetEnabled(harvestCarryItems, false);
        SetEnabled(washedAwayHarvestItems, false);
        SetEnabled(interiorStoredItems, false);
        SetEnabled(stormRescueRenderers, false);
        SetEnabled(stormRescueRopeRenderers, false);
        SetEnabled(shelterTideZoneWearRenderers, false);
        SetEnabled(previousHighWaterSaltMarks, false);
        forecastTideNotches.Hide();
        SetEnabled(v30RepairOwnerRenderers, false);
        SetEnabled(v34ExteriorRepairOwnerRenderers, false);
        SetEnabled(v35InteriorRepairOwnerRenderers, false);
        SetLookoutVistaEnabled(viewMode == SliceViewMode.Lookout);
        // The deep foreground veil belongs only to the exterior formal-water pass.
        // Reset it before every view branch so entering the interior, lookout or sailing
        // view cannot leave a rectangular underwater tint behind.
        SetEnabled(foregroundDeepWaterOcclusionRenderer, false);
        if (barrenIsland != null)
        {
            barrenIsland.UpdatePresentation(
                viewMode == SliceViewMode.Shelter && state != SliceState.FinalDeparture,
                GetPlayerStandingFeetY(WalkLane.TideFlat),
                playerPosition,
                new Vector2(
                    TideBarrenIslandController.ShelterDeliveryX,
                    GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.02f),
                new Vector2(
                    EscapeBoatStagingX,
                    GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.02f),
                GetNaturalSailingWindSpeed(),
                GetOceanSample(TideBarrenIslandController.CisternX).SurfaceY,
                time,
                pendingRepairChoice == RepairChoice.Cistern && !repairChoiceApplied,
                pendingRepairChoice == RepairChoice.Cistern ? repairWorkProgress : 0f);
        }
        wrackLine?.UpdatePresentation(
            viewMode == SliceViewMode.Shelter && state != SliceState.FinalDeparture);
        if (heavyWreckSalvage != null)
        {
            TideOceanSample heavyOcean = GetOceanSample(heavyWreckSalvage.SampleWorldX);
            heavyWreckSalvage.UpdatePresentation(
                viewMode == SliceViewMode.Shelter && state != SliceState.FinalDeparture,
                GetPlayerStandingFeetY(WalkLane.TideFlat),
                playerPosition,
                new Vector2(
                    TideBarrenIslandController.ShelterDeliveryX,
                    GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.02f),
                new Vector2(
                    EscapeBoatStagingX,
                    GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.02f),
                heavyOcean,
                time);
        }

        if (viewMode == SliceViewMode.Lookout)
        {
            SetEnabled(foregroundWaterOcclusionRenderer, false);
            UpdateLookoutVistaVisuals(time, daylight01, storm01);
            UpdateSurvivalOverlay();
            UpdateText(storm01);
            UpdateBoatViewTransitionVisual();
            return;
        }

        if (viewMode == SliceViewMode.Sailing)
        {
            SetEnabled(foregroundWaterOcclusionRenderer, false);
            SetEnabled(shelterWaveImpactRenderer, false);
            SetEnabled(shelterDamageRenderer, false);
            SetEnabled(prepRopeCoilRenderer, false);
            SetEnabled(prepBucketRenderer, false);
            SetEnabled(prepStakeRenderer, false);
            UpdateSailingSceneVisuals(time, storm01, daylight01);
            UpdateSurvivalOverlay();
            UpdateText(storm01);
            UpdateBoatViewTransitionVisual();
            return;
        }

        if (viewMode == SliceViewMode.Interior)
        {
            SetEnabled(foregroundWaterOcclusionRenderer, false);
            UpdateInteriorSceneVisuals(time, daylight01, storm01);
            UpdateStormRescueVisuals(time, storm01);
            // Interior prep stations are real lower-deck objects. Rendering this pass
            // here replaces the low-resolution sprites created by EnsureScene before
            // the interior early-return, and hides them outside their valid time window.
            UpdateTidePrepVisuals(time);
            UpdateHarvestVisuals(pulse);
            UpdateSurvivalOverlay();
            UpdateText(storm01);
            UpdateBoatViewTransitionVisual();
            return;
        }

        SetEnabled(interiorBackdropRenderer, false);
        SetEnabled(interiorLowerFloorRenderer, false);
        SetEnabled(interiorUpperFloorRenderer, false);
        SetEnabled(interiorLoftBackdropRenderer, false);
        SetEnabled(interiorLoftFloorRenderer, false);
        SetEnabled(interiorStairRenderer, false);
        SetEnabled(interiorLoftStairRenderer, false);
        SetEnabled(interiorLoftLookoutTableRenderer, false);
        SetEnabled(interiorLoftLookoutRenderer, false);
        SetEnabled(interiorUpperWarmthRenderer, false);
        SetEnabled(interiorRoofCapRenderer, false);
        SetEnabled(interiorBedFrameRenderer, false);
        SetEnabled(interiorBedHeadboardRenderer, false);
        SetEnabled(interiorBedRenderer, false);
        SetEnabled(interiorLampRenderer, false);
        SetEnabled(interiorStoveRenderer, false);
        SetEnabled(interiorStoveGlowRenderer, false);
        SetEnabled(interiorRoofPatchRenderer, false);
        SetEnabled(interiorStorageRenderer, false);
        SetEnabled(interiorFloodRenderer, false);
        SetMaskEnabled(interiorCutawayMask, false);
        SetEnabled(interiorWallPlanks, false);
        SetEnabled(interiorWindowRenderers, false);
        SetEnabled(interiorStairTreads, false);
        SetEnabled(interiorLoftStairTreads, false);
        SetEnabled(interiorLoftRafters, false);
        SetEnabled(interiorStoredItems, false);

        SetEnabled(sailingSeaRenderer, false);
        SetEnabled(sailingWreckPointRenderer, false);
        SetEnabled(sailingBuoyPointRenderer, false);
        SetEnabled(sailingBuoyTetherRenderer, false);
        SetEnabled(sailingBuoySinkerRenderer, false);
        SetEnabled(sailingSalvagePointRenderer, false);
        SetEnabled(sailingSalvageWakeRenderer, false);
        SetEnabled(sailingSalvageHookRopeRenderer, false);
        SetEnabled(sailingSalvageHookRopeEndRenderer, false);
        SetEnabled(sailingSalvageHookRenderer, false);
        SetEnabled(sailingReefPointRenderer, false);
        SetEnabled(sailingReefFoamRenderer, false);
        SetEnabled(sailingRangeBreakerRenderer, false);
        SetEnabled(boatIngressWaterRenderer, false);
        SetEnabled(sailingBailBucketRenderer, false);
        SetEnabled(sailingBailSplashRenderer, false);
        SetEnabled(sailingFlowCrests, false);
        SetEnabled(vortexCrests, false);
        SetEnabled(routeVortexRenderer, false);
        SetEnabled(routeVortexInnerRenderer, false);
        SetEnabled(routeVortexSurfaceRenderer, false);
        naturalWaterSurfaceRenderer.maskInteraction = SpriteMaskInteraction.None;
        SetEnabled(boatBackRigRenderer, false);
        SetEnabled(boatCockpitRenderer, false);
        SetEnabled(boatPassengerRenderer, false);
        SetEnabled(boatPassengerGunwaleRenderer, false);
        SetEnabled(boatRudderRenderer, false);
        SetMaskEnabled(boatWaterlineMask, false);
        SetMaskEnabled(boatPassengerBagMask, false);
        SetEnabled(stormSurgeWallRenderer, false);
        SetEnabled(brokenHousePieces, false);
        SetEnabled(snappedStiltPosts, false);
        SetEnabled(departureRouteWakes, false);
        TideOceanSample shelterCenterOcean = GetOceanSample(0f);
        float visibleShelterWaterY = shelterCenterOcean.SurfaceY;
        bool useFormalWater = GetFormalWaterSurfaceSprite() != null;
        bool useRepeatingV56Water = viewMode == SliceViewMode.Shelter &&
            HasCompleteV56OceanPresentation();
        float waterCenterX = useRepeatingV56Water ? OutdoorWaterCenterX : 0f;
        float waterWidth = useRepeatingV56Water ? OutdoorWaterWidth : 16.4f + storm01 * 0.45f;
        if (useFormalWater)
        {
            // The authored sprite owns the only readable surface: its painted crest,
            // local slope and wave detail stay at the ocean sample. Separate support
            // layers may continue below its crop, but their top edges must remain well
            // inside the opaque body so they can never look like another waterline.
            float waterBodyHeight = 2.7f + storm01 * 0.18f;
            Color formalWaterColor = GetFormalWaterTint(storm01);
            float waterRotation = Mathf.Atan(shelterCenterOcean.Slope) * Mathf.Rad2Deg * 0.08f;
            float formalBodyBottomY = visibleShelterWaterY - waterBodyHeight +
                FormalWaterAverageCrestOffset;
            float deepWaterBottomY = lowWaterY - 2.2f;
            if (HasHdFormalWater())
            {
                // AIWaterBodyHD is opaque below its crest but is still a finite crop.
                // At high tide that crop rises with the real surface and exposes the
                // sky underneath. This solid layer starts behind the painted body and
                // extends below every supported camera without changing crest geometry.
                float deepWaterTopY = formalBodyBottomY + 0.22f;
                Color deepWaterColor = Color.Lerp(
                    new Color(0.38f, 0.52f, 0.56f, 1f),
                    new Color(0.7f, 0.82f, 0.8f, 1f),
                    daylight01);
                deepWaterColor = Color.Lerp(
                    deepWaterColor,
                    new Color(0.42f, 0.52f, 0.56f, 1f),
                    storm01 * 0.55f);
                // Continue only the opaque lower body of the authored sea image.
                // The crop deliberately excludes every crest and sky pixel, so it
                // can be stretched downward as deep-water texture without creating
                // a second surface or distorting the visible waves above.
                waterRenderer.sprite = GetFormalSubsurfaceBandSprite();
                ApplyUnlitSpriteMaterial(waterRenderer);
                waterRenderer.sortingOrder = naturalWaterSurfaceRenderer.sortingOrder - 1;
                SetWorldSize(
                    waterRenderer,
                    new Vector2(waterCenterX, (deepWaterTopY + deepWaterBottomY) * 0.5f),
                    new Vector2(waterWidth + 0.2f, deepWaterTopY - deepWaterBottomY),
                    deepWaterColor,
                    waterRotation);
            }
            else
            {
                waterRenderer.sprite = GetMoonWashSprite();
                waterRenderer.sortingOrder = -20;
                SetWorldSize(waterRenderer, new Vector2(0f, (visibleShelterWaterY + lowWaterY - 3.2f) * 0.5f), new Vector2(16.4f, Mathf.Max(0.4f, visibleShelterWaterY - lowWaterY + 3.2f)), Color.Lerp(new Color(0.05f, 0.3f, 0.36f, 0.7f), dangerTideColor, storm01 * 0.42f), 0f);
            }
            naturalWaterSurfaceRenderer.sprite = GetFormalWaterSurfaceSprite();
            // The cropped HD water asset starts at its visible crest. Its top edge is
            // therefore close to the same Y used by tide, swimming and net contact.
            // Sparse spray reaches above the average crest by roughly 0.45 world
            // units at this scale, so the painted water body is raised by that amount.
            SetRepeatingWorldSize(
                naturalWaterSurfaceRenderer,
                new Vector2(waterCenterX, visibleShelterWaterY - waterBodyHeight * 0.5f + FormalWaterAverageCrestOffset),
                new Vector2(waterWidth, waterBodyHeight),
                formalWaterColor,
                waterRotation,
                useRepeatingV56Water);
            // One translucent copy sits in front of world objects. Its transparent sky
            // remains empty, while pixels below the authored crest tint submerged
            // stilts, deck boards, net and the survivor's lower body. Without this pass
            // a numerically high tide still looked like a background painted behind them.
            foregroundWaterOcclusionRenderer.sprite = GetFormalWaterSurfaceSprite();
            float boardwalkSubmersion01 = Mathf.InverseLerp(
                GetPlayerStandingFeetY(WalkLane.TideFlat) - 0.08f,
                GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.58f,
                visibleShelterWaterY);
            Color foregroundWaterTint = GetFormalWaterTint(storm01);
            foregroundWaterTint.a = Mathf.Lerp(0.11f, 0.29f, boardwalkSubmersion01) + storm01 * 0.035f;
            SetRepeatingWorldSize(
                foregroundWaterOcclusionRenderer,
                new Vector2(waterCenterX, visibleShelterWaterY - waterBodyHeight * 0.5f + FormalWaterAverageCrestOffset),
                new Vector2(waterWidth, waterBodyHeight),
                foregroundWaterTint,
                waterRotation,
                useRepeatingV56Water);
            if (HasHdFormalWater())
            {
                // The front formal copy tints objects only until its finite crop ends.
                // Continue that underwater attenuation below the crop with a separate
                // low-alpha veil. Its transparent top begins inside the painted body and
                // fades in across the crop boundary, avoiding both a bright cutoff and a
                // dark double band.
                float foregroundDeepTopY = formalBodyBottomY + 0.65f;
                Color foregroundDeepTint = Color.Lerp(
                    new Color(0.035f, 0.12f, 0.145f, 1f),
                    new Color(0.055f, 0.16f, 0.18f, 1f),
                    daylight01);
                foregroundDeepTint = Color.Lerp(
                    foregroundDeepTint,
                    new Color(0.03f, 0.07f, 0.09f, 1f),
                    storm01 * 0.45f);
                // Open water now keeps the authored lower-body texture behind every
                // object. This front pass only models depth absorption; keeping it
                // translucent prevents the edge of the house composition from turning
                // into a solid rectangular color change.
                foregroundDeepTint.a = Mathf.Clamp(
                    Mathf.Lerp(0.32f, 0.56f, boardwalkSubmersion01) + storm01 * 0.06f,
                    0.28f,
                    0.68f);
                foregroundDeepWaterOcclusionRenderer.sprite = GetSubsurfaceFadeSprite();
                ApplyUnlitSpriteMaterial(foregroundDeepWaterOcclusionRenderer);
                foregroundDeepWaterOcclusionRenderer.sortingOrder =
                    foregroundWaterOcclusionRenderer.sortingOrder - 1;
                SetWorldSize(
                    foregroundDeepWaterOcclusionRenderer,
                    new Vector2(waterCenterX, (foregroundDeepTopY + deepWaterBottomY) * 0.5f),
                    new Vector2(waterWidth + 0.2f, foregroundDeepTopY - deepWaterBottomY),
                    foregroundDeepTint,
                    waterRotation);
            }
            SetEnabled(waterFoamRenderer, false);
        }
        else
        {
            SetEnabled(naturalWaterSurfaceRenderer, false);
            SetEnabled(foregroundWaterOcclusionRenderer, false);
            SetWorldSize(waterRenderer, new Vector2(0f, (visibleShelterWaterY + lowWaterY - 3.2f) * 0.5f), new Vector2(16.4f, Mathf.Max(0.4f, visibleShelterWaterY - lowWaterY + 3.2f)), Color.Lerp(tideColor, dangerTideColor, storm01 * 0.45f), 0f);
            SetWorldSize(waterFoamRenderer, new Vector2(0f, visibleShelterWaterY + 0.05f), new Vector2(16.2f, 0.32f), Color.Lerp(Color.white, tideColor, 0.3f), Mathf.Atan(shelterCenterOcean.Slope) * Mathf.Rad2Deg * 0.08f);
        }

        bool useFormalHouse = GetFormalHouseSprite() != null;
        if (useFormalHouse)
        {
            // The authored cutout already contains roof, veranda, deck, and dense
            // stilts. Keeping the old procedural parts would make a double house.
            houseRenderer.sortingOrder = 5;
            float restoration01 = GetShelterRestoration01();
            Color abandonedTint = Color.Lerp(
                new Color(0.42f, 0.5f, 0.49f, 1f),
                new Color(0.68f, 0.74f, 0.71f, 1f),
                daylight01);
            Color livedInTint = Color.Lerp(new Color(0.67f, 0.75f, 0.74f, 1f), Color.white, daylight01);
            Color houseLightTint = Color.Lerp(abandonedTint, livedInTint, restoration01);
            EnsureV34HouseExteriorResourcesLoaded();
            bool useV69ExteriorOwners = state != SliceState.FinalDeparture &&
                HasCompleteV69CurrentHousePresentation();
            bool useV34ExteriorOwners = state != SliceState.FinalDeparture &&
                !useV69ExteriorOwners && HasCompleteV34HouseExteriorPresentation();
            bool useRegisteredExteriorOwners = useV69ExteriorOwners || useV34ExteriorOwners;
            Sprite registeredExteriorFrame = useV69ExteriorOwners
                ? formalHouseV69ActiveBase.StableBase
                : useV34ExteriorOwners
                    ? formalHouseV34ExteriorCatalog.StableBase
                : GetRegisteredExteriorFrame(time, storm01);
            if (registeredExteriorFrame != null)
            {
                houseRenderer.sprite = registeredExteriorFrame;
                // V30 already owns the low-saturation saltwood palette. A strong
                // legacy multiplier made its windows and structural joints unreadable.
                float authoredLight = Mathf.Lerp(0.72f, 0.98f, daylight01);
                houseLightTint = Color.Lerp(
                    new Color(authoredLight * 0.9f, authoredLight * 0.95f, authoredLight, 1f),
                    Color.white,
                    restoration01 * 0.22f);
                if (useRegisteredExteriorOwners || HasCompleteV32ArtPresentation())
                {
                    // V32 已按人物与帆船的灰褐盐蚀色板完成调色。这里只保留
                    // 昼夜亮度，不再叠一层青灰，避免屋子比船和人脏灰一档。
                    float neutralLight = Mathf.Lerp(0.8f, 1f, daylight01);
                    houseLightTint = new Color(
                        neutralLight * 0.98f,
                        neutralLight * 0.99f,
                        neutralLight,
                        1f);
                }
            }
            SetWorldSize(houseRenderer, GetFormalHouseWorldPosition() + shelterShakeOffset, GetFormalHouseWorldSize(), houseLightTint, shelterRock);
            if (useRegisteredExteriorOwners)
            {
                ApplyV34ExteriorRepairPresentation(
                    GetFormalHouseWorldPosition() + shelterShakeOffset,
                    houseLightTint,
                    shelterRock);
            }
            SetEnabled(roofRenderer, false);
            SetEnabled(deckRenderer, false);
            SetEnabled(verandaRenderer, false);
            SetEnabled(porchRoofRenderer, false);
            SetEnabled(longEaveRenderer, false);
            SetEnabled(underHouseShadowRenderer, false);
            SetEnabled(houseGableRoofs, false);
            SetEnabled(denseStiltPosts, false);
        }
        else
        {
            SetWorldSize(houseRenderer, houseAnchor + new Vector2(-0.18f, 0.5f) + shelterShakeOffset, new Vector2(4.15f, 2.0f), Color.Lerp(saltWoodColor, wetWoodColor, 0.28f), shelterRock);
            SetWorldSize(roofRenderer, houseAnchor + new Vector2(-0.18f, 1.78f), new Vector2(4.75f, 1.12f), new Color(0.56f, 0.2f, 0.11f, 1f), 0f);
            SetWorldSize(deckRenderer, houseAnchor + new Vector2(0.05f, -0.26f), new Vector2(5.25f, 0.34f), saltWoodColor, 0f);
            SetEnabled(verandaRenderer, false);
            SetEnabled(porchRoofRenderer, false);
            SetEnabled(longEaveRenderer, false);
            UpdateStiltHouseArchitectureVisuals();
        }
        UpdateShelterStoryStateVisuals(shelterShakeOffset, shelterRock);
        UpdateShelterWaveImpactVisuals(time);
        UpdateShelterDamageVisuals();
        UpdatePreviousHighWaterSaltMarks();
        UpdateForecastTideNotches();
        SetEnabled(boardwalkPathRenderer, false);
        tideFlatPathRenderer.sprite = GetDeckSprite();
        float pathLeft = GetBoardwalkVisualLeft();
        float pathRight = GetTideFlatVisiblePathRight();
        float pathSurfaceY = GetPlayerStandingFeetY(WalkLane.TideFlat);
        float pathSubmerge01 = Mathf.InverseLerp(pathSurfaceY - 0.12f, pathSurfaceY + 0.18f, currentWaterY);
        Color pathColor = Color.Lerp(
            new Color(0.95f, 0.88f, 0.72f, 0.96f),
            new Color(0.42f, 0.72f, 0.72f, 0.42f),
            pathSubmerge01);
        bool useFormalBoardwalk = GetFormalBoardwalkSprite() != null;
        if (useFormalBoardwalk)
        {
            SetEnabled(tideFlatPathRenderer, false);
            float totalLength = pathRight - pathLeft;
            bool useV53Mooring = HasCompleteV53MooringPresentation();
            bool useHdBoardwalk = !useV53Mooring &&
                GetFormalSprite(ref formalBoardwalkHdSprite, "AIWetBoardwalkHD") != null;
            if (useV53Mooring)
            {
                SetEnabled(formalBoardwalkSegments, false);
                bool serviceable = stiltIntegrity >= 3;
                Sprite pierSprite = GetV53MooringPierSprite(serviceable);
                float centerYOffset = serviceable
                    ? TideV53MooringCatalog.ServiceablePierCenterYOffsetFromSurface
                    : TideV53MooringCatalog.FoundPierCenterYOffsetFromSurface;
                float remaining = totalLength;
                float segmentLeft = pathLeft;
                for (int i = 0; i < formalBoardwalkSegments.Count && remaining > 0.01f; i++)
                {
                    float segmentLength = Mathf.Min(TideV53MooringCatalog.PierSpanMeters, remaining);
                    SpriteRenderer segment = formalBoardwalkSegments[i];
                    segment.sprite = pierSprite;
                    segment.sortingOrder = 0;
                    SetWorldSize(
                        segment,
                        new Vector2(segmentLeft + segmentLength * 0.5f, pathSurfaceY + centerYOffset),
                        new Vector2(segmentLength, TideV53MooringCatalog.PierWorldHeight),
                        Color.Lerp(Color.white, new Color(0.74f, 0.84f, 0.82f, 1f), pathSubmerge01 * 0.42f),
                        0f);
                    segmentLeft += segmentLength;
                    remaining -= segmentLength;
                }
            }
            else if (useHdBoardwalk)
            {
                SetEnabled(formalBoardwalkSegments, false);
                SpriteRenderer continuousPier = formalBoardwalkSegments[0];
                continuousPier.sprite = GetFormalBoardwalkSprite();
                Color continuousPierColor = Color.Lerp(
                    new Color(0.94f, 0.9f, 0.8f, 1f),
                    new Color(0.55f, 0.74f, 0.72f, 0.82f),
                    pathSubmerge01);
                SetWorldSize(
                    continuousPier,
                    new Vector2((pathLeft + pathRight) * 0.5f, pathSurfaceY - 0.22f),
                    new Vector2(totalLength, 0.82f),
                    continuousPierColor,
                    0f);
            }
            else
            {
                float segmentLength = totalLength / formalBoardwalkSegments.Count;
                for (int i = 0; i < formalBoardwalkSegments.Count; i++)
                {
                    formalBoardwalkSegments[i].sprite = GetFormalBoardwalkSprite();
                    float centerX = pathLeft + segmentLength * (i + 0.5f);
                    SetWorldSize(
                        formalBoardwalkSegments[i],
                        new Vector2(centerX, pathSurfaceY - 0.36f),
                        new Vector2(segmentLength + 0.12f, 0.72f),
                        pathColor,
                        0f);
                }
            }
        }
        else
        {
            SetEnabled(formalBoardwalkSegments, false);
            SetWorldSize(tideFlatPathRenderer, new Vector2((pathLeft + pathRight) * 0.5f, pathSurfaceY - 0.14f), new Vector2(pathRight - pathLeft, 0.28f), pathColor, 0f);
        }
        UpdateBoatLandingGangplank(pathRight, pathSurfaceY, pathColor);

        Color lamp = Color.Lerp(warmLampColor, Color.white, 0.1f + houseWarmth * 0.07f);
        lamp.a = Mathf.Clamp01(0.72f + houseWarmth * 0.07f + pulse * 0.08f);
        if (useFormalHouse)
        {
            // The authored house already owns its windows. The old generated
            // window was visibly stacked over the painting and is deliberately hidden.
            SetEnabled(windowGlowRenderer, false);
        }
        else
        {
            SetWorldSize(windowGlowRenderer, houseAnchor + new Vector2(-0.18f, 0.38f), new Vector2(1.45f + houseWarmth * 0.08f, 0.56f), lamp, 0f);
        }
        float boatBob = Mathf.Sin(time * 1.4f) * 0.035f;
        bool playerOnBoat = sailTripActive || state == SliceState.FinalDeparture;
        Vector2 playerDrawPosition = playerOnBoat
            ? GetMooredBoatPosition() + GetBoatTripOffset() + new Vector2(-0.12f, 0.34f + boatBob)
            : playerPosition;

        EnsureV20CharacterResourcesLoaded();
        EnsureV41CharacterContactResourcesLoaded();
        EnsureV42CharacterSurvivalResourcesLoaded();
        EnsureV32ArtResourcesLoaded();
        bool useV20Character = HasCompleteV20CharacterPresentation();
        bool useV41Contact = HasCompleteV41CharacterContactPresentation();
        bool useV32Climb = HasCompleteV32ArtPresentation() &&
            isLaneTransitioning &&
            viewMode == SliceViewMode.Shelter &&
            ((laneTransitionFromLane == WalkLane.Deck && laneTransitionTarget == WalkLane.TideFlat) ||
             (laneTransitionFromLane == WalkLane.TideFlat && laneTransitionTarget == WalkLane.Deck));
        bool haulingNetPoseActive = IsActivelyHaulingNet() || editorNetHaulPreviewActive;
        bool routingHaulActive = routingWorkActive && routingWorkDirection > 0f;
        bool routingReleaseActive = routingWorkActive && routingWorkDirection < 0f;
        bool netActionActive = netRigActionHeld ||
            netDepthAdjustmentActive ||
            (state == SliceState.LowTidePlanning && !netDeployed && netLoweringProgress > 0.01f) ||
            haulingNetPoseActive ||
            routingHaulActive;
        bool repairActionActive = repairWorkActive ||
            (barrenIsland != null && barrenIsland.IsDismantling);
        bool prepActionActive = tidePrepActionTimer > 0f || tidePrepWorkActive;
        bool workActionActive = repairActionActive || prepActionActive || routingReleaseActive;
        bool v42SurvivalFrameVisible = TryGetV42SurvivalWorldFrame(
                !playerOnBoat &&
                !useV32Climb &&
                !playerMoving &&
                !playerSwimming &&
                !netActionActive &&
                !workActionActive,
                time,
                out Sprite v42SurvivalFrame,
                out Vector2 v42SurvivalPivot,
                out float v42SurvivalScale,
                out bool v42SurvivalFlipX);
        bool knotActionActive = netRigActionHeld &&
            (netRigStep == NetRigStep.Carrying || netRigStep == NetRigStep.Unrolled);
        bool loweringActionActive =
            (netRigActionHeld && netRigStep == NetRigStep.Lowering) ||
            netDepthAdjustmentActive;
        bool v41AuthoredNetContactActive = useV41Contact &&
            !useV32Climb &&
            (knotActionActive || loweringActionActive);
        float visibleHaulLoad01 = haulingNetPoseActive
            ? Mathf.Clamp01(netHaulLoad01)
            : 0f;
        float actionRock = haulingNetPoseActive
            ? Mathf.Lerp(
                4f,
                Mathf.Lerp(-7f, -10f, visibleHaulLoad01),
                netHaulEffort01)
            : netActionActive || workActionActive ? Mathf.Sin(time * 9f) * 4f : 0f;
        if (netActionActive && !knotActionActive && !v41AuthoredNetContactActive)
        {
            float pull01 = haulingNetPoseActive ? netHaulEffort01 : Mathf.Abs(Mathf.Sin(time * 9f));
            playerDrawPosition += new Vector2(
                -playerFacing * Mathf.Lerp(
                    0.025f,
                    Mathf.Lerp(0.105f, 0.14f, visibleHaulLoad01),
                    pull01),
                Mathf.Lerp(0.012f, Mathf.Lerp(0.048f, 0.06f, visibleHaulLoad01), pull01));
        }
        else if (repairActionActive || (knotActionActive && !v41AuthoredNetContactActive))
        {
            playerDrawPosition += new Vector2(0f, Mathf.Abs(Mathf.Sin(time * 8f)) * 0.045f);
        }

        float locomotionMaxSpeed = playerMoveSpeed * (playerSwimming ? 0.58f : 1f);
        float locomotionSpeed01 = locomotionMaxSpeed > 0.01f
            ? Mathf.Clamp01(Mathf.Abs(playerHorizontalVelocity) / locomotionMaxSpeed)
            : 0f;
        if (isLaneTransitioning)
        {
            locomotionSpeed01 = Mathf.Max(locomotionSpeed01, Mathf.Lerp(0.45f, 0.88f, laneTransitionEntrySpeed01));
        }

        bool playerWading = !playerSwimming && playerSubmersion01 > 0.08f;
        TideV20CharacterActionState landAction =
            TideV20CharacterPresentationModel.ResolveActionState(
                false,
                netActionActive && !knotActionActive,
                workActionActive || knotActionActive,
                playerMoving);

        float transitionDuration = Mathf.Max(0.2f, boatViewTransitionSeconds);
        float boatTransition01 = boatViewTransition == BoatViewTransition.None
            ? 0f
            : Mathf.Clamp01(boatViewTransitionTimer / transitionDuration);
        bool v41BoardingVisible = useV41Contact &&
            !v42SurvivalFrameVisible &&
            ((boatViewTransition == BoatViewTransition.Boarding && boatTransition01 <= 0.5f) ||
             (boatViewTransition == BoatViewTransition.Returning && boatTransition01 >= 0.5f));
        bool v41DoorVisible = useV41Contact &&
            !v42SurvivalFrameVisible &&
            !useV32Climb &&
            ((boatViewTransition == BoatViewTransition.EnteringInterior && boatTransition01 <= 0.5f) ||
             (boatViewTransition == BoatViewTransition.ExitingInterior && boatTransition01 >= 0.5f));
        bool v41TieNetVisible = useV41Contact &&
            !v42SurvivalFrameVisible &&
            !useV32Climb &&
            !v41BoardingVisible &&
            !v41DoorVisible &&
            knotActionActive;
        bool v41LowerSinklineVisible = useV41Contact &&
            !v42SurvivalFrameVisible &&
            !useV32Climb &&
            !v41BoardingVisible &&
            !v41DoorVisible &&
            !v41TieNetVisible &&
            loweringActionActive;
        bool carryingPhysicalHarvest = harvestPhysicalState == HarvestPhysicalState.Carried &&
            currentHarvest != HarvestKind.None;
        bool v41CarryIdleVisible = useV41Contact &&
            !v42SurvivalFrameVisible &&
            !useV32Climb &&
            !v41BoardingVisible &&
            !v41DoorVisible &&
            !v41TieNetVisible &&
            !v41LowerSinklineVisible &&
            !playerSwimming &&
            !playerMoving &&
            (netRigStep == NetRigStep.Carrying || carryingPhysicalHarvest);
        bool v41LocomotionVisible = useV41Contact &&
            !v42SurvivalFrameVisible &&
            !useV32Climb &&
            !v41BoardingVisible &&
            !v41DoorVisible &&
            !v41TieNetVisible &&
            !v41LowerSinklineVisible &&
            !playerSwimming &&
            playerMoving &&
            landAction == TideV20CharacterActionState.Walk;

        // V41 已经把重心起伏画进八帧动作。继续叠加旧的正弦位移会让脚底
        // 离开楼板并产生“鬼畜”感，因此程序摆动只保留给旧资源回退路径。
        float walkWave = playerMoving && !playerSwimming &&
            !v42SurvivalFrameVisible &&
            !v41BoardingVisible && !v41DoorVisible && !v41TieNetVisible &&
            !v41LowerSinklineVisible && !v41CarryIdleVisible && !v41LocomotionVisible
            ? Mathf.Sin(playerWalkCycle * Mathf.PI * 2f)
            : 0f;
        float walkAmplitude = Mathf.Lerp(0.35f, 1f, locomotionSpeed01);
        float walkLift = playerMoving && !playerSwimming ? Mathf.Abs(walkWave) * 0.012f * walkAmplitude : 0f;
        playerDrawPosition += new Vector2(playerMoving && !playerSwimming ? playerFacing * walkWave * 0.004f * walkAmplitude : 0f, walkLift);

        float playbackRate = landAction == TideV20CharacterActionState.Walk
            ? Mathf.Lerp(0.65f, 1.35f, locomotionSpeed01)
            : 1f;
        float climbProgress = GetV32ClimbPlaybackProgress(
            laneTransitionFromLane,
            laneTransitionTarget,
            laneTransitionProgress);
        float boardingLeg01 = boatViewTransition == BoatViewTransition.Boarding
            ? Mathf.Clamp01(boatViewTransitionTimer / (transitionDuration * 0.5f))
            : Mathf.Clamp01((boatViewTransitionTimer - transitionDuration * 0.5f) /
                (transitionDuration * 0.5f));
        float doorLeg01 = boatViewTransition == BoatViewTransition.EnteringInterior
            ? Mathf.Clamp01(boatViewTransitionTimer / (transitionDuration * 0.5f))
            : Mathf.Clamp01((boatViewTransitionTimer - transitionDuration * 0.5f) /
                (transitionDuration * 0.5f));
        TideV41CharacterContactAction v41LocomotionAction =
            netRigStep == NetRigStep.Carrying || carryingPhysicalHarvest
                ? TideV41CharacterContactAction.CarryNetWalk
                : TideV41CharacterContactAction.Walk;
        Sprite v41LandFrame = v41BoardingVisible
            ? TideV41CharacterContactPresentationModel.EvaluateOneShotFrame(
                formalCharacterV41ContactCatalog,
                TideV41CharacterContactAction.Board,
                boardingLeg01,
                boatViewTransition == BoatViewTransition.Returning)
            : v41DoorVisible
                ? TideV41CharacterContactPresentationModel.EvaluateOneShotFrame(
                    formalCharacterV41ContactCatalog,
                    TideV41CharacterContactAction.DoorEnter,
                    doorLeg01,
                    boatViewTransition == BoatViewTransition.ExitingInterior)
            : v41TieNetVisible
                ? TideV41CharacterContactPresentationModel.EvaluateOneShotFrame(
                    formalCharacterV41ContactCatalog,
                    TideV41CharacterContactAction.TieNet,
                    netRigActionProgress,
                    false)
            : v41LowerSinklineVisible
                ? TideV41CharacterContactPresentationModel.EvaluateOneShotFrame(
                    formalCharacterV41ContactCatalog,
                    TideV41CharacterContactAction.LowerSinkline,
                    netSetDepth01,
                    false)
            : v41CarryIdleVisible
                ? formalCharacterV41ContactCatalog.GetFrame(
                    TideV41CharacterContactAction.CarryNetWalk,
                    0)
            : v41LocomotionVisible
                ? TideV41CharacterContactPresentationModel.EvaluateLoopFrame(
                    formalCharacterV41ContactCatalog,
                    v41LocomotionAction,
                    playerWalkCycle)
                : null;
        bool useV41LandFrame = v41LandFrame != null;
        Sprite landFrame = v42SurvivalFrameVisible
            ? v42SurvivalFrame
            : useV32Climb
            ? formalV32ArtCatalog.GetClimbFrame(climbProgress)
            : useV41LandFrame
            ? v41LandFrame
            : useV20Character
            ? EvaluateV20CharacterFrame(
                landAction,
                time,
                playbackRate)
            : GetPlayerSprite();
        Sprite swimFrame = useV20Character
            ? EvaluateV20CharacterFrame(
                TideV20CharacterActionState.Swim,
                time,
                Mathf.Lerp(0.72f, 1.28f, locomotionSpeed01))
            : GetFormalSwimPlayerSprite();

        float blend = Mathf.Clamp01(playerAquaticBlend01);
        // SmoothStep 的前两个参数是输出起止值，不是阈值。先把 blend 映射到
        // 有效区间，才能在完全离水时得到 100% 站姿、0% 游泳姿势，避免双人淡影。
        float landAlpha = v42SurvivalFrameVisible || useV32Climb
            ? 1f
            : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.82f, blend));
        float swimAlpha = v42SurvivalFrameVisible || useV32Climb
            ? 0f
            : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 0.94f, blend));
        Color formalPlayerTint = GetSurvivorClothingTint(daylight01);
        float landScale = v42SurvivalFrameVisible
            ? v42SurvivalScale
            : useV32Climb
            ? formalV32ArtCatalog.ClimbUniformScale
            : useV41LandFrame
            ? TideV41CharacterContactPresentationModel.UniformScale
            : useV20Character
            ? TideV20CharacterPresentationModel.CalculateUniformScale(landAction, landFrame)
            : 1.16f / Mathf.Max(0.01f, landFrame != null ? landFrame.bounds.size.y : 1.16f);
        float swimScale = useV20Character
            ? TideV20CharacterPresentationModel.CalculateUniformScale(
                TideV20CharacterActionState.Swim,
                swimFrame)
            : 1.16f / Mathf.Max(0.01f, swimFrame != null ? swimFrame.bounds.size.x : 1.16f);

        // playerPosition 是逻辑身体中心；V20 的陆地动作 Pivot 在脚底。入水时陆地
        // 身体缓慢压低、前倾并淡出，横游帧从同一身体中心接手，不再瞬间缩放或换层。
        Vector2 landPivotPosition = v42SurvivalFrameVisible
            ? v42SurvivalPivot
            : new Vector2(
                playerDrawPosition.x,
                playerDrawPosition.y - TideV20CharacterPresentationModel.BodyWorldLength * 0.5f -
                    blend * 0.06f);
        if (!v42SurvivalFrameVisible && useV41LandFrame)
        {
            landPivotPosition.y += TideV41CharacterContactPresentationModel.FootPivotCorrectionWorldY;
        }
        TideOceanSample playerOcean = GetOceanSample(playerDrawPosition.x);
        Vector2 swimStartPosition = playerDrawPosition + new Vector2(0f, -0.03f);
        Vector2 swimTargetPosition = new Vector2(
            playerDrawPosition.x,
            playerOcean.SurfaceY - 0.035f);
        Vector2 swimPivotPosition = Vector2.Lerp(
            swimStartPosition,
            swimTargetPosition,
            Mathf.SmoothStep(0f, 1f, blend));
        float wadeRock = playerWading ? Mathf.Sin(time * 5.2f) * 1.2f : 0f;
        float entryLean = -playerFacing * Mathf.SmoothStep(0.05f, 0.78f, blend) * 17f;
        float aquaticActionRock = (netActionActive || workActionActive)
            ? -playerFacing * Mathf.Sin(time * 8f) * 1.4f
            : 0f;

        bool standingInsideBoatLayers =
            (boatViewTransition == BoatViewTransition.Boarding && boatTransition01 >= 0.28f) ||
            (boatViewTransition == BoatViewTransition.Returning && boatTransition01 <= 0.78f);
        // Once the feet cross the authored stern, the standing body obeys the same
        // sandwich as the seated body: rear hull < survivor < front gunwale. Returning
        // restores the shore layer only after the body has crossed back to the pier.
        playerRenderer.sortingOrder = standingInsideBoatLayers ? 6 : 14;
        SetEnabled(playerRenderer, landFrame != null && landAlpha > 0.01f);
        if (playerRenderer.enabled)
        {
            SetUniformSprite(
                playerRenderer,
                landFrame,
                landPivotPosition,
                landScale,
                new Color(formalPlayerTint.r, formalPlayerTint.g, formalPlayerTint.b, landAlpha),
                v42SurvivalFrameVisible || useV32Climb || useV41LandFrame
                    ? 0f
                    : entryLean + wadeRock + aquaticActionRock,
                v42SurvivalFrameVisible
                    ? v42SurvivalFlipX
                    : useV32Climb ? false : playerFacing < 0);
        }

        SetEnabled(playerAquaticRenderer, swimFrame != null && swimAlpha > 0.01f);
        if (playerAquaticRenderer.enabled)
        {
            playerAquaticRenderer.sortingOrder = standingInsideBoatLayers ? 6 : 14;
            float surfaceLean = Mathf.Atan(playerOcean.Slope) * Mathf.Rad2Deg * 0.18f;
            SetUniformSprite(
                playerAquaticRenderer,
                swimFrame,
                swimPivotPosition,
                swimScale,
                new Color(formalPlayerTint.r, formalPlayerTint.g, formalPlayerTint.b, swimAlpha),
                surfaceLean,
                playerFacing < 0);
        }

        bool useFormalPlayer = v42SurvivalFrameVisible || useV32Climb || useV41LandFrame || useV20Character ||
            GetFormalSprite(ref formalPlayerSprite, "AIPlayer") != null;
        SetEnabled(playerGlowRenderer, !useFormalPlayer);
        if (!useFormalPlayer)
        {
            Color playerGlowColor = blend > 0.5f
                ? new Color(0.44f, 0.74f, 0.76f, 0.3f)
                : new Color(0.95f, 0.74f, 0.38f, 0.34f);
            SetWorldSize(
                playerGlowRenderer,
                playerDrawPosition,
                new Vector2(0.84f, 1.28f),
                playerGlowColor,
                0f);
        }
        UpdatePlayerBagStoryMask(
            playerRenderer,
            playerBagMask,
            playerDrawPosition,
            useFormalPlayer,
            swimAlpha > 0.5f,
            netActionActive,
            workActionActive);
        if (v42SurvivalFrameVisible)
        {
            SetMaskEnabled(playerBagMask, false);
        }

        bool survivorHasEnteredBoatCockpit = boatViewTransition == BoatViewTransition.Boarding &&
            boatViewTransitionTimer / Mathf.Max(0.2f, boatViewTransitionSeconds) >= 0.46f;
        if (survivorHasEnteredBoatCockpit)
        {
            // V39 owns the seated body from this point. Do not leave the land body on
            // top of it while the frame is dark, otherwise screenshots reveal two people.
            SetEnabled(playerRenderer, false);
            SetEnabled(playerAquaticRenderer, false);
            SetEnabled(playerGlowRenderer, false);
            SetMaskEnabled(playerBagMask, false);
        }

        bool showSwimWake = (playerSwimming || playerWading) && !playerOnBoat &&
            !v42SurvivalFrameVisible;
        SetEnabled(playerSwimWakeRenderer, showSwimWake);
        if (showSwimWake)
        {
            // A wake is a narrow surface disturbance, not a miniature copy of the
            // whole water body. Keeping this separate also avoids a dark rectangle
            // behind the swimmer.
            playerSwimWakeRenderer.sprite = GetSwimWakeSprite();
            SetWorldSize(
                playerSwimWakeRenderer,
                new Vector2(playerDrawPosition.x - playerFacing * 0.34f, playerOcean.SurfaceY - 0.09f),
                new Vector2(playerSwimming ? 0.68f : 0.42f, playerSwimming ? 0.14f : 0.1f),
                new Color(0.76f, 0.92f, 0.88f, playerSwimming ? 0.42f : 0.26f),
                0f);
            playerSwimWakeRenderer.flipX = playerFacing < 0;
        }
        if (playerRenderer != null)
        {
            playerRenderer.flipX = playerFacing < 0;
        }

        UpdateHouseDetailVisuals(time);

        SetEnabled(stiltPosts, !useFormalHouse);
        for (int i = 0; i < stiltPosts.Count && !useFormalHouse; i++)
        {
            float t = stiltPosts.Count <= 1 ? 0f : i / (stiltPosts.Count - 1f);
            float x = Mathf.Lerp(houseAnchor.x - 1.95f, houseAnchor.x + 1.9f, t);
            float lean = Mathf.Sin(i * 2.1f) * 2.8f;
            SetWorldSize(stiltPosts[i], new Vector2(x, -1.15f), new Vector2(0.2f, 3.15f), Color.Lerp(wetWoodColor, saltWoodColor, 0.08f + t * 0.22f), lean);
        }

        UpdateGangwayVisuals();

        for (int i = 0; i < houseRepairMarks.Count; i++)
        {
            bool authoredEndpointOwnsRepairs = HasCompleteV32ArtPresentation();
            int completedVisibleRepairs = useFormalHouse ? Mathf.Max(0, stiltIntegrity - 1) : stiltIntegrity;
            bool previewsCurrentRepair = !authoredEndpointOwnsRepairs &&
                state == SliceState.RepairMoment &&
                !repairChoiceApplied &&
                pendingRepairChoice == RepairChoice.Stilt &&
                repairWorkProgress > 0f &&
                i == completedVisibleRepairs;
            bool visible = !authoredEndpointOwnsRepairs &&
                state != SliceState.FinalDeparture &&
                (i < completedVisibleRepairs || previewsCurrentRepair);
            houseRepairMarks[i].enabled = visible;
            if (visible)
            {
                float reveal01 = previewsCurrentRepair ? GetRepairReveal01() : 1f;
                if (useFormalHouse && GetFormalSprite(ref formalRepairTimberSprite, "AIRepairTimber") != null)
                {
                    houseRepairMarks[i].sprite = GetFormalSprite(ref formalRepairTimberSprite, "AIRepairTimber");
                    Vector2 patchPosition = HasHdFormalHouse()
                        ? new Vector2(0.18f - i * 0.72f, -1.34f)
                        : new Vector2(-4.55f + i * 0.68f, -1.06f + (i % 2) * 0.18f);
                    Vector2 patchSize = HasHdFormalHouse()
                        ? new Vector2(0.5f * Mathf.Lerp(0.2f, 1f, reveal01), 0.14f)
                        : new Vector2(0.32f * Mathf.Lerp(0.2f, 1f, reveal01), 0.13f);
                    float patchRotation = HasHdFormalHouse() ? 88f + i * 4f : -6f + i * 4f;
                    SetWorldSize(houseRepairMarks[i], patchPosition, patchSize, new Color(0.78f, 0.76f, 0.68f, Mathf.Lerp(0.22f, 0.94f, reveal01)), patchRotation);
                }
                else
                {
                    Vector2 patchSize = new Vector2(0.3f * Mathf.Lerp(0.2f, 1f, reveal01), 0.2f);
                    SetWorldSize(houseRepairMarks[i], houseAnchor + new Vector2(-1.05f + i * 0.55f, -0.82f + (i % 2) * 0.22f), patchSize, new Color(0.85f, 0.68f, 0.42f, 0.2f + reveal01 * 0.7f), -8f + i * 5f);
                }
            }
        }

        UpdateNearshoreWorkVisuals(time);
        UpdateShipwreckVisuals(time);
        UpdateNetVisuals(time);
        UpdateBoatVisuals(time);
        UpdateSkyVisuals(time, storm01);
        UpdateWaterDetailVisuals(time);
        UpdatePayoffVisuals(time, storm01);
        UpdateTidePrepVisuals(time);
        UpdateFinalDepartureVisuals(time);
        UpdateHarvestVisuals(pulse);
        UpdateSurvivalOverlay();
        UpdateText(storm01);
        UpdateBoatViewTransitionVisual();
    }

    private void SetLookoutVistaEnabled(bool enabled)
    {
        SetEnabled(lookoutVistaMaskRenderer, enabled);
        SetEnabled(lookoutVistaRoofRenderer, enabled);
        SetEnabled(lookoutVistaCraneRenderer, enabled);
        SetEnabled(lookoutVistaWreckRenderer, enabled);
        SetEnabled(lookoutVistaLighthouseRenderer, enabled);
        SetEnabled(lookoutVistaBeamRenderer, enabled);
    }

    private void UpdateLookoutVistaVisuals(float time, float daylight01, float storm01)
    {
        EnsureV44LookoutVistaResourcesLoaded();
        EnsureV43SeaWeatherResourcesLoaded();
        bool useV44 = HasCompleteV44LookoutVistaPresentation();
        if (!useV44)
        {
            // Keep the mode visually coherent even when a catalog is missing. The
            // opaque live sky makes the failure obvious without exposing the ordinary
            // shelter scene through a half-built vista.
            Color fallbackSky = GetReadableSkyColor(daylight01, storm01);
            fallbackSky.a = 1f;
            SetWorldSize(lookoutVistaMaskRenderer, Vector2.zero, new Vector2(16.4f, 8.2f), fallbackSky, 0f);
            SetEnabled(lookoutVistaRoofRenderer, false);
            SetEnabled(lookoutVistaCraneRenderer, false);
            SetEnabled(lookoutVistaWreckRenderer, false);
            SetEnabled(lookoutVistaLighthouseRenderer, false);
            SetEnabled(lookoutVistaBeamRenderer, false);
            return;
        }

        Color sky = GetReadableSkyColor(daylight01, storm01);
        sky.a = 1f;
        SetWorldSize(lookoutVistaMaskRenderer, Vector2.zero, new Vector2(16.4f, 8.2f), sky, 0f);

        // The same V43 cloud strips already evaluated against the live wind and storm
        // clock are lifted above the vista mask. No new weather clock is introduced.
        for (int i = 0; i < cloudRenderers.Count; i++)
        {
            if (cloudRenderers[i] != null && cloudRenderers[i].enabled)
            {
                cloudRenderers[i].sortingOrder = 42 + Mathf.Min(i, 2);
            }
        }
        for (int i = 0; i < horizonCloudBanks.Count; i++)
        {
            if (horizonCloudBanks[i] != null && horizonCloudBanks[i].enabled)
            {
                horizonCloudBanks[i].sortingOrder = 42 + Mathf.Min(i, 2);
            }
        }

        float night01 = 1f - daylight01;
        float moonPhase01 = Mathf.Repeat(moonAgeDays / 29.53f, 1f);
        float illumination01 = GetMoonIllumination01(moonPhase01);
        Vector2 lookoutMoonPosition = new Vector2(-4.72f, 2.62f);
        Vector2 moonSize = new Vector2(0.86f, 0.86f);
        moonRenderer.sortingOrder = 41;
        moonShadowRenderer.sortingOrder = 42;
        SetWorldSize(
            moonRenderer,
            lookoutMoonPosition,
            moonSize,
            new Color(1f, 1f, 1f, 0.03f + night01 * 0.9f),
            0f);
        moonShadowRenderer.sprite = GetFirstSliceMoonPhaseShadowSprite(moonPhase01, illumination01);
        SetWorldSize(
            moonShadowRenderer,
            lookoutMoonPosition,
            moonSize,
            new Color(0.006f, 0.026f, 0.03f, 0.14f + night01 * 0.78f),
            0f);

        Vector2 vistaSize = TideV44LookoutVistaPresentationModel.VistaWorldSize;
        float bodySwayX = Mathf.Sin(time * 0.22f) * Mathf.Lerp(0.008f, 0.028f, storm01);
        lookoutVistaWreckRenderer.sprite = formalV44LookoutVistaCatalog.MidWreckField;
        SetWorldSize(
            lookoutVistaWreckRenderer,
            new Vector2(bodySwayX * 0.34f, 0f),
            vistaSize,
            Color.Lerp(
                new Color(0.62f, 0.72f, 0.72f, 0.38f),
                new Color(0.82f, 0.84f, 0.79f, 0.58f),
                daylight01),
            0f);

        float lookoutTideY = Mathf.Lerp(-1.48f, -0.86f, GetNaturalTideHeight01());
        TideOceanSample centreOcean = GetOceanSample(0f);
        lookoutTideY += (centreOcean.SurfaceY - currentWaterY) * 0.28f;
        bool useFormalWater = GetFormalWaterSurfaceSprite() != null;
        if (useFormalWater)
        {
            // Keep the authored crest registered to lookoutTideY, but extend only
            // the deep body below it. The shorter shelter-height crop exposed a flat
            // strip of sky at the bottom of a 16:9 lookout camera.
            float waterHeight = 5.1f + storm01 * 0.24f;
            naturalWaterSurfaceRenderer.sprite = GetFormalWaterSurfaceSprite();
            naturalWaterSurfaceRenderer.sortingOrder = 47;
            SetWorldSize(
                naturalWaterSurfaceRenderer,
                new Vector2(0f, lookoutTideY - waterHeight * 0.5f + FormalWaterAverageCrestOffset),
                new Vector2(14.8f + storm01 * 0.5f, waterHeight),
                GetFormalWaterTint(storm01),
                Mathf.Atan(centreOcean.Slope) * Mathf.Rad2Deg * 0.05f);
            SetEnabled(waterRenderer, false);
            SetEnabled(waterFoamRenderer, false);
        }
        else
        {
            SetEnabled(naturalWaterSurfaceRenderer, false);
            waterRenderer.sortingOrder = 47;
            SetWorldSize(
                waterRenderer,
                new Vector2(0f, -2.7f),
                new Vector2(14.8f, 3.8f),
                GetReadableNearSeaColor(1, daylight01, storm01),
                0f);
        }

        bool useV43Waves = useFormalWater && HasCompleteV43SeaWeatherPresentation();
        for (int i = 0; i < waveStrips.Count; i++)
        {
            SpriteRenderer crest = waveStrips[i];
            float x = -5.8f + i * 2.85f +
                Mathf.Sin(time * Mathf.Lerp(0.24f, 0.5f, storm01) + i * 0.73f) *
                Mathf.Lerp(0.12f, 0.3f, storm01);
            TideOceanSample sample = GetOceanSample(x);
            TideV43WaveKind kind = ResolveV43WaveKind(i, storm01);
            Sprite frame = useV43Waves
                ? TideV43SeaWeatherPresentationModel.EvaluateWaveFrame(
                    formalV43SeaWeatherCatalog,
                    kind,
                    time,
                    GetStableVisualVariation01(i, 401),
                    Mathf.Lerp(0.86f, 1.14f, GetStableVisualVariation01(i, 409)))
                : null;
            crest.sprite = frame ?? GetFormalSeaCurrentCrestSprite() ?? GetFoamSprite();
            crest.sortingOrder = 48;
            float localWaveY = (sample.SurfaceY - currentWaterY) * 0.32f;
            Vector2 size = frame != null
                ? TideV43SeaWeatherPresentationModel.GetWaveWorldSize(kind) *
                    Mathf.Lerp(0.92f, 1.18f, GetStableVisualVariation01(i, 419))
                : new Vector2(1.8f, 0.24f);
            SetWorldSize(
                crest,
                new Vector2(x, lookoutTideY + localWaveY),
                size,
                Color.Lerp(
                    new Color(0.86f, 0.96f, 0.94f, 0.46f),
                    new Color(0.65f, 0.76f, 0.79f, 0.67f),
                    storm01),
                Mathf.Atan(sample.Slope) * Mathf.Rad2Deg * 0.5f);
        }

        TideLighthouseVisibilitySample lighthouseVisibility = GetLighthouseVisibilitySample();
        ApplyLighthouseFogToBroadWeatherBands(lighthouseVisibility);
        float lighthouseScale = 0.19f;
        Vector2 lighthouseRoot = TideV44LookoutVistaPresentationModel.EvaluateLighthouseRootPosition(
            lookoutTideY + 0.03f,
            lighthouseScale);
        lookoutVistaLighthouseRenderer.sprite = formalV44LookoutVistaCatalog.FarLighthouse;
        SetEnabled(lookoutVistaLighthouseRenderer, lighthouseVisibility.ShowsLighthouse);
        if (lighthouseVisibility.ShowsLighthouse)
        {
            SetUniformSprite(
                lookoutVistaLighthouseRenderer,
                formalV44LookoutVistaCatalog.FarLighthouse,
                lighthouseRoot,
                lighthouseScale,
                new Color(0.92f, 0.94f, 0.9f, lighthouseVisibility.LighthouseAlpha),
                0f,
                false);
        }

        Sprite beamFrame = TideV44LookoutVistaPresentationModel.EvaluateBeamFrame(
            formalV44LookoutVistaCatalog,
            time);
        float beamAlpha = lighthouseVisibility.BeamAlpha;
        bool showBeam = lighthouseVisibility.ShowsBeam && beamFrame != null;
        SetEnabled(lookoutVistaBeamRenderer, showBeam);
        if (showBeam)
        {
            Vector2 lens = TideV44LookoutVistaPresentationModel.EvaluateLighthouseLensPosition(
                lighthouseRoot,
                lighthouseScale);
            SetUniformSprite(
                lookoutVistaBeamRenderer,
                beamFrame,
                lens,
                0.55f,
                new Color(1f, 0.9f, 0.66f, beamAlpha),
                Mathf.Sin(time * 0.22f) * 8f,
                true);
        }

        lookoutVistaRoofRenderer.sprite = roofIntegrity >= 2
            ? formalV44LookoutVistaCatalog.NearRoofRepair
            : formalV44LookoutVistaCatalog.NearRoofDamage;
        Color nearTint = Color.Lerp(
            new Color(0.58f, 0.65f, 0.64f, 1f),
            new Color(0.94f, 0.92f, 0.84f, 1f),
            daylight01);
        SetWorldSize(lookoutVistaRoofRenderer, Vector2.zero, vistaSize, nearTint, 0f);
        lookoutVistaCraneRenderer.sprite = formalV44LookoutVistaCatalog.NearCrane;
        SetWorldSize(lookoutVistaCraneRenderer, Vector2.zero, vistaSize, nearTint, 0f);

        for (int i = 0; i < weatherRainStreaks.Count; i++)
        {
            if (weatherRainStreaks[i] != null && weatherRainStreaks[i].enabled)
            {
                weatherRainStreaks[i].sortingOrder = 52;
            }
        }
    }

    private static float GetActiveCameraCenterX()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
        }

        return camera != null ? camera.transform.position.x : 0f;
    }

    private void UpdateShelterCameraFraming()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            camera = FindFirstObjectByType<Camera>();
        }

        if (camera == null || !camera.orthographic)
        {
            return;
        }

        Vector3 current = camera.transform.position;
        if (viewMode != SliceViewMode.Shelter || state == SliceState.FinalDeparture)
        {
            // Interior and sailing render in camera-relative screen coordinates. They
            // must start centred; the view hand-off is already hidden by the short fade.
            camera.transform.position = new Vector3(0f, current.y, current.z);
            return;
        }

        float aspect = camera.targetTexture != null && camera.targetTexture.height > 0
            ? (float)camera.targetTexture.width / camera.targetTexture.height
            : camera.aspect;
        float halfWidth = camera.orthographicSize * aspect;
        float estimatedBoatRight = GetMooredBoatPosition().x + GetBoatTripOffset().x + 1.72f;
        if (boatPassengerGunwaleRenderer != null && boatPassengerGunwaleRenderer.enabled)
        {
            estimatedBoatRight = Mathf.Max(estimatedBoatRight, boatPassengerGunwaleRenderer.bounds.max.x);
        }
        if (boatRudderRenderer != null && boatRudderRenderer.enabled)
        {
            estimatedBoatRight = Mathf.Max(estimatedBoatRight, boatRudderRenderer.bounds.max.x);
        }
        float requiredRightPan = Mathf.Max(0f, estimatedBoatRight + 0.38f - halfWidth);
        float requiredLeftPan = Mathf.Min(
            0f,
            TideBarrenIslandController.WalkableLeftX - 0.38f + halfWidth);
        float leftFollow01 = 1f - Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(
                TideBarrenIslandController.OpeningPlayerX,
                TideBarrenIslandController.WalkableRightX + 0.9f,
                playerPosition.x));
        float follow01 = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(
                OutdoorCameraFollowStartX,
                Mathf.Max(OutdoorCameraFollowStartX + 0.1f, GetBoatBoardingX()),
                playerPosition.x));
        float targetX = requiredLeftPan * leftFollow01 + requiredRightPan * follow01;

        // 同一连续户外世界不需要“归处屏/泊位屏”黑屏交接。运行时相机平滑
        // 跟随人物，编辑器预览则一次落到目标位置，避免截图依赖若干空帧。
        float cameraX = targetX;
        if (Application.isPlaying)
        {
            float blend = 1f - Mathf.Exp(-OutdoorCameraFollowSpeed * Mathf.Max(0f, Time.deltaTime));
            cameraX = Mathf.Lerp(current.x, targetX, blend);
        }

        camera.transform.position = new Vector3(cameraX, current.y, current.z);
    }

    private void UpdateBoatViewTransitionVisual()
    {
        float fade01 = Mathf.Max(
            GetBoatViewTransitionFade01(),
            GetSurvivalPresentationFade01());
        bool visible = fade01 > 0.001f;
        SetEnabled(boatViewTransitionRenderer, visible);
        if (!visible)
        {
            return;
        }

        boatViewTransitionRenderer.sortingOrder = 100;
        SetWorldSize(
            boatViewTransitionRenderer,
            new Vector2(GetActiveCameraCenterX(), 0f),
            new Vector2(16.4f, 8.2f),
            new Color(0.008f, 0.026f, 0.032f, Mathf.Pow(fade01, 0.72f) * 0.96f),
            0f);
    }

    private void UpdateSurvivalOverlay()
    {
        if (deathFeedbackTimer > 0f)
        {
            float envelope = Mathf.Clamp01(deathFeedbackTimer / 3.8f);
            Set(
                foregroundMoonWashRenderer,
                new Vector2(GetActiveCameraCenterX(), 0f),
                new Vector2(13.8f, 7.6f),
                new Color(0.24f, 0.015f, 0.012f, 0.22f + envelope * 0.42f),
                0f);
            return;
        }

        if (bodyWarmth01 < 0.38f)
        {
            float cold01 = Mathf.InverseLerp(0.38f, 0f, bodyWarmth01);
            Set(
                foregroundMoonWashRenderer,
                new Vector2(GetActiveCameraCenterX(), 0f),
                new Vector2(13.8f, 7.6f),
                new Color(0.025f, 0.13f, 0.18f, cold01 * 0.18f),
                0f);
        }
    }

    private void UpdateGangwayVisuals()
    {
        if (GetFormalHouseSprite() != null)
        {
            SetEnabled(houseDiagonalBraces, false);
            SetEnabled(laundryCloths, false);
            return;
        }

        Vector2 top = GetGangwayTopPosition() + new Vector2(0f, -0.12f);
        Vector2 bottom = GetGangwayBottomPosition() + new Vector2(0f, 0.08f);
        Vector2 direction = bottom - top;
        float length = direction.magnitude;
        float railAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        Vector2 normal = new Vector2(-direction.y, direction.x).normalized;

        for (int i = 0; i < houseDiagonalBraces.Count; i++)
        {
            bool visible = i < 2;
            houseDiagonalBraces[i].enabled = visible;
            if (!visible)
            {
                continue;
            }

            houseDiagonalBraces[i].sprite = GetDeckSprite();
            Vector2 offset = normal * (i == 0 ? -0.14f : 0.14f);
            SetWorldSize(houseDiagonalBraces[i], (top + bottom) * 0.5f + offset, new Vector2(length, 0.09f), new Color(0.52f, 0.34f, 0.18f, 0.96f), railAngle);
        }

        for (int i = 0; i < laundryCloths.Count; i++)
        {
            laundryCloths[i].sprite = GetDeckSprite();
            float t = (i + 1f) / (laundryCloths.Count + 1f);
            Vector2 rungPosition = Vector2.Lerp(top, bottom, t);
            SetWorldSize(laundryCloths[i], rungPosition, new Vector2(0.58f, 0.075f), new Color(0.72f, 0.57f, 0.34f, 0.96f), railAngle + 90f);
        }
    }

    private float GetDaylight01()
    {
        float solarHeight = Mathf.Sin((dayProgress01 - 0.25f) * Mathf.PI * 2f);
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(solarHeight * 1.35f));
    }

    // 海天层次增亮：只调整视觉分层，不改水位、碰撞、月相或昼夜状态机。
    private Color GetReadableSkyColor(float daylight01, float storm01)
    {
        Color night = new Color(0.025f, 0.075f, 0.13f, 1f);
        Color day = new Color(0.34f, 0.58f, 0.66f, 1f);
        Color color = Color.Lerp(night, day, daylight01);
        return Color.Lerp(color, new Color(0.12f, 0.2f, 0.23f, 1f), storm01 * 0.42f);
    }

    private Color GetReadableHorizonSeaColor(float daylight01, float storm01)
    {
        Color color = Color.Lerp(
            new Color(0.045f, 0.28f, 0.36f, 0.78f),
            new Color(0.12f, 0.58f, 0.66f, 0.88f),
            daylight01);
        return Color.Lerp(color, new Color(0.08f, 0.22f, 0.25f, 0.84f), storm01 * 0.42f);
    }

    private Color GetReadableFarSeaColor(int bandIndex, float daylight01, float storm01)
    {
        float depth01 = Mathf.Clamp01(bandIndex / 5f);
        Color color = Color.Lerp(
            new Color(0.04f, 0.26f + depth01 * 0.025f, 0.34f + depth01 * 0.035f, 0.48f),
            new Color(0.1f, 0.56f + depth01 * 0.035f, 0.64f + depth01 * 0.025f, 0.62f),
            daylight01);
        return Color.Lerp(color, new Color(0.055f, 0.18f, 0.21f, 0.58f), storm01 * 0.4f);
    }

    private Color GetReadableNearSeaColor(int bandIndex, float daylight01, float storm01)
    {
        float depth01 = Mathf.Clamp01(bandIndex / 4f);
        Color color = Color.Lerp(
            new Color(0.025f, 0.22f + depth01 * 0.04f, 0.3f + depth01 * 0.05f, 0.56f),
            new Color(0.05f, 0.54f + depth01 * 0.05f, 0.64f + depth01 * 0.04f, 0.72f),
            daylight01);
        return Color.Lerp(color, new Color(0.035f, 0.21f, 0.24f, 0.68f), storm01 * 0.44f);
    }

    private Color GetFormalWaterTint(float storm01)
    {
        // The same authored water crosses exterior, interior and sailing views. Keeping
        // one tint prevents the sea from changing material at a view boundary. Calm
        // daylight stays near-neutral; storm pressure removes warmth and saturation.
        return Color.Lerp(
            new Color(0.94f, 0.98f, 0.96f, 0.96f),
            new Color(0.66f, 0.76f, 0.79f, 0.97f),
            Mathf.Clamp01(storm01) * 0.42f);
    }

    private void UpdateCloudVisuals(float time, float daylight01, float storm01)
    {
        if (UpdateV43CloudLayers(time, daylight01, storm01))
        {
            return;
        }

        bool useFormalCloud = GetFormalSprite(ref formalCloudBankSprite, "AICloudBank") != null;
        bool useFormalSeaSky = GetFormalDaySeaSkySprite() != null && GetFormalNightSeaSkySprite() != null;
        float signedWind = GetNaturalSailingWindSpeed();
        for (int i = 0; i < cloudRenderers.Count; i++)
        {
            // The formal sea-sky plate already contains layered cloud depth. Repeating the
            // cutout above it exposes the source silhouette instead of adding atmosphere.
            cloudRenderers[i].enabled = !useFormalCloud || (!useFormalSeaSky && i < 3);
            if (!cloudRenderers[i].enabled)
            {
                continue;
            }

            float travel01 = Mathf.Repeat(time * Mathf.Lerp(0.012f, 0.042f, Mathf.Clamp01(Mathf.Abs(signedWind) / 0.75f)) + i * 0.37f, 1f);
            float windTravel = Mathf.Lerp(-0.72f, 0.72f, signedWind < 0f ? 1f - travel01 : travel01);
            float x = useFormalCloud
                ? -4.7f + i * 4.45f + windTravel
                : -5.9f + i * 2.85f + windTravel * 0.82f;
            float y = useFormalCloud
                ? 2.02f + (i % 2) * 0.42f + Mathf.Sin(time * 0.075f + i) * 0.035f
                : 2.12f + (i % 2) * 0.36f + Mathf.Sin(time * 0.11f + i) * 0.045f;
            float width = useFormalCloud ? 2.65f + i * 0.35f : 1.22f + (i % 3) * 0.32f;
            Color cloud = useFormalSeaSky && daylight01 < 0.5f
                ? Color.Lerp(new Color(0.56f, 0.64f, 0.66f, 0.2f), new Color(0.68f, 0.72f, 0.7f, 0.34f), daylight01 * 2f)
                : Color.Lerp(new Color(0.34f, 0.42f, 0.44f, 0.42f), new Color(0.82f, 0.86f, 0.82f, 0.64f), daylight01);
            cloud = Color.Lerp(cloud, new Color(0.16f, 0.22f, 0.23f, 0.58f), storm01 * 0.58f);
            if (useFormalCloud && useFormalSeaSky)
            {
                // The formal sky already carries a distant cloud bank. Keep moving clouds
                // translucent so the same painted silhouette does not read as pasted copies.
                cloud.a *= Mathf.Lerp(0.16f, 0.42f, storm01);
            }
            SetWorldSize(cloudRenderers[i], new Vector2(x, y), new Vector2(width, useFormalCloud ? 0.95f : 0.42f + (i % 2) * 0.08f), cloud, useFormalCloud ? 0f : Mathf.Sin(i * 0.8f) * 1.8f);
        }
    }

    private void UpdateHorizonCloudBankVisuals(float time, float daylight01, float storm01)
    {
        // V43 在 UpdateCloudVisuals 内同时管理每层的两张无缝副本。
        // 这里不能再用旧云层覆盖它们。
        if (HasCompleteV43SeaWeatherPresentation())
        {
            return;
        }

        bool useFormalCloud = GetFormalSprite(ref formalCloudBankSprite, "AICloudBank") != null;
        bool useFormalSeaSky = GetFormalDaySeaSkySprite() != null && GetFormalNightSeaSkySprite() != null;
        float signedWind = GetNaturalSailingWindSpeed();
        for (int i = 0; i < horizonCloudBanks.Count; i++)
        {
            // Do not layer another copy of the same formal cloud bank over a formal sky.
            horizonCloudBanks[i].enabled = !useFormalCloud || (!useFormalSeaSky && i < 2);
            if (!horizonCloudBanks[i].enabled)
            {
                continue;
            }

            float travel01 = Mathf.Repeat(time * Mathf.Lerp(0.01f, 0.035f, Mathf.Clamp01(Mathf.Abs(signedWind) / 0.75f)) + i * 0.43f, 1f);
            float x = -4.2f + i * 4.15f + Mathf.Lerp(-0.58f, 0.58f, signedWind < 0f ? 1f - travel01 : travel01);
            float y = 1.34f + (i % 2) * 0.12f + Mathf.Sin(time * 0.08f + i) * 0.028f;
            float width = 2.55f + i * 0.34f;
            Color bankColor = Color.Lerp(
                new Color(0.08f, 0.17f, 0.18f, 0.42f),
                new Color(0.5f, 0.66f, 0.62f, 0.5f),
                daylight01);
            bankColor = Color.Lerp(bankColor, new Color(0.11f, 0.18f, 0.19f, 0.58f), storm01 * 0.56f);
            SetWorldSize(horizonCloudBanks[i], new Vector2(x, y), new Vector2(useFormalCloud ? width * 1.32f : width, useFormalCloud ? 0.72f : 0.58f + i * 0.05f), bankColor, useFormalCloud ? 0f : Mathf.Sin(time * 0.05f + i) * 1.2f);
        }
    }

    private bool UpdateV43CloudLayers(float time, float daylight01, float storm01)
    {
        EnsureV43SeaWeatherResourcesLoaded();
        if (!HasCompleteV43SeaWeatherPresentation() ||
            cloudRenderers.Count < 5 || horizonCloudBanks.Count < 3)
        {
            return false;
        }

        // 每一层用两张 4096px 无缝图首尾相接。相机平移只改变视差，风才
        // 推动云层；因此出海时既有速度参照，也不会看到贴图边缘突然露出。
        Camera sceneCamera = Camera.main;
        float cameraX = sceneCamera != null ? sceneCamera.transform.position.x : 0f;
        float signedWind = GetNaturalSailingWindSpeed();
        const float cloudWidth = 16f;
        for (int i = 0; i < 3; i++)
        {
            TideV43CloudLayer layer = (TideV43CloudLayer)i;
            Sprite sprite = formalV43SeaWeatherCatalog.GetCloud(layer);
            SpriteRenderer primary = cloudRenderers[i];
            SpriteRenderer duplicate = horizonCloudBanks[i];
            primary.sprite = sprite;
            duplicate.sprite = sprite;
            primary.sortingOrder = -61 + i;
            duplicate.sortingOrder = primary.sortingOrder;

            float parallax = i == 0 ? 0.08f : i == 1 ? 0.19f : 0.38f;
            float phase = i * 4.73f;
            float offset = Mathf.Repeat(
                time * signedWind * Mathf.Lerp(0.12f, 0.24f, i * 0.5f) +
                cameraX * (1f - parallax) + phase + cloudWidth * 0.5f,
                cloudWidth) - cloudWidth * 0.5f;
            float firstX = cameraX + offset;
            float secondX = firstX + (firstX < cameraX ? cloudWidth : -cloudWidth);
            float y = i == 0 ? 2.48f : i == 1 ? 2.32f : 2.62f;

            Color tint = Color.Lerp(
                new Color(0.46f, 0.54f, 0.57f, 0.16f + i * 0.035f),
                new Color(0.9f, 0.93f, 0.9f, 0.2f + i * 0.04f),
                daylight01);
            tint = Color.Lerp(
                tint,
                new Color(0.18f, 0.24f, 0.27f, 0.34f + i * 0.055f),
                storm01 * 0.82f);

            SetWorldSize(primary, new Vector2(firstX, y), new Vector2(cloudWidth, 4f), tint, 0f);
            SetWorldSize(duplicate, new Vector2(secondX, y), new Vector2(cloudWidth, 4f), tint, 0f);
        }

        for (int i = 3; i < cloudRenderers.Count; i++)
        {
            SetEnabled(cloudRenderers[i], false);
        }

        return true;
    }

    private void UpdateFarSeaVisuals(float time, float daylight01, float storm01)
    {
        bool useFormalSeaSky = GetFormalDaySeaSkySprite() != null && GetFormalNightSeaSkySprite() != null;
        for (int i = 0; i < farSeaBands.Count; i++)
        {
            farSeaBands[i].enabled = !useFormalSeaSky && i < 3;
            if (!farSeaBands[i].enabled)
            {
                continue;
            }

            float y = -0.48f - i * 0.33f + Mathf.Sin(time * 0.12f + i) * 0.018f;
            Color bandColor = GetReadableFarSeaColor(i, daylight01, storm01);
            SetWorldSize(farSeaBands[i], new Vector2(0f, y), new Vector2(14.4f - i * 0.42f, 0.32f), bandColor, Mathf.Sin(time * 0.09f + i) * 0.7f);
        }
    }

    private void UpdateWeatherVisuals(float time, float storm01)
    {
        float rain01 = TideContinuousWeatherModel.EvaluateRain01(storm01);
        float signedWind = GetNaturalSailingWindSpeed();
        float windLean = TideContinuousWeatherModel.EvaluateRainLeanDegrees(signedWind, storm01);
        for (int i = 0; i < weatherRainStreaks.Count; i++)
        {
            SpriteRenderer streak = weatherRainStreaks[i];
            streak.enabled = rain01 > 0.01f;
            if (!streak.enabled)
            {
                continue;
            }

            streak.sprite = GetMoonWashSprite();
            float seed = Mathf.Repeat(i * 0.6180339f, 1f);
            float fall01 = Mathf.Repeat(time * Mathf.Lerp(0.42f, 0.78f, rain01) + seed, 1f);
            float x = Mathf.Lerp(-6.8f, 6.8f, Mathf.Repeat(seed * 1.73f + Mathf.Floor(time * 0.24f) * 0.071f, 1f));
            x += signedWind * fall01 * 0.72f;
            float y = Mathf.Lerp(4.1f, -3.4f, fall01);
            SetWorldSize(
                streak,
                new Vector2(x, y),
                new Vector2(0.009f, Mathf.Lerp(0.26f, 0.46f, rain01)),
                new Color(0.72f, 0.84f, 0.84f, Mathf.Lerp(0.045f, 0.18f, rain01)),
                windLean + Mathf.Sin(time * 0.32f) * 1.2f);
        }
    }

    // 近海色带是纯视觉层：它让低潮时的水体也有颜色和深浅，不改变任何水位判定。
    private void UpdateNearSeaColorBandVisuals(float time, float daylight01, float storm01)
    {
        bool useFormalWater = GetFormalWaterSurfaceSprite() != null;
        for (int i = 0; i < nearSeaColorBands.Count; i++)
        {
            nearSeaColorBands[i].enabled = !useFormalWater && i < 3;
            if (!nearSeaColorBands[i].enabled)
            {
                continue;
            }

            float y = -1.52f - i * 0.36f + Mathf.Sin(time * 0.18f + i * 0.73f) * 0.025f;
            float width = 14.2f - i * 0.36f;
            Color bandColor = GetReadableNearSeaColor(i, daylight01, storm01);
            SetWorldSize(nearSeaColorBands[i], new Vector2(0f, y), new Vector2(width, 0.36f + i * 0.02f), bandColor, Mathf.Sin(time * 0.07f + i) * 0.9f);
        }
    }

    // 昼夜色带只负责让天空从黑底变成有时间感的远海天幕，不参与状态机。
    private void UpdateDayNightSkyBandVisuals(float time, float daylight01, float storm01)
    {
        // Four opaque rectangles created visible stripes across the sky. The main
        // backdrop already interpolates continuously through dawn/day/dusk/night.
        SetEnabled(dayNightSkyBands, false);
    }

    private void UpdateReturnPressureVisuals(float time)
    {
        float pressure = GetReturnPressure01();
        if (pressure <= 0.02f)
        {
            SetEnabled(returnPressureWashRenderer, false);
            return;
        }

        float pulse = Mathf.Sin(time * 1.65f) * 0.012f;
        Color pressureColor = Color.Lerp(
            new Color(0.03f, 0.08f, 0.16f, 0.035f),
            new Color(0.45f, 0.11f, 0.06f, 0.11f),
            pressure);
        pressureColor.a += pulse;
        Set(
            returnPressureWashRenderer,
            new Vector2(GetActiveCameraCenterX(), 0f),
            new Vector2(13.8f, 7.6f),
            pressureColor,
            0f);
    }

    private void UpdateSailingDestinationVisuals(float time)
    {
        // 该浮标虽有现实原型，但当前投影下无法一眼辨认，也没有不可替代的
        // 玩法职责。首航直接让玩家读取漂物尾流；不再用孤立小图标充当门。
        SetEnabled(sailingBuoyPointRenderer, false);
        SetEnabled(sailingBuoyTetherRenderer, false);
        SetEnabled(sailingBuoySinkerRenderer, false);
        SetEnabled(sailingSalvagePointRenderer, !sailingSalvageCollected);
        if (!sailingSalvageCollected)
        {
            SetSailingDestinationVisual(sailingSalvagePointRenderer, SailingPointKind.Salvage, time, false);
            Vector2 salvageScreenPosition = GetSailingScreenPosition(GetSailingPointPosition(SailingPointKind.Salvage));
            bool wakeVisible = Mathf.Abs(sailingSalvageVelocity) > 0.025f &&
                salvageScreenPosition.x >= -7.2f && salvageScreenPosition.x <= 7.2f;
            SetEnabled(sailingSalvageWakeRenderer, wakeVisible);
            if (wakeVisible)
            {
                float speed01 = Mathf.InverseLerp(0.025f, 0.72f, Mathf.Abs(sailingSalvageVelocity));
                float wakeDirection = Mathf.Sign(sailingSalvageVelocity);
                sailingSalvageWakeRenderer.sprite = GetSwimWakeSprite();
                SetWorldSize(
                    sailingSalvageWakeRenderer,
                    salvageScreenPosition + new Vector2(-wakeDirection * (0.3f + speed01 * 0.16f), -0.08f),
                    new Vector2(0.34f + speed01 * 0.58f, 0.09f + speed01 * 0.04f),
                    new Color(0.76f, 0.94f, 0.9f, 0.24f + speed01 * 0.28f),
                    0f);
                sailingSalvageWakeRenderer.flipX = wakeDirection < 0f;
            }
        }
        else
        {
            SetEnabled(sailingSalvageWakeRenderer, false);
        }
        SetSailingDestinationVisual(sailingWreckPointRenderer, SailingPointKind.Wreck, time, sailingWreckClueClaimed || sailingClueCollected);
        UpdateSailingReefVisuals(time);
    }

    private void UpdateSailingReefVisuals(float time)
    {
        Vector2 reefScreenPosition = GetSailingScreenPosition(sailingReefPoint);
        bool onScreen = reefScreenPosition.x >= -7.4f && reefScreenPosition.x <= 7.4f;
        TideSailingReefSample reef = GetSailingReefSample(sailingBoatWorldVelocity);
        TideOceanSample ocean = GetSailingOceanSample(sailingReefPoint.x);
        sailingReef.UpdatePresentation(
            sailingReefPointRenderer,
            sailingReefFoamRenderer,
            onScreen,
            reefScreenPosition,
            reef,
            ocean,
            time,
            TideBarrenIslandController.GetSharedReefRockSprite(),
            GetFormalSeaCurrentCrestSprite());
    }

    private void UpdateSailingFlowCrestVisuals(float time, float daylight01)
    {
        float flow = GetSailingSurfaceFlowSpeed();
        float strength01 = Mathf.InverseLerp(0.035f, 0.62f, Mathf.Abs(flow));
        bool visible = strength01 > 0.01f && GetFormalSeaCurrentCrestSprite() != null;
        int visibleFlowCrests = strength01 >= 0.72f ? 2 : 1;
        for (int i = 0; i < sailingFlowCrests.Count; i++)
        {
            SpriteRenderer crest = sailingFlowCrests[i];
            bool crestVisible = visible && i < visibleFlowCrests;
            SetEnabled(crest, crestVisible);
            if (!crestVisible)
            {
                continue;
            }

            crest.sprite = GetFormalSeaCurrentCrestSprite();
            float phaseTravel01 = Mathf.Repeat(
                GetStableVisualVariation01(i, 509) + sailingFlowCrestTravelWorld / 13.8f,
                1f);
            float x = Mathf.Lerp(-6.9f, 6.9f, phaseTravel01);
            float worldX = x + GetSailingCameraWorldX();
            TideOceanSample ocean = GetSailingOceanSample(worldX);
            float y = ocean.SurfaceY + 0.015f;
            float width = 1.1f + (i % 2) * 0.42f + strength01 * 0.34f;
            float alpha = Mathf.Lerp(0.1f, 0.34f, strength01) * Mathf.Lerp(0.82f, 1f, daylight01);
            SetWorldSize(
                crest,
                new Vector2(x, y),
                new Vector2(width, 0.18f + strength01 * 0.1f),
                new Color(0.76f, 0.95f, 0.92f, alpha),
                Mathf.Atan(ocean.Slope) * Mathf.Rad2Deg * 0.42f +
                Mathf.Sin(time * 0.35f + i) * 0.6f);
            crest.flipX = flow < 0f;
        }
    }

    private void UpdateSideViewVortexVisuals(
        float time,
        bool visible,
        Vector2 surfacePosition,
        TideOceanSample ocean,
        float storm01,
        float daylight01)
    {
        bool useFormalV70 = visible && HasCompleteV70VortexPresentation();
        SetEnabled(routeVortexRenderer, visible);
        SetEnabled(routeVortexInnerRenderer, visible);
        SetEnabled(routeVortexSurfaceRenderer, useFormalV70);
        naturalWaterSurfaceRenderer.maskInteraction = SpriteMaskInteraction.None;
        if (!visible)
        {
            SetEnabled(vortexCrests, false);
            return;
        }

        if (useFormalV70)
        {
            UpdateV70VortexVisuals(time, surfacePosition, ocean, storm01, daylight01);
            return;
        }

        // From the side, a whirlpool is read mainly through converging surface waves,
        // a narrow dark throat and submerged return flow. Keep the body translucent so
        // the V56 sea remains continuous; an opaque replacement patch looked like a
        // separate pool pasted over the ocean.
        float randomGroup01 = Mathf.PerlinNoise(time * 0.11f + 6.3f, 2.7f);
        float pulse = (randomGroup01 - 0.5f) * 0.075f + Mathf.Sin(time * 0.73f) * 0.018f;
        float slopeDegrees = Mathf.Atan(ocean.Slope) * Mathf.Rad2Deg * 0.12f;
        routeVortexRenderer.sprite = GetSideViewVortexDepressionSprite();
        routeVortexRenderer.sortingOrder = naturalWaterSurfaceRenderer.sortingOrder + 3;
        SetWorldSize(
            routeVortexRenderer,
            surfacePosition + new Vector2(0f, -0.34f + pulse * 0.18f),
            new Vector2(3.5f + pulse, 1.18f),
            new Color(0.045f, 0.16f, 0.19f, 0.38f),
            slopeDegrees);

        routeVortexInnerRenderer.sprite = GetSideViewVortexUndertowSprite();
        routeVortexInnerRenderer.sortingOrder = routeVortexRenderer.sortingOrder + 1;
        SetWorldSize(
            routeVortexInnerRenderer,
            surfacePosition + new Vector2(0f, -0.45f + pulse * 0.25f),
            new Vector2(3.1f - pulse * 0.5f, 0.92f + pulse * 0.12f),
            new Color(0.38f, 0.62f, 0.62f, 0.28f),
            slopeDegrees + Mathf.Sin(time * 0.47f) * 0.8f);

        float inward01 = Mathf.SmoothStep(0f, 1f, Mathf.PerlinNoise(time * 0.16f + 3.2f, 9.4f));
        for (int i = 0; i < vortexCrests.Count; i++)
        {
            SpriteRenderer crest = vortexCrests[i];
            // 侧视画面只保留两侧汇入浪和中央喉口泡沫。旧的四条椭圆绕圈线
            // 在侧视相机中仍会读成俯视圆盘，也是用户看到 U 形调试物的来源。
            bool showCrest = i < 3;
            SetEnabled(crest, showCrest);
            if (!showCrest)
            {
                continue;
            }

            bool centerFoam = i == 2;
            if (centerFoam)
            {
                crest.sprite = GetSwimWakeSprite();
                crest.sortingOrder = routeVortexInnerRenderer.sortingOrder + 1;
                SetWorldSize(
                    crest,
                    surfacePosition + new Vector2(0f, -0.08f + pulse),
                    new Vector2(0.68f + inward01 * 0.16f, 0.12f),
                    new Color(0.82f, 0.94f, 0.91f, 0.68f),
                    slopeDegrees);
                crest.flipX = false;
                continue;
            }

            float side = i == 0 ? -1f : 1f;
            Sprite animatedCrest = HasCompleteV43SeaWeatherPresentation()
                ? TideV43SeaWeatherPresentationModel.EvaluateWaveFrame(
                    formalV43SeaWeatherCatalog,
                    TideV43WaveKind.LongSwell,
                    time,
                    i * 0.47f,
                    1.06f)
                : null;
            crest.sprite = animatedCrest ?? GetFormalSeaCurrentCrestSprite() ?? GetFoamSprite();
            crest.sortingOrder = routeVortexInnerRenderer.sortingOrder + 1;
            float x = side * Mathf.Lerp(1.32f, 0.72f, inward01);
            float y = -0.035f - inward01 * 0.16f + pulse * side * 0.25f;
            SetWorldSize(
                crest,
                surfacePosition + new Vector2(x, y),
                new Vector2(1.24f + inward01 * 0.14f, 0.2f),
                new Color(0.84f, 0.95f, 0.92f, 0.78f),
                slopeDegrees + side * Mathf.Lerp(4f, 11f, inward01));
            crest.flipX = side > 0f;
        }
    }

    private void UpdateV70VortexVisuals(
        float time,
        Vector2 surfacePosition,
        TideOceanSample ocean,
        float storm01,
        float daylight01)
    {
        // V70 是三层原子替换。只要正式 Catalog 完整，旧程序泡沫就必须整体休眠；
        // 否则两套汇流会在同一水线叠成高亮噪点，并重新产生“贴了一块水”的观感。
        SetEnabled(vortexCrests, false);

        Color daylightTint = Color.Lerp(
            new Color(0.84f, 0.9f, 0.92f, 1f),
            Color.white,
            Mathf.Clamp01(daylight01));
        Color stormTint = Color.Lerp(
            daylightTint,
            new Color(0.76f, 0.84f, 0.86f, 1f),
            Mathf.Clamp01(storm01) * 0.36f);
        Color waterTint = GetFormalWaterTint(storm01);
        Color sharedTint = new Color(
            stormTint.r * waterTint.r,
            stormTint.g * waterTint.g,
            stormTint.b * waterTint.b,
            1f);

        int baseOrder = naturalWaterSurfaceRenderer.sortingOrder + 3;
        ApplyV70VortexLayer(
            routeVortexRenderer,
            TideV70VortexLayer.ThroatDepression,
            surfacePosition,
            ocean,
            time,
            baseOrder,
            new Color(sharedTint.r, sharedTint.g, sharedTint.b, 0.88f));
        ApplyV70VortexLayer(
            routeVortexInnerRenderer,
            TideV70VortexLayer.UnderwaterReturnFlow,
            surfacePosition,
            ocean,
            time,
            baseOrder + 1,
            new Color(sharedTint.r, sharedTint.g, sharedTint.b, 0.76f));
        ApplyV70VortexLayer(
            routeVortexSurfaceRenderer,
            TideV70VortexLayer.SurfaceConvergenceFoam,
            surfacePosition,
            ocean,
            time,
            baseOrder + 2,
            new Color(sharedTint.r, sharedTint.g, sharedTint.b, 0.94f));
    }

    private void ApplyV70VortexLayer(
        SpriteRenderer renderer,
        TideV70VortexLayer layer,
        Vector2 surfacePosition,
        TideOceanSample ocean,
        float time,
        int sortingOrder,
        Color tint)
    {
        TideV70VortexPose pose = TideV70VortexPresentationModel.EvaluatePose(
            layer,
            surfacePosition,
            ocean.Slope,
            ocean.Agitation01,
            time);
        Sprite frame = formalV70VortexCatalog.GetFrame(layer, pose.FrameIndex);
        renderer.sortingOrder = sortingOrder;
        renderer.maskInteraction = SpriteMaskInteraction.None;
        SetUniformSprite(
            renderer,
            frame,
            pose.Position,
            pose.UniformScale,
            tint,
            pose.RotationDegrees,
            false);

        // 轻微水平压缩来自所有三层共享的低频包络。只改 X，不改 Y 或独立水位，
        // 因而不会让漩涡像呼吸气球，也不会破坏资源契约中的垂直水线配准。
        Vector3 scale = renderer.transform.localScale;
        scale.x *= pose.HorizontalCompression;
        renderer.transform.localScale = scale;
    }

    private void SetSailingDestinationVisual(SpriteRenderer renderer, SailingPointKind point, float time, bool visited)
    {
        if (renderer == null)
        {
            return;
        }

        renderer.sprite = GetSailingPointSprite(point);
        Vector2 pointWorldPosition = GetSailingPointPosition(point);
        Vector2 position = GetSailingScreenPosition(pointWorldPosition);
        TideOceanSample pointOcean = GetSailingOceanSample(pointWorldPosition.x);
        bool onScreen = position.x >= -7.4f && position.x <= 7.4f;
        SetEnabled(renderer, onScreen);
        if (!onScreen)
        {
            return;
        }
        float pulse = Mathf.Sin(time * 2.1f + (int)point * 0.7f) * 0.035f;
        bool formalWreck = point == SailingPointKind.Wreck &&
            (HasCompleteV40ShipwreckPresentation() || GetFormalSprite(ref formalShipwreckSprite, "AIShipwreck") != null);
        bool formalSalvage = point == SailingPointKind.Salvage &&
            GetFormalSprite(ref formalRepairTimberSprite, "AIRepairTimber") != null;
        Color color = formalWreck || formalSalvage ? Color.white : GetSailingPointColor(point, visited);
        Vector2 scale = HasCompleteV40ShipwreckPresentation() && point == SailingPointKind.Wreck
            ? Vector2.one * 1.28f
            : GetSailingPointScale(point) + new Vector2(pulse * 0.6f, pulse * 0.35f);
        if (formalWreck || formalSalvage)
        {
            float submergeOffset = formalWreck ? -0.08f : -0.04f;
            float waterSlopeRotation = Mathf.Atan(pointOcean.Slope) * Mathf.Rad2Deg;
            SetWorldSize(
                renderer,
                position + new Vector2(0f, submergeOffset),
                scale,
                color,
                formalSalvage ? waterSlopeRotation * 0.42f : 0f);
        }
        else
        {
            Set(renderer, position + new Vector2(0f, pulse * 0.45f), scale, color, GetSailingPointRotation(point, time));
        }
    }

    private Sprite GetSailingPointSprite(SailingPointKind point)
    {
        if (point == SailingPointKind.Buoy)
        {
            return GetSailingRouteBuoySprite();
        }

        if (point == SailingPointKind.Salvage)
        {
            return GetFormalSprite(ref formalRepairTimberSprite, "AIRepairTimber") ?? GetWaterWrackSprite();
        }

        if (point == SailingPointKind.Reef)
        {
            return TideBarrenIslandController.GetSharedReefRockSprite();
        }

        return HasCompleteV40ShipwreckPresentation()
            ? GetV40ShipwreckSprite(2 + tideRound)
            : GetShipwreckRibSprite();
    }

    private bool HasCompleteV40ShipwreckPresentation()
    {
        if (v40ShipwreckOriginSprites == null ||
            v40ShipwreckOriginSprites.Length != V40WreckWaterlineTopLeftY.Length)
        {
            return false;
        }

        for (int i = 0; i < v40ShipwreckOriginSprites.Length; i++)
        {
            if (v40ShipwreckOriginSprites[i] == null)
            {
                return false;
            }
        }

        return true;
    }

    private Sprite GetV40ShipwreckSprite(int variant)
    {
        if (!HasCompleteV40ShipwreckPresentation())
        {
            return GetShipwreckRibSprite();
        }

        int index = Mathf.Abs(variant) % v40ShipwreckOriginSprites.Length;
        return v40ShipwreckOriginSprites[index];
    }

    private static float GetV40WreckWaterlineOffsetY(int variant, float canvasWorldSize)
    {
        int index = Mathf.Abs(variant) % V40WreckWaterlineTopLeftY.Length;
        // V40 是 2048 方画布、中心 Pivot。该值是静水线相对画布中心的世界偏移。
        return (1024f - V40WreckWaterlineTopLeftY[index]) / 2048f * canvasWorldSize;
    }

    private Color GetSailingPointColor(SailingPointKind point, bool visited)
    {
        Color baseColor;
        if (point == SailingPointKind.Buoy)
        {
            baseColor = new Color(0.58f, 0.95f, 0.9f, 0.86f);
        }
        else if (point == SailingPointKind.Salvage)
        {
            baseColor = new Color(0.74f, 0.58f, 0.34f, 0.94f);
        }
        else if (point == SailingPointKind.Reef)
        {
            baseColor = CanSeeLighthouse()
                ? new Color(0.78f, 0.9f, 0.82f, 0.78f)
                : new Color(0.52f, 0.74f, 0.72f, 0.58f);
        }
        else
        {
            baseColor = new Color(0.84f, 0.64f, 0.38f, 0.88f);
        }

        return visited ? Color.Lerp(baseColor, new Color(1f, 0.78f, 0.32f, 0.96f), 0.55f) : baseColor;
    }

    private static Vector2 GetSailingPointScale(SailingPointKind point)
    {
        if (point == SailingPointKind.Buoy)
        {
            return new Vector2(0.2f, 0.42f);
        }

        if (point == SailingPointKind.Salvage)
        {
            return new Vector2(0.98f, 0.4f);
        }

        if (point == SailingPointKind.Reef)
        {
            return new Vector2(0.56f, 0.26f);
        }

        return GetFormalSprite(ref formalShipwreckSprite, "AIShipwreck") != null
            ? new Vector2(0.82f, 0.58f)
            : new Vector2(0.22f, 0.72f);
    }

    private static float GetSailingPointRotation(SailingPointKind point, float time)
    {
        if (point == SailingPointKind.Reef)
        {
            return Mathf.Sin(time * 0.7f) * 4f;
        }

        if (point == SailingPointKind.Buoy)
        {
            return Mathf.Sin(time * 1.4f) * 5f;
        }

        if (point == SailingPointKind.Salvage)
        {
            return -4f + Mathf.Sin(time * 0.9f) * 2f;
        }

        return -18f + Mathf.Sin(time * 0.8f) * 3f;
    }

    private Sprite EvaluateV20CharacterFrame(
        TideV20CharacterActionState actionState,
        float worldTime,
        float playbackRate = 1f)
    {
        if (formalCharacterV20Catalog == null)
        {
            return null;
        }

        if (actionState == TideV20CharacterActionState.Idle)
        {
            // 无输入不是动作。V20 的四张 Idle 由独立生成轮廓组成，循环播放会
            // 产生肩、头和衣摆抖动；稳定接触帧比伪造的加速呼吸更接近真实。
            return formalCharacterV20Catalog.GetFrame(TideV20CharacterActionState.Idle, 0);
        }

        if (actionState != TideV20CharacterActionState.Walk &&
            actionState != TideV20CharacterActionState.Swim)
        {
            return TideV20CharacterPresentationModel.EvaluateFrame(
                formalCharacterV20Catalog,
                actionState,
                worldTime,
                playbackRate);
        }

        // 位移动作只随实际位移推进，不消费全局时钟。这样站定、打开菜单或重新
        // 按方向键都不会把人物瞬间切到一张随机跨步帧；资源会话后续替换为真正
        // 同模逐帧时，这条规则仍然成立。
        int frameCount = TideV20CharacterPresentationModel.GetFrameCount(actionState);
        int frameIndex = Mathf.FloorToInt(Mathf.Repeat(playerWalkCycle, 1f) * frameCount) % frameCount;
        return formalCharacterV20Catalog.GetFrame(actionState, frameIndex);
    }

    private void UpdateInteriorSceneVisuals(float time, float daylight01, float storm01)
    {
        // This is a continuous side-view cutaway, not a menu. The work deck, inhabited
        // floor, and roof loft are three real walkable lanes in the same exterior shell.
        HideInteriorExcludedVisuals();
        // These edges sit on authored facade posts. Keeping the cut exactly between
        // them avoids the old translucent rectangle floating over the exterior.
        const float interiorMinX = -5.18f;
        const float interiorMaxX = 0.08f;
        float interiorCenterX = (interiorMinX + interiorMaxX) * 0.5f;
        float interiorWidth = interiorMaxX - interiorMinX;
        float workFloorY = GetPlayerStandingFeetY(WalkLane.InteriorLower);
        float livingFloorY = GetPlayerStandingFeetY(WalkLane.InteriorUpper);
        float loftFloorY = GetPlayerStandingFeetY(WalkLane.InteriorLoft);
        const float roomTopY = 2.02f;
        float livingCeilingY = loftFloorY - 0.04f;
        float cutawayHeight = roomTopY - livingFloorY;
        float cutawayCenterY = (roomTopY + livingFloorY) * 0.5f;

        Sprite formalHouse = GetFormalHouseSprite();
        if (formalHouse != null)
        {
            // Keep the exact exterior roof, posts and stair behind the cutaway. The
            // front wall is replaced by the room layers below, so entering the house
            // reads as seeing inside the same building rather than loading a wood box.
            houseRenderer.sortingOrder = 18;
            houseRenderer.maskInteraction = SpriteMaskInteraction.None;
            houseRenderer.sprite = formalHouse;
            Color shellTint = Color.Lerp(
                new Color(0.56f, 0.63f, 0.62f, 1f),
                new Color(0.82f, 0.79f, 0.71f, 1f),
                Mathf.Clamp01(GetShelterRestoration01() * 0.58f + daylight01 * 0.3f));
            SetWorldSize(houseRenderer, GetFormalHouseWorldPosition(), GetFormalHouseWorldSize(), shellTint, 0f);

            // Cut only the occupied wall bay out of the exact exterior sprite. Roof,
            // eaves, outer posts, stilts and the real outside stair remain visible, so
            // entering the house reveals the same building instead of swapping sets.
            houseRenderer.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
            interiorCutawayMask.isCustomRangeActive = true;
            interiorCutawayMask.frontSortingOrder = houseRenderer.sortingOrder + 2;
            interiorCutawayMask.backSortingOrder = houseRenderer.sortingOrder - 1;
            // The occupied cutaway is rectangular through the living floor, then
            // follows the shallow roof pitch around the loft. A rectangular mask
            // erased the centre of the authored roof and made the attic look like a
            // separate black box sitting on top of the house.
            interiorCutawayMask.sprite = GetInteriorThreeLevelCutawayMaskSprite();
            SetMaskWorldSize(
                interiorCutawayMask,
                new Vector2(interiorCenterX, cutawayCenterY),
                new Vector2(interiorWidth, cutawayHeight),
                0f);
        }
        else
        {
            SetEnabled(houseRenderer, false);
            SetMaskEnabled(interiorCutawayMask, false);
            interiorBackdropRenderer.maskInteraction = SpriteMaskInteraction.None;
            interiorLoftBackdropRenderer.maskInteraction = SpriteMaskInteraction.None;
        }

        // The exterior sky/cloud pass has already run. Only the enclosed living room
        // receives a wall; the lower work deck remains open between the same stilts and
        // the same sea seen outside.
        Sprite formalInteriorWall = GetFormalInteriorWallSprite();
        interiorBackdropRenderer.sprite = formalInteriorWall ?? GetMoonWashSprite();
        Color roomWall = formalInteriorWall != null
            ? Color.Lerp(new Color(0.58f, 0.63f, 0.61f, 1f), new Color(0.84f, 0.8f, 0.7f, 1f), daylight01 * 0.48f)
            : Color.Lerp(new Color(0.035f, 0.048f, 0.044f, 0.99f), new Color(0.16f, 0.125f, 0.09f, 0.99f), daylight01 * 0.38f);
        roomWall = Color.Lerp(roomWall, new Color(0.36f, 0.4f, 0.39f, 1f), storm01 * 0.28f);
        interiorBackdropRenderer.sortingOrder = 19;
        interiorBackdropRenderer.maskInteraction = formalHouse != null
            ? SpriteMaskInteraction.VisibleInsideMask
            : SpriteMaskInteraction.None;
        float livingWallHeight = livingCeilingY - livingFloorY;
        SetWorldSize(
            interiorBackdropRenderer,
            new Vector2(interiorCenterX, (livingCeilingY + livingFloorY) * 0.5f),
            new Vector2(interiorWidth, livingWallHeight),
            roomWall,
            0f);

        Sprite formalLoftWall = GetFormalInteriorLoftWallSprite();
        interiorLoftBackdropRenderer.sprite = formalLoftWall ?? GetMoonWashSprite();
        interiorLoftBackdropRenderer.sortingOrder = 19;
        interiorLoftBackdropRenderer.maskInteraction = formalHouse != null
            ? SpriteMaskInteraction.VisibleInsideMask
            : SpriteMaskInteraction.None;
        Color loftWall = formalLoftWall != null
            ? Color.Lerp(new Color(0.48f, 0.52f, 0.49f, 1f), new Color(0.72f, 0.64f, 0.51f, 1f), daylight01 * 0.42f)
            : new Color(0.1f, 0.105f, 0.09f, 0.99f);
        loftWall = Color.Lerp(loftWall, new Color(0.33f, 0.37f, 0.37f, 1f), storm01 * 0.3f);
        SetWorldSize(
            interiorLoftBackdropRenderer,
            new Vector2(interiorCenterX, (roomTopY + loftFloorY) * 0.5f),
            new Vector2(interiorWidth, roomTopY - loftFloorY),
            loftWall,
            0f);

        // The authored rear wall already supplies boards and window frames. Only the
        // two real cut posts remain in front; extra bars previously read as debug UI.
        SetEnabled(interiorWallPlanks, false);
        for (int i = 0; i < 2; i++)
        {
            interiorWallPlanks[i].sprite = GetPostSprite();
            interiorWallPlanks[i].sortingOrder = 21;
        }
        // The surviving front posts in the full house sprite already frame both cut
        // edges. Drawing another pair here produced two pitch-black vertical bars.
        SetEnabled(interiorWallPlanks[0], false);
        SetEnabled(interiorWallPlanks[1], false);

        // The lower lane is an open undercroft catwalk fixed to the same visible
        // stilts. The upper floor is only a slim cut edge of the exterior deck.
        interiorLowerFloorRenderer.sprite = GetFormalBoardwalkSprite() ?? GetDeckSprite();
        interiorLowerFloorRenderer.sortingOrder = 22;
        SetWorldSize(
            interiorLowerFloorRenderer,
            new Vector2(-2.57f, workFloorY - 0.24f),
            new Vector2(4.86f, 0.76f),
            new Color(0.68f, 0.67f, 0.58f, 0.96f),
            0f);
        interiorUpperFloorRenderer.sprite = GetDeckSprite();
        SetWorldSize(
            interiorUpperFloorRenderer,
            new Vector2(interiorCenterX, livingFloorY - 0.04f),
            new Vector2(interiorWidth, 0.16f),
            new Color(0.42f, 0.33f, 0.21f, 0.98f),
            0f);
        interiorLoftFloorRenderer.sprite = GetFormalBoardwalkSprite() ?? GetDeckSprite();
        SetWorldSize(
            interiorLoftFloorRenderer,
            new Vector2(interiorCenterX, loftFloorY - 0.06f),
            new Vector2(interiorWidth, 0.22f),
            new Color(0.46f, 0.39f, 0.28f, 0.98f),
            0f);

        // Movement interpolates actor centres; the visible stair belongs under the
        // feet, exactly one standing half-height below that trajectory.
        Vector2 stairBottom = GetInteriorStairBottomPosition() + Vector2.down * 0.58f;
        Vector2 stairTop = GetInteriorStairTopPosition() + Vector2.down * 0.58f;
        Vector2 stairMid = (stairBottom + stairTop) * 0.5f + new Vector2(0f, -0.05f);
        Vector2 stairDirection = stairTop - stairBottom;
        float stairAngle = Mathf.Atan2(stairDirection.y, stairDirection.x) * Mathf.Rad2Deg;
        interiorStairRenderer.sprite = GetDeckSprite();
        interiorStairRenderer.sortingOrder = 23;
        SetWorldSize(interiorStairRenderer, stairMid, new Vector2(stairDirection.magnitude + 0.12f, 0.15f), new Color(0.82f, 0.68f, 0.45f, 1f), stairAngle);
        for (int i = 0; i < interiorStairTreads.Count; i++)
        {
            float tread01 = (i + 0.5f) / interiorStairTreads.Count;
            Vector2 treadPosition = Vector2.Lerp(stairBottom, stairTop, tread01);
            SetWorldSize(
                interiorStairTreads[i],
                treadPosition + new Vector2(0f, 0.015f),
                new Vector2(0.27f, 0.065f),
                new Color(0.66f, 0.53f, 0.34f, 0.98f),
                0f);
        }

        Vector2 loftStairBottom = GetInteriorLoftStairBottomPosition() + Vector2.down * 0.58f;
        Vector2 loftStairTop = GetInteriorLoftStairTopPosition() + Vector2.down * 0.58f;
        Vector2 loftStairMid = (loftStairBottom + loftStairTop) * 0.5f + new Vector2(0f, -0.04f);
        Vector2 loftStairDirection = loftStairTop - loftStairBottom;
        float loftStairAngle = Mathf.Atan2(loftStairDirection.y, loftStairDirection.x) * Mathf.Rad2Deg;
        interiorLoftStairRenderer.sprite = GetDeckSprite();
        interiorLoftStairRenderer.sortingOrder = 23;
        SetWorldSize(
            interiorLoftStairRenderer,
            loftStairMid,
            new Vector2(loftStairDirection.magnitude + 0.12f, 0.14f),
            new Color(0.72f, 0.58f, 0.37f, 1f),
            loftStairAngle);
        for (int i = 0; i < interiorLoftStairTreads.Count; i++)
        {
            float tread01 = (i + 0.5f) / interiorLoftStairTreads.Count;
            Vector2 treadPosition = Vector2.Lerp(loftStairBottom, loftStairTop, tread01);
            SetWorldSize(
                interiorLoftStairTreads[i],
                treadPosition + new Vector2(0f, 0.015f),
                new Vector2(0.26f, 0.06f),
                new Color(0.62f, 0.48f, 0.3f, 0.98f),
                0f);
        }

        // The source house has a broad, shallow salt-metal roof rather than a steep
        // triangular gable. Two short hips and a long ridge preserve that silhouette
        // while still exposing enough headroom for the playable loft.
        Vector2 leftRoofRidge = new Vector2(interiorCenterX - interiorWidth * 0.34f, roomTopY - 0.08f);
        Vector2 rightRoofRidge = new Vector2(interiorCenterX + interiorWidth * 0.34f, roomTopY - 0.08f);
        Vector2 leftRafterFoot = new Vector2(interiorMinX + 0.1f, loftFloorY + 0.02f);
        Vector2 rightRafterFoot = new Vector2(interiorMaxX - 0.1f, loftFloorY + 0.02f);
        SetRafterWorldSize(interiorLoftRafters[0], leftRafterFoot, leftRoofRidge, 0.11f);
        SetRafterWorldSize(interiorLoftRafters[1], rightRoofRidge, rightRafterFoot, 0.11f);
        SetRafterWorldSize(interiorLoftRafters[2], leftRoofRidge, rightRoofRidge, 0.1f);
        SetWorldSize(
            interiorLoftRafters[3],
            new Vector2(interiorCenterX, (loftFloorY + roomTopY) * 0.5f),
            new Vector2(0.1f, roomTopY - loftFloorY),
            new Color(0.46f, 0.35f, 0.23f, 0.92f),
            0f);

        Sprite formalRoofCap = GetFormalInteriorRoofCapSprite();
        interiorRoofCapRenderer.sprite = formalRoofCap ?? GetRoofSprite();
        interiorRoofCapRenderer.sortingOrder = 25;
        interiorRoofCapRenderer.maskInteraction = SpriteMaskInteraction.None;
        SetWorldSize(
            interiorRoofCapRenderer,
            new Vector2(interiorCenterX, roomTopY - 0.06f),
            new Vector2(interiorWidth * 0.72f, 0.2f),
            new Color(0.8f, 0.72f, 0.6f, 0.98f),
            0f);

        SetEnabled(interiorUpperWarmthRenderer, false);
        float comfort01 = Mathf.Clamp01((interiorComfort + GetUncommittedRepairPreview01(RepairChoice.Bed)) / 2f);
        interiorBedFrameRenderer.sprite = GetFormalBoardwalkSprite() ?? GetDeckSprite();
        SetWorldSize(
            interiorBedFrameRenderer,
            new Vector2(GetInteriorBedX(), loftFloorY + 0.11f),
            Vector2.Lerp(new Vector2(0.92f, 0.16f), new Vector2(1.34f, 0.2f), comfort01),
            Color.Lerp(new Color(0.27f, 0.2f, 0.13f, 0.98f), new Color(0.48f, 0.34f, 0.2f, 1f), comfort01),
            0f);
        interiorBedHeadboardRenderer.sprite = GetPostSprite();
        SetWorldSize(
            interiorBedHeadboardRenderer,
            new Vector2(GetInteriorBedX() + Mathf.Lerp(0.4f, 0.61f, comfort01), loftFloorY + 0.28f),
            new Vector2(0.12f, Mathf.Lerp(0.38f, 0.5f, comfort01)),
            Color.Lerp(new Color(0.25f, 0.18f, 0.12f, 0.98f), new Color(0.48f, 0.34f, 0.2f, 1f), comfort01),
            -1f);
        SetWorldSize(
            interiorBedRenderer,
            new Vector2(GetInteriorBedX(), loftFloorY + 0.22f),
            Vector2.Lerp(new Vector2(0.72f, 0.2f), new Vector2(1.12f, 0.3f), comfort01),
            Color.Lerp(new Color(0.38f, 0.37f, 0.33f, 0.92f), new Color(0.82f, 0.76f, 0.61f, 0.98f), comfort01),
            -2f);
        interiorLoftLookoutTableRenderer.sprite = GetDeckSprite();
        SetWorldSize(
            interiorLoftLookoutTableRenderer,
            new Vector2(GetInteriorLoftLookoutX(), loftFloorY + 0.13f),
            new Vector2(0.82f, 0.18f),
            new Color(0.42f, 0.32f, 0.2f, 0.98f),
            0f);
        interiorLoftLookoutRenderer.sprite = GetRelicPieceSprite(0);
        SetWorldSize(
            interiorLoftLookoutRenderer,
            new Vector2(GetInteriorLoftLookoutX(), loftFloorY + 0.34f),
            new Vector2(0.62f, 0.42f),
            HasCurrentLoftForecast()
                ? new Color(1f, 0.92f, 0.72f, 1f)
                : new Color(0.7f, 0.72f, 0.65f, 0.9f),
            -4f);
        // The wall crop already owns its windows. Separate square overlays created
        // doubled frames and black debug-looking panes, so warmth comes from the lamp
        // and stove instead of drawing a second set of windows.
        SetEnabled(interiorWindowRenderers, false);
        float stove01 = Mathf.Clamp01((stoveCondition + GetUncommittedRepairPreview01(RepairChoice.Lamp)) / 2f);
        SetWorldSize(
            interiorLampRenderer,
            new Vector2(GetInteriorLampX(), livingFloorY + 0.92f),
            new Vector2(0.2f, 0.28f),
            new Color(1f, 0.72f, 0.3f, Mathf.Lerp(0.12f, 0.92f, stove01)),
            Mathf.Sin(time * 1.8f) * 0.8f);
        SetWorldSize(interiorStoveRenderer, new Vector2(GetInteriorLampX(), livingFloorY + 0.22f), new Vector2(0.58f, 0.48f), new Color(0.16f, 0.17f, 0.15f, 0.98f), 0f);
        SetWorldSize(interiorStoveGlowRenderer, new Vector2(GetInteriorLampX(), livingFloorY + 0.23f), new Vector2(0.22f, 0.2f), new Color(1f, 0.5f, 0.16f, Mathf.Lerp(0.03f, 0.94f, stove01)), 0f);
        interiorStorageRenderer.sprite = GetDeckSprite();
        SetWorldSize(
            interiorStorageRenderer,
            new Vector2(GetInteriorStorageX(), livingFloorY + 0.24f),
            new Vector2(0.82f, 0.18f),
            new Color(0.42f, 0.31f, 0.2f, 0.98f),
            0f);

        bool[] storedKindsVisible =
        {
            timberStock > 0,
            ropeStock + clothStock > 0,
            metalStock > 0,
            foodStock > 0
        };
        Sprite[] storedSprites =
        {
            GetWoodSprite(),
            GetPrepRopeCoilSprite(),
            GetNetWeightSprite(),
            GetFishSprite()
        };
        Vector2[] storedSizes =
        {
            new Vector2(0.38f, 0.15f),
            new Vector2(0.25f, 0.22f),
            new Vector2(0.16f, 0.18f),
            new Vector2(0.34f, 0.14f)
        };
        for (int i = 0; i < interiorStoredItems.Count; i++)
        {
            SpriteRenderer storedItem = interiorStoredItems[i];
            storedItem.enabled = storedKindsVisible[i];
            if (!storedItem.enabled)
            {
                continue;
            }

            storedItem.sprite = storedSprites[i];
            Vector2 itemPosition = new Vector2(
                GetInteriorStorageX() - 0.3f + i * 0.2f,
                livingFloorY + 0.38f + (i % 2) * 0.045f);
            SetWorldSize(storedItem, itemPosition, storedSizes[i], Color.white, -4f + i * 3f);
        }
        float roofRepairPreview01 = GetUncommittedRepairPreview01(RepairChoice.Roof);
        float visualRoofLevel = roofIntegrity + roofRepairPreview01;
        SetEnabled(interiorRoofPatchRenderer, visualRoofLevel > 0.01f);
        if (interiorRoofPatchRenderer.enabled)
        {
            interiorRoofPatchRenderer.sprite = GetBrokenHousePieceSprite();
            float patchReveal01 = roofIntegrity > 0 ? 1f : roofRepairPreview01;
            SetWorldSize(
                interiorRoofPatchRenderer,
                new Vector2(-0.72f, loftFloorY + 0.68f),
                new Vector2((0.82f + visualRoofLevel * 0.1f) * Mathf.Lerp(0.22f, 1f, patchReveal01), 0.18f),
                new Color(0.52f, 0.28f, 0.16f, Mathf.Lerp(0.22f, 0.94f, patchReveal01)),
                -2f);
        }

        UpdateInteriorSharedSeaAndMoon(time, daylight01, storm01);
        // The undercroft is open to the sea, so its first wet pixel must use the same
        // world waterline as the exterior. Shelter stress is a damage threshold, not
        // a second hidden water height.
        float interiorFloodDepth = Mathf.Max(0f, currentWaterY - workFloorY);
        bool lowerRoomFlooded = interiorFloodDepth > 0.02f;
        SetEnabled(interiorFloodRenderer, lowerRoomFlooded);
        if (lowerRoomFlooded)
        {
            float floodTopY = Mathf.Min(currentWaterY + 0.12f, livingFloorY - 0.08f);
            float floodBodyHeight = Mathf.Max(0.4f, floodTopY - (lowWaterY - 0.7f));
            interiorFloodRenderer.sprite = GetFormalWaterSurfaceSprite() ?? GetWaterSprite();
            SetWorldSize(
                interiorFloodRenderer,
                new Vector2(0f, floodTopY - floodBodyHeight * 0.5f),
                new Vector2(14.7f, floodBodyHeight),
                new Color(0.78f, 0.94f, 0.92f, 0.68f),
                Mathf.Sin(time * 0.28f) * 0.16f);
            // 正式海面资源使用语义水线 Pivot，并非几何中心。室内进水需要让
            // “真正可见的最上沿”与外部潮位一致，否则同一个潮在门内会低约 9cm。
            AlignRendererBoundsTop(interiorFloodRenderer, floodTopY);
        }

        UpdateTidePrepVisuals(time);
        SetEnabled(playerGlowRenderer, false);
        SetEnabled(playerSwimWakeRenderer, false);
        EnsureV20CharacterResourcesLoaded();
        EnsureV41CharacterContactResourcesLoaded();
        EnsureV42CharacterSurvivalResourcesLoaded();
        EnsureV32ArtResourcesLoaded();
        bool useV20Character = HasCompleteV20CharacterPresentation();
        bool useV41Contact = HasCompleteV41CharacterContactPresentation();
        bool useV32InteriorClimb = HasCompleteV32ArtPresentation() &&
            isLaneTransitioning &&
            viewMode == SliceViewMode.Interior &&
            ((laneTransitionFromLane == WalkLane.InteriorLower && laneTransitionTarget == WalkLane.InteriorUpper) ||
             (laneTransitionFromLane == WalkLane.InteriorUpper && laneTransitionTarget == WalkLane.InteriorLower) ||
             (laneTransitionFromLane == WalkLane.InteriorUpper && laneTransitionTarget == WalkLane.InteriorLoft) ||
             (laneTransitionFromLane == WalkLane.InteriorLoft && laneTransitionTarget == WalkLane.InteriorUpper));
        TideV20CharacterActionState interiorAction =
            TideV20CharacterPresentationModel.ResolveActionState(
                false,
                false,
                repairWorkActive,
                playerMoving);
        float interiorSpeed01 = playerMoveSpeed > 0.01f
            ? Mathf.Clamp01(Mathf.Abs(playerHorizontalVelocity) / playerMoveSpeed)
            : 0f;
        float interiorClimbProgress = GetV32ClimbPlaybackProgress(
            laneTransitionFromLane,
            laneTransitionTarget,
            laneTransitionProgress);
        float interiorTransitionDuration = Mathf.Max(0.2f, boatViewTransitionSeconds);
        float interiorTransition01 = boatViewTransition == BoatViewTransition.None
            ? 0f
            : Mathf.Clamp01(boatViewTransitionTimer / interiorTransitionDuration);
        bool v42InteriorFrameVisible = TryGetV42SurvivalWorldFrame(
            !useV32InteriorClimb &&
            !playerMoving &&
            !playerSwimming &&
            !repairWorkActive &&
            boatViewTransition == BoatViewTransition.None,
            time,
            out Sprite v42InteriorFrame,
            out Vector2 v42InteriorPivot,
            out float v42InteriorScale,
            out bool v42InteriorFlipX);
        bool useV41InteriorDoor = useV41Contact &&
            !v42InteriorFrameVisible &&
            !useV32InteriorClimb &&
            ((boatViewTransition == BoatViewTransition.EnteringInterior && interiorTransition01 >= 0.5f) ||
             (boatViewTransition == BoatViewTransition.ExitingInterior && interiorTransition01 <= 0.5f));
        bool useV41InteriorLookout = useV41Contact &&
            !v42InteriorFrameVisible &&
            !useV32InteriorClimb &&
            ((boatViewTransition == BoatViewTransition.EnteringLookout && interiorTransition01 <= 0.5f) ||
             (boatViewTransition == BoatViewTransition.ExitingLookout && interiorTransition01 >= 0.5f));
        bool carryingInteriorHarvest = harvestPhysicalState == HarvestPhysicalState.Carried &&
            currentHarvest != HarvestKind.None;
        bool useV41InteriorCarryIdle = useV41Contact &&
            !v42InteriorFrameVisible &&
            !useV32InteriorClimb &&
            !useV41InteriorDoor &&
            !useV41InteriorLookout &&
            !playerMoving &&
            carryingInteriorHarvest;
        bool useV41InteriorWalk = useV41Contact &&
            !v42InteriorFrameVisible &&
            !useV32InteriorClimb &&
            !useV41InteriorDoor &&
            !useV41InteriorLookout &&
            playerMoving &&
            interiorAction == TideV20CharacterActionState.Walk;
        TideV41CharacterContactAction interiorV41Action =
            netRigStep == NetRigStep.Carrying || carryingInteriorHarvest
            ? TideV41CharacterContactAction.CarryNetWalk
            : TideV41CharacterContactAction.Walk;
        float interiorDoorLeg01 = boatViewTransition == BoatViewTransition.EnteringInterior
            ? Mathf.Clamp01((interiorTransition01 - 0.5f) * 2f)
            : Mathf.Clamp01(interiorTransition01 * 2f);
        float interiorLookoutLeg01 = boatViewTransition == BoatViewTransition.EnteringLookout
            ? Mathf.Clamp01(interiorTransition01 * 2f)
            : Mathf.Clamp01((interiorTransition01 - 0.5f) * 2f);
        Sprite interiorV41Frame = useV41InteriorDoor
            ? TideV41CharacterContactPresentationModel.EvaluateOneShotFrame(
                formalCharacterV41ContactCatalog,
                TideV41CharacterContactAction.DoorEnter,
                interiorDoorLeg01,
                boatViewTransition == BoatViewTransition.ExitingInterior)
            : useV41InteriorLookout
                ? TideV41CharacterContactPresentationModel.EvaluateOneShotFrame(
                    formalCharacterV41ContactCatalog,
                    TideV41CharacterContactAction.Lookout,
                    interiorLookoutLeg01,
                    boatViewTransition == BoatViewTransition.ExitingLookout)
            : useV41InteriorCarryIdle
                ? formalCharacterV41ContactCatalog.GetFrame(
                    TideV41CharacterContactAction.CarryNetWalk,
                    0)
            : useV41InteriorWalk
                ? TideV41CharacterContactPresentationModel.EvaluateLoopFrame(
                formalCharacterV41ContactCatalog,
                interiorV41Action,
                playerWalkCycle)
            : null;
        Sprite interiorLandFrame = v42InteriorFrameVisible
            ? v42InteriorFrame
            : useV32InteriorClimb
            ? formalV32ArtCatalog.GetClimbFrame(interiorClimbProgress)
            : interiorV41Frame != null
            ? interiorV41Frame
            : useV20Character
            ? EvaluateV20CharacterFrame(
                interiorAction,
                time,
                interiorAction == TideV20CharacterActionState.Walk
                    ? Mathf.Lerp(0.65f, 1.35f, interiorSpeed01)
                    : 1f)
            : GetPlayerSprite();
        Sprite interiorSwimFrame = useV20Character
            ? EvaluateV20CharacterFrame(
                TideV20CharacterActionState.Swim,
                time)
            : GetFormalSwimPlayerSprite();
        float interiorBlend = playerLane == WalkLane.InteriorLower
            ? Mathf.Clamp01(playerAquaticBlend01)
            : 0f;
        // SmoothStep's first two arguments are output values, not blend thresholds.
        // Map submersion into 0..1 first, matching the exterior transition, so a dry
        // upper floor renders one opaque land body and no residual swimming ghost.
        float interiorLandAlpha = v42InteriorFrameVisible || useV32InteriorClimb
            ? 1f
            : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.82f, interiorBlend));
        float interiorSwimAlpha = v42InteriorFrameVisible || useV32InteriorClimb
            ? 0f
            : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 0.94f, interiorBlend));
        float walkWave = playerMoving && !v42InteriorFrameVisible &&
            !useV32InteriorClimb && interiorV41Frame == null
            ? Mathf.Sin(playerWalkCycle * Mathf.PI * 2f)
            : 0f;
        Vector2 drawPosition = playerPosition +
            new Vector2(playerFacing * walkWave * 0.004f, Mathf.Abs(walkWave) * 0.012f * interiorSpeed01);
        Vector2 landPivot = v42InteriorFrameVisible
            ? v42InteriorPivot
            : drawPosition + Vector2.down * TideV20CharacterPresentationModel.BodyWorldLength * 0.5f;
        if (!v42InteriorFrameVisible && interiorV41Frame != null)
        {
            landPivot.y += TideV41CharacterContactPresentationModel.FootPivotCorrectionWorldY;
        }
        Color actorTint = GetSurvivorClothingTint(daylight01);
        playerRenderer.sortingOrder = lowerRoomFlooded && playerLane == WalkLane.InteriorLower ? 25 : 27;
        float landScale = v42InteriorFrameVisible
            ? v42InteriorScale
            : useV32InteriorClimb
            ? formalV32ArtCatalog.ClimbUniformScale
            : interiorV41Frame != null
            ? TideV41CharacterContactPresentationModel.UniformScale
            : useV20Character
            ? TideV20CharacterPresentationModel.CalculateUniformScale(interiorAction, interiorLandFrame)
            : 1.16f / Mathf.Max(0.01f, interiorLandFrame != null ? interiorLandFrame.bounds.size.y : 1.16f);
        SetEnabled(playerRenderer, interiorLandFrame != null && interiorLandAlpha > 0.01f);
        if (playerRenderer.enabled)
        {
            SetUniformSprite(
                playerRenderer,
                interiorLandFrame,
                landPivot,
                landScale,
                new Color(actorTint.r, actorTint.g, actorTint.b, interiorLandAlpha),
                v42InteriorFrameVisible || useV32InteriorClimb || interiorV41Frame != null
                    ? 0f
                    : -playerFacing * walkWave * 0.55f * interiorSpeed01 -
                        playerFacing * interiorBlend * 16f,
                v42InteriorFrameVisible
                    ? v42InteriorFlipX
                    : !useV32InteriorClimb && playerFacing < 0);
        }

        SetEnabled(playerAquaticRenderer, interiorSwimFrame != null && interiorSwimAlpha > 0.01f);
        if (playerAquaticRenderer.enabled)
        {
            TideOceanSample interiorOcean = GetOceanSample(drawPosition.x);
            float swimScale = useV20Character
                ? TideV20CharacterPresentationModel.CalculateUniformScale(
                    TideV20CharacterActionState.Swim,
                    interiorSwimFrame)
                : 1.16f / Mathf.Max(0.01f, interiorSwimFrame.bounds.size.x);
            playerAquaticRenderer.sortingOrder = 25;
            SetUniformSprite(
                playerAquaticRenderer,
                interiorSwimFrame,
                Vector2.Lerp(drawPosition, new Vector2(drawPosition.x, interiorOcean.SurfaceY - 0.035f), interiorBlend),
                swimScale,
                new Color(actorTint.r, actorTint.g, actorTint.b, interiorSwimAlpha),
                Mathf.Atan(interiorOcean.Slope) * Mathf.Rad2Deg * 0.18f,
                playerFacing < 0);
        }

        UpdatePlayerBagStoryMask(
            playerRenderer,
            playerBagMask,
            drawPosition,
            useV20Character || interiorV41Frame != null || useV32InteriorClimb,
            interiorBlend > 0.5f,
            false,
            repairWorkActive);
        if (v42InteriorFrameVisible)
        {
            SetMaskEnabled(playerBagMask, false);
        }
        // Materials remain physical while the player crosses the exterior/interior
        // boundary. The interior hide pass runs first, then this redraws the same
        // carried bundle beside the survivor instead of making it disappear at the door.
        UpdateHarvestVisuals(Mathf.PingPong(time * 0.6f, 1f));
        if (!ApplyV35RegisteredInteriorPresentation(daylight01, storm01) &&
            !ApplyV30RegisteredInteriorPresentation(time, daylight01, storm01))
        {
            ApplyV27RegisteredInteriorPresentation(daylight01, storm01);
        }

        if (formalHouse == null)
        {
            SetEnabled(houseRenderer, false);
        }
        SetEnabled(verandaRenderer, false);
        SetEnabled(roofRenderer, false);
        SetEnabled(porchRoofRenderer, false);
        SetEnabled(longEaveRenderer, false);
        SetEnabled(underHouseShadowRenderer, false);
        SetEnabled(deckRenderer, false);
        SetEnabled(windowGlowRenderer, false);
        SetEnabled(formalBoardwalkSegments, false);
        SetEnabled(stiltPosts, false);
        SetEnabled(houseDiagonalBraces, false);
        SetEnabled(houseGableRoofs, false);
        SetEnabled(denseStiltPosts, false);
        SetEnabled(houseRoofRepairMarks, false);
        SetEnabled(houseWallRepairMarks, false);
        SetEnabled(houseInteriorVoidRenderers, false);
        SetMaskEnabled(houseRoofHoleMasks, false);
        SetMaskEnabled(houseWindowHoleMasks, false);
        SetMaskEnabled(houseWallGapMasks, false);
        SetEnabled(laundryCloths, false);
        SetEnabled(netLines, false);
        SetEnabled(netSuspensionRopes, false);
        SetEnabled(formalNetRenderer, false);
        SetEnabled(formalNetBundleRenderer, false);
        SetEnabled(netHandlingRopeRenderer, false);
        SetEnabled(netWeights, false);
        SetEnabled(netKnots, false);
        SetEnabled(netFloats, false);
        SetEnabled(netCaughtItems, false);
        SetEnabled(netDamageMarkers, false);
        SetEnabled(netWaterContactRenderer, false);
        SetEnabled(boatBackRigRenderer, false);
        SetEnabled(boatHullRenderer, false);
        SetEnabled(boatSailRenderer, false);
        SetEnabled(boatLandingWalkwayRenderer, false);
        SetEnabled(boatWakeRenderer, false);
        SetEnabled(boatWaterlineOcclusionRenderer, false);
        SetEnabled(shipwreckRibs, false);
    }

    private bool ApplyV69ExteriorRepairPresentation(
        Vector2 housePivotWorldPosition,
        Color tint,
        float rotationZ)
    {
        if (!HasCompleteV69CurrentHousePresentation())
        {
            return false;
        }

        Vector2 stableBounds = formalHouseV69ActiveBase.StableBase.bounds.size;
        float uniformScale = GetFormalHouseWorldSize().x / Mathf.Max(0.001f, stableBounds.x);
        float radians = rotationZ * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        for (int i = 0; i < formalHouseV69ActiveStructuralStages.Length; i++)
        {
            TideV69HouseStructuralStageAsset stage = formalHouseV69ActiveStructuralStages[i];
            Vector2 scaledOffset = stage.OffsetFromHousePivot * uniformScale;
            Vector2 rotatedOffset = new Vector2(
                scaledOffset.x * cos - scaledOffset.y * sin,
                scaledOffset.x * sin + scaledOffset.y * cos);
            SpriteRenderer renderer = v34ExteriorRepairOwnerRenderers[i];
            // V69 owner 像素互斥，统一排序即可；不同序号只用于稳定 Transform Z，
            // 不代表一块结构可以盖住另一块结构或人物。
            renderer.sortingOrder = 6;
            renderer.maskInteraction = SpriteMaskInteraction.None;
            SetUniformSprite(
                renderer,
                stage.Sprite,
                housePivotWorldPosition + rotatedOffset,
                uniformScale,
                tint,
                rotationZ,
                false);
        }
        return true;
    }

    private bool ApplyV69RegisteredInteriorPresentation(float daylight01, float storm01)
    {
        if (!HasCompleteV69CurrentHousePresentation())
        {
            return false;
        }

        HideLegacyInteriorPiecesForRegisteredHouse();
        SetEnabled(v30RepairOwnerRenderers, false);

        float neutralLight = Mathf.Lerp(0.78f, 1f, daylight01);
        float stormDim = Mathf.Lerp(1f, 0.9f, storm01);
        Color tint = new Color(
            neutralLight * stormDim * 0.985f,
            neutralLight * stormDim * 0.992f,
            neutralLight * stormDim,
            1f);

        houseRenderer.enabled = true;
        houseRenderer.sortingOrder = 18;
        houseRenderer.maskInteraction = SpriteMaskInteraction.None;
        houseRenderer.sprite = formalHouseV69ActiveBase.StableBase;
        SetWorldSize(
            houseRenderer,
            GetFormalHouseWorldPosition(),
            GetFormalHouseWorldSize(),
            tint,
            0f);

        Vector2 stableBounds = formalHouseV69ActiveBase.StableBase.bounds.size;
        float uniformScale = GetFormalHouseWorldSize().x / Mathf.Max(0.001f, stableBounds.x);
        int rendererIndex = 0;
        for (int i = 0; i < formalHouseV69ActiveStructuralStages.Length; i++)
        {
            TideV69HouseStructuralStageAsset stage = formalHouseV69ActiveStructuralStages[i];
            SpriteRenderer renderer = v35InteriorRepairOwnerRenderers[rendererIndex++];
            renderer.sortingOrder = 19;
            renderer.maskInteraction = SpriteMaskInteraction.None;
            SetUniformSprite(
                renderer,
                stage.Sprite,
                GetFormalHouseWorldPosition() + stage.OffsetFromHousePivot * uniformScale,
                uniformScale,
                tint,
                0f,
                false);
        }

        for (int i = 0; i < formalHouseV69ActiveBinaryStages.Length; i++)
        {
            TideV69HouseBinaryStageAsset stage = formalHouseV69ActiveBinaryStages[i];
            SpriteRenderer renderer = v35InteriorRepairOwnerRenderers[rendererIndex++];
            renderer.sortingOrder = 19;
            renderer.maskInteraction = SpriteMaskInteraction.None;
            SetUniformSprite(
                renderer,
                stage.Sprite,
                GetFormalHouseWorldPosition() + stage.OffsetFromHousePivot * uniformScale,
                uniformScale,
                tint,
                0f,
                false);
        }

        return rendererIndex == TideV35HouseInteriorPresentationModel.OwnerCount;
    }

    private void ApplyV34ExteriorRepairPresentation(
        Vector2 housePivotWorldPosition,
        Color tint,
        float rotationZ)
    {
        if (ApplyV69ExteriorRepairPresentation(
            housePivotWorldPosition,
            tint,
            rotationZ))
        {
            return;
        }

        if (!HasCompleteV34HouseExteriorPresentation())
        {
            SetEnabled(v34ExteriorRepairOwnerRenderers, false);
            return;
        }

        float foundationSteps = Mathf.Clamp(
            stiltIntegrity - 1f + GetUncommittedRepairPreview01(RepairChoice.Stilt),
            0f,
            2f);
        float roofSteps = Mathf.Clamp(
            roofIntegrity + GetUncommittedRepairPreview01(RepairChoice.Roof),
            0f,
            2f);
        float interiorSteps = Mathf.Clamp(
            GetInteriorSealVisualLevel() + GetUncommittedRepairPreview01(RepairChoice.InteriorSeal),
            0f,
            2f);

        Vector2 stableBounds = formalHouseV34ExteriorCatalog.StableBase.bounds.size;
        float uniformScale = GetFormalHouseWorldSize().x / Mathf.Max(0.001f, stableBounds.x);
        float radians = rotationZ * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        TideV34HouseExteriorCatalog.OwnerEntry[] owners = formalHouseV34ExteriorCatalog.Owners;
        for (int i = 0; i < owners.Length; i++)
        {
            TideV34HouseExteriorCatalog.OwnerEntry owner = owners[i];
            bool useRepair = TideV34HouseExteriorPresentationModel.UseRepairSprite(
                owner.Key,
                foundationSteps,
                roofSteps,
                interiorSteps);
            Vector2 scaledOffset = owner.WorldOffsetFromHousePivot * uniformScale;
            Vector2 rotatedOffset = new Vector2(
                scaledOffset.x * cos - scaledOffset.y * sin,
                scaledOffset.x * sin + scaledOffset.y * cos);
            SpriteRenderer renderer = v34ExteriorRepairOwnerRenderers[i];
            renderer.sortingOrder = 6 + i;
            renderer.maskInteraction = SpriteMaskInteraction.None;
            SetUniformSprite(
                renderer,
                useRepair ? owner.RepairSprite : owner.DamageSprite,
                housePivotWorldPosition + rotatedOffset,
                uniformScale,
                tint,
                rotationZ,
                false);
        }
    }

    private bool ApplyV35RegisteredInteriorPresentation(float daylight01, float storm01)
    {
        if (ApplyV69RegisteredInteriorPresentation(daylight01, storm01))
        {
            return true;
        }

        EnsureV35HouseInteriorResourcesLoaded();
        if (!HasCompleteV35HouseInteriorPresentation())
        {
            SetEnabled(v35InteriorRepairOwnerRenderers, false);
            return false;
        }

        HideLegacyInteriorPiecesForRegisteredHouse();
        SetEnabled(v30RepairOwnerRenderers, false);

        float foundationSteps = Mathf.Clamp(
            stiltIntegrity - 1f + GetUncommittedRepairPreview01(RepairChoice.Stilt),
            0f,
            2f);
        float roofSteps = Mathf.Clamp(
            roofIntegrity + GetUncommittedRepairPreview01(RepairChoice.Roof),
            0f,
            2f);
        float sealSteps = Mathf.Clamp(
            GetInteriorSealVisualLevel() +
            GetUncommittedRepairPreview01(RepairChoice.InteriorSeal),
            0f,
            1f);
        float workbenchSteps = Mathf.Clamp(
            workbenchCondition + GetUncommittedRepairPreview01(RepairChoice.Workbench),
            0f,
            1f);
        float bedSteps = Mathf.Clamp(
            GetInteriorBedVisualLevel() +
            GetUncommittedRepairPreview01(RepairChoice.Bed),
            0f,
            1f);
        float chartRadioSteps = Mathf.Clamp(
            chartRadioCondition + GetUncommittedRepairPreview01(RepairChoice.ChartRadio),
            0f,
            1f);
        float heatSteps = Mathf.Clamp(
            stoveCondition + GetUncommittedRepairPreview01(RepairChoice.Lamp),
            0f,
            2f);

        // The V35 palette is already matched to V32/V33, V31 and the survivor.
        // Preserve authored rust/wood color and only apply neutral daylight plus a
        // restrained storm dim; a cyan multiplier would recreate the old mismatch.
        float neutralLight = Mathf.Lerp(0.78f, 1f, daylight01);
        float stormDim = Mathf.Lerp(1f, 0.9f, storm01);
        Color tint = new Color(
            neutralLight * stormDim * 0.985f,
            neutralLight * stormDim * 0.992f,
            neutralLight * stormDim,
            1f);

        houseRenderer.enabled = true;
        houseRenderer.sortingOrder = 18;
        houseRenderer.maskInteraction = SpriteMaskInteraction.None;
        houseRenderer.sprite = formalHouseV35InteriorCatalog.StableBase;
        SetWorldSize(
            houseRenderer,
            GetFormalHouseWorldPosition(),
            GetFormalHouseWorldSize(),
            tint,
            0f);

        Vector2 stableBounds = formalHouseV35InteriorCatalog.StableBase.bounds.size;
        float uniformScale = GetFormalHouseWorldSize().x / Mathf.Max(0.001f, stableBounds.x);
        TideV35HouseInteriorCatalog.OwnerEntry[] owners = formalHouseV35InteriorCatalog.Owners;
        for (int i = 0; i < owners.Length; i++)
        {
            TideV35HouseInteriorCatalog.OwnerEntry owner = owners[i];
            bool useRepair;
            if (owner.Key == "InteriorEnvelope" || owner.Key == "EntryDoor")
            {
                useRepair = sealSteps >= 0.5f;
            }
            else if (owner.Key == "Workbench")
            {
                useRepair = workbenchSteps >= 0.5f;
            }
            else if (owner.Key == "Bed")
            {
                useRepair = bedSteps >= 0.5f;
            }
            else if (owner.Key == "ChartRadio")
            {
                useRepair = chartRadioSteps >= 0.5f;
            }
            else
            {
                useRepair = TideV35HouseInteriorPresentationModel.UseRepairSprite(
                    owner.GameplayOwner,
                    owner.RequiredStep,
                    foundationSteps,
                    roofSteps,
                    0f,
                    heatSteps);
            }
            SpriteRenderer renderer = v35InteriorRepairOwnerRenderers[i];
            // Owner masks are pixel-exclusive, so they do not need an artificial
            // front-to-back ladder. Keep every room layer behind the survivor (27)
            // and carried materials while still above the stable base (18).
            renderer.sortingOrder = 19;
            renderer.maskInteraction = SpriteMaskInteraction.None;
            SetUniformSprite(
                renderer,
                useRepair ? owner.RepairSprite : owner.DamageSprite,
                GetFormalHouseWorldPosition() + owner.WorldOffsetFromHousePivot * uniformScale,
                uniformScale,
                tint,
                0f,
                false);
        }

        return true;
    }

    private bool ApplyV30RegisteredInteriorPresentation(float time, float daylight01, float storm01)
    {
        EnsureV30HouseResourcesLoaded();
        if (!HasCompleteV30HousePresentation())
        {
            return false;
        }

        HideLegacyInteriorPiecesForRegisteredHouse();

        float foundationSteps = Mathf.Clamp(
            stiltIntegrity - 1f + GetUncommittedRepairPreview01(RepairChoice.Stilt),
            0f,
            2f);
        float roofSteps = Mathf.Clamp(
            roofIntegrity + GetUncommittedRepairPreview01(RepairChoice.Roof),
            0f,
            2f);
        float interiorSteps = Mathf.Clamp(
            interiorComfort + GetUncommittedRepairPreview01(RepairChoice.InteriorSeal),
            0f,
            2f);
        float heatSteps = Mathf.Clamp(
            stoveCondition + GetUncommittedRepairPreview01(RepairChoice.Lamp),
            0f,
            2f);
        bool fullyRepaired = TideV30HousePresentationModel.IsFullyRepaired(
            stiltIntegrity - 1f,
            roofIntegrity,
            interiorComfort,
            stoveCondition);

        float light = Mathf.Lerp(0.7f, 0.99f, daylight01);
        Color tint = Color.Lerp(
            new Color(light * 0.94f, light * 0.97f, light, 1f),
            new Color(0.72f, 0.8f, 0.81f, 1f),
            storm01 * 0.26f);
        houseRenderer.enabled = true;
        houseRenderer.sortingOrder = 18;
        houseRenderer.maskInteraction = SpriteMaskInteraction.None;

        if (fullyRepaired)
        {
            int frame = TideV30HousePresentationModel.EvaluateInteriorFrame(time);
            houseRenderer.sprite = formalHouseV30Catalog.InteriorRepairedFrames[frame];
            SetEnabled(v30RepairOwnerRenderers, false);
        }
        else
        {
            // 中间维修态严格使用 StableBase + 每个 owner 恰好一张状态层。
            // V30 合规矩阵明确没有局部过渡画，因此施工中段采用互斥换片，
            // 不把 Damage 与 Repair 双显成半透明重影。
            houseRenderer.sprite = formalHouseV30Catalog.StableBase;
            TideV30HouseRuntimeCatalog.RepairOwnerEntry[] owners = formalHouseV30Catalog.RepairOwners;
            for (int i = 0; i < owners.Length; i++)
            {
                TideV30HouseRuntimeCatalog.RepairOwnerEntry owner = owners[i];
                bool useRepair = TideV30HousePresentationModel.UseRepairSprite(
                    owner.Key,
                    foundationSteps,
                    roofSteps,
                    interiorSteps,
                    heatSteps);
                SpriteRenderer renderer = v30RepairOwnerRenderers[i];
                renderer.enabled = true;
                renderer.sortingOrder = 19 + i;
                renderer.sprite = useRepair ? owner.RepairSprite : owner.DamageSprite;
                SetWorldSize(
                    renderer,
                    GetFormalHouseWorldPosition() + owner.WorldOffsetFromHousePivot,
                    renderer.sprite.bounds.size,
                    tint,
                    0f);
            }
        }

        SetWorldSize(
            houseRenderer,
            GetFormalHouseWorldPosition(),
            GetFormalHouseWorldSize(),
            tint,
            0f);
        return true;
    }

    private void UpdateStormRescueVisuals(float time, float storm01)
    {
        if (stormRescueRenderers.Count < TideStormRescueController.ItemCount ||
            stormRescueRopeRenderers.Count < TideStormRescueController.ItemCount)
        {
            SetEnabled(stormRescueRenderers, false);
            SetEnabled(stormRescueRopeRenderers, false);
            return;
        }

        bool pressureVisible = storm01 >= 0.34f || IsStormRescueActive();
        float localCurrentSpeed = GetStormRescueLocalCurrentSpeed();
        for (int i = 0; i < TideStormRescueController.ItemCount; i++)
        {
            TideStormRescueItemState item = stormRescue.GetItem(i);
            SpriteRenderer renderer = stormRescueRenderers[i];
            SpriteRenderer ropeRenderer = stormRescueRopeRenderers[i];
            bool visible = item.Present &&
                (pressureVisible || item.Secured || item.WashoutProgress01 > 0.001f) &&
                !item.Lost;
            renderer.enabled = visible;
            bool ropeVisible = visible && (item.SecuringProgress01 > 0.001f || item.Secured);
            ropeRenderer.enabled = ropeVisible;
            if (!visible)
            {
                continue;
            }

            Vector2 size;
            if (item.Kind == TideStormRescueItemKind.DrinkingWater)
            {
                renderer.sprite = GetPrepBucketSprite();
                size = new Vector2(0.3f, 0.34f);
            }
            else if (item.Kind == TideStormRescueItemKind.BoatMaterial)
            {
                renderer.sprite = GetFormalSprite(ref formalRepairTimberSprite, "AIRepairTimber") ?? GetWoodSprite();
                size = new Vector2(0.66f, 0.2f);
            }
            else if (item.Kind == TideStormRescueItemKind.StoveFuel)
            {
                renderer.sprite = GetWoodSprite();
                size = new Vector2(0.44f, 0.26f);
            }
            else
            {
                renderer.sprite = GetLighthouseChartClueSprite();
                size = new Vector2(0.34f, 0.24f);
            }

            Vector2 position = EvaluateStormRescueWorldPosition(
                i,
                item,
                localCurrentSpeed);
            float waterRock = item.Secured
                ? 0f
                : Mathf.Sin(time * 1.3f + i * 1.71f) * item.WashoutProgress01 * 7f;
            SetWorldSize(renderer, position, size, Color.white, waterRock);
            renderer.sortingOrder = item.Secured ? 25 : 27;
            if (ropeVisible)
            {
                ropeRenderer.sprite = GetNetLineSprite();
                ropeRenderer.sortingOrder = 26;
                SetThinRopeSegment(ropeRenderer, GetStormRescueBeamPosition(i), position);
            }
        }
    }

    private void ApplyV27RegisteredInteriorPresentation(float daylight01, float storm01)
    {
        Sprite interiorEndpoint = GetV27InteriorEndpoint();
        if (interiorEndpoint == null)
        {
            return;
        }

        // V27 already contains the room envelope, workbench, bed, stove, chart desk,
        // lookout and the same lower stilts as V26/V28. Keeping the old generated
        // room pieces would double every floor and make the registered art unreadable.
        HideLegacyInteriorPiecesForRegisteredHouse();

        houseRenderer.enabled = true;
        houseRenderer.sprite = interiorEndpoint;
        houseRenderer.sortingOrder = 18;
        houseRenderer.maskInteraction = SpriteMaskInteraction.None;
        float light = Mathf.Lerp(0.66f, 0.98f, daylight01);
        Color tint = Color.Lerp(
            new Color(light * 0.9f, light * 0.95f, light, 1f),
            new Color(0.7f, 0.78f, 0.79f, 1f),
            storm01 * 0.3f);
        SetWorldSize(
            houseRenderer,
            GetFormalHouseWorldPosition(),
            GetFormalHouseWorldSize(),
            tint,
            0f);
    }

    private void HideLegacyInteriorPiecesForRegisteredHouse()
    {
        SetEnabled(interiorBackdropRenderer, false);
        SetEnabled(interiorLowerFloorRenderer, false);
        SetEnabled(interiorUpperFloorRenderer, false);
        SetEnabled(interiorLoftBackdropRenderer, false);
        SetEnabled(interiorLoftFloorRenderer, false);
        SetEnabled(interiorStairRenderer, false);
        SetEnabled(interiorLoftStairRenderer, false);
        SetEnabled(interiorLoftLookoutTableRenderer, false);
        SetEnabled(interiorLoftLookoutRenderer, false);
        SetEnabled(interiorUpperWarmthRenderer, false);
        SetEnabled(interiorRoofCapRenderer, false);
        SetEnabled(interiorBedFrameRenderer, false);
        SetEnabled(interiorBedHeadboardRenderer, false);
        SetEnabled(interiorBedRenderer, false);
        SetEnabled(interiorLampRenderer, false);
        SetEnabled(interiorStoveRenderer, false);
        SetEnabled(interiorStoveGlowRenderer, false);
        SetEnabled(interiorRoofPatchRenderer, false);
        SetEnabled(interiorStorageRenderer, false);
        SetEnabled(interiorWallPlanks, false);
        SetEnabled(interiorWindowRenderers, false);
        SetEnabled(interiorStairTreads, false);
        SetEnabled(interiorLoftStairTreads, false);
        SetEnabled(interiorLoftRafters, false);
        SetMaskEnabled(interiorCutawayMask, false);
    }

    private void SetRafterWorldSize(SpriteRenderer renderer, Vector2 start, Vector2 end, float thickness)
    {
        Vector2 direction = end - start;
        renderer.sprite = GetPostSprite();
        renderer.sortingOrder = 23;
        SetWorldSize(
            renderer,
            (start + end) * 0.5f,
            new Vector2(direction.magnitude, thickness),
            new Color(0.48f, 0.36f, 0.23f, 0.94f),
            Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
    }

    private void HideInteriorExcludedVisuals()
    {
        // Sky, clouds, moon and the shared water surface intentionally remain enabled.
        // Everything below belongs to an exterior action or sailing feedback pass. Those
        // passes run before the interior early-return, so they must be explicitly cleared.
        SetEnabled(sailingSeaRenderer, false);
        SetEnabled(sailingWreckPointRenderer, false);
        SetEnabled(sailingBuoyPointRenderer, false);
        SetEnabled(sailingBuoyTetherRenderer, false);
        SetEnabled(sailingBuoySinkerRenderer, false);
        SetEnabled(sailingSalvagePointRenderer, false);
        SetEnabled(sailingSalvageWakeRenderer, false);
        SetEnabled(sailingSalvageHookRopeRenderer, false);
        SetEnabled(sailingSalvageHookRopeEndRenderer, false);
        SetEnabled(sailingSalvageHookRenderer, false);
        SetEnabled(sailingReefPointRenderer, false);
        SetEnabled(sailingReefFoamRenderer, false);
        SetEnabled(sailingRangeBreakerRenderer, false);
        SetEnabled(boatIngressWaterRenderer, false);
        SetEnabled(sailingBailBucketRenderer, false);
        SetEnabled(sailingBailSplashRenderer, false);
        SetEnabled(sailingFlowCrests, false);
        SetEnabled(routeVortexRenderer, false);
        SetEnabled(routeVortexInnerRenderer, false);
        SetEnabled(routeVortexSurfaceRenderer, false);
        SetEnabled(vortexCrests, false);
        naturalWaterSurfaceRenderer.maskInteraction = SpriteMaskInteraction.None;
        SetEnabled(boatBackRigRenderer, false);
        SetEnabled(boatCockpitRenderer, false);
        SetEnabled(boatPassengerRenderer, false);
        SetEnabled(boatPassengerGunwaleRenderer, false);
        SetEnabled(boatRudderRenderer, false);
        SetMaskEnabled(boatWaterlineMask, false);
        SetMaskEnabled(boatPassengerBagMask, false);

        SetEnabled(shelterWaveImpactRenderer, false);
        SetEnabled(shelterDamageRenderer, false);
        SetEnabled(stormSurgeWallRenderer, false);
        SetEnabled(brokenHousePieces, false);
        SetEnabled(snappedStiltPosts, false);
        SetEnabled(departureRouteWakes, false);
        SetEnabled(returnPressureWashRenderer, false);
        SetEnabled(boardwalkPathRenderer, false);
        SetEnabled(tideFlatPathRenderer, false);
        SetEnabled(boatGlowRenderer, false);
        SetEnabled(houseRepairMarks, false);
        SetEnabled(houseSaltStreaks, false);
        SetEnabled(shelterWarmRays, false);

        SetEnabled(harvestRenderer, false);
        SetEnabled(lighthouseChartClueRenderer, false);
        SetEnabled(stormPennantRenderer, false);
        SetEnabled(departureSignalRenderer, false);
        SetEnabled(tideRoutingRopeRenderer, false);
        SetEnabled(tideRoutingBoomRenderer, false);
        SetEnabled(tideRoutingWinchRenderer, false);
        // 潮前绳、桶、桩属于外部工作台。它们的更新发生在视图分支之前，
        // 若不在这里清除，进入正式剖面后会悬浮在 V30 桩间。
        SetEnabled(prepRopeCoilRenderer, false);
        SetEnabled(prepBucketRenderer, false);
        SetEnabled(prepStakeRenderer, false);
        SetEnabled(netWaterContactRenderer, false);
        SetEnabled(formalNetRenderer, false);
        SetEnabled(formalNetBundleRenderer, false);
        SetEnabled(netHandlingRopeRenderer, false);
        SetEnabled(netLines, false);
        SetEnabled(netSuspensionRopes, false);
        SetEnabled(netWeights, false);
        SetEnabled(netKnots, false);
        SetEnabled(netFloats, false);
        SetEnabled(netCaughtItems, false);
        SetEnabled(netDamageMarkers, false);
        SetEnabled(washedAwayHarvestItems, false);
        SetEnabled(incomingTideCarryItems, false);
        SetEnabled(waterWracks, false);
        SetEnabled(pillarCheckKnots, false);
        SetEnabled(boatRigging, false);
        SetEnabled(boatPatchStitches, false);
        SetEnabled(mooringRopeSegments, false);
        SetEnabled(mooringRopeEndRenderer, false);
        SetEnabled(houseRoofRepairMarks, false);
        SetEnabled(houseWallRepairMarks, false);
        SetEnabled(houseInteriorVoidRenderers, false);
        SetMaskEnabled(houseRoofHoleMasks, false);
        SetMaskEnabled(houseWindowHoleMasks, false);
        SetMaskEnabled(houseWallGapMasks, false);
    }

    private void UpdateInteriorSharedSeaAndMoon(float time, float daylight01, float storm01)
    {
        TideOceanSample interiorCenterOcean = GetOceanSample(0f);
        float visibleInteriorWaterY = interiorCenterOcean.SurfaceY;
        bool useFormalWater = GetFormalWaterSurfaceSprite() != null;
        if (useFormalWater)
        {
            SetEnabled(waterRenderer, false);
            SetEnabled(waterFoamRenderer, false);
            naturalWaterSurfaceRenderer.sprite = GetFormalWaterSurfaceSprite();
            float waterBodyHeight = 2.7f + storm01 * 0.18f;
            const float averageCrestOffset = 0.45f;
            SetWorldSize(
                naturalWaterSurfaceRenderer,
                new Vector2(0f, visibleInteriorWaterY - waterBodyHeight * 0.5f + averageCrestOffset),
                new Vector2(14.7f + storm01 * 0.45f, waterBodyHeight),
                GetFormalWaterTint(storm01),
                Mathf.Atan(interiorCenterOcean.Slope) * Mathf.Rad2Deg * 0.08f);
        }
        else
        {
            SetEnabled(naturalWaterSurfaceRenderer, false);
            float bodyHeight = Mathf.Max(0.4f, visibleInteriorWaterY - lowWaterY + 3.2f);
            SetWorldSize(
                waterRenderer,
                new Vector2(0f, visibleInteriorWaterY - bodyHeight * 0.5f),
                new Vector2(13.8f, bodyHeight),
                Color.Lerp(new Color(0.05f, 0.3f, 0.36f, 0.78f), dangerTideColor, storm01 * 0.42f),
                0f);
            SetWorldSize(waterFoamRenderer, new Vector2(0f, visibleInteriorWaterY + 0.05f), new Vector2(13.2f, 0.28f), new Color(0.82f, 0.95f, 0.92f, 0.72f), Mathf.Atan(interiorCenterOcean.Slope) * Mathf.Rad2Deg * 0.08f);
        }

        SetEnabled(waveStrips, false);
        float moonPhase01 = Mathf.Repeat(moonAgeDays / 29.53f, 1f);
        float illumination01 = GetMoonIllumination01(moonPhase01);
        float night01 = 1f - daylight01;
        bool useFormalMoon = GetFormalSprite(ref formalMoonSprite, "AIMoon") != null;
        Vector2 moonSize = useFormalMoon ? new Vector2(0.92f, 0.92f) : new Vector2(0.76f, 0.76f);
        SetWorldSize(
            moonRenderer,
            moonAnchor,
            moonSize,
            useFormalMoon
                ? new Color(1f, 1f, 1f, 0.04f + night01 * 0.92f)
                : new Color(0.76f, 0.82f, 0.78f, 0.16f + night01 * 0.78f),
            0f);
        moonShadowRenderer.sprite = GetFirstSliceMoonPhaseShadowSprite(moonPhase01, illumination01);
        SetWorldSize(moonShadowRenderer, moonAnchor, moonSize, new Color(0.006f, 0.026f, 0.03f, 0.14f + night01 * 0.78f), 0f);
        SetEnabled(sunRenderer, false);
        SetEnabled(lighthouseRenderer, false);
        SetEnabled(lighthouseBeamRenderer, false);
        SetEnabled(stormLineRenderer, false);
    }

    private void UpdateSailingSceneVisuals(float time, float storm01, float daylight01)
    {
        // 船上人物由 V32 Passenger 独占。岸上人物、旧光圈与程序索具若只停止
        // 更新而不禁用，会留在世界原点并压住桅杆，形成金色椭圆和悬空横线。
        SetEnabled(playerRenderer, false);
        SetEnabled(playerAquaticRenderer, false);
        SetEnabled(playerGlowRenderer, false);
        SetEnabled(playerSwimWakeRenderer, false);
        SetMaskEnabled(playerBagMask, false);
        SetEnabled(boatRigging, false);
        SetEnabled(interiorBackdropRenderer, false);
        SetEnabled(interiorLowerFloorRenderer, false);
        SetEnabled(interiorUpperFloorRenderer, false);
        SetEnabled(interiorLoftBackdropRenderer, false);
        SetEnabled(interiorLoftFloorRenderer, false);
        SetEnabled(interiorStairRenderer, false);
        SetEnabled(interiorLoftStairRenderer, false);
        SetEnabled(interiorLoftLookoutTableRenderer, false);
        SetEnabled(interiorLoftLookoutRenderer, false);
        SetEnabled(interiorUpperWarmthRenderer, false);
        SetEnabled(interiorRoofCapRenderer, false);
        SetEnabled(interiorBedFrameRenderer, false);
        SetEnabled(interiorBedHeadboardRenderer, false);
        SetEnabled(interiorBedRenderer, false);
        SetEnabled(interiorLampRenderer, false);
        SetEnabled(interiorStoveRenderer, false);
        SetEnabled(interiorStoveGlowRenderer, false);
        SetEnabled(interiorRoofPatchRenderer, false);
        SetEnabled(interiorStorageRenderer, false);
        SetEnabled(interiorFloodRenderer, false);
        SetMaskEnabled(interiorCutawayMask, false);
        SetEnabled(interiorWallPlanks, false);
        SetEnabled(interiorWindowRenderers, false);
        SetEnabled(interiorStairTreads, false);
        SetEnabled(interiorLoftStairTreads, false);
        SetEnabled(interiorLoftRafters, false);
        SetEnabled(houseRenderer, false);
        SetEnabled(verandaRenderer, false);
        SetEnabled(roofRenderer, false);
        SetEnabled(porchRoofRenderer, false);
        SetEnabled(longEaveRenderer, false);
        SetEnabled(underHouseShadowRenderer, false);
        SetEnabled(deckRenderer, false);
        SetEnabled(windowGlowRenderer, false);
        SetEnabled(houseRoofRepairMarks, false);
        SetEnabled(houseWallRepairMarks, false);
        SetEnabled(houseInteriorVoidRenderers, false);
        SetMaskEnabled(houseRoofHoleMasks, false);
        SetMaskEnabled(houseWindowHoleMasks, false);
        SetMaskEnabled(houseWallGapMasks, false);
        SetEnabled(boardwalkPathRenderer, false);
        SetEnabled(tideFlatPathRenderer, false);
        SetEnabled(boatLandingWalkwayRenderer, false);
        SetEnabled(formalBoardwalkSegments, false);
        SetEnabled(harvestRenderer, false);
        SetEnabled(stormLineRenderer, false);
        SetEnabled(lighthouseChartClueRenderer, false);
        SetEnabled(stormPennantRenderer, false);
        SetEnabled(departureSignalRenderer, false);
        SetEnabled(tideRoutingRopeRenderer, false);
        SetEnabled(tideRoutingBoomRenderer, false);
        SetEnabled(tideRoutingWinchRenderer, false);
        SetEnabled(stiltPosts, false);
        SetEnabled(houseDiagonalBraces, false);
        SetEnabled(houseGableRoofs, false);
        SetEnabled(denseStiltPosts, false);
        SetEnabled(stormSurgeWallRenderer, false);
        SetEnabled(brokenHousePieces, false);
        SetEnabled(snappedStiltPosts, false);
        SetEnabled(departureRouteWakes, false);
        SetEnabled(laundryCloths, false);
        SetEnabled(pillarCheckKnots, false);
        SetEnabled(netLines, false);
        SetEnabled(netSuspensionRopes, false);
        SetEnabled(formalNetRenderer, false);
        SetEnabled(formalNetBundleRenderer, false);
        SetEnabled(netHandlingRopeRenderer, false);
        SetEnabled(netWeights, false);
        SetEnabled(netKnots, false);
        SetEnabled(netFloats, false);
        SetEnabled(netCaughtItems, false);
        SetEnabled(netDamageMarkers, false);
        SetEnabled(netWaterContactRenderer, false);
        SetEnabled(incomingTideCarryItems, false);
        SetEnabled(houseRepairMarks, false);
        SetEnabled(houseSaltStreaks, false);
        SetEnabled(shelterWarmRays, false);
        SetEnabled(playerSwimWakeRenderer, false);

        TideOceanSample sailingCenterOcean = GetSailingOceanSample(GetSailingCameraWorldX());
        float seaPulse = sailingCenterOcean.SurfaceY + 0.82f;
        bool useFormalWater = GetFormalWaterSurfaceSprite() != null;
        if (useFormalWater)
        {
            float sailingWaterHeight = 2.74f + storm01 * 0.16f;
            float sailingWaterRotation = Mathf.Atan(sailingCenterOcean.Slope) * Mathf.Rad2Deg * 0.08f;
            float formalBodyBottomY = sailingCenterOcean.SurfaceY - sailingWaterHeight +
                FormalWaterAverageCrestOffset;
            float deepWaterTopY = formalBodyBottomY + 0.28f;
            const float deepWaterBottomY = -4.35f;

            // V56 has only a few opaque rows below its visible water body. Stretching
            // those rows created vertical streaks, so the authored wave face now fades
            // into a neutral deep-water volume instead of a repeated texture curtain.
            waterRenderer.sprite = GetMoonWashSprite();
            ApplyUnlitSpriteMaterial(waterRenderer);
            waterRenderer.sortingOrder = naturalWaterSurfaceRenderer.sortingOrder - 1;
            Color sailingDeepWaterColor = Color.Lerp(
                new Color(0.035f, 0.105f, 0.13f, 1f),
                new Color(0.065f, 0.18f, 0.2f, 1f),
                daylight01);
            sailingDeepWaterColor = Color.Lerp(
                sailingDeepWaterColor,
                new Color(0.025f, 0.07f, 0.095f, 1f),
                storm01 * 0.55f);
            SetWorldSize(
                waterRenderer,
                new Vector2(0f, (deepWaterTopY + deepWaterBottomY) * 0.5f),
                new Vector2(14.9f, Mathf.Max(0.4f, deepWaterTopY - deepWaterBottomY)),
                sailingDeepWaterColor,
                sailingWaterRotation);
            naturalWaterSurfaceRenderer.sprite = GetFormalWaterSurfaceSprite();
            SetRepeatingWorldSize(
                naturalWaterSurfaceRenderer,
                new Vector2(0f, sailingCenterOcean.SurfaceY - sailingWaterHeight * 0.5f + FormalWaterAverageCrestOffset - 0.02f),
                new Vector2(14.7f + storm01 * 0.45f, sailingWaterHeight),
                GetFormalWaterTint(storm01),
                sailingWaterRotation,
                false);
            // 航行画面只允许一张全宽前景水下层负责遮挡。它从局部真实水面稍上方
            // 一直延伸到画外，用渐隐透明边缘吞没船壳水线以下的部分；不能再给船
            // 贴一块会随船移动的独立海水，否则相同浪场会出现两个互不连续的相位。
            float absorptionTopY = sailingCenterOcean.SurfaceY + 0.18f;
            Color absorptionTint = Color.Lerp(
                new Color(0.025f, 0.095f, 0.12f, 0.34f),
                new Color(0.045f, 0.14f, 0.16f, 0.29f),
                daylight01);
            absorptionTint = Color.Lerp(
                absorptionTint,
                new Color(0.025f, 0.065f, 0.085f, 0.42f),
                storm01 * 0.52f);
            foregroundDeepWaterOcclusionRenderer.sprite = GetSubsurfaceFadeSprite();
            ApplyUnlitSpriteMaterial(foregroundDeepWaterOcclusionRenderer);
            // V52 在前船舷之后还有独立破口 owner 和舵。连续前景水必须压过
            // 最前船体层，不能停在旧 V39 的排序 9 而把破口补件露在水面下。
            foregroundDeepWaterOcclusionRenderer.sortingOrder = 11;
            SetWorldSize(
                foregroundDeepWaterOcclusionRenderer,
                new Vector2(0f, (absorptionTopY + deepWaterBottomY) * 0.5f),
                new Vector2(15.1f, absorptionTopY - deepWaterBottomY),
                absorptionTint,
                sailingWaterRotation);
            sailingSeaRenderer.sprite = GetDepthBlendBandSprite();
            ApplyUnlitSpriteMaterial(sailingSeaRenderer);
            sailingSeaRenderer.sortingOrder = naturalWaterSurfaceRenderer.sortingOrder + 1;
            SetWorldSize(
                sailingSeaRenderer,
                new Vector2(0f, formalBodyBottomY + 0.05f),
                new Vector2(15f, 0.78f),
                new Color(0.055f, 0.18f, 0.2f, 0.78f),
                sailingWaterRotation);
            SetEnabled(waterFoamRenderer, false);
        }
        else
        {
            SetEnabled(naturalWaterSurfaceRenderer, false);
            SetWorldSize(waterRenderer, new Vector2(0f, -2.62f), new Vector2(14.2f, 2.7f), GetReadableNearSeaColor(3, daylight01, storm01), 0f);
            SetWorldSize(sailingSeaRenderer, new Vector2(0f, -1.74f + seaPulse), new Vector2(14.5f, 0.86f), GetReadableNearSeaColor(0, daylight01, storm01), 0f);
            SetWorldSize(waterFoamRenderer, new Vector2(0f, -1.24f + seaPulse), new Vector2(13.6f, 0.34f), new Color(0.72f, 0.96f, 0.9f, 0.5f + tideStrength * 0.22f), 0f);
        }

        EnsureV43SeaWeatherResourcesLoaded();
        bool useV43Waves = useFormalWater && HasCompleteV43SeaWeatherPresentation();
        Sprite formalCrest = GetFormalSeaCurrentCrestSprite();
        bool useFormalCrests = useFormalWater && formalCrest != null;
        float sailingWind01 = Mathf.Clamp01(
            Mathf.Abs(GetNaturalSailingWindSpeed()) / Mathf.Max(0.01f, sailingWindMaxSpeed));
        float sailingWaveDirection = GetSailingSurfaceFlowSpeed() +
            GetNaturalSailingWindSpeed() * 0.35f;
        for (int i = 0; i < waveStrips.Count; i++)
        {
            TideWaveEventSample waveEvent = TideWaveEventFieldModel.Sample(
                i,
                waveStrips.Count,
                GetSailingCameraWorldX(),
                time,
                sailingWaveDirection,
                sailingWind01,
                storm01,
                sailingCenterOcean.Agitation01);
            if (!waveEvent.Visible)
            {
                SetEnabled(waveStrips[i], false);
                continue;
            }

            float worldX = waveEvent.WorldX;
            float x = worldX - GetSailingCameraWorldX();
            if (IsVortexBlockingRoute() && Mathf.Abs(worldX - routeVortexX) < 2.25f)
            {
                SetEnabled(waveStrips[i], false);
                continue;
            }
            TideOceanSample stripOcean = GetSailingOceanSample(worldX);
            SpriteRenderer crest = waveStrips[i];
            SetEnabled(crest, true);
            TideV43WaveKind v43Kind = ResolveV43WaveKind(waveEvent.Kind);
            Sprite v43Frame = useV43Waves
                ? TideV43SeaWeatherPresentationModel.EvaluateWaveFrame(
                    formalV43SeaWeatherCatalog,
                    v43Kind,
                    time,
                    waveEvent.FramePhase01,
                    waveEvent.FrameSpeedScale)
                : null;
            bool useV43Crest = v43Frame != null;
            crest.sprite = useV43Crest ? v43Frame : useFormalCrests ? formalCrest : GetFoamSprite();
            crest.sortingOrder = useV43Crest || useFormalCrests
                ? naturalWaterSurfaceRenderer.sortingOrder + 1
                : -17;
            float y = stripOcean.SurfaceY + (useV43Crest ? 0f : useFormalCrests ? 0.025f : -0.3f);
            Vector2 v43Size = Vector2.Scale(
                TideV43SeaWeatherPresentationModel.GetWaveWorldSize(v43Kind),
                new Vector2(waveEvent.WidthScale, waveEvent.HeightScale));
            Color waveTint = useV43Crest
                ? Color.Lerp(
                    new Color(0.88f, 0.98f, 0.95f, 0.46f + tideStrength * 0.16f),
                    new Color(0.68f, 0.79f, 0.81f, 0.64f),
                    storm01 * 0.58f)
                : new Color(
                    0.76f,
                    0.96f,
                    0.9f,
                    0.3f + tideStrength * 0.24f + storm01 * 0.14f);
            waveTint.a *= waveEvent.Opacity01;
            SetWorldSize(
                crest,
                new Vector2(x, y),
                useV43Crest
                    ? v43Size
                    : useFormalCrests
                    ? new Vector2(1.82f + storm01 * 0.32f, 0.2f + storm01 * 0.07f)
                    : new Vector2(1.82f + storm01 * 0.26f, 0.17f + storm01 * 0.06f),
                waveTint,
                Mathf.Atan(stripOcean.Slope) * Mathf.Rad2Deg *
                    (useV43Crest ? 0.58f : useFormalCrests ? 0.42f : 0.18f));
        }

        UpdateSailingFlowCrestVisuals(time, daylight01);
        UpdateSailingDestinationVisuals(time);

        // Show the breaker whenever it is the nearer active constraint. The vortex may
        // still exist farther out, but it must not hide the immediate consequence of a
        // leaking hull and torn sail.
        bool showRangeBreaker = IsBoatConditionLimitingRange();
        SetEnabled(sailingRangeBreakerRenderer, showRangeBreaker);
        if (showRangeBreaker)
        {
            sailingRangeBreakerRenderer.sprite = GetFormalStiltWaveImpactSprite();
            float breakerPulse = Mathf.Sin(time * 1.7f) * 0.04f;
            SetWorldSize(
                sailingRangeBreakerRenderer,
                GetSailingScreenPosition(new Vector2(GetBoatSeaworthyRightLimit() + 0.34f, sailingHomeY + 0.18f + breakerPulse)),
                new Vector2(1.45f + breakerPulse, 0.5f + breakerPulse * 0.35f),
                new Color(0.78f, 0.94f, 0.92f, 0.34f + (1f - GetBoatReadiness01()) * 0.18f),
                0f);
        }

        // The destination renderer owns the wreck. The old decorative rib group
        // would otherwise draw a second wreck on top of the boat and interaction point.
        SetEnabled(shipwreckRibs, false);

        for (int i = 0; i < waterWracks.Count; i++)
        {
            if (useFormalWater)
            {
                waterWracks[i].enabled = false;
                continue;
            }

            float x = -4.8f + i * 1.82f + Mathf.Sin(time * 0.23f + i) * 0.12f;
            float worldX = x + GetSailingCameraWorldX();
            TideOceanSample wrackOcean = GetSailingOceanSample(worldX);
            Set(waterWracks[i], new Vector2(x, wrackOcean.SurfaceY - 0.03f), new Vector2(0.22f, 0.07f), new Color(0.58f, 0.42f, 0.24f, 0.42f), Mathf.Atan(wrackOcean.Slope) * Mathf.Rad2Deg * 0.5f);
        }

        TideOceanSample vortexOcean = GetSailingOceanSample(routeVortexX);
        Vector2 vortexScreenPosition = GetSailingScreenPosition(
            new Vector2(routeVortexX, vortexOcean.SurfaceY));
        bool vortexOnScreen = vortexScreenPosition.x >= -7.2f && vortexScreenPosition.x <= 7.2f;
        bool showVortex = IsVortexBlockingRoute() && vortexOnScreen;
        UpdateSideViewVortexVisuals(
            time,
            showVortex,
            vortexScreenPosition,
            vortexOcean,
            storm01,
            daylight01);

        float speed01 = Mathf.Clamp(sailingBoatVelocity / Mathf.Max(0.01f, GetEffectiveSailingMaxSpeed()), -1f, 1f);
        float unready01 = 1f - GetBoatReadiness01();
        float handlingWobble = Mathf.Sin(time * 3.1f) *
            (unready01 * 0.8f + sailingWaterIngress01 * 1.15f);
        TideOceanSample sailingOcean = GetSailingOceanSample(sailingBoatX);
        float boatTilt = sailingDynamics.PitchDegrees + handlingWobble;
        Vector2 oldBoatScreenPosition = GetSailingScreenPosition(GetSailingBoatBasePosition());
        float resolvedHeaveY = Mathf.Approximately(sailingDynamics.HeaveY, 0f)
            ? sailingOcean.SurfaceY
            : sailingDynamics.HeaveY;
        Vector2 boatPosition = new Vector2(
            oldBoatScreenPosition.x,
            resolvedHeaveY - sailingWaterIngress01 * 0.1f);
        bool sailVisuallyReefed = sailingSailTrim01 < 0.34f;
        Sprite formalBoatVisual;
        bool sailStillTorn = boatSailIntegrity <= 0;
        if (sailStillTorn)
        {
            formalBoatVisual = sailVisuallyReefed
                ? GetFormalDamagedReefedBoatSprite() ?? GetFormalDamagedBoatSprite() ?? GetFormalBoatSprite()
                : GetFormalDamagedBoatSprite() ?? GetFormalBoatSprite();
        }
        else
        {
            formalBoatVisual = sailVisuallyReefed
                ? GetFormalReefedBoatSprite() ?? GetFormalBoatSprite()
                : GetFormalBoatSprite();
        }

        bool sailingLoaded = sailingRewardPending ||
            extraSaltWoodOwner == ExtraSaltWoodOwner.HookingToBoat ||
            extraSaltWoodOwner == ExtraSaltWoodOwner.HookedToBoat;
        Color boatTint = Color.Lerp(
            new Color(0.78f, 0.84f, 0.84f, 1f),
            Color.white,
            daylight01);
        bool useContractBoat = ApplyBoatPresentation(
            boatPosition,
            boatTilt,
            time,
            true,
            sailingLoaded,
            storm01 >= 0.58f,
            boatTint,
            out Vector2 contractBoatRoot);
        bool useFormalBoat = useContractBoat || formalBoatVisual != null;
        // 正式船用船体起伏、尾流和帆态表达可控性。旧金色光圈只服务程序占位
        // 船，不能在航行时黏在桅杆上成为脱离世界的目标标记。
        SetEnabled(boatGlowRenderer, !useFormalBoat);
        if (!useFormalBoat)
        {
            SetWorldSize(
                boatGlowRenderer,
                boatPosition + new Vector2(0.12f, 0.05f),
                new Vector2(1.72f, 0.42f),
                new Color(0.78f, 0.7f, 0.46f, CanReturnFromSailing() ? 0.34f : 0.2f),
                0f);
        }
        if (!useContractBoat && formalBoatVisual != null)
        {
            SetEnabled(boatBackRigRenderer, false);
            boatHullRenderer.sprite = formalBoatVisual;
            SetWorldSize(
                boatHullRenderer,
                boatPosition + new Vector2(0f, 0.74f),
                GetAspectPreservingWorldSize(formalBoatVisual, 2.25f),
                boatTint,
                boatTilt);
            SetEnabled(boatCockpitRenderer, false);
            SetEnabled(boatSailRenderer, false);
            SetEnabled(boatPassengerGunwaleRenderer, false);
            SetEnabled(boatRudderRenderer, false);
            SetEnabled(boatRigging, false);
        }
        else if (!useContractBoat)
        {
            SetEnabled(boatBackRigRenderer, false);
            SetWorldSize(boatHullRenderer, boatPosition, new Vector2(1.65f + boatReadiness * 0.06f, 0.52f), new Color(0.72f, 0.45f, 0.27f, 1f), boatTilt);
            Set(boatCockpitRenderer, boatPosition + new Vector2(-0.18f, 0.12f), new Vector2(0.64f, 0.32f), new Color(0.12f, 0.07f, 0.04f, 0.98f), 0f);
            SetWorldSize(boatSailRenderer, boatPosition + new Vector2(0.33f, 0.62f), new Vector2(0.92f, 1.12f), new Color(0.94f, 0.88f, 0.7f, 0.98f), -3f + boatTilt * 0.35f);
        }
        bool wakeVisible = Mathf.Abs(speed01) >= 0.08f;
        SetEnabled(boatWakeRenderer, wakeVisible);
        if (wakeVisible)
        {
            SetWorldSize(
                boatWakeRenderer,
                boatPosition + new Vector2(-0.08f - speed01 * 0.16f, -0.11f),
                new Vector2(
                    0.62f + Mathf.Abs(speed01) * 1.08f,
                    0.12f + sailingOcean.Agitation01 * 0.07f),
                new Color(
                    0.68f,
                    0.95f,
                    0.9f,
                    0.1f + Mathf.Abs(speed01) * 0.3f),
                sailingOcean.Slope * 5f);
        }
        SetEnabled(boatWaterlineOcclusionRenderer, false);
        SetEnabled(lighthouseChartClueRenderer, sailingRewardPending);
        if (sailingRewardPending)
        {
            lighthouseChartClueRenderer.sprite = GetLighthouseChartClueSprite();
            SetWorldSize(
                lighthouseChartClueRenderer,
                boatPosition + new Vector2(-0.12f, 0.34f),
                new Vector2(0.25f, 0.18f),
                new Color(1f, 1f, 1f, 0.94f),
                -8f + boatTilt * 0.12f);
        }
        bool hookInFlight = sailingHookThrowActive &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater;
        bool timberOnTowline = extraSaltWoodOwner == ExtraSaltWoodOwner.HookingToBoat ||
            extraSaltWoodOwner == ExtraSaltWoodOwner.HookedToBoat;
        bool showSalvageLine = hookInFlight || timberOnTowline;
        SetEnabled(sailingSalvageHookRopeRenderer, showSalvageLine);
        SetEnabled(sailingSalvageHookRopeEndRenderer, showSalvageLine);
        SetEnabled(sailingSalvageHookRenderer, showSalvageLine);
        if (showSalvageLine)
        {
            // Draw from the same authored-hull stern used by interaction and physics.
            // Keeping a second visual-only offset here previously made the hook test pass
            // while the rope visibly started from the middle of the boat.
            Vector2 sternPosition = GetSailingScreenPosition(GetSailingBoatSternWorldPosition());
            Vector2 hookPosition = hookInFlight
                ? GetSailingScreenPosition(sailingHookWorldPosition)
                : GetSailingScreenPosition(GetSailingPointPosition(SailingPointKind.Salvage)) + new Vector2(0.03f, 0.04f);
            Vector2 ropeDirection = hookPosition - sternPosition;
            float ropeLength = ropeDirection.magnitude;
            float tension01 = hookInFlight
                ? Mathf.Lerp(0.12f, 0.52f, sailingHookThrow01)
                : sailingSalvageTension01;
            float sag = Mathf.Lerp(0.16f, 0.018f, tension01) * Mathf.Clamp01(ropeLength / 1.4f);
            Vector2 ropeMidpoint = (sternPosition + hookPosition) * 0.5f + Vector2.down * sag;
            Color wetRopeColor = Color.Lerp(
                new Color(0.42f, 0.37f, 0.27f, 0.94f),
                new Color(0.88f, 0.82f, 0.68f, 0.98f),
                tension01);
            float ropeThickness = Mathf.Lerp(0.038f, 0.055f, tension01);
            SetSailingSalvageRopeSegment(
                sailingSalvageHookRopeRenderer,
                sternPosition,
                ropeMidpoint,
                ropeThickness,
                wetRopeColor);
            SetSailingSalvageRopeSegment(
                sailingSalvageHookRopeEndRenderer,
                ropeMidpoint,
                hookPosition,
                ropeThickness,
                wetRopeColor);

            sailingSalvageHookRenderer.sprite = GetSeaSalvageHookSprite();
            Vector2 hookSize = GetAspectPreservingWorldSize(sailingSalvageHookRenderer.sprite, 0.23f);
            float ropeAngle = Mathf.Atan2(ropeDirection.y, ropeDirection.x) * Mathf.Rad2Deg;
            SetWorldSize(
                sailingSalvageHookRenderer,
                hookPosition,
                hookSize,
                Color.Lerp(new Color(0.58f, 0.62f, 0.59f, 1f), Color.white, tension01 * 0.3f),
                ropeAngle - 90f);

            if (timberOnTowline)
            {
                Vector2 salvageDrawPosition = GetSailingScreenPosition(GetSailingPointPosition(SailingPointKind.Salvage));
                sailingSalvagePointRenderer.sprite = GetFormalSprite(ref formalRepairTimberSprite, "AIRepairTimber") ?? GetWaterWrackSprite();
                SetWorldSize(
                    sailingSalvagePointRenderer,
                    salvageDrawPosition,
                    new Vector2(0.94f, 0.36f),
                    Color.white,
                    -3f + Mathf.Sin(time * 1.05f) * 1.4f);

                float towMotion = Mathf.Abs(sailingSalvageVelocity) + sailingSalvageTension01 * 0.45f;
                bool towWakeVisible = towMotion > 0.03f;
                SetEnabled(sailingSalvageWakeRenderer, towWakeVisible);
                if (towWakeVisible)
                {
                    float wakeDirection = Mathf.Abs(sailingSalvageVelocity) > 0.02f
                        ? Mathf.Sign(sailingSalvageVelocity)
                        : Mathf.Sign(sailingBoatWorldVelocity);
                    float wake01 = Mathf.InverseLerp(0.03f, 0.9f, towMotion);
                    sailingSalvageWakeRenderer.sprite = GetSwimWakeSprite();
                    SetWorldSize(
                        sailingSalvageWakeRenderer,
                        salvageDrawPosition + new Vector2(-wakeDirection * 0.34f, -0.09f),
                        new Vector2(0.4f + wake01 * 0.62f, 0.1f + wake01 * 0.04f),
                        new Color(0.74f, 0.94f, 0.91f, 0.22f + wake01 * 0.3f),
                        0f);
                    sailingSalvageWakeRenderer.flipX = wakeDirection < 0f;
                }
            }
        }
        for (int i = 0; i < boatRigging.Count && !useFormalBoat; i++)
        {
            Vector2 offset = i == 0 ? new Vector2(0.03f, 0.52f) : i == 1 ? new Vector2(0.42f, 0.38f) : new Vector2(-0.18f, 0.24f);
            Vector2 scale = i == 0 ? new Vector2(0.68f, 0.05f) : new Vector2(0.5f, 0.045f);
            float rotation = i == 0 ? 57f : i == 1 ? -42f : 12f;
            Set(boatRigging[i], boatPosition + offset, scale, new Color(0.65f, 0.68f, 0.55f, 0.86f), rotation);
        }

        SetEnabled(boatIngressWaterRenderer, sailingWaterIngress01 > 0.025f);
        if (boatIngressWaterRenderer.enabled)
        {
            boatIngressWaterRenderer.sprite = GetFormalStiltWaveImpactSprite();
            float ingressPulse = Mathf.Sin(time * 4.4f) * 0.018f;
            SetWorldSize(
                boatIngressWaterRenderer,
                boatPosition + new Vector2(-0.18f, 0.18f + ingressPulse),
                new Vector2(0.48f + sailingWaterIngress01 * 0.38f, 0.1f + sailingWaterIngress01 * 0.12f),
                new Color(0.66f, 0.9f, 0.9f, 0.2f + sailingWaterIngress01 * 0.62f),
                boatTilt * 0.18f);
        }

        SetEnabled(sailingBailBucketRenderer, sailingBailing);
        if (sailingBailing)
        {
            float bailPhase01 = Mathf.Repeat(sailingBailCycle, 1f);
            Vector2 bucketOffset;
            float bucketRotation;
            if (bailPhase01 < 0.32f)
            {
                float reach01 = Mathf.SmoothStep(0f, 1f, bailPhase01 / 0.32f);
                bucketOffset = Vector2.Lerp(
                    new Vector2(-0.95f, 0.72f),
                    new Vector2(-1.08f, 0.47f),
                    reach01);
                bucketRotation = Mathf.Lerp(-12f, -28f, reach01);
            }
            else if (bailPhase01 < 0.62f)
            {
                float lift01 = Mathf.SmoothStep(0f, 1f, (bailPhase01 - 0.32f) / 0.3f);
                bucketOffset = Vector2.Lerp(
                    new Vector2(-1.08f, 0.47f),
                    new Vector2(-0.95f, 0.79f),
                    lift01);
                bucketRotation = Mathf.Lerp(-28f, 8f, lift01);
            }
            else if (bailPhase01 < 0.86f)
            {
                float throw01 = Mathf.SmoothStep(0f, 1f, (bailPhase01 - 0.62f) / 0.24f);
                bucketOffset = Vector2.Lerp(
                    new Vector2(-0.95f, 0.79f),
                    new Vector2(-1.35f, 0.18f),
                    throw01);
                bucketRotation = Mathf.Lerp(8f, 52f, throw01);
            }
            else
            {
                float return01 = Mathf.SmoothStep(0f, 1f, (bailPhase01 - 0.86f) / 0.14f);
                bucketOffset = Vector2.Lerp(
                    new Vector2(-1.35f, 0.18f),
                    new Vector2(-0.95f, 0.72f),
                    return01);
                bucketRotation = Mathf.Lerp(52f, -12f, return01);
            }

            // 室内准备桶带固定滑轮，缩到船上会变成一团无法辨认的挂墙结构。
            // V37 使用同材质但可单手提起的小桶，仍由独立 Renderer 跨船舷排序。
            sailingBailBucketRenderer.sprite = formalV37BoatCharacterCatalog != null
                ? formalV37BoatCharacterCatalog.BailingBucketSprite
                : GetPrepBucketSprite();
            // 舱内阶段位于舱底维修层之后、船舷之前；外泼阶段必须越过最前方的
            // V52 破口维修层，但仍被前景深水遮住，不能像贴纸一样浮在海面上。
            sailingBailBucketRenderer.sortingOrder = bailPhase01 < 0.62f ? 6 : 10;
            sailingBailSplashRenderer.sortingOrder = 10;
            SetWorldSize(
                sailingBailBucketRenderer,
                boatPosition + bucketOffset,
                new Vector2(0.32f, 0.32f),
                Color.white,
                bucketRotation + boatTilt * 0.2f);

            bool splashVisible = bailPhase01 >= 0.62f && bailPhase01 <= 0.94f;
            SetEnabled(sailingBailSplashRenderer, splashVisible);
            if (splashVisible)
            {
                sailingBailSplashRenderer.sprite = GetFormalStiltWaveImpactSprite();
                float splash01 = Mathf.InverseLerp(0.62f, 0.94f, bailPhase01);
                SetWorldSize(
                    sailingBailSplashRenderer,
                    boatPosition + new Vector2(-1.4f - splash01 * 0.18f, -0.04f + splash01 * 0.1f),
                    new Vector2(0.42f + splash01 * 0.24f, 0.18f + splash01 * 0.06f),
                    new Color(0.72f, 0.94f, 0.92f, 0.24f + splash01 * 0.34f),
                    -16f + splash01 * 20f);
            }
        }
        else
        {
            SetEnabled(sailingBailSplashRenderer, false);
        }

        for (int i = 0; i < boatPatchStitches.Count; i++)
        {
            // 正式 V21/V31 endpoint 已经拥有完整的破损/修复外观。旧原型木片只服务
            // 程序生成船体，否则会像五块灰色菱形一样浮在正式船壳表面。
            bool visiblePatch = !useFormalBoat && i < boatReadiness;
            SetEnabled(boatPatchStitches[i], visiblePatch);
            if (!visiblePatch)
            {
                continue;
            }

            boatPatchStitches[i].sprite = GetFormalSprite(ref formalRepairTimberSprite, "AIRepairTimber") ?? GetBoatSailPatchStitchSprite();
            Vector2 patchOffset = new Vector2(-0.3f + (i % 3) * 0.21f, -0.01f + (i % 2) * 0.07f);
            SetWorldSize(boatPatchStitches[i], boatPosition + patchOffset, new Vector2(0.27f, 0.105f), Color.white, -6f + i * 3f + boatTilt * 0.15f);
        }

        SetEnabled(playerRenderer, false);
        SetEnabled(playerAquaticRenderer, false);
        // 航海视图隐藏岸上人物时必须同时关闭旧人物光环，否则它会留在世界
        // 原点并视觉上黏到船桅附近，形成与真实场景无关的金色圆环。
        SetEnabled(playerGlowRenderer, false);
        SetMaskEnabled(playerBagMask, false);
        if (useContractBoat)
        {
            // V39（缺资源时为 V31）已由 BoatRoot 契约放好后船体、完整乘员和前船舷；V37 只替换
            // 人物动作，桶与水花仍是独立道具层，不能在角色帧里重复烘焙。
            SetMaskEnabled(boatPassengerBagMask, false);
        }
        else
        {
            Sprite seatedPassenger = GetFormalBoatPassengerSprite();
            bool useSeatedPassenger = seatedPassenger != null;
            Vector2 passengerPosition = boatPosition +
                GetBoatPassengerOffset(false, useSeatedPassenger);
            boatPassengerRenderer.sprite = useSeatedPassenger
                ? seatedPassenger
                : GetPlayerSprite();
            boatPassengerRenderer.sortingOrder = 8;
            Vector2 passengerWorldSize = GetAspectPreservingWorldSize(
                boatPassengerRenderer.sprite,
                useSeatedPassenger ? 1f : 1.16f);
            SetWorldSize(
                boatPassengerRenderer,
                passengerPosition,
                passengerWorldSize,
                useSeatedPassenger ? Color.white : GetSurvivorClothingTint(daylight01),
                -boatTilt * 0.25f);
            boatPassengerRenderer.flipX = playerFacing < 0;
            UpdatePlayerBagStoryMask(
                boatPassengerRenderer,
                boatPassengerBagMask,
                passengerPosition,
                useSeatedPassenger,
                false,
                false,
                sailingBailing);
        }

        UpdateSkyVisuals(time, storm01);
        // Sailing shares the authored sky/sea palette with the shelter view. A second
        // full-screen cyan plate made the scene darker and created a colour mismatch
        // at the camera hand-off, so normal sailing does not own an extra wash.
    }

    private static Vector2 GetAspectPreservingWorldSize(Sprite sprite, float targetHeight)
    {
        float safeHeight = Mathf.Max(0.01f, targetHeight);
        if (sprite == null)
        {
            return new Vector2(safeHeight * 0.55f, safeHeight);
        }

        Vector2 sourceSize = sprite.bounds.size;
        float aspect = sourceSize.x / Mathf.Max(0.001f, sourceSize.y);
        return new Vector2(safeHeight * aspect, safeHeight);
    }

    private void SetSailingSalvageRopeSegment(
        SpriteRenderer renderer,
        Vector2 start,
        Vector2 end,
        float thickness,
        Color color)
    {
        Vector2 direction = end - start;
        renderer.sprite = GetTowRopeSprite();
        SetWorldSize(
            renderer,
            (start + end) * 0.5f,
            new Vector2(Mathf.Max(0.035f, direction.magnitude), thickness),
            color,
            Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
    }

    private static Vector2 GetBoatPassengerOffset(bool bailing, bool seated)
    {
        if (bailing)
        {
            return new Vector2(-0.5f, 0.31f);
        }

        if (seated)
        {
            // The lower quarter of the authored seated pose falls behind the hull.
            // This keeps the hood and working hands clear while the knees and boots
            // disappear naturally into the open cockpit.
            return new Vector2(-0.52f, 0.31f);
        }

        return new Vector2(-0.28f, 0.38f);
    }

    private void UpdateHouseDetailVisuals(float time)
    {
        if (GetFormalHouseSprite() != null)
        {
            SetEnabled(houseSaltStreaks, false);
            return;
        }

        for (int i = 0; i < houseSaltStreaks.Count; i++)
        {
            float x = houseAnchor.x - 1.0f + i * 0.38f;
            float y = houseAnchor.y + 0.46f - (i % 3) * 0.12f;
            float sway = Mathf.Sin(time * 0.35f + i) * 0.01f;
            Color color = new Color(0.72f, 0.78f, 0.64f, 0.34f + i * 0.025f);
            Set(houseSaltStreaks[i], new Vector2(x, y + sway), new Vector2(0.12f, 0.48f + (i % 2) * 0.18f), color, -2f + i * 0.6f);
        }
    }

    private float GetShelterRestoration01()
    {
        float foundation01 = Mathf.Clamp01((stiltIntegrity - 1f + GetUncommittedRepairPreview01(RepairChoice.Stilt)) / 2f);
        float roof01 = Mathf.Clamp01((roofIntegrity + GetUncommittedRepairPreview01(RepairChoice.Roof)) / 2f);
        float room01 = Mathf.Clamp01((interiorComfort +
            GetUncommittedRepairPreview01(RepairChoice.InteriorSeal) +
            GetUncommittedRepairPreview01(RepairChoice.Bed)) / 2f);
        float stove01 = Mathf.Clamp01((stoveCondition + GetUncommittedRepairPreview01(RepairChoice.Lamp)) / 2f);
        return (foundation01 + roof01 + room01 + stove01) * 0.25f;
    }

    private Color GetSurvivorClothingTint(float daylight01)
    {
        // The survivor does not receive a magically new outfit. Early on the same
        // salt-stained rain gear is slightly cooler while soaked; a repaired room and
        // stove let it dry. Keep the multiplier close to white so the authored ochre
        // remains the stable player-reading color against blue-gray sea and sky.
        float dry01 = Mathf.Clamp01((interiorComfort + stoveCondition) / 4f);
        Color lightlySoakedTint = new Color(0.88f, 0.94f, 0.93f, 1f);
        Color tint = Color.Lerp(lightlySoakedTint, Color.white, 0.72f + dry01 * 0.28f);
        return Color.Lerp(tint, new Color(0.92f, 0.96f, 1f, 1f), (1f - daylight01) * 0.06f);
    }

    private void UpdateShelterStoryStateVisuals(Vector2 shelterShakeOffset, float shelterRock)
    {
        if (HasRegisteredHousePresentation())
        {
            // The old wide house used world-space black cutouts and rectangular
            // repair strips.  Their coordinates do not belong to the registered
            // V26/V28 silhouette and otherwise appear as holes and bars in the sky.
            houseRenderer.maskInteraction = SpriteMaskInteraction.None;
            SetMaskEnabled(houseRoofHoleMasks, false);
            SetMaskEnabled(houseWindowHoleMasks, false);
            SetMaskEnabled(houseWallGapMasks, false);
            SetEnabled(houseRoofRepairMarks, false);
            SetEnabled(houseWallRepairMarks, false);
            SetEnabled(houseInteriorVoidRenderers, false);
            return;
        }

        bool useFormalHouse = GetFormalHouseSprite() != null && state != SliceState.FinalDeparture;
        if (!useFormalHouse)
        {
            houseRenderer.maskInteraction = SpriteMaskInteraction.None;
            SetMaskEnabled(houseRoofHoleMasks, false);
            SetMaskEnabled(houseWindowHoleMasks, false);
            SetMaskEnabled(houseWallGapMasks, false);
            SetEnabled(houseRoofRepairMarks, false);
            SetEnabled(houseWallRepairMarks, false);
            SetEnabled(houseInteriorVoidRenderers, false);
            return;
        }

        Vector2[] roofHolePositions =
        {
            new Vector2(-5.22f, 1.2f),
            new Vector2(-2.86f, 1.38f),
            new Vector2(-0.48f, 1.18f)
        };
        Vector2[] roofHoleSizes =
        {
            new Vector2(0.68f, 0.18f),
            new Vector2(0.62f, 0.2f),
            new Vector2(0.66f, 0.18f)
        };
        float[] roofAngles = { -4f, 2f, -3f };
        int remainingRoofHoles = roofIntegrity <= 0 ? 3 : roofIntegrity == 1 ? 1 : 0;
        for (int i = 0; i < houseRoofHoleMasks.Count; i++)
        {
            bool visible = i < remainingRoofHoles;
            SetMaskEnabled(houseRoofHoleMasks[i], visible);
            if (visible)
            {
                SetMaskWorldSize(
                    houseRoofHoleMasks[i],
                    roofHolePositions[i] + shelterShakeOffset,
                    roofHoleSizes[i],
                    roofAngles[i] + shelterRock);
            }
        }

        Vector2[] windowPositions =
        {
            new Vector2(-3.9f, 0.17f),
            new Vector2(-1.51f, 0.17f)
        };
        for (int i = 0; i < houseWindowHoleMasks.Count; i++)
        {
            bool visible = stoveCondition <= i;
            SetMaskEnabled(houseWindowHoleMasks[i], visible);
            if (visible)
            {
                SetMaskWorldSize(
                    houseWindowHoleMasks[i],
                    windowPositions[i] + shelterShakeOffset,
                    new Vector2(0.48f, 0.4f),
                    shelterRock);
            }

            SpriteRenderer voidRenderer = houseInteriorVoidRenderers[i];
            SetEnabled(voidRenderer, visible);
            if (visible)
            {
                voidRenderer.sprite = GetStoryRaggedMaskSprite();
                SetWorldSize(
                    voidRenderer,
                    windowPositions[i] + shelterShakeOffset,
                    new Vector2(0.5f, 0.42f),
                    new Color(0.018f, 0.032f, 0.028f, 0.96f),
                    shelterRock);
            }
        }

        Vector2[] wallGapPositions =
        {
            new Vector2(-4.83f, -0.1f),
            new Vector2(-2.72f, -0.2f)
        };
        Vector2[] wallGapSizes =
        {
            new Vector2(0.52f, 0.18f),
            new Vector2(0.58f, 0.2f)
        };
        int remainingWallGaps = interiorComfort <= 0 ? 2 : interiorComfort == 1 ? 1 : 0;
        for (int i = 0; i < houseWallGapMasks.Count; i++)
        {
            bool visible = i < remainingWallGaps;
            SetMaskEnabled(houseWallGapMasks[i], visible);
            if (visible)
            {
                SetMaskWorldSize(
                    houseWallGapMasks[i],
                    wallGapPositions[i] + shelterShakeOffset,
                    wallGapSizes[i],
                    (i == 0 ? -3f : 2f) + shelterRock);
            }

            SpriteRenderer voidRenderer = houseInteriorVoidRenderers[i + 2];
            bool needsDarkInterior = visible;
            SetEnabled(voidRenderer, needsDarkInterior);
            if (needsDarkInterior)
            {
                voidRenderer.sprite = GetStoryRaggedMaskSprite();
                SetWorldSize(
                    voidRenderer,
                    wallGapPositions[i] + shelterShakeOffset,
                    wallGapSizes[i] + new Vector2(0.04f, 0.03f),
                    new Color(0.025f, 0.04f, 0.034f, 0.98f),
                    (i == 0 ? -3f : 2f) + shelterRock);
            }
        }

        bool hasOpenStoryCutout = remainingRoofHoles > 0 || remainingWallGaps > 0 || stoveCondition < 2;
        houseRenderer.maskInteraction = hasOpenStoryCutout
            ? SpriteMaskInteraction.VisibleOutsideMask
            : SpriteMaskInteraction.None;

        for (int i = 0; i < houseRoofRepairMarks.Count; i++)
        {
            bool openHole = i < remainingRoofHoles;
            bool patchedHole = i >= remainingRoofHoles;
            bool visible = openHole || patchedHole;
            SetEnabled(houseRoofRepairMarks[i], visible);
            if (visible)
            {
                houseRoofRepairMarks[i].sprite = GetBrokenHousePieceSprite();
                if (openHole)
                {
                    // A hole still has torn corrugated lips and a visible material
                    // edge; drawing only transparent sky made it look digitally erased.
                    SetWorldSize(
                        houseRoofRepairMarks[i],
                        roofHolePositions[i] + shelterShakeOffset + new Vector2(0.02f, -0.09f),
                        new Vector2(roofHoleSizes[i].x * 0.92f, 0.075f),
                        new Color(0.4f, 0.17f, 0.1f, 0.96f),
                        roofAngles[i] + (i % 2 == 0 ? -3f : 3f) + shelterRock);
                }
                else
                {
                    Vector2 patchSize = roofHoleSizes[i] + new Vector2(0.1f, -0.03f);
                    SetWorldSize(
                        houseRoofRepairMarks[i],
                        roofHolePositions[i] + shelterShakeOffset,
                        patchSize,
                        new Color(0.48f, 0.22f, 0.13f, 0.94f),
                        roofAngles[i] + shelterRock);
                }
            }
        }

        for (int i = 0; i < houseWallRepairMarks.Count; i++)
        {
            bool openGap = i < remainingWallGaps;
            bool patchedGap = i >= remainingWallGaps;
            SetEnabled(houseWallRepairMarks[i], openGap || patchedGap);
            if (openGap)
            {
                houseWallRepairMarks[i].sprite = GetBrokenHousePieceSprite();
                SetWorldSize(
                    houseWallRepairMarks[i],
                    wallGapPositions[i] + shelterShakeOffset + new Vector2(0f, -0.08f),
                    new Vector2(wallGapSizes[i].x * 0.86f, 0.07f),
                    new Color(0.3f, 0.19f, 0.12f, 0.96f),
                    (i == 0 ? -6f : 5f) + shelterRock);
            }
            else if (patchedGap)
            {
                houseWallRepairMarks[i].sprite = GetBrokenHousePieceSprite();
                SetWorldSize(
                    houseWallRepairMarks[i],
                    wallGapPositions[i] + shelterShakeOffset,
                    new Vector2(0.52f, 0.12f),
                    new Color(0.53f, 0.34f, 0.2f, 0.96f),
                    (i == 0 ? 2f : -2f) + shelterRock);
            }
        }
    }

    // 纯视觉建筑层：强化“有人住过的高脚屋”轮廓，不参与行走、碰撞或潮水判定。
    private void UpdateStiltHouseArchitectureVisuals()
    {
        SetWorldSize(underHouseShadowRenderer, houseAnchor + new Vector2(0f, -0.82f), new Vector2(4.45f, 0.66f), new Color(0.02f, 0.035f, 0.03f, 0.48f), 0f);
        SetEnabled(longEaveRenderer, false);
        SetEnabled(houseGableRoofs, false);
        SetEnabled(denseStiltPosts, false);
    }

    private void UpdateFinalDepartureVisuals(float time)
    {
        bool visible = state == SliceState.FinalDeparture;
        float shelterStressLineY = GetShelterStressWaterlineY();
        bool exceptionalHouseFlood = !visible && currentWaterY > shelterStressLineY + 0.02f;
        bool showProceduralSurge = visible && !HasHdFormalWater();
        SetEnabled(stormSurgeWallRenderer, showProceduralSurge || exceptionalHouseFlood);
        bool useFormalHouse = GetFormalHouseSprite() != null;
        bool showProceduralBreakage = visible && !useFormalHouse;
        SetEnabled(brokenHousePieces, visible);
        SetEnabled(snappedStiltPosts, visible);
        SetEnabled(departureRouteWakes, visible);
        if (exceptionalHouseFlood)
        {
            // Ordinary water remains behind the stilts. Only a genuinely exceptional
            // surge gets a foreground slice, so water that has reached the rooms also
            // visually occludes the lower house instead of living on another layer.
            Sprite floodSprite = GetFormalWaterSurfaceSprite() != null
                ? GetFormalWaterSurfaceSprite()
                : GetStormSurgeWallSprite();
            stormSurgeWallRenderer.sprite = floodSprite;
            float floodDepth = Mathf.Clamp(currentWaterY - shelterStressLineY, 0.18f, 2.25f);
            float bodyHeight = 1.35f + floodDepth;
            SetWorldSize(
                stormSurgeWallRenderer,
                new Vector2(houseAnchor.x + 0.22f, currentWaterY - bodyHeight * 0.5f + 0.22f),
                new Vector2(7.15f, bodyHeight),
                new Color(0.82f, 0.94f, 0.92f, 0.78f),
                Mathf.Sin(time * 0.46f) * 0.2f);
        }

        if (!visible)
        {
            return;
        }

        float depart01 = Mathf.Clamp01(stateTimer / Mathf.Max(0.1f, finalDepartureSeconds));
        float pulse = Mathf.Sin(time * 2.4f) * 0.5f + 0.5f;
        float surgeY = Mathf.Lerp(lowWaterY + 0.35f, houseAnchor.y - 0.18f, depart01);
        Color surgeColor = Color.Lerp(
            new Color(0.08f, 0.38f, 0.43f, 0.46f),
            new Color(0.38f, 0.09f, 0.07f, 0.72f),
            depart01);
        if (showProceduralSurge)
        {
            SetWorldSize(
                stormSurgeWallRenderer,
                new Vector2(houseAnchor.x + 0.32f, surgeY),
                new Vector2(7.1f, 1.25f + depart01 * 2.15f),
                surgeColor,
                Mathf.Sin(time * 0.9f) * 1.8f);
        }

        float impact01 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.52f, depart01));
        float break01 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.24f, 0.84f, depart01));
        float leave01 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.76f, 1f, depart01));
        if (useFormalHouse)
        {
            Vector2 housePivot = GetFormalHouseWorldPosition();
            Vector2 houseOffset = new Vector2(0f, -impact01 * 0.08f - break01 * 0.28f);
            float houseTilt = -impact01 * 0.75f - break01 * 2.45f;
            float houseAlpha = Mathf.Lerp(1f, 0.78f, leave01);
            SetWorldSize(
                houseRenderer,
                housePivot + houseOffset,
                GetFormalHouseWorldSize(),
                new Color(0.82f, 0.88f, 0.86f, houseAlpha),
                houseTilt);

            // The authored house remains the visible body, but the storm opens real
            // transparent tears in that same sprite. This avoids the old effect where
            // an intact photograph-like cutout simply faded and sank as one rigid card.
            houseRenderer.maskInteraction = break01 > 0.001f
                ? SpriteMaskInteraction.VisibleOutsideMask
                : SpriteMaskInteraction.None;

            Vector2[] roofBreakPoints =
            {
                new Vector2(-5.22f, 1.2f),
                new Vector2(-2.86f, 1.38f),
                new Vector2(-0.48f, 1.18f)
            };
            for (int i = 0; i < houseRoofHoleMasks.Count; i++)
            {
                float localBreak01 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(i * 0.13f, 0.58f + i * 0.11f, break01));
                bool maskVisible = localBreak01 > 0.001f;
                SetMaskEnabled(houseRoofHoleMasks[i], maskVisible);
                if (!maskVisible)
                {
                    continue;
                }

                ConfigureDepartureHouseMask(houseRoofHoleMasks[i]);
                Vector2 point = TransformDepartureHousePoint(roofBreakPoints[i], housePivot, houseOffset, houseTilt);
                SetMaskWorldSize(
                    houseRoofHoleMasks[i],
                    point,
                    Vector2.Lerp(new Vector2(0.08f, 0.05f), new Vector2(1.02f, 0.3f), localBreak01),
                    houseTilt + (i % 2 == 0 ? -5f : 4f));
            }

            Vector2[] facadeBreakPoints =
            {
                new Vector2(-3.9f, 0.17f),
                new Vector2(-1.51f, 0.17f)
            };
            for (int i = 0; i < houseWindowHoleMasks.Count; i++)
            {
                float localBreak01 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f + i * 0.14f, 0.78f + i * 0.08f, break01));
                bool maskVisible = localBreak01 > 0.001f;
                SetMaskEnabled(houseWindowHoleMasks[i], maskVisible);
                if (!maskVisible)
                {
                    continue;
                }

                ConfigureDepartureHouseMask(houseWindowHoleMasks[i]);
                Vector2 point = TransformDepartureHousePoint(facadeBreakPoints[i], housePivot, houseOffset, houseTilt);
                SetMaskWorldSize(
                    houseWindowHoleMasks[i],
                    point,
                    Vector2.Lerp(new Vector2(0.06f, 0.06f), new Vector2(0.72f, 0.58f), localBreak01),
                    houseTilt + (i == 0 ? -3f : 3f));
            }

            Vector2[] failedPostPoints =
            {
                new Vector2(-4.7f, -1.38f),
                new Vector2(-2.4f, -1.38f)
            };
            for (int i = 0; i < houseWallGapMasks.Count; i++)
            {
                float localBreak01 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(i * 0.12f, 0.5f + i * 0.1f, break01));
                bool maskVisible = localBreak01 > 0.001f;
                SetMaskEnabled(houseWallGapMasks[i], maskVisible);
                if (!maskVisible)
                {
                    continue;
                }

                ConfigureDepartureHouseMask(houseWallGapMasks[i]);
                Vector2 point = TransformDepartureHousePoint(failedPostPoints[i], housePivot, houseOffset, houseTilt);
                SetMaskWorldSize(
                    houseWallGapMasks[i],
                    point,
                    Vector2.Lerp(new Vector2(0.08f, 0.2f), new Vector2(0.34f, 1.72f), localBreak01),
                    houseTilt + (i == 0 ? -2f : 2f));
            }
        }

        for (int i = 0; i < brokenHousePieces.Count; i++)
        {
            float localBreak01 = showProceduralBreakage
                ? break01
                : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f + i * 0.075f, 0.48f + i * 0.07f, break01));
            bool pieceVisible = visible && localBreak01 > 0.001f;
            SetEnabled(brokenHousePieces[i], pieceVisible);
            if (!pieceVisible)
            {
                continue;
            }

            float t = brokenHousePieces.Count <= 1 ? 0f : i / (brokenHousePieces.Count - 1f);
            float scatter = localBreak01;
            Vector2 start = useFormalHouse
                ? new Vector2(-5.6f + i * 0.84f, 1.15f - (i % 3) * 0.32f)
                : houseAnchor + new Vector2(-1.25f + i * 0.42f, 0.96f - (i % 3) * 0.22f);
            Vector2 drift = new Vector2((0.12f + t * 1.15f) * scatter, -0.32f * scatter - 0.28f * scatter * scatter + Mathf.Sin(time * 0.8f + i) * 0.035f);
            Color color = useFormalHouse
                ? new Color(0.42f, 0.3f, 0.2f, Mathf.Lerp(0.62f, 0.94f, scatter))
                : Color.Lerp(new Color(0.58f, 0.24f, 0.13f, 0.48f), new Color(0.66f, 0.18f, 0.1f, 0.86f), scatter);
            Vector2 pieceSize = useFormalHouse
                ? new Vector2(0.46f + (i % 2) * 0.14f, 0.12f + (i % 3) * 0.025f)
                : new Vector2(0.68f + (i % 2) * 0.12f, 0.25f);
            SetWorldSize(brokenHousePieces[i], start + drift, pieceSize, color, -18f + i * 11f + scatter * 24f);
        }

        for (int i = 0; i < snappedStiltPosts.Count; i++)
        {
            float localBreak01 = showProceduralBreakage
                ? break01
                : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f + i * 0.08f, 0.46f + i * 0.07f, break01));
            bool postVisible = visible && localBreak01 > 0.001f && (showProceduralBreakage || i < 2);
            SetEnabled(snappedStiltPosts[i], postVisible);
            if (!postVisible)
            {
                continue;
            }

            float t = snappedStiltPosts.Count <= 1 ? 0f : i / (snappedStiltPosts.Count - 1f);
            float x = useFormalHouse && i < 2
                ? (i == 0 ? -4.7f : -2.4f)
                : Mathf.Lerp(houseAnchor.x - 1.42f, houseAnchor.x + 1.34f, t);
            float lean = Mathf.Lerp(i % 2 == 0 ? -3f : 3f, i % 2 == 0 ? -34f : 31f, localBreak01);
            float sink = Mathf.Lerp(-1.38f, -1.72f, localBreak01) + Mathf.Sin(time * 1.1f + i) * 0.025f;
            Color color = new Color(0.24f, 0.18f, 0.13f, 0.82f + localBreak01 * 0.14f);
            SetWorldSize(snappedStiltPosts[i], new Vector2(x, sink), new Vector2(0.16f, 1.5f - localBreak01 * 0.3f), color, lean);
        }

        Vector2 boatPosition = boatAnchor + GetBoatTripOffset();
        for (int i = 0; i < departureRouteWakes.Count; i++)
        {
            float t = i / Mathf.Max(1f, departureRouteWakes.Count - 1f);
            Vector2 start = boatPosition + new Vector2(-0.75f - i * 0.36f, -0.16f - t * 0.04f);
            Color wakeColor = Color.Lerp(new Color(0.64f, 0.95f, 0.88f, 0.18f), new Color(1f, 0.76f, 0.36f, 0.32f), depart01);
            wakeColor.a += pulse * 0.04f;
            Set(departureRouteWakes[i], start, new Vector2(0.84f + depart01 * 0.26f, 0.12f + t * 0.035f), wakeColor, Mathf.Sin(time * 0.55f + i) * 4f);
        }
    }

    private void ConfigureDepartureHouseMask(SpriteMask mask)
    {
        mask.isCustomRangeActive = true;
        mask.frontSortingOrder = houseRenderer.sortingOrder + 1;
        mask.backSortingOrder = houseRenderer.sortingOrder - 1;
    }

    private static Vector2 TransformDepartureHousePoint(
        Vector2 point,
        Vector2 pivot,
        Vector2 translation,
        float rotationDegrees)
    {
        float radians = rotationDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        Vector2 offset = point - pivot;
        Vector2 rotated = new Vector2(
            offset.x * cos - offset.y * sin,
            offset.x * sin + offset.y * cos);
        return pivot + translation + rotated;
    }

    private void UpdateShipwreckVisuals(float time)
    {
        if (barrenIsland != null)
        {
            SetEnabled(shipwreckRibs, false);
            return;
        }

        if (HasCompleteV40ShipwreckPresentation())
        {
            // 开场只保留三件同源残骸，既能读出一条被毁的小帆船，又不会把六张
            // 资源排成图鉴。每件使用自己的静态水线贴合局部海况；退潮后则真实
            // 落在可见潮间木面上，不再复制五个相同黑色肋骨悬在木板下面。
            for (int i = 0; i < shipwreckRibs.Count; i++)
            {
                bool visible = i < V40ArrivalWreckVariants.Length;
                shipwreckRibs[i].enabled = visible;
                if (!visible)
                {
                    continue;
                }

                int variant = V40ArrivalWreckVariants[i];
                float canvasWorldSize = i == 0 ? 1.02f : i == 1 ? 0.9f : 0.74f;
                float x = arrivalWreckX + (i == 0 ? -0.58f : i == 1 ? 0.08f : 0.62f);
                TideOceanSample ocean = GetOceanSample(x);
                float drySupportY = GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.035f;
                float supportedWaterlineY = Mathf.Max(drySupportY, ocean.SurfaceY);
                bool afloat = ocean.SurfaceY > drySupportY + 0.04f;
                float waterlineOffsetY = GetV40WreckWaterlineOffsetY(variant, canvasWorldSize);
                float bob = afloat ? Mathf.Sin(time * 0.72f + i * 1.63f) * 0.018f : 0f;
                float rotation = Mathf.Atan(ocean.Slope) * Mathf.Rad2Deg * 0.48f +
                    (i == 0 ? -3.5f : i == 1 ? 2.2f : -1.4f);
                shipwreckRibs[i].sprite = GetV40ShipwreckSprite(variant);
                shipwreckRibs[i].sortingOrder = 2;
                SetWorldSize(
                    shipwreckRibs[i],
                    new Vector2(x, supportedWaterlineY - waterlineOffsetY + bob),
                    Vector2.one * canvasWorldSize,
                    new Color(0.78f, 0.8f, 0.77f, 1f),
                    rotation);
            }

            return;
        }

        bool useFormalShipwreck = GetFormalSprite(ref formalShipwreckSprite, "AIShipwreck") != null;
        for (int i = 0; i < shipwreckRibs.Count; i++)
        {
            if (useFormalShipwreck)
            {
                shipwreckRibs[i].enabled = i == 0;
                if (i == 0)
                {
                    float formalDrift = Mathf.Sin(time * 0.28f) * 0.02f;
                    float lodgedWreckY = Mathf.Max(lowWaterY + 0.55f, currentWaterY - 0.36f);
                    shipwreckRibs[i].sprite = GetShipwreckRibSprite();
                    SetWorldSize(
                        shipwreckRibs[i],
                        new Vector2(arrivalWreckX, lodgedWreckY + formalDrift),
                        new Vector2(1.08f, 0.76f),
                        Color.white,
                        0f);
                }

                continue;
            }

            float drift = Mathf.Sin(time * 0.28f + i * 1.7f) * 0.02f;
            Color color = Color.Lerp(wetWoodColor, saltWoodColor, 0.18f + i * 0.08f);
            if (i == 0)
            {
                shipwreckRibs[i].sprite = GetDeckSprite();
                SetWorldSize(shipwreckRibs[i], new Vector2(arrivalWreckX, GetPlayerLaneY(WalkLane.TideFlat) - 0.18f + drift), new Vector2(1.18f, 0.15f), color, -9f);
                continue;
            }

            shipwreckRibs[i].sprite = GetShipwreckRibSprite();
            float offsetX = (i - 2.5f) * 0.26f;
            Vector2 position = new Vector2(arrivalWreckX + offsetX, GetPlayerLaneY(WalkLane.TideFlat) + 0.02f + drift + (i % 2) * 0.05f);
            SetWorldSize(shipwreckRibs[i], position, new Vector2(0.18f, 0.7f - i * 0.025f), color, -34f + i * 12f);
        }
    }

    private void UpdateNearshoreWorkVisuals(float time)
    {
        bool windlassVisible = viewMode == SliceViewMode.Shelter &&
            state != SliceState.FinalDeparture;
        SetEnabled(tideRoutingWinchRenderer, windlassVisible);
        Vector2 windlassBase = new Vector2(
            GetShoreWorkX() + 0.42f,
            GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.01f);
        if (windlassVisible)
        {
            tideRoutingWinchRenderer.sprite = GetRoutingWindlassSprite(routingWorkActive);
            Vector2 windlassSize = GetAspectPreservingWorldSize(tideRoutingWinchRenderer.sprite, 0.66f);
            SetWorldSize(
                tideRoutingWinchRenderer,
                windlassBase,
                windlassSize,
                new Color(0.78f, 0.77f, 0.72f, 0.98f),
                0f);
        }

        bool timberStillInFork = extraSaltWoodOwner == ExtraSaltWoodOwner.PassingNearshore ||
            extraSaltWoodOwner == ExtraSaltWoodOwner.RoutedToNet;
        bool rigVisible = viewMode == SliceViewMode.Shelter &&
            state == SliceState.TideRising &&
            netDeployed &&
            timberStillInFork &&
            !netCatchResolved;
        SetEnabled(tideRoutingRopeRenderer, rigVisible);
        SetEnabled(tideRoutingBoomRenderer, rigVisible);
        SetEnabled(pillarCheckKnots, false);
        if (!rigVisible)
        {
            return;
        }

        float boom01 = Mathf.Clamp01(routingBoom01);
        float flowRock = Mathf.Sin(time * 1.35f + GetNaturalCurrentSigned01() * 0.8f) *
            Mathf.Lerp(1.4f, 4.2f, GetNaturalCurrentStrength01());
        float boomAngle = Mathf.Lerp(-3f, -24f, boom01) + flowRock;
        // The guide timber floats seaward of the net. Keeping it off the walkable
        // wet boards makes the authored path and the water-side routing rig agree.
        Vector2 openCenter = new Vector2(netAnchor.x + 0.78f, currentWaterY - 0.08f);
        Vector2 feedCenter = new Vector2(
            netAnchor.x + 0.34f,
            Mathf.Lerp(currentWaterY - 0.08f, GetActiveNetVisualY() + 0.08f, 0.55f));
        Vector2 boomCenter = Vector2.Lerp(openCenter, feedCenter, Mathf.SmoothStep(0f, 1f, boom01));
        boomCenter.y += Mathf.Sin(time * 1.8f) * 0.018f;

        tideRoutingBoomRenderer.sprite = GetRoutingBoomSprite();
        Vector2 boomSize = GetAspectPreservingWorldSize(tideRoutingBoomRenderer.sprite, 0.11f);
        if (boomSize.x < 0.78f)
        {
            boomSize = new Vector2(0.78f, boomSize.y);
        }
        SetWorldSize(
            tideRoutingBoomRenderer,
            boomCenter,
            boomSize,
            new Color(0.9f, 0.9f, 0.84f, 0.94f),
            boomAngle);

        float boomRadians = boomAngle * Mathf.Deg2Rad;
        Vector2 boomAttach = boomCenter - new Vector2(Mathf.Cos(boomRadians), Mathf.Sin(boomRadians)) * boomSize.x * 0.42f;
        Vector2 winchPivot = windlassBase + new Vector2(0.32f, 0.38f);
        Vector2 ropeDirection = boomAttach - winchPivot;
        float ropeAngle = Mathf.Atan2(ropeDirection.y, ropeDirection.x) * Mathf.Rad2Deg;
        float workTension01 = routingWorkActive && routingWorkDirection > 0f ? 1f : boom01;
        Color ropeColor = Color.Lerp(
            new Color(0.48f, 0.44f, 0.34f, 0.72f),
            new Color(0.74f, 0.68f, 0.54f, 0.94f),
            workTension01);
        tideRoutingRopeRenderer.sprite = GetNetLineSprite();
        SetWorldSize(
            tideRoutingRopeRenderer,
            (winchPivot + boomAttach) * 0.5f,
            new Vector2(ropeDirection.magnitude, Mathf.Lerp(0.04f, 0.058f, workTension01)),
            ropeColor,
            ropeAngle);
    }

    private void UpdateNetVisuals(float time)
    {
        v54DeployedPoseValid = false;
        float[] yValues =
        {
            GetNetLineY(NetLine.Low),
            GetNetLineY(NetLine.Mid),
            GetNetLineY(NetLine.High)
        };
        float activeNetY = GetActiveNetVisualY();
        float headLineY = GetNetHeadLineY();
        float stakeTieY = GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.22f;
        bool useV54Net = HasCompleteV54NetPresentation();
        bool useFormalNet = useV54Net || GetFormalNetSprite() != null;
        float firstStakeX = GetNetFirstStakeX();
        float secondStakeX = GetNetSecondStakeX();
        float unroll01 = Mathf.SmoothStep(0f, 1f, netUnrollProgress);
        float freeEndX = Mathf.Lerp(firstStakeX + 0.08f, secondStakeX, unroll01);
        float materialPulse01 = GetCurrentNetMaterialPulse01(time);
        float frayPulse01 = netFraying01 * (0.68f + Mathf.Abs(Mathf.Sin(time * 8.4f)) * 0.32f);
        if (netTouched && state == SliceState.TideRising)
        {
            activeNetY += Mathf.Sin(time * 5.2f) * 0.045f * GetNaturalTideHeight01();
            activeNetY += Mathf.Sin(time * 12.2f) * 0.018f * frayPulse01;
        }
        TideOceanSample netOcean = GetNetOceanSample();
        float visualContact01 = EvaluateNetWaterContact01(activeNetY, netOcean.SurfaceY);
        bool netIsWet = visualContact01 > 0.01f || (state == SliceState.EbbCollect && netTouched);

        for (int i = 0; i < netLines.Count; i++)
        {
            NetLine line = (NetLine)i;
            bool selected = selectedNetLine == line;
            bool netIsMovingOrSet = netDeployed || netRigStep >= NetRigStep.FirstEndTied || netHaulProgress > 0.01f;
            // V54 已经拥有浮纲和沉纲；旧的高亮直线只供回退网使用，否则会在
            // 写实网体上叠出一条发光调试线，破坏所见即所得。
            netLines[i].enabled = !useV54Net &&
                (!useFormalNet || (selected && netIsMovingOrSet));
            if (!netLines[i].enabled)
            {
                continue;
            }

            bool wet = netIsWet;
            Color color = selected
                ? new Color(0.84f, 0.66f, 0.38f, useFormalNet ? 0.72f : 0.98f)
                : new Color(0.38f, 0.68f, 0.7f, 0.16f);
            if (wet)
            {
                color = Color.Lerp(color, new Color(0.32f, 0.86f, 0.92f, 0.9f), 0.45f);
            }

            float lineY = selected ? activeNetY : yValues[i];
            float lineCenterX = netAnchor.x;
            float lineWidth = selected ? 1.72f : 1.18f;
            float lineThickness = selected ? (useFormalNet ? 0.058f : 0.085f) : 0.035f;
            if (selected && useFormalNet)
            {
                // This is the sink line. The authored net owns the head rope while the
                // two suspension ropes below explain how the whole curtain hangs over
                // the seaward edge instead of lying across the boardwalk.
                float visibleEndX = !netDeployed && netRigStep == NetRigStep.FirstEndTied
                    ? freeEndX
                    : secondStakeX;
                lineCenterX = (firstStakeX + visibleEndX) * 0.5f;
                lineWidth = Mathf.Max(0.08f, visibleEndX - firstStakeX);
                lineY = activeNetY;
            }
            SetWorldSize(
                netLines[i],
                new Vector2(lineCenterX, lineY),
                new Vector2(lineWidth, lineThickness),
                color,
                selected ? Mathf.Sin(time) * 1.2f : 0f);
        }

        bool firstEndSuspended = useFormalNet && !netDeployed &&
            (netRigStep == NetRigStep.FirstEndTied ||
             netRigStep == NetRigStep.Unrolled ||
             netRigStep == NetRigStep.SecondEndTied ||
             netRigStep == NetRigStep.Lowering);
        bool secondEndSuspended = useFormalNet &&
            (netRigStep == NetRigStep.SecondEndTied || netRigStep == NetRigStep.Lowering || netDeployed);
        SetEnabled(netSuspensionRopes, false);
        if (!useV54Net && netSuspensionRopes.Count >= 2)
        {
            if (firstEndSuspended || netDeployed)
            {
                SetNetSuspensionRope(
                    netSuspensionRopes[0],
                    new Vector2(firstStakeX, stakeTieY),
                    new Vector2(firstStakeX + 0.045f, headLineY));
            }
            if (secondEndSuspended)
            {
                SetNetSuspensionRope(
                    netSuspensionRopes[1],
                    new Vector2(secondStakeX, stakeTieY),
                    new Vector2(secondStakeX - 0.045f, headLineY));
            }
        }

        bool showNetBody = true;
        float netBodyAlpha = netDeployed || netLoweringProgress > 0.01f ? 0.94f : 0.78f;
        if (useV54Net)
        {
            UpdateV54NetVisuals(
                time,
                activeNetY,
                headLineY,
                stakeTieY,
                firstStakeX,
                secondStakeX,
                unroll01,
                netIsWet);
        }
        else
        {
            bool securedWetBundle = netSecuredEarly && HasSecuredPostHarvest();
            bool showNetBundle = showNetBody && useFormalNet && !netDeployed &&
                (securedWetBundle || netRigStep == NetRigStep.Stored || netRigStep == NetRigStep.Carrying);
            SetEnabled(formalNetBundleRenderer, showNetBundle);
            if (showNetBundle)
            {
                bool carryingBundle = netRigStep == NetRigStep.Carrying;
                formalNetBundleRenderer.sprite = carryingBundle || securedWetBundle
                    ? GetFormalNetSprite()
                    : GetFormalNightPrepSprite(TidePrepChoice.Rope) ?? GetPrepRopeCoilSprite();
                Vector2 bundlePosition = securedWetBundle
                    ? new Vector2(netAnchor.x - 0.42f, GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.22f)
                    : carryingBundle
                        ? playerPosition + new Vector2(playerFacing * 0.3f, 0.18f)
                        : new Vector2(GetNetStoredX() + 0.03f, GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.28f);
                Vector2 bundleSize = securedWetBundle
                    ? new Vector2(0.5f, 0.27f)
                    : carryingBundle ? new Vector2(0.52f, 0.25f) : new Vector2(0.58f, 0.62f);
                float bundleRotation = securedWetBundle ? -8f : carryingBundle ? -playerFacing * 8f : 0f;
                SetWorldSize(formalNetBundleRenderer, bundlePosition, bundleSize, Color.white, bundleRotation);
                formalNetBundleRenderer.flipX = carryingBundle && playerFacing < 0;
            }

            SetEnabled(formalNetRenderer, showNetBody && useFormalNet && !showNetBundle);
            if (showNetBody && useFormalNet && !showNetBundle)
            {
                formalNetRenderer.sprite = GetFormalNetSprite();
                float waterContact01 = netDeployed ? visualContact01 : 0f;
                float netLoad01 = netTouched
                    ? Mathf.Max(Mathf.Max(waterContact01, GetNaturalTideHeight01()), netPeakTension01)
                    : waterContact01 * 0.42f;
                if (netCatchResolved && netDeployed)
                {
                    netLoad01 = Mathf.Max(netLoad01, Mathf.Max(GetNetCatchLoad01(), materialPulse01 * 0.72f));
                }
                float flowDirection = GetNaturalCurrentSigned01();
                float visibleCurrentDrag01 = GetVisibleNetCurrentDrag01(activeNetY);
                float netFlowOffsetX = GetVisibleNetCurrentShiftX(activeNetY);
                Vector2 netPosition;
                Vector2 netSize;
                float tugAngle;
                if (!netDeployed && netRigStep == NetRigStep.Stored)
                {
                    netPosition = new Vector2(GetNetStoredX(), GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.19f);
                    netSize = new Vector2(0.54f, 0.28f);
                    tugAngle = -4f;
                }
                else if (!netDeployed && netRigStep == NetRigStep.Carrying)
                {
                    netPosition = playerPosition + new Vector2(playerFacing * 0.16f, 0.08f);
                    netSize = new Vector2(0.5f, 0.32f);
                    tugAngle = -playerFacing * 7f;
                }
                else if (!netDeployed && netRigStep == NetRigStep.FirstEndTied)
                {
                    float shallowSinkY = GetNetLineY(NetLine.High);
                    float curtainHeight = Mathf.Max(0.18f, headLineY - shallowSinkY);
                    netPosition = new Vector2(
                        (firstStakeX + freeEndX) * 0.5f,
                        (headLineY + shallowSinkY) * 0.5f);
                    netSize = new Vector2(
                        Mathf.Max(0.22f, freeEndX - firstStakeX + 0.12f),
                        curtainHeight);
                    tugAngle = 0f;
                }
                else
                {
                    float curtainHeight = Mathf.Max(0.18f, headLineY - activeNetY);
                    netPosition = new Vector2(
                        netAnchor.x + netFlowOffsetX + Mathf.Sin(time * 15.2f) * 0.035f * frayPulse01,
                        (headLineY + activeNetY) * 0.5f - netLoad01 * 0.025f);
                    netSize = new Vector2(
                        1.55f + netLoad01 * 0.14f + materialPulse01 * 0.05f,
                        curtainHeight - netLoad01 * 0.07f - materialPulse01 * 0.035f);
                    tugAngle = visibleCurrentDrag01 * 4.2f +
                        Mathf.Sin(time * 5.2f) * 1.15f * netLoad01 +
                        Mathf.Sin(time * 14.6f) * 2.4f * frayPulse01;
                }

                if ((state == SliceState.EbbCollect || state == SliceState.TideRising) && netHaulProgress > 0f)
                {
                    float haul01 = GetNetHaulVisualProgress01();
                    Vector2 hauledBundle = new Vector2(netAnchor.x - 0.42f, GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.22f);
                    netPosition = Vector2.Lerp(netPosition, hauledBundle, haul01);
                    netSize = Vector2.Lerp(netSize, new Vector2(0.48f, 0.26f), haul01);
                    tugAngle = Mathf.Lerp(tugAngle, -8f, haul01);
                }
                if (netFraying01 > 0.001f && !netBrokeThisTide)
                {
                    netPosition += new Vector2(flowDirection * netFraying01 * 0.06f, -netFraying01 * 0.13f);
                    netSize.x += netFraying01 * 0.11f;
                    netSize.y *= Mathf.Lerp(1f, 0.82f, netFraying01);
                    tugAngle += Mathf.Sin(time * 16.8f) * netFraying01 * 3.2f;
                    netBodyAlpha *= Mathf.Lerp(1f, 0.82f, netFraying01);
                }
                if (netBrokeThisTide)
                {
                    netPosition += new Vector2(flowDirection * 0.08f, -0.11f);
                    netSize.y *= 0.72f;
                    tugAngle += flowDirection * 4.5f;
                    netBodyAlpha *= 0.8f;
                }

                Color netBodyColor = Color.Lerp(
                    Color.white,
                    new Color(0.72f, 0.62f, 0.48f, 1f),
                    netFraying01 * 0.58f);
                netBodyColor.a = netBodyAlpha;
                SetWorldSize(
                    formalNetRenderer,
                    netPosition,
                    netSize,
                    netBodyColor,
                    tugAngle);
            }
        }

        for (int i = 0; i < netWeights.Count; i++)
        {
            netWeights[i].enabled = showNetBody && !useFormalNet;
            if (!netWeights[i].enabled)
            {
                continue;
            }

            float column01 = netWeights.Count <= 1 ? 0.5f : i / (netWeights.Count - 1f);
            float x = Mathf.Lerp(netAnchor.x - 0.62f, netAnchor.x + 0.62f, column01);
            SetWorldSize(netWeights[i], new Vector2(x, activeNetY - 0.04f), new Vector2(0.16f, 0.22f), new Color(0.54f, 0.46f, 0.31f, netBodyAlpha), 0f);
        }

        for (int i = 0; i < netKnots.Count; i++)
        {
            int row = i / 3;
            int column = i % 3;
            netKnots[i].enabled = showNetBody && !useFormalNet;
            if (!netKnots[i].enabled)
            {
                continue;
            }

            bool wet = netIsWet;
            Color knotColor = new Color(0.88f, 0.76f, 0.48f, netBodyAlpha);
            if (wet)
            {
                knotColor = Color.Lerp(knotColor, new Color(0.35f, 0.85f, 0.85f, 0.9f), 0.35f);
            }

            float x = netAnchor.x - 0.52f + column * 0.52f;
            float row01 = (row + 1f) / 4f;
            float y = Mathf.Lerp(headLineY, activeNetY, row01) + Mathf.Sin(time * 1.7f + column + row) * 0.012f;
            SetWorldSize(netKnots[i], new Vector2(x, y), new Vector2(0.12f, 0.12f), knotColor, Mathf.Sin((column + row) * 1.3f) * 7f);
        }

        for (int i = 0; i < netFloats.Count; i++)
        {
            netFloats[i].enabled = showNetBody && !useFormalNet;
            if (!netFloats[i].enabled)
            {
                continue;
            }

            Color floatColor = new Color(0.94f, 0.68f, 0.26f, netBodyAlpha);
            float column01 = netFloats.Count <= 1 ? 0.5f : i / (netFloats.Count - 1f);
            float x = Mathf.Lerp(netAnchor.x - 0.78f, netAnchor.x + 0.78f, column01);
            float bob = Mathf.Sin(time * 1.4f + i * 0.9f) * 0.018f;
            SetWorldSize(netFloats[i], new Vector2(x, headLineY + 0.02f + bob), new Vector2(0.14f, 0.085f), floatColor, Mathf.Sin(time + i) * 2.5f);
        }

        bool handlingNet = (!netDeployed &&
                (netRigStep == NetRigStep.FirstEndTied ||
                 netRigStep == NetRigStep.Unrolled ||
                 netRigStep == NetRigStep.Lowering ||
                 (netRigStep == NetRigStep.Carrying && netRigActionProgress > 0f))) ||
            ((state == SliceState.EbbCollect || state == SliceState.TideRising) &&
                (netHaulProgress > 0.01f || netDepthAdjustmentActive) &&
                IsPlayerNearNet());
        SetEnabled(netHandlingRopeRenderer, handlingNet);
        if (handlingNet)
        {
            netHandlingRopeRenderer.sprite = GetNetLineSprite();
            float handReach = GetFormalHaulPlayerSprite() != null ? 0.34f : 0.18f;
            Vector2 handPosition = playerPosition + new Vector2(playerFacing * handReach, 0.14f);
            Vector2 netTiePosition;
            if (!netDeployed && netRigStep == NetRigStep.Carrying)
            {
                netTiePosition = new Vector2(firstStakeX, GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.22f);
            }
            else if (!netDeployed &&
                     (netRigStep == NetRigStep.FirstEndTied || netRigStep == NetRigStep.Unrolled))
            {
                // The rope follows the actual free end throughout the walk. Snapping it
                // from the first stake to the second made the character look detached
                // from the net during most of the unroll animation.
                netTiePosition = new Vector2(freeEndX, GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.22f);
            }
            else
            {
                netTiePosition = new Vector2(secondStakeX - 0.12f, activeNetY + 0.06f);
            }
            Vector2 ropeDirection = netTiePosition - handPosition;
            float ropeAngle = Mathf.Atan2(ropeDirection.y, ropeDirection.x) * Mathf.Rad2Deg;
            float haulTension01 = netHaulProgress > 0.01f
                ? Mathf.Max(netHaulEffort01, netHaulLoad01 * 0.72f)
                : netDepthAdjustmentActive
                    ? Mathf.Clamp01(0.48f + Mathf.Abs(netDepthAdjustmentDirection) * 0.18f + netFraying01 * 0.3f)
                    : 0.35f;
            SetWorldSize(
                netHandlingRopeRenderer,
                (handPosition + netTiePosition) * 0.5f,
                new Vector2(ropeDirection.magnitude, Mathf.Lerp(0.048f, 0.072f, haulTension01)),
                Color.Lerp(
                    new Color(0.58f, 0.48f, 0.34f, 0.86f),
                    Color.Lerp(
                        new Color(0.82f, 0.68f, 0.4f, 0.96f),
                        new Color(0.62f, 0.34f, 0.23f, 0.98f),
                        netFraying01),
                    haulTension01),
                ropeAngle);
        }

        UpdateIncomingTideCarryVisuals(time, activeNetY);
        UpdateCaughtNetVisuals(time, activeNetY);
        UpdateNetDamageVisuals(time, yValues);
        UpdateNetWaterContactVisuals(time, activeNetY);
    }

    private void UpdateV54NetVisuals(
        float time,
        float activeNetY,
        float headLineY,
        float stakeTieY,
        float firstStakeX,
        float secondStakeX,
        float unroll01,
        bool netIsWet)
    {
        SetMaskEnabled(netRevealMask, false);
        formalNetRenderer.maskInteraction = SpriteMaskInteraction.None;
        formalNetRenderer.flipX = false;
        formalNetBundleRenderer.flipX = false;

        bool securedWetBundle = netSecuredEarly && HasSecuredPostHarvest();
        bool showBundle = !netBrokeThisTide && !netDeployed &&
            (securedWetBundle || netRigStep == NetRigStep.Stored || netRigStep == NetRigStep.Carrying);
        if (showBundle)
        {
            TideV54NetVisualState bundleState = securedWetBundle
                ? TideV54NetVisualState.HauledWet
                : netRigStep == NetRigStep.Carrying
                    ? TideV54NetVisualState.CarriedDry
                    : TideV54NetVisualState.StoredDry;
            bool flipped = bundleState == TideV54NetVisualState.CarriedDry && playerFacing < 0;
            Vector2 bundleSize = TideV54NetPresentationModel.GetContractWorldSize(bundleState);
            Vector2 targetAnchor;
            if (bundleState == TideV54NetVisualState.CarriedDry)
            {
                // hand_grip 对齐 V41 当前帧双手中点。Bundle 自身不再充当人物
                // 动作；行走、停步和转向仍由角色状态机负责。
                targetAnchor = GetV41CarryNetHandWorldPosition();
            }
            else if (bundleState == TideV54NetVisualState.HauledWet)
            {
                targetAnchor = new Vector2(firstStakeX + 0.025f, stakeTieY + 0.015f);
            }
            else
            {
                targetAnchor = new Vector2(
                    GetNetStoredX() + 0.03f,
                    GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.17f);
            }

            Vector2 bundlePosition = TideV54NetPresentationModel.ResolveBundlePosition(
                bundleState,
                targetAnchor,
                bundleSize,
                flipped);
            formalNetBundleRenderer.sprite = formalV54NetCatalog.Get(bundleState);
            formalNetBundleRenderer.flipX = flipped;
            SetEnabled(formalNetBundleRenderer, true);
            SetEnabled(formalNetRenderer, false);
            SetWorldSize(
                formalNetBundleRenderer,
                bundlePosition,
                bundleSize,
                Color.white,
                0f);
            SetEnabled(netSuspensionRopes, false);
            return;
        }

        SetEnabled(formalNetBundleRenderer, false);
        SetEnabled(formalNetRenderer, true);

        TideV54NetVisualState visualState = netBrokeThisTide
            ? TideV54NetVisualState.BrokenResidue
            : netFraying01 >= 0.12f
                ? TideV54NetVisualState.DeployedFrayed
                : netIsWet || netTouched
                    ? TideV54NetVisualState.DeployedWet
                    : TideV54NetVisualState.DeployedDry;
        formalNetRenderer.sprite = formalV54NetCatalog.Get(visualState);

        Vector2 leftAttachmentTarget = new Vector2(firstStakeX + 0.045f, headLineY);
        Vector2 rightAttachmentTarget = new Vector2(secondStakeX - 0.045f, headLineY);
        float dynamicSinkY = activeNetY;
        float visibleCurrentDrag01 = GetVisibleNetCurrentDrag01(activeNetY);
        float visibleCurrentShiftX = GetVisibleNetCurrentShiftX(activeNetY);
        if (visualState == TideV54NetVisualState.DeployedWet ||
            visualState == TideV54NetVisualState.DeployedFrayed)
        {
            float load01 = Mathf.Max(GetNetCatchLoad01(), netPeakTension01);
            dynamicSinkY += Mathf.Sin(time * 2.1f + firstStakeX * 0.73f) * 0.012f *
                Mathf.Clamp01(0.35f + load01);
            dynamicSinkY -= Mathf.Abs(visibleCurrentDrag01) * 0.035f *
                (1f - GetNetHaulVisualProgress01());
        }

        Vector2 netPosition;
        Vector2 netSize;
        if (visualState == TideV54NetVisualState.BrokenResidue)
        {
            Vector2 dryLeft01 = TideV54NetPresentationModel.GetLeftAttachmentTopLeft01(
                TideV54NetVisualState.DeployedDry);
            Vector2 dryRight01 = TideV54NetPresentationModel.GetRightAttachmentTopLeft01(
                TideV54NetVisualState.DeployedDry);
            float alignedDryWidth = (rightAttachmentTarget.x - leftAttachmentTarget.x) /
                Mathf.Max(0.05f, dryRight01.x - dryLeft01.x);
            TideV54NetPresentationModel.ResolveBrokenTransform(
                leftAttachmentTarget,
                dynamicSinkY,
                alignedDryWidth /
                    TideV54NetPresentationModel.GetContractWorldSize(
                        TideV54NetVisualState.DeployedDry).x,
                out netPosition,
                out netSize);
        }
        else
        {
            TideV54NetPresentationModel.ResolveDeployedTransform(
                visualState,
                leftAttachmentTarget,
                rightAttachmentTarget,
                dynamicSinkY,
                out netPosition,
                out netSize);
        }

        // V54 保持原图宽高和两端语义锚点，只让整张湿网在两根悬绳下随流
        // 偏移。悬绳随后连接移动后的真实网角，挂物也读取同一 netPosition，
        // 因而不会出现“网在吃流、货物还钉在旧坐标”的所见非所得。
        netPosition.x += visibleCurrentShiftX;

        // 收网时保持两端宽度不变，只把沉纲真实提向浮纲。旧实现把完整网横向
        // 缩成网团，既不符合 V54 契约，也让玩家看不出自己正在逐段起网。
        SetWorldSize(formalNetRenderer, netPosition, netSize, Color.white, 0f);
        v54DeployedPoseValid = true;
        v54DeployedPosition = netPosition;
        v54DeployedWorldSize = netSize;

        bool revealWhileUnrolling = !netDeployed &&
            netRigStep == NetRigStep.FirstEndTied && unroll01 < 0.999f;
        if (revealWhileUnrolling)
        {
            float spriteLeft = netPosition.x - netSize.x * 0.5f;
            float visibleRight = Mathf.Lerp(
                leftAttachmentTarget.x,
                rightAttachmentTarget.x,
                Mathf.Clamp01(unroll01));
            float maskRight = Mathf.Min(
                netPosition.x + netSize.x * 0.5f + 0.02f,
                visibleRight + 0.08f);
            float maskWidth = Mathf.Max(0.06f, maskRight - spriteLeft);
            netRevealMask.sprite = GetMoonWashSprite();
            netRevealMask.frontSortingOrder = formalNetRenderer.sortingOrder + 1;
            netRevealMask.backSortingOrder = formalNetRenderer.sortingOrder - 1;
            SetMaskWorldSize(
                netRevealMask,
                new Vector2(spriteLeft + maskWidth * 0.5f, netPosition.y),
                new Vector2(maskWidth, netSize.y + 0.16f),
                0f);
            formalNetRenderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
        }

        SetEnabled(netSuspensionRopes, false);
        if (netSuspensionRopes.Count < 2)
        {
            return;
        }

        bool firstEndSuspended = netDeployed ||
            netRigStep == NetRigStep.FirstEndTied ||
            netRigStep == NetRigStep.Unrolled ||
            netRigStep == NetRigStep.SecondEndTied ||
            netRigStep == NetRigStep.Lowering;
        bool secondEndSuspended = !netBrokeThisTide &&
            (netDeployed || netRigStep == NetRigStep.SecondEndTied ||
             netRigStep == NetRigStep.Lowering);
        if (firstEndSuspended)
        {
            Vector2 actualLeft = TideV54NetPresentationModel.EvaluateWorldAnchor(
                netPosition,
                netSize,
                TideV54NetPresentationModel.GetLeftAttachmentTopLeft01(visualState));
            SetNetSuspensionRope(
                netSuspensionRopes[0],
                new Vector2(firstStakeX, stakeTieY),
                actualLeft);
        }
        if (secondEndSuspended)
        {
            Vector2 actualRight = TideV54NetPresentationModel.EvaluateWorldAnchor(
                netPosition,
                netSize,
                TideV54NetPresentationModel.GetRightAttachmentTopLeft01(visualState));
            SetNetSuspensionRope(
                netSuspensionRopes[1],
                new Vector2(secondStakeX, stakeTieY),
                actualRight);
        }
    }

    private Vector2 GetV41CarryNetHandWorldPosition()
    {
        Sprite sprite = playerRenderer != null ? playerRenderer.sprite : null;
        if (sprite == null || sprite.name.IndexOf("CarryNetWalk", StringComparison.Ordinal) < 0)
        {
            return playerPosition + new Vector2(playerFacing * 0.22f, 0.2f);
        }

        float carryCycle01 = playerMoving ? playerWalkCycle : 0f;
        Vector2 handPixels =
            TideV41CharacterContactPresentationModel.GetCarryNetHandCenterTopLeftPixels(
                carryCycle01);
        float localX = (handPixels.x - sprite.pivot.x) / sprite.pixelsPerUnit;
        float localY = (sprite.rect.height - handPixels.y - sprite.pivot.y) /
            sprite.pixelsPerUnit;
        if (playerRenderer.flipX)
        {
            localX = -localX;
        }

        Vector3 world = playerRenderer.transform.TransformPoint(new Vector3(localX, localY, 0f));
        return new Vector2(world.x, world.y);
    }

    private void SetNetSuspensionRope(SpriteRenderer renderer, Vector2 stakePoint, Vector2 netPoint)
    {
        Vector2 direction = netPoint - stakePoint;
        renderer.enabled = true;
        renderer.sprite = GetNetLineSprite();
        renderer.sortingOrder = -1;
        SetWorldSize(
            renderer,
            (stakePoint + netPoint) * 0.5f,
            new Vector2(direction.magnitude, 0.046f),
            new Color(0.56f, 0.46f, 0.31f, 0.96f),
            Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
    }

    private float GetActiveNetVisualY()
    {
        float highY = GetNetLineY(NetLine.High);
        float lowY = GetNetLineY(NetLine.Low);
        float activeY = GetSelectedNetY();
        if (!netDeployed &&
            (netRigStep == NetRigStep.FirstEndTied ||
             netRigStep == NetRigStep.Unrolled ||
             netRigStep == NetRigStep.SecondEndTied ||
             netRigStep == NetRigStep.Lowering))
        {
            activeY = Mathf.Lerp(highY, lowY, netLoweringProgress);
        }

        if ((state == SliceState.EbbCollect || state == SliceState.TideRising) && netHaulProgress > 0f)
        {
            float haulTargetY = GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.2f;
            activeY = Mathf.Lerp(activeY, haulTargetY, GetNetHaulVisualProgress01());
        }

        return activeY;
    }

    private void UpdateNetWaterContactVisuals(float time, float activeNetY)
    {
        TideOceanSample netOcean = GetNetOceanSample();
        float contact01 = EvaluateNetWaterContact01(activeNetY, netOcean.SurfaceY);
        float overtopped01 = EvaluateNetWaterOvertopped01(activeNetY, netOcean.SurfaceY);
        bool visible = netDeployed && contact01 > 0.01f && overtopped01 < 0.995f && state == SliceState.TideRising;
        if (!visible)
        {
            SetEnabled(netWaterContactRenderer, false);
            return;
        }

        float tension01 = Mathf.Max(contact01 * 0.35f, netPeakTension01);
        float tug = Mathf.Sin(time * Mathf.Lerp(3.8f, 6.4f, tension01)) * Mathf.Lerp(0.025f, 0.095f, tension01);
        bool useFormalContactWave = HasCompleteV54NetPresentation() &&
            HasCompleteV43SeaWeatherPresentation();
        if (useFormalContactWave)
        {
            netWaterContactRenderer.sprite = TideV43SeaWeatherPresentationModel.EvaluateWaveFrame(
                formalV43SeaWeatherCatalog,
                TideV43WaveKind.LongSwell,
                time,
                0.37f,
                Mathf.Lerp(0.78f, 1.24f, tension01));
        }
        else
        {
            netWaterContactRenderer.sprite = GetFoamSprite();
        }

        Color contactColor = useFormalContactWave
            ? new Color(1f, 1f, 1f, 0.18f + contact01 * 0.35f + tension01 * 0.12f)
            : Color.Lerp(
                new Color(0.72f, 0.96f, 0.92f, 0.3f + contact01 * 0.42f),
                new Color(0.94f, 0.66f, 0.38f, 0.82f),
                tension01 * 0.55f);
        contactColor.a *= 1f - Mathf.SmoothStep(0f, 1f, overtopped01);
        float attachedContactY = Mathf.Clamp(netOcean.SurfaceY, activeNetY, GetNetHeadLineY());
        Vector2 contactSize = useFormalContactWave
            ? new Vector2(1.62f + contact01 * 0.2f, 0.16f + tension01 * 0.08f)
            : new Vector2(
                1.55f + contact01 * 0.35f + tension01 * 0.28f,
                0.14f + contact01 * 0.08f + tension01 * 0.05f);
        SetWorldSize(
            netWaterContactRenderer,
            new Vector2(netAnchor.x + GetVisibleNetCurrentShiftX(activeNetY) + tug, attachedContactY),
            contactSize,
            contactColor,
            useFormalContactWave
                ? Mathf.Atan(netOcean.Slope) * Mathf.Rad2Deg * 0.18f
                : tug * 18f);
    }

    private void UpdateIncomingTideCarryVisuals(float time, float activeNetY)
    {
        bool primaryDrift = viewMode == SliceViewMode.Shelter &&
            state == SliceState.TideRising &&
            !netCatchResolved &&
            harvestPhysicalState == HarvestPhysicalState.Drifting &&
            GetNetOceanSample().SurfaceY > lowWaterY + 0.18f;
        bool outerWreckDrift = viewMode == SliceViewMode.Shelter &&
            state == SliceState.TideRising &&
            !outerWreckPassedNearshore &&
            (extraSaltWoodOwner == ExtraSaltWoodOwner.PassingNearshore ||
             extraSaltWoodOwner == ExtraSaltWoodOwner.RoutedToNet) &&
            GetNetOceanSample().SurfaceY > lowWaterY + 0.18f;
        bool initialDrift = primaryDrift || outerWreckDrift;
        bool addedPieceCanFreeDrift = !TryGetV59FindSpec(
                currentHarvest,
                netCatchVisualPieceCount,
                currentHarvestBatchId,
                false,
                out TideV59FindSpec addedPieceSpec) ||
            addedPieceSpec.CanFreeFloat;
        bool addedPieceDrift = viewMode == SliceViewMode.Shelter &&
            state == SliceState.TideRising &&
            netDeployed &&
            netCatchResolved &&
            harvestPhysicalState == HarvestPhysicalState.CaughtInNet &&
            netCatchVisualPieceCount < netCatchBundleTier &&
            addedPieceCanFreeDrift;
        bool visible = initialDrift || addedPieceDrift;
        SetEnabled(incomingTideCarryItems, visible);
        if (!visible)
        {
            return;
        }

        HarvestKind incomingKind = initialDrift ? GetIncomingTideCarryKind() : currentHarvest;
        bool showPrimarySource = initialDrift && primaryDrift;
        bool showExtraSaltWood = initialDrift && outerWreckDrift;
        int objectCount = initialDrift
            ? (showPrimarySource ? 1 : 0) + (showExtraSaltWood ? 1 : 0)
            : 1;
        // Each object owns one surface wake. The previous 0/object, 1/ripple,
        // 2/object layout left the second routed object sliding over dry pixels.
        int rendererCount = objectCount * 2;

        for (int i = 0; i < incomingTideCarryItems.Count; i++)
        {
            SpriteRenderer carryItem = incomingTideCarryItems[i];
            carryItem.enabled = i < rendererCount;
            if (!carryItem.enabled)
            {
                continue;
            }

            bool waterRipple = (i & 1) == 1;
            int objectIndex = i / 2;
            bool isExtraSaltWood = showExtraSaltWood && (!showPrimarySource || objectIndex == objectCount - 1);
            int physicalPieceIndex = initialDrift
                ? isExtraSaltWood ? 0 : objectIndex
                : netCatchVisualPieceCount;
            HarvestKind pieceKind = isExtraSaltWood ? HarvestKind.Wood : incomingKind;
            int pieceBatchId = isExtraSaltWood
                ? extraSaltWoodBatchId
                : initialDrift
                    ? tideSourceBatchId
                    : currentHarvestBatchId;
            carryItem.sprite = waterRipple
                ? GetIncomingTideCarryRippleSprite()
                : GetCaughtNetItemSprite(
                    pieceKind,
                    isExtraSaltWood ? 0 : physicalPieceIndex,
                    pieceBatchId,
                    true);

            float carry01 = initialDrift
                ? isExtraSaltWood ? outerWreckTravel01 : incomingHarvestTravel01
                : addedHarvestPieceTravel01;
            float delay = initialDrift && showPrimarySource && showExtraSaltWood && isExtraSaltWood ? 0.04f : 0f;
            float staggered01 = Mathf.Max(0f, carry01 - delay);
            float physicalTravel01 = staggered01;
            // Spawn outside the camera's right edge, then let the authored wake enter first.
            // Starting inside the playable pier made every catch visibly pop into existence.
            float startX = GetTideFlatVisiblePathRight() + 1.65f + objectIndex * 0.42f;
            float endOffsetX = objectCount == 2
                ? (objectIndex == 0 ? -0.24f : 0.24f)
                : Mathf.Clamp((physicalPieceIndex - 1) * 0.16f, -0.16f, 0.16f);
            bool saltWoodFeedsNet = isExtraSaltWood &&
                routingDecisionLocked &&
                tideRoutingMode == TideRoutingMode.FeedNet;
            float x;
            if (isExtraSaltWood)
            {
                // Both outcomes share the same incoming water path. Only after the
                // timber reaches the visible boom does the route bend toward the net
                // or continue past it, so locking a decision cannot teleport the item.
                float forkTravel01 = TideContinuousRoutingModel.DecisionTravel01;
                float forkX = netAnchor.x + 0.68f;
                if (physicalTravel01 <= forkTravel01)
                {
                    float preFork01 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, forkTravel01, physicalTravel01));
                    x = Mathf.Lerp(startX, forkX, preFork01);
                }
                else
                {
                    float postFork01 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(forkTravel01, 1f, physicalTravel01));
                    float routeEndX = saltWoodFeedsNet ? netAnchor.x + 0.18f : netAnchor.x - 1.05f;
                    x = Mathf.Lerp(forkX, routeEndX, postFork01);
                    if (physicalTravel01 > TideDriftSourceModel.NetIntersectionTravel01)
                    {
                        float exit01 = Mathf.SmoothStep(
                            0f,
                            1f,
                            Mathf.InverseLerp(
                                TideDriftSourceModel.NetIntersectionTravel01,
                                TideDriftSourceModel.NearshoreExitTravel01,
                                physicalTravel01));
                        x = Mathf.Lerp(routeEndX, netAnchor.x - 1.7f, exit01);
                    }
                }
            }
            else
            {
                float endX = netAnchor.x + endOffsetX;
                float approach01 = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(staggered01));
                x = Mathf.Lerp(startX, endX, approach01);
                if (staggered01 > TideDriftSourceModel.NetIntersectionTravel01)
                {
                    float exit01 = Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(
                            TideDriftSourceModel.NetIntersectionTravel01,
                            TideDriftSourceModel.NearshoreExitTravel01,
                            staggered01));
                    x = Mathf.Lerp(endX, netAnchor.x - 1.45f, exit01);
                }
            }

            Vector2 objectSize = GetHarvestWorldSize(
                pieceKind,
                isExtraSaltWood ? 0 : physicalPieceIndex,
                pieceBatchId,
                true);
            Vector2 worldSize = waterRipple
                ? new Vector2(Mathf.Max(0.54f, objectSize.x * 1.35f), 0.13f)
                : objectSize;
            float settleIntoMesh01 = isExtraSaltWood
                ? saltWoodFeedsNet && netDeployed
                    ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TideContinuousRoutingModel.DecisionTravel01, 1f, physicalTravel01))
                    : 0f
                : netDeployed && netCaptureProgress01 >= 0.999f &&
                  TideDriftSourceModel.IsInsideNetCaptureWindow(staggered01)
                    ? Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(
                            TideDriftSourceModel.NetCaptureEntryTravel01,
                            TideDriftSourceModel.NetIntersectionTravel01,
                            staggered01))
                    : 0f;
            int pieceMotionIndex = isExtraSaltWood ? 0 : physicalPieceIndex;
            float freeDrift01 = 1f - settleIntoMesh01;
            // Apply the horizontal surge before sampling the ocean. The object and its
            // wake now read the exact wave that is visibly beneath their final X.
            x += GetHarvestDriftSurge(pieceKind, time, pieceMotionIndex) * freeDrift01;
            TideOceanSample carryOcean = GetOceanSample(x);
            float surfaceY = carryOcean.SurfaceY +
                (waterRipple ? -0.012f : GetHarvestSurfaceOffset(pieceKind));
            int targetPieceCount = initialDrift
                ? Mathf.Clamp(objectCount, 1, 3)
                : Mathf.Clamp(netCatchVisualPieceCount + 1, 1, 3);
            int targetPieceIndex = initialDrift
                ? Mathf.Clamp(objectIndex, 0, targetPieceCount - 1)
                : targetPieceCount - 1;
            TideNetCatchPresentationModel.Pose targetPose =
                TideNetCatchPresentationModel.GetInNetPose(
                    ToNetCatchMaterial(pieceKind),
                    targetPieceIndex,
                    targetPieceCount);
            Vector2 catchTarget = GetCaughtNetWorldPosition(
                targetPose,
                activeNetY);
            float pathX = x;
            x = waterRipple ? pathX : Mathf.Lerp(pathX, catchTarget.x, settleIntoMesh01);
            float y = waterRipple
                ? surfaceY
                : Mathf.Lerp(surfaceY, catchTarget.y, settleIntoMesh01);
            if (!waterRipple)
            {
                y += GetHarvestDriftBob(pieceKind, time, pieceMotionIndex) * Mathf.Lerp(1f, 0.22f, settleIntoMesh01);
            }

            float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.16f, staggered01));
            alpha *= 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    TideDriftSourceModel.NetCaptureExitTravel01,
                    TideDriftSourceModel.NearshoreExitTravel01,
                    physicalTravel01));
            if (waterRipple)
            {
                // 尾纹属于水面，不属于网内。实物压进网眼时尾纹在原水面淡出，
                // 禁止跟着货物一起沉到网底形成一条悬空亮线。
                alpha *= 1f - Mathf.SmoothStep(0f, 1f, settleIntoMesh01);
            }
            bool formalCarry = IsUsingFormalHarvestSprite(pieceKind);
            Color carryColor = GetIncomingTideCarryColor(pieceKind);
            Color color = waterRipple
                ? new Color(0.7f, 0.9f, 0.9f, alpha * 0.42f)
                : formalCarry
                    ? new Color(1f, 1f, 1f, alpha)
                    : new Color(carryColor.r, carryColor.g, carryColor.b, carryColor.a * alpha);
            float rotation = waterRipple
                ? Mathf.Atan(carryOcean.Slope) * Mathf.Rad2Deg * 0.2f
                : GetHarvestDriftRotation(pieceKind, time, isExtraSaltWood ? 0 : physicalPieceIndex) * (1f - settleIntoMesh01) +
                  Mathf.Lerp(GetHarvestEntryAngle(pieceKind), targetPose.RotationDegrees, staggered01) +
                  Mathf.Atan(carryOcean.Slope) * Mathf.Rad2Deg * 0.35f * (1f - settleIntoMesh01);
            if (!waterRipple)
            {
                if (TryGetV59FindSpec(
                    pieceKind,
                    isExtraSaltWood ? 0 : physicalPieceIndex,
                    pieceBatchId,
                    true,
                    out TideV59FindSpec driftSpec))
                {
                    Vector2 freeCenter = TideV59FindPresentationModel.GetSpriteCenterForWaterline(
                        new Vector2(pathX, carryOcean.SurfaceY),
                        driftSpec,
                        rotation);
                    freeCenter.y += GetHarvestDriftBob(pieceKind, time, pieceMotionIndex);
                    Vector2 physicalPosition = Vector2.Lerp(freeCenter, catchTarget, settleIntoMesh01);
                    x = physicalPosition.x;
                    y = physicalPosition.y;
                }
                else
                {
                    worldSize *= Mathf.Lerp(1f, targetPose.SizeScale, settleIntoMesh01);
                }
                carryItem.flipX = targetPose.FlipX && settleIntoMesh01 >= 0.5f;
            }
            else
            {
                carryItem.flipX = false;
            }
            SetWorldSize(carryItem, new Vector2(x, y), worldSize, color, rotation);
        }
    }

    private float GetIncomingTideCarryProgress01(float targetY)
    {
        float waterReach01 = Mathf.InverseLerp(lowWaterY + 0.18f, targetY + 0.28f, currentWaterY);
        return Mathf.Clamp01(waterReach01);
    }

    private HarvestKind GetIncomingTideCarryKind()
    {
        HarvestKind incomingKind = tideSourceHarvest != HarvestKind.None
            ? tideSourceHarvest
            : CalculateTideSourceHarvest();

        return incomingKind;
    }

    private bool IsUsingFormalHarvestSprite(HarvestKind harvest)
    {
        return (harvest != HarvestKind.None && HasCompleteV59FindPresentation()) ||
            (harvest == HarvestKind.Fish && GetFormalSprite(ref formalFishSprite, "AIHarvestFish") != null) ||
            (harvest == HarvestKind.Wood && GetFormalSprite(ref formalRepairTimberSprite, "AIRepairTimber") != null) ||
            (harvest == HarvestKind.Relic && GetFormalSprite(ref formalRouteRelicSprite, "AIRouteRelic") != null) ||
            (harvest == HarvestKind.Trash && GetFormalWetDebrisSprite() != null);
    }

    private static Sprite GetIncomingTideCarrySprite(HarvestKind harvest, int index)
    {
        if (index == 1 || index == 4)
        {
            return GetIncomingTideCarryRippleSprite();
        }

        return GetCaughtNetItemSprite(harvest, index);
    }

    private static Color GetIncomingTideCarryColor(HarvestKind harvest)
    {
        if (harvest == HarvestKind.Wood)
        {
            return new Color(0.82f, 0.58f, 0.28f, 0.88f);
        }

        if (harvest == HarvestKind.Relic)
        {
            return new Color(0.95f, 0.78f, 0.42f, 0.9f);
        }

        if (harvest == HarvestKind.Trash)
        {
            return new Color(0.58f, 0.66f, 0.58f, 0.78f);
        }

        return new Color(0.72f, 0.92f, 0.76f, 0.86f);
    }

    private static Vector2 GetIncomingTideCarryScale(HarvestKind harvest, int index)
    {
        if (index == 1 || index == 4)
        {
            return new Vector2(0.68f, 0.15f);
        }

        return GetHarvestWorldSize(harvest, index >= 2 ? 1 : 0);
    }

    private static Vector2 GetHarvestWorldSize(
        HarvestKind harvest,
        int pieceIndex,
        int stableBatchId = 0,
        bool freeDrifting = false)
    {
        if (TryGetV59FindSpec(
            harvest,
            pieceIndex,
            stableBatchId,
            freeDrifting,
            out TideV59FindSpec v59Spec))
        {
            return v59Spec.VisibleWorldSize;
        }

        if (harvest == HarvestKind.Wood)
        {
            return pieceIndex == 0 ? new Vector2(0.58f, 0.24f) : new Vector2(0.46f, 0.18f);
        }

        if (harvest == HarvestKind.Relic)
        {
            if (pieceIndex == 0)
            {
                return new Vector2(0.38f, 0.27f);
            }

            return pieceIndex == 1 ? new Vector2(0.22f, 0.2f) : new Vector2(0.17f, 0.16f);
        }

        if (harvest == HarvestKind.Trash)
        {
            return pieceIndex == 0 ? new Vector2(0.46f, 0.26f) : new Vector2(0.34f, 0.2f);
        }

        return new Vector2(0.52f, 0.21f);
    }

    private static float GetHarvestSurfaceOffset(HarvestKind harvest)
    {
        if (harvest == HarvestKind.Fish)
        {
            return -0.08f;
        }

        if (harvest == HarvestKind.Trash)
        {
            return -0.055f;
        }

        if (harvest == HarvestKind.Wood)
        {
            return -0.025f;
        }

        return -0.035f;
    }

    private static float GetHarvestDriftBob(HarvestKind harvest, float time, int pieceIndex)
    {
        float phase = pieceIndex * 1.37f;
        float materialNoise = (Mathf.PerlinNoise(
            time * 0.34f + 5.1f + (int)harvest * 1.73f,
            pieceIndex * 0.61f + 2.4f) - 0.5f) * 2f;
        if (harvest == HarvestKind.Fish)
        {
            // 鱼的短促摆尾可以周期运动，但浮沉仍由局部浪面和低频扰动共同决定。
            return Mathf.Sin(time * 5.4f + phase) * 0.016f + materialNoise * 0.008f;
        }

        if (harvest == HarvestKind.Wood)
        {
            return materialNoise * 0.006f;
        }

        if (harvest == HarvestKind.Relic)
        {
            return materialNoise * 0.011f;
        }

        return materialNoise * 0.017f;
    }

    private static float GetHarvestDriftSurge(HarvestKind harvest, float time, int pieceIndex)
    {
        float phase = pieceIndex * 0.91f;
        float materialNoise = (Mathf.PerlinNoise(
            time * 0.29f + 12.7f + (int)harvest * 0.83f,
            pieceIndex * 0.74f + 7.6f) - 0.5f) * 2f;
        if (harvest == HarvestKind.Fish)
        {
            return Mathf.Sin(time * 4.8f + phase) * 0.023f + materialNoise * 0.012f;
        }

        return materialNoise * (harvest == HarvestKind.Trash ? 0.021f : harvest == HarvestKind.Relic ? 0.012f : 0.008f);
    }

    private static float GetHarvestDriftRotation(HarvestKind harvest, float time, int pieceIndex)
    {
        float phase = pieceIndex * 1.21f;
        float materialNoise = (Mathf.PerlinNoise(
            time * 0.26f + 19.2f + (int)harvest * 0.57f,
            pieceIndex * 0.92f + 1.8f) - 0.5f) * 2f;
        if (harvest == HarvestKind.Fish)
        {
            return Mathf.Sin(time * 5.1f + phase) * 7f + materialNoise * 2f;
        }

        if (harvest == HarvestKind.Wood)
        {
            return materialNoise * 2.8f;
        }

        if (harvest == HarvestKind.Relic)
        {
            return materialNoise * 4.2f;
        }

        return materialNoise * 6.5f;
    }

    private static float GetHarvestEntryAngle(HarvestKind harvest)
    {
        if (harvest == HarvestKind.Wood)
        {
            return -2f;
        }

        if (harvest == HarvestKind.Fish)
        {
            return 4f;
        }

        return harvest == HarvestKind.Relic ? -7f : 8f;
    }

    private void UpdateNetDamageVisuals(float time, float[] yValues)
    {
        int missingIntegrity = Mathf.Clamp(3 - netIntegrity, 0, netDamageMarkers.Count);
        int visibleDamageCount = netFraying01 > 0.001f
            ? Mathf.Min(netDamageMarkers.Count, Mathf.Max(missingIntegrity + 1, 1))
            : missingIntegrity;
        float netRepairPreview01 = GetUncommittedRepairPreview01(RepairChoice.Net);
        bool storedNet = !netDeployed;
        bool visible = viewMode == SliceViewMode.Shelter && visibleDamageCount > 0 && (netDeployed || storedNet);
        SetEnabled(netDamageMarkers, visible);
        if (!visible)
        {
            return;
        }

        float baseY = storedNet
            ? GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.22f
            : GetSelectedNetY();
        if ((state == SliceState.EbbCollect || state == SliceState.TideRising) && netHaulProgress > 0f)
        {
            baseY = Mathf.Lerp(
                baseY,
                GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.2f,
                GetNetHaulVisualProgress01());
        }
        for (int i = 0; i < netDamageMarkers.Count; i++)
        {
            bool markerVisible = i < visibleDamageCount;
            netDamageMarkers[i].enabled = markerVisible;
            if (!markerVisible)
            {
                continue;
            }

            float spacing = storedNet ? 0.22f : 0.5f;
            float currentShiftX = storedNet ? 0f : GetVisibleNetCurrentShiftX(baseY);
            float x = netAnchor.x + currentShiftX - spacing + i * spacing +
                Mathf.Sin(time * 0.9f + i) * (storedNet ? 0.008f : 0.025f);
            float y = baseY - 0.04f - i * 0.02f;
            bool formalNetDamage = GetFormalNetSprite() != null;
            if (formalNetDamage)
            {
                // Frayed rope fragments replace the old bright repair-patch rectangles.
                // Damage remains readable through missing integrity, torn lines and load,
                // without placing red debug geometry on a photographic net.
                netDamageMarkers[i].sprite = GetNetLineSprite();
            }
            Color color = formalNetDamage
                ? netBrokeThisTide
                    ? new Color(0.5f, 0.38f, 0.25f, 0.9f)
                    : new Color(0.62f, 0.52f, 0.36f, 0.64f)
                : netBrokeThisTide
                    ? new Color(1f, 0.35f, 0.22f, 0.92f)
                    : new Color(0.88f, 0.58f, 0.32f, 0.72f);
            Vector2 markerSize = formalNetDamage
                ? storedNet ? new Vector2(0.18f, 0.035f) : new Vector2(0.34f, 0.045f)
                : storedNet ? new Vector2(0.2f, 0.08f) : new Vector2(0.38f, 0.16f);
            if (i >= missingIntegrity && netFraying01 > 0.001f)
            {
                // The final strand is still attached. Its shrinking, vibrating line
                // communicates a rescue window without adding a health bar or icon.
                float strandPulse = 0.72f + Mathf.Abs(Mathf.Sin(time * 8.4f + i)) * 0.28f;
                color = Color.Lerp(
                    new Color(0.68f, 0.48f, 0.3f, 0.56f),
                    new Color(0.62f, 0.28f, 0.18f, 0.94f),
                    netFraying01);
                color.a *= strandPulse;
                markerSize.x *= Mathf.Lerp(1.3f, 0.72f, netFraying01);
                markerSize.y *= Mathf.Lerp(1f, 1.45f, strandPulse * netFraying01);
                y -= netFraying01 * 0.07f + Mathf.Sin(time * 12.6f + i) * netFraying01 * 0.025f;
            }
            if (i == missingIntegrity - 1 && netRepairPreview01 > 0f)
            {
                float remainingDamage01 = 1f - netRepairPreview01;
                color.a *= remainingDamage01;
                markerSize.x *= Mathf.Lerp(0.18f, 1f, remainingDamage01);
            }
            Set(netDamageMarkers[i], new Vector2(x, y), markerSize, color, -18f + i * 12f);
        }
    }

    private void UpdateCaughtNetVisuals(float time, float activeNetY)
    {
        bool caughtInNet = viewMode == SliceViewMode.Shelter &&
            netTouched &&
            netCatchResolved &&
            currentHarvest != HarvestKind.None &&
            harvestPhysicalState == HarvestPhysicalState.CaughtInNet &&
            (state == SliceState.TideRising || state == SliceState.EbbCollect);
        bool securedAtPost = viewMode == SliceViewMode.Shelter &&
            netSecuredEarly &&
            HasSecuredPostHarvest() &&
            state != SliceState.FinalDeparture;
        bool visible = caughtInNet || securedAtPost;
        SetEnabled(netCaughtItems, visible);
        if (!visible)
        {
            return;
        }

        HarvestKind visibleHarvest = securedAtPost ? securedPostHarvest : currentHarvest;
        float bundleProgress01 = 0f;
        if (securedAtPost)
        {
            bundleProgress01 = 1f;
        }
        // 起网时 V54 仍是一张展开的湿网：玩家先把沉纲提到浮纲，挂物必须
        // 留在各自网眼上。只有完成系桩、切到 HauledWet 网束后才把它们收拢；
        // 提前向岸边网团插值会让货物脱离当前可见网体，违反所见即所得。
        float attachedLoad01 = caughtInNet && state == SliceState.TideRising && netDeployed
            ? Mathf.Max(GetNaturalTideHeight01(), netPeakTension01)
            : 0f;
        Vector2 bundledCenter = formalNetBundleRenderer != null && formalNetBundleRenderer.enabled
            ? (Vector2)formalNetBundleRenderer.bounds.center + new Vector2(0.03f, -0.015f)
            : new Vector2(
                GetNetFirstStakeX() + 0.12f,
                GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.18f);
        float repairFade = securedAtPost ? 1f : state == SliceState.RepairMoment ? 0.58f : 1f;
        int physicalPieceCount = securedAtPost ? securedPostVisualPieceCount : netCatchVisualPieceCount;
        int visibleItemCount = securedAtPost
            ? GetSecuredPostHarvestVisiblePieceCount(physicalPieceCount, false)
            : GetCurrentHarvestVisiblePieceCount(physicalPieceCount, false);

        for (int i = 0; i < netCaughtItems.Count; i++)
        {
            SpriteRenderer caughtItem = netCaughtItems[i];
            caughtItem.enabled = visible && i < visibleItemCount;
            if (!caughtItem.enabled)
            {
                continue;
            }
            HarvestKind pieceKind = securedAtPost
                ? GetSecuredPostHarvestPieceKind(i, visibleItemCount)
                : GetCurrentHarvestPieceKind(i, visibleItemCount);
            int pieceBatchId = securedAtPost
                ? GetSecuredPostHarvestPieceBatchId(i, visibleItemCount)
                : GetCurrentHarvestPieceBatchId(i, visibleItemCount);
            int pieceVisualIndex = securedAtPost
                ? GetSecuredPostHarvestPieceVisualIndex(i, visibleItemCount)
                : GetCurrentHarvestPieceVisualIndex(i, visibleItemCount);
            caughtItem.sprite = GetCaughtNetItemSprite(pieceKind, pieceVisualIndex, pieceBatchId);
            TideNetCatchMaterial material = ToNetCatchMaterial(pieceKind);
            TideNetCatchPresentationModel.Pose netPose =
                TideNetCatchPresentationModel.GetInNetPose(material, i, visibleItemCount);
            Vector2 attachedPosition = GetCaughtNetWorldPosition(netPose, activeNetY);
            Vector2 bundledPosition = bundledCenter +
                TideNetCatchPresentationModel.GetBundledOffset(material, i, visibleItemCount);
            Vector2 motion = TideNetCatchPresentationModel.EvaluateLoadMotion(
                material,
                time,
                i,
                attachedLoad01,
                bundleProgress01);
            Vector2 position = Vector2.Lerp(attachedPosition, bundledPosition, bundleProgress01) + motion;
            Vector2 itemWorldSize = GetHarvestWorldSize(pieceKind, pieceVisualIndex, pieceBatchId);
            if (!HasCompleteV59FindPresentation())
            {
                itemWorldSize *= Mathf.Lerp(netPose.SizeScale, 0.72f, bundleProgress01);
            }
            float rotation = Mathf.Lerp(
                netPose.RotationDegrees,
                TideNetCatchPresentationModel.GetBundledRotation(material, i),
                bundleProgress01);
            Color pieceColor = IsUsingFormalHarvestSprite(pieceKind)
                ? Color.white
                : pieceKind == visibleHarvest
                    ? GetCaughtNetItemColor(visibleHarvest)
                    : GetIncomingTideCarryColor(pieceKind);
            Color color = new Color(pieceColor.r, pieceColor.g, pieceColor.b, pieceColor.a * repairFade);
            caughtItem.sortingOrder = formalNetBundleRenderer != null && formalNetBundleRenderer.enabled
                ? formalNetBundleRenderer.sortingOrder + 1
                : formalNetRenderer.sortingOrder + 1;
            caughtItem.flipX = netPose.FlipX && bundleProgress01 < 0.65f;
            SetWorldSize(caughtItem, position, itemWorldSize, color, rotation);
        }

        UpdateWashedAwayHarvestVisuals(time, activeNetY);
    }

    private Vector2 GetCaughtNetWorldPosition(
        TideNetCatchPresentationModel.Pose pose,
        float activeNetY)
    {
        if (v54DeployedPoseValid)
        {
            return TideV54NetPresentationModel.EvaluateWorldAnchor(
                v54DeployedPosition,
                v54DeployedWorldSize,
                pose.AnchorTopLeft01);
        }

        // 旧网资源的兜底也遵守同一浮纲到沉纲比例，避免资源缺失时重新退回
        // “activeNetY 下方一排图标”的旧表现。
        float head01 = TideV54NetPresentationModel.GetLeftAttachmentTopLeft01(
            TideV54NetVisualState.DeployedDry).y;
        float sink01 = TideV54NetPresentationModel.GetSinkCenterTopLeft01(
            TideV54NetVisualState.DeployedDry).y;
        float vertical01 = Mathf.InverseLerp(head01, sink01, pose.AnchorTopLeft01.y);
        return new Vector2(
            netAnchor.x + GetVisibleNetCurrentShiftX(activeNetY) +
                (pose.AnchorTopLeft01.x - 0.5f) * 1.55f,
            Mathf.Lerp(GetNetHeadLineY(), activeNetY, vertical01));
    }

    private static TideNetCatchMaterial ToNetCatchMaterial(HarvestKind harvest)
    {
        switch (harvest)
        {
            case HarvestKind.Wood:
                return TideNetCatchMaterial.Wood;
            case HarvestKind.Relic:
                return TideNetCatchMaterial.Relic;
            case HarvestKind.Trash:
                return TideNetCatchMaterial.Debris;
            default:
                return TideNetCatchMaterial.Fish;
        }
    }

    private bool IsUsingFormalCaughtItem()
    {
        return IsUsingFormalHarvestSprite(currentHarvest);
    }

    private void UpdateWashedAwayHarvestVisuals(float time, float activeNetY)
    {
        bool visible = washedAwayHarvestTimer > 0f &&
            washedAwayHarvestKind != HarvestKind.None &&
            washedAwayHarvestPieceCount > 0;
        SetEnabled(washedAwayHarvestItems, visible);
        if (!visible)
        {
            return;
        }

        float scatter01 = 1f - Mathf.Clamp01(washedAwayHarvestTimer / 2.4f);
        for (int i = 0; i < washedAwayHarvestItems.Count; i++)
        {
            SpriteRenderer item = washedAwayHarvestItems[i];
            item.enabled = i < washedAwayHarvestPieceCount;
            if (!item.enabled)
            {
                continue;
            }

            item.sprite = GetCaughtNetItemSprite(
                washedAwayHarvestKind,
                i,
                washedAwayHarvestBatchId);
            item.sortingOrder = 17;
            Vector2 size = GetHarvestWorldSize(
                washedAwayHarvestKind,
                i,
                washedAwayHarvestBatchId);
            if (washedAwayHarvestKind == HarvestKind.Wood && !HasCompleteV59FindPresentation())
            {
                size *= 1.22f;
            }
            float startX = netAnchor.x + (i - (washedAwayHarvestPieceCount - 1) * 0.5f) * 0.26f;
            float pieceDriftScale = Mathf.Max(0.42f, 1f - i * 0.24f);
            if (washedAwayHarvestKind == HarvestKind.Fish)
            {
                pieceDriftScale += 0.16f;
            }
            float x = startX + washedAwayHarvestDriftX * pieceDriftScale;
            TideOceanSample washedOcean = GetOceanSample(x);
            float fadeOut01 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 1f, scatter01));
            float alpha = 1f - fadeOut01;
            Color color = IsUsingFormalHarvestSprite(washedAwayHarvestKind)
                    ? new Color(1f, 1f, 1f, alpha)
                    : new Color(0.72f, 0.76f, 0.69f, alpha);
            float rotation = (i % 2 == 0 ? -1f : 1f) * scatter01 * (34f + i * 7f) +
                Mathf.Sin(time * 2.2f + i) * 4f +
                Mathf.Atan(washedOcean.Slope) * Mathf.Rad2Deg * 0.35f;
            float y;
            if (TryGetV59FindSpec(
                washedAwayHarvestKind,
                i,
                washedAwayHarvestBatchId,
                false,
                out TideV59FindSpec washedSpec))
            {
                Vector2 center = TideV59FindPresentationModel.GetSpriteCenterForWaterline(
                    new Vector2(x, washedOcean.SurfaceY),
                    washedSpec,
                    rotation);
                float sinkingDepth = washedSpec.CanFreeFloat
                    ? 0f
                    : Mathf.SmoothStep(0f, 1f, scatter01) * 0.42f;
                y = center.y + GetHarvestDriftBob(washedAwayHarvestKind, time, i) +
                    Mathf.Sin(scatter01 * Mathf.PI) * 0.035f - sinkingDepth;
            }
            else
            {
                y = washedOcean.SurfaceY + GetHarvestSurfaceOffset(washedAwayHarvestKind) +
                    GetHarvestDriftBob(washedAwayHarvestKind, time, i) +
                    Mathf.Sin(scatter01 * Mathf.PI) * 0.035f;
            }
            SetWorldSize(item, new Vector2(x, y), size, color, rotation);
        }
    }

    private void UpdateBoatLandingGangplank(float pierTipX, float pierSurfaceY, Color pierColor)
    {
        bool boatStillMoored = viewMode == SliceViewMode.Shelter &&
            state != SliceState.FinalDeparture &&
            !sailTripActive;
        if (!boatStillMoored)
        {
            SetEnabled(boatLandingWalkwayRenderer, false);
            return;
        }

        Vector2 pierTip = new Vector2(pierTipX, pierSurfaceY);
        Vector2 sternFoot = GetMooredBoatSternStepPosition() -
            Vector2.up * (TideV20CharacterPresentationModel.BodyWorldLength * 0.5f);
        Vector2 bridge = sternFoot - pierTip;
        float length = bridge.magnitude;
        if (length <= 0.02f || length > 2.4f)
        {
            SetEnabled(boatLandingWalkwayRenderer, false);
            return;
        }

        // 固定长栈桥不随船伸缩；这一块窄跳板才负责吸收潮流横移、吃水升降和滚转。
        boatLandingWalkwayRenderer.sprite = GetMooringGangplankSprite();
        boatLandingWalkwayRenderer.sortingOrder = 6;
        Color gangplankColor = Color.Lerp(pierColor, Color.white, 0.08f);
        gangplankColor.a = Mathf.Max(0.82f, gangplankColor.a);
        SetWorldSize(
            boatLandingWalkwayRenderer,
            (pierTip + sternFoot) * 0.5f,
            new Vector2(length + 0.12f, 0.16f),
            gangplankColor,
            Mathf.Atan2(bridge.y, bridge.x) * Mathf.Rad2Deg);
    }

    private void HideMooredBoatPresentation()
    {
        SetEnabled(boatGlowRenderer, false);
        SetEnabled(boatBackRigRenderer, false);
        SetEnabled(boatHullRenderer, false);
        SetEnabled(boatSailRenderer, false);
        SetEnabled(boatLandingWalkwayRenderer, false);
        SetEnabled(boatWakeRenderer, false);
        SetEnabled(boatWaterlineOcclusionRenderer, false);
        SetEnabled(boatPassengerGunwaleRenderer, false);
        SetEnabled(boatRudderRenderer, false);
        SetEnabled(boatCockpitRenderer, false);
        SetEnabled(boatPassengerRenderer, false);
        SetEnabled(boatIngressWaterRenderer, false);
        SetEnabled(boatRigging, false);
        SetEnabled(boatPatchStitches, false);
        SetEnabled(mooringRopeSegments, false);
        SetEnabled(mooringRopeEndRenderer, false);
        SetMaskEnabled(boatWaterlineMask, false);
        SetMaskEnabled(boatPassengerBagMask, false);
    }

    private void UpdateBoatVisuals(float time)
    {
        // 船是连续世界中的实体，不再由“泊位屏是否激活”决定存在。初始镜头
        // 看不到它只是因为它确实停在远处；向右走时相机和木路会自然带到船艉。
        if (viewMode != SliceViewMode.Shelter && state != SliceState.FinalDeparture)
        {
            HideMooredBoatPresentation();
            return;
        }

        float bob = Mathf.Sin(time * 1.4f) * 0.035f;
        Vector2 mooredBase = GetMooredBoatPosition();
        Vector2 tripOffset = GetBoatTripOffset();
        Sprite formalBoatVisual = boatSailIntegrity <= 0
            ? GetFormalDamagedBoatSprite() ?? GetFormalBoatSprite()
            : GetFormalBoatSprite();
        TideOceanSample mooredOcean = GetOceanSample(mooredBase.x + tripOffset.x);
        // There is no authored shoal or seabed under the mooring. Treating boatAnchor.y
        // as an invisible floor made low tide continue downward while the hull stayed
        // suspended in air. Until a visible grounding surface exists, the only honest
        // waterline is the local continuous-ocean sample at the moving hull.
        float visibleWaterlineY = mooredOcean.SurfaceY;
        float boarding01 = boatViewTransition == BoatViewTransition.Boarding
            ? Mathf.Clamp01(boatViewTransitionTimer / Mathf.Max(0.2f, boatViewTransitionSeconds))
            : 0f;
        // The complete seated body appears only under the cut. Showing it earlier
        // duplicates the survivor with the standing renderer on the stern.
        bool showMooredPassenger = boarding01 >= 0.46f;
        bool boatLoaded = returnedSalvageAtBoat || returnedClueAtBoat ||
            extraSaltWoodOwner == ExtraSaltWoodOwner.ReturnedAtBoat;
        Color formalBoatTint = Color.Lerp(
            new Color(0.78f, 0.84f, 0.84f, 1f),
            Color.white,
            GetDaylight01());
        bool useContractBoat = ApplyBoatPresentation(
            new Vector2(mooredBase.x + tripOffset.x, visibleWaterlineY),
            mooredOcean.Slope * 4.5f,
            time,
            showMooredPassenger,
            boatLoaded,
            GetStormPressure01() >= 0.58f,
            formalBoatTint,
            out Vector2 v31Root);
        bool useFormalBoat = useContractBoat || formalBoatVisual != null;
        float boatGlowAlpha = sailTripActive ? 0.28f : Mathf.Clamp01(0.08f + boatReadiness * 0.035f);
        SetEnabled(boatGlowRenderer, !useFormalBoat);
        if (!useFormalBoat)
        {
            SetWorldSize(
                boatGlowRenderer,
                new Vector2(mooredBase.x + tripOffset.x, visibleWaterlineY + 0.08f),
                new Vector2(1.28f, 0.32f),
                new Color(0.86f, 0.72f, 0.42f, boatGlowAlpha),
                0f);
        }
        if (!useContractBoat && formalBoatVisual != null)
        {
            SetEnabled(boatBackRigRenderer, false);
            boatHullRenderer.sprite = formalBoatVisual;
            Vector2 formalSize = GetAspectPreservingWorldSize(formalBoatVisual, 2.08f);
            SetWorldSize(
                boatHullRenderer,
                mooredBase + tripOffset + new Vector2(0f, 0.56f + bob),
                formalSize,
                formalBoatTint,
                0f);
            SetEnabled(boatSailRenderer, false);
            SetEnabled(boatRigging, false);
            SetEnabled(boatCockpitRenderer, false);
            SetEnabled(boatPassengerGunwaleRenderer, false);
            SetEnabled(boatRudderRenderer, false);
            SetEnabled(boatPassengerRenderer, false);
        }
        else if (!useContractBoat)
        {
            SetEnabled(boatBackRigRenderer, false);
            SetWorldSize(boatHullRenderer, mooredBase + tripOffset + new Vector2(0f, bob), new Vector2(1.18f + boatReadiness * 0.08f, 0.38f), new Color(0.62f, 0.39f, 0.23f, 1f), 0f);
            SetWorldSize(boatSailRenderer, mooredBase + tripOffset + new Vector2(0.22f, 0.48f + bob), new Vector2(0.66f + boatReadiness * 0.04f, 0.82f), new Color(0.9f, 0.85f, 0.68f, 0.96f), -3f);
        }
        // 泊位没有航速，因此不存在船尾迹。船体浸水由整片外景海面负责，
        // 禁止回退资源重新生成一块跟船移动的局部水贴片。
        SetEnabled(boatWakeRenderer, false);
        SetEnabled(boatWaterlineOcclusionRenderer, false);

        // V31 正式船已经拥有帆桅和索具。仅跳过旧循环会保留 EnsureScene
        // 创建时的默认启用状态，必须显式关闭，避免三条程序横线悬在屋外。
        if (useFormalBoat)
        {
            SetEnabled(boatRigging, false);
        }

        for (int i = 0; i < boatRigging.Count && !useFormalBoat; i++)
        {
            float ropeBob = bob + Mathf.Sin(time * 1.1f + i) * 0.012f;
            Vector2 offset = i == 0 ? new Vector2(0.03f, 0.52f) : i == 1 ? new Vector2(0.42f, 0.38f) : new Vector2(-0.18f, 0.24f);
            Vector2 scale = i == 0 ? new Vector2(0.68f, 0.05f) : new Vector2(0.5f, 0.045f);
            float rotation = i == 0 ? 57f : i == 1 ? -42f : 12f;
            Set(boatRigging[i], mooredBase + tripOffset + offset + new Vector2(0f, ropeBob), scale, new Color(0.65f, 0.68f, 0.55f, 0.86f), rotation);
        }

        for (int i = 0; i < boatPatchStitches.Count; i++)
        {
            bool hullPatch = (i == 0 && boatHullIntegrity >= 2) || (i == 1 && boatHullIntegrity >= 3);
            bool sailPatch = (i == 2 && boatSailIntegrity >= 1) || (i == 3 && boatSailIntegrity >= 2);
            bool cabinPatch = i == 4 && boatCabinIntegrity >= 1;
            // The authored boat sprites already contain believable stitched sail and
            // hull wear. Extra generated patches float because their perspective and
            // pivot do not match the painted hull, so component progress is expressed
            // through damaged/repaired variants and tint until dedicated cutouts exist.
            bool visible = !useFormalBoat && (hullPatch || sailPatch || cabinPatch);
            boatPatchStitches[i].enabled = visible;
            if (visible)
            {
                bool newlyRepaired = state == SliceState.RepairMoment &&
                    repairChoiceApplied &&
                    ((hullPatch && pendingRepairChoice == RepairChoice.Hull) ||
                     (sailPatch && pendingRepairChoice == RepairChoice.Sail) ||
                     (cabinPatch && pendingRepairChoice == RepairChoice.Cabin));
                float reveal01 = newlyRepaired ? GetRepairReveal01() : 1f;
                if (useFormalBoat)
                {
                    Vector2 patchOffset;
                    Vector2 patchSize;
                    if (hullPatch)
                    {
                        boatPatchStitches[i].sprite = GetFormalSprite(ref formalRepairTimberSprite, "AIRepairTimber") ?? GetRepairPatchSprite();
                        patchOffset = i == 0 ? new Vector2(-0.34f, -0.03f) : new Vector2(0.08f, -0.08f);
                        patchSize = new Vector2(0.27f, 0.1f);
                    }
                    else if (sailPatch)
                    {
                        boatPatchStitches[i].sprite = GetBoatSailPatchStitchSprite();
                        patchOffset = i == 2 ? new Vector2(-0.12f, 0.72f) : new Vector2(0.08f, 0.92f);
                        patchSize = new Vector2(0.2f, 0.08f);
                    }
                    else
                    {
                        boatPatchStitches[i].sprite = GetFormalSprite(ref formalRepairTimberSprite, "AIRepairTimber") ?? GetRepairPatchSprite();
                        patchOffset = new Vector2(0.26f, 0.08f);
                        patchSize = new Vector2(0.36f, 0.12f);
                    }

                    patchSize.x *= Mathf.Lerp(0.2f, 1f, reveal01);
                    SetWorldSize(
                        boatPatchStitches[i],
                        mooredBase + tripOffset + patchOffset + new Vector2(0f, bob),
                        patchSize,
                        new Color(1f, 1f, 1f, Mathf.Lerp(0.22f, 1f, reveal01)),
                        hullPatch ? -4f + i * 3f : sailPatch ? -18f + i * 5f : 0f);
                }
                else
                {
                    Vector2 offset = new Vector2(0.31f + (i % 2) * 0.11f, 0.58f - i * 0.055f);
                    Vector2 patchSize = new Vector2(0.13f * Mathf.Lerp(0.2f, 1f, reveal01), 0.055f);
                    Set(boatPatchStitches[i], mooredBase + tripOffset + offset + new Vector2(0f, bob), patchSize, new Color(0.78f, 0.62f, 0.38f, 0.2f + reveal01 * 0.72f), -18f + i * 7f);
                }
            }
        }

        UpdateMooringRopeVisuals();
    }

    private void UpdateMooringRopeVisuals()
    {
        Vector2 playerHand = playerPosition + new Vector2(playerFacing * 0.2f, 0.14f);
        Vector2 securedDockPoint = new Vector2(
            GetBoatBoardingX() - 0.1f,
            GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.12f);
        Vector2 boatTie = GetMooredBoatPosition() + new Vector2(-0.48f, 0.24f);
        mooringRope.UpdatePresentation(
            mooringRopeSegments,
            mooringRopeEndRenderer,
            viewMode == SliceViewMode.Shelter && state != SliceState.FinalDeparture,
            playerHand,
            securedDockPoint,
            boatTie,
            GetNetWeightSprite(),
            visualZ);
    }

    private void SetThinRopeSegment(SpriteRenderer renderer, Vector2 start, Vector2 end)
    {
        Vector2 delta = end - start;
        SetWorldSize(
            renderer,
            (start + end) * 0.5f,
            new Vector2(Mathf.Max(0.01f, delta.magnitude), 0.026f),
            new Color(0.42f, 0.35f, 0.25f, 0.92f),
            Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }

    private Vector2 GetMooredBoatPosition()
    {
        float x = boatAnchor.x + mooredBoatOffsetX;
        TideOceanSample ocean = GetOceanSample(x);
        return new Vector2(x, ocean.SurfaceY);
    }

    private Vector2 GetRestingMooredBoatSternFootPosition()
    {
        if (HasCompleteV39BoatPresentation())
        {
            Vector2 stern = TideV39BoatPresentationModel.EvaluateAnchorWorldPosition(
                new Vector2(boatAnchor.x, 0f),
                TideV39BoatPresentationModel.SternStepTopLeft,
                0f,
                FormalBoatFacesRight);
            return new Vector2(stern.x, GetPlayerStandingFeetY(WalkLane.TideFlat));
        }

        EnsureV31BoatResourcesLoaded();
        if (HasCompleteV31BoatPresentation())
        {
            // 固定栈桥只需要静水船姿的水平锚点；Y 由木路自身脚面拥有。
            Vector2 stern = TideV31BoatPresentationModel.EvaluateAnchorPoseWorldPosition(
                new Vector2(boatAnchor.x, 0f),
                formalBoatV31Catalog.Anchors.BoardingSternTopLeft,
                formalBoatV31Catalog,
                0f,
                FormalBoatFacesRight);
            return new Vector2(stern.x, GetPlayerStandingFeetY(WalkLane.TideFlat));
        }

        return new Vector2(boatAnchor.x - 0.62f, GetPlayerStandingFeetY(WalkLane.TideFlat));
    }

    private Vector2 GetMooredBoatSternStepPosition()
    {
        Vector2 mooredPosition = GetMooredBoatPosition() + GetBoatTripOffset();
        if (HasCompleteV39BoatPresentation())
        {
            TideOceanSample ocean = GetOceanSample(mooredPosition.x);
            Vector2 root = new Vector2(
                mooredPosition.x,
                TideV39BoatPresentationModel.EvaluateBoatRootY(ocean.SurfaceY));
            Vector2 footAnchor = TideV39BoatPresentationModel.EvaluateAnchorWorldPosition(
                root,
                TideV39BoatPresentationModel.SternStepTopLeft,
                ocean.Slope * 4.5f,
                FormalBoatFacesRight);
            return footAnchor + Vector2.up *
                (TideV20CharacterPresentationModel.BodyWorldLength * 0.5f);
        }

        EnsureV31BoatResourcesLoaded();
        if (HasCompleteV31BoatPresentation())
        {
            TideOceanSample ocean = GetOceanSample(mooredPosition.x);
            float rootY = TideV31BoatPresentationModel.EvaluateBoatRootY(
                ocean.SurfaceY,
                formalBoatV31Catalog,
                false,
                GetStormPressure01() >= 0.58f);
            float rotationZ = ocean.Slope * 4.5f;
            Vector2 footAnchor = TideV31BoatPresentationModel.EvaluateAnchorPoseWorldPosition(
                new Vector2(mooredPosition.x, rootY),
                formalBoatV31Catalog.Anchors.BoardingSternTopLeft,
                formalBoatV31Catalog,
                rotationZ,
                FormalBoatFacesRight);
            // playerPosition 表示站立身体中心；把正式船艉锚点当脚底，再抬半个身体长度。
            return footAnchor + Vector2.up *
                (TideV20CharacterPresentationModel.BodyWorldLength * 0.5f);
        }

        return new Vector2(mooredPosition.x - 0.62f, GetPlayerLaneY(WalkLane.TideFlat));
    }

    private void UpdateBoatWaterlineOcclusion(float boatX, float waterlineY, float width, bool visible)
    {
        // 旧版用一张船宽海浪遮船底，浪形与全局海面相位无关，视觉上像船自带
        // 一滩水。接口暂时保留以兼容旧场景序列化和探针，但表现所有权已退休。
        SetMaskEnabled(boatWaterlineMask, false);
        if (boatWaterlineOcclusionRenderer != null)
        {
            boatWaterlineOcclusionRenderer.maskInteraction = SpriteMaskInteraction.None;
        }
        SetEnabled(boatWaterlineOcclusionRenderer, false);
    }

    private void UpdateSkyVisuals(float time, float storm01)
    {
        float phase01 = Mathf.Repeat(moonAgeDays / 29.53f, 1f);
        float illumination01 = GetMoonIllumination01(phase01);
        float daylight01 = GetDaylight01();
        float night01 = 1f - daylight01;
        float shelterViewCenterX = GetActiveCameraCenterX();
        float shelterWind01 = Mathf.Clamp01(
            Mathf.Abs(GetNaturalSailingWindSpeed()) / Mathf.Max(0.01f, sailingWindMaxSpeed));
        float shelterWaveDirection = GetNaturalCurrentSpeed() +
            GetNaturalSailingWindSpeed() * 0.35f;
        TideOceanSample shelterCenterOcean = GetOceanSample(shelterViewCenterX);
        bool useFormalMoon = GetFormalSprite(ref formalMoonSprite, "AIMoon") != null;
        Vector2 moonScale = useFormalMoon ? new Vector2(0.92f, 0.92f) : new Vector2(0.76f, 0.76f);
        Color moonColor = useFormalMoon
            ? new Color(1f, 1f, 1f, 0.04f + night01 * 0.92f)
            : new Color(0.76f, 0.82f, 0.78f, 0.16f + night01 * 0.78f);
        SetWorldSize(moonRenderer, moonAnchor, moonScale, moonColor, 0f);

        // 月相暗面只做柔和遮罩，不是黑色遮挡块；月面纹理仍要能透出来。
        moonShadowRenderer.sprite = GetFirstSliceMoonPhaseShadowSprite(phase01, illumination01);
        SetWorldSize(moonShadowRenderer, moonAnchor, moonScale, new Color(0.006f, 0.026f, 0.03f, 0.14f + night01 * 0.78f), 0f);

        // The former sun reused the player's ring halo and looked like an editor
        // marker. Daylight now comes from sky color until a matching sun asset exists.
        SetEnabled(sunRenderer, false);
        float clue01 = Mathf.Clamp01(lighthouseClues / Mathf.Max(1f, requiredLighthouseClues));
        Vector2 lighthouseVisualAnchor = viewMode == SliceViewMode.Sailing
            ? GetSailingScreenPosition(new Vector2(sailingTargetX, lighthouseAnchor.y))
            : lighthouseAnchor;
        bool lighthouseWithinView = viewMode != SliceViewMode.Sailing ||
            (lighthouseVisualAnchor.x >= -7.25f && lighthouseVisualAnchor.x <= 7.25f);
        TideLighthouseVisibilitySample lighthouseVisibility = GetLighthouseVisibilitySample();
        ApplyLighthouseFogToBroadWeatherBands(lighthouseVisibility);
        if (lighthouseVisibility.ShowsLighthouse)
        {
            EnsureV44LookoutVistaResourcesLoaded();
            bool useV44Lighthouse = HasCompleteV44LookoutVistaPresentation();
            Sprite visibleLighthouse = useV44Lighthouse
                ? formalV44LookoutVistaCatalog.FarLighthouse
                : GetFormalSprite(ref formalLighthouseSprite, "AILighthouse");
            SetEnabled(lighthouseRenderer, lighthouseWithinView);
            SetEnabled(lighthouseBeamRenderer, lighthouseWithinView && lighthouseVisibility.ShowsBeam);
            if (lighthouseWithinView)
            {
                if (useV44Lighthouse)
                {
                    const float lighthouseScale = 0.19f;
                    SetUniformSprite(
                        lighthouseRenderer,
                        visibleLighthouse,
                        lighthouseVisualAnchor,
                        lighthouseScale,
                        new Color(0.92f, 0.94f, 0.9f, lighthouseVisibility.LighthouseAlpha),
                        0f,
                        false);
                    Sprite beamFrame = TideV44LookoutVistaPresentationModel.EvaluateBeamFrame(
                        formalV44LookoutVistaCatalog,
                        time);
                    if (lighthouseVisibility.ShowsBeam && beamFrame != null)
                    {
                        Vector2 lens = TideV44LookoutVistaPresentationModel.EvaluateLighthouseLensPosition(
                            lighthouseVisualAnchor,
                            lighthouseScale);
                        SetUniformSprite(
                            lighthouseBeamRenderer,
                            beamFrame,
                            lens,
                            0.55f,
                            new Color(1f, 0.9f, 0.66f, lighthouseVisibility.BeamAlpha),
                            Mathf.Sin(time * 0.22f) * 8f,
                            true);
                    }
                }
                else
                {
                    SetWorldSize(
                        lighthouseRenderer,
                        lighthouseVisualAnchor + new Vector2(0f, 0.26f),
                        new Vector2(0.78f, 1.7f),
                        new Color(1f, 1f, 1f, lighthouseVisibility.LighthouseAlpha),
                        0f);
                    if (lighthouseVisibility.ShowsBeam)
                    {
                        Set(
                        lighthouseBeamRenderer,
                        lighthouseVisualAnchor + new Vector2(-0.52f, 0.53f),
                        new Vector2(1.15f + clue01 * 0.45f, 0.22f),
                        new Color(1f, 0.83f, 0.42f, lighthouseVisibility.BeamAlpha),
                        -8f);
                    }
                }
            }
        }
        else
        {
            SetEnabled(lighthouseRenderer, false);
            SetEnabled(lighthouseBeamRenderer, false);
        }
        SetEnabled(stormLineRenderer, false);

        for (int i = 0; i < waveStrips.Count; i++)
        {
            // Sailing owns these same renderers in UpdateSailingVisuals. Do not let
            // the later sky pass erase that local-water result. Interior, however,
            // has no exposed exterior sea strip and must explicitly hide them.
            if (viewMode == SliceViewMode.Sailing)
            {
                continue;
            }

            if (viewMode != SliceViewMode.Shelter)
            {
                SetEnabled(waveStrips[i], false);
                continue;
            }

            // V56 owns one connected water body. V43 only shows intermittent local
            // formation and breaking events from the world-space field below.
            TideWaveEventSample waveEvent = TideWaveEventFieldModel.Sample(
                i,
                waveStrips.Count,
                shelterViewCenterX,
                time,
                shelterWaveDirection,
                shelterWind01,
                storm01,
                shelterCenterOcean.Agitation01);
            if (!waveEvent.Visible)
            {
                SetEnabled(waveStrips[i], false);
                continue;
            }

            float x = waveEvent.WorldX;
            TideOceanSample stripOcean = GetOceanSample(x);
            SpriteRenderer crest = waveStrips[i];
            EnsureV43SeaWeatherResourcesLoaded();
            bool useV43Crest = HasCompleteV43SeaWeatherPresentation() &&
                GetFormalWaterSurfaceSprite() != null;
            TideV43WaveKind v43Kind = ResolveV43WaveKind(waveEvent.Kind);
            Sprite v43Frame = useV43Crest
                ? TideV43SeaWeatherPresentationModel.EvaluateWaveFrame(
                    formalV43SeaWeatherCatalog,
                    v43Kind,
                    time,
                    waveEvent.FramePhase01,
                    waveEvent.FrameSpeedScale)
                : null;
            useV43Crest = v43Frame != null;
            Sprite formalCrest = GetFormalSeaCurrentCrestSprite();
            bool useFormalCrest = !useV43Crest && formalCrest != null && GetFormalWaterSurfaceSprite() != null;
            crest.sprite = useV43Crest ? v43Frame : useFormalCrest ? formalCrest : GetFoamSprite();
            crest.sortingOrder = useV43Crest || useFormalCrest
                ? naturalWaterSurfaceRenderer.sortingOrder + 1
                : -17;
            float y = stripOcean.SurfaceY + (useV43Crest ? 0f : 0.025f);
            Vector2 v43Size = Vector2.Scale(
                TideV43SeaWeatherPresentationModel.GetWaveWorldSize(v43Kind),
                new Vector2(waveEvent.WidthScale, waveEvent.HeightScale));
            Color waveTint = useV43Crest
                ? Color.Lerp(
                    new Color(0.88f, 0.98f, 0.95f, 0.42f + tideStrength * 0.14f),
                    new Color(0.66f, 0.78f, 0.8f, 0.62f),
                    storm01 * 0.58f)
                : new Color(
                    0.76f,
                    0.94f,
                    0.9f,
                    0.26f + tideStrength * 0.2f + storm01 * 0.12f);
            waveTint.a *= waveEvent.Opacity01;
            SetWorldSize(
                crest,
                new Vector2(x, y),
                useV43Crest
                    ? v43Size
                    : useFormalCrest
                    ? new Vector2(1.62f + storm01 * 0.34f, 0.19f + storm01 * 0.07f)
                    : new Vector2(1.35f + storm01 * 0.3f, 0.12f + storm01 * 0.06f),
                waveTint,
                Mathf.Atan(stripOcean.Slope) * Mathf.Rad2Deg *
                    (useV43Crest ? 0.58f : useFormalCrest ? 0.42f : 0.18f));
        }
    }

    private void UpdateWaterDetailVisuals(float time)
    {
        if (GetFormalWaterSurfaceSprite() != null)
        {
            // The HD water body already contains foam and readable wave structure.
            // Old procedural flecks only add noise and make the water look layered.
            SetEnabled(waterWracks, false);
            return;
        }

        for (int i = 0; i < waterWracks.Count; i++)
        {
            float x = -5.1f + i * 1.55f + Mathf.Sin(time * 0.17f + i * 0.8f) * 0.18f;
            TideOceanSample wrackOcean = GetOceanSample(x);
            float y = wrackOcean.SurfaceY - 0.01f;
            Color color = i % 3 == 0 ? new Color(0.58f, 0.38f, 0.21f, 0.52f) : new Color(0.62f, 0.68f, 0.56f, 0.42f);
            Set(waterWracks[i], new Vector2(x, y), new Vector2(0.22f + (i % 2) * 0.08f, 0.07f), color, Mathf.Atan(wrackOcean.Slope) * Mathf.Rad2Deg * 0.5f);
        }
    }

    private TideV43WaveKind ResolveV43WaveKind(int stripIndex, float storm01)
    {
        if (storm01 >= 0.68f || (storm01 >= 0.42f && stripIndex % 2 == 1))
        {
            return TideV43WaveKind.StormBreaker;
        }

        float wind01 = Mathf.Clamp01(
            Mathf.Abs(GetNaturalSailingWindSpeed()) / Mathf.Max(0.01f, sailingWindMaxSpeed));
        return wind01 >= 0.42f || stripIndex % 3 == 1
            ? TideV43WaveKind.WindWave
            : TideV43WaveKind.LongSwell;
    }

    private static TideV43WaveKind ResolveV43WaveKind(TideWaveEventKind eventKind)
    {
        if (eventKind == TideWaveEventKind.StormBreaker)
        {
            return TideV43WaveKind.StormBreaker;
        }

        return eventKind == TideWaveEventKind.WindWave
            ? TideV43WaveKind.WindWave
            : TideV43WaveKind.LongSwell;
    }

    private static float GetStableVisualVariation01(int index, int salt)
    {
        // 不调用 UnityEngine.Random，避免一次截屏或重进场景就改变海况。
        // 不同 salt 分别控制相位、速度和尺寸，使五段浪不再同步复制。
        float value = Mathf.Sin((index + 1) * 12.9898f + salt * 0.781f) * 43758.5453f;
        return Mathf.Repeat(value, 1f);
    }

    private void UpdatePayoffVisuals(float time, float storm01)
    {
        for (int i = 0; i < shelterWarmRays.Count; i++)
        {
            bool visible = houseWarmth > i;
            shelterWarmRays[i].enabled = visible;
            if (visible)
            {
                bool newlyLit = state == SliceState.RepairMoment &&
                    repairChoiceApplied &&
                    pendingRepairChoice == RepairChoice.Lamp &&
                    i >= repairStartHouseWarmth;
                float reveal01 = newlyLit ? GetRepairReveal01() : 1f;
                float alpha = Mathf.Clamp01((0.22f + houseWarmth * 0.1f + Mathf.Sin(time * 1.4f + i) * 0.035f) * reveal01);
                Vector2 offset = new Vector2(-0.55f + i * 0.32f, 0.35f - (i % 2) * 0.08f);
                Set(shelterWarmRays[i], houseAnchor + offset, new Vector2(0.5f * Mathf.Lerp(0.18f, 1f, reveal01), 0.11f), new Color(1f, 0.62f, 0.25f, alpha), -12f + i * 8f);
            }
        }

        // This chart is a shelter payoff: it is lashed beside the moored boat after
        // a successful return. Keeping it alive in the sailing view made it float
        // at the shelter-space anchor and read like a large objective marker.
        bool hasRouteClue = viewMode == SliceViewMode.Shelter &&
            mooringScreenActive &&
            state != SliceState.FinalDeparture &&
            (lighthouseClues > 0 || returnedClueAtBoat);
        lighthouseChartClueRenderer.enabled = hasRouteClue;
        if (hasRouteClue)
        {
            lighthouseChartClueRenderer.sprite = GetLighthouseChartClueSprite();
            Vector2 chartPosition = GetMooredBoatPosition() + new Vector2(-0.22f, 0.3f);
            SetWorldSize(
                lighthouseChartClueRenderer,
                chartPosition,
                returnedClueAtBoat ? new Vector2(0.3f, 0.22f) : new Vector2(0.24f, 0.18f),
                new Color(1f, 1f, 1f, returnedClueAtBoat ? 0.96f : 0.76f),
                -7f);
        }

        bool showReturnedSalvage = viewMode == SliceViewMode.Shelter &&
            mooringScreenActive &&
            state != SliceState.FinalDeparture &&
            (returnedSalvageAtBoat || HasStagedSaltWoodAtMooring());
        if (showReturnedSalvage)
        {
            SetEnabled(sailingSalvagePointRenderer, true);
            sailingSalvagePointRenderer.sprite = GetFormalSprite(ref formalRepairTimberSprite, "AIRepairTimber") ?? GetWaterWrackSprite();
            SetWorldSize(
                sailingSalvagePointRenderer,
                HasStagedSaltWoodAtMooring()
                    ? GetMooringStagingPosition()
                    : GetMooredBoatPosition() + new Vector2(-0.38f, 0.28f),
                HasStagedSaltWoodAtMooring()
                    ? new Vector2(0.7f, 0.26f)
                    : new Vector2(0.58f, 0.22f),
                Color.white,
                -7f);
        }

        bool departureVisible = (mooringScreenActive && IsDepartureReady()) ||
            state == SliceState.FinalDeparture;
        departureSignalRenderer.enabled = departureVisible;
        if (departureVisible)
        {
            float depart01 = state == SliceState.FinalDeparture ? Mathf.Clamp01(stateTimer / Mathf.Max(0.1f, finalDepartureSeconds)) : 0f;
            Vector2 origin = Vector2.Lerp(boatAnchor + GetBoatTripOffset() + new Vector2(0.38f, 0.34f), lighthouseAnchor + new Vector2(-0.35f, 0.48f), depart01 * 0.35f);
            float alpha = state == SliceState.FinalDeparture ? 0.42f : 0.24f + Mathf.Sin(time * 1.4f) * 0.06f;
            Set(departureSignalRenderer, origin, new Vector2(1.45f + depart01 * 1.25f, 0.14f), new Color(1f, 0.82f, 0.38f, alpha), -9f);
        }

        // The lookout mast is the player's physical wind instrument. The same signed
        // wind value moves clouds and changes sailing acceleration; this cloth is world
        // feedback, not a HUD arrow. It remains readable in calm weather with a short
        // droop and stretches only when the wind strengthens.
        stormPennantRenderer.enabled = state != SliceState.FinalDeparture;
        if (stormPennantRenderer.enabled)
        {
            float signedWind = GetNaturalSailingWindSpeed();
            float wind01 = Mathf.Clamp01(Mathf.Abs(signedWind) /
                Mathf.Max(0.01f, sailingWindMaxSpeed));
            float windDirection = Mathf.Abs(signedWind) < 0.015f ? 1f : Mathf.Sign(signedWind);
            float flap = Mathf.Sin(time * Mathf.Lerp(1.4f, 5.2f, wind01)) *
                Mathf.Lerp(1.5f, 7f, wind01);
            Vector2 mastTip = HasCompleteV32ArtPresentation()
                ? GetV32WorldAnchor(new Vector2(1054f, 338f))
                : houseAnchor + new Vector2(1.2f, 1.38f);
            Vector2 streamerPosition = mastTip + new Vector2(windDirection * 0.17f, -0.05f);
            Color color = Color.Lerp(
                new Color(0.64f, 0.57f, 0.43f, 0.72f),
                new Color(0.73f, 0.42f, 0.24f, 0.9f),
                storm01 * 0.72f);
            stormPennantRenderer.sprite = GetStormWarningPennantSprite();
            stormPennantRenderer.flipX = windDirection < 0f;
            SetWorldSize(
                stormPennantRenderer,
                streamerPosition,
                new Vector2(Mathf.Lerp(0.26f, 0.46f, wind01), Mathf.Lerp(0.15f, 0.11f, wind01)),
                color,
                -windDirection * Mathf.Lerp(13f, 2f, wind01) + flap);
        }
    }

    private void UpdateShelterWaveImpactVisuals(float time)
    {
        Sprite impactSprite = GetFormalStiltWaveImpactSprite();
        bool visible = state != SliceState.FinalDeparture && shelterImpactTimer > 0f && impactSprite != null;
        SetEnabled(shelterWaveImpactRenderer, visible);
        if (!visible)
        {
            return;
        }

        shelterWaveImpactRenderer.sprite = impactSprite;
        float life01 = Mathf.Clamp01(shelterImpactTimer / 1.35f);
        float age01 = 1f - life01;
        float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age01 / 0.12f));
        float alpha = fadeIn * Mathf.Pow(life01, 0.42f);
        float grow = Mathf.SmoothStep(0f, 1f, age01);
        Vector2 size = Vector2.Lerp(new Vector2(4.15f, 1.22f), new Vector2(5.0f, 1.58f), grow);
        Color tint = shelterBreachThisTide
            ? new Color(0.94f, 0.86f, 0.76f, alpha)
            : new Color(0.9f, 0.98f, 0.97f, alpha);
        Vector2 position = new Vector2(houseAnchor.x - 0.08f, currentWaterY + 0.2f + Mathf.Sin(time * 8f) * 0.025f);
        SetWorldSize(shelterWaveImpactRenderer, position, size, tint, Mathf.Sin(time * 11f) * 0.6f * life01);
    }

    private void UpdateShelterDamageVisuals()
    {
        Sprite damageSprite = GetFormalStiltDamageSprite();
        Sprite tideZoneWearSprite = GetFormalStiltTideZoneWearSprite();
        bool showTideZoneWear = state != SliceState.FinalDeparture &&
            !HasCompleteV32ArtPresentation() &&
            HasHdFormalHouse() &&
            tideZoneWearSprite != null;
        SetEnabled(shelterTideZoneWearRenderers, showTideZoneWear);
        if (showTideZoneWear)
        {
            // Salt-water piles do not weather uniformly. Keep the strongest visible
            // history around the old tidal/splash band, with one critical split handled
            // separately below. Repairs reduce danger but never erase the old exposure.
            float foundationRepair01 = Mathf.Clamp01((stiltIntegrity - 1f) / 2f);
            float wearAlpha = Mathf.Lerp(0.56f, 0.18f, foundationRepair01);
            // Pixel-to-world checks against AIStiltHouseHD place these on authored
            // post centres, not in the empty bays between them.
            float[] wearX = { -5.36f, -4.37f, -2.4f, -0.42f };
            for (int i = 0; i < shelterTideZoneWearRenderers.Count; i++)
            {
                SpriteRenderer wear = shelterTideZoneWearRenderers[i];
                wear.sprite = tideZoneWearSprite;
                float y = -1.61f + (i % 2) * 0.045f;
                Vector2 wearSize = new Vector2(0.16f + (i % 3) * 0.012f, 0.5f + (i % 2) * 0.065f);
                Color tint = Color.Lerp(
                    new Color(0.76f, 0.8f, 0.7f, wearAlpha),
                    new Color(0.9f, 0.86f, 0.72f, wearAlpha),
                    i * 0.12f);
                SetWorldSize(wear, new Vector2(wearX[i], y), wearSize, tint, -2.2f + i * 1.35f);
            }
        }

        bool repairingBrokenPost = state == SliceState.RepairMoment &&
            pendingRepairChoice == RepairChoice.Stilt &&
            (repairWorkProgress > 0f || repairChoiceApplied) &&
            repairStartStiltIntegrity <= 1;
        float repairFade = repairingBrokenPost ? 1f - GetRepairReveal01() : 1f;
        bool visible = state != SliceState.FinalDeparture &&
            !HasCompleteV32ArtPresentation() &&
            damageSprite != null &&
            (stiltIntegrity <= 1 || repairingBrokenPost) &&
            repairFade > 0.02f;
        SetEnabled(shelterDamageRenderer, visible);
        Vector2 mainDamagePosition = HasHdFormalHouse()
            ? new Vector2(0.22f, -1.25f)
            : houseAnchor + new Vector2(1.35f, -1.45f);
        if (!visible)
        {
            return;
        }

        shelterDamageRenderer.sprite = damageSprite;
        float damageSeverity = stiltIntegrity <= 0 || shelterBreachThisTide ? 1f : 0.72f;
        Vector2 fullSize = Vector2.Lerp(new Vector2(0.31f, 1.04f), new Vector2(0.4f, 1.26f), damageSeverity);
        Vector2 size = new Vector2(fullSize.x * Mathf.Lerp(0.72f, 1f, repairFade), fullSize.y);
        SetWorldSize(shelterDamageRenderer, mainDamagePosition, size, new Color(0.96f, 0.96f, 0.9f, Mathf.Lerp(0.9f, 1f, damageSeverity) * repairFade), -1.5f);
    }

    private void UpdatePreviousHighWaterSaltMarks()
    {
        Sprite markSprite = GetFormalStiltTideZoneWearSprite();
        if (markSprite == null)
        {
            markSprite = GetHouseSaltStreakSprite();
        }

        bool visible = state != SliceState.FinalDeparture &&
            highWaterMemory.HasPreviousCycle &&
            markSprite != null;
        SetEnabled(previousHighWaterSaltMarks, visible);
        if (!visible)
        {
            return;
        }

        float peakY = Mathf.Clamp(
            highWaterMemory.PreviousCyclePeakY,
            lowWaterY + 0.04f,
            highWaterY + 0.18f);
        bool formalHouse = GetFormalHouseSprite() != null;
        for (int i = 0; i < previousHighWaterSaltMarks.Count; i++)
        {
            SpriteRenderer mark = previousHighWaterSaltMarks[i];
            mark.sprite = markSprite;
            mark.sortingOrder = 12;

            float postX = formalHouse
                ? PreviousSaltMarkPostX[Mathf.Min(i, PreviousSaltMarkPostX.Length - 1)]
                : houseAnchor.x - 1.38f + i * 0.92f;
            // Real salt bands break around grain, lashings and wave shadow. Only the
            // first fragment reaches the measured peak; the others sit a few centimetres
            // lower so four posts never read as one artificial horizontal UI line.
            float fragmentDrop = i == 0 ? 0f : 0.012f + (i % 3) * 0.012f;
            float markHeight = 0.105f + (i % 2) * 0.018f;
            float markWidth = 0.15f + (i % 3) * 0.012f;
            Vector2 markPosition = new Vector2(
                postX,
                peakY - fragmentDrop - markHeight * 0.5f);
            Color markColor = Color.Lerp(
                new Color(0.47f, 0.48f, 0.4f, 0.48f),
                new Color(0.72f, 0.68f, 0.53f, 0.54f),
                i / 5f);
            SetWorldSize(
                mark,
                markPosition,
                new Vector2(markWidth, markHeight),
                markColor,
                -3f + i * 1.9f);
        }
    }

    private void UpdateForecastTideNotches()
    {
        TideForecastSnapshot visibleSnapshot = HasLampForecast()
            ? chartForecastSnapshot
            : HasCurrentLoftForecast()
                ? loftForecastSnapshot
                : default;
        bool hasWorldForecast = visibleSnapshot.IsValid;
        float postX = GetFormalHouseSprite() != null
            ? PreviousSaltMarkPostX[0]
            : houseAnchor.x - 1.38f;
        forecastTideNotches.UpdatePresentation(
            state != SliceState.FinalDeparture && hasWorldForecast,
            postX,
            visibleSnapshot.ToHighWaterBand(),
            GetNetLineSprite(),
            visibleSnapshot.RepairedChart);
    }

    private void UpdateTidePrepVisuals(float time)
    {
        TidePrepChoice[] choices = { TidePrepChoice.Rope, TidePrepChoice.Bucket, TidePrepChoice.Stake };
        bool showWorkstations = IsTidePrepWindowOpen();
        TidePrepChoice focusedChoice = CanChooseTidePrep() ? GetNearbyTidePrepChoice() : TidePrepChoice.None;
        for (int i = 0; i < choices.Length; i++)
        {
            TidePrepChoice choice = choices[i];
            SpriteRenderer renderer = GetPrepRenderer(choice);
            bool showAtStation = showWorkstations;
            bool showAsPreparedTool = !showWorkstations && HasPreparedTidePrep() && selectedPrepChoice == choice;
            SetEnabled(renderer, showAtStation || showAsPreparedTool);
            if (!renderer.enabled)
            {
                continue;
            }

            renderer.sortingOrder = viewMode == SliceViewMode.Interior ? 24 : 13;
            bool keepPreparedToolInHouse = !showAtStation && !netDeployed && state == SliceState.LowTidePlanning;
            Vector2 position = showAtStation || keepPreparedToolInHouse
                ? GetNightPrepPosition(choice)
                : GetActivePrepPosition(choice);
            bool selected = HasPreparedTidePrep() && selectedPrepChoice == choice;
            bool focused = focusedChoice == choice;
            SetPrepToolVisual(renderer, choice, position, time, selected, focused, showAtStation || keepPreparedToolInHouse);
        }
    }

    private void SetPrepToolVisual(SpriteRenderer renderer, TidePrepChoice choice, Vector2 position, float time, bool selected, bool focused, bool fullWorkstation)
    {
        if (renderer == null)
        {
            return;
        }

        bool fixedV35Installation = fullWorkstation &&
            viewMode == SliceViewMode.Interior &&
            HasCompleteV35HouseInteriorPresentation();
        Sprite fixedSprite = fixedV35Installation
            ? GetV35FixedTidePrepSprite(choice)
            : null;
        Sprite formalSprite = fixedSprite ?? GetFormalNightPrepSprite(choice);
        renderer.sprite = formalSprite ?? GetPrepChoiceSprite(choice);
        bool highlighted = selected || focused;
        float pulse = highlighted ? Mathf.Sin(time * 3.2f) * 0.035f : Mathf.Sin(time * 1.3f + (int)choice) * 0.012f;
        Color color = selected
            ? new Color(1f, 0.95f, 0.82f, 1f)
            : focused
                ? new Color(0.92f, 0.9f, 0.78f, 0.94f)
                : new Color(0.54f, 0.62f, 0.6f, 0.72f);
        if (formalSprite != null)
        {
            Vector2 fullSize = fixedSprite != null
                ? GetV35TidePrepInstallationSize(choice)
                : GetFormalNightPrepSize(choice);
            float workstationScale = fixedV35Installation
                ? fixedSprite != null ? 1f : 0.34f
                : fullWorkstation
                ? viewMode == SliceViewMode.Interior ? 0.72f : 1f
                : 0.68f;
            Vector2 size = fullSize * workstationScale;
            // These are opaque physical workstations, not translucent interaction
            // markers. The previous generic 54% tint made good production sprites look
            // like black ghost rectangles floating through the V35 lower platform.
            Color formalColor = fixedSprite != null
                ? selected
                    ? new Color(0.84f, 0.86f, 0.82f, 1f)
                    : focused
                        ? new Color(0.77f, 0.81f, 0.79f, 1f)
                        : new Color(0.68f, 0.72f, 0.71f, 1f)
                : selected
                    ? new Color(0.98f, 0.96f, 0.9f, 1f)
                    : focused
                        ? new Color(0.92f, 0.92f, 0.88f, 1f)
                        : new Color(0.82f, 0.83f, 0.79f, 1f);
            // V35 把这些道具视为绑死在桩梁上的设施。选中状态只轻微提亮，
            // 不能再通过漂浮、缩放脉冲或整件摇摆来冒充可拾取奖励。
            float visualPulse = fixedV35Installation ? 0f : pulse;
            SetWorldSize(
                renderer,
                position + new Vector2(0f, visualPulse * 0.25f),
                size + new Vector2(visualPulse, visualPulse * 0.35f),
                formalColor,
                fixedV35Installation
                    ? 0f
                    : highlighted ? Mathf.Sin(time * 1.1f) * 0.7f : 0f);
            return;
        }

        color = GetPrepChoiceColor(choice);
        color.a = highlighted ? 0.96f : 0.52f;
        Set(renderer, position + new Vector2(0f, pulse * 0.35f), GetPrepChoiceScale(choice) + new Vector2(pulse, pulse * 0.4f), color, GetPrepChoiceRotation(choice, time));
    }

    private SpriteRenderer GetPrepRenderer(TidePrepChoice choice)
    {
        if (choice == TidePrepChoice.Bucket)
        {
            return prepBucketRenderer;
        }

        if (choice == TidePrepChoice.Stake)
        {
            return prepStakeRenderer;
        }

        return prepRopeCoilRenderer;
    }

    private static Sprite GetPrepChoiceSprite(TidePrepChoice choice)
    {
        if (choice == TidePrepChoice.Bucket)
        {
            return GetPrepBucketSprite();
        }

        if (choice == TidePrepChoice.Stake)
        {
            return GetPrepStakeSprite();
        }

        return GetPrepRopeCoilSprite();
    }

    private static Sprite GetFormalNightPrepSprite(TidePrepChoice choice)
    {
        if (choice == TidePrepChoice.Bucket)
        {
            return GetFormalSprite(ref formalNightPrepBucketSprite, "AINightPrepBucketHD");
        }

        if (choice == TidePrepChoice.Stake)
        {
            return GetFormalSprite(ref formalNightPrepStakeSprite, "AINightPrepStakeHD");
        }

        if (choice == TidePrepChoice.Rope)
        {
            return GetFormalSprite(ref formalNightPrepRopeSprite, "AINightPrepRopeHD");
        }

        return null;
    }

    private static Sprite GetV35FixedTidePrepSprite(TidePrepChoice choice)
    {
        // V36 owns only the flood-safe mounting attachments used by the V35
        // cutaway. Handheld buckets, carried net bundles and all older fallback
        // presentations continue to use the original formal night-prep sprites.
        if (choice == TidePrepChoice.Bucket)
        {
            return GetFormalSprite(
                ref formalV36TidePrepBucketFixedSprite,
                "AIV36TidePrepBucketFixed");
        }

        if (choice == TidePrepChoice.Stake)
        {
            return GetFormalSprite(
                ref formalV36TidePrepStakeFixedSprite,
                "AIV36TidePrepStakeFixed");
        }

        if (choice == TidePrepChoice.Rope)
        {
            return GetFormalSprite(
                ref formalV36TidePrepRopeFixedSprite,
                "AIV36TidePrepRopeFixed");
        }

        return null;
    }

    private static Vector2 GetFormalNightPrepSize(TidePrepChoice choice)
    {
        if (choice == TidePrepChoice.Bucket)
        {
            return new Vector2(1.12f, 0.9f);
        }

        if (choice == TidePrepChoice.Stake)
        {
            return new Vector2(0.88f, 1.08f);
        }

        return new Vector2(1.2f, 1.08f);
    }

    private Color GetPrepChoiceColor(TidePrepChoice choice)
    {
        if (choice == TidePrepChoice.Bucket)
        {
            return new Color(0.74f, 0.92f, 0.88f, 0.96f);
        }

        if (choice == TidePrepChoice.Stake)
        {
            return new Color(0.82f, 0.54f, 0.26f, 0.96f);
        }

        return new Color(0.92f, 0.74f, 0.44f, 0.96f);
    }

    private static Vector2 GetPrepChoiceScale(TidePrepChoice choice)
    {
        if (choice == TidePrepChoice.Bucket)
        {
            return new Vector2(0.28f, 0.34f);
        }

        if (choice == TidePrepChoice.Stake)
        {
            return new Vector2(0.18f, 0.62f);
        }

        return new Vector2(0.34f, 0.24f);
    }

    private static float GetPrepChoiceRotation(TidePrepChoice choice, float time)
    {
        if (choice == TidePrepChoice.Stake)
        {
            return -8f + Mathf.Sin(time * 0.9f) * 1.5f;
        }

        if (choice == TidePrepChoice.Bucket)
        {
            return Mathf.Sin(time * 1.1f) * 2.5f;
        }

        return Mathf.Sin(time * 0.8f) * 4f;
    }

    private Vector2 GetLowTidePrepPosition(TidePrepChoice choice)
    {
        int index = choice == TidePrepChoice.Rope ? 0 : choice == TidePrepChoice.Bucket ? 1 : 2;
        return new Vector2(houseAnchor.x - 0.78f + index * 0.55f, GetPlayerLaneY(WalkLane.TideFlat) + 0.3f);
    }

    private Vector2 GetActivePrepPosition(TidePrepChoice choice)
    {
        float tideFlatSurfaceY = GetPlayerStandingFeetY(WalkLane.TideFlat);
        if (choice == TidePrepChoice.Bucket)
        {
            return new Vector2(netAnchor.x + 0.82f, tideFlatSurfaceY + 0.31f);
        }

        if (choice == TidePrepChoice.Stake)
        {
            return new Vector2(GetShoreWorkX() - 0.38f, tideFlatSurfaceY + 0.37f);
        }

        return new Vector2(netAnchor.x - 0.72f, tideFlatSurfaceY + 0.37f);
    }

    private bool HasLampForecast()
    {
        return viewMode == SliceViewMode.Shelter &&
            state == SliceState.LowTidePlanning &&
            lampForecastCharges > 0 &&
            IsForecastSnapshotCurrent(chartForecastSnapshot);
    }

    private float GetPredictedNetEncounterSeconds(float depth01)
    {
        return GetPredictedNetEncounterSeconds(depth01, out _);
    }

    private float GetPredictedNetEncounterSeconds(
        float depth01,
        out TideDriftMaterial forecastMaterial)
    {
        const int samples = 192;
        float cycle = Mathf.Max(8f, tideCycleSeconds);
        float stepSeconds = cycle / samples;
        float currentPhase01 = Mathf.Repeat(tideClockSeconds / cycle, 1f);
        int currentCycleOrdinal = GetAstronomicalCycleOrdinal(weatherClockSeconds);
        int forecastCycleOrdinal = TideMixedSemidiurnalModel.GetNextHighWaterCycleOrdinal(
            currentPhase01,
            currentCycleOrdinal);
        float stormPressureAtHigh01 = EvaluateStormPressureAtCycleHigh01(
            weatherClockSeconds,
            tideClockSeconds,
            forecastCycleOrdinal);
        float inequalityRatio = EvaluateTideInequalityRatio(GetCycleHighWorldClockSeconds(
            weatherClockSeconds,
            tideClockSeconds,
            forecastCycleOrdinal));
        bool routeKnown = sailingWreckClueClaimed || lighthouseClues > 0 || routeClueReturnRound >= 0;
        TideDriftBatch batch = TideDriftSourceModel.BuildField(
            forecastCycleOrdinal,
            moonAgeDays,
            tideStrength,
            stormPressureAtHigh01,
            routeKnown).NearshoreBatch;
        forecastMaterial = batch.Material;
        float activeNetY = Mathf.Lerp(
            GetNetLineY(NetLine.High),
            GetNetLineY(NetLine.Low),
            Mathf.Clamp01(depth01));
        float headLineY = GetNetHeadLineY();
        float referenceStrength = CalculateTideStrength(OpeningMoonAgeDays);
        float referenceFloodSpeed = GetReferenceFloodCurrentSpeed();
        float travel01 = 0f;
        float progress01 = 0f;
        float maximumProgress01 = 0f;
        for (int i = 0; i < samples; i++)
        {
            float phase01 = (i + 0.5f) / samples;
            float waterY = EvaluateNaturalWaterYAtPhase(
                phase01,
                forecastCycleOrdinal,
                stormPressureAtHigh01,
                inequalityRatio);
            float signedFlowWave = TideMixedSemidiurnalModel.EvaluateSignedCurrentWave(
                phase01,
                forecastCycleOrdinal,
                inequalityRatio);
            float physicalCurrent = TideAstronomicalCurrentModel.EvaluateSignedSpeedFromWave(
                signedFlowWave,
                tideStrength,
                referenceStrength,
                MeanTidalTransportSpeed,
                EbbCurrentBoost);
            float waterGate01 = Mathf.InverseLerp(lowWaterY + 0.12f, lowWaterY + 1.18f, waterY);
            float nextTravel01 = TideDriftSourceModel.AdvanceNearshoreTravel01(
                travel01,
                stepSeconds,
                -physicalCurrent,
                referenceFloodSpeed,
                waterGate01,
                batch,
                referenceStrength,
                stormPressureAtHigh01);
            TideNetEncounterModel.Step encounter = TideNetEncounterModel.Advance(
                progress01,
                stepSeconds,
                travel01,
                nextTravel01,
                headLineY,
                activeNetY,
                waterY,
                netIntegrity / 4f,
                batch.Material);
            progress01 = encounter.Progress01;
            maximumProgress01 = Mathf.Max(maximumProgress01, progress01);
            travel01 = nextTravel01;
            if (encounter.Captured)
            {
                maximumProgress01 = 1f;
                break;
            }
        }

        TideNetEncounterModel.MaterialProfile profile = TideNetEncounterModel.GetProfile(batch.Material);
        return maximumProgress01 * profile.RequiredEffectiveContactSeconds;
    }

    private TideNetForecastModel.NetChoice EvaluateNetChoiceForecast(float depth01)
    {
        float clampedDepth = Mathf.Clamp01(depth01);
        float predictedContactSeconds = GetPredictedNetEncounterSeconds(
            clampedDepth,
            out TideDriftMaterial forecastMaterial);
        return TideNetForecastModel.EvaluateNetChoice(
            predictedContactSeconds,
            TideNetEncounterModel.GetProfile(forecastMaterial).RequiredEffectiveContactSeconds,
            tideCycleSeconds,
            CalculateForecastNetStress(clampedDepth, tideStrength));
    }

    private string GetNetChoiceForecastText(float depth01)
    {
        TideNetForecastModel.NetChoice choice = EvaluateNetChoiceForecast(depth01);
        string contactText = choice.LikelyMissesFirstCatch
            ? "首批漂物可能从网缘漏过"
            : choice.MarginalContact
                ? "首批漂物接触余量很小"
                : "首批漂物能在网面挂稳";
        string stressText = choice.StressTier <= 0
            ? "网压轻"
            : choice.StressTier == 1
                ? "网压可控"
                : choice.StressTier == 2
                    ? "网压重"
                    : "有断网风险";
        return $"{contactText}，{stressText}";
    }

    private float GetSecondsUntilNextHighWaterSlack()
    {
        float cycle = Mathf.Max(8f, tideCycleSeconds);
        float phase01 = Mathf.Repeat(tideClockSeconds / cycle, 1f);
        float phaseUntilHigh = 0.5f - phase01;
        if (phaseUntilHigh < 0f)
        {
            phaseUntilHigh += 1f;
        }

        return phaseUntilHigh * cycle;
    }

    private string GetLampForecastText()
    {
        if (!HasLampForecast())
        {
            return string.Empty;
        }

        int tidePercent = Mathf.RoundToInt(tideStrength * 100f);
        int secondsToSlack = Mathf.CeilToInt(GetSecondsUntilNextHighWaterSlack());
        return $"下一潮预报：{GetPreviousHighWaterComparisonText(chartForecastSnapshot)}，{GetForecastHighWaterRangeText(chartForecastSnapshot)}，潮强约{tidePercent}%，约{secondsToSlack}秒到高潮平流。浅挂省网，深挂覆盖低水层；贴水漂物仍可能越过网顶。";
    }

    private int GetLoopStepIndex()
    {
        if (state == SliceState.FinalDeparture)
        {
            return 4;
        }

        if (viewMode == SliceViewMode.Sailing)
        {
            return sailingClueCollected ? 2 : 1;
        }

        if (state == SliceState.RepairMoment)
        {
            return repairChoiceApplied ? 4 : 3;
        }

        if (state == SliceState.EbbCollect)
        {
            return 2;
        }

        if (state == SliceState.TideRising)
        {
            return 1;
        }

        return CanSeeLighthouse() || IsDepartureReady() ? 4 : 0;
    }

    private Vector2 GetCurrentObjectivePosition()
    {
        if (state == SliceState.FinalDeparture)
        {
            return lighthouseAnchor;
        }

        if (viewMode == SliceViewMode.Sailing)
        {
            if (sailingClueCollected)
            {
                return GetSailingScreenPosition(new Vector2(sailingHomeX, sailingHomeY + 0.3f));
            }

            Vector2 objective = EnableSailingBuoyGameplay && !sailingBuoyChecked
                ? GetSailingPointPosition(SailingPointKind.Buoy)
                : !sailingSalvageCollected
                    ? GetSailingPointPosition(SailingPointKind.Salvage)
                    : GetCurrentSailingTargetPosition();
            return GetSailingScreenPosition(objective);
        }

        if (state == SliceState.EbbCollect)
        {
            return new Vector2(netAnchor.x + 0.5f, GetSelectedNetY() + 0.04f);
        }

        if (state == SliceState.RepairMoment)
        {
            return GetRepairChoicePosition(repairChoiceApplied ? pendingRepairChoice : GetRecommendedRepairChoice());
        }

        if (state == SliceState.TideRising)
        {
            if (!nearshoreWorkDone && IsNearshoreWorkWindowOpen())
            {
                return GetShoreWorkPosition();
            }

            return boatAnchor + new Vector2(-0.1f, 0.24f);
        }

        if (CanSeeLighthouse() || IsDepartureReady())
        {
            return boatAnchor + new Vector2(-0.1f, 0.24f);
        }

        return new Vector2(netAnchor.x, GetSelectedNetY());
    }

    private void UpdateHarvestVisuals(float pulse)
    {
        bool carriedOrPlaced = state == SliceState.RepairMoment &&
            !repairChoiceApplied &&
            currentHarvest != HarvestKind.None &&
            (harvestPhysicalState == HarvestPhysicalState.Carried ||
             harvestPhysicalState == HarvestPhysicalState.PlacedAtWork);
        bool visible = carriedOrPlaced;
        harvestRenderer.enabled = visible;
        SetEnabled(harvestCarryItems, visible);
        if (!visible)
        {
            return;
        }

        // Every visible piece survives the transition from net to hands to work site.
        // The old single renderer made a three-piece catch collapse into one icon.
        Vector2 carryTarget = harvestPhysicalState == HarvestPhysicalState.Carried
            ? GetV41CarryNetHandWorldPosition()
            : playerPosition + new Vector2(playerFacing * 0.31f, 0.18f);
        Vector2 carryStart = harvestCarryStartPosition == Vector2.zero
            ? new Vector2(netAnchor.x - 0.42f, GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.22f)
            : harvestCarryStartPosition;
        float carry01 = Mathf.SmoothStep(0f, 1f, harvestCarryTransition01);
        Vector2 bundleCenter = Vector2.Lerp(carryStart, carryTarget, carry01);
        if (harvestPhysicalState == HarvestPhysicalState.PlacedAtWork &&
            harvestPlacedRepairChoice != RepairChoice.None)
        {
            float placement01 = Mathf.SmoothStep(0f, 1f, harvestPlacementTransition01);
            Vector2 workPosition = GetRepairMaterialPlacementPosition(harvestPlacedRepairChoice);
            bundleCenter = Vector2.Lerp(bundleCenter, workPosition, placement01);
        }

        float sway = Mathf.Sin(pulse * Mathf.PI * 2f);
        int pieceCount = GetCurrentHarvestVisiblePieceCount(netCatchBundleTier, true);

        // Wet catches are carried in the folded belly of the same net. This keeps fish,
        // a chart parcel and debris from reading as loose inventory icons in front of the actor.
        harvestRenderer.sortingOrder = viewMode == SliceViewMode.Interior ? 27 : 14;
        harvestRenderer.sprite = GetHarvestCarrySlingSprite();
        Vector2 slingSize = currentHarvest == HarvestKind.Wood
            ? new Vector2(0.62f, 0.29f)
            : currentHarvest == HarvestKind.Relic
                ? new Vector2(0.56f, 0.32f)
                : new Vector2(0.58f + pieceCount * 0.08f, 0.31f + pieceCount * 0.025f);
        // 这是从主网解下来的小型湿网兜，不是第二张完整渔网。提高不透明度
        // 后，物件会被网兜托住而不是悬在人物胸前；盐木本身可直接双手抱持，
        // 因此只保留较轻的绑绳轮廓。
        float slingAlpha = currentHarvest == HarvestKind.Wood
            ? 0.42f
            : currentHarvest == HarvestKind.Fish
                ? 0.7f
                : currentHarvest == HarvestKind.Relic
                    ? 0.72f
                    : 0.76f;
        float slingRotation = playerFacing < 0 ? 4f : -4f;
        SetWorldSize(harvestRenderer, bundleCenter + new Vector2(0f, -0.015f), slingSize, new Color(0.72f, 0.7f, 0.56f, slingAlpha), slingRotation);

        for (int i = 0; i < harvestCarryItems.Count; i++)
        {
            SpriteRenderer item = harvestCarryItems[i];
            item.enabled = i < pieceCount;
            if (!item.enabled)
            {
                continue;
            }

            item.sortingOrder = viewMode == SliceViewMode.Interior ? 28 : 15;
            HarvestKind pieceKind = GetCurrentHarvestPieceKind(i, pieceCount);
            int pieceBatchId = GetCurrentHarvestPieceBatchId(i, pieceCount);
            int pieceVisualIndex = GetCurrentHarvestPieceVisualIndex(i, pieceCount);
            item.sprite = GetCaughtNetItemSprite(pieceKind, pieceVisualIndex, pieceBatchId);
            bool carriedFacingFlip = harvestPhysicalState == HarvestPhysicalState.Carried && playerFacing < 0;
            item.flipX = carriedFacingFlip ^ (pieceKind == HarvestKind.Fish && (i & 1) == 1);
            Vector2 size = GetHarvestWorldSize(pieceKind, pieceVisualIndex, pieceBatchId);
            float centeredIndex = i - (pieceCount - 1) * 0.5f;
            Vector2 fanOffset;
            if (currentHarvest == HarvestKind.Wood)
            {
                fanOffset = i == 0 ? Vector2.zero : new Vector2(-0.035f, 0.075f);
            }
            else if (currentHarvest == HarvestKind.Fish)
            {
                if (pieceCount >= 3)
                {
                    fanOffset = i == 0
                        ? new Vector2(-0.18f, -0.025f)
                        : i == 1
                            ? new Vector2(0.18f, -0.025f)
                            : new Vector2(0f, 0.075f);
                }
                else
                {
                    fanOffset = i == 0
                        ? new Vector2(-0.2f, -0.055f)
                        : new Vector2(0.2f, 0.065f);
                }
            }
            else if (currentHarvest == HarvestKind.Relic)
            {
                fanOffset = i == 0
                    ? new Vector2(-0.07f, 0.025f)
                    : i == 1
                        ? new Vector2(0.105f, -0.025f)
                        : new Vector2(0.17f, 0.07f);
            }
            else
            {
                fanOffset = Vector2.zero;
                size *= 1f + Mathf.Clamp(netCatchBundleTier - 1, 0, 2) * 0.07f;
            }
            if (playerFacing < 0 && harvestPhysicalState == HarvestPhysicalState.Carried)
            {
                fanOffset.x *= -1f;
            }

            Color color = IsUsingFormalHarvestSprite(pieceKind)
                ? Color.white
                : pieceKind == HarvestKind.Trash
                    ? new Color(0.54f, 0.66f, 0.58f, 0.9f)
                    : new Color(0.9f, 0.82f, 0.54f, 0.96f);
            float rotation = centeredIndex * 3f + sway * (harvestPhysicalState == HarvestPhysicalState.Carried ? 0.9f : 0.25f);
            if (currentHarvest == HarvestKind.Fish)
            {
                rotation += centeredIndex * 12f;
            }
            Vector2 itemPosition = bundleCenter + fanOffset;
            if (TryGetV59FindSpec(
                pieceKind,
                pieceVisualIndex,
                pieceBatchId,
                false,
                out TideV59FindSpec carrySpec))
            {
                float authoredCarryRotation = item.flipX
                    ? -carrySpec.CarryRotationDegrees
                    : carrySpec.CarryRotationDegrees;
                rotation += authoredCarryRotation;
                if (harvestPhysicalState == HarvestPhysicalState.Carried)
                {
                    itemPosition = TideV59FindPresentationModel.GetSpriteCenterForGrabPoint(
                        bundleCenter + fanOffset,
                        carrySpec,
                        rotation,
                        item.flipX);
                }
            }
            SetWorldSize(item, itemPosition, size, color, rotation);
        }
    }

    private static int GetHarvestVisiblePieceCount(HarvestKind harvest, int physicalPieceCount, bool bundled)
    {
        int clampedCount = Mathf.Clamp(physicalPieceCount, 1, 3);
        if (harvest == HarvestKind.Trash && !HasCompleteV59FindPresentation())
        {
            // One tangled mass can contain a larger yield without cloning an identical
            // debris cutout three times. Relics use separately cropped map/compass parts.
            return 1;
        }

        if (bundled && harvest == HarvestKind.Wood && !HasCompleteV59FindPresentation())
        {
            return Mathf.Min(2, clampedCount);
        }

        return clampedCount;
    }

    private int GetCurrentHarvestVisiblePieceCount(int physicalPieceCount, bool bundled)
    {
        if (!extraSaltWoodBundledWithNetHarvest || !IsExtraSaltWoodInCurrentHarvest())
        {
            return GetHarvestVisiblePieceCount(currentHarvest, physicalPieceCount, bundled);
        }

        int ordinaryPieceCount = Mathf.Max(1, physicalPieceCount - 1);
        int ordinaryVisibleCount = GetHarvestVisiblePieceCount(currentHarvest, ordinaryPieceCount, bundled);
        return Mathf.Min(3, ordinaryVisibleCount + 1);
    }

    private int GetSecuredPostHarvestVisiblePieceCount(int physicalPieceCount, bool bundled)
    {
        bool includesSaltWood = extraSaltWoodBundledWithNetHarvest &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.SecuredAtPost;
        if (!includesSaltWood)
        {
            return GetHarvestVisiblePieceCount(securedPostHarvest, physicalPieceCount, bundled);
        }

        int ordinaryPieceCount = Mathf.Max(1, physicalPieceCount - 1);
        int ordinaryVisibleCount = GetHarvestVisiblePieceCount(
            securedPostHarvest,
            ordinaryPieceCount,
            bundled);
        return Mathf.Min(3, ordinaryVisibleCount + 1);
    }

    private HarvestKind GetCurrentHarvestPieceKind(int visibleIndex, int visiblePieceCount)
    {
        bool extraSaltWoodIsVisible = extraSaltWoodBundledWithNetHarvest &&
            IsExtraSaltWoodInCurrentHarvest();
        return extraSaltWoodIsVisible && visibleIndex == visiblePieceCount - 1
            ? HarvestKind.Wood
            : currentHarvest;
    }

    private int GetCurrentHarvestPieceBatchId(int visibleIndex, int visiblePieceCount)
    {
        bool extraSaltWoodIsVisible = extraSaltWoodBundledWithNetHarvest &&
            IsExtraSaltWoodInCurrentHarvest();
        return extraSaltWoodIsVisible && visibleIndex == visiblePieceCount - 1
            ? extraSaltWoodBatchId
            : currentHarvestBatchId;
    }

    private int GetCurrentHarvestPieceVisualIndex(int visibleIndex, int visiblePieceCount)
    {
        bool extraSaltWoodIsVisible = extraSaltWoodBundledWithNetHarvest &&
            IsExtraSaltWoodInCurrentHarvest();
        return extraSaltWoodIsVisible && visibleIndex == visiblePieceCount - 1
            ? 0
            : visibleIndex;
    }

    private HarvestKind GetSecuredPostHarvestPieceKind(int visibleIndex, int visiblePieceCount)
    {
        bool extraSaltWoodIsVisible = extraSaltWoodBundledWithNetHarvest &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.SecuredAtPost;
        return extraSaltWoodIsVisible && visibleIndex == visiblePieceCount - 1
            ? HarvestKind.Wood
            : securedPostHarvest;
    }

    private int GetSecuredPostHarvestPieceBatchId(int visibleIndex, int visiblePieceCount)
    {
        bool extraSaltWoodIsVisible = extraSaltWoodBundledWithNetHarvest &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.SecuredAtPost;
        return extraSaltWoodIsVisible && visibleIndex == visiblePieceCount - 1
            ? extraSaltWoodBatchId
            : securedPostHarvestBatchId;
    }

    private int GetSecuredPostHarvestPieceVisualIndex(int visibleIndex, int visiblePieceCount)
    {
        bool extraSaltWoodIsVisible = extraSaltWoodBundledWithNetHarvest &&
            extraSaltWoodOwner == ExtraSaltWoodOwner.SecuredAtPost;
        return extraSaltWoodIsVisible && visibleIndex == visiblePieceCount - 1
            ? 0
            : visibleIndex;
    }

    private Vector2 GetRepairMaterialPlacementPosition(RepairChoice choice)
    {
        Vector2 workPosition = GetRepairChoicePosition(choice);
        if (choice == RepairChoice.Stilt)
        {
            // Repair timber is carried in a folded wet net sling. The visible sprite
            // bounds extend slightly beyond its nominal 0.29 m body and the renderer
            // adds 0.015 m of wet sag, so 0.18 m places the measured lower edge on the
            // actual boardwalk instead of hovering above or clipping through it.
            return new Vector2(
                workPosition.x - 0.34f,
                GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.18f);
        }

        if (choice == RepairChoice.Cistern)
        {
            return new Vector2(
                TideBarrenIslandController.CisternX - 0.46f,
                GetPlayerStandingFeetY(WalkLane.TideFlat) + 0.14f);
        }

        if (choice == RepairChoice.Net || choice == RepairChoice.Hull || choice == RepairChoice.Cabin)
        {
            return workPosition + new Vector2(-0.26f, 0.08f);
        }

        if (choice == RepairChoice.InteriorSeal ||
            choice == RepairChoice.Workbench ||
            choice == RepairChoice.Bed ||
            choice == RepairChoice.ChartRadio ||
            choice == RepairChoice.Lamp)
        {
            // V35's repair anchors describe the wall or furniture component, not a
            // material shelf. Put the wet sling on the authored living-floor surface;
            // adding 0.18 m to the wall anchor previously left timber at chest height.
            return new Vector2(
                workPosition.x + 0.22f,
                GetPlayerStandingFeetY(WalkLane.InteriorUpper) + 0.18f);
        }

        if (choice == RepairChoice.Roof)
        {
            return new Vector2(
                workPosition.x + 0.22f,
                GetPlayerStandingFeetY(WalkLane.InteriorLoft) + 0.18f);
        }

        return workPosition + new Vector2(0.22f, 0.18f);
    }

    private void UpdateText(float storm01)
    {
        float hudLeftX = GetHudLeftX();
        titleText.text = "TIDE";
        titleText.color = new Color(0.86f, 0.94f, 0.9f, 0.9f);
        titleText.transform.localPosition = new Vector3(hudLeftX, 3.28f, visualZ - 0.3f);

        string phase = state == SliceState.LowTidePlanning ? "低潮布网" :
            state == SliceState.TideRising ? (tideCurrentlyRising ? "涨潮冲网" : "退潮冲网") :
            state == SliceState.EbbCollect ? "退潮回收" :
            state == SliceState.RepairMoment ? "修补归处" : "暴潮离开";
        if (viewMode == SliceViewMode.Sailing)
        {
            phase = "短航找灯塔";
        }
        else if (viewMode == SliceViewMode.Lookout)
        {
            phase = "阁楼瞭望";
        }
        else if (viewMode == SliceViewMode.Interior)
        {
            phase = playerLane == WalkLane.InteriorLoft
                ? "屋内瞭望阁楼"
                : playerLane == WalkLane.InteriorUpper
                    ? "屋内生活层"
                    : "屋底工作层";
        }
        statusText.text = showDebugHud ? BuildDebugHudText(phase, storm01) : string.Empty;
        statusText.color = new Color(0.82f, 0.9f, 0.86f, 0.9f);
        statusText.transform.localPosition = new Vector3(hudLeftX, 3.02f, visualZ - 0.3f);

        // 正式玩法不再常驻左下角。开发阶段需要规则、进度和操作信息时，
        // 它们与时间/距离一起进入 F3 调试层，避免文字替代世界内的因果反馈。
        loopGoalText.text = string.Empty;
        controlText.text = string.Empty;

        UpdateBoatBoardPromptText();
    }

    private static float GetHudLeftX()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            camera = FindFirstObjectByType<Camera>();
        }

        if (camera == null || !camera.orthographic)
        {
            return -6.45f;
        }

        // Batch captures render through a target texture whose aspect can differ
        // from the Editor Game view. Anchor against the surface actually rendered.
        float aspect = camera.targetTexture != null && camera.targetTexture.height > 0
            ? (float)camera.targetTexture.width / camera.targetTexture.height
            : camera.aspect;
        return camera.transform.position.x - camera.orthographicSize * aspect + 0.22f;
    }

    private void UpdateBoatBoardPromptText()
    {
        if (boatBoardPromptText == null)
        {
            return;
        }

        // Context labels are development scaffolding, not part of the authored
        // world. F3 owns every such string so normal play must read the boat, rope,
        // timber and workstations themselves instead of text painted over them.
        if (!showDebugHud)
        {
            boatBoardPromptText.text = string.Empty;
            return;
        }

        if (boatViewTransition != BoatViewTransition.None)
        {
            boatBoardPromptText.text = string.Empty;
            return;
        }

        if (viewMode == SliceViewMode.Lookout)
        {
            boatBoardPromptText.text = "F 离开瞭望";
            boatBoardPromptText.color = new Color(0.86f, 0.94f, 0.86f, 0.94f);
            boatBoardPromptText.transform.localPosition = new Vector3(
                GetHudLeftX(),
                -3.08f,
                visualZ - 0.35f);
            return;
        }

        if (viewMode == SliceViewMode.Sailing)
        {
            bool canReturn = CanReturnFromSailing();
            Vector2 boatWorldPosition = GetSailingBoatBasePosition();
            bool nearSalvage = extraSaltWoodOwner == ExtraSaltWoodOwner.SailingWater &&
                Vector2.Distance(GetSailingBoatSternWorldPosition(), GetSailingPointPosition(SailingPointKind.Salvage)) <= sailingHookReach;
            bool nearOtherPoint = !nearSalvage && CanInteractAtSailingPoint();
            float relativeSpeed = nearSalvage ? GetSailingSalvageRelativeSpeed() : 0f;
            boatBoardPromptText.text = extraSaltWoodOwner == ExtraSaltWoodOwner.HookingToBoat
                ? sailingSalvageTension01 >= TideContinuousSalvageModel.CriticalTension01
                    ? "绳过紧 · 收帆贴流"
                    : sailingSalvageHauling ? "正在收绳" : "按住 F 收绳"
                : sailingHookThrowActive
                    ? "按住 F 抛钩"
                : canReturn
                ? "F 返航"
                : nearSalvage
                    ? relativeSpeed <= sailingHookMaxRelativeSpeed ? "按住 F 抛钩" : "收帆贴流"
                    : nearOtherPoint ? "F 查看" : string.Empty;
            boatBoardPromptText.color = canReturn || IsSailingSalvageInteractionActive() ||
                (nearSalvage && relativeSpeed <= sailingHookMaxRelativeSpeed)
                ? new Color(1f, 0.86f, 0.44f, 0.96f)
                : new Color(0.74f, 0.96f, 0.92f, 0.78f);
            Vector2 promptPosition = canReturn
                ? GetSailingScreenPosition(new Vector2(sailingHomeX - 0.28f, sailingHomeY + 0.8f))
                : GetSailingScreenPosition(boatWorldPosition + new Vector2(0f, 0.92f));
            boatBoardPromptText.transform.localPosition = new Vector3(promptPosition.x, promptPosition.y, visualZ - 0.35f);
            return;
        }

        TidePrepChoice nearbyPrep = CanChooseTidePrep() ? GetNearbyTidePrepChoice() : TidePrepChoice.None;
        if (nearbyPrep != TidePrepChoice.None)
        {
            bool continuingPrep = pendingTidePrepChoice == nearbyPrep && tidePrepWorkProgress > 0f;
            int prepPercent = Mathf.RoundToInt(tidePrepWorkProgress * 100f);
            boatBoardPromptText.text = continuingPrep
                ? tidePrepWorkActive
                    ? $"备{GetPrepChoiceName(nearbyPrep)} {prepPercent}%"
                    : $"按住 F 续{GetPrepChoiceName(nearbyPrep)} {prepPercent}%"
                : $"按住 F 备{GetPrepChoiceName(nearbyPrep)} · {GetPrepRoleName(nearbyPrep)}";
            boatBoardPromptText.color = new Color(1f, 0.82f, 0.43f, 0.96f);
            Vector2 position = GetNightPrepPosition(nearbyPrep);
            boatBoardPromptText.transform.localPosition = new Vector3(position.x - 0.28f, position.y + 0.62f, visualZ - 0.35f);
            return;
        }

        if (viewMode == SliceViewMode.Interior && IsPlayerNearLoftLookout())
        {
            boatBoardPromptText.text = HasCurrentLoftForecast() ? "F 再看潮" : "F 观潮";
            boatBoardPromptText.color = new Color(0.86f, 0.94f, 0.86f, 0.94f);
            boatBoardPromptText.transform.localPosition = new Vector3(
                GetInteriorLoftLookoutX(),
                GetPlayerLaneY(WalkLane.InteriorLoft) + 0.72f,
                visualZ - 0.35f);
            return;
        }

        if (viewMode == SliceViewMode.Interior &&
            TryGetInteriorStairPrompt(out string stairPrompt, out Vector2 stairPromptPosition))
        {
            boatBoardPromptText.text = stairPrompt;
            boatBoardPromptText.color = new Color(0.82f, 0.9f, 0.82f, 0.9f);
            boatBoardPromptText.transform.localPosition = new Vector3(
                stairPromptPosition.x,
                stairPromptPosition.y + 0.7f,
                visualZ - 0.35f);
            return;
        }

        if (viewMode == SliceViewMode.Shelter &&
            TryGetExteriorStairPrompt(out string exteriorStairPrompt, out Vector2 exteriorStairPosition))
        {
            boatBoardPromptText.text = exteriorStairPrompt;
            boatBoardPromptText.color = new Color(0.82f, 0.9f, 0.82f, 0.9f);
            boatBoardPromptText.transform.localPosition = new Vector3(
                exteriorStairPosition.x,
                exteriorStairPosition.y + 0.7f,
                visualZ - 0.35f);
            return;
        }

        bool canRigNetNow = state == SliceState.LowTidePlanning || state == SliceState.TideRising;
        if (viewMode == SliceViewMode.Shelter && canRigNetNow && !netDeployed && !netSecuredEarly && IsPlayerNearNet())
        {
            boatBoardPromptText.text = GetNetRigPromptText();
            boatBoardPromptText.color = new Color(0.9f, 0.78f, 0.5f, 0.94f);
            boatBoardPromptText.transform.localPosition = new Vector3(playerPosition.x - 0.28f, playerPosition.y + 0.72f, visualZ - 0.35f);
            return;
        }

        if (viewMode == SliceViewMode.Interior &&
            harvestPhysicalState == HarvestPhysicalState.Carried &&
            IsPlayerNearInteriorStorage())
        {
            boatBoardPromptText.text = "F 存放";
            boatBoardPromptText.color = new Color(1f, 0.86f, 0.44f, 0.96f);
            boatBoardPromptText.transform.localPosition = new Vector3(GetInteriorStorageX(), GetPlayerLaneY(WalkLane.InteriorUpper) + 0.72f, visualZ - 0.35f);
            return;
        }

        bool canRestHere = IsPlayerNearRestPoint() &&
            CanRestThroughCurrentStormState() &&
            ((state == SliceState.LowTidePlanning && dayNightPhase == DayNightPhase.Night) ||
             state == SliceState.RepairMoment);
        if (canRestHere)
        {
            boatBoardPromptText.text = "F 休息";
            boatBoardPromptText.color = new Color(1f, 0.82f, 0.43f, 0.96f);
            Vector2 restPosition = viewMode == SliceViewMode.Interior
                ? new Vector2(
                    GetInteriorBedX(),
                    GetPlayerLaneY(HasRegisteredHousePresentation() ? WalkLane.InteriorUpper : WalkLane.InteriorLoft) + 0.78f)
                : new Vector2(houseAnchor.x - 1.2f, GetPlayerLaneY(WalkLane.Deck) + 0.78f);
            boatBoardPromptText.transform.localPosition = new Vector3(restPosition.x, restPosition.y, visualZ - 0.35f);
            return;
        }

        if (viewMode == SliceViewMode.Interior)
        {
            bool nearExit = IsPlayerNearInteriorExit();
            bool carryingAtBed = harvestPhysicalState == HarvestPhysicalState.Carried && IsPlayerNearRestPoint();
            boatBoardPromptText.text = nearExit
                ? "F 出门"
                : carryingAtBed ? "先下到生活层储物架" : string.Empty;
            boatBoardPromptText.color = new Color(0.86f, 0.95f, 0.9f, 0.82f);
            boatBoardPromptText.transform.localPosition = new Vector3(
                GetInteriorDoorX(),
                GetPlayerLaneY(WalkLane.InteriorUpper) + 0.72f,
                visualZ - 0.35f);
            return;
        }

        if (viewMode == SliceViewMode.Shelter && IsPlayerNearBoat() && HasReturnedSailingCargoAtBoat())
        {
            boatBoardPromptText.text = "F 卸货";
            boatBoardPromptText.color = new Color(1f, 0.86f, 0.44f, 0.96f);
            boatBoardPromptText.transform.localPosition = new Vector3(GetBoatBoardingX(), boatAnchor.y + 1.05f, visualZ - 0.35f);
            return;
        }

        if (viewMode == SliceViewMode.Shelter && IsPlayerNearBoat() && HasUnsecuredHarvestForBoarding())
        {
            boatBoardPromptText.text = "先安置潮获";
            boatBoardPromptText.color = new Color(0.92f, 0.76f, 0.5f, 0.9f);
            boatBoardPromptText.transform.localPosition = new Vector3(GetBoatBoardingX(), boatAnchor.y + 1.05f, visualZ - 0.35f);
            return;
        }

        if (IsPlayerNearInteriorDoor())
        {
            boatBoardPromptText.text = "F 进屋";
            boatBoardPromptText.color = new Color(1f, 0.82f, 0.43f, 0.94f);
            boatBoardPromptText.transform.localPosition = new Vector3(GetInteriorDoorX() - 0.2f, GetPlayerLaneY(WalkLane.Deck) + 0.75f, visualZ - 0.35f);
            return;
        }


        bool canShowPrompt = viewMode == SliceViewMode.Shelter && !sailTripActive && state != SliceState.FinalDeparture && IsBoatBoardWindowOpen() && IsPlayerNearBoat();
        if (!canShowPrompt)
        {
            boatBoardPromptText.text = string.Empty;
            return;
        }

        bool nearBoat = IsPlayerNearBoat();
        boatBoardPromptText.text = IsDepartureReady() ? "F 离开" : "F 短航";
        boatBoardPromptText.color = nearBoat
            ? new Color(1f, 0.86f, 0.44f, 0.96f)
            : new Color(0.86f, 0.95f, 0.9f, 0.72f);
        boatBoardPromptText.transform.localPosition = new Vector3(GetBoatBoardingX(), boatAnchor.y + 1.05f, visualZ - 0.35f);
    }

    private string BuildActiveControlHint()
    {
        if (arrivalVignetteActive)
        {
            return $"F / Space 跳过短镜头\n{lastActionHint}";
        }

        if (boatViewTransition != BoatViewTransition.None)
        {
            if (boatViewTransition == BoatViewTransition.EnteringMooring ||
                boatViewTransition == BoatViewTransition.ExitingMooring)
            {
                return $"沿同一段湿木路切换视野；人物、海面和货物位置不变。\n{lastActionHint}";
            }
            if (boatViewTransition == BoatViewTransition.Boarding)
            {
                return $"正在上船：扶住船艉，跨过船舷。\n{lastActionHint}";
            }
            if (boatViewTransition == BoatViewTransition.Returning)
            {
                return $"正在靠岸：船身稳定后走回湿木路。\n{lastActionHint}";
            }
            return boatViewTransition == BoatViewTransition.EnteringInterior
                ? $"正在进屋：走到门槛后推门进入生活层。\n{lastActionHint}"
                : $"正在出屋：推门后迈回同一段外廊。\n{lastActionHint}";
        }

        if (state == SliceState.FinalDeparture)
        {
            return "出航中：高脚屋被海收回，船朝灯塔走。   R 重来";
        }

        if (viewMode == SliceViewMode.Sailing)
        {
            string returnHint = CanReturnFromSailing() ? "F 返航" : "向左回归航点";
            string clueHint = CanSeeLighthouse()
                ? (sailingClueCollected ? "已确认灯塔航线" : "向右确认灯塔航线")
                : (sailingClueCollected
                    ? "灯塔还在雾后，带线索返航"
                    : EnableSailingBuoyGameplay && !sailingBuoyChecked
                        ? "先读近岸旧浮标"
                        : !sailingSalvageCollected
                            ? "沿漂浮带找补船料"
                            : "继续穿过残骸带；漩涡还在更远处");
            string bailHint = sailingWaterIngress01 > 0.02f
                ? (sailingBailing ? "正在舀水，船速下降" : "离开调查点按住 F 舀水")
                : "舱内干燥";
            return $"A/D 控方向   W/S 张帆/收帆   {returnHint}   {clueHint}\n{GetSailingWindText()} · {GetSailTrimText()} · {bailHint} · {GetReturnPressureText()}   {lastActionHint}";
        }

        if (viewMode == SliceViewMode.Interior)
        {
            if (playerLane == WalkLane.InteriorLoft)
            {
                string forecast = HasCurrentLoftForecast() ? GetLoftForecastText() : "左端海图台按 F 观潮";
                return $"A/D 走动   梯口 S 下生活层   床边 F 休息   {forecast}\n{lastActionHint}";
            }

            if (playerLane == WalkLane.InteriorUpper)
            {
                return $"A/D 走动   左梯口 W 上阁楼   右梯口 S 下工作层   门、灶台、储物架都在本层\n{lastActionHint}";
            }

            string prepHint = IsTidePrepWindowOpen()
                ? "可检查绳、桶、补桩"
                : "白天可查看真实水线，备潮留到黄昏";
            return $"A/D 走动   右侧梯口 W 上生活层   {prepHint}\n{lastActionHint}";
        }

        string boatHint = IsDepartureReady()
            ? (IsPlayerNearBoat() ? "F 离开" : "去船边 F 离开")
            : IsPlayerNearBoat()
                ? HasUnsecuredHarvestForBoarding() ? "先安置潮获" : "F 短航"
                : "靠近船可 F 短航";
        if (state == SliceState.LowTidePlanning)
        {
            if (dayNightPhase == DayNightPhase.Night)
            {
                string prepHint = HasPreparedTidePrep() ? $"已备{GetPrepChoiceName(selectedPrepChoice)}，可改选" : "屋内工作台按住 F 备绳索 / 木桶 / 木桩";
                return $"夜里不新出船、不新布网或外修；已下网可抢收   {prepHint}   灯边 F 睡到黎明\n{lastActionHint}";
            }

            string forecastHint = HasLampForecast()
                ? $"{GetLampForecastText()}   "
                : "";
            return $"A/D 走动   {GetNetRigControlHint()}   {forecastHint}{boatHint}   R 重来\n{lastActionHint}";
        }

        if (state == SliceState.TideRising)
        {
            string tideNetHint = netSecuredEarly
                ? "网已提前收住"
                : !netDeployed
                    ? GetNetRigControlHint()
                    : netCatchResolved
                    ? $"网负载 {netCatchBundleTier}/3；网桩旁 W/S 调网口 · 按住 F 收网"
                    : "网桩旁 W/S 调网口；绞盘旁 W 收索进网 / S 放索入海";
            return $"A/D 继续行动   {boatHint}   {tideNetHint}   潮水自然推进\n{lastActionHint}";
        }

        if (state == SliceState.EbbCollect)
        {
            return $"A/D 走动   网桩旁 W/S 调网口 · 按住 F 收网   {boatHint}\n{lastActionHint}";
        }

        if (state == SliceState.RepairMoment && !repairChoiceApplied)
        {
            return $"把材料带到承重柱 / 船边 / 屋内睡铺、灶台或漏雨处，按住 F 施工；缺料也可回床边休息   R 重来\n{lastActionHint}";
        }

        string eveningPrepHint = HasPreparedTidePrep()
            ? $"已备{GetPrepChoiceName(selectedPrepChoice)}，可在屋内改选"
            : "屋内三处工作点按住 F 完成一项潮前准备";
        return $"{eveningPrepHint}   床边按 F 休息   R 重来\n{lastActionHint}";
    }

    private string GetNetRigPromptText()
    {
        if (netRigStep == NetRigStep.Stored)
        {
            return CanStartNewNetRigAtCurrentTime() ? "F 拿网" : "天亮再布网";
        }

        if (netRigStep == NetRigStep.Carrying)
        {
            return netRigActionProgress > 0f
                ? $"按住 F 系第一端 {Mathf.RoundToInt(netRigActionProgress * 100f)}%"
                : "按住 F 系第一端";
        }

        if (netRigStep == NetRigStep.FirstEndTied)
        {
            return "向右走到第二桩展开网身";
        }

        if (netRigStep == NetRigStep.Unrolled)
        {
            return netRigActionProgress > 0f
                ? $"按住 F 系第二端 {Mathf.RoundToInt(netRigActionProgress * 100f)}%"
                : "按住 F 系第二端";
        }

        if (netRigStep == NetRigStep.SecondEndTied)
        {
            return "按住 F 连续放沉纲";
        }

        if (netRigStep == NetRigStep.Lowering)
        {
            return $"按住 F 放深 {Mathf.RoundToInt(netSetDepth01 * 100f)}% · 松开固定";
        }

        return string.Empty;
    }

    private string GetNetRigControlHint()
    {
        if (netDeployed)
        {
            return $"W/S 调网口 · 当前入水 {Mathf.RoundToInt(netSetDepth01 * 100f)}%深";
        }

        if (netRigStep == NetRigStep.Stored)
        {
            return "先到网团旁 F 拿网";
        }

        if (netRigStep == NetRigStep.Carrying)
        {
            return "抱网到左侧主桩，按住 F 系牢";
        }

        if (netRigStep == NetRigStep.FirstEndTied)
        {
            return "向右走，让网身从第一桩连续展开到第二桩";
        }

        if (netRigStep == NetRigStep.Unrolled)
        {
            return "到第二桩重新按住 F 系牢自由端";
        }

        if (netRigStep == NetRigStep.SecondEndTied)
        {
            return "重新按住 F 连续放沉纲；浅挂省网，深挂覆盖更低的水层也更吃流";
        }

        return $"继续按住 F 放沉纲，当前 {Mathf.RoundToInt(netSetDepth01 * 100f)}%；松开固定";
    }

    private string BuildLoopGoalText()
    {
        if (arrivalVignetteActive)
        {
            return "海难后：先活着抵达那座旧高脚屋。";
        }

        if (state == SliceState.FinalDeparture)
        {
            return "目标：向灯塔离开。";
        }

        if (viewMode == SliceViewMode.Sailing)
        {
            if (extraSaltWoodOwner == ExtraSaltWoodOwner.HookingToBoat &&
                sailingSalvageHookProgress < 0.995f)
            {
                return sailingSalvageTension01 >= TideContinuousSalvageModel.CriticalTension01
                    ? "目标：绳已经过紧，先收帆贴流，再继续收绳。"
                    : "目标：按住 F 逐段收绳；松手可先操帆稳住船身。";
            }

            if (sailingRangeBlockedThisTrip && !sailingClueCollected)
            {
                return "目标：外海碎浪超过当前船况；带近处线索返航并补船。";
            }

            if (CanSeeLighthouse())
            {
                return sailingClueCollected ? "目标：确认航线后自己返航。" : "目标：向右确认灯塔航线。";
            }

            if (sailingClueCollected)
            {
                return "目标：带航线纸回左侧归航点。";
            }

            if (EnableSailingBuoyGameplay && !sailingBuoyChecked)
            {
                return "目标：先靠近旧浮标读回航方向。";
            }

            if (!sailingSalvageCollected)
            {
                return "目标：观察漂物尾流，收帆把相对速度降下来，再在第二段钩取浮木和油布。";
            }

            return "目标：穿过近岸与漂浮带，去第三段沉船残骸取方位纸。";
        }

        if (viewMode == SliceViewMode.Interior)
        {
            return playerLane == WalkLane.InteriorLoft
                ? HasCurrentLoftForecast()
                    ? "目标：根据观潮结果，到工作层选择保网、保船或保屋。"
                    : "目标：在阁楼海图台按 F 观察下一次高潮，再决定备潮和出航。"
                : playerLane == WalkLane.InteriorUpper
                    ? "目标：在生活层存放材料、修灶台；通过两端斜梯去工作层或阁楼。"
                    : "目标：在桩间工作层准备下一潮；风暴增水会先从这里进来。";
        }

        if (state == SliceState.LowTidePlanning)
        {
            if (dayNightPhase == DayNightPhase.Night)
            {
                return HasPreparedTidePrep()
                    ? $"目标：{GetPrepChoiceName(selectedPrepChoice)}已备好；上阁楼到床边休息至黎明。"
                    : "目标：回屋存放、内修或备一件工具；已下水的网仍可抢收。";
            }

            if (IsDepartureReady())
            {
                return "目标：去船边按 F 离开。";
            }

            if (HasLampForecast())
            {
                return "目标：海图已缩小下一次高潮范围；去网桩自行决定浅挂还是深挂。";
            }

            return CanSeeLighthouse()
                ? "目标：航线开了，去船边试航灯塔。"
                : $"目标：{GetNetRigControlHint()}。";
        }

        if (state == SliceState.TideRising)
        {
            if (netSecuredEarly)
            {
                return $"目标：已提前收住{GetHarvestName()} {netCatchBundleTier}/3；潮还在走，可短航或照看屋子。";
            }

            if (netTouched && !netCatchResolved)
            {
                if (netCaptureProgress01 > 0.001f)
                {
                    return $"目标：漂物正在网口缠挂 {Mathf.RoundToInt(netCaptureProgress01 * 100f)}%；可以抬网减载，但可能让它漏过。";
                }

                return "目标：湿网正在承受水流，但漂物还没进入网口；不用守在网边，可转导流绳或短航。";
            }

            if (netCatchResolved)
            {
                return $"目标：{GetHarvestName()}负载 {netCatchBundleTier}/3；现在收更稳，继续留潮里收益更高也更伤网。";
            }

            return "目标：读漂物，决定导进网冒险加收，或放过保网；也可短航。";
        }

        if (state == SliceState.EbbCollect)
        {
            return "目标：退潮了，回网桩旁按住 F 收网。";
        }

        if (state == SliceState.RepairMoment && !repairChoiceApplied)
        {
            return stiltIntegrity <= 0
                ? "目标：柱脚已经失守；优先把收获带去修柱，或承担下一次进水风险。"
                : "目标：把收获带到柱脚、船边或屋灯前。";
        }

        return HasPreparedTidePrep()
            ? $"目标：{GetPrepChoiceName(selectedPrepChoice)}已备好；回灯边进入下一潮。"
            : "目标：在屋内选一项潮前准备，再回灯边进入下一潮。";
    }

    private string BuildCompactHudSummary(string phase, float storm01)
    {
        int tidePercent = Mathf.RoundToInt(tideStrength * 100f);
        int stormPercent = Mathf.RoundToInt(storm01 * 100f);
        string dayText = GetDayNightName();
        int dayPercent = Mathf.RoundToInt(dayProgress01 * 100f);
        string shoreWorkText = state == SliceState.TideRising || state == SliceState.EbbCollect || state == SliceState.RepairMoment
            ? $"  岔流{(routingDecisionLocked ? tideRoutingMode == TideRoutingMode.FeedNet ? "进网" : "入海" : "未定")}"
            : "";
        string repairChoiceText = state == SliceState.RepairMoment
            ? $"  选{(repairChoiceApplied ? GetRepairChoiceName(pendingRepairChoice) : GetRepairChoiceName(GetRecommendedRepairChoice()))}"
            : "";
        string forecastText = HasLampForecast()
            ? $"  预警{lampForecastCharges} {GetForecastHighWaterRangeText(chartForecastSnapshot)}"
            : "";
        return $"{phase}  {dayText}{dayPercent}%  月{moonAgeDays:0.0}天  潮{tidePercent}%\n" +
            $"网 {GetShortLineName()}  备 {GetPrepChoiceName(selectedPrepChoice)}  {GetNetIntegrityText()}  收 {GetHarvestName()}  负载{netCatchBundleTier}/3\n" +
            $"材料 {GetMaterialStockText()}  包{(hasSalvageBag ? "旧帆布" : "无")}  房 地基{stiltIntegrity}/屋面{roofIntegrity}/灶{stoveCondition}/内室{interiorComfort}\n" +
            $"船 壳{boatHullIntegrity}/帆{boatSailIntegrity}/舱{boatCabinIntegrity}  体温{GetBodyTemperatureCelsius():0.0}C  死亡{deathCount}  灯{lighthouseClues}/{requiredLighthouseClues}  {GetRouteStateText()}  暴{stormPercent}%{shoreWorkText}{repairChoiceText}{forecastText}";
    }

    private string BuildDebugHudText(string phase, float storm01)
    {
        float secondsToDark = GetSecondsUntilDayProgress(0.82f);
        float secondsToDawn = GetSecondsUntilDayProgress(0.2f);
        float distanceFromHome = viewMode == SliceViewMode.Sailing
            ? Mathf.Abs(sailingBoatX - sailingHomeX)
            : viewMode == SliceViewMode.Interior
                ? 0f
                : Vector2.Distance(playerPosition, GetHomePlayerPosition());
        string outdoorScreen = viewMode == SliceViewMode.Shelter
            ? mooringScreenActive ? "泊位" : "归处"
            : viewMode == SliceViewMode.Sailing ? "短航" : viewMode == SliceViewMode.Interior ? "屋内" : "瞭望";
        string previousPeakText = highWaterMemory.HasPreviousCycle
            ? $"{highWaterMemory.PreviousCyclePeakY:0.00}m"
            : "无完整潮";
        string heavyWreckText = heavyWreckSalvage != null
            ? heavyWreckSalvage.GetDebugSummary()
            : "重物 未接入";
        string waterText = barrenIsland != null
            ? $"饮水 池{barrenIsland.Cistern.StoredLiters:F1}L/盐{barrenIsland.Cistern.SaltFraction01:P2} · " +
              $"应急罐{stormRescueWater.Liters:F1}L · 今日已饮{waterConsumedSinceLastRest:F1}L · 昨夜缺{lastRestWaterShortfallLiters:F1}L"
            : "饮水 未接入";
        string rescueManifestText = $"暴潮清单 在场0x{GetStormRescuePresentMask():X} · " +
            $"预留木{stormRescueReservedTimber}/柴{stormRescueReservedFuelBundles}/图{stormRescueReservedChartClues} · 干柴库存{dryFuelBundles}";
        return $"{BuildPlayerHudSummary(phase, storm01)}\n" +
            $"距天黑 {FormatDebugDuration(secondsToDark)} · 距天亮 {FormatDebugDuration(secondsToDawn)} · " +
            $"离家 {distanceFromHome:0.0}m · 画面 {outdoorScreen} · F3 隐藏\n" +
            $"潮痕 上一潮 {previousPeakText} · 本潮已到 {highWaterMemory.CurrentCyclePeakY:0.00}m · 完整潮 {highWaterMemory.CompletedCycleCount}\n" +
            $"潮源 #{tideSourceBatchId} {GetTideDriftProvenanceName(currentTideDriftField.NearshoreBatch.Provenance)}/{GetHarvestName(tideSourceHarvest)} " +
            $"路程{incomingHarvestTravel01:0.00} · 盐木 #{extraSaltWoodBatchId} {extraSaltWoodOwner} 路程{outerWreckTravel01:0.00}\n" +
            $"{waterText}\n" +
            $"{rescueManifestText}\n" +
            $"{heavyWreckText}\n" +
            $"开发循环：看潮 -> 布网 -> 回收实物 -> 修屋/修船 -> 短航 · 当前：{lastActionHint}";
    }

    private float GetSecondsUntilDayProgress(float targetProgress01)
    {
        float normalizedNow = Mathf.Repeat(dayProgress01, 1f);
        float normalizedTarget = Mathf.Repeat(targetProgress01, 1f);
        float normalizedDelta = normalizedTarget >= normalizedNow
            ? normalizedTarget - normalizedNow
            : 1f - normalizedNow + normalizedTarget;
        return normalizedDelta * Mathf.Max(4f, dayLengthSeconds);
    }

    private static string FormatDebugDuration(float seconds)
    {
        int rounded = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return $"{rounded / 60:00}:{rounded % 60:00}";
    }

    private string BuildPlayerHudSummary(string phase, float storm01)
    {
        float phase01 = Mathf.Repeat(moonAgeDays / 29.53f, 1f);
        int moonPercent = Mathf.RoundToInt(GetMoonIllumination01(phase01) * 100f);
        string stormText = storm01 >= 0.7f
            ? " · 暴潮已到"
            : storm01 >= 0.35f
                ? " · 风雨逼近"
                : storm01 >= 0.08f ? " · 远云压低" : string.Empty;
        string shelterText = shelterBreachThisTide
            ? " · 柱脚失守"
            : shelterStressAppliedThisTide && shelterRawStressThisTide > 0
                ? $" · 柱脚余量 {stiltIntegrity}"
                : string.Empty;
        string tideMotion = tideCurrentlyRising ? "涨潮" : "退潮";
        string sailingText = viewMode == SliceViewMode.Sailing
            ? $" · 船况 {boatReadiness}/{requiredBoatReadiness} · {GetSailTrimText()} {Mathf.RoundToInt(sailingSailTrim01 * 100f)}% · 舱水 {Mathf.RoundToInt(sailingWaterIngress01 * 100f)}%"
            : string.Empty;
        string bodyText = bodyWarmth01 < 0.62f ? $" · 体温 {GetBodyTemperatureCelsius():0.0}C" : string.Empty;
        string deathText = deathFeedbackTimer > 0f ? $" · 死亡：{lastDeathReason}" : string.Empty;
        return $"{GetClockText()} · 第{tideRound + 1}潮 · {GetDayNightName()} · {tideMotion} · {phase} · {GetPredictedHighWaterBand()} · 月光 {moonPercent}%{bodyText}{stormText}{shelterText}{sailingText}{deathText}";
    }

    private float GetBodyTemperatureCelsius()
    {
        return Mathf.Lerp(32.2f, 36.8f, Mathf.Clamp01(bodyWarmth01));
    }

    private string GetClockText()
    {
        int totalMinutes = Mathf.FloorToInt(Mathf.Repeat(dayProgress01, 1f) * 24f * 60f);
        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;
        return $"{hours:00}:{minutes:00}";
    }

    private string GetPrepChoiceName(TidePrepChoice choice)
    {
        if (choice == TidePrepChoice.None)
        {
            return "无";
        }

        if (choice == TidePrepChoice.Bucket)
        {
            return "木桶";
        }

        if (choice == TidePrepChoice.Stake)
        {
            return "木桩";
        }

        return "绳索";
    }

    private string GetPrepRoleName(TidePrepChoice choice)
    {
        if (choice == TidePrepChoice.Bucket)
        {
            return "保船";
        }

        if (choice == TidePrepChoice.Stake)
        {
            return "保屋";
        }

        if (choice == TidePrepChoice.Rope)
        {
            return "保网";
        }

        return "未备";
    }

    private string GetPrepEffectText(TidePrepChoice choice)
    {
        if (choice == TidePrepChoice.None)
        {
            return "这一潮没有额外准备。";
        }

        if (choice == TidePrepChoice.Bucket)
        {
            return "木桶带上船后舀水更快，但仍要停舵亲手排水；它不会改变潮里漂来的东西。";
        }

        if (choice == TidePrepChoice.Stake)
        {
            return "先把短桩顶在柱脚，这潮等于提前做过一次柱脚检查。";
        }

        return "多绑一圈绳，强潮拖网时少掉一点破网压力。";
    }

    private string GetPrepDecisionText(TidePrepChoice choice)
    {
        if (choice == TidePrepChoice.None)
        {
            return "昨夜没有额外准备。";
        }

        return $"{GetPrepChoiceName(choice)}已放好：{GetPrepEffectText(choice)} ";
    }

    private string GetNetIntegrityText()
    {
        if (netIntegrity <= 0)
        {
            return "网况 破网";
        }

        if (netIntegrity == 1)
        {
            return "网况 裂";
        }

        if (netIntegrity == 2)
        {
            return "网况 松";
        }

        return "网况 稳";
    }

    private int GetCatchBundleBonus()
    {
        if (netBrokeThisTide || currentHarvest == HarvestKind.None || currentHarvest == HarvestKind.Trash)
        {
            return 0;
        }

        return Mathf.Clamp(netCatchBundleTier - 1, 0, 2);
    }

    private string GetCatchBundleText()
    {
        if (netCatchBundleTier >= 3)
        {
            return "满网的";
        }

        if (netCatchBundleTier == 2)
        {
            return "两份";
        }

        return string.Empty;
    }

    private string GetRouteStateText()
    {
        if (CanSeeLighthouse())
        {
            return "路 可试航";
        }

        if (lighthouseClues > 0 || shortSailCount > 0 || boatReadiness > 0)
        {
            return "路 待潮";
        }

        return "路 雾后";
    }

    private string GetNearshoreWorkControlHint()
    {
        if (routingDecisionLocked)
        {
            return tideRoutingMode == TideRoutingMode.FeedNet ? "盐木已进网" : "盐木已入海";
        }

        if (!IsNearshoreWorkWindowOpen())
        {
            return "绞盘已淹";
        }

        if (!netDeployed)
        {
            return "先把网系好";
        }

        return IsPlayerNearShoreWork()
            ? "W 收索进网 / S 放索入海"
            : "绞盘可 W/S 收放";
    }

    private string GetDayNightName()
    {
        if (dayNightPhase == DayNightPhase.Dawn)
        {
            return "黎明";
        }

        if (dayNightPhase == DayNightPhase.Day)
        {
            return "白昼";
        }

        if (dayNightPhase == DayNightPhase.Dusk)
        {
            return "黄昏";
        }

        return "夜";
    }

    private string GetShortLineName()
    {
        return $"{Mathf.RoundToInt(netSetDepth01 * 100f)}%深";
    }

    private string GetHarvestName()
    {
        return GetHarvestName(currentHarvest);
    }

    private static string GetHarvestName(HarvestKind harvest)
    {
        if (harvest == HarvestKind.Wood)
        {
            return "盐木";
        }

        if (harvest == HarvestKind.Relic)
        {
            return "油布海图包";
        }

        if (harvest == HarvestKind.Trash)
        {
            return "缠网废料";
        }

        if (harvest == HarvestKind.Fish)
        {
            return "小鱼";
        }

        return "无";
    }

    private string GetRepairChoiceName(RepairChoice choice)
    {
        if (choice == RepairChoice.Stilt)
        {
            return "修地基";
        }

        if (choice == RepairChoice.Cistern)
        {
            return "封蓄水池裂口";
        }

        if (choice == RepairChoice.Sail)
        {
            return "补帆";
        }

        if (choice == RepairChoice.Lamp)
        {
            return "修灶台";
        }

        if (choice == RepairChoice.Roof)
        {
            return "修屋顶";
        }

        if (choice == RepairChoice.InteriorSeal)
        {
            return "封围护与入口";
        }

        if (choice == RepairChoice.Workbench)
        {
            return "修工作台";
        }

        if (choice == RepairChoice.Bed)
        {
            return "修睡铺";
        }

        if (choice == RepairChoice.ChartRadio)
        {
            return "修海图潮尺";
        }

        if (choice == RepairChoice.Hull)
        {
            return "补船体";
        }

        if (choice == RepairChoice.Cabin)
        {
            return "修船舱";
        }

        if (choice == RepairChoice.Net)
        {
            return "清洗补网";
        }

        return "未选";
    }

    private RepairChoice GetRecommendedRepairChoice()
    {
        if (currentHarvest == HarvestKind.Trash)
        {
            return RepairChoice.Net;
        }

        if (currentHarvest == HarvestKind.Wood)
        {
            return stiltIntegrity <= roofIntegrity ? RepairChoice.Stilt : RepairChoice.Roof;
        }

        if (currentHarvest == HarvestKind.Relic)
        {
            return boatSailIntegrity <= boatCabinIntegrity ? RepairChoice.Sail : RepairChoice.Cabin;
        }

        return RepairChoice.Lamp;
    }

    private Vector2 GetRepairChoicePosition(RepairChoice choice)
    {
        bool useFormalHouse = GetFormalHouseSprite() != null;
        if (choice == RepairChoice.Stilt)
        {
            return HasHdFormalHouse()
                ? new Vector2(GetShoreWorkX(), -1.68f)
                : useFormalHouse ? new Vector2(-0.96f, -1.18f) : houseAnchor + new Vector2(-1.12f, -0.54f);
        }

        if (choice == RepairChoice.Cistern)
        {
            // 人站在池体左侧的可见裸岩上施工，而不是穿进池壁或跨出岛缘。
            return new Vector2(
                TideBarrenIslandController.CisternX - 0.72f,
                GetPlayerLaneY(WalkLane.TideFlat));
        }


        if (choice == RepairChoice.Net)
        {
            return new Vector2(GetNetStoredX(), GetPlayerLaneY(WalkLane.TideFlat));
        }

        if (choice == RepairChoice.Hull ||
            choice == RepairChoice.Sail ||
            choice == RepairChoice.Cabin)
        {
            // The old points were offsets from boatAnchor (for example hull x=9.31)
            // while the visible pier and walk lane end at the stern access (x=7.50).
            // That made all boat work logically present but physically unreachable.
            // The survivor now works from three distinct places on the final stable
            // pier: timber pile/stern hull, sheet line, and cockpit entry. V52 still
            // owns the actual changing component on the moving boat; these positions
            // only describe where a human can safely stand and use tools.
            float boardingX = GetBoatBoardingX();
            float accessOffset = choice == RepairChoice.Hull
                ? -0.58f
                : choice == RepairChoice.Sail
                    ? -0.29f
                    : 0f;
            return new Vector2(
                boardingX + accessOffset,
                GetPlayerLaneY(WalkLane.TideFlat));
        }

        if (choice == RepairChoice.Lamp)
        {
            if (viewMode == SliceViewMode.Interior)
            {
                return new Vector2(GetInteriorLampX(), GetPlayerLaneY(WalkLane.InteriorUpper));
            }

            return HasHdFormalHouse()
                ? new Vector2(-3.95f, 0.18f)
                : useFormalHouse ? new Vector2(-4.42f, 0.18f) : houseAnchor + new Vector2(-0.18f, 0.88f);
        }


        if (choice == RepairChoice.InteriorSeal)
        {
            return new Vector2(
                GetV35OwnerWorldX("EntryDoor", HasCompleteV35HouseInteriorPresentation()
                    ? GetV35WorldAnchorX(new Vector2(790f, 1288f))
                    : -4.9f),
                GetPlayerLaneY(WalkLane.InteriorUpper));
        }

        if (choice == RepairChoice.Workbench)
        {
            return new Vector2(
                GetV35OwnerWorldX("Workbench", GetInteriorStorageX()),
                GetPlayerLaneY(WalkLane.InteriorUpper));
        }

        if (choice == RepairChoice.Bed)
        {
            return new Vector2(
                GetV35OwnerWorldX("Bed", GetInteriorBedX()),
                GetPlayerLaneY(WalkLane.InteriorUpper));
        }

        if (choice == RepairChoice.ChartRadio)
        {
            return new Vector2(
                GetV35OwnerWorldX("ChartRadio", GetInteriorLoftLookoutX()),
                GetPlayerLaneY(WalkLane.InteriorUpper));
        }

        if (choice == RepairChoice.Roof)
        {
            return new Vector2(
                HasCompleteV35HouseInteriorPresentation()
                    ? GetV35WorldAnchorX(new Vector2(960f, 760f))
                    : -0.72f,
                GetPlayerLaneY(WalkLane.InteriorLoft));
        }

        return houseAnchor;
    }

    private Sprite GetRepairChoiceSprite(RepairChoice choice)
    {
        if (choice == RepairChoice.Stilt)
        {
            return GetRepairPatchSprite();
        }

        if (choice == RepairChoice.Sail)
        {
            return GetBoatSailPatchStitchSprite();
        }

        if (choice == RepairChoice.Hull || choice == RepairChoice.Roof ||
            choice == RepairChoice.InteriorSeal || choice == RepairChoice.Workbench ||
            choice == RepairChoice.Bed || choice == RepairChoice.ChartRadio ||
            choice == RepairChoice.Cabin || choice == RepairChoice.Net)
        {
            return GetRepairPatchSprite();
        }

        if (choice == RepairChoice.Lamp)
        {
            return GetShelterLampSprite();
        }

        return GetFoamSprite();
    }

    private string GetHarvestPayoffText()
    {
        if (currentHarvest == HarvestKind.Wood)
        {
            return "盐木和缠绳能补承重柱、屋面或船壳；先看哪一处真的缺料。";
        }

        if (currentHarvest == HarvestKind.Relic)
        {
            return "油布海图包里有旧布、残图和罗盘铁件，适合补帆、隔潮或做舱盖连接件。";
        }

        if (currentHarvest == HarvestKind.Trash)
        {
            if (nearshoreWorkDone)
            {
                return "缠网废料里能拆出绳和铁件；提前导流替主网吃掉了一次冲击。";
            }

            return "缠网废料会拖坏网，却也能拆出补网绳和舱盖铁件。";
        }

        if (currentHarvest == HarvestKind.Fish)
        {
            return "小鱼先作为食物；有了热食才能试灶、烘衣并更快恢复体温。";
        }

        return "网是空的，只留下湿绳和盐。";
    }

    private static Sprite GetCaughtNetItemSprite(
        HarvestKind harvest,
        int index,
        int stableBatchId = 0,
        bool freeDrifting = false)
    {
        if (HasCompleteV59FindPresentation())
        {
            TideV59FindKind v59Kind = ToV59FindKind(harvest);
            int variantIndex = TideV59FindPresentationModel.ResolveVariantIndex(
                v59Kind,
                index,
                stableBatchId,
                freeDrifting);
            Sprite v59Sprite = formalV59FindCatalog.Get(v59Kind, variantIndex);
            if (v59Sprite != null)
            {
                return v59Sprite;
            }
        }

        if (harvest == HarvestKind.Wood)
        {
            return index == 0 ? GetWoodSprite() : GetLooseSaltWoodSprite();
        }

        if (harvest == HarvestKind.Relic)
        {
            return GetRelicPieceSprite(index);
        }

        if (harvest == HarvestKind.Trash)
        {
            Sprite debris = GetFormalWetDebrisSprite();
            return index % 2 == 0 && debris != null ? debris : GetWaterWrackSprite();
        }

        return GetFishSprite();
    }

    private static bool TryGetV59FindSpec(
        HarvestKind harvest,
        int pieceIndex,
        int stableBatchId,
        bool freeDrifting,
        out TideV59FindSpec spec)
    {
        if (!HasCompleteV59FindPresentation() || harvest == HarvestKind.None)
        {
            spec = default;
            return false;
        }

        TideV59FindKind kind = ToV59FindKind(harvest);
        int variantIndex = TideV59FindPresentationModel.ResolveVariantIndex(
            kind,
            pieceIndex,
            stableBatchId,
            freeDrifting);
        spec = TideV59FindPresentationModel.GetSpec(kind, variantIndex);
        return true;
    }

    private static TideV59FindKind ToV59FindKind(HarvestKind harvest)
    {
        switch (harvest)
        {
            case HarvestKind.Wood:
                return TideV59FindKind.Wood;
            case HarvestKind.Trash:
                return TideV59FindKind.Trash;
            case HarvestKind.Relic:
                return TideV59FindKind.Relic;
            default:
                return TideV59FindKind.Fish;
        }
    }

    private Color GetCaughtNetItemColor()
    {
        return GetCaughtNetItemColor(currentHarvest);
    }

    private static Color GetCaughtNetItemColor(HarvestKind harvest)
    {
        if (harvest == HarvestKind.Wood)
        {
            return new Color(0.82f, 0.55f, 0.28f, 0.98f);
        }

        if (harvest == HarvestKind.Relic)
        {
            return new Color(0.94f, 0.75f, 0.34f, 0.96f);
        }

        if (harvest == HarvestKind.Trash)
        {
            return new Color(0.55f, 0.66f, 0.58f, 0.86f);
        }

        return new Color(0.78f, 0.94f, 0.86f, 0.9f);
    }

    private Vector2 GetCaughtNetItemScale(int index)
    {
        return GetHarvestWorldSize(currentHarvest, index);
    }

    private void Set(SpriteRenderer renderer, Vector2 position, Vector2 scale, Color color, float rotationZ)
    {
        if (renderer == null)
        {
            return;
        }

        renderer.enabled = true;
        renderer.color = color;
        renderer.transform.localPosition = new Vector3(position.x, position.y, visualZ + renderer.sortingOrder * -0.001f);
        renderer.transform.localScale = new Vector3(scale.x, scale.y, 1f);
        renderer.transform.localRotation = Quaternion.Euler(0f, 0f, rotationZ);
    }

    private void SetUniformSprite(
        SpriteRenderer renderer,
        Sprite sprite,
        Vector2 semanticPivotPosition,
        float uniformScale,
        Color color,
        float rotationZ,
        bool flipX)
    {
        if (renderer == null || sprite == null || uniformScale <= 0f)
        {
            SetEnabled(renderer, false);
            return;
        }

        renderer.sprite = sprite;
        renderer.flipX = flipX;
        renderer.enabled = true;
        renderer.color = color;
        renderer.transform.localPosition = new Vector3(
            semanticPivotPosition.x,
            semanticPivotPosition.y,
            visualZ + renderer.sortingOrder * -0.001f);
        renderer.transform.localScale = new Vector3(uniformScale, uniformScale, 1f);
        renderer.transform.localRotation = Quaternion.Euler(0f, 0f, rotationZ);
    }

    private void SetUniformContractLayer(
        SpriteRenderer renderer,
        TideV31BoatRuntimeCatalog.LayerEntry layer,
        Vector2 boatRootWorldPosition,
        float uniformScale,
        Color color,
        float rotationZ,
        bool flipX)
    {
        if (renderer == null || layer == null || layer.Sprite == null)
        {
            SetEnabled(renderer, false);
            return;
        }

        Vector2 layerPosition = TideV31BoatPresentationModel.EvaluatePoseWorldPosition(
            boatRootWorldPosition,
            layer.WorldOffsetFromBoatPivot,
            rotationZ,
            flipX,
            uniformScale);
        SetUniformSprite(
            renderer,
            layer.Sprite,
            layerPosition,
            uniformScale,
            color,
            rotationZ,
            flipX);
    }

    private bool HasCompleteV39BoatPresentation()
    {
        if (v39DamagedBoatLayers == null || v39RepairedBoatLayers == null ||
            v39DamagedBoatLayers.Length != TideV39BoatPresentationModel.LayerCount ||
            v39RepairedBoatLayers.Length != TideV39BoatPresentationModel.LayerCount)
        {
            return false;
        }

        for (int i = 0; i < TideV39BoatPresentationModel.LayerCount; i++)
        {
            if (v39DamagedBoatLayers[i] == null || v39RepairedBoatLayers[i] == null)
            {
                return false;
            }
        }

        return true;
    }

    private void SetUniformV39Layer(
        SpriteRenderer renderer,
        TideV39BoatLayer layer,
        bool repaired,
        Vector2 boatRootWorldPosition,
        Color tint,
        float rotationZ)
    {
        Sprite[] stateLayers = repaired ? v39RepairedBoatLayers : v39DamagedBoatLayers;
        int index = (int)layer;
        if (renderer == null || stateLayers == null || index < 0 || index >= stateLayers.Length ||
            stateLayers[index] == null)
        {
            SetEnabled(renderer, false);
            return;
        }

        Vector2 layerPosition = TideV39BoatPresentationModel.EvaluateLayerWorldPosition(
            boatRootWorldPosition,
            layer,
            repaired,
            rotationZ,
            FormalBoatFacesRight);
        SetUniformSprite(
            renderer,
            stateLayers[index],
            layerPosition,
            TideV39BoatPresentationModel.BoatRootScale,
            tint,
            rotationZ,
            FormalBoatFacesRight);
    }

    private void SetUniformV52Layer(
        SpriteRenderer renderer,
        Sprite sprite,
        Vector2 worldOffsetFromBoatPivot,
        Vector2 boatRootWorldPosition,
        Color tint,
        float rotationZ)
    {
        if (renderer == null || sprite == null)
        {
            SetEnabled(renderer, false);
            return;
        }

        Vector2 worldPosition = TideV39BoatPresentationModel.EvaluateOffsetWorldPosition(
            boatRootWorldPosition,
            worldOffsetFromBoatPivot,
            rotationZ,
            FormalBoatFacesRight,
            TideV52BoatRepairPresentationModel.BoatRootScale);
        SetUniformSprite(
            renderer,
            sprite,
            worldPosition,
            TideV52BoatRepairPresentationModel.BoatRootScale,
            tint,
            rotationZ,
            FormalBoatFacesRight);
    }

    private bool ApplyV67V52BoatPresentation(
        Vector2 waterlinePoint,
        float rotationZ,
        float worldTime,
        bool showPassenger,
        Color tint,
        out Vector2 boatRootWorldPosition)
    {
        if (!HasCompleteV52BoatRepairBase())
        {
            boatRootWorldPosition = waterlinePoint;
            return false;
        }

        TideV52BoatRepairStage hullStage = GetV52BoatRepairStage(TideV52BoatRepairOwner.HullBreach);
        TideV52BoatRepairStage sailStage = GetV52BoatRepairStage(TideV52BoatRepairOwner.SailRepair);
        TideV52BoatRepairStage cockpitStage = GetV52BoatRepairStage(TideV52BoatRepairOwner.CockpitFloor);
        TideV52BoatRepairStageAsset hullAsset = GetV52BoatRepairStageAsset(
            TideV52BoatRepairOwner.HullBreach,
            hullStage);
        TideV52BoatRepairStageAsset sailAsset = GetV52BoatRepairStageAsset(
            TideV52BoatRepairOwner.SailRepair,
            sailStage);
        TideV52BoatRepairStageAsset cockpitAsset = GetV52BoatRepairStageAsset(
            TideV52BoatRepairOwner.CockpitFloor,
            cockpitStage);
        if (hullAsset == null || sailAsset == null || cockpitAsset == null)
        {
            boatRootWorldPosition = waterlinePoint;
            return false;
        }

        boatRootWorldPosition = new Vector2(
            waterlinePoint.x,
            TideV39BoatPresentationModel.EvaluateBoatRootY(waterlinePoint.y));

        // V67/V52 的原子顺序来自资源契约：残破船的后索具和后船体仍是
        // 世界底座，舱底施工在人物后方，完整人物在中间，前船舷和破口补件
        // 在人物前方。每层使用不同 sortingOrder，避免同序透明图依赖偶然批次。
        boatBackRigRenderer.sortingOrder = 2;
        boatSailRenderer.sortingOrder = 3;
        boatHullRenderer.sortingOrder = 4;
        boatCockpitRenderer.sortingOrder = 5;
        boatCockpitRepairOwnerRenderer.sortingOrder = 6;
        boatPassengerRenderer.sortingOrder = 7;
        boatPassengerGunwaleRenderer.sortingOrder = 8;
        boatHullRepairOwnerRenderer.sortingOrder = 9;
        boatRudderRenderer.sortingOrder = 10;
        boatWaterlineOcclusionRenderer.sortingOrder = 11;

        // V52 QA 的组合底座明确使用 ServiceableDamaged，而不是在维修中途
        // 把整艘船跳到 V39 Repaired。三处结构收益只由各自 owner 表达。
        SetUniformV39Layer(
            boatBackRigRenderer,
            TideV39BoatLayer.BackRig,
            false,
            boatRootWorldPosition,
            tint,
            rotationZ);
        SetUniformV52Layer(
            boatSailRenderer,
            sailAsset.Sprite,
            sailAsset.WorldOffsetFromBoatPivot,
            boatRootWorldPosition,
            tint,
            rotationZ);
        SetUniformV39Layer(
            boatHullRenderer,
            TideV39BoatLayer.BackHull,
            false,
            boatRootWorldPosition,
            tint,
            rotationZ);
        SetUniformV52Layer(
            boatCockpitRenderer,
            formalBoatV52BaseAsset.CockpitFloorStable,
            formalBoatV52BaseAsset.CockpitFloorOffset,
            boatRootWorldPosition,
            tint,
            rotationZ);
        SetUniformV52Layer(
            boatCockpitRepairOwnerRenderer,
            cockpitAsset.Sprite,
            cockpitAsset.WorldOffsetFromBoatPivot,
            boatRootWorldPosition,
            tint,
            rotationZ);

        ApplyV39PassengerPresentation(
            waterlinePoint,
            boatRootWorldPosition,
            rotationZ,
            worldTime,
            showPassenger,
            tint,
            7,
            11);

        SetUniformV52Layer(
            boatPassengerGunwaleRenderer,
            formalBoatV52BaseAsset.FrontGunwaleStable,
            formalBoatV52BaseAsset.FrontGunwaleOffset,
            boatRootWorldPosition,
            tint,
            rotationZ);
        SetUniformV52Layer(
            boatHullRepairOwnerRenderer,
            hullAsset.Sprite,
            hullAsset.WorldOffsetFromBoatPivot,
            boatRootWorldPosition,
            tint,
            rotationZ);
        SetUniformV39Layer(
            boatRudderRenderer,
            TideV39BoatLayer.RudderRest,
            false,
            boatRootWorldPosition,
            tint,
            rotationZ);

        SetMaskEnabled(boatWaterlineMask, false);
        SetEnabled(boatWaterlineOcclusionRenderer, false);
        return true;
    }

    private bool ApplyV39BoatPresentation(
        Vector2 waterlinePoint,
        float rotationZ,
        float worldTime,
        bool showPassenger,
        Color tint,
        out Vector2 boatRootWorldPosition)
    {
        if (!HasCompleteV39BoatPresentation())
        {
            SetEnabled(boatHullRepairOwnerRenderer, false);
            SetEnabled(boatCockpitRepairOwnerRenderer, false);
            boatRootWorldPosition = waterlinePoint;
            return false;
        }

        if (ApplyV67V52BoatPresentation(
            waterlinePoint,
            rotationZ,
            worldTime,
            showPassenger,
            tint,
            out boatRootWorldPosition))
        {
            return true;
        }

        SetEnabled(boatHullRepairOwnerRenderer, false);
        SetEnabled(boatCockpitRepairOwnerRenderer, false);

        bool repaired = boatHullIntegrity >= 2 && boatSailIntegrity >= 1;
        boatRootWorldPosition = new Vector2(
            waterlinePoint.x,
            TideV39BoatPresentationModel.EvaluateBoatRootY(waterlinePoint.y));

        // V39 的顺序来自资源契约。CockpitFloor 与 BackHull 都在人物后方，
        // 人物完整保留双腿，再由独立 FrontGunwale 做现实遮挡；前景海浪最后
        // 覆盖静水线以下的船壳，避免整船像贴纸一样浮在水面上。
        boatBackRigRenderer.sortingOrder = 3;
        boatSailRenderer.sortingOrder = 4;
        boatHullRenderer.sortingOrder = 5;
        boatCockpitRenderer.sortingOrder = 5;
        boatPassengerRenderer.sortingOrder = 6;
        boatPassengerGunwaleRenderer.sortingOrder = 7;
        boatRudderRenderer.sortingOrder = 8;
        boatWaterlineOcclusionRenderer.sortingOrder = 9;

        SetUniformV39Layer(
            boatBackRigRenderer,
            TideV39BoatLayer.BackRig,
            repaired,
            boatRootWorldPosition,
            tint,
            rotationZ);
        SetUniformV39Layer(
            boatSailRenderer,
            TideV39BoatLayer.SailRest,
            repaired,
            boatRootWorldPosition,
            tint,
            rotationZ);
        SetUniformV39Layer(
            boatHullRenderer,
            TideV39BoatLayer.BackHull,
            repaired,
            boatRootWorldPosition,
            tint,
            rotationZ);
        SetUniformV39Layer(
            boatCockpitRenderer,
            TideV39BoatLayer.CockpitFloor,
            repaired,
            boatRootWorldPosition,
            tint,
            rotationZ);
        SetUniformV39Layer(
            boatPassengerGunwaleRenderer,
            TideV39BoatLayer.FrontGunwale,
            repaired,
            boatRootWorldPosition,
            tint,
            rotationZ);
        SetUniformV39Layer(
            boatRudderRenderer,
            TideV39BoatLayer.RudderRest,
            repaired,
            boatRootWorldPosition,
            tint,
            rotationZ);

        ApplyV39PassengerPresentation(
            waterlinePoint,
            boatRootWorldPosition,
            rotationZ,
            worldTime,
            showPassenger,
            tint,
            6,
            8);

        SetMaskEnabled(boatWaterlineMask, false);
        SetEnabled(boatWaterlineOcclusionRenderer, false);
        return true;
    }

    private void ApplyV39PassengerPresentation(
        Vector2 waterlinePoint,
        Vector2 boatRootWorldPosition,
        float rotationZ,
        float worldTime,
        bool showPassenger,
        Color tint,
        int regularSortingOrder,
        int drowningSortingOrder)
    {
        boatPassengerRenderer.sortingOrder = regularSortingOrder;
        SetEnabled(boatPassengerRenderer, showPassenger);
        if (!showPassenger)
        {
            return;
        }

        Vector2 seatPosition = TideV39BoatPresentationModel.EvaluateAnchorWorldPosition(
            boatRootWorldPosition,
            TideV39BoatPresentationModel.SeatTopLeft,
            rotationZ,
            FormalBoatFacesRight);
        Vector2 passengerPivot = GetBoatPassengerVisualPivot(seatPosition, rotationZ);
        if (TryGetV42SailingSurvivalFrame(
            waterlinePoint,
            passengerPivot,
            out Sprite survivalFrame,
            out Vector2 survivalPivot,
            out float survivalScale,
            out bool survivalDrowning))
        {
            boatPassengerRenderer.sortingOrder = survivalDrowning
                ? drowningSortingOrder
                : regularSortingOrder;
            SetUniformSprite(
                boatPassengerRenderer,
                survivalFrame,
                survivalPivot,
                survivalScale,
                tint,
                survivalDrowning ? 0f : rotationZ,
                FormalBoatFacesRight);
            return;
        }

        EnsureV32ArtResourcesLoaded();
        Sprite seatedFrame = GetV37BoatPassengerActionFrame(
            worldTime,
            GetStormPressure01() >= 0.58f,
            out float passengerUniformScale);
        if (seatedFrame == null && HasCompleteV32ArtPresentation())
        {
            seatedFrame = formalV32ArtCatalog.GetStableSeatedFrame();
            passengerUniformScale = BoatPassengerUniformScale;
        }

        if (seatedFrame == null)
        {
            SetEnabled(boatPassengerRenderer, false);
            return;
        }

        SetUniformSprite(
            boatPassengerRenderer,
            seatedFrame,
            passengerPivot,
            Mathf.Max(passengerUniformScale, BoatPassengerUniformScale),
            tint,
            rotationZ,
            FormalBoatFacesRight);
    }

    private bool ApplyBoatPresentation(
        Vector2 waterlinePoint,
        float rotationZ,
        float worldTime,
        bool showPassenger,
        bool isLoaded,
        bool isStorm,
        Color tint,
        out Vector2 boatRootWorldPosition)
    {
        if (ApplyV39BoatPresentation(
            waterlinePoint,
            rotationZ,
            worldTime,
            showPassenger,
            tint,
            out boatRootWorldPosition))
        {
            return true;
        }

        SetEnabled(boatBackRigRenderer, false);
        return ApplyV31BoatPresentation(
            waterlinePoint,
            rotationZ,
            worldTime,
            showPassenger,
            isLoaded,
            isStorm,
            tint,
            out boatRootWorldPosition);
    }

    private bool ApplyV31BoatPresentation(
        Vector2 waterlinePoint,
        float rotationZ,
        float worldTime,
        bool showPassenger,
        bool isLoaded,
        bool isStorm,
        Color tint,
        out Vector2 boatRootWorldPosition)
    {
        EnsureV31BoatResourcesLoaded();
        if (!HasCompleteV31BoatPresentation())
        {
            boatRootWorldPosition = waterlinePoint;
            return false;
        }

        float uniformScale = TideV31BoatPresentationModel.BoatRootScale;
        boatRootWorldPosition = new Vector2(
            waterlinePoint.x,
            TideV31BoatPresentationModel.EvaluateBoatRootY(
                waterlinePoint.y,
                formalBoatV31Catalog,
                isLoaded,
                isStorm,
                uniformScale));
        bool useRepairedEndpoint = boatHullIntegrity >= 2 && boatSailIntegrity >= 1;
        bool needsSailableRig = viewMode == SliceViewMode.Sailing || sailTripActive;
        Sprite endpointBase = useRepairedEndpoint
            ? formalBoatV31Catalog.RepairedBaseSprite
            : needsSailableRig
                ? formalBoatV31Catalog.SailableBaseSprite
                : formalBoatV31Catalog.FoundBaseSprite;
        // V31 Found 的前船舷继承了五块规则透明洞；它只能作为分层证明，不能进入
        // 正式 Game 视图。两个端点都使用同坐标且无洞的 Repaired 前船舷来遮住乘员
        // 双腿，残破感由发现态完整船图中的撕裂帆承担。
        TideV31BoatRuntimeCatalog.LayerEntry[] layers = formalBoatV31Catalog.RepairedLayers;
        TideV31BoatRuntimeCatalog.LayerEntry frontGunwale = FindV31Layer(layers, "FrontGunwale");
        // V20 的端点画布和语义 Pivot 与 V31 相同，但原始 PPU 是 100；V21/V31 是
        // 512。按 Sprite 实际 PPU 归一化，才能让同一像素坐标落到同一世界尺寸，
        // 同时不去改动资源包自身可能被其他系统依赖的导入元数据。
        float endpointUniformScale = uniformScale *
            endpointBase.pixelsPerUnit /
            formalBoatV31Catalog.PixelsPerUnit;

        // 完整端点图拥有船体、桅杆和帆，必须作为人物的直接后景；排序过低时，
        // 航行海况层会吃掉上半部，只留下排序更高的前船舷，造成无帆假象。
        boatHullRenderer.sortingOrder = 5;
        boatSailRenderer.sortingOrder = 4;
        boatCockpitRenderer.sortingOrder = 5;
        boatPassengerRenderer.sortingOrder = 6;
        boatPassengerGunwaleRenderer.sortingOrder = 7;
        boatRudderRenderer.sortingOrder = 8;
        boatWaterlineOcclusionRenderer.sortingOrder = 9;
        // 完整端点作为稳定后景，人物画在它前面，再由无洞的 V31 独立前船舷
        // 遮住双腿；船、人物和前船舷始终共享同一 BoatRoot。
        SetUniformSprite(
            boatHullRenderer,
            endpointBase,
            boatRootWorldPosition,
            endpointUniformScale,
            tint,
            rotationZ,
            FormalBoatFacesRight);
        SetEnabled(boatSailRenderer, false);
        SetEnabled(boatCockpitRenderer, false);
        SetUniformContractLayer(
            boatPassengerGunwaleRenderer,
            frontGunwale,
            boatRootWorldPosition,
            uniformScale,
            tint,
            rotationZ,
            FormalBoatFacesRight);
        SetEnabled(boatRudderRenderer, false);

        SetEnabled(boatPassengerRenderer, showPassenger);
        if (showPassenger)
        {
            Vector2 seatOffset = TideV31BoatPresentationModel.PixelTopLeftToBoatOffset(
                formalBoatV31Catalog.Anchors.SeatTopLeft,
                formalBoatV31Catalog.CanvasSize,
                formalBoatV31Catalog.PivotNormalized,
                formalBoatV31Catalog.PixelsPerUnit);
            seatOffset += new Vector2(-0.42f / uniformScale, 0.24f / uniformScale);
            Vector2 seatPosition = TideV31BoatPresentationModel.EvaluatePoseWorldPosition(
                boatRootWorldPosition,
                seatOffset,
                rotationZ,
                FormalBoatFacesRight,
                uniformScale);
            if (TryGetV42SailingSurvivalFrame(
                waterlinePoint,
                seatPosition,
                out Sprite survivalFrame,
                out Vector2 survivalPivot,
                out float survivalScale,
                out bool survivalDrowning))
            {
                boatPassengerRenderer.sortingOrder = survivalDrowning ? 8 : 6;
                SetUniformSprite(
                    boatPassengerRenderer,
                    survivalFrame,
                    survivalPivot,
                    survivalScale,
                    tint,
                    survivalDrowning ? 0f : rotationZ,
                    FormalBoatFacesRight);
            }
            else
            {
                EnsureV32ArtResourcesLoaded();
                if (HasCompleteV32ArtPresentation())
                {
                    Sprite seatedFrame = GetV37BoatPassengerActionFrame(
                        worldTime,
                        isStorm,
                        out float passengerUniformScale);
                    if (seatedFrame == null)
                    {
                        seatedFrame = formalV32ArtCatalog.GetStableSeatedFrame();
                        passengerUniformScale = BoatPassengerUniformScale;
                    }
                    // V32/V37 均保留完整双腿。人物 Pivot 落在真实座舱高度，
                    // 再由独立前船舷遮挡；动作之间不允许各自补偿位置。
                    SetUniformSprite(
                        boatPassengerRenderer,
                        seatedFrame,
                        seatPosition,
                        Mathf.Max(passengerUniformScale, BoatPassengerUniformScale),
                        tint,
                        rotationZ,
                        FormalBoatFacesRight);
                }
                else
                {
                    int passengerFrameIndex = TideV31BoatPresentationModel.EvaluatePassengerFrame(worldTime);
                    TideV31BoatRuntimeCatalog.LayerEntry passengerFrame =
                        formalBoatV31Catalog.PassengerFrames[passengerFrameIndex];
                    SetUniformContractLayer(
                        boatPassengerRenderer,
                        passengerFrame,
                        boatRootWorldPosition,
                        uniformScale,
                        tint,
                        rotationZ,
                        FormalBoatFacesRight);
                }
            }
        }

        // V31 契约把这三张图定义为 alpha mask，并明确标记 Unity Game-view 集成待做。
        // 当前白色横带没有浪尖轮廓，拿任意海面 Sprite 去填只会在船身中段生成规则灰块。
        // 水线高度仍由上面的 contract 数据决定；视觉遮挡暂交给连续海面和船尾流。
        SetMaskEnabled(boatWaterlineMask, false);
        SetEnabled(boatWaterlineOcclusionRenderer, false);

        return true;
    }

    private Sprite GetV37BoatPassengerActionFrame(
        float worldTime,
        bool isStorm,
        out float uniformScale)
    {
        uniformScale = 0f;
        EnsureV37BoatCharacterResourcesLoaded();
        if (viewMode != SliceViewMode.Sailing || !HasCompleteV37BoatCharacterPresentation())
        {
            return null;
        }

        uniformScale = formalV37BoatCharacterCatalog.UniformScale;
        if (sailingBailing)
        {
            return formalV37BoatCharacterCatalog.GetFrame(
                TideV37BoatCharacterAction.Bail,
                Mathf.Repeat(sailingBailCycle, 1f));
        }

        // 暴潮时先保命，再处理帆索。这个优先级也避免同一帧同时伪装成
        // 收帆和失衡；桶仍在最前面，因为主动舀水是玩家明确按住的动作。
        if (isStorm)
        {
            return formalV37BoatCharacterCatalog.GetFrame(
                TideV37BoatCharacterAction.Brace,
                Mathf.Repeat(worldTime * 1.28f, 1f));
        }

        if (sailingTrimActionTimer > 0f)
        {
            return formalV37BoatCharacterCatalog.GetFrame(
                TideV37BoatCharacterAction.Trim,
                sailingTrimCycle);
        }

        return null;
    }

    private bool TryGetV42SailingSurvivalFrame(
        Vector2 waterlinePoint,
        Vector2 seatPosition,
        out Sprite frame,
        out Vector2 pivotWorldPosition,
        out float uniformScale,
        out bool isDrowning)
    {
        frame = null;
        pivotWorldPosition = seatPosition;
        uniformScale = TideV42CharacterSurvivalPresentationModel.UniformScale;
        isDrowning = false;
        if (survivalPresentationStartView != SliceViewMode.Sailing ||
            (survivalPresentationState != SurvivalPresentationState.Drowning &&
             survivalPresentationState != SurvivalPresentationState.ColdCollapse) ||
            !HasCompleteV42CharacterSurvivalPresentation())
        {
            return false;
        }

        float progress01 = GetSurvivalPresentationProgress01();
        TideV42CharacterSurvivalAction action =
            survivalPresentationState == SurvivalPresentationState.Drowning
                ? TideV42CharacterSurvivalAction.Drown
                : TideV42CharacterSurvivalAction.ColdCollapse;
        frame = TideV42CharacterSurvivalPresentationModel.EvaluateOneShotFrame(
            formalCharacterV42SurvivalCatalog,
            action,
            progress01);
        if (frame == null)
        {
            return false;
        }

        isDrowning = action == TideV42CharacterSurvivalAction.Drown;
        if (isDrowning)
        {
            // 船舱灌满后人物从前船舷外侧进入水面。Drown 每帧声明的水线锚点
            // 贴当前局部海面，再按资源契约连续下沉；不是在座舱中缩扁或瞬移消失。
            pivotWorldPosition = TideV42CharacterSurvivalPresentationModel.EvaluateDrownPivotWorld(
                waterlinePoint + new Vector2(-0.54f, 0f),
                progress01);
        }
        else
        {
            pivotWorldPosition = seatPosition + new Vector2(
                TideV42CharacterSurvivalPresentationModel.EvaluateCollapseForwardWorld(
                    progress01,
                    FormalBoatFacesRight ? 1 : -1),
                -0.08f);
        }

        return true;
    }

    private static TideV31BoatRuntimeCatalog.LayerEntry FindV31Layer(
        TideV31BoatRuntimeCatalog.LayerEntry[] layers,
        string key)
    {
        if (layers == null)
        {
            return null;
        }

        for (int i = 0; i < layers.Length; i++)
        {
            TideV31BoatRuntimeCatalog.LayerEntry layer = layers[i];
            if (layer != null && string.Equals(layer.Key, key, StringComparison.Ordinal))
            {
                return layer;
            }
        }

        return null;
    }

    private TideOceanSample GetSailingOceanSample(float sailingWorldX)
    {
        float wind01 = Mathf.Clamp01(Mathf.Abs(GetNaturalSailingWindSpeed()) /
            Mathf.Max(0.01f, sailingWindMaxSpeed));
        return TideOceanFieldModel.Sample(
            GetSailingMeanWaterY(),
            sailingWorldX,
            weatherClockSeconds,
            tideStrength,
            GetStormPressure01(),
            wind01);
    }

    private float GetSailingMeanWaterY()
    {
        // 航行是同一世界的横向切屏，不是第二片固定高度的海。浅礁顶在航行
        // 构图中保持固定，天文潮相对礁顶的真实水深按 1:1 米制映射。这里
        // 禁止为构图压缩水位，否则画面净空与实际碰撞会再次变成两套规则。
        float reefCrownPhysicalY = lowWaterY +
            TideSailingReefModel.ReefCrownAboveLowestWaterMeters;
        float physicalDepthAboveReef = currentWaterY - reefCrownPhysicalY;
        return sailingReefPoint.y + physicalDepthAboveReef;
    }

    private TideSailingReefSample GetSailingReefSample(float horizontalSpeedMetersPerSecond)
    {
        TideOceanSample reefOcean = GetSailingOceanSample(sailingReefPoint.x);
        float localWaveOffsetMeters = reefOcean.SurfaceY - GetSailingMeanWaterY();
        float instantaneousPhysicalWaterY = currentWaterY + localWaveOffsetMeters;
        return TideSailingReefModel.Evaluate(
            lowWaterY,
            instantaneousPhysicalWaterY,
            sailingWaterIngress01,
            GetCurrentSailingTowLoad01(),
            horizontalSpeedMetersPerSecond,
            GetEffectiveSailingMaxSpeed());
    }

    private void SetRepeatingWorldSize(
        SpriteRenderer renderer,
        Vector2 position,
        Vector2 worldSize,
        Color color,
        float rotationZ,
        bool repeatHorizontally)
    {
        if (!repeatHorizontally)
        {
            SetWorldSize(renderer, position, worldSize, color, rotationZ);
            return;
        }

        if (renderer == null || renderer.sprite == null)
        {
            SetEnabled(renderer, false);
            return;
        }

        // V56 明确提供 Repeat U 与无缝边缘。使用 SpriteRenderer 的连续平铺
        // 保持每米纹理密度不变，避免把一张有限海图横向拉长成几个巨型浪团。
        renderer.drawMode = SpriteDrawMode.Tiled;
        renderer.tileMode = SpriteTileMode.Continuous;
        renderer.size = worldSize;
        renderer.enabled = true;
        renderer.color = color;
        renderer.transform.localPosition = new Vector3(
            position.x,
            position.y,
            visualZ + renderer.sortingOrder * -0.001f);
        renderer.transform.localScale = Vector3.one;
        renderer.transform.localRotation = Quaternion.Euler(0f, 0f, rotationZ);
    }

    private void SetWorldSize(SpriteRenderer renderer, Vector2 position, Vector2 worldSize, Color color, float rotationZ)
    {
        if (renderer == null || renderer.sprite == null)
        {
            SetEnabled(renderer, false);
            return;
        }

        if (renderer.drawMode != SpriteDrawMode.Simple)
        {
            renderer.drawMode = SpriteDrawMode.Simple;
        }

        Vector2 spriteSize = renderer.sprite.bounds.size;
        Vector2 scale = new Vector2(
            worldSize.x / Mathf.Max(0.001f, spriteSize.x),
            worldSize.y / Mathf.Max(0.001f, spriteSize.y));
        Set(renderer, position, scale, color, rotationZ);
    }

    private static void AlignRendererBoundsTop(SpriteRenderer renderer, float targetWorldY)
    {
        if (renderer == null || !renderer.enabled || renderer.sprite == null)
        {
            return;
        }

        // Renderer.bounds 在“上一视图禁用 -> 本帧重新启用”时可能仍是旧缓存。
        // 直接变换 Sprite 几何四角，切屏同一帧也能得到可靠的世界顶边。
        float correctionY = targetWorldY - GetRendererGeometryTopY(renderer);
        renderer.transform.position += Vector3.up * correctionY;
    }

    private static float GetRendererGeometryTopY(SpriteRenderer renderer)
    {
        if (renderer == null || renderer.sprite == null)
        {
            return float.NegativeInfinity;
        }

        Bounds localBounds = renderer.sprite.bounds;
        float topY = float.NegativeInfinity;
        for (int xIndex = 0; xIndex < 2; xIndex++)
        {
            float localX = xIndex == 0 ? localBounds.min.x : localBounds.max.x;
            for (int yIndex = 0; yIndex < 2; yIndex++)
            {
                float localY = yIndex == 0 ? localBounds.min.y : localBounds.max.y;
                float worldY = renderer.transform.TransformPoint(new Vector3(localX, localY, 0f)).y;
                topY = Mathf.Max(topY, worldY);
            }
        }

        return topY;
    }

    private void SetMaskWorldSize(SpriteMask mask, Vector2 position, Vector2 worldSize, float rotationZ)
    {
        if (mask == null || mask.sprite == null)
        {
            if (mask != null)
            {
                mask.enabled = false;
            }
            return;
        }

        Vector2 spriteSize = mask.sprite.bounds.size;
        mask.enabled = true;
        mask.transform.localPosition = new Vector3(position.x, position.y, visualZ);
        mask.transform.localScale = new Vector3(
            worldSize.x / Mathf.Max(0.001f, spriteSize.x),
            worldSize.y / Mathf.Max(0.001f, spriteSize.y),
            1f);
        mask.transform.localRotation = Quaternion.Euler(0f, 0f, rotationZ);
    }

    private static void SetMaskEnabled(SpriteMask mask, bool enabled)
    {
        if (mask != null)
        {
            mask.enabled = enabled;
        }
    }

    private static void SetMaskEnabled(List<SpriteMask> masks, bool enabled)
    {
        for (int i = 0; i < masks.Count; i++)
        {
            SetMaskEnabled(masks[i], enabled);
        }
    }

    private void UpdatePlayerBagStoryMask(
        SpriteRenderer actorRenderer,
        SpriteMask bagMask,
        Vector2 actorPosition,
        bool usesFormalActor,
        bool swimming,
        bool hauling,
        bool repairing)
    {
        // V20 的腰绳和小袋是人物服装的一部分，不是可摘除的打捞包。旧世界空间
        // 粗遮罩会挖掉 1.16m 身体近一半；剧情货物已有独立背带和实物 Renderer，
        // 因此所有移动状态都保留正式人物的完整轮廓。
        if (actorRenderer != null)
        {
            actorRenderer.maskInteraction = SpriteMaskInteraction.None;
        }
        SetMaskEnabled(bagMask, false);
    }

    private void SetEnabled(SpriteRenderer renderer, bool enabled)
    {
        if (renderer != null)
        {
            renderer.enabled = enabled;
        }
    }

    private void SetEnabled(List<SpriteRenderer> renderers, bool enabled)
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            SetEnabled(renderers[i], enabled);
        }
    }

    private static Sprite GetBackdropSprite()
    {
        return GetResourceSpriteOrFallback(ref backdropSprite, "GeneratedStiltFirstBackdropSprite", () => CreateNoiseBandSprite(160, 96, "GeneratedStiltFirstBackdropSprite", 0.22f, 0.04f));
    }

    private static Sprite GetStoryEllipseMaskSprite()
    {
        return GetResourceSpriteOrFallback(ref storyEllipseMaskSprite, "GeneratedStiltFirstStoryEllipseMaskSprite", CreateStoryEllipseMaskSprite);
    }

    private static Sprite GetStoryRaggedMaskSprite()
    {
        return GetResourceSpriteOrFallback(ref storyRaggedMaskSprite, "GeneratedStiltFirstStoryRaggedMaskSprite", CreateStoryRaggedMaskSprite);
    }

    private static Sprite GetInteriorThreeLevelCutawayMaskSprite()
    {
        if (interiorThreeLevelCutawayMaskSprite == null)
        {
            interiorThreeLevelCutawayMaskSprite = CreateInteriorThreeLevelCutawayMaskSprite();
        }

        return interiorThreeLevelCutawayMaskSprite;
    }

    private static Sprite GetFormalHouseSprite()
    {
        EnsureV34HouseExteriorResourcesLoaded();
        if (HasCompleteV34HouseExteriorPresentation())
        {
            return formalHouseV34ExteriorCatalog.StableBase;
        }

        EnsureV32ArtResourcesLoaded();
        if (HasCompleteV32ArtPresentation())
        {
            return formalV32ArtCatalog.HouseFound;
        }

        EnsureV30HouseResourcesLoaded();
        if (HasCompleteV30HousePresentation())
        {
            return formalHouseV30Catalog.ExteriorFrames[0];
        }

        EnsureV28HouseResourcesLoaded();
        if (HasCompleteV28HousePresentation())
        {
            return formalHouseV28Frames[0];
        }

        return GetFormalSprite(ref formalHouseHdSprite, "AIStiltHouseHD") ??
            GetFormalSprite(ref formalHouseSprite, "AIStiltHouse");
    }

    private static TideV69HouseProfileBaseAsset GetV69HouseProfileBase(
        TideV69HouseProfile profile)
    {
        if (formalHouseV69ActiveBase != null &&
            formalHouseV69ActiveBase.Profile == profile &&
            formalHouseV69ActiveBase.IsComplete(out _))
        {
            return formalHouseV69ActiveBase;
        }

        TideV69HouseProfileBaseAsset loaded = Resources.Load<TideV69HouseProfileBaseAsset>(
            TideV69HouseRepairPresentationModel.GetProfileBaseResourcePath(profile));
        if (loaded == null || loaded.Profile != profile || !loaded.IsComplete(out _))
        {
            return null;
        }

        TideV69HouseProfileBaseAsset previous = formalHouseV69ActiveBase;
        formalHouseV69ActiveBase = loaded;
        if (previous != null && previous != loaded)
        {
            Resources.UnloadAsset(previous);
        }
        return loaded;
    }

    private bool HasCompleteV69CurrentHousePresentation()
    {
        TideV69HouseProfile profile = viewMode == SliceViewMode.Interior
            ? TideV69HouseProfile.Interior
            : TideV69HouseProfile.Exterior;
        if (GetV69HouseProfileBase(profile) == null)
        {
            return false;
        }

        foreach (TideV69HouseStructuralOwner owner in
            Enum.GetValues(typeof(TideV69HouseStructuralOwner)))
        {
            TideV69HouseRepairStage stage = GetV69HouseRepairStage(owner);
            if (GetV69HouseStructuralStageAsset(profile, owner, stage) == null)
            {
                return false;
            }
        }

        if (profile == TideV69HouseProfile.Exterior)
        {
            ReleaseV69HouseBinaryStageAssets();
        }
        else
        {
            foreach (TideV69HouseBinaryOwner owner in Enum.GetValues(typeof(TideV69HouseBinaryOwner)))
            {
                if (GetV69HouseBinaryStageAsset(owner, GetV69BinaryOwnerServiceable(owner)) == null)
                {
                    return false;
                }
            }
        }
        return true;
    }

    private TideV69HouseRepairStage GetV69HouseRepairStage(TideV69HouseStructuralOwner owner)
    {
        RepairChoice choice = GetV69HouseRepairChoice(owner);
        bool activeRepair = state == SliceState.RepairMoment &&
            !repairChoiceApplied &&
            pendingRepairChoice == choice;
        // V69 的离散施工帧必须读取原始工序进度。GetRepairPreview01 为连续淡入
        // 做了 SmoothStep，若把它再当工序时间会让清理/试装阈值整体错位。
        float progress = activeRepair ? repairWorkProgress : 0f;
        int committedSteps = GetV69HouseCommittedChannelSteps(owner);
        bool targetsCurrentOwner = activeRepair && progress > 0f &&
            committedSteps == TideV69HouseRepairPresentationModel.GetRequiredStep(owner) - 1;
        return TideV69HouseRepairPresentationModel.EvaluateStage(
            owner,
            committedSteps,
            targetsCurrentOwner,
            progress);
    }

    private int GetV69HouseCommittedChannelSteps(TideV69HouseStructuralOwner owner)
    {
        if (owner == TideV69HouseStructuralOwner.Foundation ||
            owner == TideV69HouseStructuralOwner.AccessLadder)
        {
            return Mathf.Clamp(stiltIntegrity - 1, 0, 2);
        }
        if (owner == TideV69HouseStructuralOwner.RoofLeft ||
            owner == TideV69HouseStructuralOwner.RoofRight ||
            owner == TideV69HouseStructuralOwner.Lookout)
        {
            return Mathf.Clamp(roofIntegrity, 0, 2);
        }
        return Mathf.Clamp(GetInteriorSealVisualLevel(), 0, 1);
    }

    private static RepairChoice GetV69HouseRepairChoice(TideV69HouseStructuralOwner owner)
    {
        if (owner == TideV69HouseStructuralOwner.Foundation ||
            owner == TideV69HouseStructuralOwner.AccessLadder)
        {
            return RepairChoice.Stilt;
        }
        if (owner == TideV69HouseStructuralOwner.RoofLeft ||
            owner == TideV69HouseStructuralOwner.RoofRight ||
            owner == TideV69HouseStructuralOwner.Lookout)
        {
            return RepairChoice.Roof;
        }
        return RepairChoice.InteriorSeal;
    }

    private bool GetV69BinaryOwnerServiceable(TideV69HouseBinaryOwner owner)
    {
        switch (owner)
        {
            case TideV69HouseBinaryOwner.Workbench:
                return workbenchCondition >= 1 ||
                    GetUncommittedRepairPreview01(RepairChoice.Workbench) >= 0.5f;
            case TideV69HouseBinaryOwner.EntryDoor:
                return GetInteriorSealVisualLevel() >= 1 ||
                    GetUncommittedRepairPreview01(RepairChoice.InteriorSeal) >= 0.5f;
            case TideV69HouseBinaryOwner.Bed:
                return GetInteriorBedVisualLevel() >= 1 ||
                    GetUncommittedRepairPreview01(RepairChoice.Bed) >= 0.5f;
            case TideV69HouseBinaryOwner.ChartRadio:
                return chartRadioCondition >= 1 ||
                    GetUncommittedRepairPreview01(RepairChoice.ChartRadio) >= 0.5f;
            case TideV69HouseBinaryOwner.Stove:
                return stoveCondition >= 1 ||
                    (stoveCondition == 0 &&
                     GetUncommittedRepairPreview01(RepairChoice.Lamp) >= 0.5f);
            case TideV69HouseBinaryOwner.LightAndHeat:
                return stoveCondition >= 2 ||
                    (stoveCondition == 1 &&
                     GetUncommittedRepairPreview01(RepairChoice.Lamp) >= 0.5f);
            default:
                return false;
        }
    }

    private static TideV69HouseStructuralStageAsset GetV69HouseStructuralStageAsset(
        TideV69HouseProfile profile,
        TideV69HouseStructuralOwner owner,
        TideV69HouseRepairStage stage)
    {
        int index = (int)owner;
        TideV69HouseStructuralStageAsset current = formalHouseV69ActiveStructuralStages[index];
        if (current != null && current.Profile == profile &&
            current.Owner == owner && current.Stage == stage &&
            current.IsComplete(out _))
        {
            return current;
        }

        TideV69HouseStructuralStageAsset loaded =
            Resources.Load<TideV69HouseStructuralStageAsset>(
                TideV69HouseRepairPresentationModel.GetStructuralStageResourcePath(
                    profile,
                    owner,
                    stage));
        if (loaded == null || loaded.Profile != profile ||
            loaded.Owner != owner || loaded.Stage != stage ||
            !loaded.IsComplete(out _))
        {
            return null;
        }

        formalHouseV69ActiveStructuralStages[index] = loaded;
        if (current != null && current != loaded)
        {
            Resources.UnloadAsset(current);
        }
        return loaded;
    }

    private static void ReleaseV69HouseBinaryStageAssets()
    {
        for (int i = 0; i < formalHouseV69ActiveBinaryStages.Length; i++)
        {
            TideV69HouseBinaryStageAsset current = formalHouseV69ActiveBinaryStages[i];
            formalHouseV69ActiveBinaryStages[i] = null;
            if (current != null)
            {
                Resources.UnloadAsset(current);
            }
        }
    }

    private static TideV69HouseBinaryStageAsset GetV69HouseBinaryStageAsset(
        TideV69HouseBinaryOwner owner,
        bool serviceable)
    {
        int index = (int)owner;
        TideV69HouseBinaryStageAsset current = formalHouseV69ActiveBinaryStages[index];
        if (current != null && current.Owner == owner && current.Serviceable == serviceable &&
            current.IsComplete(out _))
        {
            return current;
        }

        TideV69HouseBinaryStageAsset loaded = Resources.Load<TideV69HouseBinaryStageAsset>(
            TideV69HouseRepairPresentationModel.GetBinaryStageResourcePath(owner, serviceable));
        if (loaded == null || loaded.Owner != owner || loaded.Serviceable != serviceable ||
            !loaded.IsComplete(out _))
        {
            return null;
        }

        formalHouseV69ActiveBinaryStages[index] = loaded;
        if (current != null && current != loaded)
        {
            Resources.UnloadAsset(current);
        }
        return loaded;
    }

    private static void EnsureV34HouseExteriorResourcesLoaded()
    {
        if (formalHouseV34ExteriorCatalog == null)
        {
            formalHouseV34ExteriorCatalog = Resources.Load<TideV34HouseExteriorCatalog>(
                "StiltFirstSliceAI/V34HouseExteriorCatalog");
        }
    }

    private static bool HasCompleteV34HouseExteriorPresentation()
    {
        EnsureV34HouseExteriorResourcesLoaded();
        return formalHouseV34ExteriorCatalog != null &&
            formalHouseV34ExteriorCatalog.IsComplete(out _);
    }

    private static void EnsureV35HouseInteriorResourcesLoaded()
    {
        if (formalHouseV35InteriorCatalog == null)
        {
            formalHouseV35InteriorCatalog = Resources.Load<TideV35HouseInteriorCatalog>(
                "StiltFirstSliceAI/V35HouseInteriorCatalog");
        }
    }

    private static bool HasCompleteV35HouseInteriorPresentation()
    {
        EnsureV35HouseInteriorResourcesLoaded();
        return formalHouseV35InteriorCatalog != null &&
            formalHouseV35InteriorCatalog.IsComplete(out _);
    }

    private static void EnsureV30HouseResourcesLoaded()
    {
        if (formalHouseV30Catalog != null)
        {
            return;
        }

        formalHouseV30Catalog = Resources.Load<TideV30HouseRuntimeCatalog>(
            "StiltFirstSliceAI/V30HouseRuntimeCatalog");
    }

    private static void EnsureV20CharacterResourcesLoaded()
    {
        if (formalCharacterV20Catalog == null)
        {
            formalCharacterV20Catalog = Resources.Load<TideV20CharacterRuntimeCatalog>(
                "StiltFirstSliceAI/V20CharacterRuntimeCatalog");
        }
    }

    private static void EnsureV41CharacterContactResourcesLoaded()
    {
        if (formalCharacterV41ContactCatalog == null)
        {
            formalCharacterV41ContactCatalog = Resources.Load<TideV41CharacterContactCatalog>(
                "StiltFirstSliceAI/V41CharacterContactCatalog");
        }
    }

    private static void EnsureV42CharacterSurvivalResourcesLoaded()
    {
        if (formalCharacterV42SurvivalCatalog == null)
        {
            formalCharacterV42SurvivalCatalog = Resources.Load<TideV42CharacterSurvivalCatalog>(
                "StiltFirstSliceAI/V42CharacterSurvivalCatalog");
        }
    }

    private static void EnsureV32ArtResourcesLoaded()
    {
        if (formalV32ArtCatalog == null)
        {
            formalV32ArtCatalog = Resources.Load<TideV32FirstSliceArtCatalog>(
                "StiltFirstSliceAI/V32FirstSliceArtCatalog");
        }
    }

    private static bool HasCompleteV32ArtPresentation()
    {
        EnsureV32ArtResourcesLoaded();
        return formalV32ArtCatalog != null && formalV32ArtCatalog.IsComplete(out _);
    }

    private static void EnsureV37BoatCharacterResourcesLoaded()
    {
        if (formalV37BoatCharacterCatalog == null)
        {
            formalV37BoatCharacterCatalog = Resources.Load<TideV37BoatCharacterCatalog>(
                "StiltFirstSliceAI/V37BoatCharacterCatalog");
        }
    }

    private static bool HasCompleteV37BoatCharacterPresentation()
    {
        EnsureV37BoatCharacterResourcesLoaded();
        return formalV37BoatCharacterCatalog != null &&
            formalV37BoatCharacterCatalog.IsComplete(out _);
    }

    private static void EnsureV43SeaWeatherResourcesLoaded()
    {
        if (formalV43SeaWeatherCatalog == null)
        {
            formalV43SeaWeatherCatalog = Resources.Load<TideV43SeaWeatherCatalog>(
                "StiltFirstSliceAI/V43SeaWeatherCatalog");
        }
    }

    private static bool HasCompleteV43SeaWeatherPresentation()
    {
        EnsureV43SeaWeatherResourcesLoaded();
        return formalV43SeaWeatherCatalog != null &&
            formalV43SeaWeatherCatalog.IsComplete(out _);
    }

    private static void EnsureV54NetResourcesLoaded()
    {
        if (formalV54NetCatalog == null)
        {
            formalV54NetCatalog = Resources.Load<TideV54NetCatalog>(
                "StiltFirstSliceAI/V54NetCatalog");
        }
    }

    private static void EnsureV59FindResourcesLoaded()
    {
        if (formalV59FindCatalog == null)
        {
            formalV59FindCatalog = Resources.Load<TideV59FindCatalog>(
                "StiltFirstSliceAI/V59TideFindCatalog");
        }
    }

    private static bool HasCompleteV59FindPresentation()
    {
        EnsureV59FindResourcesLoaded();
        return formalV59FindCatalog != null && formalV59FindCatalog.IsComplete(out _);
    }

    private static bool HasCompleteV54NetPresentation()
    {
        EnsureV54NetResourcesLoaded();
        return formalV54NetCatalog != null && formalV54NetCatalog.IsComplete(out _);
    }

    private static void EnsureV44LookoutVistaResourcesLoaded()
    {
        if (formalV44LookoutVistaCatalog == null)
        {
            formalV44LookoutVistaCatalog = Resources.Load<TideV44LookoutVistaCatalog>(
                "StiltFirstSliceAI/V44LookoutVistaCatalog");
        }
    }

    private static bool HasCompleteV44LookoutVistaPresentation()
    {
        EnsureV44LookoutVistaResourcesLoaded();
        return formalV44LookoutVistaCatalog != null &&
            formalV44LookoutVistaCatalog.IsComplete(out _);
    }

    private static void EnsureV70VortexResourcesLoaded()
    {
        if (formalV70VortexCatalog == null)
        {
            formalV70VortexCatalog = Resources.Load<TideV70VortexCatalog>(
                "StiltFirstSliceAI/V70VortexCatalog");
        }
    }

    private static bool HasCompleteV70VortexPresentation()
    {
        EnsureV70VortexResourcesLoaded();
        return formalV70VortexCatalog != null && formalV70VortexCatalog.IsComplete(out _);
    }

    private static bool HasCompleteV20CharacterPresentation()
    {
        EnsureV20CharacterResourcesLoaded();
        return formalCharacterV20Catalog != null && formalCharacterV20Catalog.IsComplete(out _);
    }

    private static bool HasCompleteV41CharacterContactPresentation()
    {
        EnsureV41CharacterContactResourcesLoaded();
        return formalCharacterV41ContactCatalog != null &&
            formalCharacterV41ContactCatalog.IsComplete(out _);
    }

    private static bool HasCompleteV42CharacterSurvivalPresentation()
    {
        EnsureV42CharacterSurvivalResourcesLoaded();
        return formalCharacterV42SurvivalCatalog != null &&
            formalCharacterV42SurvivalCatalog.IsComplete(out _);
    }

    private static void EnsureV31BoatResourcesLoaded()
    {
        if (formalBoatV31Catalog == null)
        {
            formalBoatV31Catalog = Resources.Load<TideV31BoatRuntimeCatalog>(
                "StiltFirstSliceAI/V31BoatRuntimeCatalog");
        }
    }

    private static bool HasCompleteV31BoatPresentation()
    {
        EnsureV31BoatResourcesLoaded();
        return formalBoatV31Catalog != null && formalBoatV31Catalog.IsComplete(out _);
    }

    private static void EnsureV52BoatRepairBaseLoaded()
    {
        if (formalBoatV52BaseAsset == null)
        {
            formalBoatV52BaseAsset = Resources.Load<TideV52BoatRepairBaseAsset>(
                TideV52BoatRepairPresentationModel.BaseResourcePath);
        }
    }

    private static bool HasCompleteV52BoatRepairBase()
    {
        EnsureV52BoatRepairBaseLoaded();
        return formalBoatV52BaseAsset != null && formalBoatV52BaseAsset.IsComplete(out _);
    }

    private static TideV52BoatRepairStageAsset GetV52BoatRepairStageAsset(
        TideV52BoatRepairOwner owner,
        TideV52BoatRepairStage stage)
    {
        int ownerIndex = (int)owner;
        TideV52BoatRepairStageAsset current = formalBoatV52ActiveStageAssets[ownerIndex];
        if (current != null && current.Owner == owner && current.Stage == stage &&
            current.IsComplete(out _))
        {
            return current;
        }

        TideV52BoatRepairStageAsset loaded = Resources.Load<TideV52BoatRepairStageAsset>(
            TideV52BoatRepairPresentationModel.GetStageResourcePath(owner, stage));
        if (loaded == null || loaded.Owner != owner || loaded.Stage != stage ||
            !loaded.IsComplete(out _))
        {
            return null;
        }

        formalBoatV52ActiveStageAssets[ownerIndex] = loaded;
        if (current != null && current != loaded)
        {
            // 这里只释放轻量阶段索引；Sprite 是否从纹理常驻中回收仍由 Unity
            // 资源生命周期和后续 Profiler 门决定，不能在渲染帧内强制卸载共享纹理。
            Resources.UnloadAsset(current);
        }
        return loaded;
    }

    private int GetV52BoatRepairLevel(TideV52BoatRepairOwner owner)
    {
        if (owner == TideV52BoatRepairOwner.HullBreach)
        {
            return boatHullIntegrity;
        }
        if (owner == TideV52BoatRepairOwner.SailRepair)
        {
            return boatSailIntegrity;
        }
        return boatCabinIntegrity;
    }

    private RepairChoice GetRepairChoiceForV52Owner(TideV52BoatRepairOwner owner)
    {
        if (owner == TideV52BoatRepairOwner.HullBreach)
        {
            return RepairChoice.Hull;
        }
        if (owner == TideV52BoatRepairOwner.SailRepair)
        {
            return RepairChoice.Sail;
        }
        return RepairChoice.Cabin;
    }

    private TideV52BoatRepairStage GetV52BoatRepairStage(TideV52BoatRepairOwner owner)
    {
        RepairChoice choice = GetRepairChoiceForV52Owner(owner);
        bool activeRepair = state == SliceState.RepairMoment && !repairChoiceApplied &&
            pendingRepairChoice == choice;
        return TideV52BoatRepairPresentationModel.EvaluateStage(
            owner,
            GetV52BoatRepairLevel(owner),
            activeRepair,
            activeRepair ? repairWorkProgress : 0f);
    }

    private static bool HasCompleteV30HousePresentation()
    {
        EnsureV30HouseResourcesLoaded();
        return formalHouseV30Catalog != null && formalHouseV30Catalog.IsComplete(out _);
    }

    private Sprite GetRegisteredExteriorFrame(float worldTime, float stormPressure01)
    {
        EnsureV32ArtResourcesLoaded();
        if (HasCompleteV32ArtPresentation())
        {
            return formalV32ArtCatalog.GetHouseEndpoint(GetShelterRestoration01());
        }

        EnsureV30HouseResourcesLoaded();
        if (HasCompleteV30HousePresentation())
        {
            int frame = TideV30HousePresentationModel.EvaluateExteriorFrame(
                worldTime,
                stormPressure01);
            return formalHouseV30Catalog.ExteriorFrames[frame];
        }

        return GetV28ExteriorFrame(worldTime, stormPressure01);
    }

    private static void EnsureV28HouseResourcesLoaded()
    {
        if (formalHouseV28ResourcesLoaded)
        {
            return;
        }

        formalHouseV28ResourcesLoaded = true;
        for (int i = 0; i < formalHouseV28Frames.Length; i++)
        {
            formalHouseV28Frames[i] = Resources.Load<Sprite>(
                $"StiltFirstSliceAI/V28House/Exterior/AIStiltHouseV28_F{i:00}");
        }

        formalHouseV27InteriorFoundSprite = Resources.Load<Sprite>(
            "StiltFirstSliceAI/V28House/Interior/AIStiltHouseV27_InteriorFound");
        formalHouseV27InteriorRepairedSprite = Resources.Load<Sprite>(
            "StiltFirstSliceAI/V28House/Interior/AIStiltHouseV27_InteriorRepaired");
    }

    private static bool HasCompleteV28HousePresentation()
    {
        return TideV28HousePresentationModel.IsCompleteRuntimePack(
            formalHouseV28Frames,
            formalHouseV27InteriorFoundSprite,
            formalHouseV27InteriorRepairedSprite);
    }

    private static Sprite GetV28ExteriorFrame(float worldTime, float stormPressure01)
    {
        EnsureV28HouseResourcesLoaded();
        if (!HasCompleteV28HousePresentation())
        {
            return null;
        }

        int frameIndex = TideV28HousePresentationModel.EvaluateExteriorFrame(
            worldTime,
            stormPressure01);
        return formalHouseV28Frames[frameIndex];
    }

    private Sprite GetV27InteriorEndpoint()
    {
        EnsureV28HouseResourcesLoaded();
        if (!HasCompleteV28HousePresentation())
        {
            return null;
        }

        return TideV28HousePresentationModel.UseRepairedInterior(GetShelterRestoration01())
            ? formalHouseV27InteriorRepairedSprite
            : formalHouseV27InteriorFoundSprite;
    }

    private static Sprite GetFormalInteriorWallSprite()
    {
        if (formalInteriorWallSprite != null)
        {
            return formalInteriorWallSprite;
        }

        Sprite source = GetFormalHouseSprite();
        if (source == null || source.texture == null)
        {
            return null;
        }

        // Reuse the authored salt-stained facade as the rear wall of the cutaway.
        // The crop excludes roof and stilts, which remain on the full exterior shell.
        Rect sourceRect = source.rect;
        Rect wallRect = new Rect(
            sourceRect.x + sourceRect.width * 0.18f,
            sourceRect.y + sourceRect.height * 0.43f,
            sourceRect.width * 0.6f,
            sourceRect.height * 0.215f);
        formalInteriorWallSprite = Sprite.Create(
            source.texture,
            wallRect,
            new Vector2(0.5f, 0.5f),
            source.pixelsPerUnit,
            0,
            SpriteMeshType.FullRect);
        formalInteriorWallSprite.name = "GeneratedStiltFirstFormalInteriorWallSprite";
        return formalInteriorWallSprite;
    }

    private static Sprite GetFormalInteriorLoftWallSprite()
    {
        if (formalInteriorLoftWallSprite != null)
        {
            return formalInteriorLoftWallSprite;
        }

        Sprite source = GetFormalHouseSprite();
        if (source == null || source.texture == null)
        {
            return null;
        }

        // Reuse a window-free bay from the authored rear wall. Stretching this narrow
        // salt-darkened board section avoids duplicating the living-floor windows in
        // the attic while keeping timber grain and palette identical to the shell.
        formalInteriorLoftWallSprite = CreateFormalSubSprite(
            source,
            new Vector4(0.43f, 0.43f, 0.14f, 0.215f),
            "GeneratedStiltFirstFormalInteriorLoftWallSprite");
        return formalInteriorLoftWallSprite;
    }

    private static Sprite GetFormalInteriorRoofCapSprite()
    {
        if (formalInteriorRoofCapSprite != null)
        {
            return formalInteriorRoofCapSprite;
        }

        Sprite source = GetFormalHouseSprite();
        if (source == null || source.texture == null)
        {
            return null;
        }

        // Keep the salt-corroded ridge from the exact exterior asset. Only this
        // narrow strip is redrawn over the cutaway; the broad roof face stays open
        // so the player and loft furniture remain readable.
        formalInteriorRoofCapSprite = CreateFormalSubSprite(
            source,
            new Vector4(0.12f, 0.805f, 0.76f, 0.05f),
            "GeneratedStiltFirstFormalInteriorRoofCapSprite");
        return formalInteriorRoofCapSprite;
    }

    private static bool HasHdFormalHouse()
    {
        EnsureV32ArtResourcesLoaded();
        EnsureV35HouseInteriorResourcesLoaded();
        EnsureV30HouseResourcesLoaded();
        EnsureV28HouseResourcesLoaded();
        return HasCompleteV32ArtPresentation() || HasRegisteredHousePresentation() ||
            GetFormalSprite(ref formalHouseHdSprite, "AIStiltHouseHD") != null;
    }

    private static bool HasRegisteredHousePresentation()
    {
        return HasCompleteV32ArtPresentation() ||
            HasCompleteV35HouseInteriorPresentation() ||
            HasCompleteV30HousePresentation() ||
            HasCompleteV28HousePresentation();
    }

    private Vector2 GetFormalHouseWorldPosition()
    {
        if (HasCompleteV32ArtPresentation() || HasCompleteV30HousePresentation())
        {
            // V30 的右系泊点落在现有湿木路 x=0.24，主生活楼板落在玩家脚底
            // y=-0.63。位置由运行契约的 1536 画布、PPU 192 和 Pivot 推导，
            // 不再用肉眼拖动整张屋图。
            return new Vector2(-2.9892f, -3.3383f);
        }

        if (HasCompleteV28HousePresentation())
        {
            // V25-V28 share a bottom registration pivot.  This Y value maps the
            // authored main floor (pixel y=2580) to the survivor's visible feet.
            return new Vector2(-3.15f, -3.22f);
        }

        return HasHdFormalHouse() ? new Vector2(-2.85f, -0.28f) : new Vector2(-3.2f, -0.3f);
    }

    private Vector2 GetFormalHouseWorldSize()
    {
        if (HasCompleteV32ArtPresentation() || HasCompleteV30HousePresentation())
        {
            return new Vector2(8f, 8f);
        }

        if (HasCompleteV28HousePresentation())
        {
            return new Vector2(7f, 7f);
        }

        return HasHdFormalHouse() ? new Vector2(8.8f, 4.95f) : new Vector2(8.1f, 4.2f);
    }

    private static Sprite GetFormalDaySeaSkySprite()
    {
        return GetFormalSprite(ref formalDaySeaSkyHdSprite, "AIDaySeaSkyHD") ??
            GetFormalSprite(ref formalDaySeaSkySprite, "AIDaySeaSky");
    }

    private static Sprite GetFormalNightSeaSkySprite()
    {
        return GetFormalSprite(ref formalNightSeaSkyHdSprite, "AINightSeaSkyHD") ??
            GetFormalSprite(ref formalNightSeaSkySprite, "AINightSeaSky");
    }

    private static Sprite GetFormalWaterSurfaceSprite()
    {
        EnsureV56OceanResourcesLoaded();
        if (formalV56OceanCatalog != null && formalV56OceanCatalog.IsComplete(out _))
        {
            return formalV56OceanCatalog.ContinuousBase;
        }

        return GetFormalSprite(ref formalWaterBodyHdSprite, "AIWaterBodyHD") ??
            GetFormalSprite(ref formalWaterSurfaceSprite, "AIWaterSurface");
    }

    private static bool HasHdFormalWater()
    {
        EnsureV56OceanResourcesLoaded();
        return (formalV56OceanCatalog != null && formalV56OceanCatalog.IsComplete(out _)) ||
            GetFormalSprite(ref formalWaterBodyHdSprite, "AIWaterBodyHD") != null;
    }

    private static bool HasCompleteV56OceanPresentation()
    {
        EnsureV56OceanResourcesLoaded();
        return formalV56OceanCatalog != null && formalV56OceanCatalog.IsComplete(out _);
    }

    private static Sprite GetFormalBoardwalkSprite()
    {
        EnsureV53MooringResourcesLoaded();
        if (formalV53MooringCatalog != null && formalV53MooringCatalog.IsComplete(out _))
        {
            return formalV53MooringCatalog.FoundWeatheredPier;
        }

        return GetFormalSprite(ref formalBoardwalkHdSprite, "AIWetBoardwalkHD") ??
            GetFormalSprite(ref formalBoardwalkSprite, "AIWetBoardwalk");
    }

    private static bool HasCompleteV53MooringPresentation()
    {
        EnsureV53MooringResourcesLoaded();
        return formalV53MooringCatalog != null && formalV53MooringCatalog.IsComplete(out _);
    }

    private static Sprite GetV53MooringPierSprite(bool serviceable)
    {
        EnsureV53MooringResourcesLoaded();
        if (formalV53MooringCatalog == null || !formalV53MooringCatalog.IsComplete(out _))
        {
            return GetFormalSprite(ref formalBoardwalkHdSprite, "AIWetBoardwalkHD") ??
                GetFormalSprite(ref formalBoardwalkSprite, "AIWetBoardwalk");
        }

        return serviceable
            ? formalV53MooringCatalog.ServiceablePier
            : formalV53MooringCatalog.FoundWeatheredPier;
    }

    private static Sprite GetMooringGangplankSprite()
    {
        EnsureV53MooringResourcesLoaded();
        return formalV53MooringCatalog != null && formalV53MooringCatalog.IsComplete(out _)
            ? formalV53MooringCatalog.ServiceableGangplank
            : GetLandingWalkwaySprite();
    }

    private static void EnsureV53MooringResourcesLoaded()
    {
        if (formalV53MooringCatalog == null)
        {
            formalV53MooringCatalog = Resources.Load<TideV53MooringCatalog>(
                "StiltFirstSliceAI/V53MooringCatalog");
        }
    }

    private static void EnsureV56OceanResourcesLoaded()
    {
        if (formalV56OceanCatalog == null)
        {
            formalV56OceanCatalog = Resources.Load<TideV56OceanCatalog>(
                "StiltFirstSliceAI/V56OceanCatalog");
        }
    }

    private static Sprite GetMoonWashSprite()
    {
        if (moonWashSprite == null)
        {
            moonWashSprite = CreateSolidSprite(8, 8, "GeneratedStiltFirstMoonWashSprite");
        }

        return moonWashSprite;
    }

    private static Sprite GetSubsurfaceFadeSprite()
    {
        if (subsurfaceFadeSprite == null)
        {
            subsurfaceFadeSprite = CreateSubsurfaceFadeSprite();
        }

        return subsurfaceFadeSprite;
    }

    private static Sprite GetDepthBlendBandSprite()
    {
        if (depthBlendBandSprite == null)
        {
            depthBlendBandSprite = CreateDepthBlendBandSprite();
        }

        return depthBlendBandSprite;
    }

    private static Sprite GetFormalSubsurfaceBandSprite()
    {
        if (formalSubsurfaceBandSprite != null)
        {
            return formalSubsurfaceBandSprite;
        }

        Sprite source = GetFormalWaterSurfaceSprite();
        if (source == null || source.texture == null)
        {
            return GetMoonWashSprite();
        }

        Rect sourceRect = source.textureRect;
        // Extend only the lowest few rows. A broad 42% crop put mid-water texture at
        // the continuation's top edge while the full sprite ended on seabed-dark rows,
        // producing a ruler-straight seam across the sailing view.
        float bandHeight = Mathf.Max(6f, sourceRect.height * 0.03f);
        Rect bandRect = new Rect(
            sourceRect.x,
            sourceRect.y,
            sourceRect.width,
            Mathf.Min(sourceRect.height, bandHeight));
        formalSubsurfaceBandSprite = Sprite.Create(
            source.texture,
            bandRect,
            new Vector2(0.5f, 0.5f),
            source.pixelsPerUnit,
            0u,
            SpriteMeshType.FullRect);
        formalSubsurfaceBandSprite.name = "GeneratedStiltFirstFormalSubsurfaceBandSprite";
        formalSubsurfaceBandSprite.hideFlags = HideFlags.HideAndDontSave;
        return formalSubsurfaceBandSprite;
    }

    private static void ApplyUnlitSpriteMaterial(SpriteRenderer renderer)
    {
        if (renderer == null)
        {
            return;
        }

        Material material = GetSpriteUnlitMaterial();
        if (material != null)
        {
            renderer.sharedMaterial = material;
        }
    }

    private static Material GetSpriteUnlitMaterial()
    {
        if (spriteUnlitMaterial != null)
        {
            return spriteUnlitMaterial;
        }

        // The deep-water layers represent light absorption through a continuous
        // volume, not a separately lit world object. Sprite-Lit would let local
        // house lights and 2D shadow geometry stamp rectangular brightness changes
        // into that volume, exposing the finite crop we are trying to continue.
        Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (shader == null)
        {
            return null;
        }

        spriteUnlitMaterial = new Material(shader)
        {
            name = "GeneratedStiltFirstSpriteUnlitMaterial",
            hideFlags = HideFlags.HideAndDontSave
        };
        return spriteUnlitMaterial;
    }

    private static Sprite GetWaterSprite()
    {
        return GetResourceSpriteOrFallback(ref waterSprite, "GeneratedStiltFirstWaterSprite", () => CreateWaveSprite(160, 48, "GeneratedStiltFirstWaterSprite", true));
    }

    private static Sprite GetFoamSprite()
    {
        return GetResourceSpriteOrFallback(ref foamSprite, "GeneratedStiltFirstFoamSprite", () => CreateWaveSprite(128, 24, "GeneratedStiltFirstFoamSprite", false));
    }

    private static Sprite GetSwimWakeSprite()
    {
        if (swimWakeSprite == null)
        {
            swimWakeSprite = CreateSwimWakeSprite();
        }

        return swimWakeSprite;
    }

    private static Sprite GetCloudMassSprite()
    {
        Sprite formal = GetFormalSprite(ref formalCloudBankSprite, "AICloudBank");
        if (formal != null)
        {
            return formal;
        }

        return GetResourceSpriteOrFallback(ref cloudMassSprite, "GeneratedStiltFirstCloudMassSprite", CreateCloudMassSprite);
    }

    private static Sprite GetPlayerHaloSprite()
    {
        return GetResourceSpriteOrFallback(ref playerHaloSprite, "GeneratedStiltFirstPlayerHaloSprite", CreatePlayerHaloSprite);
    }

    private static Sprite GetVortexSprite()
    {
        // The old AI vortex has high-frequency white spray and reads like a pasted
        // sticker. The low-noise procedural flow shares the sea palette and can be
        // animated in two counter-rotating layers without introducing another style.
        return GetResourceSpriteOrFallback(ref vortexSprite, "GeneratedStiltFirstRouteVortexSprite", CreateVortexSprite);
    }

    private static Sprite GetSideViewVortexDepressionSprite()
    {
        return GetResourceSpriteOrFallback(
            ref sideViewVortexDepressionSprite,
            "GeneratedStiltFirstSideViewVortexDepressionSprite",
            CreateSideViewVortexDepressionSprite);
    }

    private static Sprite GetSideViewVortexUndertowSprite()
    {
        return GetResourceSpriteOrFallback(
            ref sideViewVortexUndertowSprite,
            "GeneratedStiltFirstSideViewVortexUndertowSprite",
            CreateSideViewVortexUndertowSprite);
    }

    private static Sprite GetVerandaSprite()
    {
        return GetResourceSpriteOrFallback(ref verandaSprite, "GeneratedStiltFirstVerandaBodySprite", CreateVerandaBodySprite);
    }

    private static Sprite GetPorchRoofSprite()
    {
        return GetResourceSpriteOrFallback(ref porchRoofSprite, "GeneratedStiltFirstPorchRoofSprite", CreatePorchRoofSprite);
    }

    private static Sprite GetHouseGableRoofSprite()
    {
        return GetResourceSpriteOrFallback(ref houseGableRoofSprite, "GeneratedStiltFirstHouseGableRoofSprite", CreateHouseGableRoofSprite);
    }

    private static Sprite GetLongEaveSprite()
    {
        return GetResourceSpriteOrFallback(ref longEaveSprite, "GeneratedStiltFirstLongEaveSprite", CreateLongEaveSprite);
    }

    private static Sprite GetUnderHouseShadowSprite()
    {
        return GetResourceSpriteOrFallback(ref underHouseShadowSprite, "GeneratedStiltFirstUnderHouseShadowSprite", CreateUnderHouseShadowSprite);
    }

    private static Sprite GetDenseStiltPostSprite()
    {
        return GetResourceSpriteOrFallback(ref denseStiltPostSprite, "GeneratedStiltFirstDenseStiltPostSprite", CreateDenseStiltPostSprite);
    }

    private static Sprite GetStormSurgeWallSprite()
    {
        return GetResourceSpriteOrFallback(ref stormSurgeWallSprite, "GeneratedStiltFirstStormSurgeWallSprite", CreateStormSurgeWallSprite);
    }

    private static Sprite GetBrokenHousePieceSprite()
    {
        return GetResourceSpriteOrFallback(ref brokenHousePieceSprite, "GeneratedStiltFirstBrokenHousePieceSprite", CreateBrokenHousePieceSprite);
    }

    private static Sprite GetSnappedStiltPostSprite()
    {
        return GetResourceSpriteOrFallback(ref snappedStiltPostSprite, "GeneratedStiltFirstSnappedStiltPostSprite", CreateSnappedStiltPostSprite);
    }

    private static Sprite GetDepartureRouteWakeSprite()
    {
        return GetResourceSpriteOrFallback(ref departureRouteWakeSprite, "GeneratedStiltFirstDepartureRouteWakeSprite", CreateDepartureRouteWakeSprite);
    }

    private static Sprite GetDiagonalBraceSprite()
    {
        return GetResourceSpriteOrFallback(ref diagonalBraceSprite, "GeneratedStiltFirstDiagonalBraceSprite", CreateDiagonalBraceSprite);
    }

    private static Sprite GetLaundryClothSprite()
    {
        return GetResourceSpriteOrFallback(ref laundryClothSprite, "GeneratedStiltFirstLaundryClothSprite", CreateLaundryClothSprite);
    }

    private static Sprite GetLandingWalkwaySprite()
    {
        return GetResourceSpriteOrFallback(ref landingWalkwaySprite, "GeneratedStiltFirstBoatLandingWalkwaySprite", CreateLandingWalkwaySprite);
    }

    private static Sprite GetBoatWakeSprite()
    {
        return GetResourceSpriteOrFallback(ref boatWakeSprite, "GeneratedStiltFirstBoatWakeSprite", CreateBoatWakeSprite);
    }

    private static Sprite GetIncomingTideCarryRippleSprite()
    {
        return GetResourceSpriteOrFallback(ref incomingTideCarryRippleSprite, "GeneratedStiltFirstIncomingTideCarryRippleSprite", CreateIncomingTideCarryRippleSprite);
    }

    private static Sprite GetHouseSprite()
    {
        Sprite formal = GetFormalHouseSprite();
        if (formal != null)
        {
            return formal;
        }

        return GetResourceSpriteOrFallback(ref houseSprite, "GeneratedStiltFirstHouseSprite", CreateHouseSprite);
    }

    private static Sprite GetRoofSprite()
    {
        return GetResourceSpriteOrFallback(ref roofSprite, "GeneratedStiltFirstRoofSprite", CreateRoofSprite);
    }

    private static Sprite GetDeckSprite()
    {
        return GetResourceSpriteOrFallback(ref deckSprite, "GeneratedStiltFirstSaltwoodDeckSprite", CreateSaltwoodDeckSprite);
    }

    private static Sprite GetPostSprite()
    {
        return GetResourceSpriteOrFallback(ref postSprite, "GeneratedStiltFirstSaltwoodPostSprite", CreateSaltwoodPostSprite);
    }

    private static Sprite GetHouseSaltStreakSprite()
    {
        return GetResourceSpriteOrFallback(ref houseSaltStreakSprite, "GeneratedStiltFirstHouseSaltStreakSprite", CreateHouseSaltStreakSprite);
    }

    private static Sprite GetShelterWarmRaySprite()
    {
        return GetResourceSpriteOrFallback(ref shelterWarmRaySprite, "GeneratedStiltFirstShelterWarmRaySprite", CreateShelterWarmRaySprite);
    }

    private static Sprite GetShelterLampSprite()
    {
        return GetResourceSpriteOrFallback(ref shelterLampSprite, "GeneratedStiltFirstWarmLampSprite", CreateWindowSprite);
    }

    private static Sprite GetPlayerSprite()
    {
        Sprite formal = GetFormalSprite(ref formalPlayerSprite, "AIPlayerWalkStepHD") ?? GetFormalSprite(ref formalPlayerSprite, "AIPlayer");
        if (formal != null)
        {
            return formal;
        }

        return GetResourceSpriteOrFallback(ref playerSprite, "GeneratedStiltFirstPlayerRaincoatSprite", CreateRaincoatHumanSprite);
    }

    private static Sprite GetFormalSwimPlayerSprite()
    {
        return GetFormalSprite(ref formalSwimPlayerSprite, "AIPlayerSwimHD");
    }

    private static Sprite GetFormalHaulPlayerSprite()
    {
        return GetFormalSprite(ref formalHaulPlayerSprite, "AIPlayerHaulNetHD");
    }

    private static Sprite GetFormalRepairPlayerSprite()
    {
        return GetFormalSprite(ref formalRepairPlayerSprite, "AIPlayerRepairHD");
    }

    private static Sprite GetFormalWalkPlayerSprite(int frameIndex)
    {
        Sprite frame;
        switch (Mathf.Abs(frameIndex) % 4)
        {
            case 0:
                frame = GetFormalSprite(ref formalWalkContactRightSprite, "AIPlayerWalkContactRightHD");
                break;
            case 1:
                frame = GetFormalSprite(ref formalWalkPassRightSprite, "AIPlayerWalkPassRightHD");
                break;
            case 2:
                frame = GetFormalSprite(ref formalWalkContactLeftSprite, "AIPlayerWalkContactLeftHD");
                break;
            default:
                frame = GetFormalSprite(ref formalWalkPassLeftSprite, "AIPlayerWalkPassLeftHD");
                break;
        }

        // Older checkouts retain the single stride frame as a graceful fallback.
        return frame ?? GetFormalSprite(ref formalWalkPlayerSprite, "AIPlayerWalkStepHD");
    }

    private static Sprite GetFormalBoatPassengerSprite()
    {
        return GetFormalSprite(ref formalBoatPassengerHdSprite, "AIBoatPassengerHD");
    }

    private static Sprite GetFormalNetSprite()
    {
        if (HasCompleteV54NetPresentation())
        {
            return formalV54NetCatalog.Get(TideV54NetVisualState.DeployedDry);
        }

        return GetFormalSprite(ref formalNetHdSprite, "AINetHD") ??
            GetFormalSprite(ref formalNetSprite, "AINet");
    }

    private static Sprite GetHarvestCarrySlingSprite()
    {
        if (harvestCarrySlingSprite != null)
        {
            return harvestCarrySlingSprite;
        }

        Sprite formalNet = GetFormalNetSprite();
        if (formalNet == null || formalNet.texture == null)
        {
            return GetNetKnotSprite();
        }

        // Reuse the authored net's central mesh without its stakes, cork line or sinkers.
        // The crop makes a believable folded sling and keeps a new procedural icon out
        // of the formal asset stack.
        Rect source = formalNet.rect;
        Rect slingRect = new Rect(
            source.x + source.width * 0.03f,
            source.y + source.height * 0.02f,
            source.width * 0.94f,
            source.height * 0.58f);
        harvestCarrySlingSprite = Sprite.Create(
            formalNet.texture,
            slingRect,
            new Vector2(0.5f, 0.5f),
            formalNet.pixelsPerUnit);
        harvestCarrySlingSprite.name = "GeneratedStiltFirstHarvestCarrySling";
        return harvestCarrySlingSprite;
    }

    private static Sprite GetFormalStiltWaveImpactSprite()
    {
        return GetFormalSprite(ref formalStiltWaveImpactSprite, "AIStiltWaveImpactHD");
    }

    private static Sprite GetFormalStiltDamageSprite()
    {
        return GetFormalSprite(ref formalStiltDamageSprite, "AIStiltDamageHD");
    }

    private static Sprite GetFormalStiltTideZoneWearSprite()
    {
        if (formalStiltTideZoneWearSprite != null)
        {
            return formalStiltTideZoneWearSprite;
        }

        Sprite damageSource = GetFormalStiltDamageSprite();
        if (damageSource == null)
        {
            return null;
        }

        // The lower section contains barnacles, algae and surface loss without the
        // dramatic central fracture. Repeating this narrow crop at unequal heights
        // reads as a historic tide zone instead of four duplicated broken posts.
        formalStiltTideZoneWearSprite = CreateFormalSubSprite(
            damageSource,
            new Vector4(0.04f, 0.01f, 0.92f, 0.43f),
            "GeneratedStiltFirstFormalTideZonePostWear");
        return formalStiltTideZoneWearSprite;
    }

    private static Sprite GetFormalSeaCurrentCrestSprite()
    {
        return GetFormalSprite(ref formalSeaCurrentCrestSprite, "AISeaCurrentCrestHD");
    }

    private static Sprite GetNetLineSprite()
    {
        return GetResourceSpriteOrFallback(ref netLineSprite, "GeneratedStiltFirstNetLineSprite", () => CreateRopeSprite("GeneratedStiltFirstNetLineSprite"));
    }

    private static Sprite GetTowRopeSprite()
    {
        // A towline is not a compressed fishing-net border. Keep a dedicated resource
        // key so later production art can replace this braided fallback without changing
        // net rendering or any salvage-state code.
        return GetResourceSpriteOrFallback(
            ref towRopeSprite,
            "GeneratedStiltFirstTowRopeSprite",
            () => CreateRopeSprite("GeneratedStiltFirstTowRopeSprite"));
    }

    private static Sprite GetNetWeightSprite()
    {
        return GetResourceSpriteOrFallback(ref netWeightSprite, "GeneratedStiltFirstNetStoneWeightSprite", CreateNetStoneWeightSprite);
    }

    private static Sprite GetNetKnotSprite()
    {
        return GetResourceSpriteOrFallback(ref netKnotSprite, "GeneratedStiltFirstNetMeshKnotSprite", CreateNetKnotSprite);
    }

    private static Sprite GetCorkFloatSprite()
    {
        return GetResourceSpriteOrFallback(ref corkFloatSprite, "GeneratedStiltFirstNetCorkFloatSprite", CreateCorkFloatSprite);
    }

    private static Sprite GetBoatHullSprite()
    {
        Sprite formal = GetFormalBoatSprite();
        if (formal != null)
        {
            return formal;
        }

        return GetResourceSpriteOrFallback(ref boatHullSprite, "GeneratedStiltFirstBoatHullSprite", CreateBoatSprite);
    }

    private static Sprite GetFormalBoatSprite()
    {
        return GetFormalSprite(ref formalBoatHdSprite, "AISailboatHD") ??
            GetFormalSprite(ref formalBoatSprite, "AISailboat");
    }

    private static Sprite GetFormalDamagedBoatSprite()
    {
        return GetFormalSprite(ref formalDamagedBoatHdSprite, "AISailboatDamagedHD");
    }

    private static Sprite GetFormalReefedBoatSprite()
    {
        return GetFormalSprite(ref formalReefedBoatHdSprite, "AISailboatReefedHD");
    }

    private static Sprite GetFormalDamagedReefedBoatSprite()
    {
        return GetFormalSprite(ref formalDamagedReefedBoatHdSprite, "AISailboatDamagedReefedHD");
    }

    private static Sprite GetFormalBoatGunwaleSprite(Sprite sourceBoat)
    {
        if (sourceBoat == null)
        {
            return null;
        }

        if (formalBoatGunwaleSprites.TryGetValue(sourceBoat, out Sprite cached) && cached != null)
        {
            return cached;
        }

        // This crop is the lower saltwood hull from the exact currently displayed
        // boat variant. It is not a painted placeholder: repaired, torn and reefed
        // boats therefore keep matching plank grain when the gunwale covers the legs.
        Sprite gunwale = CreateFormalSubSprite(
            sourceBoat,
            new Vector4(0.015f, 0f, 0.97f, 0.285f),
            "GeneratedStiltFirstFormalBoatGunwale_" + sourceBoat.name);
        formalBoatGunwaleSprites[sourceBoat] = gunwale;
        return gunwale;
    }

    private static Sprite GetFormalBoatRigSprite(Sprite sourceBoat)
    {
        if (sourceBoat == null)
        {
            return null;
        }

        if (formalBoatRigSprites.TryGetValue(sourceBoat, out Sprite cached) && cached != null)
        {
            return cached;
        }

        // The split line exactly matches GetFormalBoatGunwaleSprite. Keeping both
        // crops from the same source texture means their mast, ropes and plank grain
        // rejoin without a painted seam after the passenger is inserted between them.
        Sprite rig = CreateFormalSubSprite(
            sourceBoat,
            new Vector4(0.015f, 0.285f, 0.97f, 0.715f),
            "GeneratedStiltFirstFormalBoatRig_" + sourceBoat.name);
        formalBoatRigSprites[sourceBoat] = rig;
        return rig;
    }

    private static Sprite GetSailSprite()
    {
        return GetResourceSpriteOrFallback(ref sailSprite, "GeneratedStiltFirstBoatSailSprite", CreateSailSprite);
    }

    private static Sprite GetBoatRiggingSprite()
    {
        return GetResourceSpriteOrFallback(ref boatRiggingSprite, "GeneratedStiltFirstBoatRiggingSprite", CreateWetRiggingSprite);
    }

    private static Sprite GetBoatSailPatchStitchSprite()
    {
        return GetResourceSpriteOrFallback(ref boatSailPatchStitchSprite, "GeneratedStiltFirstBoatSailPatchStitchSprite", CreateBoatSailPatchStitchSprite);
    }

    private static Sprite GetBoatCockpitSprite()
    {
        return GetResourceSpriteOrFallback(ref boatCockpitSprite, "GeneratedStiltFirstBoatCockpitSprite", CreateBoatCockpitSprite);
    }

    private static Sprite GetBoatPassengerSprite()
    {
        return GetResourceSpriteOrFallback(ref boatPassengerSprite, "GeneratedStiltFirstBoatPassengerSprite", CreateBoatPassengerSprite);
    }

    private static Sprite GetSailingRouteBuoySprite()
    {
        return GetResourceSpriteOrFallback(ref sailingRouteBuoySprite, "GeneratedStiltFirstSailingRouteBuoySprite", CreateSailingRouteBuoySprite);
    }

    private static Sprite GetLighthouseSprite()
    {
        Sprite formal = GetFormalSprite(ref formalLighthouseSprite, "AILighthouse");
        if (formal != null)
        {
            return formal;
        }

        return GetResourceSpriteOrFallback(ref lighthouseSprite, "GeneratedStiltFirstLighthouseSprite", CreateLighthouseSprite);
    }

    private static Sprite GetLighthouseBeamSprite()
    {
        return GetResourceSpriteOrFallback(ref lighthouseBeamSprite, "GeneratedStiltFirstLighthouseLensBeamSprite", CreateLighthouseBeamSprite);
    }

    private static Sprite GetMoonSprite()
    {
        if (moonSprite != null)
        {
            return moonSprite;
        }

        // The formal moon is painted with the same material language as the rest
        // of Tide. A photographic source is no longer used in the runtime scene.
        moonSprite = GetFormalSprite(ref formalMoonSprite, "AIMoon");
        if (moonSprite != null)
        {
            return moonSprite;
        }

        return GetResourceSpriteOrFallback(ref moonSprite, "GeneratedStiltFirstMoonSprite", CreateMoonSprite);
    }

    private static Sprite GetMoonShadowSprite()
    {
        return GetResourceSpriteOrFallback(ref moonShadowSprite, "GeneratedStiltFirstMoonTerminatorSprite", CreateMoonTerminatorSprite);
    }

    private static float GetMoonIllumination01(float phase01)
    {
        return Mathf.Clamp01(0.5f - Mathf.Cos(phase01 * Mathf.PI * 2f) * 0.5f);
    }

    private Sprite GetFirstSliceMoonPhaseShadowSprite(float phase01, float illumination01)
    {
        int bucket = Mathf.RoundToInt(Mathf.Repeat(phase01, 1f) * 96f);
        if (moonPhaseShadowSprite != null && moonPhaseShadowBucket == bucket)
        {
            return moonPhaseShadowSprite;
        }

        moonPhaseShadowBucket = bucket;
        moonPhaseShadowSprite = CreateMoonPhaseShadowSprite(phase01, illumination01);
        return moonPhaseShadowSprite;
    }

    private Vector2 GetMatchedMoonShadowScale(Vector2 moonScale)
    {
        if (moonRenderer == null ||
            moonRenderer.sprite == null ||
            moonShadowRenderer == null ||
            moonShadowRenderer.sprite == null)
        {
            return moonScale;
        }

        Vector2 moonSize = moonRenderer.sprite.bounds.size;
        Vector2 shadowSize = moonShadowRenderer.sprite.bounds.size;
        return new Vector2(
            moonScale.x * moonSize.x / Mathf.Max(0.001f, shadowSize.x),
            moonScale.y * moonSize.y / Mathf.Max(0.001f, shadowSize.y));
    }

    private static Sprite GetShipwreckRibSprite()
    {
        Sprite formal = GetFormalSprite(ref formalShipwreckSprite, "AIShipwreck");
        if (formal != null)
        {
            return formal;
        }

        return GetResourceSpriteOrFallback(ref shipwreckRibSprite, "GeneratedStiltFirstShipwreckRibSprite", CreateShipwreckRibSprite);
    }

    private static Sprite GetFormalWetDebrisSprite()
    {
        if (formalWetDebrisSprite != null)
        {
            return formalWetDebrisSprite;
        }

        Sprite source = GetFormalSprite(ref formalShipwreckSprite, "AIShipwreck");
        if (source == null || source.texture == null)
        {
            return null;
        }

        // The lower band contains loose, salt-darkened planks and rope without the
        // intact silhouette of another wreck. It reads as waterlogged debris in a net.
        Rect sourceRect = source.rect;
        Rect debrisRect = new Rect(
            sourceRect.x + sourceRect.width * 0.05f,
            sourceRect.y + sourceRect.height * 0.02f,
            sourceRect.width * 0.9f,
            sourceRect.height * 0.30f);
        formalWetDebrisSprite = Sprite.Create(
            source.texture,
            debrisRect,
            new Vector2(0.5f, 0.5f),
            source.pixelsPerUnit,
            0,
            SpriteMeshType.FullRect);
        formalWetDebrisSprite.name = "GeneratedStiltFirstFormalWetDebrisSprite";
        return formalWetDebrisSprite;
    }

    private static Sprite GetFormalSprite(ref Sprite cachedSprite, string resourceName)
    {
        if (cachedSprite == null)
        {
            cachedSprite = Resources.Load<Sprite>(FormalAiResourceRoot + resourceName);
        }

        return cachedSprite;
    }

    private static Sprite GetFishSprite()
    {
        Sprite formal = GetFormalSprite(ref formalFishSprite, "AIHarvestFish");
        if (formal != null)
        {
            return formal;
        }

        return GetResourceSpriteOrFallback(ref fishSprite, "GeneratedStiltFirstHarvestFishSprite", CreateFishSprite);
    }

    private static Sprite GetWoodSprite()
    {
        Sprite formal = GetFormalSprite(ref formalRepairTimberSprite, "AIRepairTimber");
        if (formal != null)
        {
            return formal;
        }

        return GetResourceSpriteOrFallback(ref woodSprite, "GeneratedStiltFirstHarvestSaltWoodSprite", CreateSaltWoodBeamSprite);
    }

    private static Sprite GetLooseSaltWoodSprite()
    {
        if (formalLooseSaltWoodSprite != null)
        {
            return formalLooseSaltWoodSprite;
        }

        Sprite formalBundle = GetFormalSprite(ref formalRepairTimberSprite, "AIRepairTimber");
        if (formalBundle != null)
        {
            // The upper strip is one authored salt-darkened timber without the bundle's
            // lower logs. It keeps later load tiers in the same resolution and palette.
            formalLooseSaltWoodSprite = CreateFormalSubSprite(
                formalBundle,
                new Vector4(0.04f, 0.56f, 0.92f, 0.38f),
                "GeneratedStiltFirstFormalLooseSaltWood");
            return formalLooseSaltWoodSprite;
        }

        return GetResourceSpriteOrFallback(ref woodSprite, "GeneratedStiltFirstHarvestSaltWoodSprite", CreateSaltWoodBeamSprite);
    }

    private static Sprite GetSeaSalvageHookSprite()
    {
        if (formalSeaSalvageHookSprite != null)
        {
            return formalSeaSalvageHookSprite;
        }

        Sprite fullHook = GetFormalSprite(ref formalSeaSalvageHookFullSprite, "AISeaSalvageHookHD");
        if (fullHook != null)
        {
            // V19 supplies a tall shore rope with a useful iron hook near its lower
            // centre. Keep only the hook and a short wet rope tail for the boat cast;
            // the long, changing towline is rendered separately from live positions.
            formalSeaSalvageHookSprite = CreateFormalSubSprite(
                fullHook,
                new Vector4(0.39f, 0.38f, 0.22f, 0.24f),
                "GeneratedStiltFirstFormalSeaSalvageHook");
            return formalSeaSalvageHookSprite;
        }

        return GetNetKnotSprite();
    }

    private static Sprite GetRoutingBoomSprite()
    {
        if (formalRoutingBoomSprite != null)
        {
            return formalRoutingBoomSprite;
        }

        Sprite formalBundle = GetFormalSprite(ref formalRepairTimberSprite, "AIRepairTimber");
        if (formalBundle != null)
        {
            // The complete repair asset is a tied four-log bundle. A routing boom is
            // one loose pole, so use the clean left half of its upper timber and keep
            // the bundle rope out of the crop. At gameplay scale this remains sharp
            // without introducing a second temporary art language.
            formalRoutingBoomSprite = CreateFormalSubSprite(
                formalBundle,
                new Vector4(0.04f, 0.72f, 0.39f, 0.19f),
                "GeneratedStiltFirstFormalRoutingBoom");
            return formalRoutingBoomSprite;
        }

        return GetResourceSpriteOrFallback(
            ref woodSprite,
            "GeneratedStiltFirstHarvestSaltWoodSprite",
            CreateSaltWoodBeamSprite);
    }

    private static Sprite GetRoutingWindlassSprite(bool working)
    {
        Sprite found = GetFormalSprite(ref formalRoutingWindlassFoundSprite, "AIWindlassFoundHD");
        if (!working)
        {
            return found;
        }

        // The working frame is a derived runtime copy of V19. Source production art
        // remains untouched so the parallel art session can replace that handoff
        // independently; gameplay only consumes the stable Resources contract.
        return GetFormalSprite(ref formalRoutingWindlassWorkingSprite, "AIWindlassWorkingHD") ?? found;
    }

    private static Sprite GetRelicSprite()
    {
        Sprite formal = GetFormalSprite(ref formalRouteRelicSprite, "AIRouteRelic");
        if (formal != null)
        {
            return formal;
        }

        return GetResourceSpriteOrFallback(ref relicSprite, "GeneratedStiltFirstHarvestRelicSprite", CreateRelicSprite);
    }

    private static Sprite GetRelicPieceSprite(int pieceIndex)
    {
        Sprite formalRelic = GetFormalSprite(ref formalRouteRelicSprite, "AIRouteRelic");
        if (formalRelic == null)
        {
            return GetRelicSprite();
        }

        if (pieceIndex <= 0)
        {
            if (formalRelicMapPieceSprite == null)
            {
                formalRelicMapPieceSprite = CreateFormalSubSprite(
                    formalRelic,
                    new Vector4(0.01f, 0.31f, 0.67f, 0.67f),
                    "GeneratedStiltFirstFormalWetChartPiece");
            }

            return formalRelicMapPieceSprite;
        }

        if (pieceIndex == 1)
        {
            if (formalRelicLargeCompassSprite == null)
            {
                formalRelicLargeCompassSprite = CreateFormalSubSprite(
                    formalRelic,
                    new Vector4(0.22f, 0.01f, 0.57f, 0.59f),
                    "GeneratedStiltFirstFormalLargeCompassPiece");
            }

            return formalRelicLargeCompassSprite;
        }

        if (formalRelicSmallCompassSprite == null)
        {
            formalRelicSmallCompassSprite = CreateFormalSubSprite(
                formalRelic,
                new Vector4(0.65f, 0.23f, 0.34f, 0.58f),
                "GeneratedStiltFirstFormalSmallCompassPiece");
        }

        return formalRelicSmallCompassSprite;
    }

    private static Sprite CreateFormalSubSprite(Sprite source, Vector4 normalizedRect, string spriteName)
    {
        Rect sourceRect = source.rect;
        Rect crop = new Rect(
            sourceRect.x + sourceRect.width * normalizedRect.x,
            sourceRect.y + sourceRect.height * normalizedRect.y,
            sourceRect.width * normalizedRect.z,
            sourceRect.height * normalizedRect.w);
        Sprite result = Sprite.Create(
            source.texture,
            crop,
            new Vector2(0.5f, 0.5f),
            source.pixelsPerUnit,
            0,
            SpriteMeshType.FullRect);
        result.name = spriteName;
        return result;
    }

    private static Sprite GetTrashSprite()
    {
        return GetResourceSpriteOrFallback(ref trashSprite, "GeneratedStiltFirstHarvestWetTrashBundleSprite", CreateWetTrashBundleSprite);
    }

    private static Sprite GetRepairPatchSprite()
    {
        return GetResourceSpriteOrFallback(ref repairPatchSprite, "GeneratedStiltFirstRepairRopeLashingSprite", CreateRepairLashingSprite);
    }

    private static Sprite GetStormLineSprite()
    {
        return GetResourceSpriteOrFallback(ref stormLineSprite, "GeneratedStiltFirstStormLineSprite", () => CreateWaveSprite(128, 18, "GeneratedStiltFirstStormLineSprite", false));
    }

    private static Sprite GetWaterWrackSprite()
    {
        return GetResourceSpriteOrFallback(ref waterWrackSprite, "GeneratedStiltFirstWaterWrackSprite", CreateWaterWrackSprite);
    }

    private static Sprite GetLighthouseChartClueSprite()
    {
        Sprite formal = GetFormalSprite(ref formalRouteRelicSprite, "AIRouteRelic");
        if (formal != null)
        {
            return formal;
        }

        return GetResourceSpriteOrFallback(ref lighthouseChartClueSprite, "GeneratedStiltFirstLighthouseChartClueSprite", CreateLighthouseChartClueSprite);
    }

    private static Sprite GetStormWarningPennantSprite()
    {
        return GetResourceSpriteOrFallback(ref stormWarningPennantSprite, "GeneratedStiltFirstStormWarningPennantSprite", CreateStormWarningPennantSprite);
    }

    private static Sprite GetPrepRopeCoilSprite()
    {
        return GetResourceSpriteOrFallback(ref prepRopeCoilSprite, "GeneratedStiltFirstPrepRopeCoilSprite", CreatePrepRopeCoilSprite);
    }

    private static Sprite GetPrepBucketSprite()
    {
        return GetResourceSpriteOrFallback(ref prepBucketSprite, "GeneratedStiltFirstPrepBucketSprite", CreatePrepBucketSprite);
    }

    private static Sprite GetPrepStakeSprite()
    {
        return GetResourceSpriteOrFallback(ref prepStakeSprite, "GeneratedStiltFirstPrepStakeSprite", CreatePrepStakeSprite);
    }

    private static Sprite GetResourceSpriteOrFallback(ref Sprite cachedSprite, string resourceName, Func<Sprite> createFallback)
    {
        if (cachedSprite != null)
        {
            return cachedSprite;
        }

        cachedSprite = Resources.Load<Sprite>(FirstSliceResourceRoot + resourceName);
        if (cachedSprite == null)
        {
            cachedSprite = createFallback();
        }

        return cachedSprite;
    }

    private static Sprite CreateHouseSprite()
    {
        const int width = 128;
        const int height = 72;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstHouseTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool body = x > 5 && x < 123 && y > 8 && y < 62;
                bool plankLine = body && y % 11 < 2;
                bool window = x > 24 && x < 94 && y > 25 && y < 52;
                bool frame = window && (x < 29 || x > 89 || y < 30 || y > 47 || Mathf.Abs(x - 59) < 2);
                Color pixel = Color.clear;
                if (body)
                {
                    float n = Hash01(x, y, 12);
                    pixel = new Color(0.62f - n * 0.12f, 0.42f - n * 0.08f, 0.25f - n * 0.05f, 1f);
                }
                if (plankLine)
                {
                    pixel = Color.Lerp(pixel, new Color(0.22f, 0.12f, 0.08f, 1f), 0.45f);
                }
                if (window && !frame)
                {
                    pixel = new Color(1f, 0.58f + Hash01(x, y, 33) * 0.2f, 0.18f, 0.9f);
                }
                if (frame)
                {
                    pixel = new Color(0.18f, 0.09f, 0.04f, 1f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstHouseSprite");
    }

    private static Sprite CreateRoofSprite()
    {
        const int width = 128;
        const int height = 48;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstRoofTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float left = 8f + y * 1.35f;
                float right = width - 9f - y * 1.1f;
                bool roof = x > left && x < right && y > 6 && y < 42;
                bool torn = roof && Hash01(x, y, 49) > 0.965f;
                Color pixel = Color.clear;
                if (roof && !torn)
                {
                    pixel = new Color(0.56f, 0.18f + Hash01(x, y, 50) * 0.08f, 0.09f, 1f);
                    if (y % 7 < 2)
                    {
                        pixel = Color.Lerp(pixel, new Color(0.26f, 0.08f, 0.04f, 1f), 0.35f);
                    }
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstRoofSprite");
    }

    private static Sprite CreateWindowSprite()
    {
        const int width = 80;
        const int height = 40;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstWarmLampTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = (x - 40f) / 40f;
                float dy = (y - 20f) / 22f;
                float glow = Mathf.Clamp01(1f - dx * dx - dy * dy);
                bool pane = x > 8 && x < 72 && y > 8 && y < 32;
                bool frame = pane && (Mathf.Abs(x - 40) < 1.5f || Mathf.Abs(y - 20) < 1.5f);
                Color pixel = new Color(1f, 0.55f, 0.16f, glow * 0.72f);
                if (!pane)
                {
                    pixel.a *= 0.45f;
                }
                if (frame)
                {
                    pixel = new Color(0.22f, 0.1f, 0.04f, 0.82f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstWarmLampSprite");
    }

    private static Sprite CreateSaltwoodDeckSprite()
    {
        const int width = 140;
        const int height = 26;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstSaltwoodDeckTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool body = x > 2 && x < width - 3 && y > 4 && y < 21;
                bool boardGap = body && (y == 10 || y == 17 || x % 29 < 2);
                bool wetEdge = body && y < 7;
                bool nail = body && ((x % 34 == 8 || x % 34 == 25) && y > 12 && y < 15);
                bool brokenLip = body && y > 18 && Hash01(x, y, 231) > 0.955f;
                Color pixel = Color.clear;
                if (body)
                {
                    float n = Hash01(x, y, 232);
                    pixel = new Color(0.52f + n * 0.08f, 0.38f + n * 0.05f, 0.23f, 0.96f);
                }
                if (boardGap)
                {
                    pixel = Color.Lerp(pixel, new Color(0.1f, 0.065f, 0.04f, 0.96f), 0.52f);
                }
                if (wetEdge)
                {
                    pixel = Color.Lerp(pixel, new Color(0.26f, 0.42f, 0.42f, 0.9f), 0.34f);
                }
                if (nail)
                {
                    pixel = new Color(0.045f, 0.05f, 0.045f, 0.95f);
                }
                if (brokenLip)
                {
                    pixel = Color.clear;
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstSaltwoodDeckSprite");
    }

    private static Sprite CreateSaltwoodPostSprite()
    {
        const int width = 30;
        const int height = 132;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstSaltwoodPostTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool post = x > 7 && x < 22 && y > 2 && y < 128;
                bool split = post && (Mathf.Abs(x - (12f + Mathf.Sin(y * 0.07f) * 2.5f)) < 1.1f || Hash01(x, y, 241) > 0.982f);
                bool barnacle = post && y < 44 && Hash01(x, y, 242) > 0.93f;
                bool ropeBand = post && (Mathf.Abs(y - 78f) < 2f || Mathf.Abs(y - 86f) < 2f);
                Color pixel = Color.clear;
                if (post)
                {
                    float n = Hash01(x, y, 243);
                    pixel = new Color(0.39f + n * 0.1f, 0.25f + n * 0.06f, 0.14f, 0.96f);
                }
                if (split)
                {
                    pixel = Color.Lerp(pixel, new Color(0.07f, 0.045f, 0.025f, 0.96f), 0.58f);
                }
                if (barnacle)
                {
                    pixel = new Color(0.72f, 0.72f, 0.58f, 0.82f);
                }
                if (ropeBand)
                {
                    pixel = new Color(0.72f, 0.62f, 0.38f, 0.94f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstSaltwoodPostSprite");
    }

    private static Sprite CreateHouseSaltStreakSprite()
    {
        const int width = 20;
        const int height = 82;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstHouseSaltStreakTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float center = 9f + Mathf.Sin(y * 0.13f) * 2.2f;
                bool streak = Mathf.Abs(x - center) < Mathf.Lerp(3.5f, 0.8f, y / 82f);
                bool drip = y < 28 && Mathf.Abs(x - (center + 3f)) < 1.1f;
                bool broken = Hash01(x, y, 251) > 0.94f;
                Color pixel = Color.clear;
                if ((streak || drip) && !broken)
                {
                    float fade = Mathf.InverseLerp(82f, 6f, y);
                    pixel = new Color(0.78f, 0.84f, 0.68f, 0.48f * fade);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstHouseSaltStreakSprite");
    }

    private static Sprite CreateShelterWarmRaySprite()
    {
        const int width = 96;
        const int height = 24;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstShelterWarmRayTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float center = 12f + Mathf.Sin(x * 0.1f) * 1.2f;
                float taper = Mathf.Lerp(5f, 1.2f, x / 96f);
                float distance = Mathf.Abs(y - center);
                bool ray = distance < taper;
                bool brokenByRain = Hash01(x, y, 281) > 0.972f || x % 23 < 2;
                Color pixel = Color.clear;
                if (ray && !brokenByRain)
                {
                    float alpha = Mathf.Clamp01(1f - distance / taper) * Mathf.Lerp(0.52f, 0.08f, x / 96f);
                    pixel = new Color(1f, 0.58f, 0.18f, alpha);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstShelterWarmRaySprite");
    }

    private static Sprite CreateRaincoatHumanSprite()
    {
        const int width = 44;
        const int height = 86;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstPlayerTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool hood = IsEllipse(x, y, 21f, 68f, 9f, 9f) && y < 78;
                bool faceShadow = IsEllipse(x, y, 22f, 66f, 5f, 5f) && y < 69;
                bool shoulder = y > 48 && y < 61 && x > 10 + (61 - y) * 0.25f && x < 34 - (61 - y) * 0.2f;
                bool coat = y > 20 && y < 54 && x > 12 - (54 - y) * 0.08f && x < 33 + (54 - y) * 0.05f;
                bool coatHem = y > 18 && y < 25 && x > 8 && x < 36;
                bool legA = x > 14 && x < 18 && y > 4 && y < 24;
                bool legB = x > 24 && x < 28 && y > 4 && y < 24;
                bool bootA = x > 11 && x < 19 && y > 1 && y < 6;
                bool bootB = x > 23 && x < 32 && y > 1 && y < 6;
                bool strap = Mathf.Abs(x - (31f - y * 0.18f)) < 1.4f && y > 25 && y < 55;
                bool bag = x > 29 && x < 39 && y > 28 && y < 48 && Mathf.Abs(x - 34f) + Mathf.Abs(y - 38f) * 0.38f < 8f;
                bool rainEdge = (coat || shoulder) && (x == 12 || x == 33 || y % 13 == 0);
                Color pixel = Color.clear;
                if (hood)
                {
                    pixel = new Color(0.13f, 0.18f, 0.17f, 1f);
                }
                if (faceShadow)
                {
                    pixel = new Color(0.04f, 0.05f, 0.045f, 0.96f);
                }
                else if (shoulder || coat || coatHem)
                {
                    float n = Hash01(x, y, 135);
                    pixel = new Color(0.26f + n * 0.07f, 0.39f + n * 0.08f, 0.36f + n * 0.06f, 1f);
                }
                if (rainEdge)
                {
                    pixel = Color.Lerp(pixel, new Color(0.65f, 0.78f, 0.7f, 1f), 0.45f);
                }
                if (legA || legB)
                {
                    pixel = new Color(0.09f, 0.11f, 0.1f, 1f);
                }
                if (bootA || bootB)
                {
                    pixel = new Color(0.035f, 0.04f, 0.035f, 1f);
                }
                if (strap)
                {
                    pixel = new Color(0.62f, 0.54f, 0.38f, 0.95f);
                }
                if (bag)
                {
                    pixel = new Color(0.12f, 0.16f, 0.14f, 0.94f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstPlayerRaincoatSprite");
    }

    private static Sprite CreateNetStoneWeightSprite()
    {
        const int width = 34;
        const int height = 44;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstNetWeightTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool stone = y > 6 && y < 35 && Mathf.Abs(x - 17f) + Mathf.Abs(y - 21f) * 0.55f < 14f + Hash01(x, y, 139) * 2f;
                bool tie = (Mathf.Abs(x - 15f) < 1.5f || Mathf.Abs(x - 20f) < 1.5f) && y > 1 && y < 16;
                bool saltChip = stone && Hash01(x, y, 140) > 0.93f;
                Color pixel = Color.clear;
                if (stone)
                {
                    float n = Hash01(x, y, 141);
                    pixel = new Color(0.34f + n * 0.08f, 0.38f + n * 0.08f, 0.34f + n * 0.08f, 0.95f);
                }
                if (saltChip)
                {
                    pixel = new Color(0.68f, 0.74f, 0.66f, 0.9f);
                }
                if (tie)
                {
                    pixel = new Color(0.72f, 0.62f, 0.4f, 0.96f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstNetStoneWeightSprite");
    }

    private static Sprite CreateNetKnotSprite()
    {
        const int size = 32;
        Texture2D texture = NewTexture(size, size, "GeneratedStiltFirstNetMeshKnotTexture");
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool ropeA = Mathf.Abs(y - x * 0.55f - 7f) < 1.7f;
                bool ropeB = Mathf.Abs(y + x * 0.48f - 23f) < 1.7f;
                bool ropeC = Mathf.Abs(y - 16f) < 1.2f && x > 4 && x < 28;
                bool knot = IsEllipse(x, y, 16f, 16f, 5.2f, 4.2f);
                bool wetSpark = Hash01(x, y, 151) > 0.965f && (ropeA || ropeB || ropeC || knot);
                Color pixel = Color.clear;
                if (ropeA || ropeB || ropeC)
                {
                    pixel = new Color(0.66f, 0.62f, 0.42f, 0.78f);
                }
                if (knot)
                {
                    pixel = new Color(0.78f, 0.68f, 0.44f, 0.94f);
                }
                if (wetSpark)
                {
                    pixel = Color.Lerp(pixel, new Color(0.72f, 0.96f, 0.9f, 0.9f), 0.5f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, size, size, "GeneratedStiltFirstNetMeshKnotSprite");
    }

    private static Sprite CreateCorkFloatSprite()
    {
        const int width = 42;
        const int height = 24;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstNetCorkFloatTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool cork = IsEllipse(x, y, 21f, 12f, 17f, 7.5f);
                bool groove = cork && (Mathf.Abs(x - 12f) < 1.2f || Mathf.Abs(x - 28f) < 1.2f || y == 12);
                bool chipped = cork && Hash01(x, y, 161) > 0.955f;
                Color pixel = Color.clear;
                if (cork)
                {
                    float n = Hash01(x, y, 162);
                    pixel = new Color(0.78f + n * 0.06f, 0.62f + n * 0.05f, 0.34f, 0.94f);
                }
                if (groove)
                {
                    pixel = Color.Lerp(pixel, new Color(0.28f, 0.18f, 0.09f, 0.95f), 0.45f);
                }
                if (chipped)
                {
                    pixel = new Color(0.9f, 0.82f, 0.62f, 0.92f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstNetCorkFloatSprite");
    }

    private static Sprite CreateBoatSprite()
    {
        const int width = 96;
        const int height = 36;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstBoatHullTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float left = 8f + y * 1.2f;
                float right = 88f - y * 0.9f;
                bool hull = y > 5 && y < 25 && x > left && x < right;
                bool rim = hull && (y > 20 || y < 9);
                Color pixel = Color.clear;
                if (hull)
                {
                    pixel = rim ? new Color(0.18f, 0.09f, 0.04f, 1f) : new Color(0.48f, 0.25f, 0.13f, 1f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstBoatHullSprite");
    }

    private static Sprite CreateSailSprite()
    {
        const int width = 60;
        const int height = 80;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstBoatSailTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool mast = Mathf.Abs(x - 11) < 2 && y > 5 && y < 76;
                bool sail = x > 12 && x < 51 - y * 0.38f && y > 12 && y < 68;
                bool patch = sail && x > 27 && x < 39 && y > 32 && y < 46;
                Color pixel = Color.clear;
                if (sail)
                {
                    pixel = new Color(0.78f, 0.76f, 0.64f, 0.92f);
                }
                if (patch)
                {
                    pixel = new Color(0.55f, 0.46f, 0.35f, 0.94f);
                }
                if (mast)
                {
                    pixel = new Color(0.22f, 0.12f, 0.06f, 1f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstBoatSailSprite");
    }

    private static Sprite CreateWetRiggingSprite()
    {
        const int width = 120;
        const int height = 18;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstBoatRiggingTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float wave = 9f + Mathf.Sin(x * 0.22f) * 1.5f;
                bool rope = Mathf.Abs(y - wave) < 1.4f || Mathf.Abs(y - (wave + 4f)) < 0.8f;
                bool fiber = rope && Hash01(x, y, 171) > 0.78f;
                bool droplet = Hash01(x, y, 172) > 0.986f && y > wave && y < wave + 7f;
                Color pixel = Color.clear;
                if (rope)
                {
                    pixel = new Color(0.58f, 0.56f, 0.42f, 0.82f);
                }
                if (fiber)
                {
                    pixel = Color.Lerp(pixel, new Color(0.86f, 0.78f, 0.55f, 0.84f), 0.45f);
                }
                if (droplet)
                {
                    pixel = new Color(0.55f, 0.9f, 0.92f, 0.7f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstBoatRiggingSprite");
    }

    private static Sprite CreateBoatSailPatchStitchSprite()
    {
        const int width = 54;
        const int height = 24;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstBoatSailPatchStitchTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool patch = x > 6 && x < 47 && y > 4 && y < 19;
                bool stitch = patch && ((x % 7 < 2 && (y < 8 || y > 15)) || (y % 9 < 2 && (x < 11 || x > 42)));
                bool crease = patch && Mathf.Abs(y - (x * 0.12f + 8f)) < 1.1f;
                bool torn = patch && Hash01(x, y, 291) > 0.965f;
                Color pixel = Color.clear;
                if (patch && !torn)
                {
                    float n = Hash01(x, y, 292);
                    pixel = new Color(0.72f + n * 0.05f, 0.66f + n * 0.04f, 0.5f, 0.72f);
                }
                if (crease)
                {
                    pixel = Color.Lerp(pixel, new Color(0.36f, 0.3f, 0.2f, 0.8f), 0.35f);
                }
                if (stitch)
                {
                    pixel = new Color(0.88f, 0.74f, 0.42f, 0.95f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstBoatSailPatchStitchSprite");
    }

    private static Sprite CreateBoatCockpitSprite()
    {
        const int width = 64;
        const int height = 32;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstBoatCockpitTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx = (x + 0.5f - width * 0.5f) / (width * 0.5f);
                float ny = (y + 0.5f - height * 0.5f) / (height * 0.5f);
                float hollow = nx * nx * 1.25f + ny * ny * 2.4f;
                bool rim = hollow < 0.82f && hollow > 0.52f;
                bool well = hollow <= 0.52f && y > 5 && y < 26;
                bool wetEdge = rim && (x + y) % 9 < 3;
                Color pixel = Color.clear;
                if (well)
                {
                    pixel = new Color(0.08f, 0.045f, 0.028f, 0.95f);
                }
                if (rim)
                {
                    pixel = wetEdge
                        ? new Color(0.34f, 0.22f, 0.13f, 0.96f)
                        : new Color(0.2f, 0.11f, 0.06f, 0.98f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstBoatCockpitSprite");
    }

    private static Sprite CreateBoatPassengerSprite()
    {
        const int width = 42;
        const int height = 68;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstBoatPassengerTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float cx = width * 0.5f;
                bool hood = (x - cx) * (x - cx) / 75f + (y - 47f) * (y - 47f) / 105f < 1f;
                bool torso = Mathf.Abs(x - cx) < Mathf.Lerp(12f, 7f, Mathf.Clamp01((y - 14f) / 33f)) && y > 13 && y < 45;
                bool knees = y > 5 && y < 17 && x > 8 && x < 34 && Mathf.Abs(x - cx) > 4f;
                bool faceShade = hood && y < 48 && y > 37 && Mathf.Abs(x - cx) < 5f;
                bool wetStripe = torso && Mathf.Abs(x - (cx + 5f)) < 1.4f && y > 16 && y < 39;
                Color pixel = Color.clear;
                if (torso || knees)
                {
                    pixel = new Color(0.62f, 0.56f, 0.34f, 0.94f);
                }
                if (hood)
                {
                    pixel = new Color(0.52f, 0.55f, 0.34f, 0.98f);
                }
                if (faceShade)
                {
                    pixel = new Color(0.04f, 0.05f, 0.04f, 0.9f);
                }
                if (wetStripe)
                {
                    pixel = new Color(0.82f, 0.74f, 0.42f, 0.88f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstBoatPassengerSprite");
    }

    private static Sprite CreateSailingRouteBuoySprite()
    {
        const int width = 30;
        const int height = 54;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstSailingRouteBuoyTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float cx = width * 0.5f;
                bool floatTop = (x - cx) * (x - cx) / 82f + (y - 35f) * (y - 35f) / 58f < 1f;
                bool neck = Mathf.Abs(x - cx) < 3.5f && y > 16 && y < 34;
                bool ropeTail = Mathf.Abs(x - (cx + Mathf.Sin(y * 0.28f) * 2.2f)) < 1.2f && y > 2 && y < 18;
                bool saltBand = floatTop && y > 33 && y < 38;
                Color pixel = Color.clear;
                if (floatTop)
                {
                    pixel = new Color(0.88f, 0.64f, 0.26f, 0.94f);
                }
                if (saltBand)
                {
                    pixel = new Color(0.72f, 0.93f, 0.86f, 0.84f);
                }
                if (neck || ropeTail)
                {
                    pixel = new Color(0.58f, 0.52f, 0.35f, 0.82f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstSailingRouteBuoySprite");
    }

    private static Sprite CreateLighthouseSprite()
    {
        const int width = 48;
        const int height = 120;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstLighthouseTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float half = Mathf.Lerp(6f, 13f, y / 120f);
                bool tower = Mathf.Abs(x - 24f) < half && y > 5 && y < 102;
                bool light = x > 14 && x < 34 && y > 86 && y < 101;
                Color pixel = Color.clear;
                if (tower)
                {
                    pixel = new Color(0.56f, 0.58f, 0.48f, 0.75f);
                }
                if (light)
                {
                    pixel = new Color(1f, 0.78f, 0.3f, 0.82f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstLighthouseSprite");
    }

    private static Sprite CreateLighthouseBeamSprite()
    {
        const int width = 160;
        const int height = 34;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstLighthouseLensBeamTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float center = height * 0.5f + Mathf.Sin(x * 0.04f) * 2.2f;
                float spread = Mathf.Lerp(4f, 13f, x / 160f);
                float distance = Mathf.Abs(y - center);
                bool beam = distance < spread;
                bool fogBreak = Hash01(x, y, 261) > 0.965f || (x % 31 < 3 && y % 7 < 2);
                Color pixel = Color.clear;
                if (beam && !fogBreak)
                {
                    float alpha = Mathf.Clamp01(1f - distance / spread) * Mathf.Lerp(0.34f, 0.05f, x / 160f);
                    pixel = new Color(1f, 0.82f, 0.38f, alpha);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstLighthouseLensBeamSprite");
    }

    private static Sprite CreateMoonSprite()
    {
        const int size = 88;
        Texture2D texture = NewTexture(size, size, "GeneratedStiltFirstMoonTexture");
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool moon = IsEllipse(x, y, 44f, 44f, 39f, 39f);
                Color pixel = Color.clear;
                if (moon)
                {
                    float n = Hash01(x, y, 81);
                    pixel = new Color(0.62f + n * 0.2f, 0.66f + n * 0.16f, 0.62f + n * 0.12f, 1f);
                    if (Hash01(x, y, 82) > 0.94f)
                    {
                        pixel = Color.Lerp(pixel, new Color(0.22f, 0.28f, 0.26f, 1f), 0.55f);
                    }
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, size, size, "GeneratedStiltFirstMoonSprite");
    }

    private static Sprite CreateMoonTerminatorSprite()
    {
        const int size = 88;
        Texture2D texture = NewTexture(size, size, "GeneratedStiltFirstMoonTerminatorTexture");
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - 44f) / 39f;
                float dy = (y - 44f) / 39f;
                float d = dx * dx + dy * dy;
                bool disc = d <= 1f;
                float feather = Mathf.Clamp01((1f - d) * 3.2f);
                bool craterCut = disc && Hash01(x, y, 181) > 0.955f;
                Color pixel = Color.clear;
                if (disc)
                {
                    pixel = new Color(0.01f, 0.035f, 0.04f, 0.62f * feather);
                }
                if (craterCut)
                {
                    pixel.a *= 0.65f;
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, size, size, "GeneratedStiltFirstMoonTerminatorSprite");
    }

    private static Sprite CreateMoonPhaseShadowSprite(float phase01, float illumination01)
    {
        const int size = 128;
        const float radius = 0.92f;
        const float feather = 0.16f;
        Texture2D texture = NewTexture(size, size, "GeneratedStiltFirstMoonSoftPhaseShadowTexture");
        bool waxing = Mathf.Repeat(phase01, 1f) < 0.5f;
        float litSide = waxing ? 1f : -1f;
        float clampedIllumination = Mathf.Clamp01(illumination01);
        float boundary = Mathf.Lerp(0.98f, -0.98f, clampedIllumination);
        float maxAlpha = Mathf.Lerp(0.98f, 0.02f, clampedIllumination * clampedIllumination);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f - size * 0.5f) / (size * 0.5f);
                float ny = (y + 0.5f - size * 0.5f) / (size * 0.5f);
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                if (r > radius)
                {
                    texture.SetPixel(x, y, Color.clear);
                    continue;
                }

                float signedX = nx * litSide;
                float shadow01 = Mathf.Clamp01((boundary - signedX + feather) / (feather * 2f));
                float rimFade = Mathf.Clamp01((radius - r) / 0.08f);
                float alpha = shadow01 * maxAlpha * rimFade;
                float softNoise = Hash01(x, y, 771) * 0.018f;
                texture.SetPixel(x, y, new Color(0.01f + softNoise, 0.035f + softNoise, 0.04f + softNoise, alpha));
            }
        }

        return FinishSprite(texture, size, size, "GeneratedStiltFirstMoonSoftPhaseShadowSprite");
    }

    private static Sprite CreateShipwreckRibSprite()
    {
        const int width = 34;
        const int height = 106;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstShipwreckRibTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float curve = 15f + Mathf.Sin(y * 0.045f) * 5f + y * 0.045f;
                bool rib = Mathf.Abs(x - curve) < 2.2f && y > 5 && y < 100;
                bool brokenCap = y > 90 && Mathf.Abs(x - curve) < 4.5f && Hash01(x, y, 191) > 0.35f;
                bool saltScar = rib && (x + y) % 13 < 2;
                Color pixel = Color.clear;
                if (rib || brokenCap)
                {
                    float n = Hash01(x, y, 192);
                    pixel = new Color(0.36f + n * 0.11f, 0.23f + n * 0.07f, 0.13f, 0.95f);
                }
                if (saltScar)
                {
                    pixel = Color.Lerp(pixel, new Color(0.76f, 0.74f, 0.58f, 0.9f), 0.45f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstShipwreckRibSprite");
    }

    private static Sprite CreateFishSprite()
    {
        const int width = 54;
        const int height = 28;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstHarvestFishTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool body = IsEllipse(x, y, 28f, 14f, 17f, 8f);
                bool tail = x < 16 && Mathf.Abs(y - 14f) < (16 - x) * 0.42f;
                Color pixel = body || tail ? new Color(0.72f, 0.82f, 0.68f, 0.95f) : Color.clear;
                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstHarvestFishSprite");
    }

    private static Sprite CreateRelicSprite()
    {
        const int size = 48;
        Texture2D texture = NewTexture(size, size, "GeneratedStiltFirstHarvestRelicTexture");
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool body = IsEllipse(x, y, 24f, 24f, 13f, 17f);
                bool hole = IsEllipse(x, y, 24f, 24f, 5f, 7f);
                Color pixel = body && !hole ? new Color(0.76f, 0.62f, 0.32f, 0.92f) : Color.clear;
                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, size, size, "GeneratedStiltFirstHarvestRelicSprite");
    }

    private static Sprite CreateSaltWoodBeamSprite()
    {
        const int width = 70;
        const int height = 26;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstHarvestSaltWoodTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool beam = x > 3 && x < 66 && y > 4 && y < 21;
                bool splitEnd = beam && (x < 9 || x > 60) && Hash01(x, y, 201) > 0.45f;
                bool grain = beam && ((x + y * 2) % 11 < 2 || Hash01(x, y, 202) > 0.965f);
                bool nail = beam && (IsEllipse(x, y, 16f, 13f, 1.8f, 1.8f) || IsEllipse(x, y, 54f, 12f, 1.6f, 1.6f));
                Color pixel = Color.clear;
                if (beam && !splitEnd)
                {
                    float n = Hash01(x, y, 203);
                    pixel = new Color(0.54f + n * 0.08f, 0.38f + n * 0.05f, 0.22f, 0.96f);
                }
                if (grain)
                {
                    pixel = Color.Lerp(pixel, new Color(0.18f, 0.09f, 0.04f, 0.96f), 0.42f);
                }
                if (nail)
                {
                    pixel = new Color(0.07f, 0.08f, 0.07f, 0.95f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstHarvestSaltWoodSprite");
    }

    private static Sprite CreateWetTrashBundleSprite()
    {
        const int width = 60;
        const int height = 42;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstHarvestWetTrashBundleTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool tornBag = y > 8 && y < 32 && x > 9 && x < 51 && Mathf.Abs(x - 30f) + Mathf.Abs(y - 20f) * 0.75f < 24f;
                bool bottle = x > 36 && x < 47 && y > 13 && y < 34 && Mathf.Abs(x - 41f) + Mathf.Abs(y - 24f) * 0.35f < 9f;
                bool wire = IsNearLine(x, y, 9f, 28f, 48f, 12f, 1.3f);
                bool tear = tornBag && (Hash01(x, y, 211) > 0.94f || Mathf.Abs(y - (x * 0.22f + 12f)) < 1.1f);
                Color pixel = Color.clear;
                if (tornBag)
                {
                    float n = Hash01(x, y, 212);
                    pixel = new Color(0.28f + n * 0.06f, 0.36f + n * 0.07f, 0.32f + n * 0.04f, 0.88f);
                }
                if (tear)
                {
                    pixel = new Color(0.08f, 0.1f, 0.09f, 0.86f);
                }
                if (bottle)
                {
                    pixel = new Color(0.42f, 0.72f, 0.68f, 0.58f);
                }
                if (wire)
                {
                    pixel = new Color(0.13f, 0.11f, 0.09f, 0.95f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstHarvestWetTrashBundleSprite");
    }

    private static Sprite CreateRopeSprite(string spriteName)
    {
        const int width = 128;
        const int height = 16;
        Texture2D texture = NewTexture(width, height, spriteName + "Texture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float wave = 8f + Mathf.Sin(x * 0.18f) * 2f;
                bool rope = Mathf.Abs(y - wave) < 2.2f || Mathf.Abs(y - (15f - wave)) < 1.4f;
                Color pixel = rope ? new Color(0.72f, 0.7f, 0.48f, 0.9f) : Color.clear;
                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, spriteName);
    }

    private static Sprite CreateVerandaBodySprite()
    {
        const int width = 128;
        const int height = 64;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstVerandaBodyTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool wall = x > 4 && x < 124 && y > 14 && y < 54;
                bool railTop = y > 20 && y < 24 && x > 6 && x < 122;
                bool railBottom = y > 10 && y < 14 && x > 6 && x < 122;
                bool upright = wall && x % 18 < 3 && y > 8 && y < 36;
                bool doorGap = x > 46 && x < 64 && y > 16 && y < 48;
                bool hangingShadow = wall && Hash01(x, y, 331) > 0.965f;
                Color pixel = Color.clear;
                if (wall && !doorGap)
                {
                    float n = Hash01(x, y, 332);
                    pixel = new Color(0.43f + n * 0.08f, 0.29f + n * 0.05f, 0.17f, 0.9f);
                }
                if (railTop || railBottom || upright)
                {
                    pixel = new Color(0.66f, 0.58f, 0.42f, 0.92f);
                }
                if (hangingShadow)
                {
                    pixel = Color.Lerp(pixel, new Color(0.04f, 0.06f, 0.05f, 0.92f), 0.55f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstVerandaBodySprite");
    }

    private static Sprite CreatePorchRoofSprite()
    {
        const int width = 128;
        const int height = 48;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstPorchRoofTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float leftSlope = 10f + x * 0.16f;
                float rightSlope = 30f - (x - 64f) * 0.05f;
                bool roof = y > leftSlope && y < rightSlope && x > 3 && x < 125;
                bool eave = x > 1 && x < 127 && y > 6 && y < 12;
                bool corrugation = roof && x % 9 < 2;
                Color pixel = Color.clear;
                if (roof || eave)
                {
                    float n = Hash01(x, y, 341);
                    pixel = new Color(0.45f + n * 0.08f, 0.14f + n * 0.04f, 0.08f, 0.96f);
                }
                if (corrugation)
                {
                    pixel = Color.Lerp(pixel, new Color(0.2f, 0.06f, 0.04f, 0.96f), 0.42f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstPorchRoofSprite");
    }

    private static Sprite CreateHouseGableRoofSprite()
    {
        const int width = 96;
        const int height = 56;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstHouseGableRoofTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float center = (width - 1) * 0.5f;
                float distance01 = Mathf.Abs(x - center) / center;
                float roofTop = 47f - distance01 * 28f;
                bool roof = y > 11 && y < roofTop && x > 4 && x < width - 5;
                bool eave = x > 2 && x < width - 3 && y > 8 && y < 14;
                bool ridge = Mathf.Abs(x - center) < 2.4f && y > 20 && y < 46;
                bool corrugation = roof && (x + y / 2) % 8 < 2;
                Color pixel = Color.clear;
                if (roof || eave)
                {
                    float n = Hash01(x, y, 361);
                    pixel = new Color(0.52f + n * 0.1f, 0.15f + n * 0.055f, 0.08f, 0.98f);
                }
                if (corrugation || ridge)
                {
                    pixel = Color.Lerp(pixel, new Color(0.22f, 0.065f, 0.035f, 0.98f), 0.42f);
                }
                if (roof && Hash01(x, y, 362) > 0.972f)
                {
                    pixel.a *= 0.28f;
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstHouseGableRoofSprite");
    }

    private static Sprite CreateLongEaveSprite()
    {
        const int width = 160;
        const int height = 36;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstLongEaveTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float slope = 5.5f + x * 0.018f;
                bool sheet = x > 4 && x < width - 5 && y > slope && y < 25f - x * 0.01f;
                bool darkLip = x > 2 && x < width - 3 && y > 5 && y < 9;
                bool corrugation = sheet && x % 10 < 2;
                bool tornEdge = sheet && y < 8 && Hash01(x, y, 371) > 0.94f;
                Color pixel = Color.clear;
                if (sheet || darkLip)
                {
                    float n = Hash01(x, y, 372);
                    pixel = new Color(0.47f + n * 0.08f, 0.14f + n * 0.04f, 0.075f, 0.96f);
                }
                if (corrugation || darkLip)
                {
                    pixel = Color.Lerp(pixel, new Color(0.18f, 0.055f, 0.035f, 0.96f), 0.42f);
                }
                if (tornEdge)
                {
                    pixel = Color.clear;
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstLongEaveSprite");
    }

    private static Sprite CreateUnderHouseShadowSprite()
    {
        const int width = 160;
        const int height = 48;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstUnderHouseShadowTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = Mathf.Abs(x - width * 0.5f) / (width * 0.5f);
                float dy = Mathf.Abs(y - height * 0.48f) / (height * 0.5f);
                float alpha = Mathf.Clamp01(1f - dx * 0.86f - dy * 0.78f);
                bool gap = x % 23 < 4 && y < 34;
                bool waterGlint = y < 9 && (x + y * 3) % 29 < 4;
                Color pixel = new Color(0.015f, 0.03f, 0.028f, alpha * 0.66f);
                if (gap)
                {
                    pixel.a *= 0.48f;
                }
                if (waterGlint)
                {
                    pixel = Color.Lerp(pixel, new Color(0.18f, 0.48f, 0.52f, 0.24f), 0.35f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstUnderHouseShadowSprite");
    }

    private static Sprite CreateDenseStiltPostSprite()
    {
        const int width = 24;
        const int height = 128;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstDenseStiltPostTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float center = width * 0.5f + Mathf.Sin(y * 0.08f) * 1.2f;
                bool post = Mathf.Abs(x - center) < 4.5f && y > 2 && y < height - 3;
                bool edge = post && Mathf.Abs(x - center) > 3.25f;
                bool barnacle = post && y < 30 && Hash01(x, y, 381) > 0.86f;
                Color pixel = Color.clear;
                if (post)
                {
                    float n = Hash01(x, y, 382);
                    pixel = new Color(0.33f + n * 0.1f, 0.19f + n * 0.06f, 0.09f, 0.88f);
                }
                if (edge)
                {
                    pixel = Color.Lerp(pixel, new Color(0.12f, 0.065f, 0.035f, 0.92f), 0.5f);
                }
                if (barnacle)
                {
                    pixel = new Color(0.72f, 0.73f, 0.58f, 0.78f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstDenseStiltPostSprite");
    }

    private static Sprite CreateStormSurgeWallSprite()
    {
        const int width = 192;
        const int height = 72;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstStormSurgeWallTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float wave = 36f + Mathf.Sin(x * 0.085f) * 7f + Mathf.Sin(x * 0.21f) * 3f;
                bool water = y < wave;
                bool foam = Mathf.Abs(y - wave) < 3.2f || (y > wave - 9f && Hash01(x, y, 391) > 0.88f);
                Color pixel = Color.clear;
                if (water)
                {
                    float n = Hash01(x, y, 392);
                    pixel = new Color(0.24f + n * 0.06f, 0.08f + n * 0.035f, 0.065f, 0.74f);
                }
                if (foam)
                {
                    pixel = Color.Lerp(pixel, new Color(0.82f, 0.9f, 0.78f, 0.74f), 0.62f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstStormSurgeWallSprite");
    }

    private static Sprite CreateBrokenHousePieceSprite()
    {
        const int width = 64;
        const int height = 28;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstBrokenHousePieceTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool plank = x > 3 && x < 60 && y > 7 && y < 21;
                bool bite = plank && ((x < 10 && Hash01(x, y, 401) > 0.42f) || (x > 51 && Hash01(x, y, 402) > 0.38f));
                bool grain = plank && ((x + y * 2) % 13 < 2 || Hash01(x, y, 403) > 0.96f);
                Color pixel = Color.clear;
                if (plank && !bite)
                {
                    float n = Hash01(x, y, 404);
                    pixel = new Color(0.45f + n * 0.12f, 0.22f + n * 0.05f, 0.12f, 0.88f);
                }
                if (grain)
                {
                    pixel = Color.Lerp(pixel, new Color(0.16f, 0.07f, 0.035f, 0.9f), 0.46f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstBrokenHousePieceSprite");
    }

    private static Sprite CreateSnappedStiltPostSprite()
    {
        const int width = 28;
        const int height = 96;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstSnappedStiltPostTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float center = width * 0.5f + Mathf.Sin(y * 0.09f) * 1.4f;
                bool post = Mathf.Abs(x - center) < 5f && y > 4 && y < 90;
                bool snappedTop = post && y > 74 && (Mathf.Abs(x - center) > 1.5f || Hash01(x, y, 411) > 0.58f);
                bool saltEdge = post && (Mathf.Abs(x - center) > 3.6f || Hash01(x, y, 412) > 0.985f);
                Color pixel = Color.clear;
                if (post && !snappedTop)
                {
                    float n = Hash01(x, y, 413);
                    pixel = new Color(0.3f + n * 0.08f, 0.16f + n * 0.05f, 0.08f, 0.88f);
                }
                if (saltEdge)
                {
                    pixel = Color.Lerp(pixel, new Color(0.72f, 0.72f, 0.56f, 0.7f), 0.28f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstSnappedStiltPostSprite");
    }

    private static Sprite CreateDepartureRouteWakeSprite()
    {
        const int width = 96;
        const int height = 28;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstDepartureRouteWakeTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float wave = 12f + Mathf.Sin(x * 0.18f) * 3.5f;
                bool foam = Mathf.Abs(y - wave) < 1.8f || Mathf.Abs(y - (wave - 5f)) < 1.1f;
                bool broken = foam && Hash01(x, y, 421) > 0.18f;
                Color pixel = Color.clear;
                if (foam && !broken)
                {
                    float fade = Mathf.Clamp01(1f - x / 112f);
                    pixel = new Color(0.72f, 0.98f, 0.9f, 0.34f * fade);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstDepartureRouteWakeSprite");
    }

    private static Sprite CreateDiagonalBraceSprite()
    {
        const int width = 24;
        const int height = 128;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstDiagonalBraceTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool plank = x > 7 && x < 16 && y > 3 && y < 125;
                bool edge = plank && (x == 8 || x == 15 || Hash01(x, y, 351) > 0.97f);
                Color pixel = Color.clear;
                if (plank)
                {
                    float n = Hash01(x, y, 352);
                    pixel = new Color(0.38f + n * 0.08f, 0.23f + n * 0.05f, 0.12f, 0.9f);
                }
                if (edge)
                {
                    pixel = Color.Lerp(pixel, new Color(0.08f, 0.05f, 0.03f, 0.92f), 0.45f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstDiagonalBraceSprite");
    }

    private static Sprite CreateLaundryClothSprite()
    {
        const int width = 42;
        const int height = 56;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstLaundryClothTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float bottom = 9f + Mathf.Sin(x * 0.35f) * 3f;
                bool cloth = x > 5 && x < 37 && y > bottom && y < 51;
                bool fold = cloth && (Mathf.Abs(x - 14f) < 1.4f || Mathf.Abs(x - 28f) < 1.2f);
                bool pin = (IsEllipse(x, y, 12f, 50f, 2f, 2f) || IsEllipse(x, y, 30f, 50f, 2f, 2f));
                Color pixel = Color.clear;
                if (cloth)
                {
                    float n = Hash01(x, y, 361);
                    pixel = new Color(0.72f + n * 0.05f, 0.72f + n * 0.04f, 0.64f + n * 0.05f, 0.82f);
                }
                if (fold)
                {
                    pixel = Color.Lerp(pixel, new Color(0.32f, 0.4f, 0.42f, 0.7f), 0.35f);
                }
                if (pin)
                {
                    pixel = new Color(0.12f, 0.1f, 0.06f, 0.95f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstLaundryClothSprite");
    }

    private static Sprite CreateLandingWalkwaySprite()
    {
        const int width = 128;
        const int height = 32;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstBoatLandingWalkwayTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool plank = x > 3 && x < 125 && y > 9 && y < 22;
                bool gaps = plank && x % 18 < 2;
                bool wetEdge = plank && (y < 12 || y > 19);
                Color pixel = Color.clear;
                if (plank)
                {
                    float n = Hash01(x, y, 371);
                    pixel = new Color(0.52f + n * 0.08f, 0.41f + n * 0.04f, 0.26f, 0.86f);
                }
                if (gaps || wetEdge)
                {
                    pixel = Color.Lerp(pixel, new Color(0.06f, 0.09f, 0.08f, 0.9f), 0.42f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstBoatLandingWalkwaySprite");
    }

    private static Sprite CreateBoatWakeSprite()
    {
        const int width = 128;
        const int height = 32;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstBoatWakeTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float center = 15f + Mathf.Sin(x * 0.12f) * 2.3f;
                bool firstLine = Mathf.Abs(y - center) < 1.4f && x > 8 && x < 116;
                bool secondLine = Mathf.Abs(y - (center - 6f + Mathf.Sin(x * 0.19f))) < 1.1f && x > 22 && x < 104;
                bool brokenFoam = Hash01(x, y, 381) > 0.965f && y > 8 && y < 22;
                Color pixel = Color.clear;
                if (firstLine || secondLine || brokenFoam)
                {
                    float alpha = firstLine ? 0.58f : secondLine ? 0.34f : 0.22f;
                    pixel = new Color(0.74f, 0.93f, 0.88f, alpha);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstBoatWakeSprite");
    }

    private static Sprite CreateIncomingTideCarryRippleSprite()
    {
        const int width = 96;
        const int height = 26;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstIncomingTideCarryRippleTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float wave = 13f + Mathf.Sin(x * 0.12f) * 2.2f + Mathf.Sin(x * 0.31f) * 0.8f;
                bool foam = Mathf.Abs(y - wave) < 1.5f && x > 4 && x < 90;
                bool brokenFoam = foam && Hash01(x, y, 641) > 0.18f;
                bool draggedThread = Mathf.Abs(y - (wave - 5f)) < 0.8f && x > 14 && x < 74 && Hash01(x, y, 642) > 0.42f;
                Color pixel = Color.clear;
                if (brokenFoam)
                {
                    pixel = new Color(0.66f, 0.92f, 0.86f, 0.56f);
                }
                if (draggedThread)
                {
                    pixel = new Color(0.42f, 0.75f, 0.72f, 0.36f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstIncomingTideCarryRippleSprite");
    }

    private static Sprite CreateStoryEllipseMaskSprite()
    {
        const int width = 72;
        const int height = 72;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstStoryEllipseMaskTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx = (x - (width - 1f) * 0.5f) / (width * 0.46f);
                float ny = (y - (height - 1f) * 0.5f) / (height * 0.46f);
                float edge = Mathf.Sqrt(nx * nx + ny * ny);
                float alpha = 1f - Mathf.SmoothStep(0.9f, 1f, edge);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstStoryEllipseMaskSprite");
    }

    private static Sprite CreateStoryRaggedMaskSprite()
    {
        const int width = 96;
        const int height = 64;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstStoryRaggedMaskTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx = Mathf.Abs((x - width * 0.5f) / (width * 0.5f));
                float ny = Mathf.Abs((y - height * 0.5f) / (height * 0.5f));
                float raggedX = 0.9f + Mathf.Sin(y * 0.31f) * 0.045f + Mathf.Sin(y * 0.77f) * 0.025f;
                float raggedY = 0.86f + Mathf.Sin(x * 0.23f) * 0.055f + Mathf.Sin(x * 0.61f) * 0.025f;
                bool inside = nx < raggedX && ny < raggedY;
                texture.SetPixel(x, y, inside ? Color.white : Color.clear);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstStoryRaggedMaskSprite");
    }

    private static Sprite CreateInteriorThreeLevelCutawayMaskSprite()
    {
        const int width = 160;
        const int height = 128;
        const float roofStart01 = 0.48f;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstInteriorThreeLevelCutawayMaskTexture");
        for (int y = 0; y < height; y++)
        {
            float y01 = y / (height - 1f);
            float roof01 = Mathf.InverseLerp(roofStart01, 1f, y01);
            // The living floor fills the full wall bay. Above it the cut narrows to
            // the broad ridge of the real roof, leaving both exterior roof shoulders
            // intact instead of cutting a rectangular hole through them.
            float halfWidth01 = y01 <= roofStart01
                ? 0.49f
                : Mathf.Lerp(0.49f, 0.35f, roof01);
            for (int x = 0; x < width; x++)
            {
                float x01 = x / (width - 1f);
                float distanceFromCenter = Mathf.Abs(x01 - 0.5f);
                float edgeNoise = (Mathf.Sin(y * 0.43f) + Mathf.Sin(y * 0.17f)) * 0.003f;
                bool inside = distanceFromCenter <= halfWidth01 + edgeNoise;
                texture.SetPixel(x, y, inside ? Color.white : Color.clear);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstInteriorThreeLevelCutawayMaskSprite");
    }

    private static Sprite CreatePlayerHaloSprite()
    {
        const int width = 80;
        const int height = 128;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstPlayerHaloTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = (x - width * 0.5f) / (width * 0.38f);
                float dy = (y - height * 0.48f) / (height * 0.43f);
                float radius = Mathf.Sqrt(dx * dx + dy * dy);
                float rim = 1f - Mathf.Abs(radius - 0.78f) * 7.5f;
                float fill = Mathf.Clamp01(1f - radius) * 0.18f;
                float alpha = Mathf.Clamp01(Mathf.Max(rim * 0.42f, fill));
                Color pixel = alpha > 0.01f ? new Color(1f, 0.78f, 0.32f, alpha) : Color.clear;
                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstPlayerHaloSprite");
    }

    private static Sprite CreateSwimWakeSprite()
    {
        const int width = 144;
        const int height = 48;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstSwimWakeTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float t = x / (width - 1f);
                float fade = Mathf.SmoothStep(0f, 1f, t) * Mathf.SmoothStep(1f, 0.72f, t);
                float separation = (1f - t) * 10f;
                float ripple = Mathf.Sin(t * Mathf.PI * 4f) * (1f - t) * 1.4f;
                float upperY = height * 0.5f + separation + ripple;
                float lowerY = height * 0.5f - separation - ripple;
                float lineDistance = Mathf.Min(Mathf.Abs(y - upperY), Mathf.Abs(y - lowerY));
                float line = Mathf.Clamp01(1f - lineDistance / 1.7f) * fade;
                float disturbedWater = Mathf.Clamp01(1f - Mathf.Abs(y - height * 0.5f) / 7f) * fade * 0.08f;
                float alpha = Mathf.Max(line * 0.78f, disturbedWater);
                Color pixel = alpha > 0.006f
                    ? new Color(0.64f, 0.88f, 0.84f, alpha)
                    : Color.clear;
                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstSwimWakeSprite");
    }


    private static Sprite CreateSideViewVortexDepressionSprite()
    {
        const int width = 384;
        const int height = 144;
        Texture2D texture = NewTexture(
            width,
            height,
            "GeneratedStiltFirstSideViewVortexDepressionTexture");
        for (int y = 0; y < height; y++)
        {
            float y01 = y / (float)(height - 1);
            for (int x = 0; x < width; x++)
            {
                float nx = (x / (float)(width - 1) - 0.5f) * 2f;
                float centreDepth = Mathf.Exp(-nx * nx * 5.8f);
                float funnelSurface01 = 0.82f - centreDepth * 0.32f;
                float edgeFade = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(1f, 0.68f, Mathf.Abs(nx)));
                float belowFunnel01 = Mathf.InverseLerp(funnelSurface01, 0.06f, y01);
                float verticalFade = Mathf.SmoothStep(0f, 1f, belowFunnel01) *
                    Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.2f, y01));
                float throat = Mathf.Exp(-nx * nx * 11f) *
                    Mathf.SmoothStep(0f, 1f, belowFunnel01);
                float alpha = edgeFade * verticalFade * (0.12f + centreDepth * 0.3f + throat * 0.22f);
                if (y01 > funnelSurface01 || alpha <= 0.002f)
                {
                    texture.SetPixel(x, y, Color.clear);
                    continue;
                }

                Color pixel = Color.Lerp(
                    new Color(0.2f, 0.42f, 0.44f, alpha),
                    new Color(0.025f, 0.12f, 0.16f, alpha),
                    Mathf.Clamp01(centreDepth * 0.72f + belowFunnel01 * 0.38f));
                pixel.a = alpha;
                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(
            texture,
            width,
            height,
            "GeneratedStiltFirstSideViewVortexDepressionSprite");
    }

    private static Sprite CreateSideViewVortexUndertowSprite()
    {
        const int width = 384;
        const int height = 128;
        Texture2D texture = NewTexture(
            width,
            height,
            "GeneratedStiltFirstSideViewVortexUndertowTexture");
        for (int y = 0; y < height; y++)
        {
            float y01 = y / (float)(height - 1);
            for (int x = 0; x < width; x++)
            {
                float nx = (x / (float)(width - 1) - 0.5f) * 2f;
                float convergence = 1f - Mathf.Abs(nx);
                float sideSign = Mathf.Sign(nx);
                float bend = convergence * convergence;
                float lineA = 0.73f - bend * 0.31f + sideSign * convergence * 0.025f;
                float lineB = 0.49f - bend * 0.2f - sideSign * convergence * 0.02f;
                float distance = Mathf.Min(
                    Mathf.Abs(y01 - lineA),
                    Mathf.Abs(y01 - lineB));
                float lineAlpha = Mathf.Clamp01(1f - distance / 0.012f);
                float edgeFade = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(1f, 0.62f, Mathf.Abs(nx)));
                float centreFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.28f, Mathf.Abs(nx)));
                float alpha = lineAlpha * edgeFade * centreFade * 0.48f;
                texture.SetPixel(
                    x,
                    y,
                    alpha > 0.001f
                        ? new Color(0.42f, 0.73f, 0.72f, alpha)
                        : Color.clear);
            }
        }

        return FinishSprite(
            texture,
            width,
            height,
            "GeneratedStiltFirstSideViewVortexUndertowSprite");
    }

    private static Sprite CreateVortexSprite()
    {
        const int size = 160;
        Texture2D texture = NewTexture(size, size, "GeneratedStiltFirstRouteVortexTexture");
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - size * 0.5f) / (size * 0.5f);
                float dy = (y - size * 0.5f) / (size * 0.5f);
                float radius = Mathf.Sqrt(dx * dx + dy * dy);
                if (radius > 0.96f)
                {
                    texture.SetPixel(x, y, Color.clear);
                    continue;
                }

                float angle = Mathf.Atan2(dy, dx);
                float outerFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.96f, 0.72f, radius));
                float innerFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.22f, radius));
                float broadSpiral = Mathf.Pow(Mathf.Clamp01(0.5f + 0.5f * Mathf.Sin(angle * 2f - radius * 12f)), 4f);
                float ringA = Mathf.Clamp01(1f - Mathf.Abs(radius - 0.72f) / 0.09f);
                float ringB = Mathf.Clamp01(1f - Mathf.Abs(radius - 0.43f) / 0.075f);
                float eye = Mathf.Clamp01(1f - radius / 0.2f);
                float alpha = Mathf.Clamp01((broadSpiral * 0.48f + ringA * 0.56f + ringB * 0.43f + eye * 0.5f) * outerFade);
                alpha *= Mathf.Lerp(0.72f, 1f, innerFade);

                Color flowColor = Color.Lerp(
                    new Color(0.055f, 0.14f, 0.16f, alpha),
                    new Color(0.38f, 0.68f, 0.67f, alpha),
                    Mathf.Clamp01(ringA * 0.72f + broadSpiral * 0.35f));
                Color pixel = alpha > 0.008f ? flowColor : Color.clear;
                pixel.a = alpha;
                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, size, size, "GeneratedStiltFirstRouteVortexSprite");
    }

    private static Sprite CreateRepairLashingSprite()
    {
        const int width = 52;
        const int height = 40;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstRepairRopeLashingTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool patchBoard = x > 6 && x < 46 && y > 9 && y < 30;
                bool ropeWrap = (Mathf.Abs(y - 15f) < 1.4f || Mathf.Abs(y - 23f) < 1.4f || IsNearLine(x, y, 10f, 29f, 43f, 9f, 1.2f));
                bool crack = patchBoard && ((x + y) % 17 < 2 || Hash01(x, y, 221) > 0.97f);
                Color pixel = Color.clear;
                if (patchBoard)
                {
                    float n = Hash01(x, y, 222);
                    pixel = new Color(0.55f + n * 0.08f, 0.38f + n * 0.04f, 0.2f, 0.95f);
                }
                if (crack)
                {
                    pixel = Color.Lerp(pixel, new Color(0.12f, 0.07f, 0.04f, 0.96f), 0.5f);
                }
                if (ropeWrap)
                {
                    pixel = new Color(0.78f, 0.66f, 0.42f, 0.96f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstRepairRopeLashingSprite");
    }

    private static Sprite CreateWaterWrackSprite()
    {
        const int width = 58;
        const int height = 22;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstWaterWrackTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool slat = x > 5 && x < 49 && y > 8 && y < 14 && Mathf.Abs(y - (10f + Mathf.Sin(x * 0.18f) * 1.4f)) < 3.2f;
                bool rope = IsNearLine(x, y, 7f, 15f, 52f, 6f, 1.2f);
                bool tornPaper = x > 30 && x < 53 && y > 11 && y < 19 && Mathf.Abs(x - 42f) + Mathf.Abs(y - 15f) * 0.8f < 13f;
                bool bite = Hash01(x, y, 271) > 0.962f;
                Color pixel = Color.clear;
                if (slat && !bite)
                {
                    float n = Hash01(x, y, 272);
                    pixel = new Color(0.5f + n * 0.08f, 0.32f + n * 0.05f, 0.17f, 0.82f);
                }
                if (rope)
                {
                    pixel = new Color(0.72f, 0.64f, 0.42f, 0.72f);
                }
                if (tornPaper && !bite)
                {
                    pixel = new Color(0.72f, 0.72f, 0.56f, 0.58f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstWaterWrackSprite");
    }

    private static Sprite CreateLighthouseChartClueSprite()
    {
        const int width = 64;
        const int height = 48;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstLighthouseChartClueTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool paper = x > 6 && x < 58 && y > 5 && y < 42 && Mathf.Abs(x - 32f) + Mathf.Abs(y - 24f) * 0.18f < 31f;
                bool tornCorner = paper && ((x < 13 && y > 33 && Hash01(x, y, 301) > 0.35f) || (x > 50 && y < 12 && Hash01(x, y, 302) > 0.45f));
                bool routeLine = IsNearLine(x, y, 13f, 14f, 50f, 34f, 1.4f) || IsNearLine(x, y, 19f, 32f, 50f, 34f, 1.1f);
                bool lighthouseMark = IsEllipse(x, y, 50f, 34f, 3f, 5f) || (Mathf.Abs(x - 50f) < 1.2f && y > 25 && y < 38);
                bool saltSpot = paper && Hash01(x, y, 303) > 0.95f;
                Color pixel = Color.clear;
                if (paper && !tornCorner)
                {
                    pixel = new Color(0.74f, 0.68f, 0.48f, 0.82f);
                }
                if (saltSpot)
                {
                    pixel = new Color(0.86f, 0.84f, 0.66f, 0.75f);
                }
                if (routeLine)
                {
                    pixel = new Color(0.18f, 0.42f, 0.42f, 0.82f);
                }
                if (lighthouseMark)
                {
                    pixel = new Color(0.95f, 0.68f, 0.22f, 0.92f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstLighthouseChartClueSprite");
    }

    private static Sprite CreateStormWarningPennantSprite()
    {
        const int width = 70;
        const int height = 40;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstStormWarningPennantTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool cord = Mathf.Abs(y - 22f) < 1.3f && x > 3 && x < 64;
                bool flag = x > 8 && x < 48 && y > 10 && y < 31 && x < 54 - Mathf.Abs(y - 20f) * 0.75f;
                bool fray = flag && (x > 42 || Hash01(x, y, 311) > 0.965f);
                bool seam = flag && (Mathf.Abs(x - 12f) < 1.3f || Mathf.Abs(y - 21f) < 1.1f);
                Color pixel = Color.clear;
                if (cord)
                {
                    pixel = new Color(0.66f, 0.58f, 0.38f, 0.78f);
                }
                if (flag && !fray)
                {
                    float n = Hash01(x, y, 312);
                    pixel = new Color(0.62f + n * 0.06f, 0.13f + n * 0.035f, 0.075f, 0.9f);
                }
                if (seam)
                {
                    pixel = Color.Lerp(pixel, new Color(0.9f, 0.68f, 0.36f, 0.9f), 0.38f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstStormWarningPennantSprite");
    }

    private static Sprite CreatePrepRopeCoilSprite()
    {
        const int width = 64;
        const int height = 44;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstPrepRopeCoilTexture");
        Vector2 center = new Vector2(width * 0.5f, height * 0.54f);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2 point = new Vector2(x, y);
                float distance = Vector2.Distance(point, center);
                float angle = Mathf.Atan2(y - center.y, x - center.x);
                float ring = Mathf.Abs(Mathf.Repeat(distance + angle * 2.5f, 9f) - 4.5f);
                bool coil = distance < 24f && distance > 5f && ring < 1.35f;
                bool tail = IsNearLine(x, y, 40f, 18f, 60f, 10f, 1.8f) || IsNearLine(x, y, 11f, 15f, 3f, 9f, 1.5f);
                Color pixel = Color.clear;
                if (coil || tail)
                {
                    float noise = Hash01(x, y, 620) * 0.08f;
                    pixel = new Color(0.84f + noise, 0.62f + noise, 0.35f, 0.94f);
                    if (Hash01(x, y, 621) > 0.82f)
                    {
                        pixel = Color.Lerp(pixel, new Color(0.32f, 0.2f, 0.12f, 0.96f), 0.34f);
                    }
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstPrepRopeCoilSprite");
    }

    private static Sprite CreatePrepBucketSprite()
    {
        const int width = 52;
        const int height = 58;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstPrepBucketTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float t = y / (height - 1f);
                float halfWidth = Mathf.Lerp(13f, 19f, t);
                float center = width * 0.5f;
                bool body = y > 8 && y < 46 && Mathf.Abs(x - center) < halfWidth;
                bool rim = y >= 43 && y < 50 && Mathf.Abs(x - center) < 21f;
                bool handle = IsNearLine(x, y, 12f, 42f, 26f, 54f, 1.6f) || IsNearLine(x, y, 26f, 54f, 40f, 42f, 1.6f);
                bool bottom = y > 6 && y < 12 && Mathf.Abs(x - center) < 14f;
                Color pixel = Color.clear;
                if (body || rim || bottom)
                {
                    float wet = Mathf.InverseLerp(8f, 46f, y);
                    pixel = Color.Lerp(new Color(0.18f, 0.42f, 0.43f, 0.95f), new Color(0.58f, 0.82f, 0.78f, 0.96f), wet);
                    if (Mathf.Abs(x - center) > halfWidth - 2f || rim)
                    {
                        pixel = Color.Lerp(pixel, new Color(0.82f, 0.9f, 0.82f, 0.98f), 0.35f);
                    }
                }
                if (handle)
                {
                    pixel = new Color(0.82f, 0.72f, 0.52f, 0.92f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstPrepBucketSprite");
    }

    private static Sprite CreatePrepStakeSprite()
    {
        const int width = 34;
        const int height = 88;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstPrepStakeTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float center = width * 0.5f + Mathf.Sin(y * 0.09f) * 1.3f;
                float halfWidth = y > 72 ? Mathf.Lerp(1.5f, 7f, Mathf.InverseLerp(86f, 72f, y)) : 6.5f;
                bool body = y > 4 && y < 85 && Mathf.Abs(x - center) < halfWidth;
                bool lash = (y > 32 && y < 39 || y > 50 && y < 56) && Mathf.Abs(x - center) < halfWidth + 2.5f;
                Color pixel = Color.clear;
                if (body)
                {
                    float grain = Hash01(x, y, 710) * 0.12f;
                    pixel = new Color(0.56f + grain, 0.34f + grain * 0.5f, 0.18f, 0.96f);
                    if (Mathf.Abs(x - center) > halfWidth - 1.4f)
                    {
                        pixel = Color.Lerp(pixel, new Color(0.22f, 0.13f, 0.08f, 1f), 0.35f);
                    }
                }
                if (lash)
                {
                    pixel = Color.Lerp(pixel, new Color(0.86f, 0.68f, 0.42f, 0.95f), 0.72f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstPrepStakeSprite");
    }

    private static Sprite CreateCloudMassSprite()
    {
        const int width = 96;
        const int height = 34;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstCloudMassTexture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool body =
                    IsEllipse(x, y, 22f, 15f, 18f, 8f) ||
                    IsEllipse(x, y, 42f, 18f, 24f, 10f) ||
                    IsEllipse(x, y, 64f, 15f, 22f, 8f) ||
                    IsEllipse(x, y, 78f, 18f, 12f, 6f);
                bool raggedEdge = body && Hash01(x, y, 381) > 0.93f;
                Color pixel = Color.clear;
                if (body && !raggedEdge)
                {
                    float topLight = Mathf.InverseLerp(0f, height, y);
                    float noise = Hash01(x, y, 382) * 0.08f;
                    pixel = new Color(0.86f + noise, 0.9f + noise * 0.4f, 0.84f, 0.72f + topLight * 0.16f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstCloudMassSprite");
    }

    private static Sprite CreateNoiseBandSprite(int width, int height, string spriteName, float density, float baseAlpha)
    {
        Texture2D texture = NewTexture(width, height, spriteName + "Texture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float fade = Mathf.InverseLerp(0f, height, y);
                float n = Hash01(x, y, 101);
                float alpha = baseAlpha + (n < density ? 0.12f : 0f) + fade * 0.04f;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
            }
        }

        return FinishSprite(texture, width, height, spriteName);
    }

    private static Sprite CreateSolidSprite(int width, int height, string spriteName)
    {
        Texture2D texture = NewTexture(width, height, spriteName + "Texture");
        Color color = Color.white;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                texture.SetPixel(x, y, color);
            }
        }

        return FinishSprite(texture, width, height, spriteName);
    }

    private static Sprite CreateSubsurfaceFadeSprite()
    {
        const int width = 8;
        const int height = 128;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstSubsurfaceFadeTexture");
        for (int y = 0; y < height; y++)
        {
            // Texture Y grows from the deep bottom to the shallow top. Keep most of
            // the body uniformly attenuating, then fade to transparent only in the
            // upper 12 percent. At shelter scale that transition finishes inside the
            // last half-metre of painted water, before any repair-owner crop can show.
            float bottomToTop01 = y / (height - 1f);
            float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.88f, 1f, bottomToTop01));
            for (int x = 0; x < width; x++)
            {
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstSubsurfaceFadeSprite");
    }

    private static Sprite CreateDepthBlendBandSprite()
    {
        const int width = 8;
        const int height = 64;
        Texture2D texture = NewTexture(width, height, "GeneratedStiltFirstDepthBlendBandTexture");
        for (int y = 0; y < height; y++)
        {
            float y01 = y / (height - 1f);
            float centre01 = 1f - Mathf.Abs(y01 - 0.5f) * 2f;
            float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(centre01));
            for (int x = 0; x < width; x++)
            {
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        return FinishSprite(texture, width, height, "GeneratedStiltFirstDepthBlendBandSprite");
    }

    private static Sprite CreateWaveSprite(int width, int height, string spriteName, bool fillBelow)
    {
        Texture2D texture = NewTexture(width, height, spriteName + "Texture");
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float wave = height * 0.68f + Mathf.Sin(x * 0.12f) * height * 0.08f + Mathf.Sin(x * 0.31f) * height * 0.03f;
                bool body = fillBelow ? y < wave : Mathf.Abs(y - wave) < 2.2f;
                bool foam = Mathf.Abs(y - wave) < 1.4f || (Hash01(x, y, 111) > 0.985f && y > wave - 8f && y < wave + 4f);
                Color pixel = Color.clear;
                if (body)
                {
                    pixel = new Color(0.42f, 0.78f, 0.86f, fillBelow ? 0.86f : 0.58f);
                }
                if (foam)
                {
                    pixel = Color.Lerp(pixel, Color.white, 0.75f);
                    pixel.a = Mathf.Max(pixel.a, 0.72f);
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        return FinishSprite(texture, width, height, spriteName);
    }

    private static Texture2D NewTexture(int width, int height, string textureName)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.name = textureName;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        return texture;
    }

    private static Sprite FinishSprite(Texture2D texture, int width, int height, string spriteName)
    {
        texture.Apply();
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), Mathf.Max(width, height));
        sprite.name = spriteName;
        return sprite;
    }

    private static bool IsNearLine(float x, float y, float ax, float ay, float bx, float by, float radius)
    {
        Vector2 point = new Vector2(x, y);
        Vector2 a = new Vector2(ax, ay);
        Vector2 b = new Vector2(bx, by);
        Vector2 ab = b - a;
        float t = Vector2.Dot(point - a, ab) / Mathf.Max(0.001f, Vector2.Dot(ab, ab));
        Vector2 closest = a + ab * Mathf.Clamp01(t);
        return Vector2.Distance(point, closest) <= radius;
    }

    private static bool IsEllipse(float x, float y, float cx, float cy, float rx, float ry)
    {
        float dx = (x - cx) / Mathf.Max(0.001f, rx);
        float dy = (y - cy) / Mathf.Max(0.001f, ry);
        return dx * dx + dy * dy <= 1f;
    }

    private static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            uint hash = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            hash = (hash ^ (hash >> 13)) * 1274126177u;
            return (hash & 0x00FFFFFFu) / 16777215f;
        }
    }
}
