# Production Lighthouse Visibility Gate V77

V77 不含运行贴图。它用 V43 低云/海雾、V44 灯塔与光束、V56 连续海体、V74 全世界调色和 V76 正式雨，证明未知、线索中、已知月夜和已知暴潮四种真实能见度。

运行接入必须原子退役 `foggedLighthouseHintRenderer` 的 `GetFoamSprite()` 与 `0.9x0.14m` 横条。未知态只显示横跨地平线的天气雾，不能在灯塔锚点显示泡沫、光点、箭头或局部雾团；已知后的光束作为 V74 后 emission。QA PNG 禁止进入 Catalog、Scene 或 Prefab。
