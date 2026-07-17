# Tide 代码阅读地图

## 入口

1. `Assets/Scenes/Tide_StiltHouse_FirstSlice.unity`
2. `Assets/Scripts/StiltHouse/TideStiltHouseFirstSliceController.cs`
3. `Assets/Scripts/StiltHouse/TideStiltHouseFirstSliceController.EditorDiagnostics.cs`（只在追预览姿态或自动 Scene 探针时读；全部 `RunEditor*`/`GetEditor*` 入口都在这个 `UNITY_EDITOR` partial，玩家运行主文件不得再承载探针）
4. `Assets/Scripts/StiltHouse/TideAuthoritativeOceanModel.cs`
5. `Assets/Scripts/StiltHouse/TideOceanFieldModel.cs`、`TideWaveEventFieldModel.cs`
6. 本页下面列出的独立纯模型

`TideStiltHouseFirstSliceController` 仍然过大，但约一万行编辑器预览/探针已隔离到 `UNITY_EDITOR` partial，不进入玩家构建。阅读运行逻辑时不要打开诊断 partial，也不要从第一行顺序看到底；先按“输入 -> 状态 -> 表现”追一条完整链。

## 2026-07-16 新链路

### 岩礁岛与淡水

- 表现/实物所有权：`TideBarrenIslandController`
- 船骸连续拆卸：`TideWreckDismantleModel`；木板、帆布和铆接板使用不同现实秒工时，松手保留原物进度，脚下水深超过 `0.42m` 或权威海况的局部浪载过大时暂停。`TideBarrenIslandController` 只插值同一原件的位置/转角，完成帧才切换到手持 owner
- 流量规则：`TideRainCisternModel`
- `F` 交互优先级：`TideIslandInteractionModel`；携带原物时只允许在明确施工位暂存，不能同时饮水或触发泊船绳
- 编排入口：`TickBarrenIslandNaturalState`、`TryHandleBarrenIslandInteraction`
- 核心单位：雨 `mm/h`、屋顶 `m²`、水量 `L`、现实秒
- 水量唯一 owner：`TideRainCisternModel` 负责盐度、蒸发/渗漏和容器转移；`TideBarrenIslandController` 拥有裂池，并把当前水面、历史最高盐线和裂口外漏投影成三个独立 renderer。补片只改变同一裂缝状态，不瞬移池水；主控制器只拥有从裂池实际转出的暴潮应急罐。睡眠先用罐水再用池水，冲失时不能二次扣池水
- 首日契约：`RunEditorFirstDayAutonomyProbe` 推进真实世界时钟，验证玩家无需先检查残骸即可拆船、布网或回屋；`RunEditorWreckDismantleTideWindowProbe` 验证单按不瞬取、松手保留、进水/破浪停工和完成时唯一 owner；`RunEditorArrivalSalvagePayoffProbe` 验证木板/帆布/铆板投住所或船的六条路线都可由零库存立即开工，并完整走通铆板、裂池和补片 owner；左岛存在时旧 `arrivalWreckX` 只作镜头参考，不再拥有交互

### 泊位绳

- 纯规则：`TideMooringRopeModel`
- 运行编排/表现：`TideMooringRopeController`；独立拥有输入占用、当前绳状态、收绳意图、断绳/靠稳一次性结果和贝塞尔绳形
- 主控制器边界：`HandleMooringRopeInput` 只转发按键并消费交互结果，`TickMooredBoatCurrent` 只传统一潮流/风场并消费环境结果，`UpdateMooringRopeVisuals` 只传人物手、码头与船艉锚点
- 状态：Loose -> Swinging -> Attached/Reeling -> Secured；张力过载回 Loose

### 借潮牵引重物

- 纯规则：`TideHeavyWreckTidalLiftModel`
- 拆件 owner 账本：`TideHeavyWreckPieceOwnershipModel`
- 场景 owner：`TideHeavyWreckSalvageController`
- 资源索引：`TideV85HeavyWreckCatalog`，首轮只引用外露龙骨肋的完整态、显缝层和三件拆解 owner
- 编排入口：`TryHandleBarrenIslandInteraction`、`TickBarrenIslandNaturalState`
- 状态：低潮双系 -> 涨潮浮起 -> 平流收绳 -> 架边待退潮 -> 落底显缝 -> 可见拆解 -> 逐根拖行 -> 施工位 -> 最终结构
- 关键约束：小吊机不能整块起吊；强流不是更快，而是更高张力；长肋贴地拖行、不能上梯；屋边只能成为斜撑、船边只能成为船体肋骨，最终固定前不得生成库存数字

