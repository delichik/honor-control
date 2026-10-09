# 荣耀电脑管家（Honor PC Manager）硬件控制机制逆向分析结论

分析日期：2026-09-22（第二轮补充：性能模式与健康显示）
分析对象：`C:\Program Files\Honor`（电脑管家全套组件）
分析方法：字符串提取 + PE 导出表解析 + Capstone 反汇编 + 活系统 WMI 实测（只读验证）
结论状态：
- **电池充电阈值**：GET 已实测；SET 由反汇编完整还原
- **性能模式（智能/高能）**：GET 已实测（4 条命令全部返回正常）；SET 命令字已还原（未实测）
- **健康显示**：调用链与两种 WMI 载荷格式已还原（未实测写入；查询邮箱 magic 已确认）

---

## 一、一句话结论

荣耀控制电池充电**不需要走它自己的内核驱动**，而是走 **ACPI-WMI 通道**：向 `root\wmi` 命名空间下 `OemWMIMethod` 类的 `OemWMIfun` 方法发送一个 64 字节报文，前几个字节是命令号和阈值参数，BIOS ACPI 方法（\SBTT/\GBTT）接收后写入 EC（嵌入式控制器），由 EC 固件执行"充到 X% 停止、低于 Y% 恢复"。**任何管理员权限的普通程序都可以直接调这个 WMI 方法实现同样功能**（电脑管家本身也是这么做的）。

## 二、完整调用链（自上而下）

```
PCManager 插件层
  SmartChargePlugin.dll          智能充电 UI 插件（弹窗、注册表开关）
  MBAMessageCenter.exe           消息中心，内含 Battery::SetChargeThreshold 调用点
        │
        ▼  C++ 类封装（导出函数）
HardwareSdk.dll                 Battery::GetChargeThreshold / SetChargeThreshold
  ├─ ??0Battery@@QEAA@XZ                 构造函数        (RVA 0x5530)
  ├─ ?Init@Battery@@QEAAXXZ              初始化          (RVA 0x5880)
  ├─ ?GetChargeThreshold@Battery@@...    读阈值          (RVA 0x5FB0)
  ├─ ?SetChargeThreshold@Battery@@...    写阈值          (RVA 0x6040)
  │     参数 StBatteryChangeThresholdLimit = 2 字节 {start, end}（均 0~100）
        │
        ▼  扁平 C API（HardwareSdk 从 HardwareHal 导入）
HardwareHal.dll
  ├─ HalInit(8, 0)                        (RVA 0x1E2D0)
  ├─ HalQueryInformation(8, 0, 9, out, 2, &len)   读  (RVA 0x1E770，跳表分发)
  ├─ HalSetInformation(8, 0, 0x0C, in, 2, &len)   写  (RVA 0x1E6C0，跳表分发)
  │   第 1 参数 8 = 设备类（电池/EC），第 3 参数 0x0C/9 = 功能号（阈值写/读）
  │   devClass=8 的 SET 真正实现位于 0x13F80，GET 位于 0x136D0
        │
        ▼  WMI COM 封装
HardwareHal.dll 内部 HalWmi 类
  （HalWmi::GetOutPutUIntEx，RVA 0x26CD0：互斥体 MutexWMI + IWbemServices::ExecMethod）
        │
        ▼
Windows WMI ACPI 提供程序（标准组件 acpi.sys/wmiacpi.sys）
  类：root\wmi : OemWMIMethod
  实例：InstanceName = "ACPI\PNP0C14\HWMI_0"（另有 HWMI_1，荣耀用 HWMI_0）
  WMI GUID：{ABBC0F5B-8EA1-11D1-A000-C90629100000}，描述 "Call BIOS Function through WMI"
  方法：OemWMIfun(u8Input uint8[], out u8Output uint8[], out u32Resrved uint32)
        │
        ▼
BIOS ACPI 方法（DSDT）：\SBTT 设阈值 / \GBTT 读阈值
        │
        ▼
EC 固件：真正切断/恢复充电电路
```

## 三、WMI 报文字节协议（核心成果）

`u8Input` 固定 **64 字节**（0x40，HardwareHal 中 `lea r8d,[rbx+0x40]`），其余字节填 0：

| 操作 | 命令字（LE） | 输入布局 | 说明 |
|---|---|---|---|
| 读阈值 | `0x1103` | `03 11 00 00 00...` | 对应 ACPI \GBTT |
| 写阈值 | `0x1003` | `03 10 [start] [end] 00...` | 对应 ACPI \SBTT |

`u8Output` 返回 256 字节：

