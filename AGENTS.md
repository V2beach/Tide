# Tide 项目协作规则

1. 权威开发目录只有 `D:/UnityEditor/Projects/Tide`，权威场景只有 `Assets/Scenes/Tide_StiltHouse_FirstSlice.unity`。
2. 开始工作前阅读 `Docs/tide-task-tracking.md`、`Docs/core-mechanic-contract.md` 和当前实施计划；每次只推进一个 P0 可验收切片。
3. 所见即所得、现实逻辑、清晰度一致、遮挡合理是四条硬门。碰撞、锚点、排序和视觉任一不一致都不能用提示文字遮掩。
4. 昼夜与剧情时间可以压缩；呼吸、动作、绳索、局部浪、船体惯性等感知频率不得按游戏日倍率加速。
5. `TideStiltHouseFirstSliceController` 只负责编排。新的潮汐、风、绳索、船体、物资和暴潮规则先写成无场景状态的纯模型，再接表现。
6. 运行美术只提交被 Scene/Prefab/Resources/Catalog 引用的文件和已批准的 Balanced 运行档。QA、High、GIF、生成中间物不进主仓库。
7. 不用屏幕文字代替游戏设计引导；正式 HUD 只显示角色能够合理知道的信息。开发状态统一放 F3 调试层。
8. 资源会话按 `Docs/tide-production-art-handoff.md` 交付 `Runtime + contract + audit`；运行会话只在 Game View、碰撞、内存和平台验证后更新运行清册。
9. 三种工作模式的完整 Prompt 见 `Docs/ai-work-prompts.md`。
