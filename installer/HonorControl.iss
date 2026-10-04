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
DotNetMissingMessage=Honor Control 需要 .NET 8 运行时，安装程序将从微软官方下载并校验数字签名。
ServiceFailedMessage=Honor Control 服务未能启动。
DesktopIconTask=创建桌面快捷方式
DesktopIconGroup=附加选项：
LaunchPanel=打开 Honor Control

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
{ 安装流程里刻意不引入任何脚本宿主（PowerShell / WMI / schtasks）：
  需要管理员能力的操作只通过系统自带的 sc.exe 与 Win32 文件 API 完成。
  两个理由：杀软对"安装器 + powershell -ExecutionPolicy Bypass + WMI 查询"的组合极其敏感；
  而这些逻辑本来就简单到不值得引入一整台脚本引擎。 }

const
  ServiceName = 'HonorControlService';
  ServiceDisplayName = 'Honor Control Service';
  ServiceDescription = 'Maintains Honor hardware configuration and provides read-only device status.';
  PipeName = '\\.\pipe\HonorControl.Service.v1';
  RuntimeUrl = 'https://builds.dotnet.microsoft.com/dotnet/Runtime/8.0.31/dotnet-runtime-8.0.31-win-x64.exe';
  RuntimeFile = 'honorcontrol-dotnet-runtime-8.0.31-x64.exe';

  { 服务的 DACL：Windows 默认服务权限 + 一条给交互式用户的 SERVICE_START(RP)。
    刻意不含 SERVICE_STOP(WP)——托盘菜单的"退出"是经管道请求服务自己停止，
    普通用户不该能直接停服务。写成固定值而不是"读取现有描述符再追加"：
    少一次外部调用，结果也可预测（这个服务本来就是本安装器创建的）。 }
  ServiceSddl = 'D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWRPWPDTLOCRRC;;;IU)(A;;CCLCSWRPWPDTLOCRRC;;;SU)(A;;RP;;;IU)';

  { WinVerifyTrust：只判断"是否由受信任发布者签名"，不做联网吊销检查。 }
  WINTRUST_ACTION_GENERIC_VERIFY_V2 = '{00AAC56B-CD44-11D0-8CC2-00C04FC295EE}';
  WTD_UI_NONE = 2;
  WTD_REVOKE_NONE = 0;
  WTD_CHOICE_FILE = 1;
  WTD_STATEACTION_VERIFY = 1;
  WTD_STATEACTION_CLOSE = 2;
  WTD_REVOCATION_CHECK_NONE = $00000010;
  WTD_CACHE_ONLY_URL_RETRIEVAL = $00001000;
  WTD_SAFER_FLAG = $00000100;

type
  TWinTrustFileInfo = record
    cbStruct: DWORD;
    pcwszFilePath: String;
    hFile: THandle;
    pgKnownSubject: Pointer;
  end;

  TWinTrustData = record
    cbStruct: DWORD;
    pPolicyCallbackData: Pointer;
    pSIPClientData: Pointer;
    dwUIChoice: DWORD;
    fdwRevocationChecks: DWORD;
    dwUnionChoice: DWORD;
    pFile: ^TWinTrustFileInfo;
    dwStateAction: DWORD;
    hWVTStateData: THandle;
    pwszURLReference: String;
    dwProvFlags: DWORD;
    dwUIContext: DWORD;
  end;

var
  RuntimePage: TDownloadWizardPage;
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
        if (FindRec.Name = '.') or (FindRec.Name = '..') then Continue;
        SourcePath := AddBackslash(Source) + FindRec.Name;
        DestinationPath := AddBackslash(Destination) + FindRec.Name;
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          CopyTree(SourcePath, DestinationPath)
        else
          FileCopy(SourcePath, DestinationPath, False);
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
  Handle: THandle;
begin
  { 直接开管道：服务在跑就一定开得成。比解析 sc query 的输出可靠得多。 }
  Handle := CreateFileW(PipeName, GENERIC_READ or GENERIC_WRITE, 0, nil, OPEN_EXISTING, 0, 0);
  Result := Handle <> INVALID_HANDLE_VALUE;
  if Result then CloseHandle(Handle);
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

{ ------------------------------------------------------------------ 数字签名 }

function VerifyAuthenticode(const FileName: String): Boolean;
var
  Action: TGUID;
  FileInfo: TWinTrustFileInfo;
  Data: TWinTrustData;
  Status: Longint;