| 偏移 | 含义 |
|---|---|
| 0 | 状态码，`0x00` = 成功 |
| 1 | start（开始恢复充电的百分比） |
| 2 | end（停止充电的百分比） |

命令号 `0x1003/0x1103` 与 Linux 内核主线驱动 `drivers/platform/x86/huawei-wmi.c` 中的 `BATTERY_THRESH_SET/BATTERY_THRESH_GET` 完全一致（华为/荣耀共用这套 BIOS 接口，只是旧款用 64 位整数参数，本机改成了字节数组）。

### 实测记录（2026-09-22，管理员权限，只读）

```
OemWMIfun(u8Input = {03 11 00 ...})
→ u8Output = 00 28 46 00 ...
             │  │  └─ end   = 0x46 = 70%
             │  └──── start = 0x28 = 40%
             └─────── 状态  = 成功
```

即本机当前"智能充电"处于开启状态：**充到 70% 停止，放到 40% 恢复**。

### 关键参数约定（与 Linux 驱动语义一致）

- 关闭充电限制（充满 100%）：`{03 10 00 64}`（start=0, end=100）
- 开启智能充电（70% 封顶）：`{03 10 28 46}`（start=40, end=70）
- 校验规则（HardwareHal 0x13F80 处）：start、end 均 ≤ 100；start ≤ end

## 四、权限与其他发现

1. **必须管理员权限**。非提权调用 `Get-CimInstance root/wmi:OemWMIMethod` 直接返回 0x80041003（拒绝访问）。提权后读取成功。
2. 电脑管家确实有一个内核驱动 **HNOs2ECx64**（`HNOs2EC10x64.sys`，KMDF 1.15，服务运行中，设备符号链接 `\\.\HNOs2EcX64`），但**电池阈值控制不走它**——走的是上面的 WMI 通道。该驱动主要供其他 EC 操作（字符串证据在 Util.dll、`sc.exe stop HNOs2EC` 等出现在 MBAMessageCenter.exe）。
3. 智能充电的用户设置持久化在注册表：
   - `HKLM\Software\PCManager\MBAPowerManager`（PowerSafeManagerMode / PowerSafeManagerStatus 等）
   - UI 层还有 SmartChargingSwitch、SmartChargeMode 等值（SmartChargePlugin.dll 字符串）。
   注意：注册表只是"开关状态"的记忆，真正生效靠每次开机后管家服务重新下发 WMI 命令——**直接改注册表不会改变充电行为**。
4. 电池状态监听（电量/是否充电）走 Windows 标准 `BatteryStatus`/`BatteryStaticData` WMI 类（BatteryStateMonitor.dll），与控制路径无关。
5. Linux 侧对照：`huawei-wmi` 内核驱动在同接口上暴露 `/sys/class/power_supply/BATT/charge_control_start_threshold`（及 end），AUR 的 huawei-wmi 包负责开机恢复设置——Windows 上若做独立工具，同样需要开机自动下发（否则重启后由电脑管家接管回注册表记忆值）。

## 五、最小复现方式（PowerShell，管理员）

读取当前阈值（已在 test-get.ps1 中验证通过）：

```powershell
$in = New-Object byte[] 64; $in[0]=0x03; $in[1]=0x11
$inst = Get-CimInstance -Namespace root/wmi -ClassName OemWMIMethod |
        Where-Object { $_.InstanceName -eq 'ACPI\PNP0C14\HWMI_0' }
$r = Invoke-CimMethod -InputObject $inst -MethodName OemWMIfun -Arguments @{ u8Input = $in }
$r.u8Output[0..2]   # 0=状态, 1=start%, 2=end%
```

设置阈值（例：40~70，即智能充电）：

```powershell
$in = New-Object byte[] 64; $in[0]=0x03; $in[1]=0x10; $in[2]=40; $in[3]=70
# 其余同上调用 OemWMIfun；返回 u8Output[0]==0 即成功
```

## 六、本次分析产物清单（honor/ 目录）

| 文件 | 说明 |
|---|---|
| conclusions.md | 本文档 |
| test-get.ps1 | 只读 GET 实测脚本（已运行成功） |
| get-result.txt | 实测原始输出 |
| huawei-wmi.c | Linux 内核参考驱动（命令号对照来源） |
| pe-exports.js / disasm.mjs / hexdump.mjs / resolve-slot.mjs | 分析工具（PE 导出表/反汇编/十六进制/IAT 解析） |
| dump-oemclass.ps1 / dump-guids.ps1 / enum-wmi.ps1 / dump-driver.ps1 / find-strings.ps1 | 字符串与 WMI 枚举脚本 |

## 七、主要证据索引

