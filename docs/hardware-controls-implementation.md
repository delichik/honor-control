# 硬件控制接入与验证

更新：2026-10-09。当前协议为 v5，面板、托盘与服务必须同步更新。

## 已实现

| 功能 | 执行位置与行为 |
|---|---|
| 电池温度 | LocalSystem 服务读取荣耀 NTC `0x0E0202`；没有有效读数时使用硬件库读数或 null |
| USB 供电诊断 | 服务每 10 秒批量读取输入电压／电流，独立展示 V、A 与计算瓦数；不填入实时功耗、额定功率或耗电历史 |
| 智能／高能 | 服务写 `0x0F04` + 0/1，再回读 `0x0E04` 与既有 Windows 方案；去掉错误的 HUNTER 能力门槛；智能回退不要求 AC／20% 电量 |
| 性能操作结果 | 保存立即标记等待，同一请求的固件／方案回读才确认；失败显示原因与回滚结果，新请求不被旧回读确认 |
| 系统电源 | 服务枚举既有 Windows 方案，选择性修改 AC/DC 关屏、睡眠、硬盘空闲和可用的 Intel 显卡策略；写前保存原值，逐项回读，失败回滚，可显式恢复 |
| 健康显示 | 交互会话中调用已安装、已签名及已验证版本的荣耀 LCD IPC：护眼、电子书、舒适、离焦、类自然光 |
| 智慧音频 | 交互会话中调用已安装 Senary C 接口，对默认端点设置麦克风场景与通话对端降噪，检查返回值及 SDK 回读 |
| 色温／色彩与其他音效 | 提供官方页面入口；色彩管理依本机能力记录禁用或允许入口，未用近似滤镜或通用 ICC 冒充官方校准 |

## 权限、恢复与互斥

服务管道仍只允许配置拥有者访问。普通面板不直接获得 WMI 或 Windows 电源设置写入权限；服务以 LocalSystem 执行这些操作。

Windows 电源备份保存在 `%ProgramData%\HonorControl\windows-power-backup.json`，仅 SYSTEM／Administrators 可写。恢复前检查本工具最后写入的值是否仍一致，外部程序已修改时停止恢复并保留备份。只写用户提交字段，不创建、导入、重置方案。

独立方案选择／恢复与性能自动维护、未完成的性能保存请求互斥，避免另一策略立即改回。保存充电配置或关闭自动维护不会顺便重新应用旧性能目标。各项失败计数只能通过重新保存对应策略重置。

显示／音频使用当前用户会话中的短时隔离 helper，避免 Session 0 显示上下文和厂商 DLL 全局状态影响面板。固定 DLL 路径、有效 Honor 发布者签名、已验证 SHA-256 和明确功能／枚举同时校验。不同版本会禁用直接控制并说明原因；不复制或分发 OEM DLL，不操作声纹注册或录音。

健康显示回读来自官方保存配置，不证明像素／面板效果。音频 SDK 回读确认场景，也不证明实际声学降噪质量。界面与返回值分别标记设置确认、请求提交及效果验证。

## 构建与检查

```powershell
# 工程常规构建（.NET SDK 8.0.408）
dotnet build src/HonorControl.Service/HonorControl.Service.csproj --configuration Release
dotnet run --project tools/WindowsPowerProbe/WindowsPowerProbe.csproj
pwsh -File tools/Test-OemTelemetryProtocol.ps1

cd panel
npm run verify
npm run build
npm run tauri build
```

`WindowsPowerProbe` 默认使用生产模块的模拟 API，覆盖 24 项输入、选择性写、失败回滚、外部变更与恢复场景，绝不写真实电源设置。`--read-only` 才查询真实 Windows 设置；OEM 协议脚本默认只编译生产代码和解析捕获报文，`-ReadHardware` 才执行只读硬件 GET。

Rust 的 6 项测试检查协议 v5／载荷分区、错误识别，以及 Windows PowerShell 的 UTF-16LE/Base64 中文、引号与代理对编码。前端通过 Chrome DevTools MCP 检查运行中的页面；浏览器示例与原生 SDK／服务结果分别记录。

本机使用系统 .NET host 加载仓库 SDK 解决仓库 runtime 初始化问题：

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PWD '.nuget-packages'
$env:DOTNET_HOST_PATH = 'C:\Program Files\dotnet\dotnet.exe'
& $env:DOTNET_HOST_PATH ./.dotnet/sdk/8.0.408/dotnet.dll build src/HonorControl.Service/HonorControl.Service.csproj --no-restore --configuration Release -p:UseSharedCompilation=false -m:1 -nr:false
```

本机无 MSVC／全局 Rust，使用项目 `.tmp/rust-gnu` 内的官方 Rust GNU 1.99.0 与既有 CLion MinGW，实际完成 check、test 和 Tauri Release 构建。GNU 产物旁必须带官方依赖中的 x64 `WebView2Loader.dll`；CI 仍使用标准 Windows Tauri 构建，不把本机工具路径写入产品配置。

## 研究证据

- [当前硬件研究汇总](../research/conclusions.md)。
- [OEM 控制接口与只读实证](../research/oem-controls-20261009.md)。
- [显示、音频与系统电源调用链](../research/honor-display-audio-power-20261009.md)。
- [研究清理与备份记录](../research/cleanup-20261009.md)。

已安装服务和新协议客户端的兼容、安装／升级、实际固件往返与显示／声学效果，必须按运行结果单独确认；构建和模拟检查不替代这些实证。