begin
  Result := False;
  Action := StringToGUID(WINTRUST_ACTION_GENERIC_VERIFY_V2);

  FileInfo.cbStruct := SizeOf(FileInfo);
  FileInfo.pcwszFilePath := FileName;
  FileInfo.hFile := 0;
  FileInfo.pgKnownSubject := nil;

  Data.cbStruct := SizeOf(Data);
  Data.pPolicyCallbackData := nil;
  Data.pSIPClientData := nil;
  Data.dwUIChoice := WTD_UI_NONE;
  Data.fdwRevocationChecks := WTD_REVOKE_NONE;
  Data.dwUnionChoice := WTD_CHOICE_FILE;
  Data.pFile := @FileInfo;
  Data.dwStateAction := WTD_STATEACTION_VERIFY;
  Data.hWVTStateData := 0;
  Data.pwszURLReference := '';
  { 不做联网吊销检查：装机环境可能没有外网，而这里只需要"发布者可信任"。
    SAFER 标志要求文件确实有可信签名，正是我们要的。 }
  Data.dwProvFlags := WTD_REVOCATION_CHECK_NONE or WTD_CACHE_ONLY_URL_RETRIEVAL or WTD_SAFER_FLAG;
  Data.dwUIContext := 0;

  Status := WinVerifyTrust(0, @Action, @Data);
  Result := Status = 0;

  { 释放状态数据，否则会泄漏。 }
  Data.dwStateAction := WTD_STATEACTION_CLOSE;
  WinVerifyTrust(0, @Action, @Data);
end;

{ ------------------------------------------------------------------ 向导与安装 }

procedure InitializeWizard();
begin
  RuntimePage := CreateDownloadPage('准备安装', ExpandConstant('{cm:DotNetMissingMessage}'), nil);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  InstallRoot, BackupRoot, RuntimePath: String;
begin
  Result := '';
  NeedsRestart := False;

  { 设备预检只用注册表。真正需要荣耀 ACPI-WMI 通道的校验由服务在启动时自己做——
    安装器不再从脚本里发起 WMI 查询：那既多余（服务会做），也是杀软最敏感的行为之一。 }
  if not IsWin64 then
  begin
    Result := 'Honor Control 需要 64 位 Windows 11。';
    Exit;
  end;
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

  { .NET 8：只有服务需要它（面板是 Rust/Tauri，自带运行时 + 系统 WebView2）。
    缺失时从微软官方下载，且必须先通过 Authenticode 校验才会执行。 }
  if not DotNet8Installed() then
  begin
    RuntimePage.Clear;
    RuntimePage.Add(RuntimeUrl, RuntimeFile, '');
    RuntimePage.Show;
    try
      try
        RuntimePage.Download;
      except
        Result := '下载 .NET 8 运行时失败：' + GetExceptionMessage;
        Exit;
      end;
    finally
      RuntimePage.Hide;
    end;

    RuntimePath := ExpandConstant('{tmp}\' + RuntimeFile);
    if not VerifyAuthenticode(RuntimePath) then
    begin
      DeleteFile(RuntimePath);
      Result := '.NET 8 运行时的数字签名校验失败，已中止安装。';
      Exit;
    end;

    if not RunTool(RuntimePath, '/install /quiet /norestart', Code) then
    begin
      Result := '无法启动 .NET 8 运行时安装程序。';
      Exit;
    end;
    if not (Code in [0, 3010]) then
    begin
      Result := '.NET 8 运行时安装失败，退出码 ' + IntToStr(Code) + '。';
      Exit;
    end;
    if Code = 3010 then NeedsRestart := True;
    DeleteFile(RuntimePath);

    if not DotNet8Installed() then
    begin
      Result := '.NET 8 运行时安装后仍然不可用。';
      Exit;
    end;
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
  { 1056 = 已在运行，1063 = 服务已在运行（不同 Windows 版本的措辞）。 }
  if not (Code in [0, 1056, 1063]) then
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
    ScRun('delete "' + ServiceName + '"');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usUninstall then Exit;
  StopServiceIfRunning();
  ScRun('delete "' + ServiceName + '"');
end;

{ ------------------------------------------------------------------ 外部函数 }

function CreateFileW(lpFileName: String; dwDesiredAccess, dwShareMode: DWORD;
  lpSecurityAttributes: Pointer; dwCreationDisposition, dwFlagsAndAttributes: DWORD;
  hTemplateFile: THandle): THandle;
  external 'CreateFileW@kernel32.dll stdcall';

function CloseHandle(hObject: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

{ 刻意不叫 Sleep：Inno 自己可能已经提供了同名函数，重名会直接编译失败。 }
procedure KernelSleep(dwMilliseconds: DWORD);
  external 'Sleep@kernel32.dll stdcall';

function WinVerifyTrust(hwnd: HWND; pgActionID: Pointer; pWVTData: Pointer): Longint;
  external 'WinVerifyTrust@wintrust.dll stdcall';
