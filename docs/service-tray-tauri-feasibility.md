# 改造可行性分析：服务化托盘 + Rust/Tauri 控制面板

> 分析对象：把当前 WinUI 3 应用拆成「系统服务（保持当前 .NET 原生实现）」+「Rust + Tauri 控制面板」，托盘图标归属服务，点击托盘启动控制面板。
> **已定架构决策**：托盘为**独立进程**（C# 实现，与服务同技术栈），由服务与控制面板**按配置**决定是否拉起（三态策略，见 4.1 与 4.10）；托盘与服务强绑定（退出托盘 = 停止服务）；只有服务需要高权限。
> **面板数据需求基线**：已完成的 UI 设计 `battery-console-winui.html`（另一工作区），其数据契约与采集能力对照见第 5 节。
> 依据：本仓库当前代码（`src/`、`installer/`、`.github/workflows/build-windows.yml`）+ Windows 平台既有约束 + 本机环境探测。
> 本机当前**无网络**（web_search 无密钥、web_fetch 被沙箱拒绝），因此涉及"当前最新版本号"的外部事实未能在线核实，已在第 11 节标注。

---

## 0. 结论摘要

需求拆成 5 个可独立判定的目标：

| # | 目标 | 判定 | 关键依据 |
|---|---|---|---|
| 1 | 服务保持当前 .NET 原生实现（WMI/CIM、协调、命名管道） | ✅ **可行** | 服务当前不依赖任何 UI 进程，`HonorControl.Service` 只引用 `HonorControl.Contracts` |
| 2 | 托盘图标"属于系统服务"——由服务进程自己创建 | ❌ **字面不可行** | 会话 0 隔离：窗口站/桌面是会话内对象，服务进程无法在用户会话注册可见托盘图标 |
| 3 | 托盘作为**独立进程**，由服务/控制面板**按配置**决定是否拉起 | ✅ **可行（已定方案）** | 三态策略 + 单实例互斥量 + 面板负责恢复；服务侧拉起必须规避静默提权（见 4.1） |
| 4 | 控制面板改用 Rust + Tauri | ✅ **可行** | Windows 11 自带常青 WebView2（本机实测 `154.0.4258.48`） |
| 5 | 点击托盘启动控制面板 | ✅ **可行** | 托盘进程 `CreateProcess` + 面板单实例激活（现有互斥量模式可直接照搬） |
| 6 | 面板所需的实时与历史数据由服务采集 | ⚠️ **部分可行** | 电量/阈值/性能模式今天就有；电池温度、适配器功率、风扇转速、PL 需真机验证；CPU/GPU/SSD 温度与自定义风扇曲线暂无已知通道（见第 5 节） |

**一句话结论：目标架构成立，唯一必须在设计上让步的是"注册托盘图标的进程不能是服务进程本身"**——托盘改为一个独立的轻量进程，运行在用户会话，由服务与控制面板按配置决定拉起与停止（见 4.1）。其余 4 项目标可按原样实现。

真正的成本有三块：**用 Web/Tauri 重写整个面板**（六个页面 + 图表 + 电池图形 + 风扇动效，`MainViewModel` 直接返回 WinUI 类型、表现层不可复用）、**服务侧新增采样与历史存储**（面板的监控页需要时间序列，现有契约完全没有），以及**在安装器和 CI 里增加 Tauri 工具链、调整依赖面**。

---

## 1. 现状基线（读代码得到的事实）

```
src/HonorControl/            WinUI 3 面板（asInvoker，托盘在这里）
src/HonorControl.Contracts/  ServiceContract / DesiredConfiguration / ActualState / ServiceSnapshot
src/HonorControl.Service/    Windows 服务（LocalSystem）+ WMI/CIM + 电源方案 + 管道服务端
installer/                   在线安装器 + 依赖预检 + 服务注册/备份/恢复
```

**服务侧事实**

- `Program.cs`：`Host.CreateDefaultBuilder().UseWindowsService()` + `ReconciliationCoordinator` + `PipeServer` 两个 `IHostedService`。
- `PipeServer.cs`：命名管道 `HonorControl.Service.v1`，单行 JSON，服务端 5 秒超时（`PipeServer.cs:86`）、最多 8 个并发实例（`PipeServer.cs:30`）、`PipeSecurity` = System/Administrators 全控 + AuthenticatedUsers 读写（`PipeServer.cs:152`），每个请求先按客户端 SID 授权（`PipeServer.cs:103`）。
- `ConfigurationStore.cs`：配置在 `%ProgramData%\HonorControl\service.json`，目录 ACL 仅 SYSTEM/Administrators（`ConfigurationStore.cs:83`）；**第一个从活动控制台会话连接的 Windows 用户成为配置拥有者**（`ConfigurationStore.cs:19`）。
- `Manage-Service.ps1:27,30`：`New-Service`（默认 LocalSystem 账号）+ `DelayedAutoStart=1`（**开机后约 2 分钟才启动**）。
- 服务不创建任何窗口、不弹任何 UI，因此当前没有踩到会话 0 的坑。

**面板侧事实**

- `TrayIconController.cs`：原生 `Shell_NotifyIcon` + `SetWindowSubclass`，挂在 WinUI 主窗口上；处理 `TaskbarCreated`（Explorer 重启恢复）、`WM_QUERYENDSESSION`、右键菜单、气泡通知。
- `App.xaml.cs:34`：`Local\HonorControl.UI.<SID>` 互斥量 + `AutoReset` 事件实现单实例与"再次点击激活已有窗口"。
- `MainWindow.xaml.cs:436`：关闭窗口 → 依据 `closeToTray` 设置隐藏到托盘而不是退出（`%LocalAppData%\HonorControl\settings.json`）。
- `ServiceClient.cs`：唯一的数据通道，5 秒超时，`GetState` / `SetCharge` / `SetPerformance` / `SetAutoReconcile`。

**两处必须处理的隐式耦合（容易漏）**

1. `HonorControl.Service.csproj:18` 用 glob 编译 UI 项目的 Models 目录：
   `<Compile Include="..\HonorControl\Models\*.cs" />`。
   **一旦删除或替换 WinUI 项目，服务会直接编译失败。** 必须先把 `PerformanceStatus`、`PowerSchemeStatus`、`PowerSchemeInfo`、`SystemPowerSnapshot` 迁到 `HonorControl.Contracts`（或新建共享库）。
2. `MainViewModel` 的公开成员直接返回 WinUI 类型（`Visibility`、`InfoBarSeverity`），表现层逻辑**不可复用**，Tauri 面板的视图模型要重写。

---

## 2. 硬约束：为什么托盘不能真的"挂在服务进程里"

这是本次改造唯一的架构级阻断点，必须先讲清楚：

1. **服务运行在会话 0**，与用户登录会话隔离（Vista 起的架构约束）。会话 0 没有交互式桌面。
2. **窗口站（Window Station）和桌面（Desktop）是会话内对象**。服务进程可以打开 `WinSta0`，但那是**会话 0 的** `WinSta0`；它无法附加到会话 1+ 的桌面。所以"服务自己 `Shell_NotifyIcon`"不存在可用的变通写法。
3. **托盘通知区域属于 Explorer，而 Explorer 只显示同会话进程注册的图标**。会话 0 里调用 `Shell_NotifyIcon` 即使返回成功，用户也看不到任何图标。
4. **"允许服务与桌面交互"（Interactive Services）已在 Windows 10 移除**，`MessageBox`/窗口/气泡通知在服务里同样不可见。
5. 推论：**"托盘属于服务"只能实现为语义归属**——托盘图标由一个**独立的、运行在用户会话的托盘进程**注册；该进程由服务与控制面板按配置决定拉起与停止，服务停止/卸载即无托盘。

