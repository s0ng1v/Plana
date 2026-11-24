# 决策记录（ADR Log）

状态：agreed
规则：只追加，不修改历史。推翻旧决定时新增一条并注明 `Supersedes: ADR-xxxx`。

---

## ADR-0001 技术栈：Avalonia 11 + .NET 10 LTS

- 日期：2026-11-17
- 状态：accepted
- 结论：桌面端使用 Avalonia（C# / XAML），运行时 .NET 10 LTS。（实际落地版本 12.1.6，见 ADR-0011）
- 理由：原生多窗口与无边框窗口是 P0 小组件的前提；内存与启动接近原生软件；不做 WebView 壳。
- 影响：本机需安装 .NET SDK；团队需具备 C#/XAML 能力。移动端不复用 UI，仅复用数据模型与同步协议。

## ADR-0002 视觉精度：小组件像素级，主窗结构级

- 日期：2026-11-17
- 状态：accepted
- 结论：小组件按参考截图 1:1 对齐；主窗口对齐结构与信息层级，视觉自有。
- 理由：小组件是差异化的第一触点，值得逐像素打磨；主窗口逐像素复刻会限制体验改进并增加返工。
- 影响：验收标准分裂为两套；主窗口需要独立的设计评审。

## ADR-0003 前端先行：交互真做、数据假做

- 日期：2026-11-17
- 状态：accepted
- 结论：P0 只做页面与交互，数据走内存假数据，重启重置；不接 SQLite。
- 理由：模型还在变，早接库会反复写迁移；先锁定体验再固化数据。
- 影响：禁止在页面阶段写持久化代码；仓储接口先行，后端接入时只换实现。

## ADR-0004 优先级快捷条：输入框上方、选完保持展开

- 日期：2026-11-17
- 状态：accepted
- 结论：输入框聚焦时在其上方展开四旗优先级条；选完不收起，失焦/ESC/点外部才收；条悬浮覆盖标题行。
- 理由：把优先级从二级菜单提到一级，减少一次点击；覆盖标题的代价小于列表位移的跳动。
- 影响：录入期间看不到「今天 ▾」；标题行需允许被覆盖。

## ADR-0005 输入框边框色 = 当前优先级

- 日期：2026-11-17
- 状态：accepted
- 结论：边框随优先级变色（无=品牌蓝/灰、低=蓝、中=黄、高=红）；失焦保留但降饱和；只改边框。
- 理由：让"这一条会被打成什么优先级"在提交前可见，减少误标。
- 影响：无优先级与低优先级色相接近，需靠旗形图标与饱和度区分。

## ADR-0006 未完成复选框描边色 = 优先级

- 日期：2026-11-17
- 状态：accepted
- 结论：任务行复选框描边色跟随优先级（红/黄/蓝/灰）；已完成统一灰色填充 + 白勾 + 删除线。
- 理由：截图观察；与输入框边框形成同一套颜色语言。
- 影响：P1 引入标签色时不得覆盖复选框颜色。

## ADR-0007 小组件窗口层级：默认贴桌面

- 日期：2026-11-17
- 状态：accepted
- 结论：默认 WorkerW 贴桌面，设置可切始终置顶；挂载失败保持**普通窗口**（不再降级为置顶，也不再强制 `HWND_BOTTOM`，避免沉到桌面壁纸之下）。
- 理由：符合"桌面小组件"的心智；普通窗口被其他应用激活时自然被盖住，且始终可见于桌面。
- 影响：需 Win32 互操作与降级提示；不同 Windows 版本行为需实测。

## ADR-0008 页面施工顺序

- 日期：2026-11-17
- 状态：accepted
- 结论：今日小组件 → 月历小组件 → 任务页 → 四象限 → 日历 → 设置。
- 理由：先验证最难的窗口/材质/输入体验；基础组件（TaskRow/Checkbox/Detail）在任务页沉淀；日历最复杂放最后。
- 影响：日历页之前不得开工；设置页承接前面所有开关。

## ADR-0009 输入框最高 5 行

- 日期：2026-11-17
- 状态：accepted
- 结论：单行 40px 起，自适应增高，最多 5 行（120px），超出框内滚动；日期 chip 多行时沉到右下角。
- 理由：兼顾长文本可见性与任务列表可用空间。
- 影响：小组件缩放到 80% 或 200% 时按比例换算，最大高度不超过窗口高度 30%。

## ADR-0010 提交规则

- 日期：2026-11-17
- 状态：accepted
- 结论：Enter 提交、Shift+Enter 换行；提交后清空、保持焦点、优先级归无、日期沿用当前视图。
- 理由：支持连续录入，同时避免优先级被无意继承。
- 影响：连续录入同类高优任务需重新点一次旗子。

## ADR-0011 实际使用 Avalonia 12.1.6

- 日期：2026-11-17
- 状态：accepted
- 结论：`Avalonia` 实际版本为 **12.1.6**（模板同为 12.1.6），不是文档原先写的 11。
- 理由：NuGet 当前稳定版即 12.1.6，无理由钉在旧大版本。
- 影响：API 与 11 有差异——`WindowDecorations` 取代 `SystemDecorations`、`PlaceholderText` 取代 `Watermark`、`GotFocusEventArgs` 不可用于事件签名；`TextBox` 在 `OnKeyDown` 内先行消费 Enter，提交必须用隧道阶段（`RoutingStrategies.Tunnel`）拦截。

## ADR-0012 亚克力：Transparent + DWMWA_SYSTEMBACKDROP_TYPE

- 日期：2026-11-17
- 状态：accepted
- 结论：`TransparencyLevelHint="Transparent"`；打开窗口后先 `DwmExtendFrameIntoClientArea(-1)` 把边框延伸进客户区，再 `DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE=38, DWMSBT_TRANSIENTWINDOW=3)` 打开系统亚克力；不再使用 `AcrylicBlur` 提示（Win11 上静默失效，呈近纯黑）。
- 理由：`AcrylicBlur` 在 Avalonia 12 下窗口背景合成为近纯黑，模糊不生效；系统背景材质走 DWM 才与 Win11 原生观感一致。
- 影响：仅 Win11 22H2+ 有效；旧系统自动退化为 85% 底色卡片，不再做额外兜底。

## ADR-0013 输入框行高与最大高度

- 日期：2026-11-17
- 状态：accepted
- 结论：行高 20px，内边距上下各 10px；高度 = 行数 × 20 + 20，夹在 40–120 之间（40/60/80/100/120）。原规格「每行 +16、两行 56」作废。
- 理由：+16 与「5 行 = 120」自相矛盾（40 + 4×16 = 104）；按实际字体行高 20px 推算才能同时满足 40 起、5 行封顶 120。
- 影响：`features/today-widget.md` §5 与验收清单已同步为 60。

## ADR-0014 录入区重排、优先级滑块与任务详情面板

- 日期：2026-11-17
- 状态：accepted
- 结论：
  1. 输入框去掉 `+`，单行高度由 40 降为 **34**（行高 20 + 上下内边距 7），最大 5 行 114；日期 chip 从输入框右侧移到**文字下方**的独立操作行。
  2. 优先级条由四个旗子按钮改为**滑块**：轨道 4 个圆点，18px 实心圆滑块，点击任意位置滑到最近档位；优先级图标统一用圆形，不再使用旗子。
  3. 日历按滴答清单样式重做：保留「今天/明天」快捷行在月历上方，周起始改为**周日**，42 格，选中日为蓝色实心圆；点日期只选中，`确定` 才应用，`清除` 回今天。
  4. 任务行只有复选框可勾选；点标题打开 505 × 429 的**任务详情面板**（标题 + Markdown 内容 + `/` 快捷插入菜单）。
  5. 编辑态只由**点击输入框区域**触发；点卡片其他区域立即收回滑块与弹窗，窗口重新激活不再自动聚焦输入框。
  6. 任务行与已完成分组 hover 覆盖整行（强制 `ContentPresenter` 横向拉伸），内部左右内边距 8。
- 理由：用户实测反馈当前的 `+`、右侧日期、旗子按钮、误聚焦与偏短的 hover 与滴答清单差异明显，且缺任务详情入口。
- 影响：`features/today-widget.md` 全文同步为 v0.2；`design-system.md` 输入框高度与优先级图标说明同步；旧 ADR-0004/0009 中「四旗」「40 起、日期沉到右下角」的描述由本 ADR 取代。
- Supersedes: ADR-0004（优先级条形态）、ADR-0009（输入框高度与 chip 位置）

## ADR-0015 日期 chip 内联、hover 分区、完成动画与侧边详情面板

- 日期：2026-11-17
- 状态：accepted
- 结论：
  1. 日期 chip 默认停在第 1 行右侧（与首行文字同一行）；用 `TextLayout` 按「录入区全宽 − chip 宽 − 6」测量，首行放不下时才移到第 2 行并保持右对齐；文字删短后自动回到第 1 行。
  2. 任务行保持**整行 hover**；复选框额外拥有独立 hover（仅包裹 18×18 框：填充 `Bg.Selected` + 放大 1.12 倍），与文字 hover 分离。
  3. 勾选未完成任务时先播 0.38s 动画：填充复选框 + 弹出白勾 → 标题划线 → 整行高度收 0、右移淡出；动画结束才 `SetCompleted` 写回仓储。已完成行点勾直接恢复。
  4. 详情面板改为侧边弹窗：`PointToScreen` 取小组件屏幕边缘，右侧优先、左侧兜底、夹在所属屏幕工作区内，纵向与小组件顶部对齐；`Topmost` 跟随宿主，保证在小组件之上。
  5. 桌面层级：WorkerW 挂载失败时保持普通窗口，依靠系统 Z 序被其他应用盖住；移除 `SetWindowPos(HWND_BOTTOM)` 降级（会把窗口压到桌面壁纸之下导致不可见）。
- 理由：用户实测反馈 chip 位置、详情面板遮挡、hover 联动与"点其他应用后小组件仍在最上层"四个问题。
- 影响：`features/today-widget.md` 升为 v0.3；`WidgetHost` 与 `WindowEffects` 去掉沉底降级；旧设置文件中的 `Layer=AlwaysOnTop` 需改为 `Desktop` 才能看到桌面层级。
- Supersedes: ADR-0014 第 6 条（整行 hover）、ADR-0007（降级为置顶）

## ADR-0016 视图切换、设置面板、细分交互与视觉校准

- 日期：2026-11-17
- 状态：accepted
- 结论：
  1. 左上角标题可点击，下拉选择 所有 / 今天 / 明天 / 最近7天；标题随视图变。`所有` 视图按日期分组，顺序为今天 → 明天 → 之后日期升序，已完成仍在最下方。
  2. 右上 `⋯` 改为 32×32 按钮，三点水平居中，与标题同高；菜单含 `设置`，设置面板提供主题、不透明度（40–100%，带百分比）、分组/排序/顺序、置于顶层、显示添加输入框、显示已完成、日期显示为；其中不透明度、置顶、显示输入框、显示已完成即时生效并写入 settings.json。
  3. 日期 chip 常驻（不再只在编辑态），去掉下拉箭头；文案 今天/明天/后天，更远显示 `M月d日`；点击 chip 任意位置展开日历。选择日期后输入框保持展开并恢复焦点。
  4. 输入框单行高度 34 → **32**；品牌蓝提亮为 `#5C8DFF`；优先级=无时聚焦描边为灰，退出编辑后恢复蓝色，颜色/粗细均带 180ms 过渡。
  5. 优先级改由 **Tab / Shift+Tab 循环**，取消 1–4 数字键拦截（数字必须能正常输入）；点击弹层任意位置不再收起输入框；四个停靠点 hover 时放大并填充对应柔色，滑块位移 260ms `CircularEaseOut`。
  6. 完成划线改为**从左到右生长**（1.2px 覆盖线，`scaleX 0→1`，200ms），不再用无法动画的 `TextDecorations`；其余勾选/吸入动画保留。
  7. 滚动条改为宽 12 轨道内嵌 6px 圆角半透明灰条（`Scroll.Thumb`），与内容留空隙，不再使用贴边的 1px 蓝线。
  8. 任务详情面板 505×429 → **440×330**；配色改中性深灰（`Bg.Widget`/`Bg.Surface` 去蓝调）；斜杠菜单出现在光标右侧（`caret.Right + 6`），菜单打开期间不因窗口失活而关闭详情面板。
  9. 间距：卡片左右边距 16，标题→输入框 10，输入框→列表 12，组标题上边距 6–8，已完成分组再上移 8。
- 理由：用户逐项实测反馈：选完优先级弹层消失、滑块无 hover、无法输入数字、chip 只在编辑态、蓝边框偏暗、详情面板过大且偏蓝、斜杠菜单位置错误、划线无动画、滚动条贴边、缺少视图切换与设置。
- 影响：`features/today-widget.md` 升为 v0.4；`WidgetSettings` 新增 `Opacity`/`IsTopmost`/`ShowInput`/`ShowCompleted`；`TodayWidgetViewModel` 新增 `ActiveView`/`Groups`/`ShowCompleted`。
- Supersedes: ADR-0015 第 1 条（chip 仅编辑态）与第 3 条（划线方式）、ADR-0014 第 2 条（优先级键盘操作）

## ADR-0017 实色弹层、仅背景透明、独立设置窗口与覆盖式滚动条

- 日期：2026-11-17
- 状态：accepted
- 结论：
  1. 所有弹层（视图菜单、更多菜单、日历、优先级条、设置、详情、斜杠菜单）一律 **100% 实色** `Bg.Surface`（`#FF26272A`），不再透出桌面内容。
  2. 不透明度滑块**只作用于背景层**：卡片根节点改透明，内部新增独立 `Backdrop` Border 承载背景色，滑动只改它的 `Opacity`；文字、边框、弹层永不跟随变透明。
  3. 设置从 Popup 改为**独立无边框窗口**（352 × 560，实色、圆角、阴影、置顶、不占任务栏），打开时贴在小组件左侧、纵向顶部对齐；标题栏整条可拖动，× 或 `Esc` 关闭。滑块实时预览、释放落盘，开关即时落盘。
  4. 字体整体 +1：标题 17、任务标题 15、任务日期 13、分组标题 14、菜单项 14、日期 chip 13、日历正文 13、详情标题 18、详情正文 14。
  5. 输入框高度 32 → **38**（内边距上下 9、圆角 11），1–5 行高度为 38/58/78/98/118；宽度随卡片左右边距 16 → **20** 再收窄。
  6. 任务行高 44 → 46；行模板改为单一 Grid（复选框 / 标题 / 日期），三者垂直居中且标题与日期同为 20px 行高；行内容左右各留 6，配合卡片 20 边距，使左右留白对称。
  7. 滚动条改为**覆盖层**：ScrollViewer 自身滚动条隐藏，右侧叠加 14px 透明轨道与 6px 圆角滑块，不占布局宽度；展开已完成时内容不再左挤；支持拖动、点击跳转与轨道滚轮。
- 理由：用户实测反馈弹层过透、不透明度把整卡变透明、设置难看且不能拖动、输入框扁平、字体偏小、左右间距不一致、展开后内容被滚动条挤窄。
- 影响：`features/today-widget.md` 升为 v0.5；新增 `WidgetSettingsWindow`；`TodayWidgetWindow` 移除设置 Popup；`Tokens.axaml` 的 `Bg.Surface` 改为不透明并新增 `Bg.Window`；`TodayWidget.axaml` 删除内置 ScrollBar 与设置面板样式，新增 `scroll-thumb`。
- Supersedes: ADR-0016 第 2 条（设置面板形态）与第 9 条（左右 16 边距）、ADR-0016 第 7 条（内置滚动条）

## ADR-0018 去外描边、窗口自由缩放（字体不缩放）与浅色设置窗口

- 日期：2026-11-17
- 状态：accepted
- 结论：
  1. 卡片不再有 `BorderBrush`/`BorderThickness`，去掉外圈白色描边；圆角外完全透明。
  2. 移除 `Viewbox` 整体缩放方案：窗口改为 `CanResize=True`，默认 331 × 454，**最小 300 × 390（参考图 2）、最大 662 × 908**；缩放只重排版式，**字号固定不缩放**。
  3. 窗口四边与四角共 8 个透明热区（边 6px、角 14px），按下调用原生 `BeginResizeDrag(WindowEdge, e)`，不再只能拖右下角。
  4. 尺寸记忆：`WidgetSettings.Scale` 删除，改为 `Width`/`Height`（空则用默认值），`WidgetHost` 监听 `SizeChanged` 写回并限制在 Min/Max 内。
  5. 优先级轨道由硬编码 272 宽改为 `Stretch` + 左右 34 边距；滑块位置与点击档位按轨道实际宽度计算，窗口缩放后自动重算。
  6. 设置窗口按参考图 1:1 复刻：**552 × 760、浅色白底**（`RequestedThemeVariant=Light`）、圆角 14、标题「配置桌面小部件」、三个灰卡片分组、无分隔线、行高 56、值后下箭头、蓝色滑块与 50 × 28 胶囊开关；删除百分比文本。
