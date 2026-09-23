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

使用 Visual Studio 2022，并安装“.NET 桌面开发”工作负载、.NET 8 SDK 和 Windows App SDK 支持，打开 `HonorControl.sln` 后构建 Release/x64。项目目标为 `net8.0-windows10.0.19041.0`，首次构建会还原 `Microsoft.WindowsAppSDK` 与 `System.Management` 包，生成 `HonorControl.exe`。

当前工作站未执行编译；这不影响项目结构或源代码交付。编译后的首次运行会出现 UAC 提示，这是 BIOS WMI 接口的必要权限。

## GitHub Actions 构建

仓库包含 `.github/workflows/build-windows.yml`。推送 `src/HonorControl/`、解决方案或该工作流的改动后会自动构建；也可在 GitHub 的 **Actions → Build Honor Control → Run workflow** 手动触发。

工作流在 GitHub 的 Windows runner 上还原 .NET 8 和 Windows App SDK 依赖，发布自包含 `win-x64` 版本，并上传 `HonorControl-win-x64` artifact。下载并解压该 artifact 后，运行其中的 `HonorControl.exe`；保留同目录的全部 DLL 和运行时文件。

当前工作流不会发布 GitHub Release，也没有代码签名。未签名的自包含 exe 可能触发 Windows SmartScreen；如需面向外部分发，应另行配置代码签名证书与受保护的 GitHub Actions secret。

## 已实现的控制行为

- 启动预检：展示荣耀 HWMI 接口、AC 供电和电池电量；各功能单独判定可用性。
- 智能充电开启：发送 `0x1003`，载荷 `{40, 70}`；成功后发送 `0x1103` 回读验证。
- 关闭充电限制：发送 `0x1003`，载荷 `{0, 100}`；同样回读验证。
- 自定义阈值：仅接受 `0 <= start <= end <= 100`，在提交前给出即时校验说明。
- 性能状态：读取 `0x0802` 和 `0x3C06`，保留前 8 字节原始响应，避免把未证实的字段含义伪装为结论。
- 性能写入：智能模式发送 `0x0C07` + payload `0`，高能模式发送 payload `1`。仅在性能接口可用、接通 AC 且电量至少 20% 时启用；写入前需要再次确认，写入后重新查询状态。

## 交互与视觉设计

- 单页硬件控制台，而非堆叠的默认控件或伪仪表盘；普通用户只看结论与可执行操作。
- 性能模式先选择，再在“待应用变更”区确认，避免误触直接写入 BIOS。
- 每次写入展示下发、BIOS 响应与回读验证的阶段性结果，并保留最近 8 条会话操作记录。
- 原始 WMI 返回和接口错误收纳于“诊断详情”，不干扰日常操作。
- 使用 WinUI 3 Fluent 控件、Mica 系统材质、原生 InfoBar、ContentDialog、RadioButtons、Expander 和自动可访问的键盘交互。
- 支持浅色和深色主题；主题切换使用 WinUI 的 `RequestedTheme`，不手写模拟 Windows 控件。

所有请求固定为 64 字节，调用 `root\\wmi:OemWMIMethod` 的 `ACPI\PNP0C14\HWMI_0` 实例及 `OemWMIfun` 方法。项目不复制、加载或分发荣耀 DLL。

## 已知风险与验证边界

智能充电的读写协议已有实测。性能模式 SET 的命令和 payload 来自逆向结论，尚未进行写入实测；程序因此保留警告和二次确认。首次切换应在 AC 供电、电量充足且适配器功率符合机型要求时进行，随后检查荣耀电脑管家、风扇和功耗行为。

荣耀电脑管家可能在重启、插拔电源或服务恢复时重新下发其记忆的配置。本项目刻意不写入电脑管家的注册表设置，也不操作风扇、GPU 模式或 Windows 电源计划。
