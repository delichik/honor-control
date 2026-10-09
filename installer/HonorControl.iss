#define AppVersion "1.0.0"

[Setup]
AppId={{4F1EA80C-EE95-4A22-8B44-8F7EE7294682}
AppName=Honor Control
AppVersion={#AppVersion}
AppPublisher=Honor Control
DefaultDirName={autopf}\Honor Control
DefaultGroupName=Honor Control
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
OutputDir=..\artifacts\installer
OutputBaseFilename=HonorControl-Setup-x64
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\panel\honor-control-panel.exe
WizardStyle=modern
SetupLogging=yes

[CustomMessages]
DesktopIconTask=创建桌面快捷方式
DesktopIconGroup=附加选项：
LaunchPanel=打开 Honor Control
ServiceFailedMessage=Honor Control 服务未能启动。
NeedDotNetMessage=安装前请先安装 .NET 8 运行时（x64）：%1

[Files]
; 三个进程：服务（SYSTEM，负责硬件）、托盘（用户会话，普通权限）、控制面板（用户会话，普通权限）。
; 需要管理员能力的地方只有服务注册，由本安装器直接调用系统自带的 sc.exe 完成。
Source: "..\artifacts\HonorControl-panel\*"; DestDir: "{app}\panel"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\HonorControl-tray\*"; DestDir: "{app}\tray"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\HonorControl-service\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Honor Control"; Filename: "{app}\panel\honor-control-panel.exe"
Name: "{autodesktop}\Honor Control"; Filename: "{app}\panel\honor-control-panel.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIconTask}"; GroupDescription: "{cm:DesktopIconGroup}"

[Run]
Filename: "{app}\panel\honor-control-panel.exe"; Description: "{cm:LaunchPanel}"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: IsServiceReady

[UninstallRun]
; 卸载前先结束托盘与面板，否则它们的可执行文件删不掉。taskkill 是系统自带工具。
Filename: "{sys}\taskkill.exe"; Parameters: "/IM HonorControl.Tray.exe /F"; Flags: runhidden; RunOnceId: "KillTray"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM honor-control-panel.exe /F"; Flags: runhidden; RunOnceId: "KillPanel"

[Code]
{ 安装流程里刻意不引入任何脚本宿主（PowerShell / WMI / schtasks），也不下载并执行任何外部程序：
  需要管理员能力的操作只通过系统自带的 sc.exe 与 Win32 文件 API 完成。

  两个理由：
  1. 杀软对"安装器 + powershell -ExecutionPolicy Bypass + WMI 查询"，以及"安装器偷偷下载并运行一个 exe"
     这两类组合都极其敏感；
  2. 这些逻辑本来就简单到不值得引入脚本引擎或下载器。

  .NET 8 运行时因此改为**前置条件**：缺失就给出官方链接并中止。
  只有服务需要它——控制面板是 Rust/Tauri（自带运行时 + 系统 WebView2）。 }

{ ------------------------------------------------------------------ 外部函数 }

{ 只保留三个最基础的外部声明：参数都是标量或字符串，没有结构体与指针，
  避免为了一点点便利引入难以验证的 ABI 细节。

  位置很重要：Pascal Script 是单趟编译，外部声明必须排在第一次调用之前，
  所以这一节放在 [Code] 最前面，而不是像通常那样挪到文件末尾。 }

function CreateFileW(lpFileName: String; dwDesiredAccess, dwShareMode: DWORD;
  lpSecurityAttributes: DWORD; dwCreationDisposition, dwFlagsAndAttributes: DWORD;
  hTemplateFile: THandle): THandle;
  external 'CreateFileW@kernel32.dll stdcall';

function CloseHandle(hObject: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

{ 刻意不叫 Sleep：Inno 自己可能已经提供同名函数，重名会直接编译失败。 }
procedure KernelSleep(dwMilliseconds: DWORD);
  external 'Sleep@kernel32.dll stdcall';

const
  ServiceName = 'HonorControlService';
  ServiceDisplayName = 'Honor Control Service';
  ServiceDescription = 'Maintains Honor hardware configuration and provides read-only device status.';
  PipeName = '\\.\pipe\HonorControl.Service.v1';
  DotNetUrl = 'https://dotnet.microsoft.com/download/dotnet/8.0';

  { CreateFileW 用到的常量自己定义：不去赌 Pascal Script 有没有预置
    GENERIC_READ / OPEN_EXISTING / INVALID_HANDLE_VALUE 这些名字。
    INVALID_HANDLE_VALUE 作为返回值就是 $FFFFFFFF。 }
  GenericRead = $80000000;
  GenericWrite = $40000000;
  OpenExisting = 3;
  InvalidHandle = $FFFFFFFF;

  { 服务的 DACL：SYSTEM 与管理员可以管理服务；交互式用户与服务账号仅可查询状态。
    SERVICE_START(RP) 与 SERVICE_STOP(WP) 都不授予普通用户，面板启停时会显式请求 UAC。
    写成固定值而不是"读取现有描述符再追加"：
    少一次外部调用，结果也可预测（这个服务本来就是本安装器创建的）。 }
  ServiceSddl = 'D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)';

var
  ServiceExisted: Boolean;
  ServiceStoppedBySetup: Boolean;
  BackupCreated: Boolean;
  ServiceReady: Boolean;

function IsServiceReady(): Boolean;
begin
  Result := ServiceReady;
end;

{ ------------------------------------------------------------------ 小工具 }

function RunTool(const FileName, Parameters: String; var ResultCode: Integer): Boolean;
begin
  Result := Exec(FileName, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

{ 调用系统自带的 sc.exe；返回退出码，启动进程本身失败时返回 -1。 }
function ScRun(const Parameters: String): Integer;
var
  Code: Integer;
begin
  if not RunTool(ExpandConstant('{sys}\sc.exe'), Parameters, Code) then
    Code := -1;
  Result := Code;
end;

function ServiceInstalled(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\' + ServiceName);
end;

function WindowsIsEleven(): Boolean;
var
  Build: String;
begin
  Result := RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Windows NT\CurrentVersion',
    'CurrentBuildNumber', Build) and (StrToIntDef(Build, 0) >= 22000);
end;

function MachineIsHonor(): Boolean;
var
  Manufacturer: String;
begin
  Result := RegQueryStringValue(HKLM, 'HARDWARE\DESCRIPTION\System\BIOS',
    'SystemManufacturer', Manufacturer) and (Pos('HONOR', Uppercase(Manufacturer)) > 0);
end;

function DotNet8Installed(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  { 8.0 的任一补丁版本都算：目录名形如 8.0.31。 }
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.NETCore.App\8.0.*'), FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

{ ------------------------------------------------------------------ 文件操作 }

procedure CopyTree(const Source, Destination: String);
var
  FindRec: TFindRec;
  SourcePath, DestinationPath: String;
begin
  if not DirExists(Source) then Exit;
  if not DirExists(Destination) then CreateDir(Destination);

  if FindFirst(AddBackslash(Source) + '*', FindRec) then
  begin
    try
      repeat
        { 不用 Continue：把过滤写成条件分支，避免依赖 Pascal Script 对它的支持程度。 }
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          SourcePath := AddBackslash(Source) + FindRec.Name;
          DestinationPath := AddBackslash(Destination) + FindRec.Name;
          if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
            CopyTree(SourcePath, DestinationPath)
          else
            { Inno 6 里 FileCopy 已更名为 CopyFile，用新名字避免编译告警。 }
            CopyFile(SourcePath, DestinationPath, False);
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure KillProcesses();
var
  Code: Integer;
begin
  { 升级/回滚前让托盘与面板退出，否则它们的可执行文件换不掉。 }
  RunTool(ExpandConstant('{sys}\taskkill.exe'), '/IM HonorControl.Tray.exe /F', Code);
  RunTool(ExpandConstant('{sys}\taskkill.exe'), '/IM honor-control-panel.exe /F', Code);
end;

{ ------------------------------------------------------------------ 服务 }

function ServicePipeAlive(): Boolean;
var
  PipeHandle: THandle;
begin
  { 直接开管道：服务在跑就一定开得成。比解析 sc query 的输出可靠得多。 }
  PipeHandle := CreateFileW(PipeName, GenericRead or GenericWrite, 0, 0, OpenExisting, 0, 0);
  Result := PipeHandle <> InvalidHandle;
  if Result then CloseHandle(PipeHandle);
end;

function WaitForServicePipe(Seconds: Integer): Boolean;
var
  Attempt: Integer;
begin
  Result := False;
  for Attempt := 1 to Seconds * 2 do
  begin
    if ServicePipeAlive() then
    begin
      Result := True;
      Exit;
    end;
    KernelSleep(500);
  end;
end;

procedure StopServiceIfRunning();
begin
  { 服务没在跑时 sc stop 返回非零，这里刻意忽略：本来就是要它停。 }
  ScRun('stop "' + ServiceName + '"');
end;

{ ------------------------------------------------------------------ 安装 }

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  InstallRoot, BackupRoot: String;
begin
  Result := '';
  NeedsRestart := False;

  { 设备预检只用注册表。64 位要求已经由 [Setup] 的 ArchitecturesAllowed/InstallIn64BitMode
    在启动时强制，这里不再重复判断，也就少一个"Pascal Script 是否提供该函数"的不确定性。
    真正需要荣耀 ACPI-WMI 通道的校验由服务在启动时自己做——安装器不发 WMI 查询：
    那既多余（服务会做），也是杀软最敏感的行为之一。 }
  if not WindowsIsEleven() then
  begin
    Result := 'Honor Control 需要 Windows 11（内部版本 22000 或更高）。';
    Exit;
  end;
  if not MachineIsHonor() then
  begin
    Result := '这台电脑不是受支持的荣耀设备：安装程序检测到的厂商不是 HONOR。';
    Exit;
  end;

  { .NET 8 是服务的前置条件。这里只检查、不下载：安装器不主动获取并执行外部程序。 }
  if not DotNet8Installed() then
  begin
    Result := FmtMessage(ExpandConstant('{cm:NeedDotNetMessage}'), [DotNetUrl]);
    Exit;
  end;

  { 升级：先让旧版本停下来并整目录备份，失败时回滚。 }
  ServiceExisted := ServiceInstalled();
  if ServiceExisted then
  begin
    KillProcesses();
    ServiceStoppedBySetup := True;
    StopServiceIfRunning();

    InstallRoot := ExpandConstant('{app}');
    BackupRoot := ExpandConstant('{tmp}\HonorControl-upgrade-backup');
    if DirExists(InstallRoot) then
    begin
      CopyTree(InstallRoot, BackupRoot);
      BackupCreated := DirExists(BackupRoot);
      if not BackupCreated then
      begin
        Result := '无法备份现有安装，已中止升级。';
        Exit;
      end;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
  ServicePath, BinPath: String;
begin
  if CurStep <> ssPostInstall then Exit;

  ServicePath := ExpandConstant('{app}\service\HonorControl.Service.exe');
  if not FileExists(ServicePath) then
    RaiseException('服务程序缺失：' + ServicePath);

  { sc.exe 要求 = 后面留一个空格；路径本身带引号，所以引号里再嵌一层。 }
  BinPath := 'binPath= "\"' + ServicePath + '\""';

  if ServiceExisted then
    Code := ScRun('config "' + ServiceName + '" ' + BinPath +
      ' start= delayed-auto DisplayName= "' + ServiceDisplayName + '"')
  else
    Code := ScRun('create "' + ServiceName + '" ' + BinPath +
      ' start= delayed-auto DisplayName= "' + ServiceDisplayName + '"');

  if Code <> 0 then
    RaiseException('注册 Honor Control 服务失败（sc.exe 退出码 ' + IntToStr(Code) + '）。');

  ScRun('description "' + ServiceName + '" "' + ServiceDescription + '"');
  ScRun('sdset "' + ServiceName + '" "' + ServiceSddl + '"');

  Code := ScRun('start "' + ServiceName + '"');
  { 1056 = 服务已在运行，1063 = 服务已启动（不同 Windows 版本的措辞）。 }
  if (Code <> 0) and (Code <> 1056) and (Code <> 1063) then
    RaiseException(ExpandConstant('{cm:ServiceFailedMessage}') +
      '（sc.exe 退出码 ' + IntToStr(Code) + '）。');

  if not WaitForServicePipe(30) then
    RaiseException(ExpandConstant('{cm:ServiceFailedMessage}') + #13#10 +
      '服务已注册，但通信管道在 30 秒内没有就绪。');

  ServiceReady := True;
  ServiceStoppedBySetup := False;
end;

procedure DeinitializeSetup();
var
  InstallRoot, BackupRoot: String;
begin
  { 安装中途失败时的回滚：还原备份并重新启动旧服务。 }
  if not ServiceStoppedBySetup then Exit;

  if BackupCreated and ServiceExisted then
  begin
    InstallRoot := ExpandConstant('{app}');
    BackupRoot := ExpandConstant('{tmp}\HonorControl-upgrade-backup');
    if DirExists(BackupRoot) then
    begin
      CopyTree(BackupRoot, InstallRoot);
      ScRun('start "' + ServiceName + '"');
    end;
  end
  else if not ServiceExisted then
  begin
    { 服务是本安装器这一次创建的：删掉它，恢复"从未安装"的状态。 }
    ScRun('delete "' + ServiceName + '"');
  end
  else
  begin
    { 服务本来就存在，但备份没做成（升级还没动到目录就中止了）：
      目录没被动过，只需要把旧服务重新拉起来，别把它留在停止状态。 }
    ScRun('start "' + ServiceName + '"');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usUninstall then Exit;
  StopServiceIfRunning();
  ScRun('delete "' + ServiceName + '"');
end;
