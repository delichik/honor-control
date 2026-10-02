//! 通过 SCM 启动 Honor Control 服务。
//!
//! 为什么需要它：服务运行在会话 0，面板是中完整性进程，两者唯一的联系是命名管道——
//! 但管道是**服务提供的**，服务没在跑就没有管道可用。用户从托盘退出会停止服务（产品决定），
//! 之后要把服务拉回来只能靠 SCM。
//!
//! 权限：安装器用 `sc.exe sdset` 给服务安全描述符授予了 Authenticated Users 的 `SERVICE_START`
//! （**不授予 STOP**），因此这里可以正常启动而不需要管理员权限，也不会触发 UAC。
//!
//! 这里直接用 advapi32 的 FFI，不引入额外 crate——只需要四个函数。

use std::ffi::c_void;
use std::os::windows::ffi::OsStrExt;
use std::ptr;

const SC_MANAGER_CONNECT: u32 = 0x0001;
const SERVICE_START: u32 = 0x0010;
/// 服务已经在运行时 StartServiceW 会返回这个错误，属于"已经是目标状态"，不应报错。
const ERROR_SERVICE_ALREADY_RUNNING: i32 = 1056;

#[link(name = "advapi32")]
extern "system" {
    fn OpenSCManagerW(machine_name: *const u16, database_name: *const u16, access: u32) -> *mut c_void;
    fn OpenServiceW(manager: *mut c_void, service_name: *const u16, access: u32) -> *mut c_void;
    fn StartServiceW(service: *mut c_void, argument_count: u32, arguments: *const *const u16) -> i32;
    fn CloseServiceHandle(handle: *mut c_void) -> i32;
}

/// Rust 字符串 → NUL 结尾的宽字符串（Win32 W 系列 API 的入参）。
fn wide(value: &str) -> Vec<u16> {
    std::ffi::OsStr::new(value).encode_wide().chain(std::iter::once(0)).collect()
}

/// 启动指定服务；已经处于运行状态时视为成功。
pub fn start_service(service_name: &str) -> Result<(), String> {
    unsafe {
        // 本机服务管理器（machine/database 传 NULL）
        let manager = OpenSCManagerW(ptr::null(), ptr::null(), SC_MANAGER_CONNECT);
        if manager.is_null() {
            return Err(format!(
                "无法连接服务管理器：{}",
                std::io::Error::last_os_error()
            ));
        }

        let name = wide(service_name);
        let service = OpenServiceW(manager, name.as_ptr(), SERVICE_START);
        if service.is_null() {
            let error = std::io::Error::last_os_error();
            CloseServiceHandle(manager);
            return Err(format!(
                "无法打开 Honor Control 服务（可能尚未安装，或本机安全策略未授予启动权限）：{error}"
            ));
        }

        let started = StartServiceW(service, 0, ptr::null());
        let code = std::io::Error::last_os_error().raw_os_error().unwrap_or(0);

        CloseServiceHandle(service);
        CloseServiceHandle(manager);

        if started == 0 && code != ERROR_SERVICE_ALREADY_RUNNING {
            return Err(format!(
                "启动 Honor Control 服务失败（Win32 {code}）：{}",
                std::io::Error::from_raw_os_error(code)
            ));
        }

        Ok(())
    }
}
