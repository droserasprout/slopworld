//! Own per-profile launcher locks and detection of externally started games.
//! The launcher keeps the lock through profile seeding and the game lifetime.

#[cfg(target_os = "linux")]
use super::EXE;
use super::{expand, option_env_nonempty};
use nix::errno::Errno;
use nix::fcntl::{Flock, FlockArg};
use std::collections::hash_map::DefaultHasher;
#[cfg(target_os = "linux")]
use std::ffi::OsStr;
use std::fs::{File, OpenOptions};
use std::hash::{Hash, Hasher};
use std::io::{Seek, SeekFrom, Write};
use std::path::{Path, PathBuf};

pub(super) struct InstanceLock {
    _file: Flock<File>,
}

impl InstanceLock {
    pub(super) fn acquire(path: &Path) -> Result<Self, String> {
        if let Some(parent) = path.parent() {
            std::fs::create_dir_all(parent)
                .map_err(|e| format!("creating {}: {e}", parent.display()))?;
        }

        let file = OpenOptions::new()
            .create(true)
            .truncate(false)
            .read(true)
            .write(true)
            .open(path)
            .map_err(|e| format!("opening SlopWorld launcher lock {}: {e}", path.display()))?;

        let mut file = match Flock::lock(file, FlockArg::LockExclusiveNonblock) {
            Ok(file) => file,
            Err((_, e)) if e == Errno::EWOULDBLOCK || e == Errno::EAGAIN => {
                return Err(format!(
                    "another SlopWorld session is already running (lock: {})",
                    path.display()
                ));
            }
            Err((_, e)) => {
                return Err(format!(
                    "locking SlopWorld launcher {}: {e}",
                    path.display()
                ));
            }
        };

        file.set_len(0)
            .and_then(|()| file.seek(SeekFrom::Start(0)))
            .and_then(|_| writeln!(file, "{}", std::process::id()))
            .map_err(|e| format!("writing SlopWorld launcher lock {}: {e}", path.display()))?;

        Ok(Self { _file: file })
    }
}

/// Use a separate launcher lock for each profile.
/// Native and sidecar games with different save folders can run concurrently.
/// Launchers for the same profile share one lock.
pub(super) fn launcher_lock_path(profile: &Path) -> PathBuf {
    let root = option_env_nonempty("XDG_RUNTIME_DIR")
        .map(PathBuf::from)
        .or_else(dirs::config_dir)
        .unwrap_or_else(|| PathBuf::from(expand("~/.config")));
    root.join("slopworld").join(lock_file_name(profile))
}

fn lock_file_name(profile: &Path) -> String {
    let mut hasher = DefaultHasher::new();
    canonical_key(profile).hash(&mut hasher);
    format!("launcher-{:016x}.lock", hasher.finish())
}

/// Resolve the existing ancestor before appending missing components, so creating
/// a profile under a symlinked parent cannot change its lock identity.
fn canonical_key(profile: &Path) -> String {
    let mut ancestor = profile;
    let mut missing = Vec::new();
    let mut resolved = loop {
        if let Ok(path) = std::fs::canonicalize(ancestor) {
            break path;
        }
        let Some(name) = ancestor.components().next_back() else {
            return lexical_normalize(profile).to_string_lossy().into_owned();
        };
        missing.push(name.as_os_str().to_owned());
        let Some(parent) = ancestor.parent() else {
            return lexical_normalize(profile).to_string_lossy().into_owned();
        };
        ancestor = parent;
    };
    for component in missing.into_iter().rev() {
        resolved.push(component);
    }
    lexical_normalize(&resolved).to_string_lossy().into_owned()
}

/// Normalize `.` and `..` even when the profile does not exist.
/// `profile_dir` supplies an absolute path, so parent components cannot escape the filesystem root.
fn lexical_normalize(path: &Path) -> PathBuf {
    let mut out = PathBuf::new();
    for component in path.components() {
        match component {
            std::path::Component::CurDir => {}
            std::path::Component::ParentDir => {
                out.pop();
            }
            other => out.push(other.as_os_str()),
        }
    }
    out
}

/// Return the save folder from the `-savedatafolder=` argument, if present.
#[cfg(any(target_os = "linux", test))]
fn savedatafolder_of(cmdline: &[u8]) -> Option<PathBuf> {
    cmdline
        .split(|byte| *byte == 0)
        .filter_map(|arg| std::str::from_utf8(arg).ok())
        .find_map(|arg| arg.strip_prefix("-savedatafolder=").map(PathBuf::from))
}

/// Check whether two profile paths name the same save folder.
/// Resolve both paths to account for symlinks and parent components.
/// If either resolution fails, compare the original paths.
#[cfg(target_os = "linux")]
fn same_profile(a: &Path, b: &Path) -> bool {
    match (std::fs::canonicalize(a), std::fs::canonicalize(b)) {
        (Ok(a), Ok(b)) => a == b,
        _ => a == b,
    }
}

/// Check for games started outside the launcher, which do not hold the launcher lock.
/// A game with a different save folder can run concurrently.
/// Prevent another launch if an existing game's save folder is unknown or unreadable.
/// Also prevent a launch if process inspection fails.
#[cfg(target_os = "linux")]
pub(super) fn game_process_running(profile: &Path) -> Result<Option<u32>, String> {
    let entries = std::fs::read_dir("/proc")
        .map_err(|e| format!("checking for an existing RimWorld process: {e}"))?;

    for entry in entries {
        let entry = entry.map_err(|e| format!("checking for an existing RimWorld process: {e}"))?;
        let name = entry.file_name();
        let Some(pid) = name.to_str().and_then(|s| s.parse::<u32>().ok()) else {
            continue;
        };

        let is_game = match std::fs::read_link(entry.path().join("exe")) {
            Ok(path) => path.file_name() == Some(OsStr::new(EXE)),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => false,
            Err(e) if e.kind() == std::io::ErrorKind::PermissionDenied => {
                match std::fs::read(entry.path().join("cmdline")) {
                    Ok(bytes) => argv0_is_game(&bytes),
                    Err(e) if e.kind() == std::io::ErrorKind::NotFound => continue,
                    Err(e) => return Err(format!("checking command line for PID {pid}: {e}")),
                }
            }
            Err(e) => return Err(format!("checking executable for PID {pid}: {e}")),
        };

        if !is_game {
            continue;
        }

        // Permit concurrent games with different save folders.
        // Prevent another launch if the save folder matches, is absent, or is unreadable.
        match std::fs::read(entry.path().join("cmdline")) {
            Ok(bytes) => match savedatafolder_of(&bytes) {
                Some(other) if !same_profile(&other, profile) => continue,
                _ => return Ok(Some(pid)),
            },
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => continue,
            Err(_) => return Ok(Some(pid)),
        }
    }

    Ok(None)
}

/// Check the executable name in argv[0] when `/proc/<pid>/exe` is unreadable but the command line is available.
#[cfg(target_os = "linux")]
fn argv0_is_game(cmdline: &[u8]) -> bool {
    let argv0 = cmdline.split(|byte| *byte == 0).next().unwrap_or_default();
    Path::new(std::str::from_utf8(argv0).unwrap_or("")).file_name() == Some(OsStr::new(EXE))
}

#[cfg(not(target_os = "linux"))]
pub(super) fn game_process_running(_profile: &Path) -> Result<Option<u32>, String> {
    Ok(None)
}

#[cfg(test)]
#[path = "instance_tests.rs"]
mod tests;
