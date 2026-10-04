param(
    [ValidateSet('Stop', 'Start', 'Install', 'Uninstall', 'Backup', 'Restore')][string]$Mode,
    [string]$ServicePath,
    [string]$InstallDir,
    [string]$BackupPath,
    [string]$ErrorFile,
    [string]$TrayPath,
    [string]$TaskUser
)

$ErrorActionPreference = 'Stop'
$name = 'HonorControlService'

function Invoke-ServiceControl([string[]]$arguments) {
    & "$env:WINDIR\System32\sc.exe" @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Service control failed: sc.exe $($arguments[0]), exit code $LASTEXITCODE." }
}

function Set-ServiceRegistration([string]$path, [bool]$exists) {
    $quotedPath = '"' + $path + '"'
    if ($exists) {
        $installedPath = (Get-CimInstance Win32_Service -Filter "Name='$name'").PathName.Trim().Trim('"')
        if (-not [string]::Equals($installedPath, $path, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'An unrelated service already uses the HonorControlService name.'
        }
        Set-Service -Name $name -StartupType Automatic -ErrorAction Stop
    }
    else {
        New-Service -Name $name -BinaryPathName $quotedPath -DisplayName 'Honor Control Service' -StartupType Automatic -ErrorAction Stop | Out-Null
    }
    $key = "HKLM:\SYSTEM\CurrentControlSet\Services\$name"
    New-ItemProperty -LiteralPath $key -Name DelayedAutoStart -Value 1 -PropertyType DWord -Force | Out-Null
    Set-ItemProperty -LiteralPath $key -Name Description -Value 'Maintains Honor hardware configuration and provides read-only device status.'
}

function Assert-ServiceReady {
    if ((Get-Service -Name $name -ErrorAction Stop).Status -ne 'Running') {
        throw 'The Honor Control service did not remain running.'
    }
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'HonorControl.Service.v1', [IO.Pipes.PipeDirection]::InOut)
    try { $pipe.Connect(5000) }
    finally { $pipe.Dispose() }
}

# The panel and tray are ordinary-user processes, and starting the service when it is
# not running can only go through the SCM. This adds one ACE to the service DACL: allow
# interactive users to start it (RP = SERVICE_START). It deliberately does NOT grant
# stop permission (WP): the tray menu asks the service to stop itself over the pipe.
function Grant-ServiceStartToUsers {
    $current = (& "$env:WINDIR\System32\sc.exe" sdshow $name | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($current)) {
        throw 'Unable to read the service security descriptor.'
    }
    $current = $current.Trim()
    if ($current -match '\(A;;RP;;;I[UU]\)') { return }  # Idempotent: do not insert the ACE twice when reinstalling.
    if (-not $current.StartsWith('D:')) { throw 'Unexpected service security descriptor format.' }

    # Insert at the front of the DACL. Order does not matter for an allow-only rule, but a
    $updated = 'D:(A;;RP;;;IU)' + $current.Substring(2)
    Invoke-ServiceControl @('sdset', $name, $updated)

    $verify = (& "$env:WINDIR\System32\sc.exe" sdshow $name | Select-Object -First 1)
    if ($verify -notmatch '\(A;;RP;;;I[UU]\)') { throw 'Granting SERVICE_START did not take effect.' }
}

# The tray logon task.
# The tray must live in the user session while the service runs in session 0; this task
# bridges the two. Registered at install time, DISABLED by default (the default policy is
# OnDemand); the service enables and triggers it according to the policy. /it means
function Set-TrayTaskRegistration([string]$path, [string]$user) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'The tray executable is missing.' }

    # schtasks accepts "user" or "domain\user"; qualify a bare name so that on a domain machine
    # it cannot silently resolve to a different account that happens to share the name.
    if (-not [string]::IsNullOrWhiteSpace($user) -and $user -notmatch '\\') {
        $user = "$env:USERDOMAIN\$user"
    }

    $action = '"{0}"' -f $path
    $arguments = @('/create', '/tn', 'HonorControlTrayHost', '/tr', $action, '/sc', 'onlogon', '/f')
    if (-not [string]::IsNullOrWhiteSpace($user)) { $arguments += @('/ru', $user, '/it') }

    & "$env:WINDIR\System32\schtasks.exe" @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Registering the tray task failed with exit code $LASTEXITCODE." }

    & "$env:WINDIR\System32\schtasks.exe" /change /tn 'HonorControlTrayHost' /disable | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Disabling the tray task failed with exit code $LASTEXITCODE." }
}

function Remove-TrayTaskRegistration {
    # schtasks returns non-zero when the task does not exist; uninstall must be idempotent.
    & "$env:WINDIR\System32\schtasks.exe" /delete /tn 'HonorControlTrayHost' /f 2>$null | Out-Null
}