### 短航

- 权威海况入口：`TideAuthoritativeOceanModel`；把 `TideOceanFieldModel` 的连续浪谱与 `TideWaveEventFieldModel` 的局部浪组合成为同一个 `TideOceanSample`。住所、船、人物、漂物、重残骸和暴潮破口不得直接绕过它读取基础海况
- 局部浪组：由世界 cell、真实经过秒、风和暴潮压力唯一确定，周期约 `8.2-14.8s`；V43 可见浪脊与同位置的水面抬升、坡度、水平推力和扰动共用事件身份。它不读取压缩昼夜，也不把自身叠加后的扰动再反馈到事件生成；平潮过零连续减速而非瞬间翻向
- 船体动力与系泊：均把权威海况中的局部水平速度叠到天文潮流；暴潮破口也读取相同事件时钟和有符号风。9 个浪脊槽只复用既有 V43 帧，用于覆盖宽屏边缘仍可能影响物理的邻格浪
- 船体动力：`TideSailboatDynamicsModel`
- 航行运动状态：主控制器中的单一 `TideSailboatDynamicsState` 同时拥有水平速度、浮沉、俯仰、帆高、压舱和舱水；旧速度/水位/帆高镜像字段已退役，碰礁、舀水、打捞和表现都通过同一状态读写
- 浅礁净空：`TideSailingReefModel`；固定礁顶、瞬时物理水位、舱水、拖载和高速浅水下沉共同决定吃水、搁浅与撞击
- 浅礁运行时：`TideSailingReefController`；独立拥有真实越过状态、撞击冷却、连续位移约束和岩脊/碎浪表现。主控制器注入唯一 `TideOceanSample` 与吃水样本，并只结算返回的船体后果。
- 漂木运行时：`TideSailingSalvageController`；独立拥有自由漂移、抛钩、张力、过载脱钩、收绳和拖带状态。主控制器注入唯一 `TideOceanSample`、风和潮流，保留物资 owner，并把一次性结果结算回世界实物。
- 输入：`HandleSailingInput`
- 积分：`AdvanceSailingSteering`
- 表现：`UpdateSailingSceneVisuals`、`UpdateSailingReefVisuals`
- A/D 是有限加速度，W/S 是帆高，Q/E 是前后压舱；风和潮流是两个独立有符号输入。
- 短航平均水位由 `GetSailingMeanWaterY` 把权威天文水位相对固定礁顶按 `1:1m` 映射；露礁、礁顶碎浪、船底净空和位移碰撞读取同一个样本，浅礁不再是按 `F` 完成的任务点。
- 固定浅礁位于首轮住所与漂木之间。空船、进水拖载和航速改变同一礁顶上的船底净空，因此首轮往返必须选择潮窗并在边缘水深减速，而不是靠剧情锁或永久空气墙控制通行。

### 潮汐观察

- 网口相遇：`TideNetEncounterModel`；漂物到达前的湿网只累计水阻，只有实物水路线段进入网口且吃水带与网面重叠才累计缠挂。浅/深网、漫顶漏过、低帧率跨窗和擦网清零共用同一模型；可见导流索可以把指定盐木压入网面，自由漂物没有隐藏下压力。
- 预报区间：`TideNetForecastModel.HighWaterBand`；粗观潮保留 `±0.22m` 不确定性，修复海图潮尺后缩窄到 `±0.08m`，但不选择网深。
- 网深预报：`GetPredictedNetEncounterSeconds` 前向推进下一天文潮次的同一 `TideDriftBatch`、实际潮流和网面相遇，再由 `TideNetForecastModel.NetChoice` 整理有效接触与网压；不能恢复成整潮泡水秒数或统一深网奖励。
- 观测生命周期：`TideForecastSnapshotModel`；使用连续天文潮次冻结观测当刻的上下界。后续天气只改变真实水位，不反向改写旧绳结；目标高潮一过，快照自然失效。这里不能使用睡眠或故事轮次 `tideRound`。
- 世界表现：`TideForecastTideNotchController`；把区间上下界绑定成正式主桩上的两道无碰撞麻绳结，排序在房屋前、前景水后。上一潮盐痕、下一潮预测结和实际水线都使用同一世界 Y。
- 正式 Scene 门：`RunEditorTideForecastAutonomyProbe`；验证粗/修区间宽度、主桩锚点、遮挡顺序、快照不随时钟漂移、过高潮隐藏和连续网深自主权。

