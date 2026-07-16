# Tide

Tide 是一个 Unity 2022.3 LTS / URP 2D 侧视原型。当前第一切片围绕：

**岩礁海难起点 -> 拆取同一船骸的原物 -> 修临时归处或逃生船 -> 拉船靠岸 -> 借风浪短航 -> 暴潮中决定保住什么。**

## 打开项目

- 唯一开发场景：`Assets/Scenes/Tide_StiltHouse_FirstSlice.unity`
- Build Settings 只包含这一场景。
- 运行入口：`Assets/Scripts/StiltHouse/TideStiltHouseFirstSliceController.cs`
- 当前任务：`Docs/tide-task-tracking.md`
- 5 分钟剧本：`Docs/prototype-playtest-walkthrough.md`
- 代码地图：`Docs/code-reading-map.md`

不要打开已删除的 `SampleScene`、`Prototype_01` 或 C 盘旧工作树。

## Windows / macOS 同步

项目源文件进入 Git；Unity 本地缓存不进入 Git。图片、音频、视频、字体和模型由 Git LFS 管理。

新电脑首次拉取：

```bash
git clone https://github.com/V2beach/Tide.git
cd Tide
git lfs install
git lfs pull
```

然后用 Unity Hub 的同一 `2022.3 LTS` 编辑器打开仓库根目录。详细步骤见 `Docs/macos-sync.md`。

## 提交前检查

Windows：

```powershell
pwsh Tools/check-tide-play-readiness.ps1
```

macOS：

```bash
bash Tools/check-tide-play-readiness.sh
```

聚合门会检查源码结构、`.meta` 配对、Git LFS 路由，并用 Unity BatchMode 运行当前核心状态探针。它不能证明美术比例、碰撞和交互自然；这些只接受用户原始 Game View 或录像验收。

## 仓库边界

提交：`Assets/`、`Packages/`、`ProjectSettings/`、`Docs/`、`Tools/` 和根配置。

不提交：`Library/`、`Temp/`、`Logs/`、`UserSettings/`、自动截图、QA 板、GIF、UHD 候选和一次性生成脚本。

正式资源必须先满足 `Docs/tide-production-art-handoff.md` 的运行合同。资源存在不等于可以直接接入。
