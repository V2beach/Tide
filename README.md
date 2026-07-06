# Tide

Tide 是一个 Unity 2D/URP 游戏项目。当前工程目标是先保证项目可以在 Windows 和 macOS 之间稳定同步开发，再逐步推进可玩的最小版本。

## 环境

- Unity: `2022.3.62f3`
- 渲染管线: Universal Render Pipeline `14.0.12`
- 2D Feature Set: `com.unity.feature.2d`
- 版本管理: Git + Git LFS

## 仓库内容

需要提交并同步：

- `Assets/`
- `Packages/`
- `ProjectSettings/`
- `.gitignore`
- `.gitattributes`
- `README.md`
- `Docs/`

不要提交：

- `Library/`
- `Temp/`
- `Obj/`
- `Logs/`
- `UserSettings/`
- IDE 本地配置

## 首次打开

1. 安装 Unity Hub。
2. 安装 Unity Editor `2022.3.62f3`，macOS 也使用同一版本。
3. 安装 Git LFS。
4. 克隆仓库后执行 `git lfs install`。
5. 在 Unity Hub 中选择项目根目录打开。
6. 第一次打开会重新生成 `Library/`，等待导入完成即可。

## 同步原则

- 只通过 Git 同步项目，不同步 Unity 自动生成目录。
- 每次切换电脑前先提交并推送当前改动。
- 每次在另一台电脑开始工作前先拉取最新改动。
- 场景、Prefab、ScriptableObject、材质等 Unity 文本资源可以合并，但同一时间尽量不要在两台机器上改同一个场景文件。

详细流程见 [macOS 同步开发说明](Docs/macos-sync.md)。

