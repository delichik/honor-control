$ErrorActionPreference = 'SilentlyContinue'
$targets = @(
    'C:\Program Files\Honor\PCManager\HardwareSdk.dll',
    'C:\Program Files\Honor\PCManager\Util.dll',
    'C:\Program Files\Honor\PCManager\HardwareHal.dll',
    'C:\Program Files\Honor\PCManager\plugins\SmartChargePlugin.dll',
    'C:\Program Files\Honor\PCManager\plugins\PowerPolicyPlugin.dll',
    'C:\Program Files\Honor\PCManager\plugins\MonPowerPlugin.dll',
    'C:\Program Files\Honor\PCManager\DriverCurVersion.dll',
    'C:\Program Files\Honor\PCManager\HnPerfPowerNexus.exe'
)
foreach ($f in $targets) {
    if (-not (Test-Path $f)) { Write-Host "MISSING: $f"; continue }
    Write-Host ""
    Write-Host "########## $f ##########"
    $bytes = [IO.File]::ReadAllBytes($f)
    $uni = [Text.Encoding]::Unicode.GetString($bytes)
    $toks = $uni -split '[^\x20-\x7E]{2,}'
    # GUID-like
    Write-Host '--- GUIDs ---'
    $toks | Where-Object { $_ -match '^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$' } | Sort-Object -Unique
    Write-Host '--- battery/charge/threshold strings ---'
    $toks | Where-Object { $_ -match '[Bb]atter|[Cc]harg|[Tt]hreshold|WMI|Ec[A-Z_]|Os2Ec|BMS|Power[A-Z]' -and $_.Length -gt 3 -and $_.Length -lt 120 } | Sort-Object -Unique | Select-Object -First 100
}
