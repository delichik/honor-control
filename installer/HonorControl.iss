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

[Files]
; 三个进程：服务（SYSTEM，负责硬件）、托盘（用户会话，普通权限）、控制面板（用户会话，普通权限）。
; 托盘与控制面板都不申请提权；需要管理员的地方只有服务注册与计划任务注册（都在本安装器里完成）。
Source: "..\artifacts\HonorControl-panel\*"; DestDir: "{app}\panel"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\HonorControl-tray\*"; DestDir: "{app}\tray"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\HonorControl-service\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Install-Prerequisites.ps1"; Flags: dontcopy
Source: "Manage-Service.ps1"; DestDir: "{app}\installer"; Flags: ignoreversion
Source: "Manage-Service.ps1"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\Honor Control"; Filename: "{app}\panel\honor-control-panel.exe"
Name: "{autodesktop}\Honor Control"; Filename: "{app}\panel\honor-control-panel.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项："

[Run]
Filename: "{app}\panel\honor-control-panel.exe"; Description: "打开 Honor Control"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: IsServiceReady

[Code]
const
  DotNetUrl = 'https://builds.dotnet.microsoft.com/dotnet/Runtime/8.0.31/dotnet-runtime-8.0.31-win-x64.exe';
  DotNetFile = 'honorcontrol-dotnet-runtime-8.0.31-x64.exe';

var
  ServiceStoppedForUpgrade: Boolean;
  ExistingService: Boolean;
  UpgradeBackupCreated: Boolean;
  ServiceReady: Boolean;
  DownloadPage: TDownloadWizardPage;
  ProgressPage: TOutputMarqueeProgressWizardPage;

function IsServiceReady(): Boolean;
begin
  Result := ServiceReady;
end;

procedure InitializeWizard();
begin
  DownloadPage := CreateDownloadPage('下载运行依赖', '仅下载此电脑缺少的微软运行依赖。', nil);
  ProgressPage := CreateOutputMarqueeProgressPage('检查安装条件', '正在验证设备和运行环境。');
end;

function Quote(const Text: String): String;
begin
  Result := '"' + Text + '"';
end;

function ReadError(const ErrorPath, Fallback: String): String;
var
  Lines: TArrayOfString;
begin
  Result := Fallback;
  if LoadStringsFromFile(ErrorPath, Lines) then
    if GetArrayLength(Lines) > 0 then Result := Lines[0];
end;

function RunPowerShell(const ScriptPath, Arguments, ErrorPath: String): Boolean;
var
  ExitCode: Integer;
begin
  DeleteFile(ErrorPath);
  ExitCode := -1;
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ' + Quote(ScriptPath) +
    ' ' + Arguments + ' -ErrorFile ' + Quote(ErrorPath), '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Result := Result and (ExitCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ErrorPath, PrerequisiteScript, ServiceScript, ServicePath, StatePath: String;
  DotNetPath: String;
  NeedDotNet: Boolean;
  StateLines: TArrayOfString;