> 现状代码的细节也说明了同一件事：`TrayIconController` 里的 `ChangeWindowMessageFilterEx`（`TrayIconController.cs:62,70`）是为了跨完整性级别接收 Explorer 发来的托盘回调与 `TaskbarCreated`——**只有当注册托盘的进程与 Explorer 权限级别不同**时才需要。这正好是 4.1 节方案 A 的副作用：若代理被服务以高完整性拉起，就必须保留这类处理；反之若代理与 Explorer 同为中完整性，这部分复杂度可以删掉。

---

## 3. 推荐目标架构

```
┌────────────────── 会话 0（服务会话，无桌面） ──────────────────┐
│  HonorControl.Service.exe   (LocalSystem, .NET 8)             │
│   ├─ ReconciliationCoordinator ─ OemWmiClient / PowerScheme   │  ← 保持现状
│   ├─ PipeServer  \\.\pipe\HonorControl.Service.v1             │  ← 唯一权威状态
│   └─ [新增] TrayHostSupervisor：策略判定 + 拉起 + 服务自停            │
└───────────────────────────────────────────────────────────────┘
                    ▲ 命名管道（读状态 / 写配置）
                    │
┌───────────────────┴──── 用户会话（每个登录会话一份） ──────────┐
│  HonorControl.Agent.exe   (Rust，中完整性，独立托盘进程)        │
│   ├─ 托盘图标 / 菜单 / 通知（全系统只此一处注册托盘）            │
│   ├─ 启动即读策略：Off 则退出；运行中转 Off 则自行退出           │
│   └─ 左键点击 → CreateProcess → HonorControl.Panel.exe        │
│                                                               │
│  HonorControl.Panel.exe   (Rust + Tauri + WebView2，按需启动)  │
│   ├─ 单实例；已运行时由第二个实例负责激活前置窗口               │
│   └─ 按配置拉起/停止托盘进程（写入策略）                        │
└───────────────────────────────────────────────────────────────┘
```

职责边界（保持现有原则不变）：**服务是策略与状态的唯一权威**；面板是策略的编辑入口兼（可选）拉起方；托盘进程不持久化策略，启动后向服务读取策略并据此决定存活；面板不再持有托盘，因此关闭窗口 = 退出面板。

---

## 4. 逐项设计分析

### 4.1 托盘进程的拉起与停止（已定方案）

**已定**：托盘是独立进程，由**服务**与**控制面板**按配置决定是否拉起。为免"两个拉起方各自为政"，三方约定一个三态策略，策略的存储与判定权威在服务（见 4.10）。下文"代理"与"托盘进程"同义，指 `HonorControl.Agent.exe`。策略定义：

| 策略 | 含义 | 服务行为 | 面板行为 | 托盘进程行为 |
|---|---|---|---|---|
| `Off` | 不要托盘 | 不拉起；发现残留则要求退出，超时兜底终止 | 不拉起 | 启动读到 `Off` 立即退出；运行中转为 `Off` 自行退出 |
| `OnDemand`（默认） | 只在用户在场时出现 | 不主动拉起 | 打开面板（用户到场）即拉起；设置页提供开关 | 正常运行，**退出后不被复活** |
| `Always` | 常驻托盘 | 会话出现即拉起（登录触发器） | 可拉起以立即生效 | 正常运行；异常结束后由下次登录或面板恢复（服务不重生成） |

两条关键一致性规则：

1. **单实例，重复拉起无害**。托盘进程用 `Local\HonorControl.Agent.<SID>` 互斥量 + `AutoReset` 事件（与 `App.xaml.cs:34` 同款）；第二个实例只发一个"刷新/前置"信号即退出。因此"服务和面板同时拉起"不需要跨进程协调，也不会出现两个托盘图标。
2. **完整性级别与启动方无关**。面板拉起 → 继承中完整性，没问题；服务拉起**不能**直接用 `WTSQueryUserToken`（管理员用户会拿到完整令牌 → 静默高完整性）。规则是"托盘进程恒定中完整性"，由 4.1.1 的机制保证。

