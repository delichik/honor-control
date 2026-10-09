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
| 图表 | Recharts 3 | 历史曲线可按 Fluent token 上色 |
| 服务数据 | TanStack Query 5 | 需要轮询、缓存、失效与错误状态；服务端快照很便宜（只读内存缓存） |
| UI 状态 | Zustand 5（带 persist） | 只放页面、主题、示例数据开关这类纯 UI 状态 |
| 构建 | Vite 8 | Tauri 官方模板同款 |

## 目录

```text
panel/
  src/
    App.jsx                 外层：左侧导航 + 页面头 + 内容区
    main.jsx                Provider（Fluent 主题 + Query Client）
    app/                    纯 UI 状态：store / theme / useNow / useElementWidth
    data/
      contract.js           与服务的通信契约（字段、命令、协议版本、口径常量）
      transport.js          唯一出口：invoke → Rust → 命名管道
      queries.js            所有轮询与写命令（含降级判断）
      derive.js             派生口径：充放电判定、系统负载、Wh 积分、格式化
      modeProfiles.js       机型档案示例（功耗墙 / 噪声 / 额定功率）
      mock/                 ← 浏览器预览与显式演示模式
        sources.js          缺哪些指标、为什么缺、依据在哪
        generator.js        时间纯函数的物理模型（自洽的示例数据）
        transport.js        浏览器/无服务时模拟服务端响应
        index.js            全量演示数据入口（applyMockPolicy）
    components/
      SectionCard.jsx       卡片外壳（设计稿的 .card）
      Icon.jsx              内联 SVG 图标（设计稿的图标库）
      Controls.jsx          ToggleSwitch / Slider / SelectorBar / 单选卡 / 统计块 / 设置行
      BatteryGauge.jsx      电池本体（电量 + 充电窗口 + 可拖动阈值）
      PowerBus.jsx          供电母线（按像素重算的线路 + 线路上的功率标签）
      PowerCard.jsx         电源卡：节点 + 母线 + 电池 + 读数
      ThermalCard.jsx       性能与散热卡：实测传感器 + 风扇 RPM
      FanRotor.jsx / SensorBar.jsx / ModeBar.jsx
      HistoryChart.jsx      历史曲线（多序列、可调高度）
      InfoBar.jsx           提示条（服务状态与写入结果共用）
      StatusBanner.jsx      服务状态横幅（组合 InfoBar）
      MockBadge.jsx         "示例数据"角标（悬停说明为什么拿不到）
      DataSourcePanel.jsx   "数据来源"面板
    styles/
      global.css            底色、字体、滚动条
      tokens.css            设计 token（深/浅两套 CSS 变量）
      design.css            卡片与图形层（设计稿的逐值移植）
      pages-*.css           各页面特有排版
    pages/                  六个页面：首页 / 电池设置 / 性能设置 / 充放电历史 / 功耗历史 / 设置
  src-tauri/                Rust 外壳：service_request（管道）、start_service（SCM）、pipe_name
  scripts/                  代码约定校验：Fluent token/图标名、示例数据与原生调用的边界
```

## 视觉规格从哪来

界面按 `docs/home-ui-design-spec.md` 实现——那是设计稿（`battery-console-winui.html`）的逐项拆解，
每个颜色、尺寸、曲线公式都标了原稿行号。样式分两层，别混：

| 层 | 谁负责 | 用什么 |
|---|---|---|
| 控件（按钮、开关、滑块、下拉） | Fluent UI v9 | `makeStyles` + `tokens.*`，跟随 Fluent 主题 |
| 卡片与图形（母线、电池、温度色阶、曲线） | 本项目 | `design.css` 的 `hc-*` 类 + `tokens.css` 的 CSS 变量 |

为什么图形层不用 Fluent token：设计稿的图形细节（135° 斜纹、母线虚线动画、六段温度色阶、
卡片 7px 圆角）用语义 token 表达不出来，逐值照搬 CSS 比重新映射更不容易走样。
主题切换只写 `<html data-theme>`（`app/theme.js`），两套变量各自生效。


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

改界面时的自查办法（不用手工点导航）：把 `scripts/shot-page.html` 复制进 `dist/`，用
`npm run preview` 起服务，然后无头浏览器打开 `/shot.html?page=battery&theme=light`——
它会先把"当前页面 + 主题"写进 localStorage，再 iframe 打开面板本体，于是任意页面、任意主题
都能一次截到图。

## 数据是怎么流动的

```
组件 → data/queries.js → data/transport.js →（Tauri invoke）→ Rust service_request → 命名管道 → 服务
                                   ↓ 没有 Tauri 运行时
                            data/mock/transport.js（假服务，返回同样形状的响应）
```

