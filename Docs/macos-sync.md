# macOS 同步开发说明

## 目标

让 Tide 可以在当前 Windows 电脑和家里的 macOS 电脑之间同步开发。核心方案是 Git 托管源码与项目配置，Git LFS 托管图片、音频、PSD/Aseprite 等二进制资源，Unity 本地生成目录由各电脑自行重建。

## 一次性准备

### Windows 当前电脑

当前项目路径：

```text
D:\UnityEditor\Projects\Tide
```

已确认：

- Unity 版本是 `2022.3.62f3`
- 项目使用 URP 2D
- Unity 序列化模式是 Force Text
- Version Control 模式是 Visible Meta Files
- 本机已安装 Git LFS

需要做的事：

```powershell
cd D:\UnityEditor\Projects\Tide
git status
git remote add origin <你的远程仓库地址>
git push -u origin main
```

如果远程仓库还没建，建议建一个私有仓库。仓库名可以直接用 `tide`。

### macOS 家里电脑

安装：

- Unity Hub
- Unity Editor `2022.3.62f3`
- Git
- Git LFS
- Rider、Visual Studio Code 或你习惯的 C# IDE

推荐安装命令：

```bash
brew install git git-lfs
git lfs install
```

克隆项目：

```bash
mkdir -p ~/Projects
cd ~/Projects
git clone <你的远程仓库地址> tide
cd tide
git lfs pull
```

然后用 Unity Hub 打开：

```text
~/Projects/tide
```

第一次打开时 Unity 会重新生成 `Library/`，这一步较慢但正常。

## 每次切换电脑前

在当前电脑：

```bash
git status
git add Assets Packages ProjectSettings Docs README.md .gitignore .gitattributes
git commit -m "描述这次改动"
git push
```

在另一台电脑：

```bash
git pull --rebase
git lfs pull
```

## Unity 协作注意点

1. 不提交 `Library/`、`Temp/`、`Logs/`、`UserSettings/`。
2. 不要在两台电脑上同时改同一个 `.unity` 场景或大型 `.prefab`。
3. 新增图片、音频、字体、PSD、Aseprite 文件后确认它们进入 Git LFS。
4. 移动或重命名 Unity 资源时，优先在 Unity Editor 内操作，避免 `.meta` 丢失。
5. 每次做出一个小的可验证进展就提交一次，不要攒到很大再提交。

## 推荐分支节奏

个人项目可以先保持简单：

```text
main
```

当开始做明确玩法时再加短分支：

```text
feature/tide-core
feature/level-prototype
feature/audio-and-vfx
```

每个分支只做一个方向，完成后合回 `main`。

## 常见问题

### macOS 打开项目后资源丢失

先执行：

```bash
git lfs pull
```

如果还缺资源，检查新增资源是否在 Windows 侧被提交。

### Unity 提示版本不同

不要直接升级项目。先在 Unity Hub 安装 `2022.3.62f3`，用完全一致版本打开。

### Git 显示大量换行变化

先不要提交。执行：

```bash
git status
git diff --stat
```

确认是否只是换行导致。当前仓库已用 `.gitattributes` 固定常见文本资源为 LF，后续新文件会稳定很多。