**生命周期与服务强绑定（已定）**：服务随开机默认启动（`DelayedAutoStart` **保留**）；**从托盘退出 = 停止服务**。用户明确这两个是强依赖，因此**不再保留"界面退出而服务继续后台维护"的旧语义**——注意这与 [README.md](../README.md#L58) 现有描述相反，文档要一并改（并且要在托盘菜单文案上写明"退出（停止后台服务）"，否则用户会以为只是收起图标，实际会丢掉后台阈值维护）。

由此，服务侧**不需要**存活判定与崩溃重生成：重复拉起由托盘进程的单实例互斥量吸收，恢复由面板负责（见下）。这比心跳监督简单，代价是托盘异常结束后不会自动回来，要等下次登录或用户打开面板。

**停止与恢复的权限路径（已定）**——托盘与面板全程中完整性、不申请任何提权：

- **停止**：托盘菜单的"退出"向管道发一条 owner-only 命令，**服务自行停止**（服务请求 SCM 停止自身，随后托盘进程退出）。全程不需要 `SERVICE_STOP` 权限。
- **启动**：安装时用 `sc.exe sdset` 给服务安全描述符授予 `Authenticated Users` 的 `SERVICE_START`（**不授予 STOP**）。面板发现服务已停止时：报告状态 → 调 `StartService` → 服务启动 → 在 `Always` 下由服务拉起托盘、在 `OnDemand` 下由面板拉起托盘，成对恢复。
- 该授权只允许启动、不允许停止或改配置；写配置仍受管道内的 OwnerSid 归属校验保护，因此不构成权限提升。

**会话范围**：托盘进程只在 `OwnerSid` 匹配的会话中运行——多用户机器上非拥有者登录不会出现托盘（非拥有者本来也无法控制设备）。

#### 4.1.1 服务侧拉起：实际实现选了 A′

| 机制 | 实现 | 完整性级别 | 评价 |
|---|---|---|---|
| A. `CreateProcessAsUser`（朴素） | `WTSQueryUserToken` + `DuplicateTokenEx` + `CreateProcessAsUser(lpDesktop="winsta0\\default")` | ⚠️ 用户**完整令牌**，管理员静默高完整性 | 需要 LocalSystem 的 `SeTcbPrivilege`/`SeAssignPrimaryToken`/`SeIncreaseQuota`（LocalSystem 都有），但破坏"普通权限"原则，**不推荐** |
| **A′. 复制 Explorer 令牌（已采用）** | 在拥有者会话的 `explorer.exe` 上 `OpenProcessToken` → `DuplicateTokenEx` → `CreateProcessAsUser` | ✅ 中完整性（与 shell 相同） | `service/TrayLauncher.cs`。不需要安装期注册任何东西，运行期也不 spawn 外部进程 |
| B. 计划任务 | 安装器注册 `TASK_LOGON_INTERACTIVE_TOKEN` 任务，服务 `schtasks /run` 触发 | ✅ 中完整性 | 原先推荐，**最终未采用**：见下 |

**为什么最后没有用计划任务（B）**：它需要安装期注册任务（还要解决"登记给哪个用户"的问题），
运行期服务还得 spawn `schtasks.exe`。而安装流程已经因为杀软敏感而全面去脚本化——
再留一个"服务派生 schtasks"的外部进程调用，收益不大、可疑度不低。
改用 A′ 之后：安装器不注册任务，服务不依赖任何外部工具，
而且复制 shell 令牌天然拿到中完整性（这正是 A 的缺陷所在），两个问题一起解决。

托盘路径解析为 `{app}\tray\HonorControl.Tray.exe`（服务的上一级目录），失败只记日志并按退避重试。

#### 4.1.2 与服务 `DelayedAutoStart` 的时序

服务当前是延迟自动启动（`Manage-Service.ps1:30`，开机后约 2 分钟），**已定保留**。于是：

- 若只靠服务拉起，`Always` 策略下托盘会比登录晚约 2 分钟出现；
- 采用 A′ 时，服务启动后按 15 秒轮询发现托盘缺失就补拉，因此 `Always` 下托盘在服务起来后约 15 秒内出现（服务本身延迟启动约 2 分钟）；
- `OnDemand`（默认）不受这个时序影响：托盘本来就要等用户打开面板才出现。

**无论选哪种**，托盘进程都必须容忍：服务尚未启动（连接失败要退避重试，而不是退出）、服务未安装/已卸载（重试若干次后退出，不留空转进程）、升级安装期间被终止（升级后由策略决定是否恢复）。

### 4.2 托盘进程/面板 ↔ 服务通信

直接复用现有协议，不需要发明新东西：

- 管道名、单行 JSON、PascalCase 属性名、5 秒超时、`GetState` 等命令都可原样使用。**托盘进程是 C#，可以直接复用现有 [`ServiceClient.cs`](../src/HonorControl/Services/ServiceClient.cs)**；只有 Tauri 面板需要自己实现一份管道客户端（`tokio::net::windows::named_pipe` 或 `windows` crate 的 `CreateFileW` + serde，注意 `#[serde(rename_all = "PascalCase")]`——`System.Text.Json` 未配置 camelCase 策略，属性名是 PascalCase）。
- `Snapshot()` 只是读内存 + 读配置文件，**轮询很便宜**，不会触发 WMI；第 5 节的 `Telemetry` 也走"服务端采样 + 客户端读缓存"，同样便宜。因此托盘用 5–15 秒、面板用 1 秒轮询即可，**不需要新增推送通道**。
- 服务端会调用 `GetNamedPipeClientProcessId` + `Process.GetProcessById` 判定会话（[PipeServer.cs:96](../src/HonorControl.Service/PipeServer.cs#L96)），中完整性用户进程可被 SYSTEM 正常查询，无额外要求。
- **需要改动契约的地方**：`PipeServer.Execute` 目前只认 `GetState` + 三个写命令，且每次请求都会走 `AuthorizeOrEnroll`。托盘进程也要读状态，因此要么为托盘定义只读命令集，要么让它沿用面板的归属注册（见 4.3）。

### 4.3 授权与"配置归属"语义变化（**容易被忽略的产品级影响**）

现状：归属注册发生在**用户主动打开应用**时——"第一个从活动控制台会话连接的用户成为拥有者"是合理的。

改造后：**默认策略是 `OnDemand`，托盘不会在登录时自启**，所以归属语义基本不变。只有在两种情况下会变成"自动发生"：

- 用户把策略改成 `Always`（登录即拉起托盘）；
- 用户从托盘打开面板时，托盘进程可能先于面板连接服务。

于是需要显式选择：

- **选项 1（推荐）**：托盘进程只持有只读通道；`SetCharge`/`SetPerformance`/`SetAutoReconcile` 仅允许面板调用，归属注册仍发生在面板首次通信时。托盘菜单只做"打开面板 / 退出"，不做快捷开关。
- **选项 2**：接受"托盘先连接即注册归属"，托盘菜单可以提供快捷开关，但要在设置页明确提示归属规则。
- **选项 3**：把归属从"单一 OwnerSid"改成"任何活动控制台会话的中完整性客户端"，削弱隔离性，不建议。

采用选项 1 时，**托盘策略本身也由面板写入、托盘进程只读**——与"托盘进程只读"自洽。唯一例外是"退出（停止后台服务）"这条命令：它由托盘发起、作用于服务自身，必须限定 owner-only，且只允许停止服务、不允许改任何配置。

### 4.4 托盘状态与通知

- 通知（气泡/Toast）**只能在托盘进程弹**：服务在会话 0 弹不出任何东西。现有"首次驻留提示"（[MainWindow.xaml.cs:460](../src/HonorControl/MainWindow.xaml.cs#L460)）逻辑迁到托盘进程。
- 托盘需要展示的状态（充电阈值、性能模式、电脑管家只读中、校正失败）都来自 `ActualState`，靠 4.2 的轮询即可。
- 生命周期语义确定后，`settings.json` 的两项都要改：
  - `CloseToTray`（关闭窗口驻留托盘）**删除**——面板不再持有托盘，关闭窗口就是退出面板；托盘是否常驻改由三态策略决定（默认 `OnDemand`，见 4.1/4.10），设置页对应项改为"显示托盘图标"。
  - `HasShownTrayHint` 归托盘进程所有。建议面板与托盘**各写各的配置文件**（面板：Tauri store 或 `%LocalAppData%\HonorControl\panel.json`；托盘：`tray.json`），避免两个进程并发写同一个 `settings.json` 造成覆盖。

### 4.5 单实例与"点击托盘打开面板"

- 面板用 `Local\HonorControl.Panel.<SID>` 互斥量 + `AutoReset` 事件即可，逻辑与 `App.xaml.cs:34` 完全一致（Tauri 也可直接用 `tauri-plugin-single-instance`，但自实现更省一个依赖、行为更可控）。
- 代理启动面板：`CreateProcess` 面板 exe（同目录解析路径），并在启动后把焦点交给已存在实例；面板被第二次拉起时发信号让第一个实例前置窗口，然后自己退出。
- 面板需要区分"从托盘启动时显示窗口"，与现有 `ShowFromExternalRequest()` 等价。

**反向的那一半（已实现）**：`OnDemand` 策略要求"用户在场才出现托盘"，而用户在场最直接的信号就是打开面板，
所以面板启动后调用 Tauri 命令 `launch_tray` 拉起托盘（`panel/src-tauri/src/main.rs`，解析 `..\tray\HonorControl.Tray.exe`，
可用 `HONORCONTROL_TRAY_PATH` 覆盖）。托盘有单实例互斥量，因此"服务、面板同时拉起"仍然不需要任何跨进程协调。

### 4.6 面板实现（已落地：React + Fluent UI + Tauri）

原型 HTML **只作为需求基线**（页面划分、每个页面要展示什么数据），代码不沿用——它是单文件手写 CSS/SVG 的演示，不是可维护的工程结构。面板已按正式工程重写在 `panel/`：

| 层 | 选择 | 理由 |
|---|---|---|
| 外壳 / 原生 | Rust + Tauri 2 | 需要命名管道与 SCM 调用；不需要把 .NET 运行时带进面板 |
| 视图 | React 19 | |
| 组件库 | **Fluent UI v9** | 目标观感是 WinUI 3：Fluent 与 WinUI 同源语义色板/控件/主题，比手写 CSS 更不容易走样 |
| 图表 | Recharts 3 | 历史曲线与风扇曲线都是折线/面积图，可按 Fluent token 上色 |
| 服务数据 | TanStack Query 5 | 轮询、缓存、失效与错误状态；服务端快照很便宜 |
| UI 状态 | Zustand 5（persist） | 只放页面/主题/示例数据开关 |
| 构建 | Vite 8 | Tauri 官方模板同款 |

已完成的骨架：六个页面（首页 / 电池设置 / 性能设置 / 充放电历史 / 功耗历史 / 设置）、
电池图形（阈值标记可拖动）、功率流向图、温度条、风扇曲线与转子、状态横幅、数据来源面板；
数据链路 `contract → transport → queries → mock 注入` 三层分离，组件不直接取数、也不判断服务在不在。

**缺数据的指标一律先放示例数据并写明原因**（第 5 节矩阵的落地方式）：

- 假数据只允许来自 `panel/src/data/mock/sources.js`，每条都带 `reason` 与 `reference`（出处指向本文第 5 节）；
- 显示假数据的位置都带"示例"角标（悬停显示原因），设置页另有"数据来源"面板逐项列出服务/示例；
- 服务接上某指标后，删掉 `sources.js` 里的条目即可，组件不改；
- 示例数据是**时间的纯函数**的物理模型（充电功率 → 电池温度 → 核心温度 → 风扇转速，适配器功率 = 负载 + 充入），
  因此实时值与历史曲线自洽，不会出现互相矛盾的假象。

**协议不再区分版本**：面板与服务按**同一份 v2 契约**发布，`Version` 不一致时直接报错让用户重装，
不保留 v1 兼容分支。因此服务端必须与面板同步升级（`ProtocolVersion` 升到 2 并实现第 5.4 节的命令集），
面板这边不需要任何条件分支——这也让"某功能只在某个服务版本可用"这类隐式状态从代码里消失。

界面观感与 WinUI 原生控件不会 1:1 相同，这是**产品取舍**，不是技术风险。

### 4.7 依赖与安装器

改造带来的依赖面变化（**是净收益**）：

- 面板不再需要 .NET Desktop Runtime，也不再需要 Windows App Runtime 2.4 → `Install-Prerequisites.ps1:57-63` 的 `Test-WindowsAppRuntime` 可以删除，`HonorControl.iss:41,126-159` 的 WindowsAppSDK 下载/安装分支随之删除。
- 仍需预检：Windows 11 x64、HONOR 厂商标识、HWMI 只读回读（不变）+ 服务所需的 x64 `Microsoft.NETCore.App` 8.0（不变）+ WebView2 存在性（Win11 自带常青版，通常只需存在性检查，不必打包）。
- 安装布局要从 `ui/ service/ installer/` 扩成 `panel/ tray/ service/ installer/`；**注意 `Manage-Service.ps1:96` 的恢复目录白名单硬编码了 `@('ui','service','installer')`**，必须同步修改，否则升级回滚会漏文件。
- **升级/卸载必须先终止所有会话中的代理与面板进程**（否则文件被占用替换失败）；现有 `-Mode Stop` 只停服务，需要扩展。安装器目前也未使用 Restart Manager 关闭应用——三个进程之后这个缺口会放大。
- 未签名分发的既有风险不变；新增的 Rust 二进制同样需要签名，否则 SmartScreen 与杀软误报概率上升。

### 4.8 CI

- 需要新增 Rust 工具链 + MSVC 构建环境 + cargo 缓存，`windows-2022` runner 自带 Rust 与 MSVC，改动量可控。
- `build-windows.yml:94-120` 的"验证安装包载荷"步骤针对 WinUI 产物写死了文件名（`HonorControl.exe`、`HonorControl.pri`、`Microsoft.WindowsAppRuntime.Bootstrap.dll`、以及"不得包含 `Microsoft.UI.Xaml.dll`"），**必须整体重写**为新布局（面板 exe、代理 exe、服务 exe、图标资源）。
- 托盘图标可继续复用 `src/HonorControl/Assets/HonorControl.ico`（多尺寸），由安装器放到安装目录供代理加载。

### 4.9 建议的迁移顺序（降低一次性风险）

**强烈建议分两阶段交付，而不是一步到位：**

- **阶段一**：不动界面。迁移共享模型 → 服务新增托盘策略与拉起/停止 → 新增 C# 托盘进程 → 服务新增采样与历史 → 安装器/CI 适配。此阶段结束时"托盘归属服务、点击启动控制面板、退出托盘停止服务"已经成立，控制面板暂时仍是现有 WinUI 程序（数据可用性也在这一阶段实测清楚）。
- **阶段二**：用 Tauri 重写面板，功能对齐后在安装器里切换并删除 WinUI 项目。

这样任一阶段都可独立验证与回滚，且阶段一就能在真机上暴露会话隔离/完整性级别/多用户这些真正的风险点。

### 4.10 策略的存储、默认值与冲突规则

- **存储**：扩展服务配置 `%ProgramData%\HonorControl\service.json`（`ConfigurationStore.cs:11-13`），增加 `TrayPolicy` 字段，作用域与现有配置一致（OwnerSid）。该文件已由 SYSTEM/Administrators 独占写、通过管道读写——**不需要新增配置文件，也不需要给托盘进程文件权限**。
- **默认值 `OnDemand`**，理由是自举顺序：安装后尚无 OwnerSid，"谁先登录谁成为拥有者"的注册由面板首次通信触发（`ConfigurationStore.cs:19`）。若默认 `Always`，登录即拉起托盘进程却读不到任何策略（此时还没有拥有者），语义不成立；默认 `OnDemand` 让"安装 → 打开面板（完成归属注册）→ 设置页显式开启常驻托盘"成为唯一清晰路径。
- **唯一权威 = 服务**：面板通过管道写策略（新增 `SetTrayPolicy` 命令），服务持久化并放进每次快照；托盘进程只读快照里的策略。两个拉起方不会各写一份配置，因此不存在并发写文件的竞态（对比现状：`%LocalAppData%\HonorControl\settings.json` 由面板进程独占写入）。
- **"托盘退出"的语义（已定）**：**退出 = 停止服务**，与策略无关——策略只决定"谁来拉起、什么时候拉起"，不改变退出行为。托盘菜单文案写明"退出（停止后台服务）"，并建议加一次二次确认（沿用现有性能模式二次确认的交互习惯），因为退出会停止后台阈值维护。实现上由托盘向管道发一条 owner-only 的 `ShutdownService` 命令，服务自行停止（见 4.1）。退出后要恢复：打开面板（面板报告服务已停止 → `StartService` → 成对恢复）或重启电脑。

### 4.11 拉起/停止失败矩阵

| 场景 | 现象 | 处理 |
|---|---|---|
| 服务延迟启动约 2 分钟 | `Always` 下托盘在服务起来后约 15 秒出现 | 服务轮询补拉（4.1.2）；`OnDemand` 不受影响 |
| 服务未安装/已卸载但托盘进程被拉起 | 连不上、读不到策略 | 退避重试若干次后退出，不留常驻空转进程 |
| 面板拉起时托盘已在运行 | 可能出现两个进程 | 单实例互斥量：后者发信号后退出 |
| 服务与面板同时拉起 | 同上 | 同上，不需要跨进程协调 |
| **托盘菜单"退出"** | 服务停止 + 托盘消失 | 预期行为：管道 `ShutdownService`（owner-only）→ 服务自停 → 托盘退出；文案与二次确认要说清 |
| **服务被停止或崩溃（非托盘触发）** | 面板报"服务已停止"，托盘若在也失效 | 面板负责恢复：报告 → `StartService`（已授予 Users `SERVICE_START`）→ 服务启动 → 按策略成对拉起托盘 |
| 托盘进程崩溃/被任务管理器结束 | 图标消失 | 服务**不重生成**；等下次登录（`Always`）或用户打开面板时恢复 |
| 策略改为 `Off` | 图标应立即消失 | 托盘进程自行退出；超时未退由服务终止 |
| Explorer 重启 | 图标消失 | 保留 `TaskbarCreated` + `NIM_SETVERSION 4` + GUID 处理（现成实现可移植） |
| 注销/切换用户/RDP 多会话 | 会话内进程结束 | 每个交互会话一份托盘进程；仅 OwnerSid 会话出现 |
| 非 OwnerSid 用户登录 | 不应出现托盘 | 拉起判定按 OwnerSid 过滤会话 |
| 升级安装替换文件 | 文件被占用 | 安装器先终止托盘与面板再替换（扩展 `-Mode Stop`） |

---

## 5. 面板数据需求 vs 服务侧采集能力

面板 UI 已经设计完备（另一工作区的 `battery-console-winui.html`，六页：首页 / 电池设置 / 性能设置 / 充放电历史 / 功耗历史 / 设置），本节以它的数据契约为需求基线。

**结论：面板需要的数据里只有一部分今天就能从服务拿到**；其余要么需要新增采集，要么在荣耀 ACPI-WMI 通道上尚未证实可得。UI 已经按"读不到就不展示"设计，所以缺口不会毁掉界面，但**必须先摸清每个指标的真实可得性，再决定哪些卡片与控件进入第一版**。

### 5.1 字段级可得性矩阵

| UI 字段 | 服务侧来源 | 现状 | 判定 |
|---|---|---|---|
| `soc` 电量 % | `GetSystemPowerStatus.BatteryLifePercent`（[SystemPowerService.cs:16](../src/HonorControl.Service/Hardware/SystemPowerService.cs#L16)） | 已实现 | ✅ 直接用 |
| `plug` 是否接适配器 | `ACLineStatus`（同上） | 已实现 | ✅ |
| `start`/`stop` 充电阈值（读+写） | `0x1103` / `0x1003` + 回读比对（[OemWmiClient.cs:15](../src/HonorControl.Service/Hardware/OemWmiClient.cs#L15)） | 已实现 | ✅ 但范围口径要统一（见 5.5） |
| `mode` 性能模式（读+写） | `0x0E04` / `0x0C07` + 回读比对（[OemWmiClient.cs:33](../src/HonorControl.Service/Hardware/OemWmiClient.cs#L33)） | 已实现 | ✅ |
| `batt` 电池功率（带符号） | `root\wmi:BatteryStatus` 的 ChargeRate/DischargeRate（mW），或按 RemainingCapacity 差分估算 | 未实现、未验证 | ⚠️ 需真机确认；差分法可兜底（服务本就在周期采样） |
| `ac` 适配器功率 | `0x0902` 适配器电压(mV) × `0x10902`/`0x110902` 电流（研究已定位，[conclusions.md:356](../research/conclusions.md#L356)） | 只实现了电压读取（[OemWmiClient.cs:41](../src/HonorControl.Service/Hardware/OemWmiClient.cs#L41)） | ⚠️ 需真机验证电流命令与多口适配器组合 |
| `temp` 电池温度 | 无已知来源；候选 `root\wmi` 电池类温度属性 / Honor WMI 未识别命令 | 未识别 | ⚠️ 需在真机枚举 `root\wmi` 电池类 |
| `cpu`/`gpu`/`ssd` 温度 | 无已知来源；候选 ACPI 热区 `MSAcpi_ThermalZoneTemperature`、第三方内核驱动 | 未识别 | ❌ 高风险：可能没有用户态通道 |
| `fans[].rpm` 风扇转速 | 候选 `0x0802` out[1..2]（研究：1776–2838，量级与 RPM 吻合但与封装功率混淆，[conclusions.md:388](../research/conclusions.md#L388)） | 已发送该命令，语义未定 | ⚠️ 语义验证通过前不能当转速用 |
| `fanMode`/`fanPoints` 自定义风扇曲线（写） | 研究：经 Honor 内核驱动 `\.\WDT0001` + NLD 风扇库（[conclusions.md:212](../research/conclusions.md#L212)） | 无用户态通道 | ❌ 首版建议只读或从 UI 移除 |
| `pl1`/`pl2`/`tdp` 功耗限制（读+写） | GVNT/WVST（PL 值经 OemWMIfun，[conclusions.md:211](../research/conclusions.md#L211)）；写路径未定位（[conclusions.md:404](../research/conclusions.md#L404)：PowerPolicyPlugin 无任何 WMI SET） | 未实现 | ⚠️ 读可能可行、写未验证 |
| 健康度 / 设计容量 / 循环次数 | `root\wmi` 电池类（BatteryStaticData / BatteryFullChargedCapacity / BatteryCycleCount） | 未实现 | ⚠️ 常见可得，需真机确认 |
| 适配器额定功率、机型、OS 版本 | `Win32_ComputerSystem`、注册表；额定功率无 API | 部分可读 | ⚠️ 额定功率需机型表 |
| 各模式静态指标（CPU 功耗 / 整机功耗 / 噪声 dB） | 需机型档案表 | 无 | ⚠️ 要产品定义机型配置 |
| 历史序列（batt/ac/load × 1h/24h/7d） | 服务侧采样 + 落盘 + 降采样 | 完全没有 | ✅ 可行，需新增（见 5.3） |
| 告警文案（低电量、温度过高、未接电源） | 现为前端硬编码 3 条规则 | — | 决策点（见第 10 节） |

现有契约 `ServiceContract.cs` 只覆盖前 4 项加错误状态，**其余全部要新增**。

### 5.1.1 实测结果（2026-10，HONOR BCC-N / Windows 11 build 26200）

上表里的 ⚠️ 已经在真机上跑过一遍（只读），结论如下——**其中两项从此不再是示例数据**：

| 指标 | 实测结论 | 数据源 |
|---|---|---|
| 电量、插电状态 | ✅ 可用 | `GetSystemPowerStatus` + `CallNtPowerInformation` |
| **电池功率（带符号）** | ✅ **可用**：`SYSTEM_BATTERY_STATE.Rate` 是**有符号毫瓦**，实测充电中 `+80.6 W`；与 `root\wmi:BatteryStatus.ChargeRate` 完全同值 | `powrprof!CallNtPowerInformation(SystemBatteryState)` |
| **循环次数** | ✅ **可用**：实测 3 | `root\wmi:BatteryCycleCount` |
| 满充容量 | ✅ 可用：实测 92.04 Wh | `SYSTEM_BATTERY_STATE.MaxCapacity`（注意：**它就是当前满充容量**） |
| 电池温度 | ❌ 拿不到：`root\wmi:BatteryTemperature` 类存在但**没有实例** | — |
| 电池健康度 / 设计容量 | ❌ 拿不到：`BatteryStaticData` 无实例、`Win32_Battery.DesignCapacity` 为空；把 `MaxCapacity` 当设计容量会恒得 100% | — |
| 适配器功率 | ❌ 拿不到：`0x0902` 只给电压（实测 20000 mV），电流命令未确认 | — |
| CPU/GPU/SSD 温度 | ❌ 拿不到：`MSAcpi_ThermalZoneTemperature` 不可用、`Win32_TemperatureProbe` 与 `Win32_Fan` 均 0 实例、SMART（`MSStorageDriver_ATAPISmartData`）无实例 | — |
| 风扇转速 | ⚠️ 仍未知：Win32 侧无来源；荣耀通道 `0x0802` 的语义需在服务（SYSTEM）里验证 | — |

另一个实测发现：**`root\wmi:OemWMIMethod` 对非管理员拒绝访问**（HRESULT 0x80041003）。这意味着能力探测只能由服务自己做（它本来就以 SYSTEM 运行），面板与任何用户态工具都读不到荣耀通道。

据此，服务端采样器已提供 `BatteryService`（电量/功率/满充容量）+ 1 Hz `TelemetrySampler` + 60 秒粒度 `HistoryStore`，
并把没有读数的字段写进 `Capabilities.MissingReason` 如实上报——服务不猜、不填估算值。
两个只读诊断工具（`tools/BatteryProbe`、`tools/HistoryProbe`）直接编译服务端源码来验证真实读数与历史降采样。

### 5.1.2 第三方硬件监测库接入（协议 v3）

`HonorControl.Service` 集成 `LibreHardwareMonitorLib 0.9.6`，启用电池、CPU、GPU、主板、存储和控制器读取。采样只在以 LocalSystem 运行的服务进程中进行；控制面板通过原命名管道读取缓存，不直接访问硬件或申请管理员权限。

该库的电池实现通过 Windows 电池设备接口读取设计容量与满充容量；CPU/GPU/存储温度和风扇 RPM 按硬件/驱动实际暴露的传感器采样。驱动没有提供的字段仍为 `null`。目前代码已接入，目标 HONOR 设备上的真实返回值仍需在更新后的服务运行后确认。

### 5.2 "读不到就不展示"的三层契约

UI 已经把降级做在三个层级上，服务侧必须配合：

- **卡片级**：传感器与风扇全不可用 → 整张"性能与散热"卡隐藏；
- **行级**：`avail.<id> = false` → 该行从 DOM 移除（传感器与风扇各自一行）；
- **数值级：UI 没有 null 保护**（`v.toFixed()` 会直接抛异常）。因此契约必须是「**要么给有效数值，要么把它从可用集合里移除，绝不发 null**」。

推论：服务与适配层要成对维护「值 + 可用集合」，Rust 侧 serde 模型要在适配层把空值转成"移除"而不是 `null`；id 必须是稳定字符串（`cpu`/`gpu`/`ssd`/`fan1..fanN`），因为 UI 用它当 DOM 重建的缓存键——热插拔安全，但改名会触发重建。

### 5.3 服务侧必须新增的能力

1. **采样器**：1 Hz 原子快照（soc/plug/batt/ac/temp），WMI 重指标用 5–10 s 缓存；客户端轮询只读内存，**不因轮询打 WMI**。
2. **能力探测**：启动时枚举一次传感器/风扇/电池能力，产出 `Capabilities`（sensors[]、fans[]、adapterRatedW、designCapacityWh、modeProfiles），随后随采样刷新可用性。
3. **历史存储**：60 秒聚合按天 append-only 写 `%ProgramData%\HonorControl\history\`，保留 30 天；查询时**服务端降采样**到 UI 需要的点数（1h→180、24h→240、7d→300，等间隔），并返回 `hours` 供前端做 Wh 积分。服务停止期间没有采样 → 图表会出现空档，面板要能显示断档。
4. **口径统一**：`charging/discharging/idle` 以 ±0.6 W 判定；`load = ac − max(batt,0)`（放电时 = `|batt|`）；Wh 积分 = `Σv·dt`。**这套口径必须由服务定义、面板复用**——当前 UI 自己就有矛盾：首页 `load()` 是派生值，历史页 `load[]` 是独立序列，两者会显示不同数字。
5. **写入对账**：所有写命令返回**实际生效值**（现有充电阈值与性能模式已有回读比对，扩展到 PL 与开关），前端据此纠正乐观状态，避免滑块漂移。

### 5.4 契约扩展（v2）

- 新增 `Telemetry`（1 Hz 快照）、`Capabilities`、`HistoryQuery`/`HistorySeries` 与写命令集合；`ProtocolVersion` 升到 2（两侧同时发布，无需兼容旧版）。
- 保留既有约定：单行 JSON、PascalCase 属性名、响应单行。
- 请求行仍受 4096 字节上限（[PipeServer.cs:142](../src/HonorControl.Service/PipeServer.cs#L142)），且服务端每请求 5 秒超时，因此历史**必须在服务端降采样后返回**（几百点、几十 KB 量级），不要一次返回 7 天原始点。
- 采样与管道解耦：客户端轮询只读缓存。

### 5.5 第一版取舍建议

- **先做**（今天就能拿到）：电量、AC 状态、充电阈值读写、性能模式读写；batt/ac 历史（待 5.1 中电池功率与适配器功率验证通过）。
- **验证后再定**：电池温度、适配器功率、风扇转速、PL 读写。
- **首版建议不做或只读**：CPU/GPU/SSD 温度（除非找到用户态来源）、自定义风扇曲线写入、care/cut 两个开关（无对应命令）。
- 充电阈值范围口径要统一：UI 把 start 限制在 40–99、stop 在 41–100，而服务当前校验的是 `0 ≤ start < end ≤ 100`（[ConfigurationStore.cs:71](../src/HonorControl.Service/ConfigurationStore.cs#L71)）。二选一：服务收紧到 UI 的范围，或 UI 放开到服务的范围——**这是产品决策，不是实现细节**。
- 面板已经能优雅降级，所以"能拿到的先上、其余隐藏"是可行路径；关键是**服务如实报告能力，不发假值**。

---

## 6. 已定实现选择与备选对比

**已定：采用方案 B**——托盘进程用 C#（与服务同一技术栈），面板用 Rust + Tauri。理由是迁移面最小：`TrayIconController.cs` 里已验证的 `Shell_NotifyIcon` 封装、右键菜单、气泡通知、`TaskbarCreated` 恢复几乎可以整体搬过去。

| | 方案 A<br>托盘(Rust) + 面板(Tauri) | **方案 B（已定）**<br>托盘(C#) + 面板(Tauri) | 方案 C<br>面板常驻自带托盘（Tauri 内建 tray） |
|---|---|---|---|
| 托盘随服务存在 | ✅ | ✅ | ❌ 面板退出即无托盘，不满足需求 |
| 代码复用 | 托盘逻辑需移植到 Rust | **`TrayIconController.cs` 几乎可直接复用** | — |
| 工具链数量 | 3（.NET / Rust / Tauri） | 2（.NET / Rust+Tauri） | 2 |
| 进程数/内存 | 服务 + 托盘 + 面板 | 服务 + 托盘(.NET) + 面板 | 服务 + 面板常驻（WebView2 常驻，内存最高） |
| 主要风险 | Rust 托盘细节需重写 | 托盘仍是 .NET | 不满足核心需求 |

**托盘进程实现上的两个具体约束**（照搬现有代码时要注意）：

1. **不要引入 WinForms**。用 `NotifyIcon` 最省事，但会把依赖从 `Microsoft.NETCore.App` 扩到 `Microsoft.WindowsDesktop.App`，安装器的运行时预检也得跟着改。建议直接创建**隐藏的顶层窗口**（`RegisterClassExW` + `CreateWindowExW`）+ 自建消息循环，复用现有 `Shell_NotifyIcon` 那段 P/Invoke。
2. **必须是顶层窗口，不能用 `HWND_MESSAGE` 消息窗口**。`TaskbarCreated` 是广播消息，消息专用窗口收不到——用了 `HWND_MESSAGE` 就会丢掉 Explorer 重启后的图标恢复能力（现有代码正是靠这条消息重建图标的）。

---

## 7. 改动清单（按文件）

**新增（.NET）— 已完成**
- `src/HonorControl.Tray/`：托盘进程。隐藏的**顶层**窗口（必须顶层，否则收不到 `TaskbarCreated` 广播）+ 消息循环 +
  `Shell_NotifyIcon`（固定 GUID + `NIM_SETVERSION` 4）、右键菜单、气泡通知、单实例互斥量、按策略自退、
  退出时经管道请求服务自停、`%LocalAppData%\HonorControl\tray.log` 诊断日志（可用 `HONORCONTROL_TRAY_LOG` 覆盖路径）。
- `src/HonorControl.Service/TrayPolicyHost.cs`：按策略决定托盘存活（Off/OnDemand/Always），Always 时调用下面的拉起器。
- `src/HonorControl.Service/TrayLauncher.cs`：复制拥有者会话里 explorer 的令牌后 `CreateProcessAsUser`（机制 A′）。
  不需要安装期注册任务，运行期也不 spawn 任何外部工具。
- `src/HonorControl.Service/WindowsServiceSessionNotifications.cs`：会话变更接入（见第 10 节待确认项）。
- `src/HonorControl.Service/Telemetry/TelemetrySampler.cs`、`Telemetry/HistoryStore.cs`、`Hardware/BatteryService.cs`：已实现（见 5.1.1）。
- `src/HonorControl.Contracts/Models/*`：从 `src/HonorControl/Models/` 迁入的共享模型。

**修改（.NET）**
- `src/HonorControl.Contracts/ServiceContract.cs`：协议升到 v2；新增 `Telemetry`、`Capabilities`、策略字段、历史查询/响应与写命令。
- `src/HonorControl.Service/HonorControl.Service.csproj:18`：删除对 UI Models 目录的 glob 依赖。
- `src/HonorControl.Service/PipeServer.cs`：新增 `SetTrayPolicy`、`ShutdownService`（owner-only）、`EnsureTray`、`GetTelemetry`、`GetCapabilities`、`GetHistory`；按调用方角色区分授权。
- `src/HonorControl.Service/ConfigurationStore.cs`：新增 `TrayPolicy` 字段与自举默认值（4.10）；归属注册时机若采纳 4.3 选项 1 则基本不变。
- `src/HonorControl.Service/Hardware/OemWmiClient.cs`：新增适配器电流（`0x10902`/`0x110902`）、候选风扇转速与 PL 读写。
- `installer/HonorControl.iss`：**安装流程已全面去脚本化**——设备预检是注册表读取，服务注册/配置/授权用系统自带的
  `sc.exe`（`create`/`config`/`description`/`sdset`/`start`/`stop`/`delete`），备份恢复用 Inno 的文件 API，
  下载的 .NET 运行时先过 `WinVerifyTrust` 验签才会执行。安装流程里不再有 PowerShell、WMI 查询与 schtasks。
- `installer/HonorControl.iss`：新增 `panel/`、`tray/` 载荷与新安装布局、依赖检查简化、卸载清理。
- `installer/Install-Prerequisites.ps1`、`installer/Manage-Service.ps1`：**已删除**（被上面的 Pascal 实现取代）。
- `.github/workflows/build-windows.yml`：新增 Rust/Tauri 构建、重写载荷校验。

**新增（Rust + Tauri，仅面板）— 骨架已完成**
- `panel/src-tauri/`：Tauri 外壳。`src/pipe.rs` 用标准库开命名管道（PascalCase 请求体、6 秒超时、按 Win32 错误码给中文提示）、
  `src/scm.rs` 用 advapi32 FFI 启动服务、`src/main.rs` 暴露 `service_request`（严格校验协议 v2）、`start_service`、`pipe_name`。
- `panel/src/`：React 前端（六个页面 + 电池/功率/风扇/曲线组件 + 数据层 + 示例数据层），见 4.6。
- `panel/scripts/`：Fluent token/图标名校验脚本；受限环境下跳过 Vite 网络驱动器探测的构建校验脚本。

**删除（阶段二结束）**
- `src/HonorControl/`（WinUI 面板）、`HonorControl.csproj` 中的 `UseWinUI` 相关配置、`HonorControl.sln` 中的对应项目。

---

## 8. 风险登记

| 风险 | 影响 | 缓解 |
|---|---|---|
| 服务进程直接创建托盘（唯一硬阻断） | 方案不成立 | 必须走独立托盘进程；不要尝试"变通写法" |
| 服务侧拉起用了朴素 `WTSQueryUserToken` | 静默提权，破坏"普通权限界面"原则 | 已采用 A′：复制 Explorer 令牌后建进程，天然中完整性 |
| 两个拉起方与策略不一致 | 托盘该出现时不出现、该消失时残留 | 单一权威（服务持久化策略）+ 托盘自行退出 + 单实例互斥量吸收重复拉起（4.10/4.11） |
| 配置归属由"登录"触发 | 多用户机器上第二个用户被拒 | 采纳 4.3 选项 1（托盘只读） |
| Explorer 重启 / 任务栏重建 | 托盘图标消失 | 托盘重写时保留 `TaskbarCreated` + `NIM_SETVERSION 4` + GUID 处理（现成实现可复用）；窗口必须是顶层窗口 |
| 服务 `DelayedAutoStart` 与托盘的时序 | `Always` 下托盘在服务启动后约 15 秒内出现（服务延迟启动约 2 分钟） | 服务按 15 秒轮询补拉；`OnDemand` 不受影响 |
| 面板需要的指标服务采集不到 | 温度/风扇等卡片只能隐藏，UI 设计意图打折 | 按 5.1 先做真机核实；服务如实报告能力，**不发假值** |
| PL 与风扇曲线的写路径未验证 | 设置页控件写入失败或无效 | 首版只读或隐藏，实测后再开放（5.5） |
| 历史数据只在服务运行时才有 | 监控图表出现断档 | 面板显式显示断档；在 UI/文档里写明口径 |
| 阈值范围口径不一致（UI 40–99 / 服务 0–100） | 写入被服务拒绝 | 统一口径（见第 10 节决策项） |
| 退出托盘会停止服务 | 用户以为只是收起图标，实际丢掉后台维护 | 菜单文案写明"停止后台服务" + 二次确认 |
| 升级时进程占用文件 | 升级失败 | 安装器先终止托盘与面板；扩展 `-Mode Stop` |
| Tauri 观感与 WinUI 不一致 | 用户体验落差 | 以已完成的 HTML 设计为基线；必要时引入 Fluent Web 组件 |
| WebView2 依赖 | Win10 需引导安装 | 项目仅支持 Win11，自带常青版；仅做存在性检查 |
| 新增未签名二进制 | SmartScreen/杀软拦截 | 与现有风险一致，分发前统一签名 |

---

## 9. 工作量估算（单人，含自测，不含真机硬件验证等待）

| 里程碑 | 内容 | 人日 |
|---|---|---|
| M0 | 共享模型迁移、契约 v2、服务构建解耦 | 1–2 |
| M1 | 服务：托盘策略 + 会话内拉起 + 停止 + `SERVICE_START` 授权 | 3–5 |
| M2 | C# 托盘进程（移植托盘/菜单/通知 + 策略自退 + 退出停服务 + 单实例） | 2–3 |
| M3 | 服务：采样器 + 能力探测 + 历史存储与降采样查询 | 6–10 |
| M4 | Tauri 面板（六页 + 图表 + 电池/风扇图形 + 适配层 + 单实例激活 + 主题/Mica） | 12–18 |
| M5 | 安装器 + CI + 依赖检查改造 | 3–5 |
| M6 | 真机验证（含 5.1 的传感器/功率来源实测）、阶段切换与回滚 | 4–6 |
| **合计** | | **31–49 人日**（约 7–10 周日历时间） |

其中 M3 与 M6 的估算区间很宽，取决于 5.1 里那几个 ⚠️ 指标到底能不能采到——**建议先花 1–2 天做一次"数据源实测"**（在这台荣耀机器上枚举 `root\wmi` 电池类、试 `0x10902`/`0x110902`、验证 `0x0802` 语义），再据此裁剪 M3/M4 的范围。这一步的投入产出比最高。

---

## 10. 需要确认/决策的问题

**已定**

1. 托盘是独立进程，由服务与控制面板按配置决定是否拉起；默认策略 `OnDemand`（见 4.1、4.10）。
2. 服务随开机默认启动、`DelayedAutoStart` 保留；**退出托盘 = 停止服务**（强依赖，见 4.1）。
3. 托盘与面板都是中完整性、不申请提权；只有服务高权限。
4. 服务侧拉起用 A′（复制 Explorer 令牌 + `CreateProcessAsUser`）；停止由托盘经管道请求服务自停；启动通过给服务 SD 授予 Users `SERVICE_START`。
5. 服务**不做**托盘存活判定与崩溃重生成；服务被停止时由面板负责报告并恢复。
6. 托盘进程用 C#（与服务同栈），面板用 Rust + Tauri。
7. **面板视图用 React + Fluent UI v9 重写**：原型 HTML 只作为需求基线，不沿用其代码（见 4.6）。
8. **缺数据的指标先放示例数据并注明原因**，收敛在 `panel/src/data/mock/sources.js`，界面带"示例"角标。
9. **面板与服务按同一份 v2 契约发布**，不保留旧协议兼容分支：服务端必须同步把 `ProtocolVersion` 升到 2 并实现新命令集，否则面板会明确报"版本不一致，请重新安装"。
10. 迁移分两阶段（见 4.9）。

**仍需决策**

1. **数据源实测结论**：先做一次真机实测，再定"电池温度 / 适配器功率 / 风扇转速 / PL 读写"哪些进入第一版（见 5.1、5.5）。
2. **充电阈值范围口径**：UI 的 40–99 / 41–100 与服务当前的 `0 < start < end ≤ 100` 哪个为准？
3. **告警由谁下发**：沿用前端硬编码的 3 条规则，还是由服务下发文案与阈值？
4. **CPU/GPU/SSD 温度**：若 ACPI 热区也拿不到，接受"永久隐藏该卡片"，还是愿意引入第三方内核驱动？
5. **自定义风扇曲线**：写路径不可用时，UI 隐藏该卡片还是保留为只读展示？
6. **历史保留策略**：60 秒采样 / 30 天保留是否合适？是否需要导出？
7. **机型档案表**：模式静态指标（CPU 功耗 / 整机功耗 / 噪声）、适配器额定功率、设计容量是否按机型维护一张表？
8. **多用户/共享电脑**：非 OwnerSid 会话完全不出现托盘（推荐）；是否需要"多用户各自一份配置"？
9. **迁移节奏**：是否接受 4.9 的两阶段交付？
10. **观感基线**：以已完成的 HTML 设计为准，还是要与当前 WinUI 视觉一致（Mica、Fluent 控件、图标字体）？

---

## 11. 验证边界与本机环境事实

**本机实测（可复现）**

- WebView2 Runtime 已安装：`154.0.4258.48`（Tauri 面板的运行时前提已满足）。
- `C:\Program Files\dotnet` 只有 `shared/`（8.0.14 / 8.0.31 / 9.0.3 / 10.0.8 / 10.0.9 运行时），**没有 SDK**；仓库通过 `global.json` 固定 SDK 8.0.408。
- **无 Rust 工具链**（无 `cargo`/`rustc`，无 `%USERPROFILE%\.cargo`）、**无 Visual Studio/MSVC 工具链**、**无 Docker**。
- 结论：**当前机器无法本地构建 .NET 或 Rust 产物**，构建依赖 GitHub Actions（或恢复容器/工具链）。这与 `docs/windows-container-build.md` 的历史记录一致（该文档已注明自 2026-09-29 起不再使用容器构建）。

**无法在 CI 或本机验证、必须在真机验证的点（沿用仓库既有立场）**

- 会话 0 隔离下的托盘可见性（正向验证：独立托盘进程能出图标；反向验证：服务进程不能）。
- **生命周期闭环**：托盘"退出"→ 服务确实停止；服务被停止 → 面板报告并能 `StartService` 恢复；`OnDemand` 与 `Always` 各自的拉起时机。
- 托盘进程在真实登录/注销/切换用户/RDP 场景下的拉起与回收。
- 托盘进程的**完整性级别**是否符合预期（A′ 复制 Explorer 令牌应得中完整性；朴素 `WTSQueryUserToken` 会得高完整性）。
- Explorer 重启后的图标恢复（顺带验证用的是顶层窗口而不是 `HWND_MESSAGE`）、通知弹出。
- 面板启动后与服务的连通性、单实例激活与前置窗口。
- **5.1 中所有 ⚠️ 指标的真机实测**：`root\wmi` 电池类是否提供温度/功率/健康度/循环次数；`0x10902`/`0x110902` 是否返回可信电流；`0x0802` 的两个字节到底是转速还是封装功率；ACPI 热区能否给出 CPU/GPU 温度。
- 1 Hz 采样器对功耗与 CPU 占用的影响，以及历史落盘的体积与查询延迟。
- WMI/电脑管家只读互斥、性能模式写入等硬件路径（与本次改造无关，但每次安装都需回归）。
- 升级/卸载时三个进程的终止与文件替换、卸载后的自启项清理。
- **托盘进程**：已验证"能启动、创建隐藏顶层窗口、连不上服务时如实记录并保持重试、单实例、异常落日志"
  （实测：宿主窗口句柄创建成功；`RegisterClassExW` 曾因 `WNDCLASSEXW` 少一个字段报 87，已修）。
  **未验证**：托盘图标在通知区域的实际外观与交互、`Always` 策略下服务复制 explorer 令牌拉起托盘能否稳定出现在用户会话（需要 SYSTEM 身份 + 真实交互式会话）、
  菜单"退出"在真实服务上的完整闭环（本机装的是旧版 v1 服务，协议版本不匹配）。
- **安装器**：脚本已按新布局改写（`SERVICE_START` 授权、登录任务、三个载荷、进程终止、去掉 Windows App Runtime 预检），
  但**没有跑过一次真实的安装/升级/卸载**——那需要一台可以随意装卸载的机器。
- **面板 Rust 外壳已编译通过**（CI `build` job 的 "Publish control panel (React + Tauri)" 步骤全绿，
  并且安装器已成功产出并通过载荷校验）。该步骤已从"允许失败"改为硬门禁。
  **仍未验证**：Mica 材质在真机上的实际效果、面板窗口的交互回归。
- **面板前端**已通过 Vite 构建校验与两条代码约定守卫（Fluent token/图标名、示例数据边界）。
- **面板 ↔ 托盘**：`launch_tray` 命令已实现（解析 `..\tray\HonorControl.Tray.exe`，支持 `HONORCONTROL_TRAY_PATH` 覆盖），
  但"面板拉起托盘 → 托盘注册图标 → 点击回到面板"这条闭环还没有在真机上跑过。
- 与服务联调、管道错误路径、SCM 启动服务（依赖安装器的 `SERVICE_START` 授权）都还没跑过。

**未能在线核实的外部事实（本会话无网络）**

- Tauri 当前稳定版本号与支持策略（本文按 Tauri 2.x 基线描述；请在联网环境确认是否已有更新主版本）。
- `tray-icon` / `windows` crate 的当前版本与新特性。
- `Microsoft.Extensions.Hosting.WindowsServices` 是否已暴露会话变更钩子。当前已知的可行做法有三条，实施时任选并在真机验证：
  1. 自定义 `ServiceBase`/`IHostLifetime` 以接管 `OnSessionChange`；
  2. 服务内创建隐藏窗口 + `WTSRegisterSessionNotification` 接收 `WM_WTSSESSION_CHANGE`；
  3. 不做会话通知，由服务定时枚举 `WTSEnumerateSessions` 判断该在哪些会话拉起托盘（最简，且服务本来就不做存活跟踪）。
