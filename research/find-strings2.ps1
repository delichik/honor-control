$ErrorActionPreference = 'SilentlyContinue'
$targets = @(
    'C:\Program Files\Honor\PCManager\plugins\HnSmartDisplay.dll',
    'C:\Program Files\Honor\PCManager\plugins\LCDEnhancementPlugin.dll',
    'C:\Program Files\Honor\PCManager\plugins\PerfCommonPlugin.dll',
    'C:\Program Files\Honor\PCManager\plugins\PowerPolicyPlugin.dll',
    'C:\Program Files\Honor\PCManager\HnPerformanceCenter.exe',
    'C:\Program Files\Honor\PCManager\HnPerfPowerNexus.exe',
    'C:\Program Files\Honor\PCManager\AwinicAPOUser.dll',
    'C:\Program Files\Honor\HnLcdEnhancement\LCD_Service.exe'
)
foreach ($f in $targets) {
    if (-not (Test-Path $f)) { Write-Host "MISSING: $f"; continue }
    Write-Host ""
    Write-Host "########## $(Split-Path $f -Leaf) ##########"
    $bytes = [IO.File]::ReadAllBytes($f)
    $uni = [Text.Encoding]::Unicode.GetString($bytes)
    $ascii = [Text.Encoding]::ASCII.GetString($bytes)
    $toks = @()
    $toks += ($uni -split '[^\x20-\x7E]{2,}')
    $toks += ($ascii -split '[^\x20-\x7E]{2,}')
    Write-Host '--- mode / display / health keywords ---'
    $toks | Where-Object {
        $_ -match 'Intell|Performance|PerfMode|PowerMode|FanMode|E-?[Ss]ports|GameMode|Turbo|Wild|Mode[A-Z_]|[Ee]ye[PC]|E-[Bb]ook|Ebook|ColorTemp|ColorMode|Blue|Night|Dimming|DcDim|Flicker|Health|ScreenMode|PaperMode|Comfort|Care' -and
        $_.Length -gt 3 -and $_.Length -lt 120
    } | Sort-Object -Unique | Select-Object -First 70
    Write-Host '--- devices / WMI / registry / IPC ---'
    $toks | Where-Object {
        $_ -match '\\\.\\|OemWMI|ROOT\\WMI|Software\\|SYSTEM\\|\.pipe\.|WmiSet|WmiGet|GammaRamp|SetDeviceGamma|MSMonitor|WmiMonitor' -and
        $_.Length -gt 3 -and $_.Length -lt 140
    } | Sort-Object -Unique | Select-Object -First 40
}
