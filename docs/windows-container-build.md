# 使用 Windows 容器构建 HonorControl

> 历史记录：自 2026-09-29 起不再使用 Docker 构建，当前构建入口为 `.github/workflows/build-windows.yml`。本文仅保留既往验证记录，不作为当前环境安装指引。

本文记录已经在本机验证成功的 Windows 容器构建方式。容器用于还原依赖和发布 `win-x64` 应用；WinUI 界面、UAC、系统托盘以及荣耀 ACPI-WMI 功能仍需在宿主 Windows 上运行和调试。

## 前置条件

- Docker Desktop 已切换到 Windows containers。
- 宿主支持 Hyper-V isolation。
- 建议至少预留 20 GB 可用空间。基础镜像本身约 9.4 GB，Visual Studio Build Tools、SDK 和 NuGet 缓存还会继续占用空间。
- 在仓库根目录使用 PowerShell 执行以下命令。

本项目不能使用 Nano Server 镜像。已验证的基础镜像是：

```powershell
docker context use desktop-windows
docker pull mcr.microsoft.com/dotnet/framework/sdk:4.8-windowsservercore-ltsc2022
```

该镜像包含 Visual Studio 2022 MSBuild 17.14，但不包含项目固定使用的 .NET SDK 8.0.408，也不包含 WinUI XAML 编译需要的 MSVC 工具链，因此还需要完成下面的一次性准备。

## 创建构建容器和缓存

下面的容器只挂载两个持久缓存 volume。MSVC 安装在容器可写层中；源码和产物通过后文明确的停止、复制、启动流程传递。

```powershell
docker volume create honorcontrol-dotnet-8-0-408
docker volume create honorcontrol-nuget

docker run -d `
  --name honorcontrol-win-buildenv `
  --isolation=hyperv `
  --mount "type=volume,source=honorcontrol-dotnet-8-0-408,target=C:\dotnet8" `
  --mount "type=volume,source=honorcontrol-nuget,target=C:\Users\ContainerAdministrator\.nuget\packages" `
  mcr.microsoft.com/dotnet/framework/sdk:4.8-windowsservercore-ltsc2022 `
  powershell -NoLogo -NoProfile -Command 'while ($true) { Start-Sleep -Seconds 3600 }'
```

如果同名容器已经存在，先用 `docker ps -a --filter name=honorcontrol-win-buildenv` 检查状态。当前保留的已验证容器可以直接执行：

```powershell
docker start honorcontrol-win-buildenv
```

不要在未确认容器用途和缓存状态前直接删除已有容器或 volume。

## 安装 .NET SDK 8.0.408

SDK 安装在命名 volume 中，重建或重启容器后仍可复用：

```powershell
docker exec honorcontrol-win-buildenv powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -Command '
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
if (-not (Test-Path C:\dotnet8\sdk\8.0.408)) {
    New-Item -ItemType Directory -Path C:\TEMP -Force | Out-Null
    Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile C:\TEMP\dotnet-install.ps1
    & C:\TEMP\dotnet-install.ps1 -Version 8.0.408 -InstallDir C:\dotnet8 -Architecture x64 -NoPath
}
C:\dotnet8\dotnet.exe --list-sdks
'
```

期望输出中包含：

```text
8.0.408 [C:\dotnet8\sdk]
```

## 安装最小 MSVC 工具链

先下载安装器：

```powershell
docker exec honorcontrol-win-buildenv powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -Command '
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
New-Item -ItemType Directory -Path C:\TEMP -Force | Out-Null
Invoke-WebRequest https://aka.ms/vs/17/release/vs_buildtools.exe -OutFile C:\TEMP\vs_buildtools.exe
'
```

再由 `docker exec` 直接启动 bootstrapper，只安装 x64/x86 VC Tools：

```powershell
docker exec honorcontrol-win-buildenv `
  C:\TEMP\vs_buildtools.exe `
  --quiet --wait --norestart --nocache `
  --installPath C:\VCBuildTools `
  --add Microsoft.VisualStudio.Component.VC.Tools.x86.x64
```

