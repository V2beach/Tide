# Tide macOS 同步开发

## 原则

Windows 和 macOS 共享 Git 中的 Unity 源文件，不共享 `Library`、`Temp`、`Logs`、`UserSettings` 等机器缓存。二进制资源由 Git LFS 下载；缺少 LFS 时 Unity 会把指针文本当成损坏图片。

## 首次设置

```bash
xcode-select --install
brew install git git-lfs
git lfs install
git clone https://github.com/V2beach/Tide.git
cd Tide
git lfs pull
```

用 Unity Hub 安装与 `ProjectSettings/ProjectVersion.txt` 一致的 `2022.3 LTS` 编辑器，然后打开仓库根目录。首次导入时间较长是正常现象，因为 macOS 会本地重建 `Library`。

## 每次切换设备

离开当前设备：

```bash
git status --short
git diff --check
git add -A
git commit -m "描述本轮改动"
git push origin main
```

来到另一台设备：

```bash
git pull --ff-only origin main
git lfs pull
```

不要让 Windows 与 macOS 同时编辑 `Tide_StiltHouse_FirstSlice.unity`。场景 YAML 冲突通常比脚本冲突更难可靠合并。

## macOS 验证

```bash
bash Tools/check-unity-sync.sh
bash Tools/check-prototype-loop.sh
bash Tools/check-tide-play-readiness.sh
```

第一次打开前没有 Unity 本地缓存时，聚合门可能提示先完成一次导入。导入后重跑。

## 平台注意

- 路径大小写必须与磁盘一致；不要依赖 Windows 的大小写宽容。
- 资源移动和重命名优先在 Unity 内完成，保证 `.meta`/GUID 一起变化。
- 脚本、JSON、YAML、Unity 文本资源统一 LF；Git 属性已配置。
- 本地嵌入的 `Packages/com.coplaydev.unity-mcp` 进入仓库，Mac 无需引用 Windows 绝对路径。
- 目前尚未完成 macOS Metal 玩家构建、纹理显存和 Profiler 签字；编辑器能打开不等于平台验收完成。

## 常见问题

图片显示为几行文本：运行 `git lfs install && git lfs pull`。

场景丢脚本：先检查 `git status`、`.meta` 是否成对，再让 Unity 完成编译；不要随手重建同名脚本制造新 GUID。

拉取被本地改动阻塞：先提交当前工作，不要用 `reset --hard` 或删除 `Library` 以外的项目文件解决源文件冲突。