begin
  Result := '';
  ExtractTemporaryFile('Install-Prerequisites.ps1');
  ExtractTemporaryFile('Manage-Service.ps1');
  ErrorPath := ExpandConstant('{tmp}\HonorControl-InstallError.txt');
  PrerequisiteScript := ExpandConstant('{tmp}\Install-Prerequisites.ps1');
  ServiceScript := ExpandConstant('{tmp}\Manage-Service.ps1');
  ServicePath := ExpandConstant('{app}\service\HonorControl.Service.exe');
  StatePath := ExpandConstant('{tmp}\HonorControl-PrerequisiteState.txt');
  DotNetPath := ExpandConstant('{tmp}\' + DotNetFile);
  DeleteFile(StatePath);
  ProgressPage.SetText('正在检查设备和运行依赖', '这一步可能需要一些时间。');
  ProgressPage.Show;
  ProgressPage.Animate;
  try
    if not RunPowerShell(PrerequisiteScript, '-Mode Check -StateFile ' + Quote(StatePath), ErrorPath) then
    begin
      Result := ReadError(ErrorPath, '设备或运行依赖检查失败。');
      Exit;
    end;
  finally
    ProgressPage.Hide;
  end;
  if not LoadStringsFromFile(StatePath, StateLines) or (GetArrayLength(StateLines) <> 1) then
  begin
    Result := '无法读取运行依赖检查结果。';
    Exit;
  end;
  NeedDotNet := StateLines[0] = 'DotNet=1';
  // 只有服务需要 .NET 运行时：控制面板是 Rust/Tauri（自带运行时 + 系统 WebView2），
  // 不再需要 Windows App Runtime，安装包因此少一个下载分支。
  if NeedDotNet then
  begin
    DownloadPage.Clear;
    DownloadPage.Add(DotNetUrl, DotNetFile, '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        Result := '下载微软运行依赖失败：' + GetExceptionMessage;
        Exit;
      end;
    finally
      DownloadPage.Hide;
    end;
    ProgressPage.SetText('正在安装运行依赖', '安装 .NET 运行时可能需要几分钟。');
    ProgressPage.Show;
    ProgressPage.Animate;
    try
      if not RunPowerShell(PrerequisiteScript,
        '-Mode Install -DotNetInstaller ' + Quote(DotNetPath) +
        ' -RestartFlag ' + Quote(ExpandConstant('{tmp}\HonorControl-RestartRequired.flag')), ErrorPath) then
      begin
        Result := ReadError(ErrorPath, '微软运行依赖安装失败。');
        Exit;
      end;
    finally
      ProgressPage.Hide;
      DeleteFile(DotNetPath);
    end;
  end;
  ExistingService := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\HonorControlService');
  if not RunPowerShell(ServiceScript, '-Mode Stop -ServicePath ' + Quote(ServicePath), ErrorPath) then
  begin
    Result := ReadError(ErrorPath, '无法停止旧版 Honor Control 服务。');
    Exit;
  end;
  ServiceStoppedForUpgrade := True;
  if ExistingService then
  begin
    if not RunPowerShell(ServiceScript,
      '-Mode Backup -InstallDir ' + Quote(ExpandConstant('{app}')) +
      ' -BackupPath ' + Quote(ExpandConstant('{tmp}\HonorControl-upgrade-backup')), ErrorPath) then
    begin
      Result := ReadError(ErrorPath, '无法备份旧版 Honor Control。');
      Exit;
    end;
    UpgradeBackupCreated := True;
  end;
  NeedsRestart := FileExists(ExpandConstant('{tmp}\HonorControl-RestartRequired.flag'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorPath, ScriptPath, ServicePath, TrayPath, Arguments: String;
begin
  if CurStep <> ssPostInstall then Exit;
  ErrorPath := ExpandConstant('{tmp}\HonorControl-ServiceError.txt');
  ScriptPath := ExpandConstant('{app}\installer\Manage-Service.ps1');
  ServicePath := ExpandConstant('{app}\service\HonorControl.Service.exe');
  TrayPath := ExpandConstant('{app}\tray\HonorControl.Tray.exe');
  // TrayPath：注册托盘的登录任务（默认禁用，运行期由服务按策略启用）。
  // TaskUser：把任务登记给运行安装器的用户（{username} 是原始用户，不是提权后的管理员），
  //           他最可能就是配置拥有者——也就是之后第一次打开面板的人。
  //           如果之后换成另一个用户登录，Always 策略会退化成"由面板拉起"，不会报错。
  Arguments := '-Mode Install -ServicePath ' + Quote(ServicePath) +
    ' -TrayPath ' + Quote(TrayPath) +
    ' -TaskUser ' + Quote(ExpandConstant('{username}'));
  if not RunPowerShell(ScriptPath, Arguments, ErrorPath) then
    RaiseException(ReadError(ErrorPath, 'Honor Control 服务安装失败。'));
  ServiceReady := True;
  ServiceStoppedForUpgrade := False;
end;

procedure DeinitializeSetup();
var
  ErrorPath, ScriptPath, ServicePath, Arguments: String;
begin
  if not ServiceStoppedForUpgrade then Exit;
  ScriptPath := ExpandConstant('{tmp}\Manage-Service.ps1');
  ErrorPath := ExpandConstant('{tmp}\HonorControl-RestoreServiceError.txt');
  if not FileExists(ScriptPath) then Exit;
  if ExistingService then
  begin
    if UpgradeBackupCreated then
    begin
      ServicePath := ExpandConstant('{app}\service\HonorControl.Service.exe');
      Arguments := '-Mode Restore -ServicePath ' + Quote(ServicePath) +
        ' -InstallDir ' + Quote(ExpandConstant('{app}')) +
        ' -BackupPath ' + Quote(ExpandConstant('{tmp}\HonorControl-upgrade-backup'));
    end
    else Arguments := '-Mode Start';
  end
  else Arguments := '-Mode Uninstall';
  if not RunPowerShell(ScriptPath, Arguments, ErrorPath) then
    MsgBox(ReadError(ErrorPath, '未能恢复旧版 Honor Control 服务，请重新运行安装器修复。'), mbError, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ErrorPath, ScriptPath: String;
begin
  if CurUninstallStep <> usUninstall then Exit;
  ScriptPath := ExpandConstant('{app}\installer\Manage-Service.ps1');
  ErrorPath := ExpandConstant('{tmp}\HonorControl-UninstallError.txt');
  if FileExists(ScriptPath) and not RunPowerShell(ScriptPath, '-Mode Uninstall', ErrorPath) then
    RaiseException(ReadError(ErrorPath, '无法移除 Honor Control 服务。'));
end;
