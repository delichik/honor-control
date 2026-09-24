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
    '/verbosity:diagnostic'
    "/property:SelfContained=$($SelfContained.ToString().ToLowerInvariant())"
    "/property:WindowsAppSDKSelfContained=$($WindowsAppSdkSelfContained.ToString().ToLowerInvariant())"
    "/property:PublishDir=$PublishDirectory"
)

& msbuild @arguments 2>&1 | Tee-Object -FilePath $logPath
$exitCode = $LASTEXITCODE
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
    $errorLines = @($logLines | Select-Object -Last 80)
}

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value "## MSBuild publish failure`n`n``````text"
    $errorLines | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value '```'
}

foreach ($line in $errorLines | Select-Object -Last 30) {
    $annotation = $line.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
    Write-Output "::error::$annotation"
}

exit $exitCode
