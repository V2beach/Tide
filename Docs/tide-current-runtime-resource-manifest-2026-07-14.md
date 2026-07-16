# Tide 当前运行资源清册

更新时间：2026-07-16

## 唯一开发入口

- 仓库：`D:/UnityEditor/Projects/Tide`
- 分支：`main`
- 场景：`Assets/Scenes/Tide_StiltHouse_FirstSlice.unity`
- 运行编排：`Assets/Scripts/StiltHouse/TideStiltHouseFirstSliceController.cs`
- C 盘历史工作树已删除，不得再把其路径写入 Catalog、文档或脚本。

## 当前组合

| 领域 | 资源/入口 | 所有权 |
| --- | --- | --- |
| 高脚屋外景 | `ProductionStiltDockV34` / `V34HouseExteriorCatalog.asset` | 稳定底图和 6 组外景维修 owner |
| 高脚屋室内 | `ProductionStiltDockV35` / `V35HouseInteriorCatalog.asset` | 与外景同画布、楼板、梯口和桩列；12 组室内维修 owner |
| 房屋身份/路径 | V32、V33、V38 | 发现态身份、同栋修复端点和可走面/梯/门锚点；不得按透明边缘生成碰撞 |
| 潮前固定设施 | V36 | 绳盘短网、倒挂桶、束桩；不拥有房屋和库存 |
| 岸上人物 | V41 + V20 回退 | V41 拥有行走、持网、登船、系网、过门、瞭望；V20 只回退待机/游泳/维修/拉网 |
| 生存人物 | V42 | 受寒、睡眠、溺水、失温；结算由状态机拥有 |
| 船上人物 | V32 空闲 + V37 动作 | 完整身体保留；位于后船体与前船舷之间，不裁腿 |
| 船体 | V39 语义分层 + V46 Balanced 派生 | 后索具、帆、后船体、舱底、前船舷、舵；不拥有人物和海水 |
| 船体维修 | V52/V67/V69 对应 Catalog | 当前阶段懒加载；不得恢复整包常驻或二态跳整船 |
| 连续海体 | V56 Catalog + `TideOceanFieldModel` | 唯一水面、水位、坡度、水平流速和扰动；局部浪只拥有泡沫/浪脊 Alpha |
| 海况/云 | V43 + `TideWaveEventFieldModel` | 长涌、风浪、暴潮透明层及云带；不创建第二水线 |
| 侧视漩涡 | V70 Balanced | 三层 12 相；潮位、吸力、碰撞和船体受力仍由运行模型拥有 |
| 瞭望/灯塔 | V44 + V77 能见度状态 | 未知、方位、已知共用同一海雾；禁止提示图标泄露位置 |
| 潮带实物 | `ProductionTideBorneFindsV59/Runtime/High` / `V59TideFindCatalog.asset` | 12 件实物跨漂来、挂网、手持、暂放、冲失保持身份 |
| 海难船骸候选 | V72 `Runtime/Balanced` | 六件同源残骸；等待左侧岩礁岛正式资源接入 |
| 月亮 | `Assets/Art/Moon/PIA00405_Moon_CircleAlpha.png` | 月面；连续月相遮罩与天文状态由运行侧拥有 |

## 2026-07-16 新系统

- 左侧岩礁岛当前由 `TideBarrenIslandController` 生成结构化占位表现；正式美术尚未通过原始 Game View。
- 淡水由 `TideRainCisternModel` 拥有；雨槽、裂缝、蒸发和盐侵使用现实单位。
- 泊位绳由 `TideMooringRopeModel` 拥有；船不会瞬移回岸。
- 航行动力由 `TideSailboatDynamicsModel` 拥有；A/D、帆、风、流、压舱、浮沉和俯仰共用一个状态。
- 暴潮物资由 `TideStormRescueModel` 拥有；Renderer 只读取浮力、固定和冲失状态。

## Git 规则

- PNG、音频、视频、字体和建模文件走 Git LFS。
- V59 `Runtime/High` 是当前 Catalog 的直接 GUID 依赖，必须提交，不能被通配规则忽略。
- `QA/`、GIF、UHD 候选、自动截图、`Library/`、`Temp/`、`Logs/`、`UserSettings/` 不提交。
- 资源不得移动或改名；替换时保留 GUID，或由对应 Catalog Builder 原子重建引用。

## 运行重建入口

- V20/V41/V42：人物 Catalog Builder
- V31/V32/V37：船与船上人物 Catalog Builder
- V34/V35/V69：房屋 Catalog/维修 Builder
- V43/V44/V54/V56/V59/V70：海况、瞭望、网、海体、潮获、漩涡 Builder
- 聚焦验证：`TideCoreLoopConvergenceProbe.RunFromCommandLine`
- 完整入口：`pwsh Tools/check-tide-play-readiness.ps1`

## 未通过项

- 左侧岩礁岛仍需正式透明分层资源。
- 人物、房屋、船的最终投影清晰度仍未统一。
- 梯子、阁楼边界和船上腿部遮挡仍需用户原始 Game View 复核。
- 尚未执行 Windows/macOS 玩家构建、Metal 实测和完整 Profiler 预算。
