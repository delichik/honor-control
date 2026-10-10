# Honor 电池温度、USB 输入与性能切换复查

日期：2026-10-09。设备：HONOR BCC-N。分析对象：本机 `C:\Program Files\Honor\PCManager`。

本次仅执行硬件 GET、读取已运行服务快照及 Windows 电源配置，没有发送 SET，也没有切换电源方案。下面的函数语义以本次正确地址的反汇编及函数内日志为准，覆盖 `conclusions.md` 中早期的性能命令推断。

## 1. 电池温度：本机已读到 34 °C

调用链：

`Util.dll!BiosWmi::GetBatteryNtcTemp` → `HardwareSdk.dll!HotInterface::GetSensorTemperature` → `OemWMIMethod.OemWMIfun`。

证据：

- `Util.dll` RVA `0x842B0`：普通机型传入传感器编号 `0x0E`；机型判断结果为 3 时使用编号 `0x01`。
- `HardwareSdk.dll` RVA `0x13CE0`：将编号左移 16 位，与 `0x0202` 组成 GET 命令；成功时从 `out[2]` 取温度，`out[1] == 1` 表示负数。
- `Util.dll!WMI::GetBatteryTemp` RVA `0x133B60`：直接构造 `0x0E0202`，并按上述符号与温度布局解析。
- `Util.dll!BiosWmi::GetBatteryMosTemp` RVA `0x84330`：使用编号 `0x1A`。这是电池 MOS 温度，不能替代电芯 NTC 温度。

管理员只读调用，64 字节输入，其余字节填零：

| 命令 | 输入前 3 字节 | 返回前 4 字节 | 本机结果 |
|---|---|---|---|
| 电池 NTC 默认编号 | `02 02 0E` | `00 00 22 00` | 34 °C，连续六次相同 |
| 电池 NTC 替代编号 | `02 02 01` | `01 00 00 00` | BIOS 拒绝 |
| 电池 MOS | `02 02 1A` | `01 00 00 00` | BIOS 拒绝 |

Windows 的 `root\wmi:BatteryTemperature` 仍无实例；不能由此断言荣耀私有通道也无电池温度。已运行的 HonorControl 服务尚未采集此 GET，所以仍输出 `BatteryTemperatureC=null`。

实现应优先采用有明确命名证据的电池 NTC 读数，验证状态、长度、符号和合理范围。错误或不支持时返回 null，不使用 ACPI 热区或 MOS 温度冒充电池温度。

## 2. USB 输入：电压和电流值已读到，实时功率语义仍有限制

`HardwareSdk.dll` 导出与对应命令：

| 函数 | RVA | 命令 | 本机六次结果 |
|---|---|---|---|
| `GetVoltageUSB0` | `0x13F00` | `0x000902` | 20,000 |
| `GetCurrentUSB0` | `0x13F40` | `0x100902` | 5,000 |
| `GetVoltageUSB1` | `0x13F10` | `0x001902` | BIOS 状态 `0xEE` |
| `GetCurrentUSB1` | `0x13F50` | `0x110902` | 0 |

`GetVoltageOrCurrent`（RVA `0x13F80`）成功时从 `out[2..3]` 按小端读取 16 位值，`out[1] == 1` 表示负数。

`PerfCommonPlugin.dll` RVA `0x3B100` 所在函数，经函数内日志确认是 `TurboMode::CheckChargWatt`，不是早期报告所称的 `WmiGetAcOut`：

1. 读取 `0x0902`。
2. 电压有效且非零时，读取 `0x100902`。
3. 第一个电压不可用或零时，改读 `0x10902` 与 `0x110902`。
4. 两值相乘，除以 1,000,000；该整数除法由乘数 `0x431BDE83` 和右移 18 位实现。

因此 20,000 × 5,000 / 1,000,000 = 100 W；按电压 mV、电流 mA 可解释为 20 V、5 A。官方用于充电器瓦数检查，但没有证明电流值是负载随时间变化的实测值。六次短时采样均固定，也没有独立仪表或负载对照。

建议作为 USB 供电诊断／能力信息接入，注明电流和功率口径待进一步确认。不要把 100 W 写入现有的实时 `AdapterPowerW` 或据此生成负载和耗电历史。也不能仅凭它断言适配器铭牌额定功率。

## 3. 性能切换：当前实现使用了错误的写命令和能力字段

本次按 PE `.pdata` 定位完整函数，用函数内日志与源码文件名验证语义，而不是仅凭附近字符串推断。

