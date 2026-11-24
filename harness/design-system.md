# 设计系统（Design Tokens）

状态：agreed v0.3（2026-11-19）
更新时间：2026-11-23
规则：所有数值以本文为准；实现里出现本文没有的颜色或尺寸即为缺陷。

## 1. 尺寸基准

| 对象 | 数值 |
|---|---|
| 今日小组件基准 | 331 × 454（100% 缩放） |
| 月历小组件基准 | 662 × 544 |
| 主窗口基准 | 1104 × 706（最小 1004 × 620） |
| 主窗口图标栏 | 64 |
| 主窗口清单栏 | 260 |
| 主窗口详情面板最小宽 | 320（与列表按 0.92 : 1 分配剩余宽度） |
| 主窗口标题栏 | 40（扩展进客户区） |
| 小组件缩放范围 | 80% – 200% |
| 页面外边距 | 16 |
| 列表左右内边距 | 16 |
| 任务行高 | 44 |
| 单行输入框高 | 34（最小），最大 114（5 行） |
| 优先级条高 | 40（滑块轨道） |
| 任务详情面板 | 505 × 429 |
| 标题行高 | 44 |
| 复选框 | 18 × 18，圆角 5，描边 1.5 |
| 日期 chip 高 | 24，左右内边距 8 |
| 圆角 | 小组件 16 / 卡片 12 / 输入框 10 / chip 与按钮 8 |

## 2. 间距

4 的倍数：4 / 8 / 12 / 16 / 24 / 32。常用 8 与 16。禁止出现 6、10、14 这类非 4 倍数（图标内部除外）。

## 3. 颜色令牌

### 深色（小组件 · Plana 默认主题）

| 令牌 | 值 | 用途 |
|---|---|---|
| `Bg.Widget` | `#201C27`（实色） | 小组件底色；不透明度由设置滑块控制 |
| `Bg.Surface` | `#2A2533` | 悬浮面板、菜单、弹层实色底 |
| `Bg.Window` | `#26212E` | 小组件二级容器底色 |
| `Bg.Hover` | `#FFFFFF` @ 9% | 行/按钮 hover |
| `Bg.Selected` | `#FFFFFF` @ 15% | 行/单元格选中 |
| `Bg.Input` | `#FFFFFF` @ 10% | 小组件输入框底色 |
| `Stroke.Widget` | `#FFFFFF` @ 12% | 小组件按钮与弹层细描边 |
| `Text.Primary` | `#EFE9F5` | 标题、任务名 |
| `Text.Secondary` | `#A99FB6` | 已完成、辅助说明 |
| `Text.Tertiary` | `#7A7088` | 占位符、禁用 |
| `Text.OnAccent` | `#FFFFFF` | 强调色底上的文字与对勾 |
| `Divider` | `#FFFFFF` @ 8% | 分隔线 |
| `Accent` | `#B49AF0` | 品牌紫、今天、聚焦边框默认 |
| `Accent.Muted` | `#B49AF0` @ 70% | 失焦后的默认边框；月历任务条底色 |
| `Accent.Soft` | `#B49AF0` @ 18% | 今日格子柔底、选中浅底 |
| `Calendar.Festival` | `#5FC7A2` | 月历节日、节气强调色 |
| `Month.Chip.Default` | `#B49AF0` @ 27% | 月历默认优先级任务条纯色底 |
| `Month.Chip.High` | `#E5533D` @ 28% | 月历高优先级任务条纯色底 |
| `Month.Chip.Medium` | `#E8A33D` @ 28% | 月历中优先级任务条纯色底 |
| `Month.Chip.Low` | `#3D7BE5` @ 28% | 月历低优先级任务条纯色底 |
| `Month.Chip.Done` | `#FFFFFF` @ 9% | 月历已完成任务条纯色底 |

### 主窗口浅色 · Plana（默认主题）