- `Util.dll`/`HardwareHal.dll` 字符串：`ACPI\PNP0C14\HWMI_0`、`OemWMIMethod`、`OemWMIfun`、`OemWMIfunEx`、`SELECT * FROM OemWMIEvent`、`ROOT\WMI`
- `MBAMessageCenter.exe` 导出符号：`Battery::SetChargeThreshold(StBatteryChangeThresholdLimit)`
- WMI-Activity 事件日志：`ExecMethod - ROOT\WMI : OemWMIMethod.InstanceName='ACPI\PNP0C14\HWMI_0'::OemWMIfun`
- HardwareSdk.dll IAT 名字表（RVA 0x3AF0E/0x3AF18/0x3AFD4）：`HalInit` / `HalQueryInformation` / `HalSetInformation`
- HardwareHal.dll 0x180014058：`mov word ptr [rsp+0x30], 0x1003` + `{start,end}` 拼包 + `GetOutPutUIntEx(in, 0x40, out, 0x100)`
- Linux 内核 huawei-wmi.c：`BATTERY_THRESH_GET = 0x00001103 (\GBTT)`、`BATTERY_THRESH_SET = 0x00001003 (\SBTT)`

---

# 第二轮：性能模式切换（智能/高能）机制

## 一、一句话结论

"智能/高能"切换是**三件事的组合**：① 向同一个 `OemWMIfun` WMI 通道发性能模式命令（SET `0x0C07`，GET `0x0802` 等，命令表见下）；② 切换 Windows 电源计划/overlay（`HonorPowerPlan::EnterHighPerfMode`、`PowerRegisterForEffectivePowerModeNotifications`）；③ 风扇曲线控制（部分机型经 `\.\WDT0001` 设备 + NLD 风扇库）。BIOS 收到命令后由 EC/固件调整功耗墙（PL1/PL2）与风扇策略。内部把高能模式叫 **HUNTER_MODE**（HunterCamp 服务目录即由此而来）。

## 二、调用链

```
HnPerformanceCenter.exe / 性能中心 UI（Fn+P 弹窗、PerfModeTask）
        │
        ▼
plugins\PerfCommonPlugin.dll          TurboMode 类（核心）
  ├─ TurboMode::SetTurboMode(int)          设置性能模式
  ├─ TurboMode::CheckTurboModeStatus()     查询当前模式状态
  ├─ TurboMode::WmiGetSupportMode()        查询机型支持的模式位掩码
  ├─ TurboMode::WmiGetAcOut()              查询适配器输出功率
  ├─ TurboMode::SetFanSpeed(u8, u8)        风扇转速
  ├─ HonorPowerPlan::EnterHighPerfMode()   Windows 电源计划切换
  └─ PowerSchemeFunction::InitPowerModeNotify()   电源模式变化通知
        │
        ▼  Util.dll 导出的 WMI 包装类
  WMI::GetOutPutUIntEx(u8* in, u32 inLen, u8* out, u32 outLen)
  WMI::GetOutPutUInt(u64 cmd, u8* out, u32 outLen)     ← 64 位命令版
        │
        ▼  （与电池充电完全相同的通道）
  root\wmi : OemWMIMethod.OemWMIfun('ACPI\PNP0C14\HWMI_0') → BIOS ACPI → EC
```

## 三、WMI 命令表（PerfCommonPlugin.dll 内全部调用点逆向所得）

所有命令同样发 64 字节 u8Input（剩余补 0），命令字为**前 2 字节小端**：

| 命令字 | 字节 | 语义 | 载荷/返回 | 依据（调用点 RVA） |
|---|---|---|---|---|
| `0x0C07` | `07 0C` | **设置性能模式** | 输入：第 3 字节 = 模式值；代码校验 `(mode-1) <= 2`，即合法模式 **1/2/3** | 0x29d71（`mov word[rsp+0x100],0xC07` + `mov byte[rsp+0x102], mode-1`） |
| `0x0802` | `02 08` | **查询当前模式状态** | 返回 `out[3] \| out[4]<<8` 为 16 位值 | 0x2a51f |
| `0x3C06` | `06 3C` | 查询支持的模式位掩码 | 返回 `out[1]`（HUNTER_MODE 支持位） | 0x349c4 |
| `0x2606` | `06 26` | 查询（TurboMode 关联状态） | 返回 `out[1]` | 0x34c12 |
| `0x0E04` | `04 0E` | 查询（PerfModeTask 上下文，PC 恢复事件） | 返回 `out[0]`=状态 | 0x353d0 / 0x361ae |
| `0x1004` | `04 10` | 查询/设置（模式保持相关） | — | 0x3b3f0 |
| `0x0902` | `02 09` | **适配器输出功率**（WmiGetAcOut） | 返回 `out[2] \| out[3]<<8`，单位 μW/mW 级 | 0x3b6a0 包装 + 0x3b13b |
| `0x10902` / `0x110902` / `0x100902` | — | 多口适配器功率（与 0x902 组合相乘换算） | 同上 | 0x3b15b/0x3b167 |

