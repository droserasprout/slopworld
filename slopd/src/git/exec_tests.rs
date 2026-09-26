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
