//! Host Git mutations must not follow agent-controlled metadata symlinks outside their grant.
//! Landlock confines filesystem writes. Git_exec independently forbids repository helpers.
use std::io;
use std::path::Path;
use std::process::Command;

#[cfg(target_os = "linux")]
pub(super) fn restrict(command: &mut Command, paths: &[&Path]) -> io::Result<()> {
    use landlock::{
        Access, AccessFs, CompatLevel, Compatible, PathBeneath, PathFd, Ruleset, RulesetAttr,
        RulesetCreatedAttr, ABI,
    };
    use nix::libc;
    use std::os::fd::{AsRawFd, OwnedFd};
    use std::os::unix::process::CommandExt;

    // ABI 3 adds Truncate. Require every handled write right so older kernels cannot
    // silently leave a write operation outside the ruleset.
    let write_access =
        AccessFs::from_all(ABI::V3) & !(AccessFs::Execute | AccessFs::ReadFile | AccessFs::ReadDir);
    let mut ruleset = Ruleset::default()
        .set_compatibility(CompatLevel::HardRequirement)
        .handle_access(write_access)
        .and_then(Ruleset::create)
        .map_err(io::Error::other)?;
    for path in paths {
        ruleset = ruleset
            .add_rule(PathBeneath::new(
                PathFd::new(path).map_err(io::Error::other)?,
                write_access,
            ))
            .map_err(io::Error::other)?;
    }
    ruleset = ruleset
        .add_rule(PathBeneath::new(
            PathFd::new("/dev/null").map_err(io::Error::other)?,
            AccessFs::WriteFile,
        ))
        .map_err(io::Error::other)?;
    let ruleset: OwnedFd = Option::from(ruleset).ok_or_else(|| {
        io::Error::new(
            io::ErrorKind::Unsupported,
            "Landlock ruleset was not created",
        )
    })?;
    // The crate's restrict_self method is for ordinary process context. Keep the post-fork
    // callback to syscall-only work rather than calling a higher-level Rust builder there.
    // SAFETY: only prctl and landlock_restrict_self run between fork and exec. The closure retains
    // the ruleset descriptor. CLOEXEC closes it on exec. This code allocates no memory and takes no locks.
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
