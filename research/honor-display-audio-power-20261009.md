# 智慧显示、智慧音频与系统电源管理研究

日期：2026-10-09。对象：本机 HONOR BCC-N 安装的荣耀电脑管家及 LCD 组件。

本次采用 PE 导入／导出解析、按 `.pdata` 定位函数的反汇编、函数内日志交叉确认，以及注册表、`powercfg`、`pnputil` 的只读查询。没有加载调用荣耀 DLL，没有发送控制消息、硬件 SET，也没有修改显示、音频或电源配置。

文件哈希与显示能力快照：[证据清单](evidence/honor-display-audio-power-20261009.json)。本页 RVA 只适用于该清单中的 DLL；不能直接套用到其他版本。

## 结论与实现难度

| 功能 | 真正执行效果的层 | 独立实现判断 |
|---|---|---|
| 护眼、色温、电子书 | LCD 服务 → 显示适配层 → GPU 矩阵／LUT、GDI Gamma 或机型专用 HAL | 软件近似效果可实现；完整官方效果要处理显卡、HDR、校准及能力分支 |
| 舒适显示、离焦视力舒缓等 | LCD 适配层中的独立状态与回调逻辑 | 不是一个统一 WMI 开关；结构体与状态参数尚未完整还原 |
| 色彩管理 | BIOS 校色资源 + GPU 色彩管线 + Windows ICC 关联 | 比简单护眼复杂，不能只安装一个 ICC 文件就视为等效 |
| 智慧音频 | 管家后台场景插件 → 声卡／音效厂商接口 → 已安装效果模块 | 控制现成效果可研究接入；独立重现降噪、声纹、空间音效算法不属于普通设置 API |
| 系统电源管理的节能优化 | Windows 电源方案与具体电源设置 | 最简单，适合用标准 Windows API 独立实现 |
| 系统电源管理中的智能／高能档位 | UI → 管家 IPC → 性能后台与固件 | 是另一处入口，仍需荣耀模式协议，不能靠节能设置替代 |

## 1. 健康显示

### 界面与执行端

```text
PCManager/plugins/HnSmartDisplay.dll
    └─ IPCMessage.dll（控制／打开页面消息）
        └─ HnLcdEnhancement 的 LCD 服务与管理界面
            └─ LCDServiceTrans.dll
                └─ BasicDisplayAdapter.dll
                    ├─ Intel IGCL 的矩阵、1D LUT、3D LUT
                    ├─ AMD ADL 等显卡分支
                    ├─ gdi32!GetDeviceGammaRamp / SetDeviceGammaRamp
                    └─ HardwareHal!HalSetInformation（机型专用分支）
```

`LCDServiceTrans.dll` 直接导入 `SetEyeProtection`、`SetTemperature`、`OpenOffEBook`、`SetComfortDisplay`、`SetDefocusEye`、`SetColorSpace`、`SetMonitorICC` 等执行方法；`HnSmartDisplay.dll` 则包含 IPC 帮助类和打开 LCD 组件的逻辑。不是由管家主页自身处理显示像素。

### 各项如何实现

| 项目 | 已定位的机制 | 证据 |
|---|---|---|
| 护眼模式／色温 | 调整色彩通道增益、矩阵或 Gamma／LUT；还存在 HAL 分支 | `BasicDisplay::SetEyeProtection` RVA `0xCFD0`；`SetTemperature` RVA `0xD1F0` |
| 电子书模式 | 彩转灰与色温处理；Intel 分支使用像素变换／3D LUT | `OpenOffEBook` RVA `0xCC20` 调 `0x5B360`；后者解析 `ctlInit`、枚举显卡与输出；`OperateColor::SetEBook3DLut` RVA `0x5BC50` |
| 舒适显示 | 单独维护开关状态，调用 Gamma／色彩处理方法 | `SetComfortDisplay` RVA `0xD680` → `SetComfortDisplayState` RVA `0x3DB70`；开启分支调用 `0x4A0B0`、`0x4A6F0` |
| 离焦视力舒缓 | 独立的多状态及回调处理，不能简化成普通色温开关 | `SetDefocusEye` RVA `0xD5C0` → `SetDefocusEyeState` RVA `0x42450`，日志区分状态 0／1／2；存在 `OnProbeCallback` 路径 |
| 动态亮度／类自然光相关能力 | 有独立亮度调整入口和设备能力标记 | 导出 `OpenAdjustBrightness` RVA `0xD0C0`、`CloseAdjustBrightness` RVA `0xD0E0`；完整自然光调制参数本轮未还原 |

