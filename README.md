# 荣耀控制中心

这是一个 Windows 11 x64 的 WinUI 3 应用。普通权限界面保存期望配置并读取服务看到的真实状态；独立 Windows 服务通过荣耀 ACPI-WMI 通道回读和校正充电阈值、性能模式。

## 项目结构

```text
HonorControl.sln
src/HonorControl/
  App.xaml                         WinUI 3 应用资源
  MainWindow.xaml                  Fluent 主窗口与 Mica 系统材质
  ViewModels/MainViewModel.cs      期望配置编辑与服务状态展示
  Services/ServiceClient.cs        本机管道客户端
  Services/TrayIconController.cs   原生 Win32 托盘与窗口生命周期
  Assets/HonorControl.ico          应用和托盘多尺寸图标
src/HonorControl.Contracts/         界面与服务的版本化通信契约
src/HonorControl.Service/           Windows 服务、配置协调、WMI 与电源方案
installer/                          在线安装器与设备/依赖预检
```

## 打开和构建

使用 Visual Studio 2022、.NET SDK 8.0.408、Windows App SDK 及 Inno Setup 构建。解决方案包含界面、通信契约和服务；单独运行 `HonorControl.exe` 不能提供控制功能，需安装并启动服务。

仓库通过 `global.json` 固定 .NET SDK `8.0.408`；界面使用 Windows App SDK `2.4.0`。

GitHub Actions 负责发布和打包。安装器要求管理员权限以注册服务；日常界面以普通用户权限运行。CI 编译成功不等于目标机器上的安装、界面、WMI 或服务验证通过。

## GitHub Actions 构建

仓库包含 `.github/workflows/build-windows.yml`。推送 `src/HonorControl/`、解决方案或该工作流的改动后会自动构建；也可在 GitHub 的 **Actions → Build Honor Control → Run workflow** 手动触发。

工作流发布框架依赖版 WinUI 程序与服务，编译 `HonorControl-Setup-x64.exe`。安装包不包含 .NET Desktop Runtime 或 Windows App Runtime；安装前先检查 Windows 11 x64、荣耀厂商标识和 HWMI 只读回读，然后仅在缺少依赖时从微软官方下载 .NET 8 Desktop Runtime 及 Windows App Runtime 2.4。Windows App Runtime 检查以当前登录用户的包注册状态为准，避免把其他用户已安装误判为可用。下载的 .NET 安装器校验官方 SHA-512，两者均校验微软数字签名。网络或校验失败会阻止应用文件安装。

工作流尚未发布 GitHub Release，也没有代码签名。面向外部分发前，需签名安装器、界面和服务，并在目标荣耀电脑上完成安装/升级/卸载与硬件验证。升级前会备份旧安装，安装失败时尝试恢复文件和服务；这一恢复流程尚未经真实升级验证，不能视为事务性回滚。

## 已实现的控制行为

- 服务启动后独立回读硬件状态；界面只显示服务快照和已保存的期望配置，不直接调用 WMI。
- 保存配置会唤醒服务；开启“开机自动维护配置”后，服务在启动和运行期间周期性比对，只有不一致时才尝试写入并回读验证。
- 检测到荣耀电脑管家交互进程时，服务只读；电脑管家关闭后继续处理等待中的配置。连续写入失败会暂停该项校正，直至再次保存配置。
- 智能充电开启：发送 `0x1003`，载荷 `{40, 70}`；成功后发送 `0x1103` 回读，并明确比较请求与实际阈值。
- 关闭充电限制：发送 `0x1003`，载荷 `{0, 100}`；同样回读并比较。若不符，不会宣称设置成功，也不会对未识别机型盲发 Linux 专用 quirk。
- 自定义阈值：通过 0–100% 下拉框选择，仅接受 `0 <= start < end <= 100`；不符合条件的选项会禁用。
- 性能状态：以 `0x0E04` 读取当前模式，同时读取 `0x0802`、`0x3C06` 和 `0x0902` 并保留诊断响应。
- 性能写入由服务执行：智能模式发送 `0x0C07` + payload `0`，高能模式发送 payload `1`；写前复核 AC、电量至少 20%、固件模式和目标电源方案，写后回读固件与 Windows 电源方案。

## 交互与视觉设计

- 设备概览统一展示充电管理和性能模式；外观、后台行为和故障排查统一收纳到设置页。
- 界面启动和窗口激活时获取服务快照，窗口可见且未最小化时每 15 秒更新；服务不依赖窗口或托盘持续运行。
- 页面共享相同的内容宽度、间距、卡片、选择项和操作按钮规格，并使用 Segoe Fluent 图标建立清晰层级。
- 页面明确区分期望配置和服务回读的实际状态；保存成功不表示硬件已同步。
- 性能配置保留风险提示和二次确认；故障排查显示服务报告的读取、校正错误。
- 设置页支持跟随系统、浅色和深色主题，以及“关闭窗口时驻留托盘”；偏好保存在当前用户的 `%LocalAppData%\HonorControl\settings.json`。
- 系统托盘使用 Windows 原生 `Shell_NotifyIcon`：支持重新打开、显式退出、首次驻留提示和 Explorer 重启后的图标恢复，不依赖第三方托盘组件。

服务以 LocalSystem 运行，配置保存在仅 SYSTEM/管理员可写的 `%ProgramData%\HonorControl\service.json`。首次从本机活动控制台连接的 Windows 用户成为配置拥有者；之后仅该用户可通过本机管道读写服务配置。关闭界面或托盘退出不会停止服务。

所有请求固定为 64 字节，通过 Windows CIM/MI 调用 `root\\wmi:OemWMIMethod` 的 `ACPI\PNP0C14\HWMI_0` 实例及 `OemWMIfun` 方法；这与研究阶段成功的 `Get-CimInstance` / `Invoke-CimMethod` 路径一致。项目不复制、加载或分发荣耀 DLL。

## 已知风险与验证边界

智能充电的读写协议已有实测。性能模式 SET 的命令和 payload 来自逆向结论，尚未进行写入实测；模式 1/2 的中文语义、电脑管家电源计划、风扇及 GPU 联动均不能由单个 BIOS 命令证明。服务在写入前复核，并在失败时尝试回滚；首次切换仍需在目标机观察风扇、功耗和电脑管家行为。

荣耀电脑管家可能在重启、插拔电源或服务恢复时重新下发其记忆的配置。本项目不写入电脑管家的注册表设置，也不操作风扇或 GPU 模式。性能切换会同步已有的 Windows 电源计划（智能对应平衡，高能对应 Honor Performance），失败时尝试回滚；不会自动创建或重置电源计划。读取失败与目标计划不存在会分别提示。
