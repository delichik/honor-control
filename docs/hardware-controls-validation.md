# 2026-10-09 验证记录

## 构建与纯检查

- .NET 服务与托盘 Release 发布成功；服务编译零警告、零错误。
- 前端 Fluent token／图标／架构检查及 Vite 生产构建通过。
- Rust 原生 check、6 项协议／编码测试及 Tauri GNU Release 构建通过。
- Windows 电源生产模块的 24 项模拟检查通过，包含只改提交字段、硬盘秒数、回读、部分失败、激活失败、外部变更、备份失败及恢复。
- 生产 OEM 硬件类直接编译与捕获报文解析检查通过：性能命令／载荷、NTC 温度、USB 单位、符号、长度、范围及 BIOS 拒绝。
- 研究清理的备份／迁移哈希、保留工具语法、本地文档链接检查通过。

这些检查不执行真实固件、电源、显示或音频切换。

## 本机只读结果

- Windows 方案：平衡；AC/DC 关屏 600/180 秒、睡眠 0/0、硬盘空闲 60/60 秒、Intel 策略 1/1。
- LCD：五项已验证开关支持，保存状态均关闭；色温支持但仅提供官方入口；色彩管理支持标记为 0，入口禁用。
- Senary：默认录音端点能力 8195、场景 1（多人）；播放端点能力 16384、通话降噪状态 1。
- 两个原生同值调用：护眼保持关闭、麦克风保持多人。均返回 `changed=false`、`submitted=false`、`settingsConfirmed=true`、`effectsVerified=false`；没有发送显示 IPC 或音频 SET。

## 界面验收

通过官方 Chrome DevTools MCP 连接实际运行的 Tauri WebView2，不以构建通过代替界面证明。

- 真实“显示与音频”页面：五个健康显示开关、色温官方入口、色彩不支持提示、Senary 场景与对端降噪读数均正确展示。截图已检查，无布局溢出；最终原生 console 无 error／warn。
- 真实服务离线页面：性能／Windows 电源操作禁用，显示连接失败原因。核实当前已安装 `HonorControlService` 状态为 Stopped；本轮没有安装、替换或启动该服务。
- 同一 MCP 打开的 localhost 浏览器页面确认无 `__TAURI_INTERNALS__`，因此模拟交互不会操作本机设备。已验证关屏字段的选择性修改、自动维护冲突、备份恢复、显示／音频场景切换与性能回读提示。
- localhost 预览只有 favicon.ico 404；页面 JS/CSS 与数据模块正常加载，无业务 JavaScript 异常。

发现并修复：Windows PowerShell helper 不应继承 PowerShell 7 的 `PSModulePath`，否则类型元数据重复使组件调用失败。原生沙箱测试还需把 TEMP/TMP 指向工作区可写临时目录；该测试进程的环境配置没有修改系统设置。

测试截图与原始 MCP 状态保存在 `.tmp/ui-verification/`。原生截图是控件／SDK 状态证明，不是物理屏幕色彩或声学效果证明。

## 交付与尚未验证

同步更新文件位于 `artifacts/implementation-20261009/`，包括面板、GNU 构建的 Microsoft `WebView2Loader.dll`、托盘、协议 v5 服务及 SHA-256 清单。没有复制荣耀 DLL。

当前机器的实际固件智能→高能→智能、实际电源写入／恢复、显示像素效果和声学降噪质量尚未验证；安装／升级也未执行。面板、托盘与服务需要同步升级到 v5 后，才能对服务路径做端到端切换验收。
