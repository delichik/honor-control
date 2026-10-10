//! Uses the installed, signed OEM components in the interactive user's session.
//! Display requests use the JSON IPC emitted by MonitorManage.exe. Audio requests
//! use the Senary C exports. OEM binaries are never copied or redistributed.
use serde_json::Value;
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};

const PRELUDE: &str = r#"
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
trap { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
$base=Join-Path $env:ProgramFiles 'HONOR'
$lcd=Join-Path $base 'HnLcdEnhancement'
$pc=Join-Path $base 'PCManager'
$reg='HKLM:\SOFTWARE\HONOR\PCManager\LCDEnhancement'
function Get-LcdValue($key) {
  try { return (Get-ItemPropertyValue -LiteralPath $reg -Name $key -ErrorAction Stop) } catch { return $null }
}
function Test-OemFile($path,$hash='') {
  if (!(Test-Path -LiteralPath $path -PathType Leaf)) { return '未安装所需荣耀组件。' }
  $sig=Get-AuthenticodeSignature -LiteralPath $path
  if ($sig.Status -ne 'Valid' -or !$sig.SignerCertificate -or $sig.SignerCertificate.Subject -notmatch 'O="?Honor Device Co\., Ltd\.') { return '荣耀组件签名无效或发布者不匹配，已禁用调用。' }
  if ($hash -and (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $hash) { return '该荣耀组件版本尚未验证接口，已禁用直接控制；可使用官方页面。' }
  return ''
}
$displayHash='1C943E630B0DA977AED4307BCDFB5D7EA4ADDD4ADB22CFD5F5B18A5AC5810D43'
$audioHash='2CEB760C8857BA2987625B44434233D718761BF3832B11C4529B64A934CA5357'
function Add-NativeBridge {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class HonorNativeBridge {
  [DllImport("kernel32", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
  [DllImport("kernel32", CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr dll,string name);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ReadEndpoint(IntPtr endpoint);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int WriteEndpoint(int value, IntPtr endpoint);
  [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet=CharSet.Ansi)] delegate int PostDisplay([MarshalAs(UnmanagedType.LPStr)] string json);
  public static IntPtr Load(string path) {
    IntPtr dll=LoadLibraryEx(path,IntPtr.Zero,0x1100);
    if(dll==IntPtr.Zero) throw new Exception("加载荣耀组件失败，Windows错误码："+Marshal.GetLastWin32Error());
    return dll;
  }
  static IntPtr Address(IntPtr dll,string name) {
    IntPtr p=GetProcAddress(dll,name);
    if(p==IntPtr.Zero)throw new Exception("荣耀组件缺少已验证接口："+name);
    return p;
  }
  public static int Read(IntPtr dll,string name) { return ((ReadEndpoint)Marshal.GetDelegateForFunctionPointer(Address(dll,name),typeof(ReadEndpoint)))(IntPtr.Zero); }
  public static int Write(IntPtr dll,string name,int value) { return ((WriteEndpoint)Marshal.GetDelegateForFunctionPointer(Address(dll,name),typeof(WriteEndpoint)))(value,IntPtr.Zero); }
  public static void Display(IntPtr dll,string json) {
    // This function's return register is overwritten during native teardown.
    // It is not an IPC acknowledgement and must not be used as success proof.
    ((PostDisplay)Marshal.GetDelegateForFunctionPointer(Address(dll,"PostHealthDisplayStateFromMonitor"),typeof(PostDisplay)))(json);
  }
}
'@
}
function Get-SenaryState($dll) {
  $caps=[HonorNativeBridge]::Read($dll,'GetCaptureAbilityByEndpoint')
  $mode=[HonorNativeBridge]::Read($dll,'GetCaptureCurrentMode')
  $renderCaps=[HonorNativeBridge]::Read($dll,'GetRenderAbilityByEndpoint')
  $render=[HonorNativeBridge]::Read($dll,'GetRenderStatus')
  return [ordered]@{
    captureAbility=$caps; captureMode=$mode; renderAbility=$renderCaps; renderStatus=$render
    # Restrict controls to capability profiles actually observed on this machine;
    # do not infer undocumented bit semantics from a numeric mask.
    microphoneControlAvailable=($caps -eq 8195 -and $mode -ge 0 -and $mode -le 2)
    callNoiseControlAvailable=($renderCaps -eq 16384 -and ($render -eq 0 -or $render -eq 1))
    stateSource='Senary SDK / 当前默认音频端点'; effectsVerified=$false
  }
}
"#;

const QUERY: &str = r#"
$trans=Join-Path $lcd 'MonitorManageTrans.dll'
$displayReason=Test-OemFile $trans $displayHash
$features=@()
foreach($item in @(
  @('eyeProtection','护眼模式','IsSupportEyeProtect','EyeProtectSwitch'),
  @('ebook','电子书模式','IsSupportEbookMode','EBookSwitch'),
  @('comfortDisplay','舒适显示','IsSupportComfortDisplay','ComfortDisplaySwitch'),
  @('defocusEye','离焦视力舒缓','IsSupportDefocusEyePrtect','DefocusEyeProtectSwitch'),
  @('naturalLight','类自然光','IsSupportNatualLight','NaturalLightSwitch'),
  @('colorTemperature','色温调节','IsSupportColorTemperatureAdjust','ColorTemperatureMode'),
  @('colorManagement','色彩管理','IsSupportColorManagement','ColorMode')
)) {
  $support=Get-LcdValue $item[2]; $value=Get-LcdValue $item[3]
  $known=$null -ne $support; $supported=if($known){[int]$support -eq 1}else{$null}
  $enabled=if($null -ne $value -and $item[0] -notin @('colorTemperature','colorManagement')){[int]$value -eq 1}else{$null}
  $reason=$displayReason
  if(!$known){$reason='官方组件未留下能力记录，支持状态未知。'}
  elseif(!$supported){$reason='官方组件记录本机不支持该功能。'}
  elseif($item[0] -eq 'colorTemperature'){$reason='可在官方健康显示页面调节；当前未接入多参数色温控制。'}
  elseif($item[0] -eq 'colorManagement'){$reason='色彩管理模式与逐屏校准参数尚未接入，请使用官方页面。'}
  elseif((Get-LcdValue 'HdrAcmEnabled') -eq 1){$reason='当前开启 HDR / ACM，请先在官方页面核对显示兼容性。'}
  elseif((Get-LcdValue 'UniqueDirectEnabled') -eq 1 -and $item[0] -in @('eyeProtection','ebook','comfortDisplay')){$reason='当前启用独显直连，官方健康显示控制受限。'}
  $features+=[ordered]@{id=$item[0];name=$item[1];supported=$supported;enabled=$enabled;savedValue=$value;controlAvailable=($known -and $supported -and !$reason);reason=$reason;stateSource='荣耀组件注册表记录（可能滞后）';effectsVerified=$false}
}
$audioPath=Join-Path $pc 'SenaryInterface.dll'
$audioReason=Test-OemFile $audioPath $audioHash
$audioState=$null
if(!$audioReason){try {Add-NativeBridge; $audioDll=[HonorNativeBridge]::Load($audioPath); $audioState=Get-SenaryState $audioDll} catch {$audioReason=$_.Exception.Message}}
$saved=Get-ItemProperty 'HKCU:\SOFTWARE\HONOR\PCManager\SmartAudio' -ErrorAction SilentlyContinue
if($audioState -and !$audioState.microphoneControlAvailable -and !$audioState.callNoiseControlAvailable){$audioReason='当前默认音频端点没有已验证可控制的 Senary 效果能力。'}
[ordered]@{
  display=[ordered]@{features=$features;displayType=(Get-LcdValue 'DisplayType');stateSource='荣耀组件注册表记录（可能滞后）';healthPageAvailable=(!(Test-OemFile (Join-Path $lcd 'MonitorManage.exe')));colorPageAvailable=((Get-LcdValue 'IsSupportColorManagement') -eq 1 -and !(Test-OemFile (Join-Path $lcd 'MonitorManage.exe')))}
  audio=[ordered]@{installed=(Test-Path -LiteralPath $audioPath);reason=$audioReason;live=$audioState;savedMicScene=$saved.MicScene;savedSpeakerEffect=$saved.SpeakerSoundEffect;savedCallNoise=$saved.CallNoiseStatus;savedStateSource='当前用户管家保存设置（不能证明已应用）';officialPageAvailable=(!(Test-OemFile (Join-Path $pc 'PCManager.exe')))}
} | ConvertTo-Json -Depth 8 -Compress
"#;

#[tauri::command(async)]
pub fn get_oem_features() -> Result<Value, String> {
    execute(QUERY)
}

#[tauri::command(async)]
pub fn oem_set_display(feature: String, enabled: bool) -> Result<Value, String> {
    let (support, key) = match feature.as_str() {
        "eyeProtection" => ("IsSupportEyeProtect", "EyeProtectSwitch"),
        "ebook" => ("IsSupportEbookMode", "EBookSwitch"),
        "comfortDisplay" => ("IsSupportComfortDisplay", "ComfortDisplaySwitch"),
        "defocusEye" => ("IsSupportDefocusEyePrtect", "DefocusEyeProtectSwitch"),
        "naturalLight" => ("IsSupportNatualLight", "NaturalLightSwitch"),
        _ => return Err("未接入该显示功能的直接控制，请使用官方页面。".into()),
    };
    let script = format!(r#"
$support='{support}'; $key='{key}'; $desired={desired}
$path=Join-Path $lcd 'MonitorManageTrans.dll'
$reason=Test-OemFile $path $displayHash; if($reason){{throw $reason}}
if((Get-LcdValue $support) -ne 1){{throw '官方组件未确认本机支持该显示功能。'}}
if((Get-LcdValue 'HdrAcmEnabled') -eq 1){{throw 'HDR / ACM 开启时暂不允许直接切换健康显示，请使用官方页面。'}}
if((Get-LcdValue 'UniqueDirectEnabled') -eq 1 -and $key -in @('EyeProtectSwitch','EBookSwitch','ComfortDisplaySwitch')){{throw '当前独显直连状态限制该健康显示功能，请使用官方页面。'}}
$before=Get-LcdValue $key
if($null -eq $before){{throw '无法读取官方组件原设置，未发送切换请求。'}}
if([int]$before -eq $desired){{[ordered]@{{changed=$false;submitted=$false;settingsConfirmed=$true;effectsVerified=$false;enabled=($desired -eq 1);stateSource='荣耀组件注册表记录';message='官方保存设置已经是该状态，未发送切换请求；显示效果未验证。'}} | ConvertTo-Json -Compress;exit}}
Add-NativeBridge
$dll=[HonorNativeBridge]::Load($path)
$json=@{{action='1';params=@{{key=$key;value=[string]$desired}}}} | ConvertTo-Json -Depth 3 -Compress
[HonorNativeBridge]::Display($dll,$json)
$after=$null
for($i=0;$i -lt 20;$i++){{Start-Sleep -Milliseconds 100;$after=Get-LcdValue $key;if($null -ne $after -and [int]$after -eq $desired){{break}}}}
$confirmed=$null -ne $after -and [int]$after -eq $desired
$message=if($confirmed){{'已请求官方组件切换，保存设置已回读；显示像素效果未验证。'}}else{{'已调用官方切换接口，但尚未确认保存设置更新；请打开官方页面核对。'}}
[ordered]@{{changed=$confirmed;submitted=$true;settingsConfirmed=$confirmed;effectsVerified=$false;before=$before;savedValue=$after;enabled=if($null -ne $after){{[int]$after -eq 1}}else{{$null}};stateSource='荣耀组件注册表记录';message=$message}} | ConvertTo-Json -Compress
"#, desired = if enabled { 1 } else { 0 });
    execute(&script)
}

/// Applies only to the currently selected default endpoint, not every connected
/// microphone/headphone. No recording, voice registration or VoiceID APIs exist here.
#[tauri::command(async)]
pub fn oem_set_audio(feature: String, value: String) -> Result<Value, String> {
    let (setter, getter, requested, capability) = match (feature.as_str(), value.as_str()) {
        ("microphoneScene", "bypass") => ("SetCaptureEffectMode", "GetCaptureCurrentMode", 0, "microphoneControlAvailable"),
        ("microphoneScene", "multiplayer") => ("SetCaptureEffectMode", "GetCaptureCurrentMode", 1, "microphoneControlAvailable"),
        ("microphoneScene", "single") => ("SetCaptureEffectMode", "GetCaptureCurrentMode", 2, "microphoneControlAvailable"),
        ("callNoiseReduction", "off") => ("SetRenderStatus", "GetRenderStatus", 0, "callNoiseControlAvailable"),
        ("callNoiseReduction", "on") => ("SetRenderStatus", "GetRenderStatus", 1, "callNoiseControlAvailable"),
        _ => return Err("未接入该音频功能或场景，请使用官方智慧音频页面。".into()),
    };
    execute(&format!(r#"
$path=Join-Path $pc 'SenaryInterface.dll'
$reason=Test-OemFile $path $audioHash; if($reason){{throw $reason}}
Add-NativeBridge
$dll=[HonorNativeBridge]::Load($path)
$state=Get-SenaryState $dll
if(!$state.{capability}){{throw '当前默认音频端点未报告该已验证 Senary 效果能力，未执行切换。'}}
$before=[HonorNativeBridge]::Read($dll,'{getter}')
if($before -eq {requested}){{[ordered]@{{changed=$false;submitted=$false;settingsConfirmed=$true;effectsVerified=$false;before=$before;actual=$before;stateSource='Senary SDK / 当前默认音频端点';message='当前默认音频端点已处于该场景，未重复调用；声学效果未验证。'}} | ConvertTo-Json -Compress;exit}}
$result=[HonorNativeBridge]::Write($dll,'{setter}',{requested})
if($result -ne 1){{throw ('Senary 接口拒绝切换，返回码：'+$result)}}
$after=[HonorNativeBridge]::Read($dll,'{getter}')
if($after -ne {requested}){{throw ('Senary 已接受请求但场景回读不一致，期望 {requested}，实际 '+$after+'；请刷新并核对默认端点。')}}
[ordered]@{{changed=$true;submitted=$true;settingsConfirmed=$true;effectsVerified=$false;before=$before;actual=$after;stateSource='Senary SDK / 当前默认音频端点';message='当前默认音频端点场景已回读确认；实际降噪效果未做录音验证。'}} | ConvertTo-Json -Compress
"#))
}

#[tauri::command(async)]
pub fn open_oem_page(page: String) -> Result<Value, String> {
    let (relative, argument, note) = match page.as_str() {
        "healthDisplay" => ("HnLcdEnhancement\\MonitorManage.exe", "", "已打开荣耀官方健康显示窗口。"),
        "colorManagement" => ("HnLcdEnhancement\\MonitorManage.exe", "ColorManageOpen=1", "已打开荣耀官方色彩管理窗口。"),
        // The audio UI is a PCManager plugin. No verified standalone command-line
        // page selector exists; explicitly open the manager instead of pretending.
        "smartAudio" => ("PCManager\\PCManager.exe", "", "已打开荣耀电脑管家，请在其中进入智慧音频。"),
        _ => return Err("未知荣耀官方页面。".into()),
    };
    execute(&format!(r#"
$path=Join-Path $base '{relative}'
$reason=Test-OemFile $path; if($reason){{throw $reason}}
{guard}
$info=[Diagnostics.ProcessStartInfo]::new()
$info.FileName=$path; $info.WorkingDirectory=Split-Path -Parent $path
$info.Arguments='{argument}'; $info.UseShellExecute=$true
$process=[Diagnostics.Process]::Start($info)
[ordered]@{{opened=$true;message='{note}'}} | ConvertTo-Json -Compress
"#, guard = if page == "colorManagement" { "if((Get-LcdValue 'IsSupportColorManagement') -ne 1){throw '官方组件记录本机不支持色彩管理。'}" } else { "" }))
}

#[cfg(windows)]
fn execute(script: &str) -> Result<Value, String> {
    use std::os::windows::process::CommandExt;
    let encoded = encoded_command(&format!("{PRELUDE}\n{script}"));
    // OEM native libraries stay in a short-lived helper process, so their COM,
    // IPC callbacks and DLL-global state cannot corrupt the Tauri process.
    let windows = std::env::var_os("SystemRoot").ok_or("无法定位 Windows 系统目录。")?;
    let executable = std::path::PathBuf::from(windows)
        .join("System32\\WindowsPowerShell\\v1.0\\powershell.exe");
    let mut child = Command::new(executable)
        // The panel can be launched by PowerShell 7. Its inherited module path
        // loads incompatible type metadata in the Windows PowerShell 5 helper.
        .env_remove("PSModulePath")
        .args(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", &encoded])
        .creation_flags(0x08000000)
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .map_err(|error| format!("启动荣耀组件调用进程失败：{error}"))?;
    // Drain both pipes while polling; a native diagnostic must not fill an
    // anonymous pipe and make the child block before it can exit.
    let stdout = capture_output(child.stdout.take().ok_or("无法读取荣耀组件标准输出。")?);
    let stderr = capture_output(child.stderr.take().ok_or("无法读取荣耀组件错误输出。")?);
    let start = Instant::now();
    loop {
        if child.try_wait().map_err(|error| format!("读取荣耀组件调用状态失败：{error}"))?.is_some() { break; }
        if start.elapsed() > Duration::from_secs(12) {
            let _ = child.kill();
            let _ = child.wait();
            return Err("荣耀组件调用超过 12 秒；若已请求切换，请刷新状态核对结果。".into());
        }
        std::thread::sleep(Duration::from_millis(40));
    }
    let status = child.wait().map_err(|error| format!("读取荣耀组件结果失败：{error}"))?;
    let out = stdout.join().map_err(|_| "荣耀组件输出读取线程异常。")?
        .map_err(|error| format!("读取荣耀组件结果失败：{error}"))?;
    let err = stderr.join().map_err(|_| "荣耀组件错误读取线程异常。")?
        .map_err(|error| format!("读取荣耀组件错误失败：{error}"))?;
    if !status.success() {
        let text = String::from_utf8_lossy(&err);
        return Err(format!("荣耀组件调用失败：{}", text.trim().chars().take(1600).collect::<String>()));
    }
    serde_json::from_slice(&out).map_err(|error| format!("荣耀组件响应不是有效 JSON：{error}"))
}

#[cfg(windows)]
fn capture_output(mut pipe: impl std::io::Read + Send + 'static) -> std::thread::JoinHandle<std::io::Result<Vec<u8>>> {
    std::thread::spawn(move || {
        let mut result = Vec::new();
        let mut buffer = [0u8; 4096];
        loop {
            let count = pipe.read(&mut buffer)?;
            if count == 0 { return Ok(result); }
            // Continue draining excess diagnostics but bound retained memory.
            let remaining = 64 * 1024usize - result.len();
            result.extend_from_slice(&buffer[..count.min(remaining)]);
        }
    })
}

#[cfg(not(windows))]
fn execute(_: &str) -> Result<Value, String> {
    Err("荣耀显示与音频组件仅可在 Windows 上使用。".into())
}

fn encoded_command(script: &str) -> String {
    const ALPHABET: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let bytes: Vec<u8> = script.encode_utf16().flat_map(u16::to_le_bytes).collect();
    let mut output = String::with_capacity((bytes.len() + 2) / 3 * 4);
    for chunk in bytes.chunks(3) {
        let a = chunk[0]; let b = *chunk.get(1).unwrap_or(&0); let c = *chunk.get(2).unwrap_or(&0);
        output.push(ALPHABET[(a >> 2) as usize] as char);
        output.push(ALPHABET[(((a & 3) << 4) | (b >> 4)) as usize] as char);
        output.push(if chunk.len() > 1 { ALPHABET[(((b & 15) << 2) | (c >> 6)) as usize] as char } else { '=' });
        output.push(if chunk.len() > 2 { ALPHABET[(c & 63) as usize] as char } else { '=' });
    }
    output
}

#[cfg(test)]
mod tests {
    use super::encoded_command;

    // Reference vectors independently produced by .NET Unicode (UTF-16LE)
    // encoding and Convert.ToBase64String, as required by -EncodedCommand.
    #[test]
    fn powershell_encoding_handles_empty_text_and_base64_padding() {
        assert_eq!(encoded_command(""), "");
        assert_eq!(encoded_command("a"), "YQA=");
        assert_eq!(encoded_command("abc"), "YQBiAGMA");
    }

    #[test]
    fn powershell_encoding_preserves_chinese_and_mixed_quotes() {
        assert_eq!(encoded_command("Write-Output \"健康 '显示'\""),
            "VwByAGkAdABlAC0ATwB1AHQAcAB1AHQAIAAiAGVQt14gACcAPmY6eScAIgA=");
    }

    #[test]
    fn powershell_encoding_preserves_utf16_surrogate_pairs() {
        assert_eq!(encoded_command("Write-Output '😀'"),
            "VwByAGkAdABlAC0ATwB1AHQAcAB1AHQAIAAnAD3YAN4nAA==");
    }
}
