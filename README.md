<div align="center">

<img src="src/VGList.Desktop/Assets/Plana/plana-mark.png" alt="Plana Logo" width="84" />

<br />

<img src="src/VGList.Desktop/Assets/Plana/plana-wordmark.png" alt="Plana" width="150" />

### 原生轻量、本地优先的桌面任务、日程与 AI 规划工作台

把「今天要做什么」放在桌面上——一眼可见、一键可改、随时可撤；更有懂日程的 **Lana** 陪你规划每一天。

<p>
  <a href="README.md"><strong>简体中文</strong></a> ·
  <a href="README_EN.md">English</a> ·
  <a href="https://github.com/s0ng1v/Plana/releases/latest"><strong>下载最新版 (v0.1.10)</strong></a> ·
  <a href="CHANGELOG.md">更新日志</a>
</p>

<p>
  <a href="https://github.com/s0ng1v/Plana/releases/latest"><img src="https://img.shields.io/badge/Release-v0.1.10-2F6BFF?style=flat-square" alt="Release v0.1.10" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-1F8A5B?style=flat-square" alt="License: MIT" /></a>
  <img src="https://img.shields.io/badge/.NET-10_LTS-512BD4?style=flat-square" alt=".NET 10 LTS" />
  <img src="https://img.shields.io/badge/Avalonia-12.1.6-8B5CF6?style=flat-square" alt="Avalonia 12.1.6" />
  <img src="https://img.shields.io/badge/Platform-Windows_x64_%7C_macOS_(Preview)-0EA5B7?style=flat-square" alt="Platforms" />
</p>

<br />

<img src="docs/screenshots/02-lana-chat.png" alt="Plana · Lana 智能日程助理" width="100%" />

</div>

---

## ✨ 认识 Lana：不止会聊天的日程规划助理

Lana 是内置在 Plana 里的原生 AI 日程助手——不打开时零存在感、不占资源；需要规划时随叫随到：

- **一句话排期与拆解**：直接说「把明天下午的评审提前 15 分钟提醒」「帮我把周报拆成三步」，Lana 会结合已有日程自动定日期、设提醒、判优先级并同步安排。
- **看懂图片与各类文档**：支持拖入或粘贴截图、PDF、Word、Excel、PPT、`.ics` 日历、网页和代码文本（输入 `@` 精准引用），帮你把课程表、会议纪要直接整理成待办。
- **实时进度与可微调/可撤销卡片**：工具查日程、读文件过程实时可见；生成的日程卡片里可直接切换优先级、调整提前或反复提醒，**支持单条撤销、多选撤销与 `Ctrl+Z` 整批撤销**。
- **懂你的规则与长期记忆**：可设置每日任务上限、高优上限与休息日偏好，支持查看编辑本地画像与记忆记录，还能生成周复盘总结与改进建议。
- **20 家模型服务 + 本地离线模型**：预置 DeepSeek、Claude、OpenAI、Gemini、Kimi、智谱、通义、豆包、OpenRouter 与本地 Ollama，密钥仅在本机加密保存。
- **外部 AI 安全接入（MCP）**：按需开启本地 MCP 服务后，Claude Desktop、Cursor、Antigravity 等外部 AI 工具也能按规则安全读写你的 Plana 日程。

---

## 🖼️ 界面预览（v0.1.10）

### Plana 淡粉樱雾主题 · 个性化欢迎舞台

支持 **经典蓝白** 与 **Plana 淡粉樱雾** 双主题即时切换，打开即呈现当前日期农历与待办概览，动效柔和不打扰。

<img src="docs/screenshots/06-plana-theme-welcome.png" alt="Plana 淡粉樱雾主题与 Lana 欢迎页" width="100%" />

---

### 任务工作台与详情面板

聚合「今天」「最近 7 天」「收集箱」「自定义清单」「已完成」与「回收站（保留 30 天）」，支持行内输入中文日期自动识别（如「下周三」「月底前」）、多 `#标签` 与重复任务筛选、子待办进度及具体时间提醒。

<img src="docs/screenshots/01-tasks-main.png" alt="Plana 任务工作台与详情面板" width="100%" />

---

### 今日日程 + 月历桌面小组件

两个无边框透明工具窗口（不占任务栏与 `Alt+Tab`），与主窗口共享同一本地数据库毫秒级同步；支持 40%–100% 透明度调节、自由缩放、位置尺寸锁定、鼠标点击穿透（托盘一键解除），以及逾期任务一键「顺延到今天」。

<img src="docs/screenshots/07-desktop-widgets.png" alt="Plana 今日日程与月历桌面小组件" width="100%" />

---

### 四象限矩阵

按优先级自动映射重要/紧急四象限，支持跨象限拖拽改优先级与侧向悬浮编辑卡片，帮你快速分清轻重缓急。

<img src="docs/screenshots/03-eisenhower.png" alt="Plana 四象限矩阵" width="100%" />

---

### 月 / 周双视图日历

内置 1901–2100 年离线农历、二十四节气与公农历节日标注，无需联网即可查看完整历法；任务条支持直接拖拽改期与悬浮快速新建。

<img src="docs/screenshots/04-calendar-month.png" alt="Plana 月视图日历" width="100%" />

---

### 模型服务配置（20 家官方预设 + 自定义）

主流厂商开箱即用，自动识别多模态视觉能力与多版本接入点，也支持接入任意 OpenAI / Anthropic 兼容地址或本机 Ollama。

<img src="docs/screenshots/05-model-settings.png" alt="Plana 模型服务设置" width="100%" />

---

## 🌟 更多特性

