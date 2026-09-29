$ErrorActionPreference = 'Stop'
Write-Host '--- OemWMIMethod instances ---'
$inst = Get-CimInstance -Namespace root/wmi -ClassName OemWMIMethod
$inst | ForEach-Object { Write-Host ("InstanceName = " + $_.InstanceName + "  Active = " + $_.Active) }

$target = $inst | Where-Object { $_.Active } | Select-Object -First 1
if (-not $target) { $target = $inst | Select-Object -First 1 }

# 64-byte input, legacy command BATTERY_THRESH_GET = 0x1103 -> bytes 03 11 00 00 ...
$in = New-Object byte[] 64
$in[0] = 0x03; $in[1] = 0x11

Write-Host '--- Calling OemWMIfun(03 11 ...) ---'
try {
    $r = Invoke-CimMethod -InputObject $target -MethodName OemWMIfun -Arguments @{ u8Input = $in }
    Write-Host ("ReturnValue : " + $r.ReturnValue)
    Write-Host ("u32Resrved : " + $r.u32Resrved)
    if ($r.u8Output) {
        $hex = ($r.u8Output | ForEach-Object { $_.ToString('X2') }) -join ' '
        Write-Host ("u8Output len=" + $r.u8Output.Count + " : " + $hex)
    } else {
        Write-Host 'u8Output : (null)'
    }
} catch {
    Write-Host ("ERROR: " + $_.Exception.Message)
    if ($_.Exception.InnerException) { Write-Host ("INNER: " + $_.Exception.InnerException.Message) }
}
