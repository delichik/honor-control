# Honor Control 控制面板（panel）

服务（`src/HonorControl.Service`，.NET 8，LocalSystem）的控制面板。**面板本身不做任何权限提升**，
所有硬件操作都由服务经 ACPI-WMI 完成，面板只是它的一个客户端。

界面用 **React + Fluent UI v9** 重写，不再沿用之前的 WinUI 3 与原型 HTML 的实现；
原生外壳与命名管道客户端用 **Rust + Tauri 2**。

## 技术栈

| 层 | 选择 | 为什么 |
|---|---|---|
| 外壳 / 原生 | Rust + Tauri 2 | 需要命名管道与 SCM 调用；产物小，不需要把 .NET 运行时带进面板 |
| 视图 | React 19 | |
| 组件库 | **Fluent UI v9**（`@fluentui/react-components`） | 目标观感是 WinUI 3 / Windows 11：Fluent 提供同一套语义色板、控件与主题，比手写 CSS 更接近原生也不容易走样 |
| 图标 | `@fluentui/react-icons`（Fluent System Icons） | 与 WinUI 的 Segoe Fluent 图标同源 |
| 图表 | Recharts 3 | 历史曲线与风扇曲线都是折线/面积图，React 友好、可按 Fluent token 上色 |
| 服务数据 | TanStack Query 5 | 需要轮询、缓存、失效与错误状态；服务端快照很便宜（只读内存缓存） |
| UI 状态 | Zustand 5（带 persist） | 只放页面、主题、示例数据开关这类纯 UI 状态 |
| 构建 | Vite 8 | Tauri 官方模板同款 |

## 目录

```text
panel/
  src/
    App.jsx                 外层：左侧导航 + 内容区
    main.jsx                Provider（Fluent 主题 + Query Client）
    app/                    纯 UI 状态：store / theme / useNow / useRecentSamples
    data/
      contract.js           与服务的通信契约（字段、命令、协议版本、口径常量）
      transport.js          唯一出口：invoke → Rust → 命名管道
      queries.js            所有轮询与写命令（含降级判断）
      derive.js             派生口径：充放电判定、系统负载、Wh 积分、格式化
      fanCurve.js           示例风扇策略曲线（服务暂无通道）
      modeProfiles.js       机型档案示例（功耗墙 / 噪声 / 额定功率）
      mock/                 ← 唯一允许放假数据的地方
        sources.js          缺哪些指标、为什么缺、依据在哪
        generator.js        时间纯函数的物理模型（自洽的示例数据）
        transport.js        浏览器/无服务时模拟服务端响应
        index.js            示例数据注入点（applyMockPolicy）
    components/             卡片、电池图形、功率流向、风扇、曲线、状态横幅…
    pages/                  六个页面：首页 / 电池设置 / 性能设置 / 充放电历史 / 功耗历史 / 设置
  src-tauri/                Rust 外壳：service_request（管道）、start_service（SCM）、pipe_name
  scripts/                  代码约定校验：Fluent token/图标名、示例数据与原生调用的边界
```

## 运行

```bash
npm install
npm run dev          # 浏览器里跑：没有 Tauri 运行时，自动切到示例数据（假服务）
npm run tauri dev    # 真正的桌面窗口（需要 Rust 工具链 + MSVC + WebView2）
npm run build        # 只构建前端产物到 dist/
npm run smoke        # 冒烟：无头浏览器加载 dist，断言界面真的挂载且控制台无未捕获异常
npm run tauri build  # 打包 NSIS 安装包
npm run verify       # 两条代码约定校验，CI 里必须通过（见下）
```

`npm run verify` 包含两个守卫脚本，都是"靠人记不住、交给 CI"的约定：

- `verify:names` —— 代码里用到的 Fluent token 与图标名必须真实存在（JS 写错 token 名不会报错，只会静默失效）；
- `verify:arch` —— 示例数据只允许从 `data/queries.js` 注入（白名单之外引用 `data/mock/*` 直接失败）；
  除 `data/` 外不许直接 import `@tauri-apps/*`；`data/transport.js` 必须**动态**加载假服务，
  否则 mock 代码会被打进桌面产物。

`npm run smoke` 是**运行期**的守卫：`vite build` 只能证明语法和依赖没问题。曾经出现过
"构建全绿、用户打开却是白屏"——原因是一个运行时错误（`useFluent().theme` 拿到 `undefined`），
所以 CI 会把构建产物真正跑一遍，断言导航文案出现、`#root` 有内容、控制台没有未捕获异常。

`npm run dev` 是日常开发的主力：**不需要服务、不需要 Windows 也能把六个页面全部看一遍**，
因为数据链路会自动走 `data/mock`。真机联调时用 `npm run tauri dev`。

## 数据是怎么流动的

```
组件 → data/queries.js → data/transport.js →（Tauri invoke）→ Rust service_request → 命名管道 → 服务
                                   ↓ 没有 Tauri 运行时
                            data/mock/transport.js（假服务，返回同样形状的响应）
```

组件**永远不直接取数**，也不判断"服务在不在"。降级判断只在 `queries.js` 里做一次，
示例数据注入只在 `data/mock/index.js` 里做一次。

## 缺数据的指标：放示例数据 + 写明原因

服务当前有一部分指标拿不到（可行性分析第 5 节有完整矩阵）。这些位置**先用示例数据把界面填满**，
规则是：

1. 假数据只能来自 `data/mock/`，尤其是 `sources.js`；
2. 每个条目必须写清 `reason`（为什么拿不到）和 `reference`（结论出处）；
3. 凡是显示假数据的地方都带一个"示例"角标（`MockBadge`），悬停可以看到原因；
4. 设置页的"数据来源"面板列出所有字段是"服务"还是"示例"；
5. 服务接上某个指标后，**删掉 `sources.js` 里对应的条目即可**，组件不需要改。