| 命令 | 官方函数与证据 | 应有含义 |
|---|---|---|
| `0x0F04` | `TurboMode::SetTurboMode(int)`，函数 RVA `0x35810`；`0x35A39` 写命令，`0x35A43` 写输入参数，`0x35A74` 调 WMI | 性能模式 SET，第三字节为状态参数 |
| `0x0E04` | RVA `0x353A0` / `0x36180` 构造 GET；后者取 `out[1]` | 性能模式 GET；本机返回智能状态 0 |
| `0x0C07` | `NoiseEventDealer::SetNoiseMode`（函数 `0x29C30`）；`TurboMode::SetNoiseMode`（函数 `0x35BF0`）；日志为 `Set noise mode success` | 噪声／风扇模式，载荷为 `mode - 1`，不能当成智能／高能 SET |
| `0x3C06` | 函数 `0x34900`，错误日志明确标记 `TurboMode::CheckACConnectStatus` | AC 连接状态，不能当成 HUNTER 能力掩码 |
| `0x1004` | 函数 `0x3B330`，日志为 `TurboMode::WmiGetSupportMode` | 官方性能支持信息读取；本次未调用 |
| `0x2606` | 函数 `0x34B40`，日志为 `TurboMode::WmiGetAcOut` | 官方另一条 AC_OUT 信息；本次未调用 |
| `0x0802` | 函数 `0x2A470`，日志为 `NoiseEventDealer::GetFan0TargetRPM`，取 `out[3..4]` | 风扇目标 RPM 查询；不能由此把 `out[1..2]` 推定为实测 RPM |

项目缺陷：

- `OemWmiClient.SetPerformanceMode` 目前写 `0x0C07`，再用 `0x0E04` 回读两个不同功能。
- `GetPerformanceStatus` 把 `0x3C06 out[1]` 当支持掩码；`SupportsHunterMode => SupportMask != 0` 再用于拒绝高能切换。本机此读数为 0，不能据此断言高能不支持。
- 读取模式时绑定了额外遥测、上述错误能力查询及适配器查询；其中任一失败会导致整个模式读取失败。
- `ValidatePerformancePreconditions` 把 AC 和电量至少 20% 的要求施加到智能回退，可能阻碍拔电或低电量时退出高能。
- 面板写命令返回的是保存后的快照，后台稍后执行；需要区分“配置已保存”“等待执行”“已回读生效”和失败，不以保存成功证明切换成功。

本次没有实测 `0x0F04` SET。智能／高能写入应使用第三字节 0／1，并经 `0x0E04` 回读验证；该映射需要实际切换验证后才能作为写入实证。

## 4. 电源方案复查：已有 Honor Performance，不应重建

`powercfg /list` 本次仅列平衡，但只读生产代码及注册表均确认以下方案存在：

- 平衡：`381b4222-f694-41f0-9685-ff5bb260df2e`。
- Honor Performance：`b8a2c9f4-7d3e-4a1b-9c2f-5e8d6a3b1c4f`，FriendlyName 为 `Honor Performance`。

`tools/Test-PowerSchemeRead.ps1` 运行通过。服务报告当前智能、平衡方案、AC=true、电量 69%、电脑管家界面未打开。本次不需要创建或导入新方案。

## 5. 待确认的最小实施范围

1. 服务采集电池 NTC 温度，接入现有实时温度与能力字段；不支持或失败保留 null。
2. USB 电压、电流和官方瓦数计算值以独立诊断字段呈现，注明供电能力／口径待确认，实时适配器功率与负载历史继续保持未知。
3. 修正性能写入为 `0x0F04` + 0/1，回读 `0x0E04`；移除错误的 `0x3C06` 能力门槛；非关键遥测失败不阻断模式读取。
4. 高能进入与智能回退分别校验；保留已有电源方案同步、写后验证、失败回滚和电脑管家互斥。
5. 面板按后台实际回读展示等待、成功与错误。

影响：服务硬件读取与性能控制、共享契约（若新增诊断字段）及面板相应显示。风险：切换高能会改变固件功耗／风扇策略；与电脑管家后台策略可能竞争，失败回滚也需要真实验证。修改前依用户全局规则等待确认。

验证：服务构建与有意义的解析／模式命令检查；Chrome DevTools 验证面板状态；本机服务接入后确认温度，再做智能→高能→智能往返，记录 BIOS 回读和 Windows 电源方案并恢复原配置。浏览器模拟结果与硬件实测分别报告。

## 6. 样本身份

下列 DLL FileVersion 均为 `11.0.0.0`：

| 文件 | SHA-256 |
|---|---|
| `Util.dll` | `3B8416E6D217CD0EE2A4437550441A8EDA04288A04C94BEB5210DB9473B977E5` |
| `HardwareSdk.dll` | `376BE4BA96A00BFB6F35CAA57DCAC20386FDA8E0DDA50AB5842099379EE1B17D` |
| `plugins/PerfCommonPlugin.dll` | `A886CD86983037162EB03361ABC6175834D474D07513EFA2BEC3BB6B9FA28FC8` |

只读原始样本：[honor-telemetry-20261009.json](evidence/honor-telemetry-20261009.json)。原采样探针已合并整理为[统一只读 GET 工具](tools/Read-HonorTelemetry.ps1)，HunterSupport 标签已更正为 AcConnectionStatus；原始 JSON 的历史标签与读数不改写。原探针精确副本留在清理备份中。

整理说明：本页保留研究时点的函数证据与验证边界；后续源码实施状态见[研究汇总](conclusions.md)。
