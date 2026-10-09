//! Explicitly elevated start/stop controls for the Windows service.

use std::ffi::c_void;
use std::os::windows::ffi::OsStrExt;
use std::ptr;
use std::time::Duration;

const SERVICE_NAME: &str = "HonorControlService";
const SC_MANAGER_CONNECT: u32 = 0x0001;
const SERVICE_QUERY_STATUS: u32 = 0x0004;
const SERVICE_START: u32 = 0x0010;
const SERVICE_STOP: u32 = 0x0020;
const SERVICE_CONTROL_STOP: u32 = 0x0001;
const SERVICE_STOPPED: u32 = 0x0001;
const SERVICE_START_PENDING: u32 = 0x0002;
const SERVICE_STOP_PENDING: u32 = 0x0003;
const SERVICE_RUNNING: u32 = 0x0004;
const ERROR_SERVICE_ALREADY_RUNNING: u32 = 1056;
const ERROR_SERVICE_NOT_ACTIVE: u32 = 1062;
const ERROR_SERVICE_REQUEST_TIMEOUT: u32 = 1053;
const ERROR_CANCELLED: i32 = 1223;
const SEE_MASK_NOCLOSEPROCESS: u32 = 0x00000040;
const SW_HIDE: i32 = 0;
const WAIT_OBJECT_0: u32 = 0;
const WAIT_TIMEOUT: u32 = 0x00000102;

#[repr(C)]
struct ServiceStatus {
    service_type: u32,
    current_state: u32,
    controls_accepted: u32,
    win32_exit_code: u32,
    service_specific_exit_code: u32,
    check_point: u32,
    wait_hint: u32,
}

#[repr(C)]
struct ShellExecuteInfoW {
    cb_size: u32,
    f_mask: u32,
    hwnd: *mut c_void,
    lp_verb: *const u16,
    lp_file: *const u16,
    lp_parameters: *const u16,
    lp_directory: *const u16,
    n_show: i32,
    h_inst_app: *mut c_void,
    lp_id_list: *mut c_void,
    lp_class: *const u16,
    hkey_class: *mut c_void,
    dw_hot_key: u32,
    h_icon_or_monitor: *mut c_void,
    h_process: *mut c_void,
}

#[link(name = "advapi32")]
extern "system" {
    fn OpenSCManagerW(machine_name: *const u16, database_name: *const u16, access: u32) -> *mut c_void;
    fn OpenServiceW(manager: *mut c_void, service_name: *const u16, access: u32) -> *mut c_void;
    fn StartServiceW(service: *mut c_void, argument_count: u32, arguments: *const *const u16) -> i32;
    fn ControlService(service: *mut c_void, control: u32, status: *mut ServiceStatus) -> i32;
    fn QueryServiceStatus(service: *mut c_void, status: *mut ServiceStatus) -> i32;
    fn CloseServiceHandle(handle: *mut c_void) -> i32;
}

#[link(name = "shell32")]
extern "system" {
    fn ShellExecuteExW(info: *mut ShellExecuteInfoW) -> i32;
}

#[link(name = "kernel32")]
extern "system" {
    fn WaitForSingleObject(handle: *mut c_void, milliseconds: u32) -> u32;
    fn GetExitCodeProcess(handle: *mut c_void, exit_code: *mut u32) -> i32;
    fn CloseHandle(handle: *mut c_void) -> i32;
}

fn wide(value: &str) -> Vec<u16> {
    std::ffi::OsStr::new(value).encode_wide().chain(std::iter::once(0)).collect()
}

fn last_error_code() -> u32 {
    std::io::Error::last_os_error().raw_os_error().unwrap_or(0) as u32
}

