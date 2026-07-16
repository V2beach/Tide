# Tide 代码阅读地图

## 入口

1. `Assets/Scenes/Tide_StiltHouse_FirstSlice.unity`
2. `Assets/Scripts/StiltHouse/TideStiltHouseFirstSliceController.cs`
3. `Assets/Scripts/StiltHouse/TideOceanFieldModel.cs`
4. 本页下面列出的独立纯模型

`TideStiltHouseFirstSliceController` 仍然过大。阅读时不要从第一行顺序看到底；先按“输入 -> 状态 -> 表现”追一条完整链。

## 2026-07-16 新链路

### 岩礁岛与淡水

- 表现/实物所有权：`TideBarrenIslandController`
- 流量规则：`TideRainCisternModel`
- `F` 交互优先级：`TideIslandInteractionModel`；携带原物时只允许在明确施工位暂存，不能同时饮水或触发泊船绳
- 编排入口：`TickBarrenIslandNaturalState`、`TryHandleBarrenIslandInteraction`
- 核心单位：雨 `mm/h`、屋顶 `m²`、水量 `L`、现实秒
- 水量唯一 owner：`TideRainCisternModel` 负责盐度、蒸发/渗漏和容器转移；`TideBarrenIslandController` 拥有裂池，主控制器只拥有从裂池实际转出的暴潮应急罐。睡眠先用罐水再用池水，冲失时不能二次扣池水
- 首日契约：`RunEditorFirstDayAutonomyProbe` 推进真实世界时钟，验证玩家无需先检查残骸即可拆船、布网或回屋；左岛存在时旧 `arrivalWreckX` 只作镜头参考，不再拥有交互

### 泊位绳

- 规则：`TideMooringRopeModel`
- 输入：`HandleMooringRopeInput`
- 世界推进：`TickMooredBoatCurrent`
- 表现：`UpdateMooringRopeVisuals`
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

- 规则：`TideSailboatDynamicsModel`
- 输入：`HandleSailingInput`
- 积分：`AdvanceSailingSteering`
- 表现：`UpdateSailingSceneVisuals`
- A/D 是有限加速度，W/S 是帆高，Q/E 是前后压舱；风和潮流是两个独立有符号输入。

### 暴潮抢救

- 规则：`TideStormRescueModel`
- 输入：`HandleStormRescueInteraction`
- 推进：`TickStormRescue`
- 表现：`UpdateStormRescueVisuals`
- 物件由浮力、局部水深、潮流和系固决定冲失，剧情不写死次序；第一次抓住漂物后，交互点固定在原开间吊点，`SecuringProgress01` 同时驱动物件和吊绳连续上升
- `Present` 区分“没有这件实物”和“实物被冲失”；`PrepareStormRescueManifest` 只把真实 `4L` 水、`2` 木料、独立干柴和已有海图转成暴潮预留。抢救成功由 `RestoreSecuredStormRescueCargo` 归回可用储物，失败不再对普通库存二次扣账
- 睡眠边界：`WouldSleepIntervalFloodLooseStormCargo` 用权威潮位、风暴压力和可能的结构破口采样到黎明前的水深；未处理物资仍在水路或即将进入水路时，`BeginSleepPresentation` 拒绝换日。`stormRescueFloodStarted` 区分“刚收到警戒”和“实物确实经历过进水”，`RecoverSurvivingStormCargoAtRest` 只整理退水后的幸存物
- 场景取舍门：`TideStormRescueTradeoffConvergenceProbe` 遍历四件物资的 24 种完整优先级，验证至少可救一件、不能全救、不同顺序产生不同实物损失，并检查吊升完成帧不跳位；运行控制器只通过 `TideStormRescueLayout` 提供房内实物布局

### 实物维修

- 工序节拍：`TideRepairWorkPhaseModel`，统一为检查 -> 清理 -> 试装 -> 固定 -> 密封
- 船骸材料选择：`TideSalvageMaterialModel`，最终固定时才选择能满足需求的最少原物组合
- 实物归属：轻件由 `TideBarrenIslandController.TryIntegrateStagedPart` 管理，重型弯肋由 `TideHeavyWreckPieceOwnershipModel` 管理
- 表现投影：`TideV52BoatRepairPresentationModel`、`TideV69HouseRepairPresentationModel`
- 正式 Scene 门：`TideRepairSceneConvergenceProbe`

## 既有核心

- 天文水位/潮流：`TideMixedSemidiurnalModel`、`TideAstronomicalCurrentModel`
- 连续海况：`TideOceanFieldModel`、`TideWaveEventFieldModel`
- 天气：`TideContinuousWeatherModel`
- 潮源/实物：`TideDriftSourceModel`、`TideV59FindPresentationModel`
- 网具：`TideNetForecastModel`、`TideNetLoadLedgerModel`、`TideNetHaulModel`、`TideV54NetPresentationModel`
- 航海打捞：`TideContinuousSalvageModel`
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

1. 从 `HandleMooringRopeInput` 追到 `TideMooringRopeModel.Advance`，画出每个状态和断绳条件。
2. 从 `HandleSailingInput` 追到 `TideSailboatDynamicsModel.Advance`，分别关掉风、流、玩家输入，预测速度变化。
3. 修改一个纯模型常量，先补 `TideCoreLoopConvergenceProbe` 断言，再改数值。
4. 从 `TideHeavyWreckTidalLiftModel` 比较急流与平流的拖运率，解释为什么“涨潮”和“平流”是两个不同条件。
5. 最后再读 Controller 的视觉分支，理解为什么同一状态不能由两套资源同时拥有。

## 验证

- 聚焦探针：`Assets/Editor/TideCoreLoopConvergenceProbe.cs`
- 正式 Scene 维修门：`Assets/Editor/TideRepairSceneConvergenceProbe.cs`
- 正式 Scene 视觉与流程门：`Assets/Editor/TideVisualSceneConvergenceProbe.cs`；每项重开权威 Scene，验证首日自主性、暴潮物资守恒、暴潮休息旁路与几何契约，防止预览状态串扰，不执行自动截图
- 静态门：`Tools/check-prototype-loop.ps1` / `.sh`
- 同步门：`Tools/check-unity-sync.ps1` / `.sh`
- 完整入口：`Tools/check-tide-play-readiness.ps1` / `.sh`
- 完整入口同时要求 `TIDE_CORE_LOOP_PROBE PASS`、`TIDE_REPAIR_SCENE_PROBE PASS` 与 `TIDE_VISUAL_SCENE_PROBE PASS`。
