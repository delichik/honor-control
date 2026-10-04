// Honor Control 控制面板：Tauri v2 外壳。
//
// 职责边界：本进程把前端的 invoke 转发给 Honor Control Windows 服务（LocalSystem，.NET 8），
// 并通过 SCM 启动该服务。托盘图标是独立的 C# 进程，本 crate 不涉及托盘。
//
// 生产构建下不显示控制台窗口（服务不可用时前端会拿到中文错误串并自行展示）。
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod pipe;
mod scm;

/// 通信协议版本。必须与 `src/HonorControl.Contracts/ServiceContract.cs` 的 `ProtocolVersion` 一致：
/// 面板与服务按同一份 v2 契约发布，版本不一致时直接报错让用户重装，不做降级兼容。
const PROTOCOL_VERSION: i64 = 2;

/// 单次请求超时（毫秒）。服务端每个请求自己有 5 秒超时（`PipeServer.cs` 的 `CancelAfter`），
/// 超时后它直接关闭管道、什么都不回写。这里留 1 秒余量，让大多数故障落到“服务端超时断连”
/// 这个更具体的诊断上，而不是含糊的“客户端超时”。
const REQUEST_TIMEOUT_MS: u64 = 6_000;

/// 请求体。`rename_all = "PascalCase"` 是刻意的：System.Text.Json 未配置 camelCase 策略，
/// 服务端按 PascalCase 属性名反序列化（Version / Command / Desired）。
/// `desired` 不加 `skip_serializing_if`：契约里 `Desired` 是可空字段，请求样例显式带 `"Desired":null`。
#[derive(serde::Serialize)]
#[serde(rename_all = "PascalCase")]
struct ServiceRequest<'a> {
    version: i64,
    command: &'a str,
    desired: Option<&'a serde_json::Value>,
}

fn build_request<'a>(
    command: &'a str,
    desired: Option<&'a serde_json::Value>,
) -> Result<serde_json::Value, String> {
    serde_json::to_value(ServiceRequest {
        version: PROTOCOL_VERSION,
        command,
        desired,
    })
    .map_err(|error| format!("构造服务请求失败（内部错误）：{error}"))
}

/// 响应里的错误串（服务端所有失败都走这个字段，且都是中文）。
fn response_error(response: &serde_json::Value) -> Option<&str> {
    match response.get("Error").and_then(|value| value.as_str()) {
        Some(text) if !text.is_empty() => Some(text),
        _ => None,
    }
}

/// 向服务发送一条命令并返回完整响应。
///
/// 失败一律返回可直接展示的中文串；Tauri 会把 `Err(String)` 序列化成字符串作为 Promise 的 reject 值。
///
/// 线程模型：`#[tauri::command(async)]` 让同步体跑在线程池上，避免最长 6 秒的阻塞管道 I/O 卡住窗口。
#[tauri::command(async)]
fn service_request(
    command: String,
    desired: Option<serde_json::Value>,
) -> Result<serde_json::Value, String> {
    let request = build_request(command.as_str(), desired.as_ref())?;
    let response = pipe::call(&request, REQUEST_TIMEOUT_MS)?;

    // 响应版本校验：服务端自己的校验只覆盖请求方向，响应方向在这里兜底。
    match response.get("Version").and_then(|value| value.as_i64()) {
        Some(version) if version == PROTOCOL_VERSION => {}
        Some(version) => {
            return Err(format!(
                "应用与服务的通信版本不一致（服务 v{version} / 面板 v{PROTOCOL_VERSION}），请重新安装应用。"
            ))
        }
        None => {
            return Err(format!(
                "服务响应缺少 Version 字段，无法确认通信版本（面板期望 v{PROTOCOL_VERSION}），请重新安装应用。"
            ))
        }
    }

    // 服务端的错误信息本身就是中文（例如“当前 Windows 用户无权访问 Honor Control 服务。”），
    // 这里统一转成 Err，使前端只需处理一个错误通道——与现有 C# 客户端 ServiceClient.cs 的行为一致。
    if let Some(error) = response_error(&response) {
        return Err(format!("服务返回错误：{error}"));
    }

    Ok(response)
}

/// 返回命名管道名，供前端在“诊断”里展示，便于用户核对服务端配置。
#[tauri::command]
fn pipe_name() -> String {
    pipe::PIPE_NAME.to_string()
}

/// 通过 SCM 启动 Honor Control 服务。
///
/// 服务自己没法把自己启动起来，必须走 SCM；安装器已经给服务安全描述符授予了
/// Authenticated Users 的 `SERVICE_START`，所以中完整性的面板直接调用即可，不会弹 UAC。
#[tauri::command(async)]
fn start_service() -> Result<(), String> {
    scm::start_service("HonorControlService")
}

/// 拉起托盘进程。
///
/// 为什么由面板来做：策略 `OnDemand`（默认）的含义就是"用户在场时才出现托盘"，
/// 而"用户在场"最直接的信号就是打开了面板。`Always` 由服务经登录任务拉起（见 TrayPolicyHost），
/// 面板这边同样可以调用——托盘本身有单实例互斥量，重复拉起是无害的。
///
/// 返回 true 表示确实启动了一个新进程（开发时用来核对路径解析）。
#[tauri::command(async)]
fn launch_tray() -> Result<bool, String> {
    let executable = resolve_tray_executable()?;
    std::process::Command::new(&executable)
        .spawn()
        .map_err(|error| format!("启动托盘进程失败（{}）：{error}", executable.display()))?;
    Ok(true)
}

/// 托盘可执行文件的位置。
///
/// 安装布局：`{app}\panel\honor-control-panel.exe` 与 `{app}\tray\HonorControl.Tray.exe`，
/// 所以从面板自己的目录往上一级再进 tray 就能找到。开发时可以用
/// `HONORCONTROL_TRAY_PATH` 直接指定（Rust 侧构建出来的托盘不在这个相对位置上）。
fn resolve_tray_executable() -> Result<std::path::PathBuf, String> {
    if let Ok(overridden) = std::env::var("HONORCONTROL_TRAY_PATH") {
        let path = std::path::PathBuf::from(overridden);
        if path.is_file() {
            return Ok(path);
        }
        return Err(format!("HONORCONTROL_TRAY_PATH 指向的文件不存在：{}", path.display()));
    }

    let base = std::env::current_exe()
        .map_err(|error| format!("无法定位面板自身路径：{error}"))?
        .parent()
        .ok_or_else(|| "无法定位面板所在目录。".to_string())?
        .to_path_buf();

    let candidates = [
        base.join("..").join("tray").join("HonorControl.Tray.exe"),
        base.join("HonorControl.Tray.exe"),
    ];

    for candidate in candidates {
        if candidate.is_file() {
            return Ok(candidate);
        }
    }

    Err(format!(
        "找不到托盘程序（已查找 {} 与同级目录）。请重新安装 Honor Control。",
        base.join("..").join("tray").join("HonorControl.Tray.exe").display()
    ))
}

fn main() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![
            service_request,
            pipe_name,
            start_service,
            launch_tray
        ])
        .run(tauri::generate_context!())
        .expect("Honor Control 控制面板启动失败：无法创建主窗口。")
}
