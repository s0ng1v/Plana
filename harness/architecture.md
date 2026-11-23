# 技术架构共识

状态：agreed（2026-11-17）
更新时间：2026-11-19
前提：本机当前无 .NET SDK，需先安装 .NET 10 LTS。

## 1. 技术选型

| 层 | 选型 | 说明 |
|---|---|---|
| 运行时 | .NET 10 LTS | 待安装 |
| UI | Avalonia 12 + XAML | 原生多窗口、无边框、透明、置顶 |
| MVVM | CommunityToolkit.Mvvm | 只做 Observable/Command，不引入重型框架 |
| 数据（P1） | SQLite + Microsoft.Data.Sqlite + Dapper | P0 不建 Data 项目 |
| 系统集成 | Win32 P/Invoke 独立工程 | WorkerW 贴桌面、DWM 圆角/工具窗口、热键、托盘、通知 |

不采用 Electron / Tauri：违反 constitution 第 1 条。

## 2. 工程结构（P0）

```
VGList.sln
├─ src/
│  ├─ VGList.Core            领域模型与业务规则，无 UI 无 IO
│  ├─ VGList.App             用例、内存仓储、事件总线、设置
│  ├─ VGList.Desktop         主窗口与主窗口内的页面
│  ├─ VGList.Widgets         WidgetHost + 今日/月历小组件
│  └─ VGList.Platform.Win    Win32 互操作
└─ tests/
   └─ VGList.Tests           领域与仓储单测
```

依赖方向：`Desktop / Widgets → App → Core`；`Platform.Win` 仅被 UI 层引用。禁止 Core 引用任何 UI 或 IO。

P0 不创建 `VGList.Data`，接入 SQLite 时再建，`App` 中的仓储接口保持不变。

## 3. 数据契约（P0 假数据）

```csharp
public interface ITaskRepository
{
    IReadOnlyList<TaskItem> Query(TaskQuery query);
    TaskItem Create(TaskDraft draft);
    void Update(TaskItem task);
    void SetCompleted(string id, bool completed);
    void Delete(string id);
    event EventHandler<TaskChangedEventArgs> Changed;
}
```

- `TaskDraft`：标题、日期（可空）、优先级、清单 Id。
- `TaskChangedEventArgs`：变更类型 + 任务 Id，UI 层据此局部刷新。
- 所有窗口只依赖接口，内存实现 `InMemoryTaskRepository` 放在 `VGList.App`。
- 接入 SQLite 时新增 `SqliteTaskRepository`，不改任何 UI 代码。

## 4. 窗口策略

| 窗口 | 尺寸基准 | 属性 |
|---|---|---|
| 主窗口 | 1104 × 706 | 普通窗口，可最小化，单实例 |
| 今日日程小组件 | 331 × 454 | 无边框、可拖动、可缩放 80–200%、默认贴桌面、工具窗口（不占任务栏 / Alt+Tab） |
| 月历小组件 | 662 × 544 | 同上 |
| 任务详情小窗 | 505 × 429 | 由小组件点击任务唤起，复用详情控件 |

- P0 默认层级：**普通非置顶窗口**（不挂 WorkerW，见 ADR-0024）。设置可切「始终置顶」。
- 贴桌面（WorkerW）推迟到 P1：需要先解决桌面图标层拦截输入的问题，否则小组件不可交互。
- 位置、尺寸、层级、透明度持久化到本地设置文件；重启恢复。
- 单实例：命名互斥体；第二实例唤起主窗口。

## 5. 状态与刷新约定

1. 所有写操作走 `App` 层用例，不直接改模型。
2. 仓储触发 `Changed`，各窗口的 ViewModel 订阅并做局部刷新，禁止全量重建列表。
3. 列表必须虚拟化，只渲染可视区。
4. 动画与数据解耦：状态先变，动画后跑；动画不得阻塞提交。

## 6. 命名与代码约定