当前用示例数据占位的指标（2026-10 在 HONOR BCC-N 上实测后收敛）：

| 指标 | 状态 | 原因（摘要） |
|---|---|---|
| 电池温度 | **确认拿不到** | `root\wmi:BatteryTemperature` 类存在但没有实例 |
| 适配器功率 | **确认拿不到** | `0x0902` 只给电压（实测 20000 mV），电流命令未确认 |
| 电池健康度 / 设计容量 | **确认拿不到** | 没有设计容量来源；`MaxCapacity` 实测等于当前满充容量 |
| CPU / GPU / SSD 温度 | **确认拿不到** | ACPI 热区不可用、`Win32_TemperatureProbe` 与 SMART 均无实例 |
| 风扇转速 | 语义未确认 | `Win32_Fan` 无实例；荣耀通道 `0x0802` 需在服务里验证 |
| 风扇曲线、功耗限制（PL1/PL2/TDP） | 不可写 | 写路径未定位（PL）/ 需要荣耀内核驱动（风扇） |

**已经不再是示例数据的**：电量、插电状态、**电池功率（带符号，实测 +80.6 W 充电中）**、满充容量、**循环次数**——
这些由服务端 `BatteryService` + 1 Hz `TelemetrySampler` 提供，面板直接显示真实值。

示例数据不是随机数：`generator.js` 是一个小的物理模型（充电功率 → 电池温度 → 核心温度 → 风扇转速，
适配器功率 = 系统负载 + 充入电池的功率），并且是**时间的纯函数**，所以实时值与历史曲线自洽。

## 与托盘、服务的关系

面板是三个进程里的一个：它**不做任何硬件操作**，也不持有托盘图标。

```text
HonorControl.Service.exe   LocalSystem：唯一的策略与状态权威（阈值、模式、遥测、历史、托盘策略）
HonorControl.Tray.exe      用户会话、普通权限：托盘图标与菜单
honor-control-panel.exe    用户会话、普通权限：本面板
```

- **拉起托盘**：默认策略 `OnDemand` 的含义是"用户在场才出现托盘"，而"用户在场"最直接的信号就是打开面板——
  所以面板启动后会调用 Tauri 命令 `launch_tray` 把托盘拉起来（托盘有单实例互斥量，重复调用无害）。
  策略 `Off` 时不拉；`Always` 由服务在拥有者的会话里直接拉起（复制 explorer 令牌 + `CreateProcessAsUser`）。
  命令实现在 `src-tauri/src/main.rs`，解析 `..\tray\HonorControl.Tray.exe`，开发时可用
  `HONORCONTROL_TRAY_PATH` 指定路径。
- **启动服务**：面板是普通权限，靠安装器授予的 `SERVICE_START` 调 SCM（不会弹 UAC）。
- **停止服务**：托盘菜单的"退出（停止后台服务）"经管道请求服务自己停止。

## 与服务协议（重要）

面板只认**协议 v2**，与服务端按同一份契约发布：`Version` 不一致就直接报错让用户重装，
代码里没有任何"某功能只在某个服务版本可用"的分支。

因此**服务端需要同步完成 v2 升级**，否则面板一个请求都发不出去：

- `ServiceContract.ProtocolVersion` 从 1 改到 2；
- `PipeServer.Execute` 增加 `GetTelemetry` / `GetCapabilities` / `GetHistory` / `SetTrayPolicy` / `ShutdownService`
  （`GetState` 与三个既有写命令保留，命令名不变）；
- 服务端新增采样器、能力探测与历史存储（命令与字段见 `docs/service-tray-tauri-feasibility.md` 的 5.3/5.4，改动清单见第 7 节）。

服务升级之前，用 `npm run dev` 开发界面即可——它会自动走示例数据，不需要服务在场。

## 尚未验证的部分

- **Rust 外壳没有编译过**：开发机上没有 Rust/MSVC 工具链。`src-tauri` 的代码是按 Tauri v2 的
  稳定 API 写的，但需要一次 `npm run tauri dev` 才能确认（尤其 `windowEffects` 的 Mica 取值、
  `tauri`/`tauri-build` 的具体版本）。
- **Mica**：`tauri.conf.json` 里配了 `windowEffects: { effects: ["mica"] }`，要求 Windows 11 22H2+；
  前端已备好 CSS 回退底色（`--panel-backdrop`），去掉该键也不会影响可读性。要真正透出 Mica，
  还需要把窗口设为 `transparent: true`。
- **`start_service` 依赖安装器改造**：面板是中等完整性进程，要通过 SCM 启动服务，需要安装器用
  `sc.exe sdset` 给服务安全描述符授予 Authenticated Users 的 `SERVICE_START`（不授予 STOP）。
  在改造完成前，设置页的"启动服务"会明确报错，而不是静默失败。
- **图标**：`src-tauri/icons/icon.ico` 目前是从 WinUI 项目复制的多尺寸图标，正式打包前应确认尺寸齐全。
- 打包产物体积：前端产物约 1.1 MB（gzip 约 313 KB），主要是 Fluent UI；后续可按页面做代码分割。

## 与其它部分的关系

- 托盘图标**不在这个进程里**：它是独立的 C# 进程（与服务同技术栈），由服务/面板按配置拉起，
  点击后启动本面板。设计见可行性文档 4.1 与 4.10。
- 可行性分析、数据矩阵、协议草案：`docs/service-tray-tauri-feasibility.md`。
