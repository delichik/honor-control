$ErrorActionPreference = 'SilentlyContinue'
$classes = Get-CimClass -Namespace root/WMI
Write-Host ("Total: " + $classes.Count)
Write-Host '=== Non-MS, non-system classes in root/WMI ==='
$classes | Where-Object {
    $_.CimClassName -notmatch '^__' -and
    $_.CimClassName -notmatch '^MS' -and
    $_.CimClassName -notmatch '^(Bcd|Kernel|Win32|CIM|ACPI|Acpi|WMI|WT_|Etherip|Fibre|ISCSI|iSCSI|MSFC|MS_SM|MSSerial|MSNdis|MSTape|MSiSCSI|MpDevice|PortCls|RegisterLsn|Scrdencl|Smartcard|Storage|TcpIp|WLan|Wmicm|Root|System|Processor|Generic|Ioctl|Indirect|AntiStarvation|AutoBoost|Ctx|Cs|Perf|Firmware|Activity|ALPC|CritSec|Csc|Bid|BITSService|Bfe|Certificate|CompCS|CancelKTimer|Counter|Close|CMsft|Context|Authfwcfg|Amlie|Ataport|Cooling|Content|CPMG|Date|DDC|DC|Desktop|Device|Dfs|DHCP|Display|Dns|Dpapi|Drive|DVD|Event|ExFAT|Fastfat|File|FltMgr|Ftp|HTTP|Idle|IP|Ipmi|IPv4|IPv6|Irm|Load|Lbr|Logical|Lsm|Mailbox|Memory|Mode|Modify|MCA|NDIS|Ntfs|OS|Pae|Page|Physical|PnP|Power|Print|Process|Process|Registry|Rio|Rpc|RTL|Sampler|Scan|Session|Set|SMB|Srv|Tcp|Thread|Time|Trace|UMDF|Usb|Vid|VolMgr|Volume|Watchdog|Wifi|WinHttp|Winnat|WMIPF|WMIREG|WMITrace|Xsave|Zone|WSS|Spl|UM|Tpm|TBS|Spool|Serialize|Segment|Reflector|Read|Quantum|Protect|Pool|Policy|Prc|Pmc)'
} | Sort-Object CimClassName | ForEach-Object { Write-Host ("  " + $_.CimClassName) }