- 理由：用户实测反馈外围白线突兀、窗口缩小字体跟着缩小、缺少最小尺寸约束、设置界面与参考图差距大、只能拖右下角缩放。
- 影响：`features/today-widget.md` 升为 v0.6；新增 `Settings.*` 浅色令牌；`WidgetSettings` 用 `Width`/`Height` 取代 `Scale`；`TodayWidgetWindow` 移除 `Viewbox`、新增八向热区；`WidgetSettingsWindow` 整体重写。
- Supersedes: ADR-0017 第 3 条（设置窗口尺寸与深色外观）

## ADR-0019 任务行压紧到 40

- 日期：2026-11-17
- 状态：accepted
- 结论：任务行高 46 → **40**，与参考稿（输入框 48 : 行距 50 ≈ 1.04）比例一致；进行中与已完成行共用。输入框与列表间距 12 保持不变，行内文字与复选框仍垂直居中。
- 理由：用户用平行线对比参考稿与实现，指出每行任务间距偏大。
- 影响：`features/today-widget.md` §1 / §8 行高改为 40；`TodayWidget.axaml` 的 `Border.taskrow` 高度改为 40；勾选吸入动画不变。

## ADR-0021 设置窗口瘦身、输入框去描边与占位着色

- 日期：2026-11-17
- 状态：accepted（Supersedes：ADR-0005 的「无优先级=品牌蓝描边」、ADR-0020 的「占位三态从按Tab开始」）
- 结论：
  1. 设置窗口由 552 × 760 缩到 **440 × 600**（用户反馈「太大了」，比参考稿再小一档）；行高 56→42、字号 15→13、圆角 10→8、开关 50 × 28→**40 × 22**、滑块宽 188→140、轨道 4→3、小球 14→**11**。
  2. 输入框**空闲态完全无描边**（不再有品牌蓝/灰蓝描边）；点击进入编辑态即**默认低优先级**，边框直接显示 `Priority.Low`。
  3. 占位「添加任务」提亮为 `Text.Secondary`（去掉 50% 透明衰减）；编辑态占位显示当前优先级，且**文字色=优先级色**。
  4. 日期 chip **仅编辑态渐入渐出**（160ms），空闲态隐藏且不响应点击。
  5. 卡片「更多」三点直径 3.5→3，间距 3→2.5；「设置」入口图标改为描边齿轮（16×16，1.2px）。
  6. 按过 Tab 后循环回「无」时，占位显示「无优先级」并用灰色 `Priority.None`（不再退回提示语）。
- 理由：用户逐条以截图对照；空闲态描边抢视觉且与「边框=优先级」的语义冲突；默认低优先级让录入路径更短；chip 常驻在空闲态没有意义。
- 影响：**本次编辑第一次按 Tab 直接到「高」**（默认边框仍是低优先级），之后按 高→中→低→无 循环；**点击只改边框，占位仍是「按Tab切换优先级」，按过 Tab 才显示优先级名**；提交后优先级回到「低」并重置提示态；**失焦退出编辑态时优先级还原为「无」**（下次点击重新从低开始）；chip 隐藏时仍占位，避免文字宽度跳动。

---

## ADR-0020 去掉优先级滑块、占位文案三态、圆角与高度收紧、设置窗口自绘滑块

- 日期：2026-11-17
- 状态：accepted
- 结论：
  1. **删除优先级滑块弹层**（palette / 轨道 / 停靠点 / 滑块），优先级只用**输入框边框颜色**表达：无优先级=蓝，低/中/高=对应实色，聚焦与否一致。
  2. 输入框占位文案三态：空闲「添加任务」；聚焦且无优先级「按Tab切换优先级」；聚焦且已设优先级显示「低/中/高优先级」（`Tab` 循环 无→低→中→高→无）。
  3. 窗口最小高度 390 → **320**（参考图 3 量得约 330）。
  4. 卡片圆角 16 → **10**（`RootCard` 与 `Backdrop` 同步）。
  5. 「已完成」分组标题行高 36 → 28、上边距 8 → 2，消除与上方任务之间的大片空白。
  6. 设置窗口不透明度改为**自绘滑块**：4px 圆角轨道 188 宽 + 14px 品牌蓝实心小球，填充段到小球中心，按下/拖动实时预览、释放落盘；开关白球改为 28 × 28，**上下刚好填满 50 × 28 胶囊**。
- 理由：用户逐条对照参考图指出优先级滑块多余、占位文案缺失、最小高度过高、圆角过圆、「已完成」间距过大、滑块小球过大被裁切、开关圆球未填满胶囊。
- 影响：`features/today-widget.md` 升为 v0.7；`TodayWidgetViewModel` 新增 `PlaceholderText`；`TodayWidgetWindow` 删除 palette 相关 XAML/代码；`TodayWidget.axaml` 删除 palette 样式并调整输入框描边与 `group-header`；`WidgetSettingsWindow` 移除 `Slider` 改为自绘控件；新增 `Settings.Track` 令牌。
- Supersedes: ADR-0018 第 5 条（优先级轨道自适应）、ADR-0017 第 6 条（优先级滑块交互）

---

## ADR-0022 不透明度 100% 即完全不透明

- 日期：2026-11-18
- 状态：accepted
- 结论：`Bg.Widget` 由 `#D91E1F21`（85% alpha）改为 `#FF1E1F21` 实色；不透明度只由背景层 `Backdrop.Opacity` 表达，滑块 100% 时背景完全不透出桌面。删除未使用的 `Bg.Widget.Opaque`。
- 理由：用户实测「不透明度拉满了还是透明的」——滑块满值 1.0 乘上画刷自身 85% alpha，实际只有 85% 不透明。
- 影响：40–99% 段的实际不透明度整体上移约 15 个百分点；默认设置 1.0 现在是实色卡片，需要半透明时下调滑块。

---

## ADR-0023 月历小组件：固定 6 行、只读任务条、共享仓储

- 日期：2026-11-18
- 状态：accepted
- 结论：
  1. 基准 **662 × 544**，6 × 7 固定 42 格（不随月份变成 5 行）；标题行 44、星期行 28，单元格随窗口拉伸。
  2. 默认停在工作区右上角（右、上各 32）；`settings.json` 新增独立 `Month` 段，位置尺寸与今日组件分开记忆。
  3. 任务条只显示当天第一条未完成任务的标题，其余折叠为 `+N`；已完成任务不进入月历，任务条不响应点击。
  4. 农历用 `ChineseLunisolarCalendar`（1901–2100），节气用通用近似公式；优先级 节日 > 节气 > 农历；初一显示月名。
  5. 新增 `Calendar.Festival = #4FAE7A`；任务条用 `Accent.Muted`，不引入第二套蓝色。
  6. 头部 `＋` 打开 280 宽快速新建弹层，固定添加到今天；`月⌄` 在 P0 只有「月 ✓」一项；标题不打开年月选择器。
  7. 翻月播放 180ms 淡入 + 12px 位移；单元格 hover 120ms。
- 理由：参考桌面右上角滴答清单月历（实测 702 × 565 物理像素 / 125% DPI、5–6 行自适应、单条任务 +N、年月选择器、视图菜单与显示设置），按本产品 P0 范围裁剪，并与今日组件共用一套仓储与视觉语言。
- 影响：新增 `harness/features/month-widget.md`；`WidgetHost` 支持 `Month` 设置槽位；`App` 同时打开今日与月历两个窗口；月历与今日通过 `ITaskRepository.Changed` 实时同步。

---

## ADR-0024 P0 取消 WorkerW 挂载，小组件改为普通非置顶窗口

- 日期：2026-11-18
- 状态：accepted
- 结论：`WidgetHost` 不再调用 `DesktopPinner.TryPinToDesktop`，两个小组件都以普通顶层、非置顶窗口运行；「置于顶层」开关仍然有效。删除 `DesktopPinner` 及 `NativeMethods` 中仅供其使用的 P/Invoke。
- 理由：Windows 11 上 `FindWorkerW` 的 Progman 兜底会把窗口挂成 Progman 的子窗口，位置落在桌面图标 `SysListView32` 之下。实测该状态下 `WindowFromPoint` 命中的是图标列表窗口，点击全部被系统吞掉，翻月、今天、快速新建、输入框都无法操作；同时子窗口用 DirectComposition 渲染，GDI 截图与桌面重绘会出现残影。ADR-0015 要求挂载失败时保持普通窗口，Progman 兜底违背了这一点。
- 影响：小组件被普通应用窗口遮挡、不随「显示桌面」保持，符合 P0「可交互优先」；`WidgetSettings.Layer` 字段保留，贴桌面（WorkerW）作为 P1 能力，需先实现输入转发或点击穿透策略。`harness/architecture.md` §4 同步更新。

---

## ADR-0025 月历首次启动避让今日组件

- 日期：2026-11-18
- 状态：accepted
- 结论：月历没有记忆位置时，若今日组件也在默认右上角（无记忆位置或记忆位置就在默认点附近），月历改停在今日组件左侧，间距 16；否则仍停右上角。今日组件本身的位置逻辑不变。
- 理由：两个组件都默认右上角会在首次启动时完全重叠，月历 662 宽会盖住今日组件。
- 影响：`WidgetHost` 增加可选的避让参数，仅月历传入今日组件的设置与基准宽度。

---

## ADR-0026 月历「今天」幂等、输入法回车与单实例保护

- 日期：2026-11-18
- 状态：accepted
- 结论：
  1. 「今天」在已处于本月时不做任何切换（今天始终高亮），只有跨月才播放切回动画。
  2. 快速新建弹层输入中文时，第一次 Enter 由输入法提交候选，第二次 Enter 才提交任务；这是系统输入法行为，不做拦截。
  3. 新增单实例保护：`Program` 用命名互斥量，重复启动直接退出，避免两套内存数据并存破坏「所有数据同步」。
  4. 实机验收：翻月/今天/`月⌄`/`＋` 均可用；月历快速新建后今日列表立即出现，今日组件新建与勾选任务后月历任务条与 `+N` 立即变化；翻月 180ms 淡入+位移经逐帧采样确认。
- 理由：P0 数据在内存，双实例会各自维护一套数据；月历与今日组件必须看到同一份数据。
- 影响：`month-widget.md` 验收清单已勾选实机通过项；截图存于 `harness/assets/month/`（01 月历、02 视图菜单、03 快速新建、04 数据同步、05 缩放）。

---

## ADR-0027 主程序主窗口：生命周期主体、浅色扩展标题栏、三栏任务页

- 日期：2026-11-18
- 状态：accepted
- 结论：
  1. 主窗口成为 `desktop.MainWindow`，`ShutdownMode=OnMainWindowClose`；关闭主窗口时显式关闭今日与月历小组件，进程退出。小组件不再是应用生命周期入口。
  2. 主窗口固定浅色（`RequestedThemeVariant=Light`），与深色小组件并存；新增 `Main.*` 浅色令牌，仍只允许引用令牌，不得写死色值。
  3. 窗口使用扩展客户区：`ExtendClientAreaToDecorationsHint=True`、标题栏高 40、保留系统最小化/最大化/关闭按钮；顶部 40px 空白区域拖动窗口，双击最大化/还原。Avalonia 12 自带 `WindowDrawnDecorations` 会额外绘制标题文字与全屏按钮，已在 `App.axaml` 用应用级样式隐藏（`chrome|WindowDrawnDecorations TextBlock`、`Button#PART_FullScreenButton`），只留三个系统按钮；列表与详情内容顶部同时留 40，避免落进按钮区域。
  4. 布局按调研实测：64 图标栏 + 260 清单栏 + 任务列表 + 详情面板，列表与详情按 1 : 0.92 分配剩余宽度，详情最小 320。
  5. 本期只实现任务页；四象限/日历/设置保留图标栏入口，点击显示占位页并明确标注「P0 后续迭代实现」，不伪造内容。
  6. 任务页与小组件共用同一仓储；主窗口的添加、勾选、改期、改清单、删除通过 `Changed` 即时同步到两个小组件。
- 理由：用户明确「主程序管理所有内容，主程序关了小组件也关闭」；主窗口是对标滴答清单的主入口，需要与小组件同等的视觉完成度；浅色主窗 + 深色小组件是参考截图的实际形态。
- 影响：`App.axaml.cs` 生命周期改写；新增 `harness/features/main-window.md`；`design-system.md` 增加主窗口尺寸与 `Main.*` 令牌；`SampleData` 增加逾期任务与「欢迎」清单任务以覆盖分组与清单视图。

---

## ADR-0028 月历小组件功能完善：自适应多任务条、当日浮层、年月跳转与设置同步

- 日期：2026-11-18
- 状态：accepted
- Supersedes: ADR-0023 第 3 条（仅显示一条与不响应点击）、第 6 条（标题静态与无 `⋯` 设置）
- 结论：
  1. 单元格任务条由固定单条改为按格高自适应展示多条（默认 662×544 展示 2 条，放大后自动展示 3–4 条），超出部分折叠为 `+N`；任务条左侧增设 3px 优先级色条与语义柔底，悬停展示完整标题。
  2. 点击任务条直接打开共享的 `TaskDetailWindow`；点击日期单元格或 `+N` 弹出「当日日程浮层」，支持查看全天任务、直接勾选/取消完成、与在该日就地新建任务（`Tab` 切优先级）。
  3. 月份标题 `2026年11月 ⌄` 支持点击打开年月选择器快速跳转；新增右上 `⋯` 更多菜单入口打开 `WidgetSettingsWindow`，同时让月历背景层 `Backdrop.Opacity` 真实应用并记忆 `Settings.Opacity`、`ShowCompleted`、`ShowLunar`。
  4. 修复今日日期圆徽标内部左偏问题并增加 `Accent.Soft` 今日单元格柔底高亮，支持鼠标滚轮与左右方向键翻月。
- 理由：原月历小组件在窗口放大或单日多任务时空间大量留白、无法展开 `+N` 任务且完全忽略了设置的不透明度，用户无法从月历直接完成或为指定日期安排任务。
- 影响：`harness/features/month-widget.md` 同步更新；`WidgetSettings` 新增 `ShowLunar`；`WidgetSettingsWindow` 支持月历模式；`MonthWidgetWindow` / `MonthWidgetViewModel` 新增自适应容量计算、当日日程浮层、年月选择器与 `TaskDetailWindow` 联动。

---

## ADR-0029 主窗口架构与 UI 重构及补齐 P0 四象限、日历、设置页

- 日期：2026-11-18
- 状态：accepted
- 结论：
  1. 将原 `MainWindow` 单体 XAML/后台拆分为模块化控件与页面：`TaskRowControl`、`DatePickerPopover`、`ToastBanner`、`TasksPageView`、`TaskDetailPanel`、`EisenhowerView`、`CalendarPageView`、`SettingsPageView` 及 `WidgetCoordinator`。
  2. 顶部 40px 扩展标题栏改为统一工具条（左侧清单栏折叠开关、今日农历与进度胶囊、右侧安全避让系统按钮 `144px`），彻底解决详情关闭按钮与系统关闭按钮垂直叠放与视觉冲突的问题。
  3. 任务页升级：行内添加支持优先级（`Tab`/点击）、日期、清单预设；列表支持排序、分组折叠与「已过期一键顺延到今天」；`TaskRowControl` 配备完成动效、悬停快捷改期/优先级/删除；`TaskDetailPanel` 标题支持折行并修复日期弹层非本月透明度未生效问题；底部新增 4 秒浮动撤销 `ToastBanner`。
  4. 按 `product.md` P0 清单完整实现：四象限页（2×2 优先级映射、卡片内 `+` 新建与独立滚动、详情抽屉）、日历页（月/周双视图、点击任意日新建、指针拖拽任务芯片改期）、设置页（减少动画开关、两个桌面小组件的显隐/透明度/置顶/位置重置联动、快捷键速查与示例数据重置）。
  5. 为全部页面切换、侧栏折叠、详情滑入、分组箭头、悬停与弹层补充 120–180ms 流畅过渡动画，并受设置页「减少动效」开关控制。
- 理由：原主窗口存在三页占位未实现、单体文件过重、标题栏与详情关闭按钮位置冲突、缺少完成/切换动效及核心生产力交互（行内选优先级/日期、撤销删除、顺延、日历拖拽）等问题。
- 影响：`harness/design-system.md` 新增主窗口与月历令牌；`harness/features/main-window.md` 更新并新增 `eisenhower.md`、`calendar.md`、`settings.md`；`VGList.Desktop` 重构为分模块结构并接入 `WidgetCoordinator`。

---

## ADR-0030 视觉精修：自绘紧贴右上角标题按钮、纯色日历芯片、内嵌无黑角浮层与轻盈设置页