/// Called by the ordinary-privilege Tauri process. Windows shows a UAC prompt
/// before relaunching this executable in its narrowly scoped service-control mode.
fn run_elevated(action: &str) -> Result<(), String> {
    let executable = std::env::current_exe()
        .map_err(|error| format!("无法定位控制面板：{error}"))?;
    let executable = executable.as_os_str().encode_wide().chain(std::iter::once(0)).collect::<Vec<_>>();
    let verb = wide("runas");
    let parameters = wide(&format!("--service-control {action}"));
    let mut info = ShellExecuteInfoW {
        cb_size: std::mem::size_of::<ShellExecuteInfoW>() as u32,
        f_mask: SEE_MASK_NOCLOSEPROCESS,
        hwnd: ptr::null_mut(),
        lp_verb: verb.as_ptr(),
        lp_file: executable.as_ptr(),
        lp_parameters: parameters.as_ptr(),
        lp_directory: ptr::null(),
        n_show: SW_HIDE,
        h_inst_app: ptr::null_mut(),
        lp_id_list: ptr::null_mut(),
        lp_class: ptr::null(),
        hkey_class: ptr::null_mut(),
        dw_hot_key: 0,
        h_icon_or_monitor: ptr::null_mut(),
        h_process: ptr::null_mut(),
    };

    let launched = unsafe { ShellExecuteExW(&mut info) };
    if launched == 0 {
        let error = std::io::Error::last_os_error();
        if error.raw_os_error() == Some(ERROR_CANCELLED) {
            return Err("已取消管理员授权。".to_string());
        }
        return Err(format!("请求管理员权限失败：{error}"));
    }

    if info.h_process.is_null() {
        return Err("Windows 未返回服务控制进程。".to_string());
    }

    let wait = unsafe { WaitForSingleObject(info.h_process, 30_000) };
    if wait != WAIT_OBJECT_0 {
        unsafe { CloseHandle(info.h_process); }
        return Err(if wait == WAIT_TIMEOUT {
            "服务启停操作超时。".to_string()
        } else {
            format!("等待服务启停操作失败（Windows {}）。", last_error_code())
        });
    }

    let mut exit_code = u32::MAX;
    let read_exit_code = unsafe { GetExitCodeProcess(info.h_process, &mut exit_code) };
    unsafe { CloseHandle(info.h_process); }
    if read_exit_code == 0 {
        return Err(format!("读取服务启停结果失败（Windows {}）。", last_error_code()));
    }

    if exit_code == 0 {
        Ok(())
    } else {
        Err(format!("服务启停失败（Windows {exit_code}）。"))
    }
}

pub fn start_service() -> Result<(), String> {
    run_elevated("start")
}

pub fn stop_service() -> Result<(), String> {
    run_elevated("stop")
}

/// Child-process entry point. Returns `None` for the normal GUI launch.
pub fn handle_elevated_helper() -> Option<i32> {
    let mut arguments = std::env::args_os().skip(1);
    if arguments.next()?.to_string_lossy() != "--service-control" {
        return None;
    }

    let result = match arguments.next()?.to_string_lossy().as_ref() {
        "start" => control_service(true),
        "stop" => control_service(false),
        _ => Err(87),
    };
    Some(result.err().unwrap_or(0) as i32)
}

fn control_service(start: bool) -> Result<(), u32> {
    unsafe {
        let manager = OpenSCManagerW(ptr::null(), ptr::null(), SC_MANAGER_CONNECT);
        if manager.is_null() {
            return Err(last_error_code());
        }

        let name = wide(SERVICE_NAME);
        let access = SERVICE_QUERY_STATUS | if start { SERVICE_START } else { SERVICE_STOP };
        let service = OpenServiceW(manager, name.as_ptr(), access);
        if service.is_null() {
            let error = last_error_code();
            CloseServiceHandle(manager);
            return Err(error);
        }

        let result = control_open_service(service, start);
        CloseServiceHandle(service);
        CloseServiceHandle(manager);
        result
    }
}

unsafe fn control_open_service(service: *mut c_void, start: bool) -> Result<(), u32> {
    let target_state = if start { SERVICE_RUNNING } else { SERVICE_STOPPED };
    let mut status = std::mem::zeroed::<ServiceStatus>();
    if QueryServiceStatus(service, &mut status) == 0 {
        return Err(last_error_code());
    }
    if status.current_state == target_state {
        return Ok(());
    }

    if start {
        if StartServiceW(service, 0, ptr::null()) == 0 {
            let error = last_error_code();
            if error != ERROR_SERVICE_ALREADY_RUNNING {
                return Err(error);
            }
        }
    } else if ControlService(service, SERVICE_CONTROL_STOP, &mut status) == 0 {
        let error = last_error_code();
        if error == ERROR_SERVICE_NOT_ACTIVE {
            return Ok(());
        }
        return Err(error);
    }

    for _ in 0..120 {
        if QueryServiceStatus(service, &mut status) == 0 {
            return Err(last_error_code());
        }
        if status.current_state == target_state {
            return Ok(());
        }
        std::thread::sleep(Duration::from_millis(250));
    }

    Err(ERROR_SERVICE_REQUEST_TIMEOUT)
}