### 暴潮抢救

- 规则：`TideStormRescueModel`
- 运行时：`TideStormRescueController`；独立拥有四件实物的搁架失效、当前拉绳目标、吊升、冲失与退水整理状态。主控制器注入唯一房内水深/流速，只消费收妥/冲失事件并结算水罐升数和库存 owner。
- 输入：`HandleStormRescueInteraction`
- 推进：`TickStormRescue`
- 表现：`UpdateStormRescueVisuals`
- 物件由浮力、局部水深、潮流和系固决定冲失，剧情不写死次序；第一次抓住漂物后，交互点固定在原开间吊点，`SecuringProgress01` 同时驱动物件和吊绳连续上升
- 搁架失效：`TideStormRescueModel.ShouldReleaseCargo` 读取积水深度和破口流速；浅水警戒期物件仍在原搁架，达到实际冲击条件才同时进入水路。室内流速由 `EvaluateStormRescueLocalCurrentSpeed` 合成天文潮流与 `TideOceanFieldModel` 的连续涌浪轨道速度，高潮平流不再等于室内静水
- 吊升差异：`TideStormRescueModel.Advance` 按水罐、船材、柴束和湿海图的重量/系固难度计算工时；顺序改变结果，但不写死固定损失名单
- 非文字反馈：搁架首次失效由 `TideAudioController.PlayStormShelfBreakCueInScene` 合成短木裂/落水声，并复用房体既有受浪包络；物件位置仍完全来自权威状态，不加独立视觉偏移。吊升完成复用拾取确认声
- `Present` 区分“没有这件实物”和“实物被冲失”；`PrepareStormRescueManifest` 只把真实 `4L` 水、`2` 木料、独立干柴和已有海图转成暴潮预留。抢救成功由 `RestoreSecuredStormRescueCargo` 归回可用储物，失败不再对普通库存二次扣账
- 睡眠边界：`WouldSleepIntervalFloodLooseStormCargo` 用权威潮位、风暴压力和可能的结构破口采样到黎明前的水深；未处理物资仍在水路或即将进入水路时，`BeginSleepPresentation` 拒绝换日。`TideStormRescueController.FloodStarted/CargoReleased` 分别记录实际进水与搁架失效；`RecoverSurvivingStormCargoAtRest` 只整理退水后的真实幸存物
- 场景取舍门：`TideStormRescueTradeoffConvergenceProbe` 同时遍历峰值水况和权威自然潮全过程的 24 种完整优先级；验证不操作更差、至少可救一件、最优也不能全救、顺序产生不同实物损失，并检查吊升完成帧不跳位。运行控制器只提供房内布局与由正式潮/浪模型生成的环境采样

### 实物维修

- 目标与配方：`TideRepairRecipeModel` 唯一拥有维修目标枚举、海难首件六路映射、分阶段材料需求和住所/船施工位归属；不读取场景、输入、潮位或库存
- 工序节拍：`TideRepairWorkPhaseModel`，统一为检查 -> 清理 -> 试装 -> 固定 -> 密封
- 施工会话：`TideRepairWorkController` 唯一拥有当前目标、连续进度、阶段、暂停和已提交状态；`Advance` 只完成现实工时，材料守恒与世界 owner 成功提交后才允许 `Complete`
- 船骸材料选择：`TideSalvageMaterialModel`，最终固定时才选择能满足需求的最少原物组合
- 实物归属：轻件由 `TideBarrenIslandController.TryIntegrateStagedPart` 管理，重型弯肋由 `TideHeavyWreckPieceOwnershipModel` 管理
- 首件材料工位：`TideRepairRecipeModel.GetArrivalRepairTarget` 把木板映射到地基/船壳、帆布映射到收纳网/船帆；铆接板投住所时必须搬到裂蓄水池，`ApplyCisternPlatePatch` 同帧提交漏率与唯一补片，投船时先完成纯金属舱盖/排水口，第二阶段才需要木框
- 表现投影：`TideV52BoatRepairPresentationModel`、`TideV69HouseRepairPresentationModel`
- 正式 Scene 门：`TideRepairSceneConvergenceProbe`