- 日期：2026-11-18
- 状态：accepted
- Supersedes: ADR-0027 第 3 条（依赖 `WindowDrawnDecorations` 隐藏全屏按钮）
- 结论：
  1. 主窗口标题栏改用 Avalonia 12 `WindowDecorations=BorderOnly` 搭配 `ExtendClientAreaToDecorationsHint=True`，移除 `WindowDrawnDecorations` 的隐藏全屏按钮样式——该原生模板在隐藏全屏按钮后会将 `— □ ×` 整体左移约一格，导致无法紧贴右上角；改由主窗口顶部栏自绘 46×40 Win11 风格最小化 / 最大化还原 / 关闭按钮，严格顶到右上角 `(0, 0)`，并去掉顶部多余的「今日 3/9」进度胶囊。
  2. 主窗口日历页与桌面月历小组件的任务条均去除左侧 3px 加深竖条，仅保留纯色语义柔底胶囊与圆角造型。
  3. 重绘左侧导航栏品牌徽标与任务 / 四象限 / 日历 / 设置矢量图标（统一 20×20 网格与柔润圆角）；设置页改用纯白背景 + 无边框浅灰圆角卡片 + 38×20 轻盈胶囊开关（带轻阴影 16px 白球），与参考设计风格一致。
  4. 月历小组件的年月选择器、快速新建、视图/更多菜单与当日日程浮层改用窗口内嵌动效浮层（Canvas 定位 + 点击外侧关闭 + 160ms 淡入位移），彻底消除原生透明 Popup 窗口在合成与截图时的四角黑色底噪，并补齐今日小组件的视图切换动画、箭头平滑旋转与空状态提示。
- 理由：实机截图与用户走查指出右上角三按钮未顶右、日历芯片左侧竖线多余、左侧图标与设置开关质感欠佳、顶部进度胶囊多余、以及月历 Popup 四角黑底瑕疵。
- 影响：`App.axaml`、`Tokens.axaml`、`MainWindow.axaml(.cs)`、`SettingsPageView.axaml`、`CalendarPageView.axaml`、`MonthWidgetWindow.axaml(.cs)`、`TodayWidgetWindow.axaml(.cs)` 及对应 harness 文档同步更新。

---

## ADR-0031 品牌 VG 徽标升级：连贯几何拓扑与 60fps 流动矢量动效

- 日期：2026-11-18
- 状态：accepted
- 结论：
  1. 淘汰左侧导航栏顶部原有的单对勾纯色蓝块，采用基于文本生成设计稿定型的品牌几何结构：`V` 右笔向上顺势流转衔接 `G` 的顶部圆弧，左下以干净几何留白分隔出 `G` 下半弧与横折，兼顾品牌识别与「勾选/推进」意象。
  2. 在 `MainWindow` 左上角实现原生矢量控件 `VgBrandLogo`（`36×36` 圆角 10 钴蓝渐变卡片，内含液态蓝极光光斑、斜向高光扫带与沿 `VG` 矢量路径连续流动的高亮流光 `StrokeDashOffset`），同时尊重设置页「减少界面动效」开关，支持点击直达任务页。
  3. 同步沉淀品牌高清圆角应用图标设计图供后续打包桌面 `.ico` 使用。
- 理由：原单一白色对勾缺乏 VGList 品牌辨识度；全矢量 + 合成流光实现可在 100%/125%/150% DPI 下保持边缘绝对锐利且自带生动流动感。
- 影响：`harness/design-system.md`、`harness/features/main-window.md`、`Tokens.axaml`、新增 `Controls/VgBrandLogo.axaml(.cs)`、`MainWindow.axaml(.cs)`。

---

## ADR-0032 统一悬浮快速新建卡片、任务内待办事项（Checklist）与饼图进度、四象限及细节打磨

- 日期：2026-11-18
- 状态：accepted
- 结论：
  1. **任务内待办事项（`TaskSubtask`）与饼状进度图标**：`TaskItem` 新增 `Subtasks` 子待办集合；日历、月历小组件、四象限悬浮新建卡片与主窗口 `TaskDetailPanel` 均支持「描述文本 ↔ 待办清单」一键双向互转（多行文本逐行转为可勾选待办，再次点击转回多行纯文本；待办输入行 `Enter` 下增一项，空行按 `Backspace` 删除并上移焦点）。任务存在子待办时，任务行与卡片自动显示按完成比例绘制的圆形饼状进度图标。
  2. **日历 / 月历 / 四象限统一侧向悬浮新建卡片**：
     - 取消主窗口日历页与桌面月历小组件右上角重复的「新建」按钮，点击任意日期格即在格旁伸出侧向悬浮新建卡片（优先向右贴边伸出；右侧空间不足时向左贴边伸出），右上角展示并支持 `Tab` / 点击切换优先级旗帜，底部支持切换清单（收集箱 / 欢迎）。
     - 四象限卡片删除页副标题「优先级自动归类，无需重复维护」及四张卡片副标题（「立即处理」等）；卡片标题色与左侧 `!` 徽标同色；右上角 `＋` 仅在卡片悬停时渐现，点击后右列象限（Q2/Q4）向左延伸出悬浮新建卡片，左列象限（Q1/Q3）向右延伸出卡片。
  3. **主窗口全量消除原生 Popup 矩形阴影边缘**：将 `TasksPageView`、`TaskDetailPanel`、`CalendarPageView` 与 `EisenhowerView` 的日期/优先级/清单/排序/溢出/新建弹层全部转为窗内 `OverlayCanvas` 渲染，彻底消除 Win32 `<Popup>` 透明角矩形边框伪影。
  4. **图标、对齐与命中区打磨**：修复清单栏图标与中文文本基线垂直居中对齐；将 `TaskRowControl` 悬停快捷操作按钮放大至 `28×28` 圆角胶囊；修复日历「今天」日号徽标为正圆并垂直居中；重绘左侧栏四象限图标为全对称等大圆角矩形。
- 理由：响应用户实机体验后的 10 项细节与交互建议，统一多端新建心智并消除平台透明弹窗瑕疵。
- 影响：`VGList.Core`（`TaskSubtask` / `TaskItem` / `TaskDraft`）、`VGList.App`、`VGList.Widgets`（`QuickCreateViewModel`、`SubtaskPieControl`、`MonthWidgetWindow`）、`VGList.Desktop`（`TaskQuickCreateCard`、`TaskRowControl`、`TaskDetailPanel`、`TasksPageView`、`EisenhowerView`、`CalendarPageView`、`MainWindow`）及对应 harness 文档。




---

## ADR-0033 任务行视觉精简、子待办饼图对齐、日历悬浮编辑小卡片与优先级圆点

- 日期：2026-11-18
- 状态：accepted
- 结论：
  1. **去除任务行备注图标与悬停快捷操作胶囊**：`TaskRowControl`（任务页与四象限共用）移除标题右侧的三横线备注图标与右侧悬停弹出的 `[改期 / 优先级 / 删除]` 胶囊按钮，仅保留子待办进度饼图、清单名与日期标签。
  2. **子待办饼状图微缩与中文垂直居中**：任务行内的 `SubtaskPieControl` 尺寸缩小至 `12×12` 并上移微调 `Margin="6,0,0,1"`，使其与左侧 14px 中文任务标题达到光学垂直居中。
  3. **修复待办事项键盘删除与放大悬浮小卡片**：待办事项列表 `TextBox` 的键盘事件改由隧道阶段（`RoutingStrategies.Tunnel`）在 `ItemsControl` 层拦截，解决 Avalonia `TextBox` 内部先行吞掉 `Backspace`/`Delete` 导致空行无法按键盘删除的问题；同时将悬浮小卡片宽度由 `286` 放大至 `348`，正文/待办区最小高度提升至 `124`。
  4. **日历点击任务弹出悬浮编辑小卡片、右上角优先级改用圆点、移除日历「显示已完成」**：
     - 日历页（月/周视图）移除顶部工具条的「显示已完成」按钮，同时移除右侧滑出详情抽屉；点击日历中的任意已有任务芯片时，直接在芯片旁弹出与新建卡片同构的**悬浮编辑小卡片**（顶部左侧含完成复选框 + 竖线 + 日期，修改标题/描述/子待办/优先级/日期/清单实时写回仓储，底部右侧提供删除按钮）。
     - 悬浮小卡片（含桌面月历小组件当日浮层）右上角优先级指示统一改为**语义色圆点**（不再使用旗帜图标）。
- 理由：响应用户对任务行干扰元素、饼图尺寸与对齐、待办空行删除、日历小卡片尺寸与就地弹出编辑形态的实机反馈。
- 影响：`TaskRowControl`、`TaskQuickCreateCard`、`QuickCreateViewModel`、`TaskDetailPanel`、`CalendarPageView`、`CalendarPageViewModel`、`EisenhowerView`、`MonthWidgetWindow` 及相关 harness 文档。

---

## ADR-0034 四象限悬浮编辑卡、小组件工具窗口化与静态 VG 徽标

- 日期：2026-11-19
- 状态：accepted
- Supersedes：ADR-0031 第 2 条（徽标流光/扫光动效）；ADR-0003x 中四象限「右侧详情抽屉」的实现形态
- 结论：
  1. **四象限点击任务改为侧向悬浮编辑卡**：移除 `EisenhowerView` 右侧 360px `TaskDetailPanel` 抽屉，复用 `TaskQuickCreateCard` + `QuickCreateViewModel`；点击任务行时就地弹出 348px 编辑卡，左列象限（Q1/Q3）向右伸出、右列象限（Q2/Q4）向左伸出，点外部或 `Esc` 收起，标题/描述/待办/优先级/日期/清单实时写回，底部删除走可撤销 Toast。`Esc` 由主窗口统一路由到 `CloseQuickCreate()`。
  2. **桌面小组件改为工具窗口**：今日/月历小组件设置 `ShowInTaskbar=False`，并通过 `WindowEffects.TrySetToolWindow` 追加 `WS_EX_TOOLWINDOW`；任务栏悬停预览与 Alt+Tab 不再把小组件列为独立程序，任务栏只保留主窗口。小组件仍可正常拖动、缩放、置顶与交互。
  3. **VG 徽标去动效并放大字标**：删除液态极光球、斜向扫光带、沿笔画流光与外环光晕，仅保留钴蓝渐变底、1px 内缘高光与白色几何 VG 描边；`Viewbox` 由 24 放大到 27（36px 壳体内），移除 `OrbLight` / `OrbDeep` / `Sheen` / `StrokeFlow` / `FlowAura` 五个不再使用的颜色令牌。
- 理由：用户实机走查要求四象限与日历保持同一「就地小卡片」编辑心智；小组件被任务栏/Alt+Tab 当作程序会污染程序切换列表；原徽标循环流光与扫光在静止桌面环境中过于闪烁，字标也偏小。
- 影响：`EisenhowerView.axaml(.cs)`、`EisenhowerViewModel.cs`、`MainWindowViewModel.cs`、`WidgetHost.cs`、`WindowEffects.cs`、`NativeMethods.cs`、`TodayWidgetWindow.axaml`、`MonthWidgetWindow.axaml`、`VgBrandLogo.axaml(.cs)`、`Tokens.axaml` 及 `harness/` 对应文档。

---

## ADR-0035 小组件取消 DWM 亚克力，改为真实 Alpha 透明

- 日期：2026-11-19
- 状态：accepted
- Supersedes：ADR-0012（`TransparencyLevelHint=Transparent` + `DwmExtendFrameIntoClientArea(-1)` + `DWMWA_SYSTEMBACKDROP_TYPE` 系统亚克力）
- 结论：
  1. `WidgetHost.OnOpened` 不再调用 `TryEnableAcrylicBackdrop`；删除 `WindowEffects.TryEnableAcrylicBackdrop`、`DwmExtendFrameIntoClientArea`、`Margins`、`DWMWA_SYSTEMBACKDROP_TYPE`、`DWMSBT_TRANSIENTWINDOW` 等不再使用的 P/Invoke 与常量。
  2. 小组件保持 `TransparencyLevelHint="Transparent"`、`WindowDecorations="None"`、`Background="Transparent"`，由 `Backdrop` 层 `Bg.Widget` 的 `Opacity` 直接与桌面做 per-pixel Alpha 合成。
  3. 窗口仍保留 DWM 圆角偏好与工具窗口样式；不透明度滑块语义不变：40–100% 只作用于背景层，文字/边框/弹层始终 100% 不透明，100% 时完全不透出桌面。
- 理由：用户实机走查发现两个问题同源——① 降低不透明度时背景只被系统亚克力「洗淡」，无法真实透出桌面；② `DwmExtendFrameIntoClientArea(-1)` 的亚克力/边框层是不透明矩形，在圆角外侧形成方形灰带，底部两角尤其明显。取消亚克力后透明区域变为真正的 per-pixel Alpha，圆角与真实透明同时恢复。
- 影响：`WidgetHost.cs`、`WindowEffects.cs`、`NativeMethods.cs`、`Tokens.axaml` 注释、`harness/design-system.md`、`harness/architecture.md`、`harness/features/today-widget.md`、`harness/features/month-widget.md`。

---

## ADR-0036 Plana 品牌更名、官方 Logo 资源与双主题

- 日期：2026-11-19
- 状态：accepted
- Supersedes：ADR-0034 第 3 条（VG 徽标实现）；应用名 VGList → Plana
- 结论：
  1. **品牌更名**：产品名、窗口标题、程序集 `AssemblyTitle/Product`、示例文案统一改为 `Plana`；可执行文件名保持 `VGList.exe` 不变。
  2. **Logo 必须使用官方资源**：由用户提供的设计稿裁切导出 `Assets/Plana/plana-mark.png`（512×512 透明底）与 `plana-wordmark.png`（1024 宽透明底），主窗左栏顶部显示 36×36 图形 + 46px 字标，点击返回任务页；删除手绘 `VgBrandLogo`，**禁止任何矢量化重绘或再创作**。
  3. **应用图标**：由官方图形生成 `Assets/Plana/plana.ico`（16–256 多尺寸），`csproj` 设 `ApplicationIcon`，`MainWindow.Icon` 指向该 ico，任务栏 / Alt+Tab 显示 Plana 图标。
  4. **双主题**：默认 `Plana`（粉紫蓝：主窗 `Main.Accent #8F7BE5`、`Main.Rail #FAF6FE`，小组件深梅色 `Bg.Widget #201C27`、`Accent #B49AF0`）；保留原蓝白为 `ClassicBlue`。令牌拆分为 `Themes/PlanaTokens.axaml` 与 `Themes/BlueTokens.axaml`，只在 `App.axaml` 的 `Application.Resources` 合并；`AppTheme.Apply` 逐键覆盖实现运行期换肤，`MainSettings.Theme` 持久化，设置页提供两个带色样的选项即时切换。
- 理由：用户明确指定品牌名为 Plana 并要求沿用设计稿配色与图形，不接受自行绘制的 Logo；同时要求保留原蓝白配色作为可切换主题。
- 影响：`Themes/PlanaTokens.axaml`、`Themes/BlueTokens.axaml`（删除 `Tokens.axaml`）、`AppTheme.cs`、`App.axaml(.cs)`、`MainSettings.cs`、`WidgetCoordinator.cs`、`SettingsPageView.axaml(.cs)`、`SettingsPageViewModel.cs`、`PlanaBrandLogo.axaml(.cs)`、`MainWindow.axaml(.cs)`、`VGList.Desktop.csproj`、`Assets/Plana/*`、`SampleData.cs`、`harness/design-system.md`、`harness/architecture.md`、`harness/features/main-window.md`、`harness/features/settings.md`。

---

## ADR-0037 动态清单（新建 / 重命名 / 删除 / 归属）

- 日期：2026-11-19
- 状态：accepted
- 结论：
  1. **清单归仓储管理**：`ITaskRepository` 新增 `Lists`、`CreateList`、`RenameList`、`DeleteList` 与 `ListsChanged` 事件；P0 仍为内存实现。收集箱 `inbox` 固定存在，不可重命名/删除。
  2. **删除语义**：删除自建清单时，其任务自动改归属收集箱，并触发一次任务变更；删除后 Toast 提示，不提供撤销。
  3. **名称解析**：`TaskListCatalog` 由仓储在清单变化时同步，任务行、行内添加 chip、详情面板、月历与打卡卡片统一从该目录取清单名，移除 `MainLists` 与「欢迎/收集箱」二选一硬编码。
  4. **侧栏**：清单分组由 `MainWindowViewModel.Lists` 动态渲染（彩色圆点 + 名称 + 未完成计数 + 选中态）；「＋ 新建清单」固定在分组末尾；右键菜单提供重命名（行内编辑，`Enter` 提交 / `Esc` 取消 / 失焦提交）与删除。
  5. **任务归属**：任务页 `CurrentListId` 决定 `MainSection.List` 的查询与标题；行内添加 chip、编辑卡片在全部清单间循环切换；详情面板清单菜单列出全部清单并标记当前项。
- 理由：用户要求「接入清单功能」，此前仅有欢迎与收集箱两条硬编码；动态清单是任务归属、计数与后续持久化的基础能力。
- 影响：`TaskList.cs`、`TaskListCatalog.cs`、`ITaskRepository.cs`、`InMemoryTaskRepository.cs`、`TasksPageViewModel.cs`、`MainWindowViewModel.cs`、`ListSidebarItem.cs`、`ListPickItem.cs`、`MainWindow.axaml(.cs)`、`TaskDetailPanel.axaml(.cs)`、`MainTaskDetailViewModel.cs`、`QuickCreateViewModel.cs`、`MainTaskRowViewModel.cs`、`CalendarPageViewModel.cs`、`MonthDayViewModel.cs`、`harness/features/main-window.md`。