使用无空格路径 `C:\VCBuildTools` 是刻意的。不要用 Windows PowerShell 5.1 的 `Start-Process -ArgumentList` 传递带空格的 `--installPath`；参数可能被错误拆分。

安装后检查：

```powershell
docker exec honorcontrol-win-buildenv powershell -NoLogo -NoProfile -Command `
  'Get-ChildItem C:\VCBuildTools\VC\Tools\MSVC -Directory | Select-Object -ExpandProperty FullName'
```

已验证版本为 `14.44.35207`。构建命令会动态选择目录名最大的已安装版本，不依赖这个固定值。

## 同步当前源码

Docker Desktop 不支持对正在运行的 Hyper-V Windows 容器执行 `docker cp`。每次构建前，先在运行中的容器里清理并创建专用工作目录，然后停止容器、复制源码并重新启动：

```powershell
docker start honorcontrol-win-buildenv

docker exec honorcontrol-win-buildenv powershell -NoLogo -NoProfile -Command '
foreach ($directory in @("C:\work", "C:\out")) {
    if (Test-Path $directory) {
        Remove-Item $directory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
'

docker stop honorcontrol-win-buildenv
docker cp .\global.json honorcontrol-win-buildenv:C:\work\global.json
docker cp .\src honorcontrol-win-buildenv:C:\work\src
docker cp .\tools honorcontrol-win-buildenv:C:\work\tools
docker start honorcontrol-win-buildenv
```

这里只复制实际构建需要的文件，不复制 `.git`、`research` 或旧的 `artifacts`。

## 发布轻量版和便携版

下面的命令读取刚复制到容器内 `C:\work` 的源码，并将两种发布结果写入容器内 `C:\out`。宿主源码不会被修改。

Windows Server Core 中，Windows SDK BuildTools 10.0.26100.4654 的 x64 `MakePri.exe` 和 `mt.exe` 会以 `0xC0000005` 崩溃。对应的 x86 工具可以正常生成架构无关的 PRI 和 manifest；应用本身仍按 `win-x64` 发布。

```powershell
@'
$ErrorActionPreference = 'Stop'
$env:DOTNET_ROOT = 'C:\dotnet8'
$env:Path = "C:\dotnet8;$env:Path"

$vcDirectory = Get-ChildItem C:\VCBuildTools\VC\Tools\MSVC -Directory |
    Sort-Object Name -Descending |
    Select-Object -First 1
if (-not $vcDirectory) {
    throw 'MSVC tools were not found.'
}

$vcTools = $vcDirectory.FullName + '\'
$sdkTools = 'C:\Users\ContainerAdministrator\.nuget\packages\microsoft.windows.sdk.buildtools\10.0.26100.4654\bin\10.0.26100.0\x86'
$makePri = Join-Path $sdkTools 'makepri.exe'
$manifestTool = Join-Path $sdkTools 'mt.exe'

Set-Location C:\work

msbuild src\HonorControl\HonorControl.csproj `
    /target:Restore `
    /property:Configuration=Release `
    /property:Platform=x64 `
    /property:RuntimeIdentifier=win-x64 `
    /property:SelfContained=true `
    /property:WindowsAppSDKSelfContained=true `
    "/property:VCToolsInstallDir=$vcTools"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

msbuild src\HonorControl\HonorControl.csproj `
    /target:Publish `
    /property:Configuration=Release `
    /property:Platform=x64 `
    /property:RuntimeIdentifier=win-x64 `
    /property:SelfContained=false `
    /property:WindowsAppSDKSelfContained=false `
    /property:PublishDir=C:\out\HonorControl-lightweight\ `
    "/property:VCToolsInstallDir=$vcTools" `
    "/property:MakePriExeFullPath=$makePri" `
    /property:MakePriArchitecture=x86 `
    "/property:ManifestTool=$manifestTool"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

msbuild src\HonorControl\HonorControl.csproj `
    /target:Publish `
    /property:Configuration=Release `
    /property:Platform=x64 `
    /property:RuntimeIdentifier=win-x64 `
    /property:SelfContained=true `
    /property:WindowsAppSDKSelfContained=true `
    /property:PublishDir=C:\out\HonorControl\ `
    "/property:VCToolsInstallDir=$vcTools" `
    "/property:MakePriExeFullPath=$makePri" `
    /property:MakePriArchitecture=x86 `
    "/property:ManifestTool=$manifestTool"
exit $LASTEXITCODE
'@ | docker exec -i honorcontrol-win-buildenv powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -Command -
```

必须先发布轻量版，再发布便携版，避免轻量版复用自包含发布留下的中间文件。

## 复制并验证产物

发布成功后先停止容器，再将 `C:\out` 中的结果复制回宿主。以下命令只会替换 `artifacts\docker-windows` 下的两个构建产物目录：

```powershell
$output = Join-Path (Resolve-Path .).Path 'artifacts\docker-windows'
New-Item -ItemType Directory -Path $output -Force | Out-Null

docker stop honorcontrol-win-buildenv

foreach ($directory in @('HonorControl', 'HonorControl-lightweight')) {
    $target = Join-Path $output $directory
    if (Test-Path $target) {
        Remove-Item $target -Recurse -Force
    }
}

docker cp honorcontrol-win-buildenv:C:\out\HonorControl (Join-Path $output 'HonorControl')
docker cp honorcontrol-win-buildenv:C:\out\HonorControl-lightweight (Join-Path $output 'HonorControl-lightweight')
```

成功后宿主应出现：

```text
artifacts/docker-windows/HonorControl/
artifacts/docker-windows/HonorControl-lightweight/
```

两种版本都必须包含：

- `HonorControl.exe`
- `HonorControl.pri`
- `Assets\HonorControl.ico`
- `Microsoft.Management.Infrastructure.dll`
- `Microsoft.Management.Infrastructure.Native.dll`
- `microsoft.management.infrastructure.native.unmanaged.dll`

便携版还必须包含 `Microsoft.UI.Xaml.dll`、`System.Private.CoreLib.dll` 和 `coreclr.dll`。轻量版必须包含 `Microsoft.WindowsAppRuntime.Bootstrap.dll` 与 `Microsoft.WindowsAppRuntime.Bootstrap.Net.dll`，且不应混入前述自包含运行时核心文件。

本次实际验证结果：

- 便携版：515 个文件，约 218.42 MB。
- 轻量版：53 个文件，约 77.96 MB。

## 本地运行和调试边界

容器只提供可复现的 Windows 编译环境，不用于运行 WinUI 桌面程序。完成发布后，应在宿主 Windows 上从 `artifacts\docker-windows` 启动目标版本进行调试。

容器构建成功只能证明 Restore、XAML/C# 编译、PRI 生成和 Publish 成功，不能证明以下运行时行为：

- WinUI 窗口、Mica 和系统托盘行为。
- UAC 提权及管理员进程生命周期。
- `root\wmi:OemWMIMethod`、荣耀 ACPI-WMI 通道和真实硬件读写。
- 目标电脑上的 .NET Desktop Runtime 或 Windows App Runtime 安装状态。

调试硬件写入时仍应遵守主 README 中的前置条件、确认流程和验证边界。

## 停止和复用

复制产物的步骤会让容器保持停止状态，以释放运行资源并保留已安装的 MSVC。下次构建时从“同步当前源码”一节重新启动并刷新工作目录。

如果只需要检查容器，可以手动启动：

```powershell
docker start honorcontrol-win-buildenv
```

如果容器被删除，需要重新安装 MSVC；`.NET SDK` 和 NuGet 缓存仍可从两个命名 volume 复用，除非这些 volume 也被显式删除。