# Stop the tray and the panel before an upgrade or uninstall: their executables are locked.
function Stop-TrayAndPanel {
    foreach ($processName in @('HonorControl.Tray', 'honor-control-panel', 'HonorControl.Panel')) {
        Get-Process -Name $processName -ErrorAction SilentlyContinue | ForEach-Object {
            try {
                $_.Kill()
                $_.WaitForExit(5000) | Out-Null
            }
            catch {
                # Already exited or access denied: file replacement reports any real problem later.
            }
        }
    }
}

try {
    $service = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($Mode -eq 'Stop' -and $service -and $ServicePath) {
        $installedPath = (Get-CimInstance Win32_Service -Filter "Name='$name'").PathName.Trim().Trim('"')
        if (-not [string]::Equals($installedPath, $ServicePath, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'An unrelated service already uses the HonorControlService name.'
        }
    }
    if ($Mode -in @('Stop', 'Uninstall') -and $service) {
        if ($service.Status -ne 'Stopped') {
            Stop-Service -Name $name -ErrorAction Stop
            $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
        }
    }
    # The tray and panel must exit before files are replaced, otherwise their exe stays locked.
    if ($Mode -in @('Stop', 'Restore')) { Stop-TrayAndPanel }
    if ($Mode -eq 'Install') {
        if (-not (Test-Path -LiteralPath $ServicePath -PathType Leaf)) { throw 'The service executable is missing.' }
        Set-ServiceRegistration $ServicePath ([bool]$service)
        Grant-ServiceStartToUsers
        if (-not [string]::IsNullOrWhiteSpace($TrayPath)) {
            Set-TrayTaskRegistration $TrayPath $TaskUser
        }
        Start-Service -Name $name -ErrorAction Stop
        (Get-Service -Name $name).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
        Assert-ServiceReady
    }
    if ($Mode -eq 'Start' -and $service -and $service.Status -ne 'Running') {
        Start-Service -Name $name -ErrorAction Stop
        (Get-Service -Name $name).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    }
    if ($Mode -eq 'Uninstall') {
        Stop-TrayAndPanel
        Remove-TrayTaskRegistration
        if ($service) { Invoke-ServiceControl -arguments @('delete', $name) }
    }
    if ($Mode -eq 'Backup') {
        if (-not (Test-Path -LiteralPath $InstallDir -PathType Container)) { throw 'The existing installation is missing.' }
        if (Test-Path -LiteralPath $BackupPath) { throw 'The upgrade backup destination already exists.' }
        Copy-Item -LiteralPath $InstallDir -Destination $BackupPath -Recurse -Force
        if (-not (Test-Path -LiteralPath (Join-Path $BackupPath 'service\HonorControl.Service.exe') -PathType Leaf)) {
            throw 'The existing service executable was not backed up.'
        }
    }
    if ($Mode -eq 'Restore') {
        if ([string]::IsNullOrWhiteSpace($InstallDir) -or [string]::IsNullOrWhiteSpace($BackupPath)) {
            throw 'The installation and backup paths are required for recovery.'
        }
        $installRoot = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')
        $backupRoot = [IO.Path]::GetFullPath($BackupPath).TrimEnd('\')
        if ($installRoot -eq [IO.Path]::GetPathRoot($installRoot).TrimEnd('\') -or
            $backupRoot.StartsWith($installRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The recovery paths are unsafe.'
        }
        if (-not (Test-Path -LiteralPath (Join-Path $BackupPath 'service\HonorControl.Service.exe') -PathType Leaf)) {
            throw 'The upgrade backup is incomplete.'
        }
        if ($service -and $service.Status -ne 'Stopped') {
            Stop-Service -Name $name -ErrorAction Stop
            $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
        }
        foreach ($folder in @('panel', 'tray', 'service', 'installer')) {
            $destination = [IO.Path]::GetFullPath((Join-Path $installRoot $folder))
            if (-not $destination.StartsWith($installRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
                throw 'A recovery target is outside the installation directory.'
            }
            if (Test-Path -LiteralPath $destination) {
                if (((Get-Item -LiteralPath $destination -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw 'A recovery target is a reparse point.'
                }
                Remove-Item -LiteralPath $destination -Recurse -Force
            }
            $source = Join-Path $BackupPath $folder
            if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $destination -Recurse -Force }
        }
        Get-ChildItem -LiteralPath $BackupPath -File -Force | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $InstallDir $_.Name) -Force
        }
        if ($service) {
            Set-ServiceRegistration $ServicePath $true
            Start-Service -Name $name -ErrorAction Stop
            (Get-Service -Name $name).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
            Assert-ServiceReady
        }
    }
    exit 0
}
catch {
    if ($ErrorFile) { [IO.File]::WriteAllText($ErrorFile, $_.Exception.Message, [Text.UTF8Encoding]::new($false)) }
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