---

## ADR-0038 行内添加框改为多行录入 + 清单下拉；空状态与头部图标精简

- 日期：2026-11-19
- 状态：accepted
- Supersedes：ADR-0037 第 5 条中「行内添加 chip 在全部清单间循环切换」
- 结论：
  1. **行内添加框与今日小组件同形态**：AcceptsReturn 多行录入，Enter 提交、Shift+Enter 换行，随内容增高（上限 132）；聚焦才显示优先级与日期按钮，文字换行时两者整体下放第二行并右对齐；日期按钮为白底描边 chip（日历图标 + 相对日期 + 下拉箭头）。
  2. **清单选择改为右侧常驻下拉**：圆点 + 当前清单名 + 箭头，弹层列出全部清单，选择后焦点回到输入框；不再点击循环。
  3. **失焦语义**：点击任务页空白、切换分组/清单、离开任务页均清焦点并收起选项与浮层；Esc 亦关闭清单下拉。
  4. **空状态精简**：列表空状态只保留标题 + 副标题，删除线稿图标与「＋ 立即添加一条任务」按钮。
  5. **头部视图图标**：改为 20×20 正方形包围盒的线性圆勾，替换原 15×12 扁平方框勾。
- 理由：用户反馈行内添加需失焦、换行与月历按钮下放，并希望清单等选项用下拉承载；同时认为空状态图标/按钮与头部勾选图标观感不佳。
- 影响：TasksPageView.axaml(.cs)、TasksPageViewModel.cs、MainWindow.axaml(.cs)、MainWindowViewModel.cs、MainWindowFeaturesTests.cs、harness/features/main-window.md。

## ADR-0039 添加框优先级入框 + 头部图标重设计 + 日期/收集箱 chip 改版

- 日期：2026-11-19
- 状态：accepted
- Supersedes：ADR-0038 第 1 条中「优先级独立 pill」与「日期按钮为白底描边 chip + 下拉箭头」、第 5 条「线性圆勾」
- 结论：
  1. **优先级移入输入框内部**（与今日小组件一致，删除独立优先级 pill）：边框色即优先级（聚焦时低=蓝/中=黄/高=红/无=中性灰）；占位三段式「添加任务…」→「按Tab切换优先级」→「低/中/高/无优先级」，占位色跟随优先级；聚焦默认低、失焦还原无；Tab 首次直达「高」再循环；提交后仍聚焦则从低继续。
  2. **单行垂直居中**：单行时加号、文本、日期按钮、清单下拉整体在录入框内垂直居中（此前文字贴顶偏高约 4px）；多行恢复顶部对齐，日期按钮下放第二行右对齐。
  3. **头部视图图标重设计**：圆勾改为 20×20 圆角方块（圆角 4.5、描边 1.5 Main.Accent）+ 对勾。
  4. **日期按钮改纯点击样式**：去下拉箭头、去白底描边，透明底 + 蓝色日历图标 + 蓝色相对日期，图标与文字上下居中。
  5. **收集箱（清单）下拉改 chip 样式**：透明底、彩色圆点 + Main.Accent 名称 + 灰色下拉箭头（与日期按钮同族但保留下拉箭头以区隔「下拉」与「点击」语义）。
- 理由：用户反馈优先级 pill 冗余（小组件已用边框+占位表达）、添加文字与日期/图标不居中、月历按钮不应呈下拉形态、收集箱需改为下拉框 chip。
- 影响：TasksPageView.axaml(.cs)、TasksPageViewModel.cs、Themes/MainWindow.axaml、harness/features/main-window.md。

## ADR-0040 换行时日期与清单按钮同排下放；日历图标几何居中

- 日期：2026-11-19
- 状态：accepted
- 结论：
  1. **第二行合并**：添加框引入 `DraftMetaRow`（外网格第 2 行，跨文本列与清单列，右对齐）。单行时日期按钮留在文本网格第一行右侧、清单下拉留在外网格第 3 列；文字换行时两者一并移入 `DraftMetaRow`，同一行并排（日期在前、清单在后）。
  2. **日历图标居中**：图标几何改为 14×12 包围盒内上下左右对称（顶边 y=3、底边 y=11、左右 1.5..12.5）。原几何纵向占据 1..13 而控件高 12，导致视觉下坠 1px。
- 理由：用户反馈换行后收集箱未与「今天」同排，且「今天」左侧日历图标偏下。
- 影响：TasksPageView.axaml(.cs)、harness/features/main-window.md。

## ADR-0041 日期/清单统一为下拉框 chip；失焦改为窗口级捕获

- 日期：2026-11-20
- 状态：accepted
- Supersedes：ADR-0039 第 4 条（日期按钮去箭头/透明底）
- 结论：
  1. 添加框内「今天」与「收集箱」统一为下拉框形态：圆角矩形（圆角 7、1px Main.Divider 描边、白底）、图标 + 文字 + 灰色下拉箭头，悬停背景切换为 Main.Hover（0.12s 过渡）。样式合并为 `Border.draft-chip`。
  2. 失焦监听从 TasksPageView 内部上移到宿主 Window 的 Tunnel 阶段，覆盖左侧清单栏、导航栏等任务页之外区域；命中录入框或其浮层时保持焦点。
- 理由：用户反馈点左侧不失焦；「今天」按钮需有圆角矩形框与悬停反馈，且与收集箱同为下拉框。
- 影响：TasksPageView.axaml(.cs)、Themes/MainWindow.axaml、harness/features/main-window.md。


---

## ADR-0042 助理阶段立项：性能分级、提前本地持久化、新增 Agent 能力

- 日期：2026-11-21
- 状态：accepted（用户 2026-11-21 明确同意）
- Supersedes（部分）：ADR-0003（前端先行，禁止持久化）仅对「助理阶段」放宽；constitution 第 3、6、10 条在助理阶段按本 ADR 分级适用。
- 结论：
  1. **新增「助理阶段」**，在 P0 页面验收收口后进入。内容：中文自然语言日期、模型 API 配置、内置 AI 助理（面板 + 幽灵预演 + 批量撤销）、我的规则 / 记忆与画像 / 周复盘、日常小帮手、外部 AI 接入（MCP）。详见 `features/assistant.md`。
  2. **性能分级**：
     - **常驻档**（助理从未被打开，即只用桌面小组件 + 主窗口）：沿用 constitution 第 6 条——内存 ≤ 150MB（5000 任务）、冷启动 ≤ 1.2s、60fps。助理相关程序集、模型 SDK、记忆文件必须**懒加载**，未打开助理前不得占用启动时间与常驻内存。
     - **助理活跃档**（助理面板已打开或 MCP 正在服务）：内存软上限 300MB，不设冷启动指标；关闭面板且空闲 10 分钟后释放助理相关对象。
     - **安装包**：硬上限由 60MB 放宽为 100MB（不含任何离线模型；本地模型由用户自行安装，不随包分发）。
  3. **提前本地持久化**：助理需要记忆、撤销、对话历史，任务数据不能再「重启重置」。助理阶段第一件事是落地 SQLite（WAL）+ 变更日志（`ChangeSet`），仓储接口保持不变。P0 页面阶段的「禁止 SQLite」约束到此解除。
  4. **安全三原则**（不可退让）：① 助理对任务的写入默认走「提案 → 用户应用」，不直接写；② 每次写入形成一个可整批撤销的变更集；③ 所有记忆、画像、对话、规则只存本机文本/SQLite，无遥测，模型调用只发送完成当次请求必需的内容。
  5. 本地优先不变：不引入账号、不做云同步；API Key 本机加密存储。
- 理由：用户明确「不必严守原性能铁律（当时没考虑 Agent），但只用桌面小组件时开销必须不高」。分级既保住小组件的轻量心智，又为 Agent 留出空间。
- 影响：constitution 追加第 13–14 条；`product.md` 增加「助理阶段」；`architecture.md` 新增助理分层与性能分级；新增 `VGList.Data` / `VGList.Agent` / `VGList.Mcp` 工程；任务树见 `.trellis/tasks/*-plana-assistant`。

---

## ADR-0043 模型 API 配置：厂商预设卡片 + 自定义

- 日期：2026-11-21
- 状态：accepted
- 结论：
  1. 设置页新增「模型服务」分组，交互参照 ccswitch 的「模型厂商」网格：卡片 = 图标 + 厂商名 + 域名 + 「N 个版本」。点卡片进入配置抽屉，**预设厂商只需填 API Key**；版本（通用 / 国际站 / Coding 套餐等）与模型名有默认值，可展开「高级」修改。
  2. 另提供「自定义」卡片：名称 + Base URL + API Key + 协议格式（OpenAI 兼容 / Anthropic 兼容）+ 模型名。
  3. 预设清单以随包 JSON 目录维护（`providers.json`），新增厂商不改代码；首批对齐参考图 15 家，并补充 OpenAI、Anthropic、Google Gemini、OpenRouter、Ollama（本地）。
  4. 每个厂商可有多个已配置实例，仅一个为「当前使用」；提供「测试连接」与「拉取模型列表」（不支持时手填）。
  5. API Key 使用 Windows DPAPI（当前用户）加密后写入本机，界面只显示末 4 位；日志与错误信息禁止出现 Key。
  6. 厂商图标：默认使用「首字母 + 品牌色」方块，不内置未取得授权的官方商标；有授权素材再放入 `Assets/Providers/`。
- 理由：用户要求「预设固定厂商、只填 Key，也支持自定义 URL 与 Key」。
- 影响：`settings.md` 追加「模型服务」；新增 `features/model-settings.md`；`VGList.Agent` 通过 `IChatClient` 抽象使用，不感知厂商。


---

## ADR-0044 助理左侧宠物 IP 入口、独立对话页面样式先行预览与 A0–A2 实现细节

- 日期：2026-11-21
- 状态：accepted（用户 2026-11-21 明确要求）
- Supersedes（部分）：`features/assistant.md` §3 中「入口在主窗口标题栏」
- 结论：
  1. **左侧宠物 IP 入口**：主窗口左侧 64px 图标栏新增「Plana 助理」导航按钮（`Ctrl+J` / `Ctrl+4`），直接使用用户提供的宠物 IP 透明底资源 `Assets/Plana/plana-pet.png`（原图归档于 `harness/assets/assistant/pet-ip-source.jpg`），禁止矢量重绘；选中时呈现柔光胶囊底与描边，悬停带微幅上浮与柔晕过渡（受「减少动效」控制）。
  2. **独立对话页面样式先行预览**：点击左侧宠物 IP 切换至 `MainPage.Assistant` 独立对话页面（与任务/四象限/日历/设置同级），在 A0–A2 阶段先行提供大厂级交互/动画/过渡的对话页面（可折叠左侧历史会话栏、模型状态胶囊、宠物 IP 欢迎态、快捷提示卡、流式输出打字机与思考波浪动画、可折叠工具步骤胶囊、可勾选变更清单预览卡片、附件拖放/粘贴 chip、浮动胶囊输入框与停止/复制/重试反馈）。未配置模型时支持一键进入交互式样式演示；已配置模型时可直连 A2 配置的模型进行实时流式对话（纯 `HttpClient`，不加载任何 Agent SDK，严守 ADR-0042 懒加载）。后续 A4 的「幽灵预演」在需要叠加任务/日历/四象限时提供抽屉联动。
  3. **A1 解析方案**：采用 `VGList.Core` 零依赖自研规则解析器（`ChineseDateParser` + `CnCalendar` 下沉到 Core），不引入 `Microsoft.Recognizers.Text.DateTime`（避免体积与正则冷编译开销）。
  4. **A2 API Key 加密**：在 `VGList.Platform.Win` 中直接通过 Win32 P/Invoke 调用 `CryptProtectData` / `CryptUnprotectData`（`CurrentUser` + `CRYPTPROTECT_UI_FORBIDDEN`），零额外 NuGet 依赖。
- 理由：用户明确要求左侧按钮采用所提供宠物 IP，且在 A0–A2 完成后优先检视对话页面的交互、动画与过渡质感；同时维持零多余包体与常驻档轻量。
- 影响：`design-system.md`、`features/assistant.md`、`features/settings.md`、`features/model-settings.md`、`MainWindow`、`SettingsPageView`、`AssistantPageView`、`VGList.Core`、`VGList.Data`、`VGList.App`、`VGList.Platform.Win`。

---

## ADR-0045 淡粉 Plana 主题重构、设置页独立「模型服务」子页（厂商真实 Logo）与对话全元素渐入动效

- 日期：2026-11-21
- 状态：accepted（用户 2026-11-21 实机验看反馈）
- Supersedes（部分）：ADR-0043 第 1、6 条（模型服务嵌入设置长页 + 首字母色块默认图标）、`design-system.md` §3/§8 中 Plana 偏紫色板
- 结论：
  1. **Plana 主题重设计为「淡粉樱雾」**：去除整体偏紫（`#8F7BE5`/`#FAF6FE`/`#F3ECFF`），改为以纯净瓷白底（`#FFFFFF`/`#FFFDFE`）配极浅樱雾侧栏（`#FEF5F8`）与柔和樱粉主色（`#E46B92` / 浅粉底 `#FDECF2` / 用户气泡 `#FDEEF4` / 宠物柔晕 `#36F8A8C8`），深色小组件同步调整为暖玫炭底（`#231D20`）+ 柔粉强调（`#F299B8`）。
  2. **设置页升级为左侧分类导航 + 独立子页**：「常规与外观」「模型服务」「桌面小组件」三页独立呈现，切换带短渐入过渡；「模型服务」拥有完整可视高度与筛选条（全部 / 国内 / 海外与本地 / 已配置），不再受限高滚动框约束；从助理页点击模型胶囊直达「模型服务」子页。
  3. **厂商真实官方 Logo**：20 家预设厂商全部配置真实公司 Logo 图标（存于 `src/VGList.Desktop/Assets/Providers/<id>.png`），置于统一 40×40 浅色圆角白底容器内；仅自定义服务或缺失资源时回退到首字母徽标。
  4. **对话页出现类元素全量短渐入 + SSE 文字渐入**：欢迎主视觉、2×2 快捷卡、每条消息气泡、工具进度胶囊、思考波浪、流式 SSE 增量文字（尾部新到达片段按 200ms `CubicEaseOut` 透明渐入且不触发排版抖动）、变更提案卡片、微操作行、附件芯片与悬浮回底胶囊全部具备顺滑短渐入动效；严格受「减少动效」开关控制。
- 理由：用户实机审看后明确提出：① 对话内出现类元素含 SSE 文本均需柔和渐入，避免瞬间跳出突兀；② 模型服务应作为设置中独立一页并采用各公司真实 Logo；③ Plana 主题此前整体偏紫，需重构为舒适柔和的淡粉色风格。
- 影响：`PlanaTokens.axaml`、`BlueTokens.axaml`（仅同步 `Theme.Swatch.Plana`）、`Assets/Providers/*.png`、`SettingsPageView(.axaml/.cs)`、`SettingsPageViewModel`、`ModelServicesViewModel`、`AssistantPageView(.axaml/.cs)`、`ChatMarkdown.cs`、`design-system.md`、`features/settings.md`、`features/model-settings.md`。

---

## ADR-0046 助理运行时选型：MEAI + 轻量双协议 IChatClient 适配器、字符预算与懒加载边界

- 日期：2026-11-21
- 状态：accepted
- 结论：
  1. **运行时抽象选用 `Microsoft.Extensions.AI`**（`IChatClient`、`AIFunctionFactory`、`FunctionInvokingChatClient`），不引入 `Microsoft.Agents.AI`（Agent Framework）；本阶段仅需单 Agent 工具循环 + 自有提案/权限/撤销安全层，MEAI 体积更小、边界清晰、易于测试。
  2. **双协议适配采用 `VGList.Agent` 内置原生 `HttpClient` 实现 `IChatClient`**（`OpenAiCompatibleChatClient` 与 `AnthropicCompatibleChatClient`），直接支持文本流式 SSE、流式工具调用（`tool_calls` / `tool_use`）、多模态图片输入、自定义 `BaseUrl` 与国内中转站容错，避免引入重型第三方厂商 SDK 及其对非标准国内兼容端点的序列化冲突。
  3. **上下文预算首版按字符近似裁剪**（系统提示 ≤ 6,000 字，历史对话 ≤ 24,000 字，保留系统提示与最近 6 轮，从最旧轮次裁剪），不引入 tokenizer 与摘要压缩模型调用。
  4. **附件解析依赖仅限 `VGList.Agent`**：PDF 文本层提取使用 `PdfPig`，图片缩放复用 `SkiaSharp`，`.docx` 使用 BCL `System.IO.Compression` + `System.Xml.Linq`，GBK/中文编码探测使用 `System.Text.Encoding.CodePages`。
  5. **常驻懒加载铁律**：`VGList.Desktop` 仅在助理首次激活对话运行时通过独立工厂方法创建 `AssistantHost`，常驻态（未打开助理）不得加载 `VGList.Agent`、`Microsoft.Extensions.AI` 或 `PdfPig` 程序集。
