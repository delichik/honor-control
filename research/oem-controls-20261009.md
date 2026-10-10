# 官方显示与 Senary 音频接入实证

日期：2026-10-09。实现文件：`panel/src-tauri/src/oem.rs`。本记录补充先前研究，记录已还原的明确 ABI 与本轮只读运行结果。显示与音频 SET 由主 Agent 集中验证；本子任务未执行 SET、声音录制或声纹读取。

## 接入边界

- 健康显示的 5 个开关发送官方 `MonitorManage.exe` 使用的 JSON IPC，由已安装的 LCD 后台执行。未自行模拟 Gamma 或健康效果。
- 色温多参数控制尚未接入，可打开官方健康显示窗口；色彩管理依据本机支持记录显示为不支持，不能提供虚假模式。
- 智慧音频对接已安装 `SenaryInterface.dll`，仅控制当前默认音频端点的麦克风场景和通话对端降噪。没有录音、声纹注册、声纹查询或下载／分发 OEM DLL。
- 官方智慧音频 UI 在 `PCManager.exe` 内部插件中，未找到可靠的独立页命令行。入口明确表示打开管家后进入智慧音频。
- 显示回读是官方保存设置，可能滞后；音频回读是 SDK 的端点设置。两者均不能代替像素或声学效果验证，返回 `effectsVerified=false`。

## 1. 健康显示 ABI 与协议

对 `MonitorManage.exe` 使用 .NET Framework 的 ReflectionOnlyLoad，读取类型、P/Invoke 特性与方法 IL；未执行该程序集方法。

`MonitorManage.MainWindow.PostHealthDisplayStateFromMonitor` 的原始声明：

```text
Int32 PostHealthDisplayStateFromMonitor(System.String)
DllImport("MonitorManageTrans.dll",
  EntryPoint="PostHealthDisplayStateFromMonitor", CharSet=1,
  CallingConvention=2, PreserveSig=True)
```

这里 `CharSet=1` 为 None（实际默认 ANSI），`CallingConvention=2` 为 StdCall。本机 DLL 是 x64，C 参数为 `const char *`；程序传入的 JSON 均为 ASCII。未手工传入 C++ 字符串或未还原的显示结构体。

`MonitorManage.JsonHelper.RequestData` 和 `Params` 的 DataMember 明确写出小写字段 `action`、`params`、`key`、`value`。UI 的真实用户事件调用如下：

| UI 方法 | action | key | 开／关 value |
|---|---|---|---|
| `UserSwitch_Open/Close` | `1` | `EyeProtectSwitch` | `1` / `0` |
| `EbookSwitch_Open/Close` | `1` | `EBookSwitch` | `1` / `0` |
| `SwitchComfortDisplay_Open/Close` | `1` | `ComfortDisplaySwitch` | `1` / `0` |
| `SwitchDeFocusEye_Open/Close` | `1` | `DefocusEyeProtectSwitch` | `1` / `0` |
| `NaturalLightSwitch_Open/Close` | `1` | `NaturalLightSwitch` | `1` / `0` |

例如护眼开关发送：

```json
{"action":"1","params":{"key":"EyeProtectSwitch","value":"1"}}
```

`MonitorManageTrans.dll` 导出 RVA `0x8640` 最终转入 `0x4F20`：创建 `IPCMessageClient`，本方模块 `0x1EB0000`，目标模块 `0x1EA0000`，消息号与响应号 `0x1EA0003`，载荷为上述窄字符串。其内部调用 `PostIPCMessage` 的返回值随后被日志和析构调用覆盖，**导出 Int32 返回值不能当作 IPC ACK**。实现忽略该返回值，发送后轮询官方保存设置；相同的已保存状态不再发送命令，返回 `submitted=false`。

LCD 后台程序集也已读取：`IPCHelper.OnRecvSetHealthDisplayService` → `ServiceCommand.SetHealthDisplayParamet` → `Service.Command_SetHealthDisplaySetting` → `HealthDisplayManager`。后台负责实际效果与官方状态保存，面板没有直接修改显示注册表。

官方 UI 在 HDR、独显直连等情况下存在限制。本实现依据官方记录的 `HdrAcmEnabled`、`UniqueDirectEnabled` 和能力标记预先限制调用；这些记录仍可能滞后，后台回读与真实效果验证不能省略。

## 2. Senary 音频 C ABI

静态证据来自 `SenaryInterface.dll` C 导出 wrapper，以及官方 `AdjustSmartAudioScenes.dll` 的 `SenaryAudioAlgInterfaceImp`。