另有 2 个调用点（0x35a74、0x3477b）命令字在寄存器中动态构造，未能静态确定。

### 模式值语义（部分推断）

- 代码只允许模式 1/2/3（`(mode-1)∈{0,1,2}`），对应 UI 的 智能平衡/高能/（第三档，疑似电竞或超能，HUNTER）；
- 切换前置条件（PerfCommonPlugin 字符串证据）：必须 AC 供电且电池 ≥ 20%、适配器功率达标（`CheckChargWatt`），否则回退智能模式（"Reset support mode keep"）；
- 高能模式内部代号 HUNTER_MODE；切换同时联动 dGPU 模式（`OnReceiveGpuModeProcessReStart`、`DualGPUFourModes`、GPU 模式切换提示窗 HnPerfPowerNexus.exe）。

### 实测记录（2026-09-22，管理员权限，只读）

```
0x0802 模式状态: out = 00 90 08 00...   → 状态成功，out[1]=0x90, out[2]=0x08
0x3C06 支持掩码: out = 00 00 00 00...   → 状态成功，掩码 0
0x2606:          out = 00 1E 00 00...   → 状态成功，值 30
0x0902 适配器:   out = 00 00 20 4E 00.. → 0x4E20 = 20000 ≈ 20W（当前输入功率）
```

## 四、Windows 侧与持久化

- 电源计划：`HonorPowerPlan::EnterHighPerfMode`（PowerPolicyPlugin.dll）调 Windows Power API 切换 scheme/overlay；`PowerRegisterForEffectivePowerModeNotifications` 监听系统电源模式联动。PowerPolicyPlugin 内还引用一组电源设置子组 GUID（电源按钮 4f971e89-…、PCIe 7648efa3-… 等）用于注册通知。
- 功耗墙：`VstInterface::WmiGetGVNT / WmiSetWVST`（PowerPolicyPlugin）——同样经 OemWMIfun 的 PL 值读写。
- 风扇：`\.\WDT0001` 设备（Honor-WDT 驱动，DeviceStore 里 `Honor-WDT-1.0.24.39`）；NLD 风扇库（`NLDFanSettings.dat`、`NLD AutoModeON`）；注册表 `HKLM\Software\HONOR\PCManager\Performance\IntelligentFanControl`（SmartFanMode/SubSmartFanMode 值）。
- 模式记忆：`HKLM\Software\HONOR\PCManager\Performance`、`...\PerformanceModeSwitch`、`config\ModeKeepConfig.dat`、`config\system\HSPP_SceneStrategy.dat`（场景引擎策略）。开机/插拔电源时由管家服务按记忆值重新下发 WMI 命令。

---

# 第三轮：健康显示（护眼/色温/电子书/舒适显示等）机制

## 一、一句话结论

健康显示功能由 **HnSmartDisplay.dll（UI 插件）→ IPC → HnLcdEnhancement 组件（LCD_Service.exe / MonitorColor.exe / BasicDisplayAdapter.dll）** 实现。真正生效有**两条路径**：硬件路径经 `HalSetInformation(devClass=0x13, …)` 走同一条 OemWMIfun WMI 通道直达面板/EC；软件路径在面板不支持硬件调节（`IsSupportRegulateGamma()==false`）时，用 **256×3×u16 Gamma Ramp 表（SetDeviceGammaRamp 类 API）** 在显卡层做颜色/色温/黑白变换。能力与状态查询使用 **0x1020 字节的"邮箱"报文**（4 字节 magic + 1 字节子功能）。

## 二、功能面（BasicDisplayAdapter.dll 导出 = 健康显示完整 API）

