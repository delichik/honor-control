$Dir = 'C:\Program Files\Honor\PCManager'
$names = Get-ChildItem $Dir -Include *.dll,*.exe -Recurse -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty FullName
$hits = @{}
foreach ($f in $names) {
    try {
        $bytes = [IO.File]::ReadAllBytes($f)
    } catch { continue }
    $ascii = [Text.Encoding]::ASCII.GetString($bytes)
    $uni = [Text.Encoding]::Unicode.GetString($bytes)
    $toks = @()
    $toks += ($ascii -split '[^\x20-\x7E]{2,}')
    $toks += ($uni -split '[^\x20-\x7E]{2,}')
    $found = $toks |
        Where-Object { $_ -match 'root\\|ROOT\\|\\WMI|WbemLocator|Os2Ec|ChargeMode|SmartCharg|ChargeLimit|ChargeTh|BatteryProtect|CustomCharge|\\\\\.\\' } |
        Where-Object { $_.Length -gt 4 -and $_.Length -lt 160 } |
        Sort-Object -Unique
    if ($found) {
        $hits[$f] = $found | Select-Object -First 60
    }
}
foreach ($k in $hits.Keys) {
    Write-Host "=== $k ==="
    $hits[$k] | ForEach-Object { Write-Host "  $_" }
}
