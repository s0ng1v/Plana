# Harness · VGList 共识文档体系

状态：agreed（2026-11-17）
作用域：整个仓库
唯一原则：**代码服从文档，文档服从用户。**

## 1. 这个目录是什么

harness 是人与 Agent 之间的唯一共识来源。任何范围、视觉、交互、技术决定，先落在这里，再落进代码。
聊天里的口头结论、代码注释里的"约定"、某个人的记忆，一律不算共识。

## 2. 文档分级

| 级别 | 文件 | 变更要求 |
|---|---|---|
| L0 铁律 | [constitution.md](./constitution.md) | 需用户明确同意，只增不改语义 |
| L1 共识 | [product.md](./product.md)、[architecture.md](./architecture.md)、[design-system.md](./design-system.md)、[blueprint.md](./blueprint.md) | 需用户确认后修改 |
| L2 规格 | [features/](./features/) 下的单页规格 | 实现前写，验收后定稿 |
| L3 记录 | [decisions.md](./decisions.md) | 只追加，不改历史 |

## 3. 索引

| 文档 | 回答什么问题 | 什么时候读 |
|---|---|---|
| [constitution.md](./constitution.md) | 哪些事绝对不能做 | 每次开工前 |
| [product.md](./product.md) | 做什么、不做什么、先做哪个 | 排期与拆任务前 |
| [architecture.md](./architecture.md) | 工程怎么分层、窗口怎么管、数据契约长什么样 | 写任何代码前 |
| [design-system.md](./design-system.md) | 尺寸、颜色、字号、圆角、动效的准确值 | 写任何 UI 前 |
| [features/today-widget.md](./features/today-widget.md) | 今日日程小组件的逐项规格 | 做该页时 |
| [features/month-widget.md](./features/month-widget.md) | 月历小组件的逐项规格 | 做该页时 |
| [features/main-window.md](./features/main-window.md) | 主窗口壳层与任务页的逐项规格 | 做该页时 |
| [features/eisenhower.md](./features/eisenhower.md) | 四象限页的逐项规格 | 做该页时 |
| [features/calendar.md](./features/calendar.md) | 日历页（月/周视图与拖拽改期）的逐项规格 | 做该页时 |
| [features/settings.md](./features/settings.md) | 设置页（外观/小组件/通用）的逐项规格 | 做该页时 |
| [features/assistant.md](./features/assistant.md) | 助理阶段（日期识别 / 助理面板 / 规则 / 记忆 / 外部 AI）的用户视角规格 | 做助理相关功能时 |
| [features/model-settings.md](./features/model-settings.md) | 设置页「模型服务」（厂商预设 + 自定义）规格 | 做模型 API 配置时 |
| [workflow.md](./workflow.md) | 一页怎么从开始做到验收 | 每页开工前 |
| [glossary.md](./glossary.md) | 词是什么意思 | 有歧义时 |
| [decisions.md](./decisions.md) | 每个决定为什么这么定 | 想推翻某个决定时 |
| [blueprint.md](./blueprint.md) | 竞品调研与整体蓝图（长文） | 需要背景时 |

## 4. 写入规则

1. 先改文档，再改代码。代码与文档冲突时以文档为准，立即补代码或回滚代码。
2. 每个决定写三段：结论 / 理由 / 影响面。
3. 不做文档里没有的功能。想加功能，先写进 product.md 并标注 P1/P2。
4. 每份文档头部必须有 `状态` 与 `更新时间`。
5. 术语统一走 glossary.md，禁止同义词混用（尤其"清单 / 收集箱 / 列表"）。
6. 需要参考图时放 `harness/assets/`，命名 `页面-状态@DPI.png`。

## 5. 与 Trellis 的分工

- harness：是什么、为什么、做成什么样（共识）。
- `.trellis/`：怎么做、任务拆解、实现规范与日志（流程）。
- Trellis 的 spec 可以引用 harness，但不得覆盖 harness 的结论；冲突时以 harness 为准。

## 6. 当前阶段

P0 页面阶段（前端先行，交互真做、数据假做）：正在完善月历小组件交互，并重构主窗口 UI 与补齐四象限、日历、设置三个 P0 页面。