| 导出函数 | 参数结构 | 对应 UI 功能 |
|---|---|---|
| `SetEyeProtection(TemperatureInfo*)` | 见下 | 护眼模式 |
| `SetTemperature(TemperatureInfo*)` | 同上 | 色温调节 |
| `SetColorAdjust(TemperatureInfo*)` | 同上 | 色彩微调 |
| `SetComfortDisplay(ComfortDisplayInfo*, int)` | — | 舒适显示 |
| `SetDefocusEye(DefocusEyeInfo*, int)` | — | 离焦护眼 |
| `OpenOffEBook(EBookInfo*)` | — | 电子书模式（黑白） |
| `SetColorMode(bool)` / `SetColorEnhance(bool)` | — | 色彩模式/色彩增强 |
| `SetColorSpace(ColorModeInfo*)` / `SetColorSpaceCSC(...)` | — | 色域切换（sRGB/P3，硬件 CSC/ICC，mscms.dll） |
| `SetLCDDefaultMode()` / `RestoreColorMatrix()` | — | 恢复默认 |
| `IsSupportRegulateGamma()` | — | 硬件调节能力探测 |
| `CheckAndLoadColorFile/GetEDIDCoorDinate/SetMonitorICC` | — | EDID/ICC 色彩管理 |
| `OnStartMonitorColor(const char*)` | — | 外接屏色彩（MonitorColor.exe） |

`TemperatureInfo` 布局（由 0xd022 helper 反汇编还原）：
```
+0x00  u32 mode        // mode==1 → 硬件路径；否则软件 Gamma 路径
+0x10  u32 val1        \ 硬件路径载荷（func5 的 8 字节）
+0x14  u32 val2        /
+0x18  double value    // 软件路径的强度/色温参数
```

## 三、WMI 硬件路径载荷格式

### SET（经 HalSetInformation，devClass=0x13，最终同 OemWMIfun）

| func | 载荷 | 用途 | 调用点 |
|---|---|---|---|
| 5 | 8 字节 = `{u32 val1, u32 val2}` | 颜色/护眼参数下发（TemperatureInfo mode==1） | BasicDisplayAdapter 0x5e380←0xd022 |
| 6 | 20 字节 = **5 个 float**（r8d=0x14；疑似 R/G/B 增益 + 亮度 + gamma） | 色彩增益矩阵下发 | BasicDisplayAdapter 0x5d83f 区（`movss [rbp+0x88..0x98]`） |

### GET（大缓冲"邮箱"协议，直接构造后调 GetOutPutUIntEx）

```
u8Input（0x1020 字节）:
  +0x00  u32 magic = 0xBBAA2306     ← 显示控制命名空间
  +0x04  u8  subfunc（入参校验 ≤ 0x7f）
  +0x05  ...  数据区（0x101B 字节，先清零）
u8Output: 0x1020 字节，out[0]=状态
```
- 调用点：BasicDisplayAdapter 0x1fd20（`mov dword[rsp+0xB0],0xBBAA2306; mov byte[rsp+0xB4],dl; memset 0x101B`），inLen=r8d=0x1020。
- 另一个查询函数（0x31700）使用**不同 magic `0x88DB322C`** 的邮箱（用途未深挖，疑似 EDID/工厂色彩数据）。
- ⚠️ 邮箱子功能号（subfunc）语义未逆向，**不要盲目写该邮箱**；SET 也仅还原了封装，未实测。

### 软件 Gamma 路径（面板不支持硬件调节时）

BasicDisplayAdapter 内有典型 Gamma 表构建循环：对每个通道生成 **256 级 × u16**（`mulss` 缩放 + `mov [rcx], ax` 循环到 0x10000），随后按通道经函数指针下发（SetDeviceGammaRamp 风格，作用于 `\.\DISPLAY1`）。电子书模式的黑白化、护眼滤蓝光在旧面板上都走这条路。

## 四、UI→服务的完整链路与持久化

```
PCManager\plugins\HnSmartDisplay.dll（纯 UI，无任何硬件/WMI 导入）
   │  IPC（IPCMessage.dll 命名管道 \.\Pipe\HonorMateBookAssistant 等）
   ▼
HnLcdEnhancement\LCD_Service.exe（服务端） / MonitorColor.exe（外接屏） / HnDisplayEngine.exe
   │
   ▼
HnLcdEnhancement\BasicDisplayAdapter.dll（全部 Set* 实现）
   ├─ HalSetInformation(0x13, …)  → HardwareHal.dll → OemWMIfun WMI → BIOS/面板   （硬件路径）
   ├─ Gamma Ramp 256×3 u16        → 显卡 LUT                                        （软件路径）
   ├─ mscms.dll（WCS/ICC 色彩管理，SetMonitorICC/SetColorSpace）
   └─ dxgi.dll（枚举显示器/EDID）
```

