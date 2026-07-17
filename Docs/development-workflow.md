# Tide 开发流程

## 日常入口

1. 在 `D:/UnityEditor/Projects/Tide` 拉取 `main` 与 LFS 对象。
2. 打开 `Assets/Scenes/Tide_StiltHouse_FirstSlice.unity`。
3. 从 `Docs/tide-task-tracking.md` 只取一个 P0/P1 叶子任务。
4. 修改前先追 `Docs/code-reading-map.md` 中对应的输入、状态和表现链。
5. 用纯模型或聚焦探针验证规则，再进 Play 调节动作和表现。
6. 视觉结论只使用用户原始 Game View/录像；不得用自动裁图替代。

## 当前架构门

- 运行消费者只读取 `TideAuthoritativeOceanModel`：内部合成 `TideOceanFieldModel` 的连续海体与 `TideWaveEventFieldModel` 的可见/物理同源局部浪；表现层不得另建水位、浪力或拍击随机数。
- 纯 Model 不读取 `Input`、Renderer、Scene 或 `Time.time`。
- Controller 只编排意图、模型和 presenter；不能维护第二套船位、潮位或可走面。
- 可见物、交互点、碰撞和动画接触必须读取同一组世界锚点。
- 新资源必须有 owner 与互斥表。旧 owner 退出后才能显示新 owner。
- 人物、房屋、船不得用状态切换时改变 scale 的办法修比例。

## 验证层级

最小静态门：

```powershell
pwsh Tools/check-prototype-loop.ps1
```

同步/LFS 门：

```powershell
pwsh Tools/check-unity-sync.ps1
```

Unity 核心探针与聚合门：

```powershell
pwsh Tools/check-tide-play-readiness.ps1
```

macOS 将 `pwsh ...ps1` 换成对应的 `bash Tools/...sh`。

聚合门通过后仍要按 `Docs/prototype-playtest-walkthrough.md` 手玩。规则、编译、视觉是三种证据，不能互相冒充。

## 提交规则

```powershell
git status --short
git diff --check
git add -A
git lfs ls-files
git diff --cached --stat
git commit -m "描述本轮可验证结果"
git push origin main
```

- 一个提交只表达一轮可验证收敛。
- 改场景、Prefab、Catalog 或资源时必须连同 `.meta` 提交。
- 两台电脑不要同时修改同一场景；切换设备前先提交并推送。
- `Library/`、`Temp/`、`Logs/`、截图证据、QA/GIF/UHD 不得暂存。

## 资源接入

1. 先读 `Docs/tide-current-runtime-resource-manifest-2026-07-14.md`。
2. 再按 `Docs/tide-production-art-handoff.md` 审核世界逻辑、视角、比例、透明层、锚点和 owner。
3. 只选一个运行档，默认 Balanced。
4. 原子接入 Catalog/Renderer，并退役同职责旧资源。
5. 验证 16:9、4:3、内外景、昼夜、潮位和遮挡。
6. 记录未做的 Atlas、Profiler、Windows/macOS 平台测试，不能把资源侧 QA 写成项目终验。

## 三种 Prompt

日常开发收敛、Playtest 优先和架构收敛 Prompt 统一放在 `Docs/ai-work-prompts.md`。
