# Tide 项目研究

本文只记录当前工程的稳定事实和第一阶段开发判断，不把游戏主题写死。

## 当前项目形态

Tide 现在是一个干净的 Unity 2D URP 项目，还不是玩法原型。

- Unity：`2022.3.62f3`
- 渲染管线：Universal Render Pipeline `14.0.12`
- Renderer：URP 2D Renderer
- Build Scene：`Assets/Scenes/SampleScene.unity`
- 当前场景对象：
  - `Main Camera`
  - `Global Light 2D`
  - `Light 2D`
  - 带 `SpriteRenderer` 的 `Capsule`
- 当前资源目录：
  - `Assets/Scenes`
  - `Assets/Settings`
  - `Assets/Settings/Scenes`
- 当前玩法代码：无
- 当前自定义美术 / 音频资源：无

这反而是好事。项目还没有历史包袱，第一版可以直接围绕一个最小循环搭。

## 已核验技术事实

- `ProjectSettings/EditorSettings.asset` 的 `m_SerializationMode: 2` 表示 Force Text。
- `ProjectSettings/VersionControlSettings.asset` 使用 Visible Meta Files。
- `ProjectSettings/GraphicsSettings.asset` 指向 `Assets/Settings/UniversalRP.asset`。
- `Assets/Settings/UniversalRP.asset` 使用 `Assets/Settings/Renderer2D.asset` 作为 2D Renderer Data。
- `ProjectSettings/EditorBuildSettings.asset` 目前只包含 `Assets/Scenes/SampleScene.unity`。
- `.gitignore` 已排除 Unity 生成目录。
- `.gitattributes` 已让 Unity 文本资源保持可 diff，并让常见二进制资源进入 Git LFS。
- 远端仓库可以重新浅克隆，克隆后能读到预期的 Unity 版本文件。

## 对第一版玩法的影响

2D 不等于只能俯视角。对 Tide 来说，两个方向都成立：

- 俯视角：潮汐更像地图范围、路径开放、区域封锁和路线规划。
- 横板 / 侧视角：潮汐更像可见水位、压力、回撤路线和垂直安全区。

如果做横板，第一版不要做“硬核平台跳跃”。更稳的结构是：

```text
侧视角行走探索 + 轻解谜 + 上涨水位压力
```

先不要做：

- 精密跳跃；
- 战斗；
- 重背包生存；
- 大量剧情分支；
- 程序生成关卡；
- 复杂存档系统。

第一版只证明一个循环：

```text
进入低处 -> 拿到一个东西 -> 潮水上涨 -> 返回高处或找到新的安全点
```

## 建议的初始目录

目录不要提前造太多。等第一批代码和资源真的出现时，再按这个形状建：

```text
Assets/
  Art/
    Sprites/
    Materials/
  Audio/
    Music/
    Sfx/
  Prefabs/
  Scenes/
  Scripts/
    Core/
    Player/
    Tide/
    Interaction/
    UI/
  Settings/
```

`asmdef` 暂时不用急。现在项目太小，文件夹边界足够。

## 第一批系统

1. 玩家控制器
   - 先只做水平移动；
   - 如果是横板，再加最基础的落地判断；
   - 不做冲刺、攀爬、二段跳等高级移动。

2. 潮汐控制器
   - 全局只有一个权威潮汐值；
   - 可以配置周期长度；
   - 水位物体或淹没区域由这个值驱动。

3. 交互系统
   - 一个键完成查看、拾取、开关或触发；
   - 给少量文字反馈；
   - 第一版不做背包 UI。

4. 原型关卡
   - 一条低处路线；
   - 一条高处路线；
   - 一个必须回撤或上行的压力点。

5. 氛围层
   - 2D Light；
   - 简单环境声；
   - 黑暗、雾、水面反光放在玩法循环跑通之后。

## 主题方向记录

`Tide: Anchor` 可以当临时名，但确实偏文艺和抽象。如果你想要更现实、更黑暗，并且用负面衬托美好，当前更适合的工作方向是：

```text
Tide: Below the Line
```

暂定前提：

```text
一个疲惫的人在夜间反复涨潮的低洼海边城区里行动，取回文件、药、钥匙、照片和一些很小但具体的生活证据。每次潮水都会重新吞掉低处。
```

黑暗不需要靠大段台词表达，可以由世界本身承担：

- 低处总会被淹；
- 明明离安全处不远，但时间不够；
- 别人的房间亮着，你只能从水里路过；
- 有些东西救不回来，只能选择带走哪一个；
- 美好的瞬间很小，例如还亮着的楼梯灯、没被水泡湿的照片、潮面上短暂完整的月亮。

## 主要风险

- 先写主题，后找玩法，容易空转。
- 第一张场景做太大，会立刻失控。
- 把项目成败当成自我价值判断，会让开发负担过重。
- 潮汐循环没跑通前就加系统，会拖慢进度。
- Windows 和 macOS 来回改同一个场景，容易制造冲突。

## 下一个具体里程碑

Milestone 0 只做一个小 playable block：

```text
一个场景。
一个可控制角色。
一个潮汐周期。
一个要取回的物品。
一个高处安全点。
一个失败或重开条件。
无战斗。
无存档。
无剧情分支。
主菜单可选。
```

完成标准：

```text
从新克隆的项目打开 Unity，按 Play，可以取物、经历潮水压力、成功回到安全点或失败重开。
```
