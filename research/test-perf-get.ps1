$ErrorActionPreference = 'Continue'
$inst = Get-CimInstance -Namespace root/wmi -ClassName OemWMIMethod |
        Where-Object { $_.InstanceName -eq 'ACPI\PNP0C14\HWMI_0' }

function Call-Wmi([byte[]]$in) {
    $r = Invoke-CimMethod -InputObject $inst -MethodName OemWMIfun -Arguments @{ u8Input = $in }
    return $r
}

function HexDump([byte[]]$b, [int]$n) {
    ($b[0..([Math]::Min($n, $b.Count) - 1)] | ForEach-Object { $_.ToString('X2') }) -join ' '
}

# 1. Perf mode GET 0x0802 (TurboMode status, word at out[3..4])
$in = New-Object byte[] 64; $in[0] = 0x02; $in[1] = 0x08
$r = Call-Wmi $in
Write-Host ("0x0802 (perf mode status): ret=" + $r.ReturnValue + " u32Resrved=" + $r.u32Resrved)
if ($r.u8Output) { Write-Host ("  out[0..7]: " + (HexDump $r.u8Output 8)) }

# 2. Support mode GET 0x3C06 (WmiGetSupportMode, mask at out[1])
$in = New-Object byte[] 64; $in[0] = 0x06; $in[1] = 0x3C
$r = Call-Wmi $in
Write-Host ("0x3C06 (support mask): ret=" + $r.ReturnValue)
if ($r.u8Output) { Write-Host ("  out[0..7]: " + (HexDump $r.u8Output 8)) }

# 3. GET 0x2606 (TurboMode related query, out[1])
$in = New-Object byte[] 64; $in[0] = 0x06; $in[1] = 0x26
$r = Call-Wmi $in
Write-Host ("0x2606: ret=" + $r.ReturnValue)
if ($r.u8Output) { Write-Host ("  out[0..7]: " + (HexDump $r.u8Output 8)) }

# 4. Adapter wattage 0x0902 (u64-style in 64-byte form: 02 09)
$in = New-Object byte[] 64; $in[0] = 0x02; $in[1] = 0x09
$r = Call-Wmi $in
Write-Host ("0x0902 (adapter): ret=" + $r.ReturnValue)
if ($r.u8Output) { Write-Host ("  out[0..7]: " + (HexDump $r.u8Output 8)) }