- 理由：兼顾 .NET 10 原生轻量、20 家国内外模型厂商真实协议兼容性与 ADR-0042 常驻档性能约束。
- 影响：新增 `src/VGList.Agent/`、`VGList.App/Assistant/`、`VGList.Data/Migrations/002_assistant.sql`；`VGList.slnx` 与 `tests/VGList.Tests` 同步接入。

---

## ADR-0047 助理「Lana」品牌与交互精修：流光思考球、时间轴交错渲染、Markdown 表格、2 行提案胶囊、模型下拉切换、`@`/Ctrl+V 附件引用与任务录入框稳定性修复

- 日期：2026-11-21
- 状态：accepted（用户 2026-11-21 实机 13 条详审反馈）
- 结论：
  1. **去掉「每日负荷上限」**：移除 `MaxTasksPerDay` / `MaxHighPriorityPerDay` 规则字段、校验规则、设置 UI 与系统提示相关文案，仅保留休息日、节假日清单保护、自然语言偏好与权限分级。
  2. **全局点空白失焦**：窗口级 Tunnel 点击监测，在任意页面点击非文本输入区域时自动释放当前 `TextBox` 焦点（任务页点击录入框自身或已打开的日历浮层时正常保留）。
  3. **时间轴交错消息块 + 真实工具运行态**：助理消息改为按时间顺序交错排列 `ChatToolStepBlock` 与 `ChatTextBlock`（不再把所有工具堆到开头）；工具执行中显示流光态，工具结束后若模型仍在思考或继续工具循环，底部持续展示思考动效直至流式正文结束。
  4. **粉紫流光灵动思考动画（`LanaThinkingOrb`）**：替代静态三点「思考中…」，以 60fps 合成绘制柔和粉紫蓝渐变呼吸光球 + 轨道微星 + 流光文字波，受「减少动效」开关控制。
  5. **完整 Markdown 块渲染（含表格与代码块）**：`ChatMarkdownView` 原生支持 Markdown 表格（圆角表头、清晰列分隔、单元格内粗体/代码）、围栏代码块与 220ms SSE 渐入段落。
  6. **提案卡片精简为 2 行 + 差异化圆角胶囊**：移除冗余第三行理由文本；第一行任务标题，第二行将日期、优先级（按高/中/低语义色区分）与步骤/清单分别用差异化圆角胶囊包裹。
  7. **宠物定名「Lana」与极简欢迎态**：移除 4 张推荐卡片；欢迎语与标题使用「Lana」（名字采用粉紫蓝品牌渐变色 + `Font.Display`）；Lana 系统人格为温和可爱风，适度使用颜文字与 emoji。
  8. **顶部模型胶囊改为窗内下拉切换菜单**：点击展开已配置模型列表（含厂商真实 Logo）即时切换激活模型，底部提供「管理模型服务…」跳转设置页。
  9. **修复「回到最新消息」残留**：切换会话或新建空会话时强制重置滚动跟随与隐藏回底胶囊。
  10. **字体体系升级**：新增 `Font.Ui`（`Anthropic Sans` 优先，回退 `Noto Sans SC, Segoe UI Variable Text, Microsoft YaHei UI`）与 `Font.Display`（`Anthropic Serif` 优先，回退 `Noto Serif SC, Georgia, Microsoft YaHei UI`），用于大标题与模型选择按钮。
  11. **任务页录入框与「今天」图标精修**：优化「今天」标题图标为居中无裁切日历标记；添加框隐藏默认「收集箱」字样，仅留紧凑下拉箭头供选择清单；修复日历浮层开启后点输入框需点两次的问题；彻底重构换行布局逻辑（消除 `SizeChanged` 反复重挂载导致的死循环卡死与 `TextLayout` 泄漏）。
  12. **Lana 输入框支持 `Ctrl+V` 粘贴图片/文件与 `@` 引用文件**：`Ctrl+V` 支持系统剪贴板截图（Win32 DIB/PNG 兜底）与文件列表；输入 `@` 呼出窗内文件引用菜单，选中后以文件类型图标 + 文件名胶囊呈现；鼠标悬浮图片胶囊直接预览缩略图。
- 影响：`RulesStore.cs`、`SystemPromptBuilder.cs`、`PlanaTokens.axaml`、`BlueTokens.axaml`、`ClipboardHelper.cs`、`LanaThinkingOrb.cs`、`ChatMarkdownView.cs`、`AssistantPageView(.axaml/.cs)`、`AssistantPageViewModel.cs`、`TasksPageView(.axaml/.cs)`、`SettingsPageView(.axaml/.cs)`、`MainWindow.axaml.cs`。

---

## ADR-0048 Lana 体验深度打磨：纯文字流光思考、历史编辑与截断重生成、结构化画像与称呼引导、默认自动执行 + 细粒度撤销、实时工具进度与顶部浅色 Toast

- 日期：2026-11-21
- 状态：accepted（用户 2026-11-21 实机 11 条反馈）
- Supersedes（部分）：ADR-0042 §4（写入权限默认值改为除删除外默认自动执行，删除默认需确认且可在设置中修改）；ADR-0047 第 4 条（去掉思考左侧光球，改纯粉紫蓝渐变持续流光文字）
- 结论：
  1. **纯文字持续流光思考态**：移除思考态左侧圆球；全程以粉→紫→天蓝平滑循环流动的渐变文字展示（字体采用 `Font.Ui` / `Anthropic Sans`），配备多组可爱人性化思考文案随机轮换（如「Lana 正在思考…」「让 Lana 想想…」「Lana 正在整理思绪…」等）。
  2. **移除顶栏「体验演示」按钮**：保持对话顶栏极简，仅保留历史折叠、模型下拉切换。
  3. **重新生成同步截断持久化历史**：`IConversationStore` 新增 `TruncateMessages(sessionId, keepCount)`，重新生成时将上一轮记录从 SQLite 中同步移除，确保模型不再看到旧回复。
  4. **支持编辑历史用户消息**：鼠标悬停用户消息显示铅笔编辑按钮，支持就地编辑正文及图片/文件附件的增删改；保存发送后同步截断后续对话历史并重新生成。
  5. **用户画像去原始 Markdown 符号**：设置页「我的用户画像」由结构化表单（我的称呼 / 作息习惯 / 常做的事 / 高效时段 / 容易拖延的类型 / 其他）呈现，磁盘仍保存可读 `profile.md` 并保留手改保护与历史版本恢复。
  6. **用户称呼首次自然引导**：用户画像新增「我的称呼」；若尚未设置且此前未询问过，Lana 仅在初次对话回复末尾轻轻询问一次并持久化标记，绝不每次发消息重复追问；新增 `set_user_nickname` 工具随时记录称呼。
  7. **默认自动执行权限 + 顶部浅色 Toast + 准确提案标签**：
     - `AssistantPermissionConfig` 默认开启新建/修改/改期/拆子待办/归类/完成/建清单的自动执行；仅「删除任务」默认需确认，且允许用户在「设置 → 助理与记忆」按需修改全部权限开关；
     - 全局 `ToastBanner` 改为浅色瓷白卡片风格并移至窗口顶部居中，不再遮挡底部输入框；
     - 修正提案分类逻辑：仅真正改动日期才标注「改期」，标题/优先级/子待办修改标注「修改」，子待办胶囊展示可读名称而非晦涩的 `+1 子步骤`，并约束提示词不擅自加子步骤。
  8. **实时工具执行中展示**：在 `OpenAiCompatibleChatClient` 与 `AnthropicCompatibleChatClient` 流式接收 `tool_calls` / `tool_use` 首个分片时即刻发射 `ToolStartedEvent`（以及读取附件前发射阅读进度），让用户实时看到「Lana 正在翻看你的日程…」「Lana 正在查看文件…」，完成后再切为 `✓` 静态结果。
  9. **细粒度撤销（单条 / 多选 / 整批）**：`IScheduleService` 新增 `UndoEntries(changeSetId, entityIds)`，`AutoAppliedEvent` 携带完整提案与实体映射；无论是自动执行还是手动应用的日程改动卡片，每行支持单条「撤销」、多选「撤销已选 (N)」与底栏「全部撤销」。
  10. **欢迎页视觉与动态问候**：去掉欢迎页宠物头像后的蓝色/柔光底圆；按实时系统时间动态刷新「早上好/中午好/下午好/晚上好/夜深了」（有称呼时带上称呼）；副标题以「今天是 M月d日 星期X（农历…）」开头并准备多套生动文案随机展示。
  11. **任务页月历弹层改右下弹出**：`TasksPageView` 录入框日期按钮使用 `BelowLeftAligned` 向按钮右下方展开，不再偏向左侧。
- 影响：`IScheduleService.cs`、`IConversationStore.cs`、`SqliteTaskRepository.cs`、`SqliteConversationStore.cs`、`AssistantContracts.cs`、`UserProfileStore.cs`、`PermissionPolicy.cs`、`ProposalStore.cs`、`OpenAiCompatibleChatClient.cs`、`AnthropicCompatibleChatClient.cs`、`AssistantHost.cs`、`SystemPromptBuilder.cs`、`MemoryTools.cs`、`ProposalTools.cs`、`ReadTools.cs`、`LanaThinkingOrb.cs`、`AssistantPageViewModel.cs`、`AssistantPageView(.axaml/.cs)`、`ToastBanner.axaml`、`MainWindow.axaml(.cs)`、`SettingsPageView.axaml`、`AssistantMemorySettingsViewModel.cs`、`TasksPageView.axaml.cs`。

### ADR-0049 · 助理路线裁剪、主窗口关闭行为选择与两项视觉微调（2026-11-21）
- 状态：accepted
- 决策：
  1. **助理路线裁剪**：
     - **A4（任务/日历/四象限幽灵预演）取消**：当前直接通过对话内日程改动卡片与细粒度撤销管理改动，不再叠加页面幽灵虚线预演。
     - **A6（主动小帮手）仅保留「早安提示」**：取消「拖延破冰」与「帮我准备」入口，避免界面冗余；保留每日首次启动（存在逾期任务时）的今日小组件早安轻提醒，点击直达 Lana 整理今日安排，并支持在设置中关闭。
     - **A7（本地 MCP 服务）保留并确认**：供外部 AI 工具通过本机协议读写任务，默认关闭、写入走 `IScheduleService` 并支持主窗口 Toast 撤销。
  2. **主窗口关闭按钮（`×`）行为选择与可配置**：
     - 新增 `MainSettings.CloseAction`（`Ask` 默认每次询问 / `Minimize` 最小化窗口 / `Exit` 退出 Plana）。
     - 点击右上角 `×`（或系统关闭窗口）且配置为 `Ask` 时，主窗口内弹出居中模态确认卡（遵守 ADR-0032 不使用原生弹出窗口），提供「最小化」（保留桌面小组件继续运行）、「退出 Plana」（关闭全部窗口）与「取消」，并支持勾选「记住我的选择」。
     - 在「设置 → 常规与外观」提供「关闭主窗口时」三项切换，随时可改回每次询问或指定行为；OS/应用级系统关机不受拦截。
  3. **视觉对齐与防裁切微调**：
     - 对话页 Lana 宠物头像（`Margin="0,-4,0,0"`）与右侧流光思考态（`LanaThinkingOrb` `Margin="2,1.5,0,2"`）及正文首行在像素级上下视觉中心对齐。
     - 设置 → 模型服务页 `Border.provider-card` 增加顶部留白（`Margin="0,3,10,7"`）并对网格容器设置 `ClipToBounds="False"`，确保首行卡片悬停上浮 `-1.5px` 时顶部边框不再被裁切。
- 影响：`harness/features/assistant.md`、`MainSettings.cs`、`WidgetCoordinator.cs`、`MainWindowViewModel.cs`、`MainWindow(.axaml/.cs)`、`SettingsPageViewModel.cs`、`SettingsPageView(.axaml/.cs)`、`AssistantPageView.axaml`。

### ADR-0050 · 系统托盘最小化、内嵌 Anthropic 字体栈、右键菜单统一样式与权限配置修复（2026-11-21）
- 状态：accepted
- 决策：
  1. **真正最小化到系统托盘**：引入原生 `TrayIcon`（`plana.ico`），左键点击唤回主窗口、右键菜单提供「打开 Plana / 退出 Plana」；点击主窗口 `×` 选择「最小化到托盘」时调用 `Hide()` 从任务栏隐藏并常驻系统托盘（桌面小组件继续运行）；单实例启动互斥时通过命名事件唤醒已隐藏在托盘的主窗口；关闭确认卡精简掉多余说明文案。
  2. **内嵌真实 Anthropic 字体（`AnthropicFontCollection`）**：随应用内嵌 `AnthropicSerifWebText.ttf`、`AnthropicSansWebText.ttf`、`AnthropicMonoVariable.ttf`，通过 `EmbeddedFontCollection` + `FontManagerOptions.FontFamilyMappings` 注册。`Font.Display` 配置为 `Anthropic Serif, SimSun, STSong, Noto Serif SC, Georgia, serif`（英文与数字呈现真实 Anthropic Serif，中文呈现宋体风格，覆盖 Lana 思考、Lana 回复、模型切换按钮及所有原思源宋体位置）；`Font.Ui` 配置为 `Anthropic Sans, Noto Sans SC, ...`。
  3. **侧栏清单右键菜单风格统一**：在 `Themes/MainWindow.axaml` 为 `ContextMenu` / `MenuItem` 定义 Plana 瓷白圆角浮层样式与危险项红色反馈，并配简笔矢量图标。
  4. **Lana 写入权限自动迁移与完全可配**：`UserRulesDocument` 增加 `PermissionsSchemaVersion = 2`，加载旧版 `rules.json` 时自动升级到默认权限（新增/修改/改期/拆步骤/归类/完成默认直写并支持撤销，删除默认需确认）；移除 `RulesStore` 中强行覆写 `AutoDeleteTask = false` 的硬编码，让设置页内每一项权限开关均真实生效；提案工具向模型返回 `willAutoApply` 提示，确保 Lana 回复文案与实际执行模式一致。
- 影响：`VGList.Widgets.csproj`、`AnthropicFontCollection.cs`、`PlanaTokens.axaml`、`BlueTokens.axaml`、`Program.cs`、`App.axaml.cs`、`MainWindow(.axaml/.cs)`、`Themes/MainWindow.axaml`、`SettingsPageView.axaml`、`RulesStore.cs`、`ProposalTools.cs`、`AssistantHost.cs`、`ChatMarkdownView.cs`、`ChatMarkdown.cs`。

### ADR-0051 · 恢复上一版思源默认字体并在设置页提供实时字体切换（2026-11-21）
- 状态：accepted
- 决策：
  1. **默认字体完全还原上一版本**：`PlanaTokens.axaml` / `BlueTokens.axaml` 默认恢复为 `Font.Ui = Noto Sans SC, Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI`、`Font.Display = Noto Serif SC, Georgia, Microsoft YaHei UI, serif`、`Font.Mono = Cascadia Mono, Consolas, Courier New, monospace`。
  2. **设置页支持实时切换字体预设**：新增 `MainSettings.FontPreset`（`Default` 默认思源 / `AnthropicSerif` Anthropic + 思源宋体 / `AnthropicClassic` Claude 经典宋体 / `CleanSans` 纯净黑体 / `System` 微软雅黑）；通过 `AppTheme.ApplyFontPreset` 动态刷新 `Application.Resources` 中的 `Font.Ui` / `Font.Display` / `Font.Mono`，所有已打开窗口与 Lana 对话即时生效且持久化保存；切换主题配色时保留当前所选字体预设。
- 影响：`MainSettings.cs`、`PlanaTokens.axaml`、`BlueTokens.axaml`、`AppTheme.cs`、`App.axaml.cs`、`WidgetCoordinator.cs`、`SettingsPageViewModel.cs`、`SettingsPageView(.axaml/.cs)`、`ChatMarkdownView.cs`、`ChatMarkdown.cs`。

