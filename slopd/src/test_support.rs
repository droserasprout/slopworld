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
    let thread = std::thread::current();
    let name = thread.name()?;
    if std::env::var("SLOPD_ISOLATED_TEST").as_deref() == Ok(name) {
        return Some(PathBuf::from(std::env::var_os("SLOPD_TEST_ROOT")?));
    }
    let root = std::env::temp_dir().join(format!("slopd-isolated-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(root.join("cache")).unwrap();
    let output = std::process::Command::new(std::env::current_exe().unwrap())
        .args(["--exact", name, "--nocapture"])
        .env("SLOPD_ISOLATED_TEST", name)
        .env("SLOPD_TEST_ROOT", &root)
        .env("SLOPD_STATE", root.join("state"))
        .env("SLOPD_CACHE", root.join("cache"))
        .env_remove("OPENROUTER_API_KEY")
        .env("NO_PROXY", "*")
        .env("no_proxy", "*")
        .output();
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
