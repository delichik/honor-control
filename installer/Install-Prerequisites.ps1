param(
    [ValidateSet('Check', 'Install')][string]$Mode = 'Check',
    [string]$ErrorFile,
    [string]$RestartFlag,
    [string]$StateFile,
    [string]$DotNetInstaller
)

$ErrorActionPreference = 'Stop'
$dotNetSha512 = '2dc2f346a4bcb53ab15ab6ab84348c02eb31b0a3d4e8e06aac73b4c89f1b001af01943ed4c5735fa5585f1dd731a942d2034577dfe937f4454367ff2d6edf929'

function Test-SupportedDevice {
    $windows = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    if (-not [Environment]::Is64BitOperatingSystem -or [int]$windows.CurrentBuildNumber -lt 22000) {
        throw 'Windows 11 x64 is required.'
    }
    $bios = Get-ItemProperty -LiteralPath 'HKLM:\HARDWARE\DESCRIPTION\System\BIOS'
    if ($bios.SystemManufacturer -notmatch '(?i)\bHONOR\b') {
        throw "An HONOR computer is required. Detected manufacturer: $($bios.SystemManufacturer)."
    }
    $instances = @(Get-CimInstance -Namespace root/wmi -ClassName OemWMIMethod |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_.InstanceName) -and ($null -eq $_.Active -or $_.Active) } |
        Sort-Object @{ Expression = { if ($_.InstanceName -eq 'ACPI\PNP0C14\HWMI_0') { 0 } else { 1 } } })
    if ($instances.Count -eq 0) { throw 'The HONOR hardware interface is unavailable.' }
    [byte[]]$inputBytes = New-Object byte[] 64
    $inputBytes[0] = 0x03
    $inputBytes[1] = 0x11
    foreach ($instance in $instances) {
        try {
            $reply = Invoke-CimMethod -InputObject $instance -MethodName OemWMIfun -Arguments @{ u8Input = $inputBytes }
            $outputBytes = [byte[]]$reply.u8Output
            $transportSucceeded = if ($reply.ReturnValue -is [bool]) { $reply.ReturnValue } else { $reply.ReturnValue -eq 0 }
            if ($transportSucceeded -and ($null -eq $reply.u32Resrved -or $reply.u32Resrved -eq 0) -and
                $outputBytes.Length -ge 3 -and $outputBytes[0] -eq 0 -and
                $outputBytes[1] -le 100 -and $outputBytes[2] -le 100 -and $outputBytes[1] -lt $outputBytes[2]) {
                return
            }
        }
        catch { continue }
    }
    throw 'The HONOR hardware interface did not return a valid charge threshold.'
}

function Test-DotNetRuntime {
    $programFiles64 = if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }
    $root = Join-Path $programFiles64 'dotnet\shared\Microsoft.NETCore.App'
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { return $false }
    foreach ($directory in Get-ChildItem -LiteralPath $root -Directory) {
        $version = $null
        if ([Version]::TryParse($directory.Name, [ref]$version) -and
            $version.Major -eq 8 -and $version.Minor -eq 0) { return $true }
    }
    return $false
}

function Assert-VerifiedInstaller([string]$path, [string]$name, [string]$sha512) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Downloaded $name is missing." }
    if ($sha512 -and (Get-FileHash -LiteralPath $path -Algorithm SHA512).Hash -ne $sha512) {
        throw "Downloaded $name failed hash verification."
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
        throw "Downloaded $name is not a valid Microsoft-signed installer."
    }
}

function Install-Dependency([string]$path, [string[]]$arguments) {
    $process = Start-Process -FilePath $path -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden
    if ($process.ExitCode -notin @(0, 3010)) { throw "Dependency installation failed with exit code $($process.ExitCode)." }
    if ($process.ExitCode -eq 3010) {
        if ($RestartFlag) { Set-Content -LiteralPath $RestartFlag -Value '1' -Encoding ascii }
    }
}

try {
    Test-SupportedDevice
    # Only the service needs a runtime now: the control panel is Rust/Tauri (its own runtime plus the
    # system WebView2 on Windows 11), so the Windows App Runtime check and its download are gone.
    $needsDotNet = -not (Test-DotNetRuntime)
    if ($Mode -eq 'Check') {
        if ($StateFile) {
            $state = "DotNet=$([int]$needsDotNet)`r`n"
            [IO.File]::WriteAllText($StateFile, $state, [Text.UTF8Encoding]::new($false))
        }
    }
    else {
        if ($needsDotNet) {
            Assert-VerifiedInstaller $DotNetInstaller '.NET 8 Runtime' $dotNetSha512
            Install-Dependency $DotNetInstaller @('/install', '/quiet', '/norestart')
            if (-not (Test-DotNetRuntime)) { throw '.NET 8 Runtime is still unavailable after installation.' }
        }
    }
    exit 0
}
catch {
    if ($ErrorFile) { [IO.File]::WriteAllText($ErrorFile, $_.Exception.Message, [Text.UTF8Encoding]::new($false)) }
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