- 项目前缀统一 `VGList.`；命名空间与目录一致。
- View 后缀 `View`，ViewModel 后缀 `ViewModel`，服务后缀 `Service`。
- XAML 中禁止出现十六进制颜色字面量，必须引用 `DynamicResource` 令牌。
- 每个页面的样式资源放 `Themes/<页面名>.axaml`；公共令牌按主题拆成 `Themes/PlanaTokens.axaml`（默认）与 `Themes/BlueTokens.axaml`（经典蓝白），只在 `App.axaml` 的 `Application.Resources` 合并；窗口与页面一律引用 `DynamicResource`，禁止局部覆盖令牌。

## 7. 主题系统

- `MainSettings.Theme` 持久化到 `%LOCALAPPDATA%\VGList\settings.json`，取值 `Plana`（默认）/ `ClassicBlue`。
- 启动时 `AppTheme.Apply(theme)` 在创建任何窗口之前把对应主题字典逐键写入 `Application.Resources`；设置页切换调用 `WidgetCoordinator.SetTheme`，写盘后立即重新应用，所有窗口即时换肤。
- `Theme.Swatch.*` 是设置页主题选项的固定色样，不随主题变化。

## 8. 待定

- 月历小组件的农历算法库（自研表 or 现成库）。
- ~~亚克力在 Win10 的降级方案~~：P0 已取消小组件亚克力，改为透明窗口 + 背景层实色/真实 Alpha（ADR-0035）。


## 9. 助理阶段架构（2026-11-21，ADR-0042）

### 9.1 新增工程

```
src/
├─ VGList.Data      SQLite(WAL) 仓储、迁移、ChangeSet 日志与撤销（Core 无依赖，App 引用）
├─ VGList.Agent     助理内核：模型客户端、工具、提案、权限、规则校验、记忆（仅被 Desktop 懒加载引用）
└─ VGList.Mcp       外部 AI 接入：stdio 入口 + 本机命名管道桥（独立进程模式，不进主进程启动路径）
```

依赖：`Agent → App → Core`、`Data → Core`、`App → Data`（仅仓储实现）。`Agent` 不得引用 UI；`Desktop` 通过 `IAssistantHost` 接口 + 延迟加载使用 `Agent`。

### 9.2 性能分级落地

- 常驻档：`App.axaml.cs` 启动路径不 `new` 任何 Agent / Mcp 类型；助理入口按钮点击时才加载程序集。
- 助理活跃档：面板关闭 10 分钟后释放客户端与会话缓存。
- 自动化验证：启动冷测与常驻内存测在 CI/验收脚本里对比「引入助理前后」基线，差异 ≤ 3%。

### 9.3 核心契约（详见任务 `*-plana-assistant/design.md`）

- 一切任务写入经 `IScheduleService`（`App` 层），内部开启 `ChangeScope(source, label)`；`ChangeSet` 写入 SQLite，支持 `Undo(changeSetId)`。
- 助理工具分只读工具与「提案工具」；提案工具只向 `ProposalStore` 追加条目，**不触达仓储**。
- 外部 AI（MCP）写入同样产生 `ChangeSet`（source=Mcp），危险操作（删除、批量改期）强制进入应用内待确认队列。
- 模型接入统一 `Microsoft.Extensions.AI.IChatClient`，厂商差异只在 `ModelProviderCatalog` 与客户端工厂。

### 9.4 外部 AI 接入（A7 · MCP 进程与命名管道拓扑）

```
外部 AI 客户端（Claude Desktop / Cursor / Antigravity / Codex）
    │  标准 MCP JSON-RPC 2.0 over stdio
    ▼
VGList.Mcp.exe（轻量独立控制台进程，与 VGList.exe 同目录，不进主进程加载路径）
    │  Windows Named Pipe: \\.\pipe\plana-mcp-<CurrentUserSID>（ACL 仅当前用户）
    ▼
VGList.exe 主进程：ExternalAccessBridge（默认关闭，仅在设置开启后监听）
    │  白名单任务/日历工具 + 限流 + 待确认队列 + 隐私脱敏审计
    ▼
ITaskRepository / IScheduleService / ChineseDateParser / CnCalendar
```