### ADR-0052 · A6③ 早安提示与 A7 本地 MCP 服务（stdio 代理 + 命名管道桥）（2026-11-21）
- 状态：accepted
- 决策：
  1. **A6③ 早安提示（纯 `VGList.App` + 视图层，常驻零 Agent 加载）**：
     - `MorningBriefService` 仅读取未完成逾期任务（`Date < today`）与 `%LOCALAPPDATA%\VGList\assistant\helpers.json`（记录当日关闭状态），无逾期不出现、当日关闭后不再打扰。
     - 今日小组件标题栏下方展示轻提示条；当今日小组件隐藏时回退至主窗口任务页「今天」顶部提示条；点击后唤醒主窗口并向 Lana 自动发送当日日程整理请求（此时才按需懒加载 Agent）。支持在设置中开关（默认开）。
  2. **A7 本地 MCP 外部 AI 接入（默认关闭、严格安全隔离）**：
     - 独立轻量化控制台进程 `VGList.Mcp.exe`（`net10.0-windows`，纯 `System.Text.Json` 标准 MCP JSON-RPC 2.0 stdio 实现，零重型 SDK 依赖，与主程序同目录输出且不被主进程托管引用）。
     - 主进程内 `ExternalAccessBridge`（`VGList.App`）仅在 `MainSettings.McpEnabled == true` 时开启命名管道 `\\.\pipe\plana-mcp-<CurrentUserSID>`，使用 `PipeSecurity` ACL 仅授权当前 Windows 用户 SID，关闭开关即停止监听并断开现有连接。
     - 协议采用换行分隔 JSON（版本字段 `v: 1`）；Plana 未运行或开关未开启时 `VGList.Mcp` 返回明确中文引导，**绝不自动拉起 GUI**。
     - 严格白名单 10 个工具（仅任务与日历：`get_today` / `list_tasks` / `list_lists` / `resolve_date` / `cn_calendar_info` / `create_tasks` / `update_task` / `complete_task` / `reschedule_tasks` / `delete_tasks`），**绝不暴露记忆、画像、规则、对话或 API Key**；只读模式拒绝一切写工具；每客户端写入限流 60 次/分钟。
     - 写入经 `IScheduleService`（`ChangeSource.Mcp`），即时同步小组件并在主窗口弹 Toast「由 <客户端> … · 撤销」；`delete_tasks` 与影响 > 5 项的 `reschedule_tasks` 强制进入应用内 `PendingConfirmationQueue`（等待用户允许/拒绝或 90 秒超时自动拒绝，支持主窗口后台时任务栏闪烁提示）。
     - 审计日志 `%LOCALAPPDATA%\VGList\mcp\audit.jsonl` 仅记录时间、客户端、工具、条数、结果与变更集 Id（**绝不记录任务标题或正文**），超 1MB 自动滚动。
- 影响：`harness/architecture.md`、`harness/features/assistant.md`、`VGList.slnx`、`MainSettings.cs`、`MorningBriefService.cs`、`VGList.Platform.Win`（管道 ACL 与任务栏闪烁）、`VGList.App/Mcp/*`、`VGList.Mcp/*`、`TodayWidget*`、`TasksPage*`、`MainWindow*`、`SettingsPage*`。

### ADR-0053 · P1 六项效率增强（重复任务、回收站、标签筛选、四象限拖拽、导入导出、自启与小组件锁定穿透）（2026-11-21）
- 状态：accepted
- 决策：
  1. **SQLite 架构迁移 v3（`003_p1_features.sql`）**：`tasks` 表新增 `tags_json`（JSON 数组字符串）、`recurrence`（整型枚举）与 `deleted_at`（软删除时间，为空表示正常任务），保留 100% 向后兼容；启动时自动清理超过 30 天的回收站过期条目。
  2. **重复任务**：支持 `Daily / Weekdays / Weekly / Monthly / Yearly / LunarYearly`（农历每年复用 `CnCalendar`）；完成重复任务时在单事务、单 `ChangeSet` 内同时标记本期完成并生成下一期（重置子待办），撤销即原子回滚。
  3. **回收站（软删除）**：`Delete(id)` 改为写 `deleted_at`（默认查询 `IncludeDeleted=false` 自动隐藏），主窗口侧栏底部新增「回收站」视图，提供单条「恢复」、「彻底删除」与「清空回收站」，且删除本身仍支持即时 Toast/`Ctrl+Z` 撤销。
  4. **标签与筛选器**：支持任务多标签与录入框 `#标签` 自动解析；任务页顶部新增轻量化标签与重复筛选胶囊栏，行内展示紧凑标签胶囊与重复图标，详情与快速编辑卡支持增删标签与设置重复规则。
  5. **四象限跨卡片拖拽改优先级**：在四象限页支持按住任务行拖入任意象限卡片（>6px 启动浮影预览 + 目标象限高亮），松手经 `IScheduleService` 更新优先级并弹出可撤销 Toast；短按仍打开悬浮编辑卡互不干扰。
  6. **数据导入 / 导出备份**：支持完整 JSON、分组 Markdown 与标准 RFC 5545 `.ics`（含 `RRULE`）导入与导出，导入均经 `IScheduleService` 原子落盘并可撤销。
  7. **开机自启动 & 小组件锁定 / 点击穿透**：通过 `HKCU\...\Run` 实现免管理员开机自启（支持 `--minimized` 启动即藏入系统托盘）；小组件新增独立 `IsLocked`（禁止拖动与缩放）与 `IsClickThrough`（Win32 `WS_EX_TRANSPARENT` 鼠标穿透），系统托盘菜单额外提供「解除小组件点击穿透」安全出口。
- 影响：`harness/product.md`、`VGList.Core`、`VGList.Data`（003 迁移）、`VGList.App`、`VGList.Platform.Win`、`VGList.Widgets`、`VGList.Desktop`。

## ADR-0054 实机视觉与日历体验打磨（子待办圆圈居中、已完成按日分组、去 DWM 四角残影、月历动态行数）

- 日期：2026-11-21
- 状态：accepted（用户实机 4 点反馈）
- 结论：
  1. **子待办饼图圆圈光学居中**：移除 `TaskRowControl` 与 `TaskQuickCreateCard` 上将圆圈上推的底部 `1px` 边距，改为轻度下沉微调（`Margin="6,1.5,0,0"` / `Margin="0,1,0,0"`），与中文文字视觉中心对齐。
  2. **「已完成」页按日期折叠分组（越近越上面）**：`MainSection.Completed` 改用可折叠日期分组栏（今天 → 昨天 → 前天 → 具体日期，按完成日期降序，缺失 `CompletedAt` 时回退任务日期），跨年显示年份；侧栏「已完成」数字绑定全局已完成总数 `TotalCompletedCount`。
  3. **消除透明桌面小组件四角偏白残影**：Windows 11 下对无边框透明窗口调用 `DWMWCP_ROUND` 会叠加系统 8px 圆角框与 1px 半透明白边，在月历小组件 16px 圆角外露出四角残影。改为对透明小组件调用 `WindowEffects.TryDisableDwmChrome`（`DWMWCP_DONOTROUND` + `DWMWA_BORDER_COLOR = DWMWA_COLOR_NONE`），由 Avalonia 自身 `RootCard` 圆角与 `Stroke.Widget` 细描边统一绘制。
  4. **月历动态周行数（4 / 5 / 6 行）**：月历小组件与主窗口日历月视图均按 `(firstDayOfWeek + daysInMonth + 6) / 7` 动态计算当月实际周数（如 2026 年 11 月为 5 行 35 格、2026 年 2 月为 4 行 28 格、2026 年 8 月为 6 行 42 格），不再出现整行全为下月日期的冗余第 6 行，单元格芯片容量同步按实际行数计算。
- 影响：`TaskRowControl.axaml`、`TaskQuickCreateCard.axaml`、`TodayWidgetWindow.axaml`、`DateLabels.cs`、`TasksPageViewModel.cs`、`MainWindow.axaml`、`NativeMethods.cs`、`WindowEffects.cs`、`WidgetHost.cs`、`MonthWidgetWindow(.axaml/.cs)`、`MonthWidgetViewModel.cs`、`CalendarPageView(.axaml/.cs)`、`CalendarPageViewModel.cs`。

### ADR-0055 · Lana 扩展五项：日程时间与原生通知提醒（含反复提醒）、月日中文日期格式、去除冗余引导文案、自动优先级判别与卡片即时调参、并行会话标题生成（2026-11-21）
- 状态：accepted
- 决策：
  1. **日程具体时间 + 灵活提醒 + 反复提醒 + Windows 原生通知**：
     - SQLite 迁移 v4（`004_reminders.sql`）：`tasks` 表新增 `due_time`（`HH:mm` 可空）与 `reminder_json`（JSON 序列化 `TaskReminder`：`Enabled`、`OffsetMinutes` 默认 `10`、`AbsoluteAt`、`RepeatIntervalMinutes`、`RepeatMaxCount`、`FiredCount`、`LastFiredAt`）。
     - 当日程含具体时间（如「明天早上九点交软著文档」）时，Lana 自动识别 `DueTime` 并默认设置**提前 10 分钟提醒**；同时支持提前任意时间（准时 / 任意分钟 / 小时 / 天）及**反复提醒**（按设定间隔重复直至完成或达到次数上限）。
     - 提醒既可在 **Lana 同步/提案卡片行内直接修改**（点击即生效，无需确认按钮），也可在**任务详情面板手动设置**；后台由常驻 `ReminderScheduler`（`VGList.App`，零 Agent 依赖）每 15 秒扫描到期提醒，通过 `WindowsNotificationService`（`VGList.Platform.Win`，纯 Win32 `Shell_NotifyIconW` `NIF_INFO` 零 NuGet 依赖，未打包 `.exe` 亦可在 Windows 10/11 操作中心弹出原生通知并点击唤起主窗口）送达。
     - 重复任务完成生成下一期时继承 `DueTime` 与 `Reminder` 配置并归零触发状态；任务改期或调整提醒配置时自动重置触发状态。
  2. **卡片日期胶囊统一月日中文形式**：Lana 日程卡片中日期胶囊改用 `今天 (M月d日)` / `明天 (M月d日)` / `后天 (M月d日)` / `昨天 (M月d日)` / `M月d日`（有时间时附 `HH:mm`），不再出现 `11/22` 斜杠写法。
  3. **去除傻瓜式提示文字**：移除 Lana 同步/提案卡片标题下方的「点击单条『撤销』、勾选多项撤销，或直接全部撤销」及「勾选要执行的条目后点击确认 · 应用后随时可撤销」等解释显而易见交互的副标题；后续界面亦不得新增此类文案。
  4. **Lana 自动判别优先级 + 卡片内点击即调优先级**：系统提示词与 `ProposalTools` 要求 Lana 为每条新建日程自动判别并赋予合适优先级（模型省略时由规则兜底推断）；卡片每条任务行直接显示当前优先级胶囊及右侧小下拉箭头，点击弹出窗内下拉菜单，**点击选项即刻生效**（已同步直接写仓储，待确认直接改提案操作），无须确认按钮。
  5. **首条消息并行自动生成会话标题**：新会话首次发送时，立即以独立轻量 `IChatClient` 并行发起 `GenerateSessionTitleAsync` 与主对话流式回复同时执行，互不阻塞等待；生成完成后写入 `IConversationStore.UpdateSessionTitle` 并实时刷新左侧历史栏标题（若用户已手动改名或删除则不覆盖）。
- 影响：`VGList.Core`（`TaskReminder.cs`、`TaskItem.cs`、`TaskDraft.cs`、`ChangeModels.cs`）、`VGList.Data`（`004_reminders.sql`、`SqliteDatabase.cs`、`SqliteTaskRepository.cs`）、`VGList.App`（`InMemoryTaskRepository.cs`、`TaskBackupService.cs`、`ReminderScheduler.cs`、`AssistantContracts.cs`）、`VGList.Platform.Win`（`WindowsNotificationService.cs`）、`VGList.Agent`（`SystemPromptBuilder.cs`、`ProposalTools.cs`、`ReadTools.cs`、`ProposalStore.cs`、`AssistantHost.cs`）、`VGList.Desktop`（`App.axaml.cs`、`AssistantPageView*`、`TaskDetailPanel*`、`MainTaskDetailViewModel.cs`、`TaskRowControl.axaml`、`MainTaskRowViewModel.cs`）。

### ADR-0056 · 详情顶栏响应式统一、2026 官方模型目录与多模态自动识别、输入框 `@` 引用附件、拖拽防闪屏、Toast 通知与多格式附件支持（2026-11-22）
- 状态：accepted
- 决策：
  1. **任务详情面板顶栏响应布局 + 统一高度 + 去除冗余笔记标签**：
     - `TaskDetailPanel` 顶栏改为 `Auto, Auto, *, Auto`（左侧复选框与分隔线、中间 `<WrapPanel>` 包裹日期/提醒/重复/优先级四枚胶囊、右侧固定关闭按钮）；四枚胶囊统一 `Height="26"`、`CornerRadius="8"`、`Padding="8,0"`，窗口/详情面板变窄时自动换行，绝不重叠或挤掉关闭按钮。
     - 移除 `MainTaskDetailViewModel.ContentMetaLabel` 的「纯文本 / Markdown 笔记」及「待办事项 · 回车添项」占位文案，仅在有真实字数或子待办进度时显示。
  2. **按 2026 最新官方文档升级全部 20 家厂商默认模型目录、支持快速新增模型并自动识别图片输入能力**：
     - `providers.json` 为每家厂商的接入版本补充 `models` 数组（含官方 `id`、`supportsImages` 与简短说明），并将默认模型升级为最新官方文档主力（如 DeepSeek 默认 `deepseek-flash` 原生支持图片 + 可选 `deepseek-v4-pro` 纯文本；OpenAI `gpt-5.4`/`gpt-6.2-sol`；Anthropic `claude-sonnet-5-5`/`claude-opus-5-5`；Gemini `gemini-3-flash`；智谱 `glm-5.3-flash`/`glm-5v-turbo`；Kimi `kimi-k3`/`kimi-k3.1-pro`；千问 `qwen3.5-plus`/`qwen4-max`；火山方舟 `doubao-seed-2-0-lite-260428`；MiniMax `MiniMax-M3.1-Flash`；StepFun `step-3.7-flash`；Xiaomi MiMo `mimo-v2.6-flash`；LongCat `LongCat-2.5`；百度千帆 `ernie-5.5-flash-preview`；腾讯混元 `hunyuan-vision-1.5-instruct` 等）。
     - `ProviderCatalog` 新增 `TryGetKnownSupportsImages` 自动识别已知官方模型与视觉族是否支持图片输入；`ModelProfileStore` 读取/解析已有配置时对已确认支持多模态的模型（如已保存的 `deepseek-flash`）自动修正 `SupportsImages = true`，用户无需再手动进设置开启。
     - 设置页模型抽屉直接展示官方预设模型胶囊（含多模态标识，点击切换）及「+ 新增模型」快捷输入，并实时显示自动识别结果。
  3. **删掉输入框右下角冗余文案 + `@` 改为引用输入框内已添加的图片/文件**：
     - 删除 `@ 引用文件 · Ctrl+V 粘贴 · Enter 发送` 冗余文字；`@` 不再扫描电脑桌面/文档/下载目录，改为弹出当前输入框已附加的图片与文件列表，选中后插入 `@文件名 ` 供 Lana 精准定位（同时在 `AssistantHost` 中为图片附件附带 `[图片附件: 文件名]` 标识）。
  4. **彻底消除拖拽文件移动鼠标时的提示层闪屏**：
     - 取消拖拽遮罩的重复入场动画；在 `AssistantPageView` 监听 `DragEnter` + `DragOver` 并使用 70ms `DragLeave` 防抖计时器，彻底避免鼠标在子控件边界间移动触发冒泡 `DragLeave`/`DragEnter` 导致的闪屏。
  5. **附件错误与提示改为 Toast 通知**：
     - 移除输入框上方常驻的黄色 `AttachmentNotice` 提示条，所有附件数量上限、体积超限、格式不支持或纯文本模型禁图提示统一通过顶栏自动消失的 `ToastService` 通知展示。
  6. **大幅扩展支持的附件文件格式（统一真源 `AttachmentFormats`）**：
     - 图片：`.png .jpg .jpeg .webp .bmp .gif .tif .tiff .ico`；
     - Office / 文档：`.pdf`、`.docx`（Word）、`.xlsx`（Excel 原生解析为 Markdown 表格）、`.pptx`（PowerPoint 原生分页提取）、`.epub`、`.rtf`；
     - 网页 / 日历 / 数据：`.html .htm .xhtml .svg .ics .json .jsonl .xml .yaml .yml .toml .csv .tsv .ini .conf .cfg .env .log .sql`；
     - 纯文本与主流源码：`.txt .md .markdown .rst .tex .org .cs .py .js .ts .tsx .jsx .java .kt .go .rs .c .cpp .h .hpp .css .scss .less .sh .ps1 .bat .cmd .rb .php .swift .dart .vue .svelte .axaml .xaml .csproj .sln .slnx`。
- 影响：`TaskDetailPanel.axaml`、`MainTaskDetailViewModel.cs`、`AttachmentFormats.cs`、`AttachmentProcessor.cs`、`providers.json`、`ModelModels.cs`、`ProviderCatalog.cs`、`ModelProfileStore.cs`、`ModelServicesViewModel.cs`、`SettingsPageView(.axaml/.cs)`、`AssistantHost.cs`、`AssistantPageView(.axaml/.cs)`、`AssistantPageViewModel.cs`。