- **真正原生架构**：基于 **.NET 10 + Avalonia 12** 构建，拒绝套壳 WebView；内置 `Anthropic` 与中文思源字体栈，不同机器保持一致清晰排版。
- **本地优先与隐私保护**：数据全部存储在本地 SQLite 数据库中，无账号绑定、无任何遥测上报；Windows 下 API Key 经系统 DPAPI 加密保护。
- **数据备份与迁移**：支持 **JSON** 完整备份、**Markdown** 清单与标准 **RFC 5545 (`.ics`)** 日历导入导出（导入同样支持整批撤销）。
- **外部 AI 接入配置示例（MCP）**：在「设置 → 助理与记忆 → 外部 AI 接入」开启后，可一键复制如下配置给 Claude Desktop / Cursor / Antigravity 使用：

```json
{
  "mcpServers": {
    "plana": {
      "command": "<Plana 安装目录>/VGList.Mcp.exe",
      "args": []
    }
  }
}
```

---

## 📦 下载与安装

前往 **[GitHub Releases](https://github.com/s0ng1v/Plana/releases/latest)** 下载对应平台安装包：

### Windows 10 / 11 (x64)（主力支持）
- **安装版（推荐）**：下载 `Plana-Setup-0.1.10.exe` 运行即可，免管理员权限安装到当前用户目录，后续覆盖升级自动保留全部任务、设置与记忆。
- **绿色便携版**：下载 `Plana-win-x64-portable-0.1.10.zip` 解压后运行 `VGList.exe`。

### macOS（预览体验版）
- **Apple Silicon (M1 / M2 / M3 / M4)**：下载 `Plana-mac-arm64-0.1.10.zip`。
- **Intel Mac**：下载 `Plana-mac-x64-0.1.10.zip`。
- 解压后保持 `Plana.app`、`首次运行.command` 与 `安装说明.txt` 在同一目录，**双击（或右键 → 打开）`首次运行.command`** 完成首次签名准备并启动。

> **macOS 预览版提示**：暂无 Apple Developer ID 签名与公证；开机自启、点击穿透与剪贴板图片/文件粘贴暂未在 macOS 端实现，完整体验建议优先使用 Windows 版。

---

## 🛠️ 本地构建与打包

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)（`10.0.x`）。

```powershell
# 1. 还原与构建（Windows）
dotnet restore VGList.slnx
dotnet build src/VGList.Desktop/VGList.Desktop.csproj -c Release -f net10.0-windows --no-restore

# 2. 运行主程序（开发调试）
dotnet run --project src/VGList.Desktop/VGList.Desktop.csproj -f net10.0-windows

# 3. 运行单元测试
dotnet test tests/VGList.Tests/VGList.Tests.csproj -c Release

# 4. 打包 Windows 安装程序（需 Inno Setup 6，输出至 dist/Plana-Setup-0.1.10.exe）
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/build-installer.ps1 -Version 0.1.10
```

在 macOS 上构建双架构预览包：

```bash
chmod +x scripts/build-macos.sh
bash scripts/build-macos.sh 0.1.10
```

---

## 🗂️ 工程结构与本地数据目录

| 工程 / 目录 | 职责 |
|---|---|
| `src/VGList.Core` | 领域模型、重复规则、提醒计算、离线中文历法（无 UI / IO 依赖） |
| `src/VGList.Data` | SQLite (WAL) 仓储、顺序迁移与变更集撤销引擎 |
| `src/VGList.App` | 用例服务、设置/模型配置存储、规则/记忆/画像、MCP 桥接 |
| `src/VGList.Agent` | Lana 助理内核（模型客户端、工具、提案、附件解析） |
| `src/VGList.Widgets` | `WidgetHost`、今日日程小组件、月历小组件、主题令牌与内置字体 |
| `src/VGList.Desktop` | 主窗口工作台、四象限、日历、设置页、Lana 舞台与托盘管理 |
| `src/VGList.Mcp` | 独立 MCP 本地代理进程（命名管道转发至主进程） |
| `src/VGList.Platform*` | 跨平台抽象与 Windows / macOS 系统集成实现 |
| `tests/VGList.Tests` | xUnit 单元与集成回归测试套件 |

**本地数据目录**（卸载或覆盖升级均不会删除）：
- **Windows**：`%LOCALAPPDATA%\VGList\`（包含 `plana.db`、`settings.json`、`models.json`、`assistant\`、`mcp\`）
- **macOS**：`~/.local/share/VGList/`

---

## ⌨️ 常用快捷键

| 快捷键 | 功能 |
|---|---|
| `Ctrl + 1` / `2` / `3` / `4` | 切换「任务 / 四象限 / 日历 / Lana 助理」页面 |
| `Ctrl + J` | 快速打开 Lana 助理页面 |
| `Ctrl + ,` | 打开设置页 |
| `Ctrl + B` | 折叠 / 展开左侧清单栏 |
| `Ctrl + N` | 聚焦任务页快速添加框 |
| `Ctrl + Z` | 撤销最近一次 Lana / MCP 日程变更（焦点不在输入框时） |
| `Tab` | 在任务录入框内循环切换优先级 |
| `Esc` | 关闭当前弹层 / 抽屉 / 详情面板 |

---

## 📄 开源协议与第三方声明

- 本项目源码采用 **[MIT License](LICENSE)** 开源协议。
- **内置字体声明**：`Noto Sans SC` / `Noto Serif SC` 遵循 [SIL Open Font License 1.1](https://openfontlicense.org/) 分发；`Anthropic Sans / Serif / Mono` 为 Anthropic PBC 版权字体资源，仅随程序用于界面排版，不适用 MIT 代码授权的再授权。
- **第三方厂商标识**：`Assets/Providers/` 中的服务商 Logo 仅用于在设置页标识对应模型厂商，相关商标权益归各自所有者所有。
