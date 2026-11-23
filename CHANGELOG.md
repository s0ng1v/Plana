# Changelog

All notable changes to **Plana** will be documented in this file.

## [v0.1.10] - 2026-11-23

### Visual Polish & Release Fixes

- **Toast 圆角阴影修复（ADR-0069）**：移除 `ToastBanner.axaml` 的矩形裁切 `DropShadowEffect`，统一采用与全站浮层一致的圆角 `BoxShadow` 与对称留白外边距，消除提示条底边左右两侧残留灰色尖角阴影的问题。
- **跨平台构建与文档完善**：补充 GitHub Actions CI 与多平台自动 Release 工作流（Windows 安装版 / 便携版 + macOS Apple Silicon / Intel 预览版 + `SHA256SUMS.txt`），并更新 `v0.1.10` 双语 README 与实机高清截图。

### Core Highlights (Included in v0.1.10)

- **桌面原生架构**：基于 `.NET 10` + `Avalonia 12` 构建（Windows / macOS Preview），无 Electron / WebView 嵌套开销；本地 `SQLite (WAL)` 持久化，支持每步变更集记录与细粒度撤销（单条 / 多选 / 整批撤销、`Ctrl+Z`）。
- **主窗口与多视图工作台**：任务页（今天 / 最近 7 天 / 收集箱 / 自定义清单 / 已完成 / 回收站、多 `#标签` 与重复筛选、中文自然语言日期解析）、四象限页（跨象限拖拽与侧向悬浮编辑卡）、日历页（月/周视图、1901–2100 离线农历/节气/节日、拖拽改期）。
- **桌面小组件与实时同步**：今日日程与月历透明工具窗口（不占任务栏与 `Alt+Tab`），支持 40%–100% 透明度、80%–200% 缩放、锁定与点击穿透（托盘一键解除），以及「已过期」任务一键顺延到今天。
- **Lana 智能日程助理**：内置交互式陪伴舞台与全窗视线跟随；支持流式工具进度、自适应 Markdown 表格、多格式附件（图片/PDF/Word/Excel/PPT/ICS/文本/代码）及 `@` 引用、规则/记忆/用户画像（`profile.md`）与周复盘。
- **20 家主流模型服务 + 本地 MCP Server**：预置 DeepSeek、Anthropic、OpenAI、Google Gemini、Kimi、Zhipu GLM、QwenCloud、火山引擎、OpenRouter、Ollama 等 20 家厂商最新模型目录（Windows DPAPI 加密 Key），并随包提供独立轻量化 `VGList.Mcp` stdio 代理接入外部 AI 工具。

## [v0.1.9] - 2026-11-23

- 初始公开发布基准版本：集成桌面工作台、双桌面小组件、Skia 舞台级 Lana 动效、20 家模型服务、本地 MCP 服务、双主题（经典蓝白 / Plana 淡粉樱雾）及 Windows / macOS 打包脚本。