## 既有核心

- 天文水位/潮流：`TideMixedSemidiurnalModel`、`TideAstronomicalCurrentModel`
- 连续海况：运行消费者只读 `TideAuthoritativeOceanModel`；其内部合成 `TideOceanFieldModel` 连续浪谱与 `TideWaveEventFieldModel` 可见/物理同源的局部浪组
- 天气：`TideContinuousWeatherModel`
- 潮源/实物：`TideDriftSourceModel` 使用连续天文潮次生成不可变批次；不能再使用睡眠/故事轮次 `tideRound`。`TideWrackDepositModel` 与 `TideWrackLineController` 让未捕获且仍在近岸的同一批次在退潮后贴岩搁浅、再浸卷走，拾取不重新开奖；单件外观继续复用 `TideV59FindPresentationModel` 与 V59 Catalog。
- 网具：`TideNetForecastModel`、`TideNetLoadLedgerModel`、`TideNetHaulModel`、`TideV54NetPresentationModel`
- 航海打捞公式：`TideContinuousSalvageModel`；连续运行状态见 `TideSailingSalvageController`
- 房屋维修：V34/V35/V69 Catalog 与 Presentation Model
- 船体维修：V39/V52/V67 Catalog 与 Presentation Model
- 人物：V20/V41/V42 Catalog 与 Presentation Model
- 灯塔：`TideLighthouseVisibilityModel`

## 资源边界

- Catalog/Asset 只保存资源引用、锚点和阶段数据。
- 纯 Model 不读取 `Input`、Scene、Renderer 或 `Time.time`。
- Controller 把玩家意图和世界采样传给 Model。
- Renderer/Collider 只消费权威状态；不能再计算第二套潮位、船位或可走面。
- 当前运行资源见 `Docs/tide-current-runtime-resource-manifest-2026-07-14.md`。

## 推荐阅读练习

1. 从 `HandleMooringRopeInput` 追到 `TideMooringRopeController`，再进入 `TideMooringRopeModel.Advance`，画出输入、环境与表现分别拥有的数据。
2. 从 `HandleSailingInput` 追到 `TideSailboatDynamicsModel.Advance`，分别关掉风、流、玩家输入，预测速度变化。
3. 修改一个纯模型常量，先补 `TideCoreLoopConvergenceProbe` 断言，再改数值。
4. 从 `TideHeavyWreckTidalLiftModel` 比较急流与平流的拖运率，解释为什么“涨潮”和“平流”是两个不同条件。
5. 最后再读 Controller 的视觉分支，理解为什么同一状态不能由两套资源同时拥有。

## 验证

- 聚焦探针：`Assets/Editor/TideCoreLoopConvergenceProbe.cs`
- 正式 Scene 维修门：`Assets/Editor/TideRepairSceneConvergenceProbe.cs`
- 正式 Scene 视觉与流程门：`Assets/Editor/TideVisualSceneConvergenceProbe.cs`；每项重开权威 Scene，验证首日自主性、世界潮尺、暴潮物资守恒、暴潮休息旁路与几何契约，防止预览状态串扰，不执行自动截图
- 静态门：`Tools/check-prototype-loop.ps1` / `.sh`
- 同步门：`Tools/check-unity-sync.ps1` / `.sh`
- 完整入口：`Tools/check-tide-play-readiness.ps1` / `.sh`
- 完整入口同时要求 `TIDE_CORE_LOOP_PROBE PASS`、`TIDE_REPAIR_SCENE_PROBE PASS` 与 `TIDE_VISUAL_SCENE_PROBE PASS`。
