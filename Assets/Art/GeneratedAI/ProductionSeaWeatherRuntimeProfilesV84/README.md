# TIDE V84 海浪 / 云层 Balanced 运行档

V84 从 V43 只读派生 24 张 1024x512 海浪帧和 3 张 2048x512 无缝云层。PPU 同比减半，因此世界画布、Pivot、浪基线、八帧相位、云层视差和染色所有权均保持不变。

运行侧应选择 V84 Balanced 海浪/云层与 V70 Balanced 漩涡；同一时刻只驻留一个浪族，天气切换最多短暂并存两个浪族。Repeat 云层不进 Atlas，三层云纹理由全部 Renderer 共享。

本包没有修改 Catalog、Scene、Prefab、Controller 或权威运行清册。QA 投影不是 Unity Game View；Windows/macOS Metal 压缩、Atlas、首次加载、峰值显存和 Build Size 仍需运行会话实测。
