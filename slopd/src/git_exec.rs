//! Git builtins may inspect repositories but may not fork repository-selected helpers.

use std::os::unix::process::CommandExt;
use std::process::Command;

pub(super) fn restrict(command: &mut Command) {
    // SAFETY: the child callback only makes prctl calls with stack-owned arguments.
    unsafe {
        command.pre_exec(install);
    }
}

#[cfg(all(
    target_os = "linux",
    any(target_arch = "x86_64", target_arch = "aarch64")
))]
fn install() -> std::io::Result<()> {
    use nix::libc;

    const fn stmt(code: u32, k: u32) -> libc::sock_filter {
        libc::sock_filter {
            code: code as u16,
            jt: 0,
            jf: 0,
            k,
        }
    }
    const fn jump(k: u32, jt: u8, jf: u8) -> libc::sock_filter {
        libc::sock_filter {
            code: (libc::BPF_JMP | libc::BPF_JEQ | libc::BPF_K) as u16,
            jt,
            jf,
            k,
        }
    }
    #[cfg(target_arch = "x86_64")]
    const ARCH: u32 = 0xc000003e;
    #[cfg(target_arch = "aarch64")]
    const ARCH: u32 = 0xc00000b7;
    const LOAD: u32 = libc::BPF_LD | libc::BPF_W | libc::BPF_ABS;
    const RET: u32 = libc::BPF_RET | libc::BPF_K;
    const DENY: u32 = libc::SECCOMP_RET_ERRNO | libc::EPERM as u32;

    let filter = [
        stmt(LOAD, 4), // seccomp_data.arch
        jump(ARCH, 1, 0),
        stmt(RET, libc::SECCOMP_RET_KILL_PROCESS),
        stmt(LOAD, 0), // seccomp_data.nr
        #[cfg(target_arch = "x86_64")]
        libc::sock_filter {
            code: (libc::BPF_JMP | libc::BPF_JSET | libc::BPF_K) as u16,
            jt: 0,
            jf: 1,
            k: 0x40000000,
        },
        #[cfg(target_arch = "x86_64")]
        stmt(RET, DENY), // Reject the x32 syscall ABI.
        #[cfg(target_arch = "x86_64")]
        jump(libc::SYS_fork as u32, 0, 1),
        #[cfg(target_arch = "x86_64")]
        stmt(RET, DENY),
        #[cfg(target_arch = "x86_64")]
        jump(libc::SYS_vfork as u32, 0, 1),
        #[cfg(target_arch = "x86_64")]
        stmt(RET, DENY),
        jump(libc::SYS_clone3 as u32, 0, 1),
        // libc falls back to clone, whose flags seccomp can inspect.
        stmt(RET, libc::SECCOMP_RET_ERRNO | libc::ENOSYS as u32),
        jump(libc::SYS_clone as u32, 0, 4),
        stmt(LOAD, 16), // seccomp_data.args[0], clone flags
        libc::sock_filter {
            code: (libc::BPF_JMP | libc::BPF_JSET | libc::BPF_K) as u16,
            jt: 1,
            jf: 0,
            k: libc::CLONE_THREAD as u32,
        },
        stmt(RET, DENY),
        stmt(RET, libc::SECCOMP_RET_ALLOW),
        stmt(RET, libc::SECCOMP_RET_ALLOW),
    ];
    let program = libc::sock_fprog {
        len: filter.len() as u16,
        filter: filter.as_ptr() as *mut _,
    };
    // SAFETY: prctl reads the valid filter during this call; it retains no userspace pointer.
    unsafe {
        if libc::prctl(libc::PR_SET_NO_NEW_PRIVS, 1, 0, 0, 0) != 0
            || libc::prctl(libc::PR_SET_SECCOMP, libc::SECCOMP_MODE_FILTER, &program) != 0
        {
            return Err(std::io::Error::last_os_error());
        }
    }
    Ok(())
}

#[cfg(not(all(
    target_os = "linux",
    any(target_arch = "x86_64", target_arch = "aarch64")
)))]
fn install() -> std::io::Result<()> {
    Err(std::io::Error::from_raw_os_error(nix::libc::ENOTSUP))
}

#[cfg(all(
    test,
    target_os = "linux",
    any(target_arch = "x86_64", target_arch = "aarch64")
))]
mod tests {
    use super::*;

    #[test]
    fn restriction_blocks_processes_but_allows_threads() {
        const CHILD: &str = "SLOPD_GIT_EXEC_TEST_CHILD";
        if std::env::var_os(CHILD).is_none() {
            let mut command = Command::new(std::env::current_exe().unwrap());
            command
                .args([
                    "--exact",
                    std::thread::current().name().unwrap(),
                    "--nocapture",
                ])
                .env(CHILD, "1");
            restrict(&mut command);
            let output = command.output().unwrap();
            assert!(
                output.status.success(),
                "{}",
                String::from_utf8_lossy(&output.stderr)
            );
            return;
        }

        use nix::libc;
        // The filter is installed before exec, never in the parent test runner.
        assert_eq!(
            unsafe { libc::prctl(libc::PR_GET_NO_NEW_PRIVS, 0, 0, 0, 0) },
            1
        );
        assert_eq!(std::thread::spawn(|| 42).join().unwrap(), 42);
        let error = Command::new("true").status().unwrap_err();
        assert_eq!(error.raw_os_error(), Some(libc::EPERM));
        let result = unsafe { libc::syscall(libc::SYS_clone3, std::ptr::null::<u8>(), 0) };
        assert_eq!(result, -1);
        assert_eq!(
            std::io::Error::last_os_error().raw_os_error(),
            Some(libc::ENOSYS)
        );
        // Raw clone bypasses libc's choice of fork/vfork/clone3 fallback.
        let result = unsafe { libc::syscall(libc::SYS_clone, libc::SIGCHLD, 0, 0, 0, 0) };
        if result == 0 {
            unsafe { libc::_exit(0) };
        }
        if result > 0 {
            unsafe { libc::waitpid(result as i32, std::ptr::null_mut(), 0) };
            panic!("process clone unexpectedly allowed");
        }
        assert_eq!(
            std::io::Error::last_os_error().raw_os_error(),
            Some(libc::EPERM)
        );
    }
}
