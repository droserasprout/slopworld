#![allow(
    clippy::unwrap_used,
    reason = "Test setup and assertions fail immediately"
)]
//! Isolated filesystem settings shared by owner tests.
use crate::settings::Settings;
pub(crate) fn settings(root: &std::path::Path) -> Settings {
    let home = root.join("home");
    std::fs::create_dir_all(&home).unwrap();
    Settings {
        home: home.clone(),
        config: home.join("config"),
        data: home.join("data"),
        image: "test-image".into(),
        container: "test-car".into(),
        port: 7718,
        memory: "8g".into(),
        cpus: "4".into(),
        pids: "4096".into(),
    }
}
