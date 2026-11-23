<div align="center">

<img src="src/VGList.Desktop/Assets/Plana/plana-mark.png" alt="Plana Logo" width="84" />

<br />

<img src="src/VGList.Desktop/Assets/Plana/plana-wordmark.png" alt="Plana" width="150" />

### Native, Local-First Desktop Tasks, Calendar Widgets & AI Schedule Assistant

Keep what matters today right on your desktop — visible at a glance, editable in one click, always undoable, with **Lana** built in to help organize your schedule.

<p>
  <a href="README.md">简体中文</a> ·
  <a href="README_EN.md"><strong>English</strong></a> ·
  <a href="https://github.com/s0ng1v/Plana/releases/latest"><strong>Download Latest (v0.1.10)</strong></a> ·
  <a href="CHANGELOG.md">Changelog</a>
</p>

<p>
  <a href="https://github.com/s0ng1v/Plana/releases/latest"><img src="https://img.shields.io/badge/Release-v0.1.10-2F6BFF?style=flat-square" alt="Release v0.1.10" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-1F8A5B?style=flat-square" alt="License: MIT" /></a>
  <img src="https://img.shields.io/badge/.NET-10_LTS-512BD4?style=flat-square" alt=".NET 10 LTS" />
  <img src="https://img.shields.io/badge/Avalonia-12.1.6-8B5CF6?style=flat-square" alt="Avalonia 12.1.6" />
  <img src="https://img.shields.io/badge/Platform-Windows_x64_%7C_macOS_(Preview)-0EA5B7?style=flat-square" alt="Platforms" />
</p>

<br />

<img src="docs/screenshots/02-lana-chat.png" alt="Plana · Lana AI Schedule Assistant" width="100%" />

</div>

---

## ✨ Meet Lana: Your Built-In AI Schedule Assistant

Lana lives inside Plana — lazy-loaded so the app stays featherweight until you open her, and ready to help plan whenever you do:

- **Natural-Language Scheduling & Breakdown**: Ask Lana to reschedule meetings, set exact reminder offsets, prioritize tasks, or break big goals into subtasks in plain language.
- **Multimodal & Document Understanding**: Drag or paste screenshots, PDFs, Word, Excel, PowerPoint, `.ics` calendars, HTML, or code/text files (reference them cleanly with `@`) to turn notes and schedules directly into actionable tasks.
- **Interactive & Granularly Undoable Cards**: Watch live tool progress while Lana inspects your schedule; tweak task priority or reminders directly inside the synced card, and undo **per item, multi-selected items, or the entire batch (`Ctrl+Z`)**.
- **Rules, Memory & Weekly Review**: Set daily task caps, high-priority limits, and rest-day preferences; inspect or edit your local profile and memories; and generate weekly productivity reviews.
- **20 Model Presets + Local Ollama**: Pre-configured presets for DeepSeek, Anthropic, OpenAI, Google Gemini, Kimi, Zhipu GLM, Qwen, OpenRouter, Ollama, and custom OpenAI/Anthropic-compatible endpoints — with API keys encrypted locally via DPAPI on Windows.
- **External AI via Local MCP**: Optionally enable the bundled local MCP server so Claude Desktop, Cursor, or Antigravity can safely read and update your Plana tasks with confirmation gates.

---

## 🖼️ Screenshots (v0.1.10)

### Plana Cherry-Pink Theme · Personalized Welcome Stage

Switch live between **Classic Blue** and **Plana Cherry Pink** with time-aware greetings, lunar date summary, and an expressive companion stage.

<img src="docs/screenshots/06-plana-theme-welcome.png" alt="Plana Cherry-Pink Theme & Lana Welcome Stage" width="100%" />

---

### Tasks Workspace & Detail Panel

Smart views (**Today**, **Next 7 Days**, **Inbox**, **Completed**, **30-day Trash**), custom lists, inline natural-language Chinese date parsing, `#tag` & recurrence filters, subtask tracking, and exact-time reminders.

<img src="docs/screenshots/01-tasks-main.png" alt="Plana Tasks Workspace & Task Detail Panel" width="100%" />

---

### Desktop Today & Month Widgets

Borderless transparent tool windows (hidden from taskbar and `Alt+Tab`) synced instantaneously with the main window — featuring 40%–100% opacity, free scaling, lock & click-through modes, and one-click overdue postponement.

<img src="docs/screenshots/07-desktop-widgets.png" alt="Plana Today and Month Desktop Widgets" width="100%" />

---

### Eisenhower Matrix

Maps task priorities automatically into four quadrants with drag-and-drop reprioritization and floating edit cards.

<img src="docs/screenshots/03-eisenhower.png" alt="Plana Eisenhower Matrix" width="100%" />

---

### Month / Week Calendar

Built-in offline 1901–2100 Chinese lunar dates, 24 solar terms, and holidays alongside drag-to-reschedule task chips and quick-create popovers.

<img src="docs/screenshots/04-calendar-month.png" alt="Plana Monthly Calendar View" width="100%" />

---

### Model Services (20 Official Presets + Custom Endpoints)

Paste an API key and test connectivity in seconds, with automatic vision/multimodal detection and multi-variant endpoint switching.

<img src="docs/screenshots/05-model-settings.png" alt="Plana Model Services Settings" width="100%" />

---

## 📦 Installation

Download the latest release binaries from **[GitHub Releases](https://github.com/s0ng1v/Plana/releases/latest)**:

- **Windows 10 / 11 (x64)**:
  - Run `Plana-Setup-0.1.10.exe` (user-level Inno Setup installer, no admin required; seamlessly upgrades while preserving `%LOCALAPPDATA%\VGList\`), or extract `Plana-win-x64-portable-0.1.10.zip` and launch `VGList.exe`.
- **macOS (Preview)**:
  - Download `Plana-mac-arm64-0.1.10.zip` (Apple Silicon) or `Plana-mac-x64-0.1.10.zip` (Intel).
  - Extract the archive and double-click (or right-click → Open) `首次运行.command` to clear quarantine attributes, apply ad-hoc signing, and launch `Plana.app`. *(Note: macOS preview builds are unsigned/unnotarized; full feature parity such as click-through widgets and native notification click-back is prioritized on Windows.)*

---

## 🛠️ Build from Source

Requires **.NET 10 SDK** (`10.0.x`).

```powershell
# Restore & build on Windows
dotnet restore VGList.slnx
dotnet build src/VGList.Desktop/VGList.Desktop.csproj -c Release -f net10.0-windows --no-restore

# Run unit tests
dotnet test tests/VGList.Tests/VGList.Tests.csproj -c Release

# Build Windows installer (requires Inno Setup 6)
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/build-installer.ps1 -Version 0.1.10
```

On macOS:

```bash
chmod +x scripts/build-macos.sh
bash scripts/build-macos.sh 0.1.10
```

---

## 📄 License & Third-Party Notices

- Source code is licensed under the **[MIT License](LICENSE)**.
- Bundled `Noto Sans SC` and `Noto Serif SC` fonts are distributed under the [SIL Open Font License 1.1](https://openfontlicense.org/).
- Bundled `Anthropic Sans / Serif / Mono` font files carry Anthropic PBC copyright and are included solely as embedded UI font assets rather than re-licensed under MIT.
- Third-party provider logos in `src/VGList.Desktop/Assets/Providers/` remain the trademarks of their respective owners and are used strictly for provider identification in Settings.