- 开关持久化：`HKLM\SOFTWARE\HONOR\PCManager\LCDEnhancement`（HnSmartDisplay 字符串证据）；护眼等状态开机由 LCD_Service 恢复。
- 亮度（非色彩）走 Windows 标准 `WmiMonitorBrightnessMethods.WmiSetBrightness`（LCDEnhancementPlugin.dll / APLBrightnessPlugin.dll）；自动亮度监听 `WmiMonitorBrightnessEvent`。
- HnDisplayEngine.exe 引 d3d11.dll + dwmapi.dll（全屏/独占场景检测，用于游戏中自动暂停色彩干预）。
- UI 能力探测字段（决定显示哪些开关）：IsSupportEyeProtect / IsSupportEbookMode / IsSupportComfortDisplay / IsSupportColorTemperatureAdjust / IsSupportColorManagement / IsSupportDefocusEyePrtect / IsSupportDefocusEyePrtect（自然护眼 NaturalEyeProtection、离焦 DefocusEyeProtect）。

## 五、与充电/性能通道的关系

三者共用**同一条 WMI 通道**（`root\wmi OemWMIMethod.OemWMIfun('ACPI\PNP0C14\HWMI_0')`），差别只在报文内容：
- 电池阈值：64 字节短命令（0x1003/0x1103 …）
- 性能模式：64 字节短命令（0x0C07/0x0802 …）
- 显示控制：`HalSetInformation(devClass=0x13)` 打包的命令 + 0x1020 字节 magic 邮箱（0xBBAA2306 / 0x88DB322C）

也就是说，BIOS 的 HWMI ACPI 方法是一个**多路复用网关**：短命令字按 16 位分发，邮箱按 magic 分发，最终都落到 EC/面板/电源固件执行。

---

## 附：产物清单更新与证据索引（第二/三轮）

### 新增分析产物

| 文件 | 说明 |
|---|---|
| test-perf-get.ps1 / perf-get-result.txt | 性能模式 4 条 GET 命令实测脚本与结果 |
| find-strings2.ps1 | 性能/显示关键词字符串提取 |
| imports.mjs | PE 导入表解析 + IAT 调用点交叉引用 |
| xref.mjs / ctx.mjs / ctx-strings.mjs / callers.mjs / scan-cmds.mjs | 反汇编上下文/调用图/命令字提取工具 |
| pe-exports.js / disasm.mjs / hexdump.mjs / resolve-slot.mjs | （第一轮已有）导出表/反汇编/十六进制/IAT |

### 关键证据（RVA 均已在文件内验证）

- PerfCommonPlugin.dll 0x29d0b：`mov word ptr [rsp+0x100], 0xC07`；0x29d24：`mov byte [rsp+0x102], mode-1`（模式 SET）
- PerfCommonPlugin.dll 0x2a4b8：`mov word [rsp+0xC0], 0x802`；0x2a53e：`out[3]|out[4]<<8`（模式 GET）
- PerfCommonPlugin.dll 0x3b136：`mov edx, 0x902` / 0x3b154-0x3b167：`0x100902/0x10902/0x110902`（适配器功率）
- 字符串：`isTurboModeAvailable`、`HUNTER_MODE`、`EnterHighPerfMode`、`CheckChargWatt`、`\.\WDT0001`
- BasicDisplayAdapter.dll 0x5e8a9-0x5e8b2：`ecx=0x13; r8d=5; len=8`（func5 SET）
- BasicDisplayAdapter.dll 0x5e89a：`len=0x14`（5 floats，func6 SET，0x5e848-0x5e876 组包）
- BasicDisplayAdapter.dll 0x1fd5a：`mov dword [rsp+0xB0], 0xBBAA2306` + 0x1fd65 `mov byte [rsp+0xB4], dl` + `r8d=0x1020`（显示查询邮箱）
- BasicDisplayAdapter.dll 0x31700 区：`mov dword [rsp+0xC0], 0x88DB322C`（第二邮箱 magic）
- BasicDisplayAdapter.dll 0x5f2c1-0x5f328：256×u16 Gamma 表构建循环（软件路径）
- 导出面：SetEyeProtection/SetTemperature/SetComfortDisplay/SetDefocusEye/OpenOffEBook/…（完整功能 API）
- HardwareHal.dll 0x1e6dc：`cmp ecx,0xb` 分支 → 0x18000d9e0（devClass 0x13 分发）

### 实测输出（perf-get-result.txt）

```
0x0802: 00 90 08 00 ...   0x3C06: 00 00 00 00 ...
0x2606: 00 1E 00 00 ...   0x0902: 00 00 20 4E 00 ...（0x4E20=20000≈20W）
```

---

# 第四轮：智能充电运行期行为实测（用户报告"插电掉电快"的诊断）

## 结论：机制没有缺失，"掉电"是 drain-to-threshold 设计行为

