# 荣耀控制中心

这是一个原生 Windows WinUI 3 / Windows App SDK 项目，不是 PowerShell 脚本或 WPF 仿制界面。它通过荣耀已验证的 ACPI-WMI 通道控制智能充电，并提供性能模式的查询和受确认保护的写入。

## 项目结构

```text
HonorControl.sln
src/HonorControl/
  App.xaml                         WinUI 3 应用资源
  MainWindow.xaml                  Fluent 主窗口与 Mica 系统材质
  ViewModels/MainViewModel.cs      UI 状态、前置条件和操作编排
  Services/OemWmiClient.cs         OemWMIMethod WMI 协议实现
  Services/SystemPowerService.cs   Windows AC/电量状态读取
  Models/                          充电阈值、性能模式模型
  app.manifest                     强制 UAC 管理员权限
```

## 打开和构建

使用 Visual Studio 2022，并安装“.NET 桌面开发”工作负载、.NET 8 SDK 和 Windows App SDK 支持，打开 `HonorControl.sln` 后构建 Release/x64。项目目标为 `net8.0-windows10.0.22000.0`（Windows 11），首次构建会还原 `Microsoft.WindowsAppSDK` 与 `Microsoft.Management.Infrastructure` 包，生成 `HonorControl.exe`。

仓库通过 `global.json` 固定使用 .NET SDK `8.0.408`，避免被机器上更高版本的 SDK 自动选中；Windows App SDK 1.6 当前应使用该 .NET 8 SDK 构建。

当前工作站没有本地 .NET SDK，仓库以 GitHub Actions 的 Windows 构建结果为准。编译后的首次运行会出现 UAC 提示，这是 BIOS WMI 接口的必要权限。

## GitHub Actions 构建

仓库包含 `.github/workflows/build-windows.yml`。推送 `src/HonorControl/`、解决方案或该工作流的改动后会自动构建；也可在 GitHub 的 **Actions → Build Honor Control → Run workflow** 手动触发。

工作流在 GitHub 的 Windows runner 上还原 .NET 8 和 Windows App SDK 依赖，并使用 Visual Studio 的 `MSBuild.exe`（含 Windows App SDK 生成 PRI 所需的 Appx 打包任务）同时发布两个 `win-x64` artifact：

- `HonorControl-win-x64`：便携版，包含 .NET 8 与 Windows App SDK 运行时；下载并解压后可直接运行。
- `HonorControl-win-x64-lightweight`：轻量版，不携带上述两套运行时；目标电脑必须预先安装 x64 的 **.NET 8 Desktop Runtime** 和 **Windows App SDK 1.6 Runtime**。

无论选择哪个版本，都应完整解压 artifact，并保留 `HonorControl.exe` 同目录的 DLL、PRI 和原生运行时文件。

当前工作流不会发布 GitHub Release，也没有代码签名。未签名的自包含 exe 可能触发 Windows SmartScreen；如需面向外部分发，应另行配置代码签名证书与受保护的 GitHub Actions secret。

## 已实现的控制行为

- 启动预检：展示荣耀 HWMI 接口、AC 供电和电池电量；各功能单独判定可用性。
- 智能充电开启：发送 `0x1003`，载荷 `{40, 70}`；成功后发送 `0x1103` 回读，并明确比较请求与实际阈值。
- 关闭充电限制：发送 `0x1003`，载荷 `{0, 100}`；同样回读并比较。若不符，不会宣称设置成功，也不会对未识别机型盲发 Linux 专用 quirk。
- 自定义阈值：仅接受 `0 <= start <= end <= 100`，在提交前给出即时校验说明。
- 性能状态：读取 `0x0802`、`0x3C06` 和 `0x0902`，保留原始响应，并解析 `0x3C06` 的支持掩码和适配器输出值。
- 性能写入：智能模式发送 `0x0C07` + payload `0`，高能模式发送 payload `1`。仅在性能接口可用、接通 AC、电量至少 20%、BIOS 报告有效适配器输出，且高能支持位存在时启用；确认后会立即重新检查这些条件。

## 交互与视觉设计

- 使用概览、智能充电、性能模式、诊断和设置五个独立导航内容区，避免把所有控制堆在同一页面。
- 性能模式先选择，再在“待应用变更”区确认，避免误触直接写入 BIOS。
- 每次智能充电写入都展示下发、BIOS 响应与阈值回读验证；性能写入只报告“BIOS 接收命令 + 原始查询刷新”，不把未验证模式语义伪装为成功，并保留最近 8 条会话操作记录。
- 原始 WMI 返回和接口错误收纳于“诊断详情”，不干扰日常操作。
- 使用 WinUI 3 Fluent 控件、Mica 系统材质、原生 InfoBar、ContentDialog、RadioButtons、Expander 和自动可访问的键盘交互。
- 设置页支持跟随系统、浅色和深色主题；偏好保存在当前用户的 `%LocalAppData%\HonorControl\settings.json`。

所有请求固定为 64 字节，通过 Windows CIM/MI 调用 `root\\wmi:OemWMIMethod` 的 `ACPI\PNP0C14\HWMI_0` 实例及 `OemWMIfun` 方法；这与研究阶段成功的 `Get-CimInstance` / `Invoke-CimMethod` 路径一致。项目不复制、加载或分发荣耀 DLL。

## 已知风险与验证边界

智能充电的读写协议已有实测。性能模式 SET 的命令和 payload 来自逆向结论，尚未进行写入实测；模式 1/2 的中文语义、电脑管家电源计划、风扇及 GPU 联动均不能由本程序的单个 BIOS 命令证明。程序因此保留警告、二次确认、写入前即时复核和原始响应。首次切换应在 AC 供电、电量充足且适配器功率符合机型要求时进行，随后检查荣耀电脑管家、风扇和功耗行为。

荣耀电脑管家可能在重启、插拔电源或服务恢复时重新下发其记忆的配置。本项目刻意不写入电脑管家的注册表设置，也不操作风扇、GPU 模式或 Windows 电源计划。
