# 模型服务 · 设置页规格

状态：planned（2026-11-21）
更新时间：2026-11-21
对应：ADR-0043；参考图：[providers-reference.png](../assets/model-settings/providers-reference.png)（ccswitch「模型厂商」网格）

## 1. 布局（ADR-0045 独立子页升级）

设置页左侧二级导航包含独立子页「模型服务」（从助理页点击模型胶囊直达本页）：

1. **当前使用**条：显示当前模型（真实厂商 Logo + 名称 + 模型名 + 脱敏 Key）与「编辑配置」；未配置时显示「尚未启用模型」。不使用绿点/红点等状态圆点。
2. **搜索与筛选栏**：按厂商名/域名实时搜索 + 分类切换（全部 / 国内厂商 / 海外与本地 / 已配置）+ 顶栏「＋ 自定义模型」快捷入口。
3. **模型厂商**网格：整页自然滚动两列卡片（窄窗降为一列），卡片 = 42×42 真实公司 Logo 容器（`Assets/Providers/<id>.png`）+ 厂商名（14 加粗）+ 域名与「N 个版本」（12 灰）+ 右侧「已配置 / 使用中」徽标；自定义入口同步位于网格末尾。
3. 点击卡片 → 右侧抽屉（窗内覆盖层，遵循 ADR-0032 的窗内浮层约定，禁止原生 Popup）：
   - 顶部：厂商名 + 域名链接（「获取 API Key」，用系统浏览器打开）。
   - **版本**：当厂商有多个接入点（通用 / 国际站 / Coding 套餐…）时的下拉；单版本不显示。
   - **API Key**：密码框，显示/隐藏；保存后只显示末 4 位。
   - **模型**：按 2026 最新官方文档展示厂商预设默认模型胶囊（点击一键切换，带视觉多模态标识），支持行内「+ 新增模型」自定义添加与「拉取列表」；已知官方模型自动判定是否支持图片输入，无需手动勾开。
   - **高级（折叠）**：Base URL（预填、可改）、协议格式（预设厂商不可改）、「支持图片输入」手动覆盖开关（未识别自定义模型可用）。
   - 底部：「测试连接」「保存」「设为当前使用」「删除」。测试通过才允许设为当前使用。
4. **自定义**卡片抽屉：名称、Base URL、API Key、协议格式（OpenAI 兼容 / Anthropic 兼容）、模型、「支持图片输入」。必填校验行内提示，URL 必须 https（`localhost` / 127.0.0.1 允许 http）。

## 2. 预设厂商（首批 · 已全部核对 2026-11-21）

对齐参考图 15 家，并补充 5 家常用。所有 20 家官方 Base URL、协议与默认模型已核对并写入 `src/VGList.App/Assets/Providers/providers.json`（核对出处见 `.trellis/tasks/11-21-model-provider-settings/research/provider-catalog.md`）。

| 厂商 | 域名 | 版本（示例） | 协议 | 初值 Base URL |
|---|---|---|---|---|
| Baidu Qianfan | cloud.baidu.com | 通用 / Coding | OpenAI | `https://qianfan.baidubce.com/v2` |
| BaiLing（蚂蚁百灵） | developer.ant-ling.com | 通用 | OpenAI | 待核 |
| BytePlus | byteplus.com | 通用 | OpenAI | 待核 |
| DeepSeek | platform.deepseek.com | 通用 | OpenAI / Anthropic | `https://api.deepseek.com` |
| 火山引擎 | volcengine.com | 通用 / Coding / 其他 | OpenAI | `https://ark.cn-beijing.volces.com/api/v3` |
| Kimi | platform.kimi.com | 国内 / 国际 / Coding 等 | OpenAI / Anthropic | `https://api.moonshot.cn/v1`（版本待核） |
| Longcat | longcat.chat | 通用 | OpenAI | 待核 |
| Meta Model API | dev.meta.ai | 通用 | OpenAI | 待核 |
| MiniMax | platform.minimax.cn | 国内 / 国际 | OpenAI / Anthropic | `https://api.minimax.chat/v1`（待核） |
| 千问 AI 平台 | platform.qianwenai.com | 多版本 | OpenAI | 待核 |
| QwenCloud | home.qwencloud.com | 国内 / 国际 / 其他 | OpenAI | `https://dashscope.aliyuncs.com/compatible-mode/v1`（待核） |
| StepFun | platform.stepfun.com | 国内 / 国际 | OpenAI | `https://api.stepfun.com/v1`（待核） |
| 腾讯云 | cloud.tencent.com | 多版本（含混元） | OpenAI | `https://api.hunyuan.cloud.tencent.com/v1`（待核） |
| Xiaomi MiMo | platform.xiaomimimo.com | 通用 / 其他 | OpenAI | 待核 |
| Zhipu GLM | open.bigmodel.cn | 国内 / 国际 | OpenAI | `https://open.bigmodel.cn/api/paas/v4` |
| OpenAI | platform.openai.com | — | OpenAI | `https://api.openai.com/v1` |
| Anthropic | console.anthropic.com | — | Anthropic | `https://api.anthropic.com` |
| Google Gemini | ai.google.dev | — | OpenAI 兼容 | 待核 |
| OpenRouter | openrouter.ai | — | OpenAI | `https://openrouter.ai/api/v1` |
| Ollama（本地） | localhost | — | OpenAI | `http://localhost:11434/v1`，无需 Key |

## 3. 规则

1. 目录随包分发：`Assets/Providers/providers.json`；字段：`id / name / domain / apiKeyUrl / variants[{name, baseUrl, protocol, defaultModel, supportsImages}]`。
2. 用户配置存本机；Key 经 DPAPI 加密；导出/日志不含 Key。
3. 仅一个「当前使用」；删除当前使用项后回到「尚未配置」。
4. 错误提示须可行动：Key 无效 / 网络不通 / 模型不存在 / 额度不足，分别提示。
5. 厂商图标：首字母 + 品牌色方块（无授权素材时），见 ADR-0043 第 6 条。

## 4. 验收清单

- [ ] 20 张预设卡片 + 自定义卡片正常渲染，窄窗口降为一列。
- [ ] 预设厂商仅填 Key 即可「测试连接」并设为当前使用。
- [ ] 自定义 URL（含 localhost http）可用；非法 URL 有行内提示。
- [ ] Key 不以明文出现在 settings 文件、日志、UI（仅末 4 位）。
- [ ] 所有标「待核」的 Base URL 已由官方文档确认并在目录中标注来源与核对日期。
- [ ] 截图（100% / 125% / 150% DPI）存档到 `harness/assets/model-settings/`。