| 导出 | ABI | 证据 |
|---|---|---|
| `GetCaptureAbilityByEndpoint` | `int(const char *endpoint)` | RVA `0x3C760` → `0x38820`；null 分支查询默认录音端点 |
| `GetCaptureCurrentMode` | `int(const char *endpoint)` | RVA `0x3C780` → `0x38600`；null 分支查询默认录音端点 |
| `GetRenderAbilityByEndpoint` | `int(const char *endpoint)` | RVA `0x3C7A0`；官方后台动态解析并按端点读取 |
| `GetRenderStatus` | `int(const char *endpoint)` | RVA `0x3C7C0`；官方后台按端点回读 |
| `SetCaptureEffectMode` | `int(int mode, const char *endpoint)` | RVA `0x3C820` 保存 ECX、RDX，转入 `0x394C0`；null 分支选择默认录音端点 |
| `SetRenderStatus` | `int(int enable, const char *endpoint)` | RVA `0x3C850` → `0x39290`；仅接受 0、1，null 分支选择默认播放端点 |

官方后台先 `WcharToChar` 再把 endpoint 传给 C API；SDK 将非空 endpoint 从 UTF-8 转为宽字符串。实现只使用明确存在的 null 默认端点分支，不传猜测端点或结构体。

麦克风枚举已从官方后台读取／设置函数交叉确认：

- `0` → `ByPass`：关闭场景处理。
- `1` → `Multiplayer`：多人场景。
- `2` → `Individual`：单人场景。

官方 `GetMicAudioEffectMode` RVA `0x136F0` 对上述值还原字符串，`SetMicAudioEffectMode` RVA `0x142C0` 根据字符串使用相同值调用 `SetCaptureEffectMode`。某些 3.5mm 麦克风另有特殊处理，本实现仅在已验证默认端点能力 profile 上开放控制。

`SetCallNoiseReductionEnable` RVA `0x15800` 调用 `SetRenderStatus`，不是音量控制。SDK 新分支 `0x392B0` 明确将 0、1 映射到下行处理关闭／开启，成功返回值为 `1`。实现需 SET 返回 1 且对应 GET 回读与请求相同，才报告 `settingsConfirmed=true`；仍不声称降噪声学效果已验证。

## 3. 只读运行结果

已在隔离的 64 位 Windows PowerShell 子进程中验证加载与 GET：

```json
{"captureAbility":8195,"captureMode":1,"renderAbility":16384,"renderStatus":1}
```

嵌入 Rust 的完整查询脚本也已通过实际执行，输出有效 JSON。本机默认麦克风场景为多人，默认播放端点的通话降噪状态为开启；管家保存设置分别为 `MicScene=Multiplayer`、`SpeakerSoundEffect=Music`、`CallNoiseStatus=1`。

能力 profile 的位语义未完整文档化，代码仅在实测 `8195` / `16384` profile 下开放相应控制，没有从数值猜测任意位含义。SDK ABI 调用在隔离子进程中进行，不初始化、录制麦克风流，也不调用 VoiceID 接口。

健康显示能力记录支持护眼、电子书、舒适、离焦、类自然光和色温；开关保存状态均为关闭。色彩管理标记为 0。完整只读查询快照见 `oem-read-state-20261009.json`。

## 4. 版本与加载保护

已检查相关 DLL 的 PE machine 为 `0x8664`（x64），官方程序与 DLL 的 Authenticode 为 Valid，发布者为 Honor Device Co., Ltd.。直接调用仅允许下列本轮已还原版本：

| 文件 | SHA256 |
|---|---|
| `HnLcdEnhancement/MonitorManageTrans.dll` | `1C943E630B0DA977AED4307BCDFB5D7EA4ADDD4ADB22CFD5F5B18A5AC5810D43` |
| `PCManager/SenaryInterface.dll` | `2CEB760C8857BA2987625B44434233D718761BF3832B11C4529B64A934CA5357` |

组件升级产生不同 hash 时直接控制关闭并给出“该版本接口尚未验证”提示，保留签名有效的官方入口。调用通过 `LoadLibraryEx(absolutePath, ..., LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS)`；不搜索应用当前目录中的同名 OEM DLL，不将 DLL 纳入安装包。

## 5. 集成命令与验证状态

- `get_oem_features()`：显示支持／保存设置及 Senary 当前默认端点状态。
- `oem_set_display(feature, enabled)`：feature 为 `eyeProtection`、`ebook`、`comfortDisplay`、`defocusEye`、`naturalLight`。
- `oem_set_audio(feature, value)`：麦克风 `microphoneScene` + `bypass/multiplayer/single`；通话降噪 `callNoiseReduction` + `off/on`。
- `open_oem_page(page)`：`healthDisplay`、支持时 `colorManagement`、`smartAudio`（打开管家）。

所有参数通过 Rust allowlist 映射为常量，不把用户输入拼进脚本。调用有 12 秒超时，失败返回中文错误。正式查询已运行，三个格式化控制／入口脚本均通过 PowerShell AST 语法解析；本子任务尚未在 Tauri 窗口中执行显示 SET、音频 SET 或像素／声学验收。
