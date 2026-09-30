param(
    [ValidateSet('Stop', 'Start', 'Install', 'Uninstall', 'Backup', 'Restore')][string]$Mode,
    [string]$ServicePath,
    [string]$InstallDir,
    [string]$BackupPath,
    [string]$ErrorFile
)

$ErrorActionPreference = 'Stop'
$name = 'HonorControlService'

function Invoke-ServiceControl([string[]]$arguments) {
    & "$env:WINDIR\System32\sc.exe" @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Service control failed: sc.exe $($arguments[0]), exit code $LASTEXITCODE." }
}

function Assert-ServiceReady {
    if ((Get-Service -Name $name -ErrorAction Stop).Status -ne 'Running') {
        throw 'The Honor Control service did not remain running.'
    }
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'HonorControl.Service.v1', [IO.Pipes.PipeDirection]::InOut)
    try { $pipe.Connect(5000) }
    finally { $pipe.Dispose() }
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
    if ($Mode -eq 'Install') {
        if (-not (Test-Path -LiteralPath $ServicePath -PathType Leaf)) { throw 'The service executable is missing.' }
        if ($service) {
            Invoke-ServiceControl -arguments @('config', $name, "binPath= `"$ServicePath`"", 'start= delayed-auto')
        }
        else {
            Invoke-ServiceControl -arguments @('create', $name, "binPath= `"$ServicePath`"", 'start= delayed-auto', 'DisplayName= Honor Control Service')
        }
        Invoke-ServiceControl -arguments @('description', $name, 'Maintains Honor hardware configuration and provides read-only device status.')
        Start-Service -Name $name -ErrorAction Stop
        (Get-Service -Name $name).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
        Assert-ServiceReady
    }
    if ($Mode -eq 'Start' -and $service -and $service.Status -ne 'Running') {
        Start-Service -Name $name -ErrorAction Stop
        (Get-Service -Name $name).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    }
    if ($Mode -eq 'Uninstall' -and $service) {
        Invoke-ServiceControl -arguments @('delete', $name)
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
        foreach ($folder in @('ui', 'service', 'installer')) {
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
            Invoke-ServiceControl -arguments @('config', $name, "binPath= `"$ServicePath`"", 'start= delayed-auto')
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
