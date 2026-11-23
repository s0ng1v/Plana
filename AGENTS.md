<!-- TRELLIS:START -->
# Trellis Instructions

These instructions are for AI assistants working in this project.

This project is managed by Trellis. The working knowledge you need lives under `.trellis/`:

- `.trellis/workflow.md` — development phases, when to create tasks, skill routing
- `.trellis/spec/` — package- and layer-scoped coding guidelines (read before writing code in a given layer)
- `.trellis/workspace/` — per-developer journals and session traces
- `.trellis/tasks/` — active and archived tasks (PRDs, research, jsonl context)

If a Trellis command is available on your platform (e.g. `/trellis:finish-work`, `/trellis:continue`), prefer it over manual steps. Not every platform exposes every command.

If you're using Codex or another agent-capable tool, additional project-scoped helpers may live in:
- `.agents/skills/` — reusable Trellis skills
- `.codex/agents/` — optional custom subagents

Managed by Trellis. Edits outside this block are preserved; edits inside may be overwritten by a future `trellis update`.

<!-- TRELLIS:END -->

<!-- HARNESS:START -->
# Harness：Agent 共识文档

本项目的产品、设计、架构与验收共识全部存放在 `harness/`，这是唯一事实来源。任何写代码之前：

1. 先读 `harness/README.md`（索引与写入规则）。
2. 再读 `harness/constitution.md`（不可协商的工程铁律）。
3. 涉及 UI 必读 `harness/design-system.md`；做当前页面必读 `harness/features/*.md`。
4. 文档与代码冲突时以文档为准；要改共识，先改 harness 再改代码，并在 `harness/decisions.md` 追加记录。
5. 不得实现 harness 中未列入 P0 的功能。

当前阶段：P0 页面阶段（前端先行，交互真做、内存假数据）。
已完成：主程序主窗口壳层 + 任务页（三栏、详情、日期/清单弹层、占位页，见 `harness/features/main-window.md`）、今日小组件、月历小组件；三者共享同一仓储并实时同步。四象限页已完成，点击任务行改为侧向悬浮编辑卡（见 `harness/features/eisenhower.md`）；小组件已工具窗口化，不再出现在任务栏与 Alt+Tab。左侧导航 Lana 入口已改为 Skia 五态动效头像（ADR-0066）。
助理阶段已立项（ADR-0042/0043，见 `harness/features/assistant.md`、`harness/features/model-settings.md`，Trellis 父任务 `*-plana-assistant`）：P0 收口后开工；性能分两档，助理未打开时必须保持原有轻量；助理写入一律「提案→应用→可撤销」。
下一个动作：主窗口剩余验收项（删除、已完成视图、日历/设置占位页、关闭主窗口联动、100%/125%/150% DPI 截图）→ 再进入下一个 P0 页面。
<!-- HARNESS:END -->
