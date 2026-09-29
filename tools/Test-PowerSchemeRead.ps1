# Read-only regression test. No firmware or Windows power settings are changed.
# Run with PowerShell 7 (also available on the GitHub Windows runner).
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$files = @(
    'src/HonorControl/Services/PowerSchemeService.cs',
    'src/HonorControl/Models/PowerSchemeInfo.cs',
    'src/HonorControl/Models/PowerSchemeStatus.cs'
)
$sources = foreach ($file in $files) {
    $source = Get-Content -Raw -Encoding utf8 (Join-Path $root $file)
    # Compile the production code without the WinUI project's implicit usings.
    ($source -replace '(?m)^namespace ([\w.]+);', 'namespace $1 {') + "`n}"
}
Add-Type -TypeDefinition ("#nullable enable`nglobal using System;`n" + ($sources -join "`n"))
$service = [HonorControl.Services.PowerSchemeService]::new()
$status = $service.GetStatus()
if ([string]::IsNullOrWhiteSpace($status.Active.Name)) { throw 'Active scheme name is empty.' }
if ($status.Active.Id -eq [HonorControl.Services.PowerSchemeService]::BalancedSchemeId -and $null -eq $status.Balanced) {
    throw 'The active Balanced scheme was incorrectly reported as missing.'
}
if ($null -ne $status.Balanced -and $status.GetTarget(1).Id -ne $status.Balanced.Id) {
    throw 'Smart mode target does not match Balanced.'
}
if ($null -ne $status.HonorPerformance -and $status.GetTarget(2).Id -ne $status.HonorPerformance.Id) {
    throw 'High-performance target does not match Honor Performance.'
}
if ($null -ne $status.GetTarget(3)) { throw 'Unknown modes must not have a target.' }
$readOptionalScheme = [HonorControl.Services.PowerSchemeService].GetMethod('TryGetScheme', [Reflection.BindingFlags]'NonPublic, Static')
if ($null -ne $readOptionalScheme.Invoke($null, @([Guid]::Empty))) {
    throw 'A nonexistent scheme must not be reported as available.'
}
Write-Output "PASS: production power-scheme reader; active=$($status.Active.Name), Balanced=$($null -ne $status.Balanced), HonorPerformance=$($null -ne $status.HonorPerformance)"
