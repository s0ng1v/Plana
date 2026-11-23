# Changelog

All notable changes to **Plana** will be documented in this file.

## [v0.1.9] - 2026-11-23

### Highlights

- **桌面原生架构**：基于 `.NET 10` + `Avalonia 12` 构建（Windows / macOS Preview），无 Electron / WebView 嵌套开销；本地 `SQLite (WAL)` 持久化，支持每步变更集中管理与细粒度撤销（单条 / 多选 / 整批撤销、`Ctrl+Z`）。
- **主窗口与多视图工作台**：
  - **任务页**：支持「今天 / 最近 7 天 / 收集箱 / 自定义清单 / 已完成 / 回收站（保留 30 天）」、多 `#标签` 筛选、重复任务筛选、行内中文自然语言日期识别与右侧详情面板。
  - **四象限页**：按优先级一比一映射四个象限，支持跨象限拖拽改优先级与侧向悬浮编辑卡片。
  - **日历页**：月视图（6×7）与周视图（7 列），内置 1901–2100 离线农历、二十四节气与法定节假日/传统节日标注，支持拖拽改期与行内新建/编辑卡片。
- **桌面小组件与实时同步**：
  - **今日日程小组件**与**月历小组件**：无边框透明工具窗口（不占任务栏与 `Alt+Tab`），共享同一仓储实时联动；支持透明度调节（40%–100%）、自由缩放、置顶、位置尺寸锁定、鼠标点击穿透（含托盘应急解除），以及「已过期」任务一键顺延到今天。
- **Lana 智能日程助理与舞台级 Skia 动效**：
  - 全新 **Skia 五态动效 Lana**（`Sleep` / `Idle` / `Listening` / `Thinking` / `Happy`），支持对话画布唯一活体飞翔转场、历史轮次静态留影、全窗鼠标视线跟随与「减少动效」降级。
  - 支持流式工具进度展示、Markdown 表格自适应排版、可实时调优优先级与提前/反复提醒的日程同步卡片，以及历史消息就地编辑（含附件增删改）与重新生成截断历史。
  - 支持多格式附件拖拽/粘贴及 `@` 引用：图片、PDF、Word (`.docx`)、Excel (`.xlsx`)、PowerPoint (`.pptx`)、`.ics`、HTML、EPUB、RTF、JSON/CSV/代码等文本文件。
  - 本地规则约束、可编辑用户画像（`profile.md`）、记忆列表与周复盘建议。
- **20 家主流模型服务 + 本地 Ollama**：
  - 预置 DeepSeek、Anthropic、OpenAI、Google Gemini、Kimi、Zhipu GLM、QwenCloud、火山引擎、OpenRouter、Ollama 等 20 家厂商最新模型目录与官方图标，支持多版本接入点、自动识别视觉多模态能力及自定义 OpenAI / Anthropic 兼容 URL；Windows 下使用 DPAPI 本地加密保存 API Key。
- **外部 AI 接入（本地 MCP Server）**：
  - 独立轻量化 `VGList.Mcp` stdio 代理进程 + 当前用户专属命名管道桥，默认关闭，开启后可供 Claude Desktop、Cursor、Antigravity 等外部 AI 工具安全读写 Plana 日程（支持只读模式、限流、危险操作应用内确认与脱敏审计日志）。
- **双主题、内置字体与安装包优化**：
  - 提供「经典蓝白（ClassicBlue）」与「Plana 淡粉樱雾（Plana）」双主题即时切换，以及 4 套内嵌字体预设（含思源黑体/宋体可变字重重定基，不依赖系统字体环境）。
  - 全新安装默认空白干净起点（不预置虚假演示任务），提供 Windows 免管理员权限覆盖升级安装程序（Inno Setup 6）与 macOS Apple Silicon / Intel 预览体验包。
