//! Own isolated test processes and temporary tmux socket lifetimes.
use std::path::PathBuf;

/// Own the socket directory until every test client and recovery handle is done.
pub(crate) struct TmuxSocket {
    pub(crate) path: String,
    directory: PathBuf,
}

impl TmuxSocket {
    pub(crate) fn new() -> Self {
        let directory = std::env::temp_dir().join(format!("slop-tmux-{}", uuid::Uuid::new_v4()));
        std::fs::create_dir(&directory).expect("test tmux directory");
        Self {
            path: directory.join("socket").to_str().unwrap().to_owned(),
            directory,
        }
    }
}

impl Drop for TmuxSocket {
    fn drop(&mut self) {
        // Stop the server before removing its socket, including after a panic.
        drop(
            std::process::Command::new("tmux")
                .args(["-S", &self.path, "kill-server"])
                .output(),
        );
        drop(std::fs::remove_dir_all(&self.directory));
    }
}

pub(crate) fn isolated() -> Option<PathBuf> {
    isolated_with_env(|_, _| {})
}

/// Configure the child's environment before its test harness or runtime starts threads.
pub(crate) fn isolated_with_env(
    configure: impl FnOnce(&mut std::process::Command, &std::path::Path),
) -> Option<PathBuf> {
    let thread = std::thread::current();
    let name = thread.name()?;
    if std::env::var("SLOPD_ISOLATED_TEST").as_deref() == Ok(name) {
        return Some(PathBuf::from(std::env::var_os("SLOPD_TEST_ROOT")?));
    }
    let root = std::env::temp_dir().join(format!("slopd-isolated-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(root.join("cache")).unwrap();
    let mut command = std::process::Command::new(std::env::current_exe().unwrap());
    command
        .args(["--exact", name, "--nocapture"])
        .env("SLOPD_ISOLATED_TEST", name)
        .env("SLOPD_TEST_ROOT", &root)
        .env("SLOPD_DATA", root.join("data"))
        .env("SLOPD_CACHE", root.join("cache"))
        .env_remove("OPENROUTER_API_KEY")
        .env("NO_PROXY", "*")
        .env("no_proxy", "*");
    configure(&mut command, &root);
    let output = command.output();
    let cleanup = std::fs::remove_dir_all(&root);
    let cleanup_failure = cleanup
        .as_ref()
        .err()
        .map(|error| {
            format!(
                "\nisolated test cleanup failed for {}: {error}",
                root.display()
            )
        })
        .unwrap_or_default();
    let output =
        output.unwrap_or_else(|error| panic!("start isolated test: {error}{cleanup_failure}"));
    assert!(
        output.status.success(),
        "isolated test failed:\n{}\n{}{cleanup_failure}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    );
    cleanup.expect("clean isolated test directory");
    None
}

// Dynamic provider URLs and catalog failure fixtures use an isolated process-local
// overlay. Native environment mutation would race with HTTP or Tokio helper threads.
static ENVIRONMENT: std::sync::Mutex<std::collections::BTreeMap<String, std::ffi::OsString>> =
    std::sync::Mutex::new(std::collections::BTreeMap::new());

pub(crate) fn set_env(key: &str, value: impl AsRef<std::ffi::OsStr>) {
    assert!(std::env::var_os("SLOPD_ISOLATED_TEST").is_some());
    ENVIRONMENT
        .lock()
        .unwrap()
        .insert(key.into(), value.as_ref().into());
}

pub(crate) fn var_os(key: &str) -> Option<std::ffi::OsString> {
    let value = ENVIRONMENT.lock().unwrap().get(key).cloned();
    value.or_else(|| std::env::var_os(key))
}

pub(crate) fn var(key: &str) -> Result<String, std::env::VarError> {
    var_os(key)
        .ok_or(std::env::VarError::NotPresent)?
        .into_string()
        .map_err(std::env::VarError::NotUnicode)
}

/// A private, uniquely named fixture directory that removes itself after the test.
pub(crate) struct Fixture(PathBuf);

impl std::ops::Deref for Fixture {
    type Target = std::path::Path;

    fn deref(&self) -> &Self::Target {
        &self.0
    }
}

impl Drop for Fixture {
    fn drop(&mut self) {
        match std::fs::remove_dir_all(&self.0) {
            Ok(()) => {}
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
            Err(error) if std::thread::panicking() => {
                eprintln!(
                    "could not remove browse fixture {}: {error}",
                    self.0.display()
                );
            }
            Err(error) => panic!(
                "could not remove browse fixture {}: {error}",
                self.0.display()
            ),
        }
    }
}

pub(crate) fn fixture(tag: &str) -> Fixture {
    let dir = std::env::temp_dir().join(format!("slopd-browse-{tag}-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir(&dir).unwrap();
    Fixture(dir)
}

// Keep real persistence, Git, tmux and reader attachment. Replace only the final
// sandbox executable so these orchestration tests need no agent or nested sandbox.
pub(crate) fn isolated_worker_runtime() -> Option<std::path::PathBuf> {
    crate::test_support::isolated_with_env(|command, root| {
        use std::os::unix::fs::PermissionsExt;
        let bin = root.join("bin");
        std::fs::create_dir(&bin).unwrap();
        let executable = bin.join("bwrap");
        std::fs::write(
            &executable,
            "#!/bin/sh\nfor arg do\n  if [ \"$arg\" = /usr/bin/git ]; then exec /usr/bin/bwrap \"$@\"; fi\ndone\nprintf '\\033[?2004hready> '\nexec /bin/sleep 60\n",
        )
        .unwrap();
        std::fs::set_permissions(&executable, std::fs::Permissions::from_mode(0o755)).unwrap();
        let mut paths = vec![bin];
        paths.extend(std::env::split_paths(&std::env::var_os("PATH").unwrap()));
        command.env("PATH", std::env::join_paths(paths).unwrap());
    })
}
