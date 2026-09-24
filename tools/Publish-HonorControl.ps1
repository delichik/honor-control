param(
    [Parameter(Mandatory = $true)]
    [bool]$SelfContained,

    [Parameter(Mandatory = $true)]
    [bool]$WindowsAppSdkSelfContained,

    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory
)

$ErrorActionPreference = 'Stop'
$logPath = Join-Path $env:RUNNER_TEMP ("HonorControl-publish-{0}.log" -f [Guid]::NewGuid().ToString('N'))
$arguments = @(
    'src/HonorControl/HonorControl.csproj'
    '/target:Publish'
    '/property:Configuration=Release'
    '/property:Platform=x64'
    '/property:RuntimeIdentifier=win-x64'
    '/verbosity:normal'
    "/property:SelfContained=$($SelfContained.ToString().ToLowerInvariant())"
    "/property:WindowsAppSDKSelfContained=$($WindowsAppSdkSelfContained.ToString().ToLowerInvariant())"
    "/property:PublishDir=$PublishDirectory"
)

$msbuild = Get-Command msbuild -ErrorAction Stop
$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $msbuild.Source
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
foreach ($argument in $arguments) {
    $startInfo.ArgumentList.Add($argument)
}

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo
$process.Start() | Out-Null
$standardOutputTask = $process.StandardOutput.ReadToEndAsync()
$standardErrorTask = $process.StandardError.ReadToEndAsync()
$process.WaitForExit()
$standardOutput = $standardOutputTask.GetAwaiter().GetResult()
$standardError = $standardErrorTask.GetAwaiter().GetResult()
$exitCode = $process.ExitCode
$process.Dispose()

$combinedOutput = $standardOutput
if (-not [string]::IsNullOrWhiteSpace($standardError)) {
    $combinedOutput += "`n--- STDERR ---`n" + $standardError
}
[System.IO.File]::WriteAllText($logPath, $combinedOutput, [System.Text.UTF8Encoding]::new($false))
Write-Output $combinedOutput
if ($exitCode -eq 0) {
    exit 0
}

$logLines = Get-Content -LiteralPath $logPath
$errorLines = @($logLines | Where-Object {
    $_ -match '(^|:\s+)error\s+[A-Z]+\d+:' -or
    $_ -match '\berror\s+(MSB|NETSDK|CS|XLS)\d+\b'
})

$xamlDiagnostics = [System.Collections.Generic.List[string]]::new()
$objRoot = Join-Path $PWD 'src\HonorControl\obj'
if (Test-Path -LiteralPath $objRoot) {
    $outputFiles = @(Get-ChildItem -LiteralPath $objRoot -Recurse -File -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -in @('output.json', 'XamlCompilerError.xml') -or $_.Extension -in @('.err', '.log')
    })
    foreach ($file in $outputFiles) {
        $xamlDiagnostics.Add("--- $($file.FullName) ---")
        foreach ($line in Get-Content -LiteralPath $file.FullName -ErrorAction SilentlyContinue | Select-Object -First 200) {
            $xamlDiagnostics.Add($line)
        }
    }
}
if ($xamlDiagnostics.Count -gt 0) {
    $errorLines += $xamlDiagnostics
}
if ($errorLines.Count -eq 0) {
    $focusIndexes = [System.Collections.Generic.List[int]]::new()
    for ($index = 0; $index -lt $logLines.Count; $index++) {
        if ($logLines[$index] -match 'MarkupCompilePass1|XamlCompiler\.exe|CompileXaml|FAILED|Exception|exit(?:ed)? with code') {
            $focusIndexes.Add($index)
        }
    }
    $contextIndexes = [System.Collections.Generic.SortedSet[int]]::new()
    foreach ($focusIndex in $focusIndexes | Select-Object -First 4) {
        for ($index = [Math]::Max(0, $focusIndex - 2); $index -le [Math]::Min($logLines.Count - 1, $focusIndex + 4); $index++) {
            $contextIndexes.Add($index) | Out-Null
        }
    }
    $errorLines = @($contextIndexes | ForEach-Object { "[$($_ + 1)] $($logLines[$_])" })
}
if ($errorLines.Count -eq 0) {
    $errorLines = @($logLines | Select-Object -Last 9)
}

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value "## MSBuild publish failure`n`n``````text"
    $errorLines | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value '```'
}

foreach ($line in $errorLines | Select-Object -First 9) {
    $annotation = $line.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
    Write-Output "::error::$annotation"
}

exit $exitCode
