# 研究工具

以下工具只读取指定 PE 文件或本机已确认的 HWMI GET，不提供固件 SET。工具输出是研究线索，不单独证明函数语义、设备能力或效果。

## 静态 PE 工具

从仓库根目录运行，路径带空格时加引号。工具面向本次研究的 x64 PE；RVA 必须来自匹配版本的 DLL，不能直接套用到其他版本。

| 工具 | 用途 | 示例 |
|---|---|---|
| `pe-exports.js` | 导出名称、序号和 RVA | `node research/tools/pe-exports.js "C:/Program Files/Honor/PCManager/HardwareSdk.dll" GetCurrentUSB` |
| `imports.mjs` | 导入 API；扫描 `call/jmp [rip+disp32]` 引用 | `node research/tools/imports.mjs "C:/Program Files/Honor/PCManager/plugins/PerfCommonPlugin.dll" Power list` |
| `hexdump.mjs` | 根据 RVA 映射文件偏移并输出字节 | `node research/tools/hexdump.mjs "C:/Program Files/Honor/PCManager/HardwareSdk.dll" 13f80 64` |
| `resolve-slot.mjs` | 解析导入名称槽或显示原始指针 | `node research/tools/resolve-slot.mjs "C:/Program Files/Honor/PCManager/HardwareSdk.dll" 13f80` |
| `callers.mjs` | 扫描直接 `call rel32` 的反向引用 | `node research/tools/callers.mjs "C:/Program Files/Honor/PCManager/plugins/PerfCommonPlugin.dll" 35810` |
| `disasm.mjs` | 从给定 RVA 有限长度反汇编 | `node research/tools/disasm.mjs "C:/Program Files/Honor/PCManager/HardwareSdk.dll" 13ce0 128` |

只有 `disasm.mjs` 依赖 `capstone-wasm`；依赖入口保留在 [package.json](../package.json) 与 lockfile。需要时在 `research/` 安装锁定依赖。不要把扫描中的偶然字节匹配或邻近字符串直接认定为调用链；必须结合 PE 函数边界、真实指令、函数日志和参数核对。

`callers.mjs` 的 `DEPTH` 环境变量控制扫描层数，默认 2；这是字节扫描参考，不是完整函数级调用图。`resolve-slot.mjs` 主要按 OriginalFirstThunk 解析名称槽；IAT 的实际间接调用引用以 `imports.mjs ... xref` 核对。

## 统一只读 HWMI 探针

[Read-HonorTelemetry.ps1](Read-HonorTelemetry.ps1) 合并阈值、NTC、USB、性能状态、AC 和风扇目标 GET。必须以本机管理员或等效授权运行；普通权限被拒绝不能据此推断设备不支持。

```powershell
pwsh -NoProfile -File research/tools/Read-HonorTelemetry.ps1 -OutputPath .tmp/honor-get-new-sample.json -SampleCount 6
```

输出目录必须已存在，输出文件必须不存在；工具用 `CreateNew` 防止覆盖证据，并明确输出完整路径、请求采样次数和记录数。原始响应、传输状态、BIOS 状态与解析错误一并保留。没有任意命令参数，也没有 SET 入口。

温度只将默认 NTC 当电池电芯温度；替代传感器、MOS 仅作为分别命名的诊断读数。`0x3C06` 名为 `AcConnectionStatus`，不能当高能支持掩码；`0x0802` 输出是风扇目标 RPM，不能当实测 RPM。USB 值按 mV/mA 显示，但不能据此声明实时功耗或铭牌额定功率；服务实现的验证边界见[研究汇总](../conclusions.md)。

## 保留的工程验证工具

仓库根 `tools/` 中的 BatteryProbe、HistoryProbe、Test-PowerSchemeRead、Publish-HonorControl、Generate-AppIcon 及本次新增的 Test-OemTelemetryProtocol／WindowsPowerProbe 均保留；这些是工程验证或构建工具，没有随一次性研究脚本删除。
