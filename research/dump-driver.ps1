$ErrorActionPreference = 'SilentlyContinue'
$f = 'C:\Program Files\HONOR\PCManager\HNOs2EC10x64.sys'
$bytes = [IO.File]::ReadAllBytes($f)
Write-Host ("Size: " + $bytes.Length)
$ascii = [Text.Encoding]::ASCII.GetString($bytes)
$uni = [Text.Encoding]::Unicode.GetString($bytes)
Write-Host '--- ASCII strings (len>=4) ---'
($ascii -split '[^\x20-\x7E]{4,}') | Where-Object { $_.Length -ge 4 } | Sort-Object -Unique
Write-Host ''
Write-Host '--- Unicode strings (len>=4) ---'
($uni -split '[^\x20-\x7E]{4,}') | Where-Object { $_.Length -ge 4 } | Sort-Object -Unique