### ADR-0057 · 跨线程渲染崩溃与卡死根治、附件及多步骤计划「先议定再执行」体验优化（2026-11-22）
- 状态：accepted
- 背景：实机使用中在发送 3 个 HTML 附件后暴露出两个问题：(1) `AssistantHost`、`ExternalAccessBridge`（MCP）与 `ReminderScheduler` 在线程池中写入仓储并触发 `Changed`/`ListsChanged` 时，UI ViewModel 直接在后台线程构建 `SolidColorBrush`（`ListSidebarItem.DotBrush`、`TasksPageViewModel.DraftListDotBrush` 等）与变更 `ObservableCollection`，导致下一帧渲染 `Shape.Render` → `Brush.OnReferencedFromCompositor` 抛出跨线程 `InvalidOperationException`（WER 写 dump 卡死约 12s 后闪退）；(2) 仅发附件未输文字时默认代发文案为「帮我看看…并整理待办吧」，且 Lana 在尚未与用户商量计划前就直接调用 `propose_*` 大批写入日程，体验草率。
- 决策：
  1. **全链路 UI 线程封送与防阻塞**：
     - 在 `VGList.Widgets.Support` 新增 `UiDispatcher.PostIfNeeded(Action)`（在存在 `Application.Current` 且不在 UI 线程时 `Dispatcher.UIThread.Post` 切回 UI 线程；单测或已在 UI 线程时同步执行）。
     - 为所有响应仓储/设置/模型存储变更的 ViewModel（`MainWindowViewModel`、`TasksPageViewModel`、`EisenhowerViewModel`、`CalendarPageViewModel`、`TodayWidgetViewModel`、`MonthWidgetViewModel`、`ModelServicesViewModel`、`AssistantPageViewModel`、`SettingsPageViewModel`）统一加装 UI 线程封送；同步缓存 `TasksPageViewModel.DraftListDotBrush` 避免每次读取重复分配画刷。
     - 将 `AssistantHost.RunAsync` 中的 `AttachmentProcessor.Process` 移入 `Task.Run` 后台工作线程执行，防止多份大附件解析阻塞 UI 线程。
  2. **先说明计划、征询意见，再动手执行**：
     - 仅发送附件未填写文字时，代发消息改为征询规划意图（`先帮我看看这个/这 N 个附件的内容，把你打算怎么安排日程的计划列出来跟我确认，同意后再执行`）。
     - 在 `SystemPromptBuilder` 中明确「先议定计划，再动手执行」准则（并在本轮携带附件且用户未明确要求立即写入时于系统上下文补充引导）：单条明确指令（如「明天早上 9 点交软著」）依然按权限直接执行；而携带文档附件或涉及多步骤/多天安排规划时，Lana 须先只读梳理并用自然亲切的口吻说明「Lana 打算这样安排：……（列出拟添加/调整的日程、日期、优先级、提醒与子待办思路）」，征询用户同意或调整意见；待用户确认后再调用 `propose_*` 工具写入日程。
- 影响：`UiDispatcher.cs`、`MainWindowViewModel.cs`、`TasksPageViewModel.cs`、`EisenhowerViewModel.cs`、`CalendarPageViewModel.cs`、`TodayWidgetViewModel.cs`、`MonthWidgetViewModel.cs`、`ModelServicesViewModel.cs`、`SettingsPageViewModel.cs`、`AssistantPageViewModel.cs`、`AssistantHost.cs`、`SystemPromptBuilder.cs`。

### ADR-0058 · Markdown 表格零横滚自适应排版、全应用纤细胶囊滚动条与工具调用实时转圈指示（2026-11-22）
- 状态：accepted
- 决策：
  1. **Markdown 表格取消横向滚动条、自适应换行排版** (`ChatMarkdownView.cs`)：移除表格外层 `ScrollViewer` 与单元格硬编码 `MaxWidth="300"`；按列内容视觉宽度自动区分紧凑列（`GridLength.Auto`）与长文本列（加权 `*` 比例列宽），单元格内容随可用宽度自动换行、行高与列宽自适应，彻底消除图 1 所示表格横滚裁切。
  2. **统一纤细圆角胶囊滚动条** (`Themes/MainWindow.axaml`)：全局设置 `ScrollViewer.AllowAutoHide="True"` 并重写 `ScrollBar`（竖向与横向）精简 `ControlTemplate`，移除粗大矩形灰底轨道与前后箭头按钮，复用 `Main.Scroll.Thumb` / `Main.Scroll.Thumb.Hover` token 绘制 4px（悬停 6px）半透明圆角细条。
  3. **工具调用左侧转圈圆圈与可视状态切换** (`ToolSpinnerRing.cs`、`LanaThinkingOrb.cs`、`AssistantPageView.axaml`、`AssistantPageViewModel.cs`)：新增矢量自绘旋转弧圈 `ToolSpinnerRing`（不可见/离线/减少动效时立即停表零开销）置于运行中工具胶囊左侧；对瞬时（`< 300ms`）完成的本地工具调用在 UI 上提供不阻塞后台流的最小旋转可见窗口（`300ms`），完成后在同一槽位平滑切为 `✓`（或 `!`）。
- 影响：`ChatMarkdownView.cs`、`MainWindow.axaml`、`ToolSpinnerRing.cs`、`LanaThinkingOrb.cs`、`AssistantPageView.axaml`、`AssistantPageViewModel.cs`。

### ADR-0059 · 今日小组件移除早安提示条，改为直接列出「已过期」任务并提供「顺延到今天」按钮（2026-11-22）
- 状态：accepted
- 决策：
  1. 移除今日小组件顶部「早安！昨天还有…」提示横幅（`MorningBriefBanner`），不再通过跳转 Lana 间接处理逾期任务。
  2. 今日小组件「今天」（与「最近7天」）视图与主窗口任务页保持一致：当存在未完成的逾期任务（`Date < Today`）时，在列表顶部直接展示「已过期」分组（含件数与红色逾期日期高亮），并在分组头右侧提供一键「**顺延到今天**」按钮；点击后通过 `IScheduleService` 原子批量顺延至今天并实时同步所有窗口（无逾期任务时仍保持无分组头的极简列表）。
- 影响：`TaskGroupViewModel.cs`、`TaskRowViewModel.cs`、`TodayWidgetViewModel.cs`、`TodayWidgetWindow(.axaml/.cs)`、`TodayWidget.axaml`、`TodayWidgetViewModelTests.cs`。

### ADR-0060 · 系统托盘浅色右键菜单美化与 Plana Windows 安装程序打包（2026-11-22）
- 状态：accepted
- 决策：
  1. **系统托盘右键菜单浅色化与字体统一** (`App.axaml`)：`Application.RequestedThemeVariant` 由 `Dark` 切为 `Light`（深色桌面小组件各自显式声明 `RequestedThemeVariant="Dark"` 不受影响），并在 `App.axaml` 全局定制 `ContextMenu` / `MenuItem` / `Separator`（纯白 `Main.Bg`、`Main.Divider` 细边框、`CornerRadius="12"`、`Font.Ui` 字体与 `Main.Hover` 圆角悬停态），使托盘右键菜单与主窗口视觉风格完全一致。
  2. **Plana Windows 安装程序（Inno Setup 6 + 自包含 win-x64）** (`installer/Plana.iss`、`scripts/build-installer.ps1`)：
     - 产品名统一为 `Plana`，使用 `plana.ico` 图标与自包含 `dotnet publish -r win-x64 --self-contained true`（测试机免装 .NET 运行时，同时打包 `VGList.Mcp.exe`）；
     - 固定 `AppId` 且默认安装于当前用户目录 `{localappdata}\Programs\Plana`（免管理员提权），所有用户数据继续持久化在 `%LOCALAPPDATA%\VGList\`（与程序目录彻底分离）；后续新版本安装程序可直接覆盖安装升级，自动检测并关闭旧进程，用户任务、设置、模型 Key 与 Lana 记忆全部完整保留。
- 影响：`App.axaml`、`VGList.Desktop.csproj`、`installer/Plana.iss`、`scripts/build-installer.ps1`。

### ADR-0061 · 全新安装默认空白日程（不注入示例任务）与默认主题切为经典蓝白（2026-11-22）
- 状态：accepted
- 决策：
  1. **全新安装零预设示例日程** (`SampleData.cs`)：`OpenPersistentRepository` 首次初始化空数据库时仅创建内置「收集箱」清单，不再自动写入开发期的 14 条演示任务（`CreateSeedTasks` 仅保留给设置页「重置为示例数据」手动触发）。
  2. **初始默认主题改为经典蓝白** (`MainSettings.cs`、`AppTheme.cs`、`App.axaml`、`SettingsPageView.axaml`)：新安装默认启用 `ClassicBlue`（经典蓝白）主题，`AppTheme.Normalize(null)` 兜底为 `ClassicBlue`，仍保留 `Plana 淡粉` 作为设置页可切换主题；已保存过主题偏好的老用户保持原有选择不变。
- 影响：`SampleData.cs`、`MainSettings.cs`、`AppTheme.cs`、`App.axaml`、`SettingsPageView.axaml`。

### ADR-0063 · 内嵌中文思源字体（修复无思源机器回退雅黑）与多目标构建修复（2026-11-22）
- 状态：accepted
- 背景：两台均装 0.1.1 的电脑字体渲染不一致——英文/数字一致（Anthropic 字体已内嵌），汉字一细一粗。根因：中文思源字体（Noto Sans SC / Noto Serif SC）未随包分发，依赖系统安装；开发机恰好装有 `NotoSansSC-VF.ttf` / `NotoSerifSC-VF.ttf`，无思源的机器回退微软雅黑（更粗）。
- 决策：
  1. **中文思源可变字体一并内嵌**：`NotoSansSC-VF.ttf`（17 MB）与 `NotoSerifSC-VF.ttf`（24.5 MB）复制到 `VGList.Widgets\Assets\Fonts`，与 Anthropic 字体共用同一 `EmbeddedFontCollection`；`CreateMappings()` 增加 `Noto Sans SC` / `Noto Serif SC` → `fonts:Anthropic#...` 映射，字体链无需改动。安装包 37.3 → 56.2 MB。
  2. **修复 Directory.Build.props 全局 TargetFramework 短路多目标**：全局 `<TargetFramework>net10.0</TargetFramework>`（单数）会使 `VGList.Desktop` / `VGList.Widgets` 的 `TargetFrameworks=net10.0-windows;net10.0` 双目标展开被短路（只构建 net10.0，`#if WINDOWS` 类型缺失导致测试 CS0246；此前被 obj 残留旧产物掩盖）。改为各项目显式声明 TFM：App/Platform/Platform.Mac/Agent/Data/Core 六个 csproj 各加 `<TargetFramework>net10.0</TargetFramework>`，Directory.Build.props 移除全局行。
  3. **CopyMcpServerToOutput 限定 inner build**：多目标 outer build 无 `$(OutDir)` 触发 MSB3023，加 `Condition="'$(TargetFramework)' != ''"` 与空列表防御。
  4. **build-installer.ps1 的 Desktop publish 显式 `-f net10.0-windows`**：多目标项目 publish 必须指定 TFM（NETSDK1129）。
  5. **0.1.3 字体补漏**：对话 Markdown 代码块原硬编码 `Cascadia Mono` 链，绕过内嵌字体，改为 `Anthropic Mono` 优先；「纯净黑体」预设的等宽字体同样改为 Anthropic Mono 优先；今日/月历/任务详情/小组件设置四个窗口补 `FontFamily="{DynamicResource Font.Ui}"`（此前继承系统默认字体）；字体映射补充旧家族名 `Anthropic Sans Web Text` / `Anthropic Serif Web Text` 别名，避免平台按 nameID 1/16 解析差异导致回退。
  6. **仍为系统字体的部分（有意保留）**：`System` 预设（按定义使用系统字体）、`AnthropicClassic` 预设的中文 SimSun（微软专有字体不可再分发，Windows 各机型一致）、彩色 Emoji（Segoe UI Emoji 专有，Windows 各机型一致）；其余所有文本角色均以内嵌字体为首选。
