# Honor Control

Windows 11 x64 的荣耀设备控制工具。React + Tauri 面板显示设备状态；独立 LocalSystem 服务负责荣耀 WMI、电源配置与历史采样；普通用户会话中的托盘独立运行。

## 功能

- 充电阈值：预设和自定义 `0 <= start < end <= 100`，由服务写入并回读。
- 电池温度：荣耀 NTC 与硬件库的有效读数；无读数时保持未知。
- 智能／高能：固件 `0x0F04` 写入、`0x0E04` 回读，同步已有 Windows 方案；显示保存、等待、回读和失败结果。
- USB 输入诊断：电压、电流和官方瓦数计算值；不冒充实时适配器功耗、铭牌额定功率或耗电历史。
- 系统电源：既有方案选择，AC/DC 关屏、睡眠、硬盘空闲及可用的 Intel 显卡策略；只改提交字段，回读、失败回滚、备份恢复。
- 健康显示：利用本机已安装的官方 LCD 组件控制护眼、电子书、舒适、离焦和类自然光。色温与其他校准提供官方入口。
- 智慧音频：已验证 Senary 版本的默认麦克风场景和通话对端降噪，SDK 回读；其他声卡／功能提供准确限制或官方入口。
- 历史与传感器：服务采样、历史缺口与能力标记；原生面板不以示例值填补未知读数。

色彩管理按本机能力开放官方入口；没有逐屏校色资料时不提供假校准模式。设备／组件版本不同，直接控制接口可能不可用；界面显示具体原因。

## 进程与权限

```text
HonorControl.Service.exe   LocalSystem：硬件、Windows 电源、配置与历史
HonorControl.Tray.exe      用户会话：托盘菜单与面板入口
honor-control-panel.exe    用户会话：React/Tauri 面板及官方显示／音频组件调用
```

面板单独拉起托盘；关闭面板或退出托盘不停止服务。服务启停由 SCM 显式请求 UAC，与托盘退出分开。面板不是管理员程序；只有服务写荣耀 WMI 和 Windows 电源设置。显示／音频在当前用户会话执行，避免 Session 0 显示上下文问题。

当前通信协议 **v5**，管道名保留 `HonorControl.Service.v1`；面板、托盘与服务同步更新，版本不匹配会明确报错。服务管道只授权配置拥有者。服务配置和电源恢复备份在 `%ProgramData%\HonorControl`，仅 SYSTEM／管理员可写。

电脑管家交互进程在运行时暂停服务的固件写入。独立方案选择／恢复与性能自动维护、尚未完成的明确性能请求互斥。详情见 [硬件控制实现说明](docs/hardware-controls-implementation.md)。

## 目录与构建

```text
src/HonorControl.Contracts    v5 共享契约
src/HonorControl.Service      Windows 服务
src/HonorControl.Tray         独立托盘
panel/                       React + Fluent UI + Tauri
tools/                       工程与验证工具
research/                    当前结论、原始证据、归档与静态分析工具
src/HonorControl/             保留的 WinUI 参考实现，不进入当前安装包
```

```powershell
dotnet build src/HonorControl.Service/HonorControl.Service.csproj --configuration Release
dotnet build src/HonorControl.Tray/HonorControl.Tray.csproj --configuration Release
pwsh -File tools/Test-OemTelemetryProtocol.ps1
dotnet run --project tools/WindowsPowerProbe/WindowsPowerProbe.csproj
cd panel
npm ci
npm run verify
npm run build
npm run tauri build
```

.NET SDK 锁定 8.0.408；原生 Tauri 构建需要 Rust 与 Windows 链接工具。浏览器 `npm run dev` 使用明确标注的示例数据。当前功能与运行方式见 [面板说明](panel/README.md)；本机 GNU 工具链的实际构建记录见 [实现说明](docs/hardware-controls-implementation.md)。

GitHub Actions 工作流构建面板、托盘、服务与安装器，并运行纯协议解析、电源方案只读检查及电源回滚模拟。不会在 CI 发送固件 SET 或测试真实音频／显示切换。

## 研究与验证边界

- [有效结论汇总](research/conclusions.md)：以最新函数级证据为准。
- [显示／音频／系统电源研究](research/honor-display-audio-power-20261009.md)。
- [OEM 控制接入实证](research/oem-controls-20261009.md)。
- [旧研究整理及备份记录](research/cleanup-20261009.md)。
- [Windows 容器构建记录](docs/windows-container-build.md)。
- [第三方声明](THIRD-PARTY-NOTICES.md)。

不复制、分发荣耀 DLL；直接调用前验证签名、发布者、已验证版本及能力。显示保存配置回读不等于像素效果验证，音频场景回读不等于声学质量验证；构建、模拟、只读探针与真实控制结果分别记录。

固件往返、失败回滚、真实显示／声学效果及安装／升级需要在目标机器验证；本机研发产物不自动覆盖当前安装。
