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
- 编排入口：`TickBarrenIslandNaturalState`、`TryHandleBarrenIslandInteraction`
- 核心单位：雨 `mm/h`、屋顶 `m²`、水量 `L`、现实秒

### 泊位绳

- 规则：`TideMooringRopeModel`
- 输入：`HandleMooringRopeInput`
- 世界推进：`TickMooredBoatCurrent`
- 表现：`UpdateMooringRopeVisuals`
- 状态：Loose -> Swinging -> Attached/Reeling -> Secured；张力过载回 Loose

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
- 物件由浮力、局部水深、潮流和系固决定冲失，剧情不写死次序。

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
4. 最后再读 Controller 的视觉分支，理解为什么同一状态不能由两套资源同时拥有。

## 验证

- 聚焦探针：`Assets/Editor/TideCoreLoopConvergenceProbe.cs`
- 静态门：`Tools/check-prototype-loop.ps1` / `.sh`
- 同步门：`Tools/check-unity-sync.ps1` / `.sh`
- 完整入口：`Tools/check-tide-play-readiness.ps1` / `.sh`
