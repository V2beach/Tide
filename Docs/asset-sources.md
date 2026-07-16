# Tide 资源来源

更新时间：2026-07-16

本文件只保留当前仓库中实际运行资源的来源与使用边界。被删除的 A/B 试用包、QA 板、生成脚本和历史候选不再列为运行依赖。

## NASA 月球

- 资源：`Assets/Art/Moon/PIA00405_Moon_CircleAlpha.png`
- 原始条目：NASA Science Photojournal，`PIA00405 - Earth's Moon`
- 来源：https://science.nasa.gov/photojournal/earths-moon/
- 使用方式：原始月面照片的透明圆形派生图；连续月相、照明比例和潮汐状态由运行代码拥有。
- 仓库策略：只保留运行派生图，不保留未引用的方形原始 JPG。

## 项目生成资源

`Assets/Art/GeneratedAI/` 与 `Assets/Resources/StiltFirstSliceAI/` 下的房屋、人物、帆船、海况、漩涡、灯塔、潮获、船骸和动作资源均为本项目生成或由这些项目自有资源确定性派生。主要生成期为 2026-07-11 至 2026-07-16。

- 生成方式：OpenAI 图像生成、项目内参考图编辑，以及离线裁切、透明化、调色、分层、缩放和状态差分。
- 外部素材：这些包不以 Kenney、Sonniss 或其他第三方游戏素材为母图。
- 追踪方式：每个保留包内的 `runtime-contract.json`、`production-audit.json`、README 或 Catalog 记录具体 owner、画布、PPU、Pivot、锚点和派生关系。
- 运行边界：资源只拥有合同声明的像素；潮位、风、流、碰撞、物体身份和状态转移由代码拥有。
- 仓库策略：提交当前运行档与必要契约；Source、QA、GIF、UHD 和一次性 Python 生成器不提交。

当前运行组合以 `Docs/tide-current-runtime-resource-manifest-2026-07-14.md` 为准。资源会话的新增交付必须回写 `Docs/tide-production-art-handoff.md`。

## 已移除试用素材

下列试用素材曾用于早期 A/B，但当前没有场景、Prefab、Catalog 或代码 GUID 引用，已从主线删除：

- Kenney Pixel Platformer Industrial Expansion（CC0）
- Kenney Particle Pack（CC0）
- Sonniss GDC 2026 Audio A/B 暂存包

删除只表示 Tide 当前不使用这些文件，不改变原作者或许可证。若未来重新引入，必须重新登记来源、许可证、实际使用文件和运行职责，不能从旧缓存直接恢复整包。

## 声音

当前海声、脚步和交互提示由 `TideAudioController` 在运行时程序生成，不使用外部音频文件。以后引入录音时，必须记录来源 URL、许可证、原始文件名、裁切/混音变化和是否允许商业发行。