- 验证：244 项单测全通过（含新增断言：两个思源 VF TTF 嵌入 `!AvaloniaResources`、Noto SC 与旧家族名映射指向嵌入集合、除系统预设外所有字体预设首选字体均内嵌）；0.1.2 安装包内 `VGList.Widgets.dll`（42.4 MB）实测含全部 5 个字体。
- 影响：`AnthropicFontCollection.cs`、`LanaPolishTests.cs`、`Directory.Build.props`、6 个 csproj、`VGList.Desktop.csproj`、`build-installer.ps1`、`VGList.Widgets\Assets\Fonts\`。

### ADR-0062 · P0 收口细节修正：提示条圆角阴影、默认字体、小组件勾选刷新、模型服务状态点与设置文本裁切（2026-11-22）
- 状态：accepted
- 决策：
  1. **Toast 阴影改为跟随圆角**：撤销提示条由 `BoxShadow` 改为 `DropShadowEffect`（BlurRadius 20 / OffsetY 8 / 12% 黑），消除圆角外侧露出的灰色方角。
  2. **助理侧栏身份文案**：底部说明由「Lana · 本地优先，随时可撤销」改为「Lana · 你的日程小助手」。
  3. **小组件任务详情勾选即时反馈**：`TaskDetailViewModel.ToggleCompleted` 在仓储 `SetCompleted` 后同步当前 `TaskItem` 实例的 `IsCompleted` / `CompletedAt`，修复 SQLite 仓储更新的是数据库副本、弹卡界面不刷新（实际已完成）的问题。
  4. **默认字体改为 Anthropic + 思源宋体**：`MainSettings.FontPreset` 与 `PlanaTokens.axaml` / `BlueTokens.axaml` 的 `Font.Ui` / `Font.Display` / `Font.Mono` 默认值改为 Anthropic 优先；设置页移除旧「默认（思源）」预设，旧版已保存的 `Default` 在运行期按新默认解析（ADR-0051 的默认项被取代）。
  5. **模型服务设置不再显示状态圆点**：移除左侧导航「模型服务」绿点、「当前使用」徽标绿点、未配置提示橙点、已保存实例绿点与图片能力提示圆点，只保留文字。
  6. **设置页说明文字换行**：`TextBlock.set-desc` 全局启用 `TextWrapping="Wrap"`，修复「导出数据备份」等长说明被按钮列挤占后截断吞字。
- 影响：`ToastBanner.axaml`、`AssistantPageView.axaml`、`TaskDetailViewModel.cs`、`MainSettings.cs`、`AppTheme.cs`、`PlanaTokens.axaml`、`BlueTokens.axaml`、`SettingsPageViewModel.cs`、`SettingsPageView.axaml`、`LanaPolishTests.cs`、`harness/features/model-settings.md`。

### ADR-0064 · 月历单元格芯片容量按真实纵向开销计算，修正最后一条被底边格线裁切（2026-11-23）
- 状态：accepted
- 背景：月历单元格容量沿用「格子高度 − 34」估算，但真实固定开销为格线 1 + 上下内边距 10 + 日号行 24 + 芯片上间距 3 = 38，导致估算容量多出 4px，最后一条任务条总被单元格底边裁掉（小组件缩到 454 高时最明显，只能看到 2 条且第二条贴边截断）。
- 决策：
  1. 单元格局内边距由 `6,5` 收紧为 `6,4`，芯片区上间距由 3 收紧为 2，固定开销降为 35（格线 1 + 内边距 8 + 日号行 24 + 间距 2）。
  2. 新增纯函数 `MonthWidgetViewModel.ComputeChipCapacity(cellHeight)`：`capacity = 1 + floor((cellHeight − 35 − 16) / 18)`，并夹在 1–5；窗口只负责传入格子高度，容量计算可单测。
  3. 容量只按能完整放下的行数取整，不再让最后一条溢出格底；默认 544 高度下 5 行月份 3 条、6 行月份 2 条，最小 436 高度下 5 行月份 2 条、6 行月份 1 条。
  4. 单元格内层 Grid 增加 `ClipToBounds`，任何异常内容也不越出格线。
- 影响：`MonthWidgetWindow.axaml`、`MonthWidgetWindow.axaml.cs`、`MonthWidgetViewModel.cs`、`MonthWidgetViewModelTests.cs`、`harness/features/month-widget.md`。

### ADR-0067 · 中文思源可变字体默认字重重定基到 400（修复正文发灰、过细）（2026-11-23）
- 状态：accepted
- 背景：ADR-0063 内嵌了 `NotoSansSC-VF.ttf` / `NotoSerifSC-VF.ttf`，但这两个可变字体的默认实例分别是 Thin(100) / ExtraLight(200)。Avalonia 在普通正文（`FontWeight=Normal`）下未取 `wght` 变体轴，直接使用字体默认实例，导致安装 0.1.6 后中文任务标题、正文明显比英文细且发灰（用户截图反馈「灰色的、很细」）。
- 决策：
  1. 用 `fontTools.varLib.instancer.instantiateVariableFont` 将两个思源 VF 的字重轴重定基为 **400..900、默认 400**（`updateFontNames=True`，家族名仍为 `Noto Sans SC` / `Noto Serif SC`），文件名保持不变，嵌入与映射链路无需改动。
  2. 不再提供 400 以下字重（产品未使用）；400 及以上仍保留可变字重能力，`FontWeight` 变化时由排版层决定是否取轴。
  3. 该修复只影响内嵌思源字体，Anthropic Sans / Serif / Mono 与系统预设不变。
- 验证：Debug / Release 构建通过；251 项单测全通过；实机截图对比，任务标题中文恢复为深色常规字重，与英文 `Week1Day0` 视觉粗细一致；助理页宋体标题正常。
- 影响：`src/VGList.Widgets/Assets/Fonts/NotoSansSC-VF.ttf`、`NotoSerifSC-VF.ttf`（字体二进制）、`AnthropicFontCollection.cs` 注释、`harness/design-system.md` §4。

### ADR-0066 · 左侧导航 Lana 入口改为 Skia 五态动效头像（2026-11-23）
- 状态：accepted
- 背景：左栏助理入口原为 `34×34` 静态 `plana-pet.png`，与对话页的 Lana 形象割裂，也没有「待命 / 聆听 / 思考 / 完成」的反馈。用户提供 `LanaAvatarControl` 动效控件与落地指南，要求替换左栏显示样式。
- 决策：
  1. **新增 `VGList.Desktop/Controls/LanaAvatarControl.cs`**：SkiaSharp 自绘 + `ICustomDrawOperation`，支持 Sleep / Idle / Listening / Thinking / Happy 五态、悬停唤醒、点击果冻回弹、Happy 约 2s 自动回落；默认 60fps，Sleep 收敛后停止渲染循环。
  2. **左栏入口替换为 `LanaAvatarControl`（42×42，`RenderMode=Interactive`）**，`State` 绑定 `MainWindowViewModel.LanaNavState`；对话页欢迎主视觉与消息头像仍使用 `plana-pet.png`（仅左栏入口不再使用 PNG，取代 ADR-0044 对入口的静态图约束）。
  3. **状态推导集中到 `MainWindowViewModel`**：非助理页 = Sleep；助理页无生成 = Idle；底部输入框聚焦 = Listening；生成中 = Thinking；生成结束 = Happy，控件庆祝结束后回落。输入框聚焦由 `AssistantPageViewModel.IsComposerFocused` 提供。
  4. **性能与无障碍**：Sleep 收敛后不再请求动画帧，助理未打开时保持零常驻渲染开销；`MotionHelper.ReduceMotion` 开启时控件退化为静态缓存帧，并新增 `ReducedMotionChanged` 事件即时响应设置切换。
- 验证：251 项单测全通过；Debug / Release(self-contained) 构建通过；实机截图确认左栏 Sleep（任务页）与 Listening（对话页）渲染正常、状态随页面切换；`Plana-Setup-0.1.6.exe`（56.25 MB）构建成功。
- 影响：`LanaAvatarControl.cs`（新增）、`MainWindow.axaml`、`MainWindow.axaml.cs`、`MainWindowViewModel.cs`、`AssistantPageViewModel.cs`、`AssistantPageView.axaml.cs`、`MotionHelper.cs`、`harness/features/main-window.md`、`harness/features/assistant.md`、`harness/design-system.md`。

### ADR-0065 · 月历选中高亮只跟随当日日程浮层，关闭即清除（2026-11-23）
- 状态：accepted
- 背景：点击日期会写入 `SelectedDate`，但浮层关闭/点击浮层外区域/窗口失焦后从不清除，单元格 `.selected` 灰底永久残留；「今天/T」写入的选中态同样会留下无意义灰底。
- 决策：
  1. 选中高亮的生效条件改为 `IsAgendaOpen && SelectedDate == day.Date`，统一由 `RefreshDaySelection()` 在 `SelectedDate` 或 `IsAgendaOpen` 变化、以及 `BuildDays()` 重建网格时计算。
  2. `SelectedDate` 仍保留最近一次选中的日期（供快捷新建兜底），但浮层关闭后不再产生任何视觉选中态。
  3. 今天格继续使用 `Accent` 胶囊 + `Accent.Soft` 柔底，不再叠加选中灰底。
- 影响：`MonthWidgetViewModel.cs`、`MonthWidgetViewModelTests.cs`、`harness/features/month-widget.md`。

### ADR-0068 · 对话页舞台级活体 Lana（欢迎态居中待机 → 首轮飞向左侧 → 多轮滑落与历史静影留痕 + 全窗视线跟随鼠标）（2026-11-23）
- 状态：accepted
- 背景：ADR-0066 已将左侧导航栏入口升级为 Skia 五态动效 `LanaAvatarControl`，但对话页面内的欢迎主视觉、回复左侧头像、历史栏底标与拖拽提示仍使用静态 `plana-pet.png`，视觉割裂且缺少「Lana 就在对话画布中陪伴」的连续感；同时现有眼神跟随仅在指针悬停于头像自身边界内生效。
- 决策：
  1. **全窗视线跟随、柔焦聆听光晕与可见性省电（`LanaAvatarControl`）**：
     - 新增 `TrackWindowPointer` 属性（默认关闭；仅对话舞台 `StageLanaAvatar` 启用，左侧导航栏 `LanaNavAvatar` 关闭，侧边栏头像不随鼠标移动）：在顶层窗口监听 `PointerMoved` / `PointerExited`，按径向平滑函数 `tanh(dist / radius)` 计算单位圆内视线向量 `(LookX, LookY)`，结合本体 2.5D 轻倾、五官视差与眼球/高光二次视差，使唤醒态（`Idle` / `Listening` / 悬停 `WinkGreet`）的舞台 Lana 自然看向鼠标所在位置；指针离开窗口时平滑回正。
     - `Listening` 聚焦态光环改为边缘自然羽化的紫蓝柔焦弥散呼吸光晕（替代原细描边圆环），视觉更柔和且不裁切。
     - 增加 `IsEffectivelyVisible` 守卫：控件不可见（如离开助理页或滚出消息视口）或 `Sleep` 收敛后彻底停止帧循环与视线计算，保持助理未打开时零渲染开销；静图缓存上限放宽至 256px 并纳入 DPI 判断，保证 100%–200% DPI 清晰度。
  2. **对话页「唯一活体 Lana + 历史静影」舞台模型**：
     - 整个对话画布中仅存在**唯一一个** `Interactive` 模式的 `LanaAvatarControl`（`StageLanaAvatar`，置于受视口裁切的覆盖层 `LivingLanaLayer`），其状态与左侧导航栏 `LanaNavState` 保持单一事实来源同步（由 `AssistantPageViewModel.StageLanaState` 推导：非助理页 `Sleep`、空闲 `Idle`、输入聚焦 `Listening`、生成中 `Thinking`、正常回复完成后 `Happy` 庆祝 2s 回落；取消或报错不误触庆祝）。
     - **欢迎空态**：居中停靠于 `108×108` 主视觉锚位，与左栏同频待机/聆听并随鼠标转动眼神，支持悬停眨眼与点击果冻回弹。
     - **首轮开聊转场**：发送首条消息时欢迎文案柔和淡出（200ms），活体 Lana 从中心 `108×108` 以 `520ms` `cubic-bezier(0.77, 0, 0.175, 1)` 丝滑飞向首条助理回复左侧 `42×42` 头像位，途中切入 `Thinking` 流光环绕并在落位时轻弹缓冲。
     - **多轮接续（真身滑落 + 原地静影留痕）**：新的一轮消息开始时，活体 Lana 从上一条回复左侧平滑滑落（`440ms`）到最新回复左侧（若原锚位已滚出视口，则自动夹紧至视口边缘柔和滑入，避免跨屏高速穿行）；原位置延时 `80ms` 在 `280ms` 内淡入一枚零帧开销的静态缓存 Lana（`RenderMode=StaticAvatar`，`Opacity=0.82`）作为历史对话锚点，既保留对话阅读节奏，又突出唯一真身伴随感。
     - **新会话与会话切换**：点击「新对话」回到空态时，活体 Lana 柔和浮回居中主视觉（`420ms`）；切换到已有历史会话、窗口缩放、侧栏折叠或消息滚动时即时贴合锚点零延迟；滚出视口自动暂停渲染；「减少动效」开启时自动退化为无飞行的即时切换与静态缓存帧。
     - **页面内形象统一**：文件拖拽提示同步改用 `LanaAvatarControl`（`StaticAvatar` 模式）；历史侧栏底标「Lana · 你的日程小助手」去除多余的 Lana 图标，保持简洁纯文字居中展示。
- 影响：`LanaAvatarControl.cs`、`LanaStageMotion.cs`（新增）、`AssistantPageView(.axaml/.cs)`、`AssistantPageViewModel.cs`、`MainWindowViewModel.cs`、`LanaPolishTests.cs`、`harness/design-system.md`、`harness/features/assistant.md`。

### ADR-0069 · 消除 ToastBanner 圆角外侧灰色尖角阴影（2026-11-23）
- 状态：accepted
- 背景：ADR-0062 为 `ToastBanner` 启用了 `DropShadowEffect`（`BlurRadius=20, OffsetY=8`），但 Avalonia 12 的合成器会将 `Effect` 渲染表面裁切在控件自身矩形包围盒 `[0, 0, Width, Height]` 内，导致圆角胶囊外的阴影被一刀切平，仅在左下、右下角残留两块灰色尖角（截断的阴影三角形）。
- 决策：
  1. 移除 `ToastBanner.axaml` 中的 `DropShadowEffect`，改用与全站浮层一致的圆角 `BoxShadow="{DynamicResource Main.Popup.Shadow}"`（`BorderRenderHelper` 按 `CornerRadius` 路径绘制柔和圆角外阴影，不会出现矩形裁切尖角）。
  2. 为 `Pill` 增加透明留白外边距（`Margin="24,12,24,28"`，并在 `MainWindow.axaml` 将宿主 `ToastBanner` 的 `Margin` 调整为 `0` 以维持胶囊距顶栏 12px 的视觉位置），保证进场位移与阴影扩张空间充分。
- 影响：`ToastBanner.axaml`、`MainWindow.axaml`。

### ADR-0070 · 恢复打开 Lana 页面时懒加载持久化历史会话与消息（2026-11-24）
- 状态：accepted
- 背景：用户反馈覆盖升级后 Lana 历史对话似乎被抹除。核查 `%LOCALAPPDATA%\VGList\plana.db` 后确认 `assistant_sessions`（30 条）与 `assistant_messages`（154 条）均完整保留，安装脚本并未清库；真实根因是 `AssistantPageViewModel` 仅在内存中初始化一条「新对话」，从未在打开助理页时从 `IConversationStore` 读取已有会话与消息，导致每次重启或覆盖升级重开进程后历史栏空白。
- 决策：
  1. **严守懒加载边界读取历史（Constitution 第 13 条）**：`AssistantPageViewModel` 直接使用常驻数据层的 `IConversationStore`（`SqliteConversationStore`，位于 `VGList.Data`，不引入 `VGList.Agent`），且延迟到首次进入助理页（`IsPageActive == true`）时调用 `EnsurePersistedSessionsLoaded()` 拉取最近 30 条持久化会话列于初始「新对话」下方，确保未打开助理页时零额外启动开销与零 Agent 程序集加载。
  2. **按需加载单会话消息并支持续聊/编辑/删除**：点击历史会话时由 `SelectSession` 按 `SessionSummary.Id` 懒加载 `LoadMessages(sessionId)`（含文本、模型标签、附件元信息与时间戳），并延续原 `SessionId` 供后续追问无缝续接上下文；`DeleteSession`、`CommitEditMessageAsync` 与 `RetryMessageAsync` 在 `_host` 尚未实例化时同步回落操作 `_conversationStore`，避免删改失效。
- 影响：`AssistantPageViewModel.cs`、`LanaPolishTests.cs`。

### ADR-0071 · 拆分内嵌字体集合与显式集合 URI 修复无思源环境中文回退宋体失败（2026-11-24）
- 状态：accepted
- 背景：用户反馈在另一台未系统安装思源字体的电脑上安装 `0.1.10` 后（设置确认选为「Anthropic + 思源宋体」），对话页英文仍显示宋体/衬线，而中文完全回退系统宋体（`SimSun`），并非思源宋体。经 Avalonia 12 `FontManager.TryMatchCharacter` 实测确认两个叠加根因：
  1. `FontFamilyMappings` 在 Avalonia 12 中不会用于复合 `FontFamily` 逗号列表里的回退家族（也不用于 `TryMatchCharacter` 的字符级回退）：写作 `"Anthropic Serif, Noto Serif SC, ..."` 时，遇到 `Anthropic Serif` 缺字的中文字符，`TryMatchCharacter` 直接向 `fonts:SystemFonts` 查找 `"Noto Serif SC"`；开发机恰好在 `C:\Windows\Fonts` 安装了 `NotoSerifSC-VF.ttf` 与 `NotoSansSC-VF.ttf` 掩盖了问题，未装思源的机器则跳过内嵌思源直落系统字体。
  2. 将 `Anthropic`、`Noto Sans SC`、`Noto Serif SC` 全部放入同一 `EmbeddedFontCollection`（`fonts:Anthropic`）会导致集合内越权回退：`Anthropic Serif Web` 缺字时，`FontCollectionBase.TryMatchCharacter` 会在离开集合前遍历集合内其它字体，按顺序先命中 `Noto Sans SC`（黑体），根本不会走到后面的 `Noto Serif SC`。
- 决策：
  1. **按字族隔离为三个独立内嵌字体集合**：
     - `AnthropicFontCollection`（`fonts:Anthropic`）仅加载 `AnthropicSansWebText.ttf`、`AnthropicSerifWebText.ttf`、`AnthropicMonoVariable.ttf`，不再混入任何 CJK 字体，使中文字符查询干净返回未命中并进入下一回退项；
     - 新增 `NotoSansSCFontCollection`（`fonts:NotoSansSC`）仅加载 `NotoSansSC-VF.ttf`；
     - 新增 `NotoSerifSCFontCollection`（`fonts:NotoSerifSC`）仅加载 `NotoSerifSC-VF.ttf`。
     由 `AnthropicFontCollection.RegisterAll(FontManager)` 统一注册三者，且保留各自 `CollectionKey` 供单测与显式引用。
  2. **`Font.Ui` / `Font.Display` / `Font.Mono` 使用显式 `fonts:<Key>#<Family>` 复合链表**：`AppTheme.ResolveFonts` 及 `BlueTokens.axaml` / `PlanaTokens.axaml` 中的内嵌族统一以 `fonts:Anthropic#...`、`fonts:NotoSerifSC#Noto Serif SC`、`fonts:NotoSansSC#Noto Sans SC` 声明，尾部保留系统字体与 Emoji（`Segoe UI Emoji` 等）系统回退。
  3. **`CreateMappings()` 同步更新映射目标**：`"Noto Sans SC"` 映射到 `fonts:NotoSansSC#Noto Sans SC`，`"Noto Serif SC"` 映射到 `fonts:NotoSerifSC#Noto Serif SC`，避免单独引用名称时指向错误集合。
- 影响：`AnthropicFontCollection.cs`、`Program.cs`、`AppTheme.cs`、`BlueTokens.axaml`、`PlanaTokens.axaml`、`harness/design-system.md`、`LanaPolishTests.cs`。

### ADR-0072 · Lana 飞抵左侧后自左向右显现思考文字与多会话后台独立生成（2026-11-24）
- 状态：accepted
- 背景：
  1. 新一轮发起回复时，活体 `Lana` 正在从欢迎区（108×108）或上一轮位置飞向当前回复左侧槽位，`LanaThinkingOrb`（“Lana 正在思考…”）就已整句提前冒出，视觉上缺乏“就位后再开口思考”的顺畅感。
  2. `AssistantPageViewModel` 原先使用单一全局 `_runCts` / `IsGenerating` 管理模型流，导致用户在 Lana 回复期间点击「新对话」或切换到其他会话时调用 `StopGeneration()`，直接掐断上一会话正在进行的回复或工具调用。
- 决策：
  1. **就位门槛 + 240ms 自左向右轻柔显现**：`LanaStageMotion` 定义转场显现阈值（`FirstTurn` `t >= 0.78`，`NextTurn` `t >= 0.72`），由 `AssistantPageView` 门控 `ChatMessageViewModel.IsThinkingRevealed`；放行或无飞行时立即揭示，并在 `LanaThinkingOrb` 上通过 `RevealOnShow=True` 以 `240ms QuadEaseOut` 羽化渐变透明度自左向右流畅显现；开启「减少动效」或切回/取消飞行时立即完整显示，不拖沓且不卡死。
  2. **按会话粒度隔离生成生命周期**：将 `ActiveRunCts`、`IsGenerating`、`LastGenerationSucceeded` 收归每个 `ChatSessionItemViewModel` 独立持有；`StartNewSession()` 与 `SelectSession()` 不再调用 `StopGeneration()`，原会话继续在后台静默运行、更新消息块并落盘，多个会话可并发对话；切换会话时页面实时同步目标会话的 `IsGenerating` 与舞台状态（避免后台态误触 Happy 庆祝），左侧会话列表对正在后台生成的会话展示 11×11 `ToolSpinnerRing` 轻转圈指示。仅显式停止当前回复（`StopGeneration`）、删除运行中会话（`DeleteSession`）或视图模型释放（`Dispose`）时取消对应运行。
- 影响：`LanaStageMotion.cs`、`LanaThinkingOrb.cs`、`AssistantPageView.axaml`、`AssistantPageView.axaml.cs`、`AssistantPageViewModel.cs`、`harness/features/assistant.md`、`LanaPolishTests.cs`。




