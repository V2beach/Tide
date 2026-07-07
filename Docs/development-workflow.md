# Tide 开发流程

本文记录当前已经核验过的 Windows / macOS 双机开发流程。它不是宏大的团队规范，目标只有一个：让你在两台电脑之间切换时不丢资源、不提交 Unity 生成垃圾、不把场景冲突攒成灾难。

## 已核验状态

核验时间：`2026-07-07`

- 仓库：`https://github.com/V2beach/Tide.git`
- Windows 本机路径：`D:\UnityEditor\Projects\Tide`
- Unity 版本：`2022.3.62f3`
- 渲染管线：URP `14.0.12`
- Renderer：URP 2D Renderer
- Git LFS：已配置，远端指向 GitHub LFS
- Unity 序列化：Force Text
- Unity Version Control：Visible Meta Files
- 主分支：`main`

## Windows 日常流程

开始开发前：

```powershell
cd D:\UnityEditor\Projects\Tide
git status
git pull --rebase
git lfs pull
```

然后从 Unity Hub 打开：

```text
D:\UnityEditor\Projects\Tide
```

结束开发时：

```powershell
cd D:\UnityEditor\Projects\Tide
git status
git add Assets Packages ProjectSettings Docs README.md .gitignore .gitattributes
git commit -m "描述这次改动"
git push
```

如果 `git status` 出现没见过的文件，先看清楚再提交。尤其不要把 `Library/`、`Temp/`、`Logs/`、`UserSettings/` 之类目录加进 Git。

## macOS 首次准备

安装同一个 Unity 版本：

```text
Unity 2022.3.62f3
```

安装 Git 和 Git LFS：

```bash
brew install git git-lfs
git lfs install
```

克隆项目：

```bash
mkdir -p ~/Projects
cd ~/Projects
git clone https://github.com/V2beach/Tide.git tide
cd tide
git lfs pull
```

然后从 Unity Hub 打开：

```text
~/Projects/tide
```

第一次打开会重新生成 `Library/`。这一步会慢，但它是每台电脑自己的本地缓存，不需要也不应该同步。

## macOS 日常流程

开始开发前：

```bash
cd ~/Projects/tide
git status
git pull --rebase
git lfs pull
```

结束开发时：

```bash
cd ~/Projects/tide
git status
git add Assets Packages ProjectSettings Docs README.md .gitignore .gitattributes
git commit -m "描述这次改动"
git push
```

## 哪些内容进 Git

应该提交：

- `Assets/`
- `Packages/`
- `ProjectSettings/`
- `Docs/`
- `README.md`
- `.gitignore`
- `.gitattributes`

不要提交：

- `Library/`
- `Temp/`
- `Obj/`
- `Build/`
- `Builds/`
- `Logs/`
- `UserSettings/`
- IDE 自动生成文件，例如 `.sln`、`.csproj`、`.idea/`、`.vscode/`

## Unity 资源规则

- 移动或重命名资源时，优先在 Unity Editor 里操作，确保 `.meta` 跟着资源一起变化。
- 新增资源时，资源文件和 `.meta` 文件要一起提交。
- 不要在 Windows 和 macOS 上同时改同一个 `.unity` 场景。
- 不要长时间攒着大量未提交的场景改动。Unity 场景即使用文本序列化，冲突也很难合。
- 图片、音频、字体、PSD/PSB、Aseprite、视频、FBX、Blend 等二进制资源已通过 `.gitattributes` 进入 Git LFS。

## 分支节奏

当前个人原型阶段可以先保持：

```text
main
```

开始做明确玩法后，再使用短生命周期分支：

```bash
git switch -c feature/tide-core
git switch -c feature/level-prototype
git switch -c feature/audio-vfx
```

每个分支只做一个小目标，验证后合回 `main`。

## 提交前检查

每次提交前至少过一遍：

1. `git status` 里只有你预期的文件。
2. 没有 Unity 生成目录被暂存。
3. 新增二进制资源按规则进入 Git LFS。
4. 改过场景或 Prefab 后，Unity 已保存并正常关闭。
5. 这次提交小到可以用一句话讲清楚。

## 出问题时先看这里

macOS 打开后资源缺失：

```bash
git lfs pull
```

Unity 提示版本不一致：先停下，安装 `2022.3.62f3`，不要随手升级工程。

Git 出现大量换行变化：

```bash
git diff --stat
git diff -- Assets Packages ProjectSettings Docs
```

如果只是换行导致的变化，不要急着提交，先确认 `.gitattributes` 是否生效。