组件**永远不直接取数**，也不判断"服务在不在"。服务状态只在 `queries.js` 处理；完整示例数据只用于浏览器预览或显式演示模式。原生面板不会把缺失读数替换成模拟值。

## 缺数据的指标

服务暂时没有读到的指标保持未知；示例数据只用于浏览器预览与显式演示模式。数据来源面板会标出缺失原因。

当前采集状态：

| 指标 | 状态 | 原因（摘要） |
|---|---|---|
| 电池温度 | 当前设备未返回 | LibreHardwareMonitor 没有返回电池温度 |
| 风扇转速 | 当前设备未返回 | LibreHardwareMonitor 风扇数组为空；`0x0802` 遥测候选尚未和 RPM 独立对照 |
| 适配器功率 | 尚未接入 | `0x0902` 返回 20 V；`0x100902` 返回 5000，单位及实时/额定语义未确认 |
| 电池设计容量 / 健康度 | 已接入，待真机确认 | 由硬件库或 Windows 电池接口提供 |
| 温度传感器 | 当前返回 NVMe 与 ACPI 热区 | YMTC PC41Q-1TB-B：44 °C；ACPI TZ00：27.85 °C（不代表电池或 CPU 温度） |
| 功耗限制（PL1/PL2/TDP） | 不可写 | 服务写入路径尚未定位 |

本机已确认的真实读数包括电量、供电状态、电池功率、满充容量、循环次数、NVMe 温度和一个未映射到具体组件的 ACPI 热区温度。电池温度、风扇 RPM 和实时适配器功率当前没有可信读数。

LibreHardwareMonitor 由 `HonorControl.Service` 在 SYSTEM 服务进程中打开，控制面板仍以普通用户权限运行。硬件/驱动没有暴露的值保持为空；不会因此提升控制面板权限。

浏览器预览的示例数据来自 `generator.js`，只用于演示界面，不会出现在原生面板的服务读数中。

## 与托盘、服务的关系

面板是三个进程里的一个：它**不做任何硬件操作**，也不持有托盘图标。

```text
HonorControl.Service.exe   LocalSystem：硬件读写、遥测采样与历史记录
HonorControl.Tray.exe      用户会话、普通权限：独立托盘图标与菜单
honor-control-panel.exe    用户会话、普通权限：本面板
```

- 面板打开时会单独拉起托盘；托盘可在服务未连接时继续运行并打开面板。
- 托盘菜单的“退出托盘”只退出托盘进程，不会停止服务。
- 面板读取实时数据和历史记录；服务负责硬件采样、历史落盘及硬件控制。
- 服务启停由面板通过 SCM 执行，每次都会由 Windows 显示 UAC；普通用户无需管理员权限运行面板。
- 托盘路径解析在 `src-tauri/src/main.rs`，开发时可用 `HONORCONTROL_TRAY_PATH` 指定路径。

## 与服务协议（重要）

面板只认**协议 v4**，与服务端按同一份契约发布：`Version` 不一致就直接报错让用户重装，
代码里没有任何"某功能只在某个服务版本可用"的分支。

服务端与面板必须同步发布：

- `ServiceContract.ProtocolVersion` 为 4；未知供电状态与历史缺口以 `null` 表示；
- 服务端提供遥测、能力、历史查询与配置写命令，不保存托盘策略，也不接受自停命令；
- 服务启停只由面板通过 SCM 发起，并显式请求管理员授权；硬件读写仍全部由服务执行。

浏览器开发模式可用示例数据预览；原生面板在服务未连接或真实遥测尚未返回时显示未知值，不会先显示模拟电量。

## 尚未验证的部分

- **CI 构建**：`.github/workflows/build-windows.yml` 编译 Tauri 面板、服务、托盘与诊断工具，并打包安装器；
  目标机上的 UAC、传感器和托盘交互仍需真机验证。
- **Mica**：`tauri.conf.json` 里配了 `windowEffects: { effects: ["mica"] }`，要求 Windows 11 22H2+；
  前端已备好 CSS 回退底色（`--panel-backdrop`），去掉该键也不会影响可读性。要真正透出 Mica，
  还需要把窗口设为 `transparent: true`。
- **图标**：`src-tauri/icons/icon.ico` 目前是从 WinUI 项目复制的多尺寸图标，正式打包前应确认尺寸齐全。
- 打包产物体积：前端产物约 1.1 MB（gzip 约 313 KB），主要是 Fluent UI；后续可按页面做代码分割。

## 与其它部分的关系

- 托盘图标**不在这个进程里**：它是独立的 C# 进程（与服务同技术栈），由面板拉起，
  点击后启动本面板。设计见可行性文档 4.1 与 4.10。
- 可行性分析、数据矩阵、协议草案：`docs/service-tray-tauri-feasibility.md`。