| 令牌 | 值 | 用途 |
|---|---|---|
| `Main.Bg` | `#FFFFFF` | 主窗口、列表、详情底色 |
| `Main.Surface.Subtle` | `#FCFAFF` | 四象限画布、日历星期头、设置底色 |
| `Main.Rail` | `#FAF6FE` | 左侧图标栏 |
| `Main.Sidebar` | `#FFFFFF` | 清单栏 |
| `Main.Hover` | `#F7F1FB` | 行 hover |
| `Main.Selected` | `#F0E8F9` | 行/导航选中 |
| `Main.Input` | `#F7F2FB` | 行内添加框、次要按钮底 |
| `Main.Divider` | `#EEE6F6` | 分栏、分组分隔 |
| `Main.Border.Subtle` | `#F4EDFA` | 卡片外框、日期格线 |
| `Main.Text.Primary` | `#2B2333` | 标题、任务名 |
| `Main.Text.Secondary` | `#786C86` | 元信息、分组标题 |
| `Main.Text.Tertiary` | `#A99DB8` | 占位符、计数 |
| `Main.Accent` | `#8F7BE5` | 今天、选中、日期 chip |
| `Main.Accent.Hover` | `#7A66D6` | 主操作按钮 hover |
| `Main.Accent.Soft` | `#F4EEFF` | 选中日期底、头像底、选中导航底 |
| `Main.Accent.Border` | `#DBCDF7` | 拖拽放置高亮描边 |
| `Main.Toggle.On` | `#A98BE8` | 设置开关开启态胶囊底色 |
| `Main.Calendar.TodayTint` | `#FAF5FF` | 日历页今天单元格微弱柔底 |
| `Main.Danger` | `#D64545` | 逾期日期、删除 |
| `Main.Danger.Soft` | `#FDECEF` | 删除按钮 hover |
| `Main.Toast.Bg` | `#332A3D` | 底部浮动撤销提示条底色 |
| `Main.Toast.Text` | `#FFFFFF` | 提示条正文 |
| `Main.Eisenhower.High.Soft` | `#FFF1EF` | 第一象限标签柔底 |
| `Main.Eisenhower.High.Border` | `#FBD6D0` | 第一象限标签描边 |
| `Main.Eisenhower.Medium.Soft` | `#FFF7EB` | 第二象限标签柔底 |
| `Main.Eisenhower.Medium.Border` | `#FBE1B8` | 第二象限标签描边 |
| `Main.Eisenhower.Low.Soft` | `#F2F4FF` | 第三象限标签柔底 |
| `Main.Eisenhower.Low.Border` | `#D5DCFF` | 第三象限标签描边 |
| `Main.Eisenhower.None.Soft` | `#F8F4FB` | 第四象限标签柔底 |
| `Main.Eisenhower.None.Border` | `#EBE2F3` | 第四象限标签描边 |

### 经典蓝白（ClassicBlue，可选主题）

设置页可切回原蓝白配色，下列令牌覆盖 Plana 值；未列出的令牌与 Plana 相同：

| 令牌 | 值 |
|---|---|
| `Main.Rail` | `#F5F6F8` |
| `Main.Surface.Subtle` | `#FAFBFC` |
| `Main.Hover` | `#F3F4F6` |
| `Main.Selected` | `#EDEFF2` |
| `Main.Input` | `#F5F6F8` |
| `Main.Divider` | `#E9EBEE` |
| `Main.Border.Subtle` | `#EFF1F4` |
| `Main.Text.Primary` | `#1E2329` |
| `Main.Text.Secondary` | `#6E7781` |
| `Main.Text.Tertiary` | `#A2A9B2` |
| `Main.Accent` | `#2F6BFF` |
| `Main.Accent.Hover` | `#1F5AE5` |
| `Main.Accent.Soft` | `#EAF1FF` |
| `Main.Accent.Border` | `#B8D0FF` |
| `Main.Toggle.On` | `#4B7BFF` |
| `Main.Toast.Bg` | `#1E2329` |
| `Bg.Widget` | `#1E1F21` |
| `Bg.Surface` | `#26272A` |
| `Bg.Window` | `#232427` |
| `Accent` | `#5C8DFF` |
| `Accent.Muted` | `#5C8DFF` @ 70% |
| `Accent.Soft` | `#5C8DFF` @ 15% |
| `Month.Chip.Default` | `#5C8DFF` @ 26% |
| `Calendar.Festival` | `#4FAE7A` |