1. **EC 配置未变**：`03 11` 读回始终为 `00 28 46`（start=40%, end=70%），管家注册表 `PowerSafeManagerStatus=1`（智能充电开）。
2. **实测电池行为**（2026-09-23 16:54，电池 73.7%，高于截止阈值 70%）：
```
[16:54:04] 67846mWh (73.713%) Charging=True(18754mW) Discharging=False AC=True
[16:55:22] 67455mWh (73.288%) Charging=True(17562mW) Discharging=False AC=True
→ 78 秒净减 391mWh ≈ 5W 持续净放电（插着电！）
```
3. **解释**：智能充电（end=70）生效时，EC **拒绝给高于 70% 的电池充电**，让系统负载把电池"放到" 70% 后保持。这个阶段插着电电量也会下降——是设计行为，不是故障。到 70% 后进入保持区：只在低于 start（40%）时才重新充电至 70%，期间系统完全走 AC。
4. **表计陷阱**：`BatteryStatus`（root\wmi）此时报 `Charging=True, ChargeRate≈+15~19W`，但净容量在减少——该标志位反映充电 IC 侧电流（含系统取电路径），**判断充放方向必须看 `RemainingCapacity` 的净趋势**（或 WinRT BatteryReport 的 ChargeRateInMilliwatts 符号）。
5. **想立刻充满**：用管家里的"充满一次/继续充电"，或直接发 SET `{03 10 00 64}`（start=0,end=100）；恢复智能充电发 `{03 10 28 46}`。充满后管家会按注册表记忆值恢复 40/70。

## 修正：0x0902 不是"适配器功率"，是适配器电压

两次相隔一天的 `02 09` 读数完全相同（`00 00 20 4E` = 0x4E20 = 20000），且与负载无关 → 不是实时功率。结合 `TurboMode::WmiGetAcOut`（PerfCommonPlugin 0x3b100）的反汇编：
```
ecx = WmiGetU64(0x0902)            // 电压，单位 mV（20000 mV = 20V，USB-C PD 电压轨）
edi = WmiGetU64(0x10902 / 0x110902) // 电流
ecx *= edi;  ≈ /1e6                // V(mV) × I(μA/mA) / 1e6 → W
```
即 **0x0902 = 适配器电压(mV)，0x10902/0x110902 系 = 电流，相乘除 1e6 得瓦数**（CheckChargWatt 用它判断适配器是否够高能模式用）。第二轮命令表中 0x0902 的"μW 功率"描述作此更正。

## 诊断脚本与数据

| 文件 | 说明 |
|---|---|
| test-battery-diag.ps1 / battery-diag.txt | 首次诊断：EC 阈值 + BatteryStatus 双采样 + 服务进程 |
| test-battery-diag2.ps1 / battery-diag2.txt | 注册表状态 + WinRT 尝试 + HWMI 复查 |
| test-battery-diag3.ps1 / battery-diag3.txt | **精确连续采样（RemainingCapacity 净趋势，本次诊断的关键证据）** |

新读到的持久化字段：`HKLM\Software\PCManager\MBAPowerManager` → `PowerSafeManagerStatus=1`（智能充电开启）、`PowerSafeManagerMode=0`。

---

# 第五轮：高能模式参数实测（用户配合三步切换实验）

实验方式：用户在管家 UI 手动切换 智能→高能→智能，每步采一次全字段快照（perf-snapshot.ps1 → perf-snapshots.log）。

## 三步快照对比（本机实测，2026-09-23 17:19~17:20）

| 字段 | ①智能 | ②高能 | ③切回智能 | 结论 |
|---|---|---|---|---|
| **q0E04 out[1]** | `00` | **`01`** | `00` | ⭐ **性能模式状态位：0=智能，1=高能**（0x0E04 是模式读回命令） |
| **Windows 电源计划** | 381b4222-…（平衡） | **b8a2c9f4-7d3e-4a1b-9c2f-5e8d6a3b1c4f（"Honor Performance"）** | 381b4222（平衡） | 高能=切到管家自建电源方案；智能=回系统平衡 |
| mode0802 out[1..2] | 0x0831(2097) | 0x0B16(2838) | 0x0834(2100) | 动态遥测（风扇转速 RPM 或封装功率），随模式/负载变化，**不是模式字段** |
| q1004 / q1007 / qC02 / qC06 / q1206 / q1306 / q3406 / q3F06 | EE/01/70/80/15/02/01/01 | 同左 | 同左 | **静态机型参数表（温控/风扇阈值），与模式无关** |

本机静态参数表：`q1004=0xEE(238)  q1007=1  qC02=70  qC06=80  q1206=15  q1306=2  q3406=1  q3F06=1`（疑似：70/80=温度目标/触发点、15=风扇步进或迟滞、238=某上限）。

