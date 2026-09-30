//! Run environment-dependent tests alone so overrides cannot affect parallel tests.
use std::path::PathBuf;

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
    std::fs::remove_dir_all(&root).unwrap();
    let output = output.expect("start isolated test");
    assert!(
        output.status.success(),
        "isolated test failed:\n{}\n{}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    );
    None
}
