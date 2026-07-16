# TIDE V74 全世界统一光色

V74 是数据型资源包，不是五套重画贴图。它用五张 `32^3` 展平 LUT 统一 V66 房、V67 船、V61/V62 人、V56 海和 V43 云在白昼、黄昏、月夜、雨锋、暴潮中的色温与明度。

## 运行边界

- 时间轴：`Day / Dusk / Night`，采样后按权重混合。
- 天气轴：`ClearIdentity / Rain / Storm`，在时间调色之后执行。
- V62 湿衣/盐蚀先于 V74；暖窗、灯塔 emission 与 UI 后于 V74。
- 开启 V74 时使用中性 `AIDaySeaSkyHD`；旧 `AINightSeaSkyHD` 与分散 tint 只保留为回退，不得同帧叠加。
- 展平图必须按 `Point + Clamp` 导入并手动八点三线性采样，或在编辑器阶段转成 Texture3D。

## 资源预算

5 张 `1024x32 RGBA32` 的未压缩上限为 `0.625 MiB`。QA 图不进入运行时。

## 状态

资源审计：`PASS`。当前没有接入 Unity、没有修改 Catalog/Scene/Prefab，也没有更新权威运行清册。