## 关键结论

1. **模式读回**：`{04 0E}`（0x0E04）→ `out[1]`：`0`=智能（平衡），`1`=高能。本机 UI 只有两档。
2. **支持掩码**：`{06 3C}`（0x3C06）读回恒为 0 → 机型不支持 HUNTER（第三档/电竞），与"UI 无第三档"完全吻合——**该掩码可用于探测机型支持哪些模式**。
3. **模式设置**（推断，未实测写）：`{07 0C <mode>}`（0x0C07），代码允许 mode∈{1,2,3}；结合本机两档 UI，推测 `1=智能, 2=高能`（3 为其他机型的 HUNTER 档）。如需程序化切换：SET 0x0C07 → GET 0x0E04 验证 out[1] → `powercfg /setactive` 同步 Windows 方案（智能=381b4222-f694-41f0-9685-ff5bb260df2e，高能=b8a2c9f4-7d3e-4a1b-9c2f-5e8d6a3b1c4f）。
4. **0x0802 重新解读**：out[3..4]（CheckTurboModeStatus 解析的字段）在本机恒为 0；out[1..2] 是持续变化的遥测量（1776~2838，量级与风扇 RPM 吻合），此前"查询当前模式"的描述不准确，模式读回应以 0x0E04 为准。
5. **第二轮命令表补充/更正**（PowerPolicyPlugin 静态逆向 + 本轮实测）：
   - `0x0E04`：模式状态读回（实测）
   - `0x1007`：PowerPolicyPlugin GET（恒 01）
   - `0x0C02` / `0x0C06` / `0x1206` / `0x1306` / `0x1B06` / `0x2506` / `0x3406` / `0x3F06`：PowerPolicyPlugin 读取的静态参数/温控表 GET（实测恒定）
   - `0x0902`=电压(mV)（第四轮已更正）
6. **PowerPolicyPlugin 无任何 WMI SET**（只导入 GetOutPutUInt/GetOutPutUIntEx）——功耗墙 GVNT/WVST 的写操作在其他组件（待查：HnPerformanceCenter.exe / OS2SOCService.dll / huntercamp），或经 0x0C07 一并生效。

## 实验产物

| 文件 | 说明 |
|---|---|
| perf-snapshot.ps1 | 单次快照脚本（带 LABEL 参数，追加写入 perf-snapshots.log） |
| perf-snapshots.log | 三步实验的原始快照（step1-current / step2-highperf / step3-back-smart） |
| perf-mode-trace.ps1 / perf-mode-trace.txt | 6 分钟连续采样（基线稳定性分析：区分动态遥测 vs 静态参数） |
| analyze-trace.js | 采样数据 run-length 分析工具 |

## 2026-10-09：传感器与适配器通道只读复查

这次在本机通过已运行的 HonorControl 服务命名管道读取了实时遥测；另用管理员 PowerShell 对 `OemWMIfun` 做了只读调用，没有发送任何写命令。

- 服务实际返回：电池温度 `null`、适配器功率 `null`、风扇数组为空；温度传感器返回 `YMTC PC41Q-1TB-B` 44 °C，Warning 82 °C、Critical 84 °C。此前设置页数据源表把这条传感器标为“服务”，却在说明里写“未返回”，现已修正为显示实际标签和值。
- `OemWMIfun(02 09)` 返回 `00 00 20 4E`，即 20,000 mV。扩展输入 `02 09 10`（命令 `0x100902`）返回 `00 00 88 13`，即数值 5,000；`0x10902` 和 `0x110902` 的相应短命令输入返回 0。这个 5,000 的单位及其代表实时电流还是适配器能力尚未确认，所以不能将其相乘后冒充实时瓦数。
- `OemWMIfun(02 08)` 返回 `00 8A 08 00`；`out[1..2]` 连续六次在 2,184–2,187 间变化，数值像 RPM，但仍可能是处理器功耗遥测。尚未与独立转速表读数交叉验证，故没有填入风扇 RPM。
- 提权只读查询还发现 `MSAcpi_ThermalZoneTemperature` 有一个 `TZ00` 实例，`CurrentTemperature=3010`（27.85 °C）；`BatteryTemperature`、`Win32_Fan`、`Win32_TemperatureProbe` 均无实例。服务新增通用“ACPI 热区 TZ00”读数；它不是电池温度，也不推断为 CPU 温度。
- `OemWMIfunEx` 对上述短命令和 64 位命令尝试均返回“无效的参数”。

因此原生面板对未确认的温度、风扇 RPM 与适配器功率保持未知；当前已确认的 NVMe 温度会在温度传感器行展示。