> 两套主题都是「浅色主窗 + 深色小组件」，切换只替换颜色令牌；主窗口与小组件不得各自固定主题。

### 品牌资产（两套主题共用）

| 资产 | 路径 | 用途 |
|---|---|---|
| 官方图形 | `Assets/Plana/plana-mark.png`（512×512，透明底） | 左栏顶部徽标、窗口图标 |
| 官方字标 | `Assets/Plana/plana-wordmark.png`（1024 宽，透明底） | 左栏徽标下方 46px 宽字标 |
| 应用图标 | `Assets/Plana/plana.ico`（16–256 多尺寸） | exe 图标、任务栏、Alt+Tab |

- 徽标必须直接使用设计稿导出的官方图形资源，**禁止用矢量重绘、修改或再创作 Logo**；缩放时保持等比，不裁切、不变形。

### 浅色小组件（预留，未启用）

| 令牌 | 值 |
|---|---|
| `Bg.Widget` | `#FFFFFF` @ 92% |
| `Bg.Surface` | `#F5F6F8` |
| `Text.Primary` | `#1E2329` |
| `Text.Secondary` | `#66707A` |
| `Divider` | `#000000` @ 8% |

### 优先级与四象限（两套通用）

| 令牌 | 值 | 优先级 | 象限 |
|---|---|---|---|
| `Priority.High` | `#E5533D` | 高 | 重要且紧急 |
| `Priority.Medium` | `#E8A33D` | 中 | 重要不紧急 |
| `Priority.Low` | `#3D7BE5` | 低 | 不重要但紧急 |
| `Priority.None` | `#8A9099` | 无 | 不重要不紧急 |

`Priority.None` 在输入框聚焦时用 `Accent`，失焦时用 `Accent.Muted`。

## 4. 字体

| 层级 | 字号 / 字重 | 用途 |
|---|---|---|
| 主窗口页面标题 | 20 / SemiBold | 今天、最近7天、清单名 |
| 主窗口详情标题 | 20 / SemiBold | 任务详情的可编辑标题 |
| 组件标题 | 15 / SemiBold | 今天、2026年11月 |
| 侧栏与列表项 | 14 / Regular | 清单栏、任务标题、详情正文 |
| 输入文字 | 14 / Regular | 输入框 |
| 日期、chip | 12 / Regular | 今天、月历农历 |
| 辅助与计数 | 11–12 / Regular | 已完成 (3)、清单名、分组计数 |

字体族：内嵌 `Anthropic Sans` / `Anthropic Serif` / `Anthropic Mono` + 内嵌 `Noto Sans SC` / `Noto Serif SC`（分为 `fonts:Anthropic`、`fonts:NotoSansSC`、`fonts:NotoSerifSC` 三个独立集合并在字体链中以 `fonts:<Key>#<Family>` 显式声明，确保未装系统思源的机器也能准确命中对应宋体/黑体回退而不串扰或直落系统字体，ADR-0071），系统字体（Segoe UI、微软雅黑、Segoe UI Emoji 等）仅作尾部回退；「系统」预设按定义使用系统字体。主窗口与小组件窗口统一继承 `Font.Ui`（ADR-0063）。中文思源可变字体的字重轴已重定基为 400..900、默认 400，避免正文落到 100/200 的极细默认实例（ADR-0067）。

## 5. 材质与层级

- 小组件背景：**不使用 DWM 亚克力/云母**；`Backdrop` 层为 `Bg.Widget` 实色，窗口透明，设置滑块 40–100% 只调该层 `Opacity`，降低时真实透出桌面，文字、边框、弹层始终 100% 不透明；无边框，圆角 16/10，1px 内描边 `#FFFFFF` @ 8%。
- 悬浮面板（优先级条、菜单、日期弹窗）：`Bg.Surface` 97% + 圆角 12 + 阴影 `0 8 24 rgba(0,0,0,0.28)`。
- 阴影只用于浮层，不用于列表行。

