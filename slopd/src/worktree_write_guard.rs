//! Host Git mutations must not follow agent-controlled metadata symlinks outside their grant.
//! Landlock confines filesystem writes; git_exec independently forbids repository helpers.
use std::io;
use std::path::Path;
use std::process::Command;

#[cfg(target_os = "linux")]
pub(super) fn restrict(command: &mut Command, paths: &[&Path]) -> io::Result<()> {
    use nix::libc;
    use std::os::fd::{AsRawFd, FromRawFd, OwnedFd};
    use std::os::unix::{fs::OpenOptionsExt, process::CommandExt};
    // ABI 3 is needed to restrict truncation as well as write/open/create/rename/unlink.
    // SAFETY: version query has no input structure and writes no user memory.
    let abi = unsafe {
        libc::syscall(
            libc::SYS_landlock_create_ruleset,
            std::ptr::null::<u8>(),
            0,
            1,
        )
    };
    if abi < 3 {
        return Err(io::Error::new(
            io::ErrorKind::Unsupported,
            "worktree allocation requires Linux Landlock ABI 3 or later",
        ));
    }
    const WRITE_ACCESS: u64 = (1 << 1) | (0x7ff << 4);
    #[repr(C)]
    struct Ruleset {
        access: u64,
    }
    #[repr(C, packed)]
    struct PathRule {
        access: u64,
        parent: i32,
    }
    let rules = Ruleset {
        access: WRITE_ACCESS,
    };
    // SAFETY: rules is the original 8-byte ruleset ABI; the returned descriptor is owned here.
    let raw = unsafe {
        libc::syscall(
            libc::SYS_landlock_create_ruleset,
            &rules,
            std::mem::size_of::<Ruleset>(),
            0,
        )
    };
    if raw < 0 {
        return Err(io::Error::last_os_error());
    }
    // SAFETY: the successful syscall returned a new file descriptor.
    let ruleset = unsafe { OwnedFd::from_raw_fd(raw as i32) };
    for path in paths
        .iter()
        .copied()
        .chain(std::iter::once(Path::new("/dev/null")))
    {
        let file = std::fs::OpenOptions::new()
            .read(true)
            .custom_flags(libc::O_PATH | libc::O_CLOEXEC)
            .open(path)?;
        let rule = PathRule {
            access: if path == Path::new("/dev/null") {
                1 << 1
            } else {
                WRITE_ACCESS
            },
            parent: file.as_raw_fd(),
        };
        // SAFETY: the path rule has the kernel's packed layout; both descriptors are live.
        if unsafe {
            libc::syscall(
                libc::SYS_landlock_add_rule,
                ruleset.as_raw_fd(),
                1,
                &rule,
                0,
            )
        } < 0
        {
            return Err(io::Error::last_os_error());
        }
    }
    // SAFETY: only prctl and landlock_restrict_self run between fork and exec. The ruleset
    // descriptor is retained by the closure and CLOEXEC; no allocation or locks occur here.
    unsafe {
        command.pre_exec(move || {
            if libc::prctl(libc::PR_SET_NO_NEW_PRIVS, 1, 0, 0, 0) != 0
                || libc::syscall(libc::SYS_landlock_restrict_self, ruleset.as_raw_fd(), 0) < 0
            {
                return Err(io::Error::last_os_error());
            }
            Ok(())
        });
    }
    Ok(())
}

#[cfg(not(target_os = "linux"))]
pub(super) fn restrict(_command: &mut Command, _paths: &[&Path]) -> io::Result<()> {
    Err(io::Error::new(
        io::ErrorKind::Unsupported,
        "worktree allocation requires Linux Landlock",
    ))
}