具体护眼分支已核对：`SetEyeProtection` 的输入结构首字段为 1 时，传入 `+0x10/+0x14` 两个值调用 RVA `0x5E380`；该函数调用 `HalSetInformation`，设备类 `0x13`、功能号 5、载荷 8 字节。其他模式字段走不同的软件／显卡处理函数。不能认为所有机型都使用同一条 HAL 命令。

GDI 路径也已直接确认：RVA `0x43350` 从系统目录加载 `gdi32.dll`，动态获取 `GetDeviceGammaRamp` 与 `SetDeviceGammaRamp`。但**电子书彩转灰不能仅靠三个彼此独立的 1D Gamma 通道完成**，本次还定位到了矩阵／3D LUT 路径；旧报告的“Gamma 路径足以说明所有显示效果”应收紧。

官方 `res/layout/html/pages/ProtectEye.html` 将“软件护眼”“彩转灰”“硬件低蓝光”“高频 PWM”等明确列为不同技术。硬件低蓝光的发光材料／光谱和面板 PWM 不能由普通软件滤蓝光等效实现。本文不将页面宣传的健康效果当成实测结果。

### 本机支持情况与接入边界

只读 `HKLM\SOFTWARE\HONOR\PCManager\LCDEnhancement` 得到：

- `DisplayType=DISPLAY_TYPE_INTEL`。
- 电子书、护眼、色温、离焦、舒适显示以及 `IsSupportNatualLight` 均为 1。
- `IsSupportColorManagement=0`、`IsSupportScreenMaintenance=0`。
- `HdrAcmEnabled=0`。

这些是管家留下的能力记录，不是本轮逐项切换成功的证明。正式接入需要重新查询组件能力并做可恢复的开关测试。显示上下文、HDR、Windows 夜间模式、ICC 和显卡驱动可能共同作用；不能把所有 GPU／Gamma 操作机械地放入 Session 0 服务。

## 2. 色彩管理

它包含三个相互配合的部分：获取正确的校色数据、设置显示管线、关联色彩描述文件。

### 校色数据

- 官方说明页 `ColorManager.html` 写明逐屏校准、BIOS 保存校色文件及切换色域时联动 Windows 色彩描述文件。
- 二进制也有对应路径：`BasicDisplayAdapter.dll` RVA `0x25750` 所在函数记录 `failed get icc and 3Dlut file from BIOS`、`Loadlutfile failed`、`GetSetColorFileFromBIOS failed`。
- RVA `0x1FD20` 使用 WMI 邮箱 `0xBBAA2306`，输入／输出长度 `0x1020`，子功能字节位于偏移 4；另有校色资源读取逻辑。具体全部子功能与资源格式仍未还原，本轮未调用该邮箱。

因此，通用色域矩阵可以制作近似转换，但没有逐屏校色数据就不能声明复现官方出厂校准或色准指标。

### 显卡管线

- Intel 实现明确使用 IGCL：RVA `0x44700` 动态加载 `ControlLib`，获取 `ctlInit` 等函数；同时定位到 `ctlPixelTransformationGetConfig`／`SetConfig`、3×3 矩阵、1D LUT 与 3D LUT 功能。
- `Generate3DLUTFromMatrixAndGamma`、`Generate3DLUT` 的函数内日志支持其生成 LUT 的过程；`ColorSetInit::GetSet3DLUT` RVA `0x2B3C0`、`SetLocal3DLUT` RVA `0x2CEF0` 是对应执行路径。
- AMD 分支动态解析 `ADL_Display_Color_Set`、`ADL_Display_Gamut_Set`、能力查询等接口。
- `BasicDisplay::SetColorSpace` RVA `0xD790` 在设备分支后调用具体色彩执行函数；`SetColorSpaceCSC` 另有独立入口。

这比单独调整 Gamma 丰富得多。支持的显示输出、驱动版本、色域和 LUT 区块必须先枚举，不能向不支持的输出盲写。

### Windows ICC 联动

- 二进制中有 `HonorNative.icc`、`HonorsRGB.icc`、`HonorP3.icc` 等资源名称。
- `BasicDisplay::SetMonitorICC` RVA `0xDC10` 转入内部 ICC 管理逻辑。
- `ColorSetInit::SetExternalDisplayProfile` RVA `0x28100` 调 `mscms!InstallColorProfileW`（调用点 `0x282CA`），并调用 `mscms` 的序号 232；函数内日志将后者标为 `ColorProfileSetDisplayDefaultAssociation`。尚未把该序号当成跨 Windows 版本稳定的公开 ABI。

