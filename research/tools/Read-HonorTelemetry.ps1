# Local read-only probe. Fixed known GET commands only; no SET or arbitrary-command input.
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$OutputPath,
    [ValidateRange(1,60)][int]$SampleCount = 6,
    [ValidateRange(250,60000)][int]$SampleIntervalMs = 1000
)
$ErrorActionPreference = 'Stop'
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $resolvedOutput) { throw "Refusing to overwrite existing evidence: $resolvedOutput" }
if (!(Test-Path -LiteralPath (Split-Path $resolvedOutput) -PathType Container)) { throw 'Output directory must already exist.' }
$records = [Collections.Generic.List[object]]::new()
$result = [ordered]@{ OutputPath=$resolvedOutput; SampleCount=$SampleCount; SampleIntervalMs=$SampleIntervalMs; Records=$records }
$session = $null
try {
    $session = New-CimSession
    $instances = @(Get-CimInstance -CimSession $session -Namespace root/wmi -ClassName OemWMIMethod)
    $eligible = @($instances | Where-Object { ($null -eq $_.Active -or $_.Active) -and $_.InstanceName })
    $instance = $eligible | Where-Object InstanceName -eq 'ACPI\PNP0C14\HWMI_0' | Select-Object -First 1
    if (!$instance) { $instance = $eligible | Where-Object InstanceName -like '*HWMI_1' | Select-Object -First 1 }
    if (!$instance) { $instance = $eligible | Select-Object -First 1 }
    if (!$instance) { throw 'No active Honor HWMI instance available.' }
    $result.InstanceName = $instance.InstanceName
    # Semantic labels were corrected from named functions; historical raw evidence is unchanged.
    $commands = [ordered]@{
        ChargeThreshold = 0x001103
        BatteryNtcDefault = 0x0E0202
        BatteryNtcAlternate = 0x010202
        BatteryMos = 0x1A0202
        UsbVoltage0 = 0x000902
        UsbCurrent0 = 0x100902
        UsbVoltage1 = 0x001902
        UsbCurrent1 = 0x110902
        PerformanceMode = 0x000E04
        AcConnectionStatus = 0x003C06
        Fan0TargetRpm = 0x000802
        AcOutInformation = 0x002606
    }
    for ($sample = 0; $sample -lt $SampleCount; $sample++) {
        foreach ($entry in $commands.GetEnumerator()) {
            $inputBytes = [byte[]]::new(64)
            # Explicit little-endian three-byte command, independent of host BitConverter endianness.
            $inputBytes[0] = $entry.Value -band 0xFF
            $inputBytes[1] = ($entry.Value -shr 8) -band 0xFF
            $inputBytes[2] = ($entry.Value -shr 16) -band 0xFF
            $record = [ordered]@{
                Sample=$sample; Time=[DateTimeOffset]::Now.ToString('o'); Name=$entry.Key
                Command=('0x{0:X6}' -f $entry.Value)
            }
            try {
                $reply = Invoke-CimMethod -CimSession $session -InputObject $instance -MethodName OemWMIfun -Arguments @{u8Input=$inputBytes}
                $bytes = [byte[]]$reply.u8Output
                $record.ReturnValue = $reply.ReturnValue
                $record.Reserved = $reply.u32Resrved
                $record.OutputLength = $bytes.Length
                $record.Hex = [BitConverter]::ToString($bytes)
                $record.Status = if ($bytes.Length -ge 1) { $bytes[0] } else { $null }
                $record.Sign = if ($bytes.Length -ge 2) { $bytes[1] } else { $null }
                $record.ByteValue = if ($bytes.Length -ge 3) { $bytes[2] } else { $null }
                $record.WordValue = if ($bytes.Length -ge 4) { [int]$bytes[2] -bor ([int]$bytes[3] -shl 8) } else { $null }
                $transportOk = $null -ne $reply.ReturnValue -and $(if ($reply.ReturnValue -is [bool]) { $reply.ReturnValue } else { [uint64]$reply.ReturnValue -eq 0 })
                if (!$transportOk -or ($null -ne $reply.u32Resrved -and [uint32]$reply.u32Resrved -ne 0)) { throw 'CIM transport returned a failure status.' }
                if ($bytes.Length -lt 1) { throw 'Missing u8Output.' }
                if ($bytes[0] -ne 0) { throw ('BIOS rejected GET: 0x{0:X2}' -f $bytes[0]) }
                if ($entry.Key -like 'BatteryNtc*' -or $entry.Key -eq 'BatteryMos') {
                    if ($bytes.Length -lt 3 -or $bytes[1] -gt 1) { throw 'Invalid temperature packet length/sign.' }
                    $temperature = if ($bytes[1] -eq 1) { -[int]$bytes[2] } else { [int]$bytes[2] }
                    if ($temperature -lt -40 -or $temperature -gt 100) { throw 'Temperature outside plausible range.' }
                    $record.TemperatureC = $temperature
                } elseif ($entry.Key -like 'Usb*') {
                    if ($bytes.Length -lt 4 -or $bytes[1] -gt 1) { throw 'Invalid USB packet length/sign.' }
                    $value = $record.WordValue / 1000.0
                    if ($bytes[1] -eq 1) { $value = -$value }
                    $limit = if ($entry.Key -like 'UsbVoltage*') { 60 } else { 20 }
                    if ($value -lt 0 -or $value -gt $limit) { throw 'USB input reading outside plausible range.' }
                    if ($entry.Key -like 'UsbVoltage*') { $record.VoltageV = $value } else { $record.CurrentA = $value }
                    $record.Semantics = 'USB supply diagnostic; instantaneous load and rated power are not established.'
                } elseif ($entry.Key -eq 'ChargeThreshold') {
                    if ($bytes.Length -lt 3 -or $bytes[1] -ge $bytes[2] -or $bytes[2] -gt 100) { throw 'Invalid charge-threshold response.' }
                    $record.ChargeStartPercent = $bytes[1]; $record.ChargeStopPercent = $bytes[2]
                } elseif ($entry.Key -eq 'PerformanceMode') {
                    if ($bytes.Length -lt 2) { throw 'Truncated performance-mode response.' }
                    $record.PerformanceMode = switch ($bytes[1]) { 0 { 'Smart' }; 1 { 'High' }; default { 'Unknown' } }
                } elseif ($entry.Key -eq 'Fan0TargetRpm') {
                    if ($bytes.Length -lt 5) { throw 'Truncated fan-target response.' }
                    $record.TargetRpm = [int]$bytes[3] -bor ([int]$bytes[4] -shl 8)
                }
            } catch { $record.Error = $_.Exception.Message }
            $records.Add([pscustomobject]$record)
        }
        if ($sample -lt $SampleCount - 1) { Start-Sleep -Milliseconds $SampleIntervalMs }
    }
    try { $result.Computer = Get-CimInstance -CimSession $session Win32_ComputerSystem | Select-Object Manufacturer,Model }
    catch { $result.ComputerError = $_.Exception.Message }
} catch { $result.Error = $_.Exception.Message }
finally { if ($session) { Remove-CimSession $session } }
$json = ($result | ConvertTo-Json -Depth 7).Replace([string][char]13, '')
# CreateNew prevents races from overwriting an existing sample even after collection.
$stream = [IO.File]::Open($resolvedOutput, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
try {
    $data = [Text.UTF8Encoding]::new($false).GetBytes($json)
    $stream.Write($data, 0, $data.Length)
} finally { $stream.Dispose() }
Write-Output "OutputPath=$resolvedOutput"
Write-Output "SampleCount=$SampleCount; RecordCount=$($records.Count)"