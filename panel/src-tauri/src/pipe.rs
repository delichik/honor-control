//! 命名管道客户端：与 Honor Control Windows 服务通信。
//!
//! 协议（与 `src/HonorControl.Service/PipeServer.cs` 对齐）：
//! 客户端写入**一行** UTF-8 JSON 并以 `\n` 结尾；服务端回写**一行** UTF-8 JSON。
//! 属性名为 PascalCase（System.Text.Json 默认策略，未配置 camelCase）。
//! 服务端每个请求有 5 秒超时，超时后直接关闭管道、不回写任何内容。
//!
//! 本模块**只使用标准库**（外加 serde_json 做解析），不引入 tokio / windows crate。

use std::fs::OpenOptions;
use std::io::{BufRead, BufReader, Write};
use std::time::{Duration, Instant};

/// 管道名，与 `HonorControl.Contracts.ServiceContract.PipeName` 保持一致。
/// 用原始字符串 `r"..."`：里面的 `\\` 不是转义，`\\.\pipe\` 是 Windows 命名管道的命名空间前缀。
pub const PIPE_NAME: &str = r"\\.\pipe\HonorControl.Service.v1";

/// 管道不存在（Win32 错误码 2）：服务未安装或未运行。
const ERROR_NO_PIPE: &str =
    "无法连接到 Honor Control 服务：命名管道不存在，服务可能未安装或未启动。";
/// 拒绝访问（Win32 错误码 5）：服务端 PipeSecurity 未放行当前用户，或服务未运行。
const ERROR_ACCESS_DENIED: &str =
    "访问 Honor Control 服务被拒绝：当前 Windows 用户未获得授权（或服务未运行）。";
/// 管道忙（Win32 错误码 231）：服务端并发实例已用满。
const ERROR_PIPE_BUSY: &str =
    "Honor Control 服务正忙：命名管道并发实例已用满，请稍后重试。";