## 6. 动效

| 场景 | 时长 | 曲线 |
|---|---|---|
| 优先级条展开/收起 | 160ms | `cubic-bezier(0.2, 0, 0, 1)` |
| 输入框高度变化 | 120ms | 同上 |
| 复选框勾选 | 120ms | `ease-out` |
| 任务完成移入已完成分组 | 150ms | `ease-out` |
| 页面切换 / 详情面板进入 | 180ms | `ease-out`（淡入 + 8–12px 轻位移） |
| 侧栏折叠与展开 | 180ms | `ease-out`（宽度 + 透明度） |
| 悬浮菜单 / Toast 出现 | 160ms | `ease-out`（淡入 + 4–8px 位移） |
| 折叠分组箭头旋转 | 150ms | `ease-out` |
| 品牌 Plana 徽标 | 无 | 官方 PNG 图形 + 字标，静态，无流光/扫光；点击返回任务页 |
| 侧栏与对话页 Lana 头像 | 五态常驻 + 舞台转场 | `LanaAvatarControl` Skia 自绘：呼吸/漂浮/眨眼、全窗鼠标视线跟随、悬停唤醒、点击回弹、Thinking 环绕流光、Happy 2s 庆祝（ADR-0066）；对话页唯一活体 Lana 在欢迎居中（108）↔ 回复左侧（42）间平滑飞行（520ms/440ms），历史轮次留静态缓存静影（`Opacity=0.82`，ADR-0068） |

原则：动画只做位移、透明度、高度；不做缩放弹跳；尊重系统"减少动画"设置。**唯一例外**：左侧导航栏与对话页舞台的 `LanaAvatarControl` 允许轻微缩放/挤压的果冻回弹、全窗眼神跟随与首轮/新会话尺寸过渡，且「减少动效」开启时自动退化为静态帧与即时落位。

## 7. 图标

- 统一 16 × 16 或 20 × 20 线性图标，描边 1.5。
- 四象限使用 `!` 徽标 + 语义色；优先级一律使用圆形（空心圆停靠点 + 实心圆滑块），不再使用旗形。
- 图标一律自绘矢量 Path，不依赖第三方图标字体。


## 8. 助理阶段新增令牌与资产（2026-11-21，ADR-0043 / ADR-0044）

### 新增品牌与 IP 资产

| 资产 | 路径 | 用途 |
|---|---|---|
| 宠物 IP | `Assets/Plana/plana-pet.png`（512×512，透明底，去水印） | 关闭确认卡徽标（左栏入口与对话页欢迎主视觉、消息头像、侧栏底标、拖拽层均已由 `LanaAvatarControl` 自绘统一替代，ADR-0066 / ADR-0068） |
| 厂商官方 Logo | `Assets/Providers/<provider-id>.png`（20 家厂商真实品牌图标） | 设置页「模型服务」子页卡片、配置抽屉顶栏与助理页模型胶囊 |

- 直接使用真实图形资源，禁止手绘近似矢量或魔改。

### Plana 淡粉主题与模型服务（`Provider.*`）/ 对话页面（`Assistant.*`）颜色令牌（ADR-0045）

