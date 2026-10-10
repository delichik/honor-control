# Pure production parser/command validation. Does not send WMI SET or change power settings.
# -ReadHardware additionally opens one local CIM session for read-only NTC and USB diagnostics.
param([switch]$ReadHardware)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$files = @(
    'src/HonorControl.Service/Hardware/OemTelemetryProtocol.cs',
    'src/HonorControl.Service/Hardware/OemWmiClient.cs',
    'src/HonorControl/Models/ChargeThreshold.cs',
    'src/HonorControl/Models/PerformanceStatus.cs'
)
$sources = foreach ($file in $files) {
    $source = Get-Content -Raw -Encoding utf8 (Join-Path $root $file)
    $source = $source -replace '(?m)^using [^\r\n]+;\r?\n', ''
    if ($source -match '(?m)^namespace [\w.]+;') {
        ($source -replace '(?m)^namespace ([\w.]+);', 'namespace $1 {') + "`n}"
    } else { $source }
}
$prefix = @'
#nullable enable
global using System;
global using System.Collections.Generic;
global using System.Text;
global using System.Threading;
global using HonorControl.Models;
global using Microsoft.Management.Infrastructure;
'@
$references = @(Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll' | Select-Object -ExpandProperty FullName)
$references += [Microsoft.Management.Infrastructure.CimSession].Assembly.Location
# PowerShell's bundled MMI targets an older System.Runtime than its compiler. Suppress only that identity warning.
Add-Type -TypeDefinition ($prefix + "`n" + ($sources -join "`n")) -ReferencedAssemblies $references -CompilerOptions '/nowarn:1701'

function Assert-Rejected([scriptblock]$Operation, [string]$Name) {
    $rejected = $false
    try { & $Operation | Out-Null } catch { $rejected = $true }
    if (!$rejected) { throw "Expected rejection: $Name" }
}
function Assert-Equal($Actual, $Expected, [string]$Name) {
    if ($Actual -ne $Expected) { throw "$Name expected $Expected, got $Actual" }
}
$protocol = [HonorControl.Services.OemTelemetryProtocol]
Assert-Equal $protocol::PerformanceSetCommand 0x0F04 'SET command'
Assert-Equal ($protocol::PerformancePayload(1)) 0 'Smart payload'
Assert-Equal ($protocol::PerformancePayload(2)) 1 'High payload'
Assert-Rejected { $protocol::PerformancePayload(3) } 'Unknown mode'
Assert-Equal ($protocol::ParsePerformanceMode([byte[]](0,0))) 1 'Smart GET'
Assert-Equal ($protocol::ParsePerformanceMode([byte[]](0,1))) 2 'High GET'
Assert-Equal ($protocol::ParsePerformanceMode([byte[]](0,9))) 0 'Unknown GET'
Assert-Equal ($protocol::ParseBatteryTemperature([byte[]](0,0,34,0))) 34 'Captured NTC packet'
Assert-Equal ($protocol::ParseBatteryTemperature([byte[]](0,1,12))) -12 'Signed NTC'
Assert-Rejected { $protocol::ParseBatteryTemperature([byte[]](1,0,34)) } 'BIOS rejected NTC'
Assert-Rejected { $protocol::ParseBatteryTemperature([byte[]](0,0)) } 'Truncated NTC'
Assert-Rejected { $protocol::ParseBatteryTemperature([byte[]](0,2,34)) } 'Invalid sign'
Assert-Rejected { $protocol::ParseBatteryTemperature([byte[]](0,0,101)) } 'Implausible NTC'
$voltage = $protocol::ParseAdapterValue([byte[]](0,0,0x20,0x4E), $true)
$current = $protocol::ParseAdapterValue([byte[]](0,0,0x88,0x13), $false)
Assert-Equal $voltage 20 'Captured USB mV'
Assert-Equal $current 5 'Captured USB mA'
Assert-Equal ($voltage * $current) 100 'Diagnostic product only'
Assert-Rejected { $protocol::ParseAdapterValue([byte[]](0xEE,0,0,0), $true) } 'Rejected USB port'
Assert-Rejected { $protocol::ParseAdapterValue([byte[]](0,0,0), $true) } 'Truncated USB'
Assert-Rejected { $protocol::ParseAdapterValue([byte[]](0,1,0x20,0x4E), $true) } 'Negative input voltage'
Write-Output 'PASS: production OemWmiClient compiles; performance command/mapping, NTC, USB units, rejection/sign/length/range checks.'
if ($ReadHardware) {
    [HonorControl.Services.OemWmiClient]::new().GetTelemetry() | ConvertTo-Json
}
