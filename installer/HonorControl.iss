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
UninstallDisplayIcon={app}\ui\HonorControl.exe
WizardStyle=modern
SetupLogging=yes

[Files]
Source: "..\artifacts\HonorControl-lightweight\*"; DestDir: "{app}\ui"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\HonorControl-service\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Install-Prerequisites.ps1"; Flags: dontcopy
Source: "Manage-Service.ps1"; DestDir: "{app}\installer"; Flags: ignoreversion
Source: "Manage-Service.ps1"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\Honor Control"; Filename: "{app}\ui\HonorControl.exe"
Name: "{autodesktop}\Honor Control"; Filename: "{app}\ui\HonorControl.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项："

[Run]
Filename: "{app}\ui\HonorControl.exe"; Description: "打开 Honor Control"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: IsServiceReady

[Code]
const
  DotNetUrl = 'https://builds.dotnet.microsoft.com/dotnet/Runtime/8.0.31/dotnet-runtime-8.0.31-win-x64.exe';
  WindowsAppUrl = 'https://aka.ms/windowsappsdk/2.4/2.4.0/windowsappruntimeinstall-x64.exe';
  DotNetFile = 'honorcontrol-dotnet-runtime-8.0.31-x64.exe';
  WindowsAppFile = 'honorcontrol-windowsappruntime-2.4-x64.exe';

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
  DotNetPath, WindowsAppPath: String;
  NeedDotNet, NeedWindowsApp: Boolean;
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
  WindowsAppPath := ExpandConstant('{tmp}\' + WindowsAppFile);
  DeleteFile(StatePath);
  ProgressPage.SetText('正在检查设备和运行依赖', '这一步可能需要一些时间。');
  ProgressPage.Show;
  ProgressPage.Animate;
  try
    if not RunPowerShell(PrerequisiteScript, '-Mode Check -StateFile ' + Quote(StatePath), ErrorPath) then
    begin
      Result := ReadError(ErrorPath, '设备或微软运行依赖检查失败。');
      Exit;
    end;
  finally
    ProgressPage.Hide;
  end;
  if not LoadStringsFromFile(StatePath, StateLines) or (GetArrayLength(StateLines) <> 2) then
  begin
    Result := '无法读取运行依赖检查结果。';
    Exit;
  end;
  NeedDotNet := StateLines[0] = 'DotNet=1';
  NeedWindowsApp := StateLines[1] = 'WindowsApp=1';
  if NeedDotNet or NeedWindowsApp then
  begin
    DownloadPage.Clear;
    if NeedDotNet then DownloadPage.Add(DotNetUrl, DotNetFile, '');
    if NeedWindowsApp then DownloadPage.Add(WindowsAppUrl, WindowsAppFile, '');
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
    ProgressPage.SetText('正在安装运行依赖', '安装微软运行依赖可能需要几分钟。');
    ProgressPage.Show;
    ProgressPage.Animate;
    try
      if not RunPowerShell(PrerequisiteScript,
        '-Mode Install -DotNetInstaller ' + Quote(DotNetPath) +
        ' -WindowsAppInstaller ' + Quote(WindowsAppPath) +
        ' -RestartFlag ' + Quote(ExpandConstant('{tmp}\HonorControl-RestartRequired.flag')), ErrorPath) then
      begin
        Result := ReadError(ErrorPath, '微软运行依赖安装失败。');
        Exit;
      end;
    finally
      ProgressPage.Hide;
      if NeedDotNet then DeleteFile(DotNetPath);
      if NeedWindowsApp then DeleteFile(WindowsAppPath);
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
  ErrorPath, ScriptPath, ServicePath: String;
begin
  if CurStep <> ssPostInstall then Exit;
  ErrorPath := ExpandConstant('{tmp}\HonorControl-ServiceError.txt');
  ScriptPath := ExpandConstant('{app}\installer\Manage-Service.ps1');
  ServicePath := ExpandConstant('{app}\service\HonorControl.Service.exe');
  if not RunPowerShell(ScriptPath, '-Mode Install -ServicePath ' + Quote(ServicePath), ErrorPath) then
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