/// 发送一条请求，返回服务端回写的单个 JSON 对象。
///
/// `timeout_ms` 是本函数的等待上限，见下方关于超时策略的说明；返回的 `Err` 都是可直接展示的中文串。
pub fn call(request: &serde_json::Value, timeout_ms: u64) -> Result<serde_json::Value, String> {
    let started = Instant::now();
    let timeout = Duration::from_millis(timeout_ms);

    // ── 超时策略（本模块的明确选择：务实方案）──
    // 标准库没有“带超时的文件读取”，因此这里把截止时间记在 Instant 上，在 open / write 之后
    // 以及读取到 EOF 时检查已耗时，超限就返回超时错误。判定点是固定的三处，不做轮询。
    // 已知局限：若服务端接受连接后既不回写也不关闭管道，`read_line` 会一直阻塞，本函数的超时
    // 无法打断它。目前可以接受——服务端自己 5 秒后就会关闭管道（`PipeServer.cs` 的
    // `CancelAfter` + `using (pipe)`），`read_line` 随即返回 EOF。
    // 更严格的做法（本次**未**采用，若将来真的出现卡死再换）：
    //   ① 用 `OpenOptionsExt::custom_flags(FILE_FLAG_OVERLAPPED)` 做重叠 I/O，
    //      配合 WaitForSingleObject / CancelIoEx 精确取消；
    //   ② 把整个收发放进工作线程，调用方用 `std::sync::mpsc::Receiver::recv_timeout` 等结果
    //      （线程会滞留到管道关闭，但调用方不会被拖住）。

    // Windows 上标准库的 open 最终调用 CreateFileW（GENERIC_READ|GENERIC_WRITE、OPEN_EXISTING、
    // 不设 FILE_FLAG_OVERLAPPED），命名管道也走这条路径，所以可以直接用 fs API 打开 `\\.\pipe\...`。
    // 路径不会被改写（标准库不会给设备路径加 `\\?\` 前缀）。
    // 备选：改用 `tokio::net::windows::named_pipe`（异步、可 select 超时），代价是引入 tokio。
    let mut pipe = OpenOptions::new()
        .read(true)
        .write(true)
        .open(PIPE_NAME)
        .map_err(|error| map_open_error(&error))?;

    ensure_within(started, timeout, timeout_ms)?;

    // 一行 JSON + '\n'。服务端按 '\n' 切分并自行去掉 '\r'，所以这里只用 '\n' 即可。
    // 服务端单次请求最多读 4096 个字符，正常情况下请求远小于该上限。
    let mut line = serde_json::to_string(request)
        .map_err(|error| format!("序列化服务请求失败（内部错误）：{error}"))?;
    line.push('\n');
    pipe.write_all(line.as_bytes())
        .map_err(|error| format!("向 Honor Control 服务发送请求失败：{error}"))?;
    // flush 的实际语义要说清楚：标准库的 File 没有用户态缓冲，写入本身就是直达句柄的系统调用，
    // 因此 flush 在这里最终调用的是 Win32 FlushFileBuffers。对**管道**句柄，FlushFileBuffers 会
    // 等到对端把数据读走才返回——正常情况下服务端立刻在读取这一行，所以不会卡住；它同时也确认了
    // 请求确实已经发出。注意这也是本函数的第二个潜在阻塞点（与 read_line 同类，见上面的超时说明）。
    pipe.flush()
        .map_err(|error| format!("向 Honor Control 服务发送请求失败（flush）：{error}"))?;

    ensure_within(started, timeout, timeout_ms)?;

    // 服务端**只回写一行**（`WriteLineAsync` 一次），所以读一行就够。
    // 它用 UTF8Encoding(false) 编码（无 BOM），因此不需要处理 BOM；
    // 但 `WriteLine` 输出的是 CRLF，读到的字符串末尾会带 "\r\n"，解析前统一 trim。
    let mut reader = BufReader::new(pipe);
    let mut response_line = String::new();
    let read = reader
        .read_line(&mut response_line)
        .map_err(|error| format!("读取 Honor Control 服务响应失败：{error}"))?;

    if read == 0 {
        // EOF：服务端已关闭管道。要么是它的 5 秒超时路径（什么都不回写直接断开），要么服务已退出。
        return Err(if started.elapsed() >= timeout {
            timeout_message(started, timeout_ms)
        } else {
            format!(
                "服务未返回任何内容（管道已关闭）：服务可能已停止，或请求超过服务端 5 秒超时被中断（已等待 {} 毫秒）。",
                started.elapsed().as_millis()
            )
        });
    }

    let text = response_line.trim();
    if text.is_empty() {
        // 只收到空白行：同样按空响应对待，不要送进 JSON 解析器。
        return Err("服务返回了空响应，请确认服务版本与面板版本一致。".to_string());
    }

    // 迟到的响应仍然解析：宁可接受数据，也不要因为“超过客户端超时”而丢掉一份有效快照。
    serde_json::from_str::<serde_json::Value>(text)
        .map_err(|error| format!("服务返回的内容不是合法 JSON，可能版本不匹配或服务异常：{error}"))
}

/// 截止时间检查：超过 `timeout` 就返回超时错误（错误信息里带实际耗时，便于现场诊断）。
fn ensure_within(started: Instant, timeout: Duration, timeout_ms: u64) -> Result<(), String> {
    if started.elapsed() >= timeout {
        Err(timeout_message(started, timeout_ms))
    } else {
        Ok(())
    }
}

fn timeout_message(started: Instant, timeout_ms: u64) -> String {
    format!(
        "等待 Honor Control 服务响应超时（已等待 {} 毫秒，上限 {timeout_ms} 毫秒），请检查服务是否正在运行。",
        started.elapsed().as_millis()
    )
}

/// 打开管道失败的原因分类：服务未安装 / 未启动、无权访问、实例已用满。
fn map_open_error(error: &std::io::Error) -> String {
    // Win32 错误码在 raw_os_error 里：2 = ERROR_FILE_NOT_FOUND，5 = ERROR_ACCESS_DENIED，
    // 231 = ERROR_PIPE_BUSY。优先按错误码判断，同时保留 ErrorKind 作为兜底。
    match error.raw_os_error() {
        Some(2) => ERROR_NO_PIPE.to_string(),
        Some(5) => ERROR_ACCESS_DENIED.to_string(),
        Some(231) => ERROR_PIPE_BUSY.to_string(),
        _ => match error.kind() {
            std::io::ErrorKind::NotFound => ERROR_NO_PIPE.to_string(),
            std::io::ErrorKind::PermissionDenied => ERROR_ACCESS_DENIED.to_string(),
            _ => format!("连接 Honor Control 服务失败：{error}"),
        },
    }
}