| 令牌 | Plana 淡粉值（默认） | ClassicBlue 值 | 用途 |
|---|---|---|---|
| `Main.Accent` / `Hover` | `#E46B92` / `#D45780` | `#2F6BFF` / `#1F5AE5` | 主强调色（柔和樱粉） |
| `Main.Accent.Soft` / `Border` | `#FDECF2` / `#F6C6D6` | `#EAF1FF` / `#B8D0FF` | 选中柔和粉底与细描边 |
| `Main.Rail` / `Surface.Subtle` | `#FEF5F8` / `#FFF9FB` | `#F5F6F8` / `#FAFBFC` | 极浅樱雾导航栏与次级底 |
| `Provider.Card.Bg` | `#FFFFFF` | `#FFFFFF` | 厂商卡片底色 |
| `Provider.Card.Border` | `#F3E4EA` | `#E6E8EC` | 厂商卡片描边 |
| `Provider.Card.HoverBorder` | `#F2B9CC` | `#B8D0FF` | 厂商卡片悬停描边 |
| `Provider.Badge.Bg` | `#EAF8F1` | `#EAF8F1` | 「已配置 / 已连接」胶囊柔底 |
| `Provider.Badge.Fg` | `#1F8A5B` | `#1F8A5B` | 「已配置 / 已连接」文字与状态点 |
| `Provider.Warn.Bg` | `#FFF6E8` | `#FFF6E8` | 「未配置 / 需填写地址」提醒柔底 |
| `Provider.Warn.Fg` | `#C27613` | `#C27613` | 提醒文字与状态点 |
| `Provider.Scrim` | `#38291820` | `#3D111827` | 窗内右侧抽屉背景遮罩 |
| `Assistant.Canvas.Bg` | `#FFFDFE` | `#FAFBFC` | 对话页面柔和瓷白画布底 |
| `Assistant.Sidebar.Bg` | `#FEF5F8` | `#F5F6F8` | 对话页面可折叠会话栏底 |
| `Assistant.UserBubble.Bg` | `#FDEEF4` | `#EAF1FF` | 用户消息淡粉气泡柔底 |
| `Assistant.UserBubble.Border` | `#F8D4E2` | `#CFE0FF` | 用户消息气泡细描边 |
| `Assistant.AiCard.Bg` | `#FFFFFF` | `#FFFFFF` | 助理消息/提案卡片实色底 |
| `Assistant.Composer.Bg` | `#FFFFFF` | `#FFFFFF` | 底部浮动胶囊输入框底 |
| `Assistant.Composer.Border` | `#F3DCE5` | `#DFE3E8` | 输入框默认描边 |
| `Assistant.Composer.FocusBorder` | `#E46B92` | `#2F6BFF` | 输入框聚焦描边 |
| `Assistant.ToolChip.Bg` | `#FEF4F8` | `#F2F4F7` | 工具调用/思考状态胶囊底 |
| `Assistant.PetHalo` | `#36F8A8C8` | `#265C8DFF` | 左侧宠物 IP 按钮与主视觉柔粉光晕 |
| `Assistant.Proposal.Add.Bg` | `#ECFBF3` | `#ECFBF3` | 提案「新增」标签柔底 |
| `Assistant.Proposal.Add.Fg` | `#1F8A5B` | `#1F8A5B` | 提案「新增」标签前景色 |
| `Assistant.Proposal.Move.Bg` | `#FFF6E8` | `#FFF6E8` | 提案「改期」标签柔底 |
| `Assistant.Proposal.Move.Fg` | `#C27613` | `#C27613` | 提案「改期」标签前景色 |
| `Assistant.Proposal.Edit.Bg` | `#FDEEF4` | `#EAF1FF` | 提案「修改」标签柔底 |
| `Assistant.Proposal.Edit.Fg` | `#D45780` | `#2F6BFF` | 提案「修改」标签前景色 |

### 对话页渐入与 SSE 流式文字动效规范（ADR-0045）

- 欢迎主视觉、2×2 快捷卡片、消息气泡、工具进度胶囊、思考波浪、变更提案卡片、底部微操作行、草稿附件芯片与悬浮回底胶囊均采用 160–240ms `CubicEaseOut` 透明渐入 + 轻微纵向位移（4–8px），禁止瞬间硬切。
- SSE 增量文字输出采用尾部片段 200ms alpha 渐入（`0.12 → 1.0`），不改变文字字距与换行排版，无抖动；「减少动效」开启时全部立即呈现。

> **数据色例外说明**：厂商目录（`providers.json`）中的 `color` 字段仅用于自定义服务与缺失图标时的回退徽标，与自建清单圆点 `TaskList.Color` 同属数据色，不属于硬编码 UI 主题色。