ICC 告诉支持色彩管理的软件如何解释显示器颜色，矩阵／LUT 则改变输出变换；二者职责不同。**只切 ICC 不一定改变所有窗口的实际像素，只有 GPU 变换而 ICC 不匹配也可能产生错误色彩转换。**

官方说明中的“13 种模式”是组件／产品说明，并不证明 BCC-N 全部支持。本机能力记录当前为色彩管理不支持，不能因为 DLL 有实现就直接开放全部模式。

## 3. 智慧音频

### 分层结构

```text
SmartAudioPlugin.dll（界面、选项、试听与 IPC）
    └─ 管家 IPC
        └─ AdjustSmartAudioScenes.dll（设备识别、场景与消息处理）
            ├─ Histen/SWS 接口
            ├─ AwinicAPOUser.dll
            ├─ Realtek 的 COM 音频算法接口
            ├─ SenaryInterface.dll
            └─ DTS / Nahimic 分支
                └─ 已安装音频效果模块与声卡驱动
```

安装配置 `PCManager/config/plugincfg.xml` 将 `SmartAudioPlugin` 标为 process 0，将 `AdjustSmartAudioScenes` 标为 process 2；这进一步支持界面与执行插件分开。具体 process 数字与宿主进程的完整映射本轮未验证。

### 具体功能与接口

| 功能 | 已定位接口 | 证据与限制 |
|---|---|---|
| 扬声器／耳机音效场景 | Histen 的 `SwsSetSpeakerMode`、`SwsSetHpMode`，以及对应 Get 方法 | `AdjustSmartAudioScenes.dll` RVA `0x7D90` 用 `GetProcAddress` 获取这些函数；模式枚举仍需逐项匹配 |
| Awinic 扬声器效果 | `AwSetSpeakerMode` | 后台插件动态解析；本机 `AwinicAPOUser.dll` 也确实导出该函数 |
| 麦克风场景／AI 降噪 | Realtek 分支 `SetMicScene`、`SetMicrophoneAINoise` | RVA `0x93E0`、`0xC680`；经初始化取得的 COM 接口调用，不是一个标准 Core Audio 降噪开关 |
| 通话对端降噪 | Realtek 或 Senary 的 `SetCallNoiseReductionEnable` | 消息处理 RVA `0x28360` 按可用实现分发到 `0xA1F0` 或 `0x15800` |
| Senary 麦克风效果 | `SetCaptureEffectMode`、`GetCaptureCurrentMode`、`GetCaptureAbilityByEndpoint` | 后台初始化 RVA `0x12D50` 从 `SenaryInterface.dll` 动态解析；设置 RVA `0x142C0` 按端点与能力执行 |
| Senary 播放效果 | `SetRenderStatus`、`GetRenderAbilityByEndpoint` 等 | 同一厂商接口；按播放端点管理 |
| 声纹／仅保留本人声音 | 界面有录入与授权状态；Senary 有 `SetVoiceID`、`QueryVoiceIDStatus` 等 | 尚未完整还原注册流程与数据格式，也未读取用户声纹内容 |
| 空间音效、试听 | DTS、Histen 等场景接口；UI 另有试听方法 | UI 支持分支依赖耳机／扬声器和厂商能力，不能把它等同于调系统音量 |

本机只读 PnP 枚举确认已连接 `Senary Audio`，并有 Nahimic 设备。其驱动 `oem61.inf` 标识 Senary；扩展 `oem84.inf` 包含 Nahimic `EFXModules` 和 `APOAlgoType` 配置。由此可确认这台设备存在音频效果／驱动集成路径，**不能据此证明某个智慧音频选项本轮已经生效**。

接入时，应先枚举实际声卡、播放／录音端点及能力，再调用对应厂商的控制接口并回读。普通音量、默认设备等可用 Windows API，但无法代替这些降噪／声纹／空间效果算法。只改保存场景的注册表值也不能证明处理模块已应用设置。

## 4. 系统优化中的电源管理

需要分开看“节能建议”与“智能／高能档位”；同一 DLL 中两种逻辑均已找到。

### 节能建议：主要是标准 Windows 电源设置

`hn_plugin_doctor.dll` 的 `HwaPageDetect::DetectPowerSetting`（RVA `0x13A8F0`）读取当前方案、DC 设置，以及管家智能充电开关。对应处理函数 RVA `0x13D4F0` 含下列明确操作：

