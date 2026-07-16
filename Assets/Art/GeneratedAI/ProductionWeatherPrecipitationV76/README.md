# Production Weather Precipitation V76

V76 用 12 张远/中/近雨束和 4 张近海脱离飞沫，替换当前把 `MoonWashSprite` 拉成长条的雨占位。雨束母形保持竖直，运行时只从连续天气模型读取一次风向倾角；飞沫只允许依附 V43 活跃浪冠，不含第二条浪冠或海面底色。

运行接入必须原子关闭旧 18 条雨，占用已有 Renderer 池或等价池，不得让两套雨同时显示。V76 应在 V74 世界调色之前渲染，暖窗、灯塔 emission 和 UI 仍在 V74 之后。资源包没有修改 Scene、Prefab、Controller、Catalog 或权威运行清册。