| 项目 | 处理动作 | 证据 |
|---|---|---|
| 电源方案 | 切回 Windows 平衡方案 `381b4222-f694-41f0-9685-ff5bb260df2e` | `0x13D84D` 的 `powercfg -setactive ...` 字符串和后续 `ExecuteCommand` |
| 电池供电关屏时间 | 写 300 秒，即 5 分钟 | `0x13D8EA` 参数 `0x12C`；调用 `PowerWriteDCValueIndex`，成功后 `PowerSetActiveScheme` |
| 电池供电硬盘空闲时间 | 写 600 秒，即 10 分钟 | `0x13D9FD` 参数 `0x258`；同样写 DC 值并重新激活当前方案 |
| Intel 显卡电源策略 | Intel Graphics Power Plan 写索引 1（Balanced／平衡） | `0x13DB72`、`0x13DB89`；运行期 GUID 字符串的静态初始化指向 `44f3beca-…`／`3619c3f2-…`，`powercfg /qh` 核对名称与枚举值 |
| AMD 显卡电源策略 | `PowerPlanCfg::SetAMDPowerPlan`，写 DC 索引 0 | RVA `0x15A720`，写入点 `0x15A7B5`；对应 GUID 为 `f693fb01-…`／`191f65b5-…`，本机 Intel 平台无法验证 AMD 策略实际效果 |

所用 API 包括 `PowerGetActiveScheme`、`PowerReadDCValueIndex`、`PowerWriteDCValueIndex`、`PowerSetActiveScheme`。这是对已有 Windows 方案的修改，不是从中推断 CPU PL1/PL2 或风扇曲线。

二进制还包含 PCIe 链路状态和 USB 选择性暂停等 GUID，但这些字符串存在并不意味着上述处理函数在写它们。本页的 Intel／AMD 行已跟踪 GUID 字符串初始化与实际调用参数，避免将相邻常量当成执行证据。

另外，UI XML 中还保留睡眠、智能充电等设置项。部分布局为旧版／注释块，仅有控件定义不代表本机可见或当前分支执行；本轮只把上述函数内找到的动作列为确认路径。

**这部分确实更简单。** HonorControl 可以用标准 API 实现关屏、睡眠、已有电源方案选择等独立功能；实现时读取当前值、区分 AC/DC、保存可恢复的修改，并回读验证即可，不需要重现荣耀显示或音频算法。

### 智能／高能：仍然使用管家性能后台

`OSPowerManagerUINew::OnClick`（RVA `0x122E80`）处理 `optOSAIMode`、`optOShighEnergyMode`、`optOSHunterMode`，调用 RVA `0x12BD70`。

该函数构造模块 `0x2440000`、消息 `0x2440004`、1 字节载荷，再调用 `IPCMessageClient::PostIPCMessage`（`0x12BDD5`）。UI 的具体模式值存在机型分支，不能直接把所有 UI 值当成 BIOS 字节。

结合 [上一轮性能协议复查](honor-telemetry-20261009.md)，它是发给管家性能后台的模式请求，不是 UI 直接 `PowerSetActiveScheme` 就完成整套高能模式。普通性能固件 SET 为 `0x0F04`，回读为 `0x0E04`；模式写入仍未在本轮执行。

因此可分别提供“Windows 电源设置”和“荣耀性能模式”，但不能用关屏／硬盘节能或单独切计划，宣称已经完成荣耀固件高能切换。

## 5. 对项目的建议与验证边界

1. Windows 电源设置最适合先独立实现，范围与 API 都清楚。
2. 健康显示若希望完整保留官方效果，优先研究使用机器已安装的 LCD 组件；若自己实现，先限制为可回读、可恢复的软件色温／护眼，再按显卡能力扩展。
3. 色彩管理需先核实本机支持与校色资源，不能只搬运模式名称、矩阵或 ICC 文件。
4. 智慧音频优先对接已有驱动／效果模块的控制层，先验证本机 Senary 的只读能力／状态，再决定开放哪些选项；不把普通 Windows 声音设置当作等效降噪实现。

本次确认了静态控制路径与部分安装／能力状态，没有进行 UI 点击、显示截图对比、声音录制测试或电源写入测试。功能实现与切换验证属于后续待确认的工作；此前的性能修正、旧文档清理也尚未执行。

整理说明：本页保留研究时点的结论；后续实施状态见[研究汇总](conclusions.md)，OEM 控制限制见[接入研究](oem-controls-20261009.md)。
