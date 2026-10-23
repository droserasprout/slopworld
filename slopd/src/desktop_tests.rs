use super::*;
use crate::config::{Config, ProjectCfg, SessionCfg};
use crate::presets::SandboxPreset;
use std::os::unix::ffi::OsStringExt;
use std::os::unix::fs::PermissionsExt;
use std::path::{Path, PathBuf};

fn isolated() -> Option<PathBuf> {
    crate::test_support::isolated_with_env(|command, root| {
        command.env(
            "PATH",
            format!("{}:{}", root.display(), std::env::var("PATH").unwrap()),
        );
        command.env_remove("SLOPD_RUNTIME");
        for name in DESKTOP_VARIABLES {
            if *name != "XDG_RUNTIME_DIR" {
                command.env_remove(name);
            }
        }
    })
}

fn tool(root: &Path, script: &str) {
    let path = root.join("systemctl");
    std::fs::write(&path, format!("#!/bin/sh\n{script}\n")).unwrap();
    std::fs::set_permissions(path, std::fs::Permissions::from_mode(0o700)).unwrap();
}

fn manager(root: &Path, value: &serde_json::Value) {
    std::fs::write(
        root.join("manager.json"),
        serde_json::to_vec(&value).unwrap(),
    )
    .unwrap();
}

fn inputs() -> (Config, SessionCfg, ProjectCfg) {
    let preset = SandboxPreset {
        name: "desktop-test".into(),
        ro: vec!["$XAUTHORITY".into()],
        rw: vec!["$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY".into()],
        env: vec![
            "DISPLAY".into(),
            "WAYLAND_DISPLAY".into(),
            "XAUTHORITY".into(),
            "XDG_RUNTIME_DIR".into(),
        ],
        ..Default::default()
    };
    let session = SessionCfg {
        name: "desktop-agent".into(),
        cmd: Some("true".into()),
        sandbox: vec![preset.name.clone()],
        sandbox_snapshots: vec![preset],
        ..Default::default()
    };
    let project = ProjectCfg {
        name: "project".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    (Config::default(), session, project)
}

fn exported(plan: &crate::sandbox::LaunchPlan, name: &str) -> Option<String> {
    plan.environment
        .windows(3)
        .rfind(|args| args[0] == "--setenv" && args[1] == name)
        .map(|args| args[2].clone())
}

#[tokio::test]
async fn non_unicode_environment_survives_clipboard_execution_and_preset_validation() {
    let raw = OsString::from_vec(b"value-\xff".to_vec());
    let Some(root) = crate::test_support::isolated_with_env(|command, root| {
        command
            .env("PATH", root)
            .env("SLOPD_TEST_RAW", &raw)
            .env_remove("SLOPD_RUNTIME");
    }) else {
        return;
    };
    tool(
        &root,
        "printf '{\"DISPLAY\":\":77\",\"XDG_CURRENT_DESKTOP\":\"GNOME\"}'",
    );
    let xclip = root.join("xclip");
    std::fs::write(
        &xclip,
        "#!/bin/sh\nprintf '%s' \"$SLOPD_TEST_RAW\" > \"$SLOPD_TEST_ROOT/raw-environment\"\nprintf clipboard-text\n",
    ).unwrap();
    std::fs::set_permissions(&xclip, std::fs::Permissions::from_mode(0o700)).unwrap();

    let snapshot = Environment::current().await;
    assert_eq!(snapshot.get_os("SLOPD_TEST_RAW"), Some(raw.as_os_str()));
    assert_eq!(snapshot.get("SLOPD_TEST_RAW"), None);
    assert_eq!(snapshot.expand("$SLOPD_TEST_RAW"), "");
    assert_eq!(
        crate::clipboard::read_text().await.unwrap(),
        "clipboard-text"
    );
    assert_eq!(
        std::fs::read(root.join("raw-environment")).unwrap(),
        b"value-\xff"
    );

    let (mut cfg, mut session, project) = inputs();
    cfg.defaults.agent_shell = "/bin/sh".into();
    session.sandbox_snapshots[0]
        .env
        .push("SLOPD_TEST_RAW".into());
    let table = session.preset_table();
    crate::sandbox::validate_preset(&session.sandbox_snapshots[0], &table).unwrap();
    let plan = crate::sandbox::prepare_launch(&cfg, &session, &project)
        .await
        .unwrap();
    assert_eq!(exported(&plan, "DISPLAY").as_deref(), Some(":77"));
    assert_eq!(exported(&plan, "SLOPD_TEST_RAW"), None);
}

#[tokio::test]
async fn late_desktop_login_refreshes_forwarding_and_mounts_without_restarting_daemon() {
    let Some(root) = isolated() else { return };
    tool(&root, "/bin/cat \"$SLOPD_TEST_ROOT/manager.json\"");
    manager(&root, &serde_json::json!({}));
    let (cfg, session, project) = inputs();
    let early = crate::sandbox::prepare_launch(&cfg, &session, &project)
        .await
        .unwrap();
    assert_eq!(exported(&early, "DISPLAY"), None);
    let runtime = root.join("desktop $literal");
    std::fs::create_dir(&runtime).unwrap();
    let auth = runtime.join("auth $literal");
    std::fs::write(&auth, "synthetic authentication").unwrap();
    let socket = runtime.join("wayland-test");
    let _listener = std::os::unix::net::UnixListener::bind(&socket).unwrap();
    manager(
        &root,
        &serde_json::json!({
            "DISPLAY": ":77", "WAYLAND_DISPLAY": "wayland-test", "XAUTHORITY": auth,
            "XDG_RUNTIME_DIR": runtime, "PATH": "/untrusted", "SLOPD_TEST_UNTRUSTED": "secret"
        }),
    );
    let snapshot = Environment::current().await;
    assert_eq!(snapshot.get_os("PATH"), std::env::var_os("PATH").as_deref());
    assert!(snapshot.get_os("SLOPD_TEST_UNTRUSTED").is_none());
    // A later manager change cannot mix socket paths from one login with another's variables.
    manager(&root, &serde_json::json!({"DISPLAY": ":88"}));
    let table = session.preset_table();
    let presets =
        crate::sandbox::presets_with_environment(&cfg, &session, &project, &table, &snapshot)
            .unwrap();
    let ready = crate::sandbox::build_plan(&cfg, &session, &project, &presets, &snapshot).unwrap();
    assert_eq!(exported(&ready, "DISPLAY").as_deref(), Some(":77"));
    assert_eq!(exported(&ready, "XAUTHORITY").as_deref(), auth.to_str());
    assert!(
        ready
            .mounts
            .windows(3)
            .any(|args| args == ["--ro-bind", auth.to_str().unwrap(), auth.to_str().unwrap()])
    );
    assert!(
        ready
            .mounts
            .windows(3)
            .any(|args| args == ["--bind", socket.to_str().unwrap(), socket.to_str().unwrap()])
    );
    assert!(
        std::env::var("DISPLAY").is_err(),
        "process-global environment must stay untouched"
    );
    let next = crate::sandbox::prepare_launch(&cfg, &session, &project)
        .await
        .unwrap();
    assert_eq!(exported(&next, "DISPLAY").as_deref(), Some(":88"));
    assert_eq!(exported(&next, "XAUTHORITY"), None);
    assert!(!next.mounts.iter().any(|arg| arg == auth.to_str().unwrap()));
}

#[tokio::test]
async fn refreshed_paths_keep_guards_preset_authority_and_explicit_environment_precedence() {
    let Some(root) = isolated() else { return };
    tool(&root, "/bin/cat \"$SLOPD_TEST_ROOT/manager.json\"");
    manager(
        &root,
        &serde_json::json!({"DISPLAY": ":77", "XAUTHORITY": crate::paths::config_root()}),
    );
    let (cfg, mut session, project) = inputs();
    let error = crate::sandbox::prepare_launch(&cfg, &session, &project)
        .await
        .unwrap_err();
    assert!(
        error.to_string().contains("invalid sandbox preset"),
        "{error:#}"
    );
    let preset = session.sandbox_snapshots.first_mut().unwrap();
    preset.ro.clear();
    preset.rw.clear();
    preset.env.clear();
    let plain = crate::sandbox::prepare_launch(&cfg, &session, &project)
        .await
        .unwrap();
    assert_eq!(exported(&plain, "DISPLAY"), None);
    let preset = session.sandbox_snapshots.first_mut().unwrap();
    preset.env.push("DISPLAY".into());
    preset.setenv.insert("DISPLAY".into(), ":explicit".into());
    let explicit = crate::sandbox::prepare_launch(&cfg, &session, &project)
        .await
        .unwrap();
    assert_eq!(exported(&explicit, "DISPLAY").as_deref(), Some(":explicit"));
}

#[tokio::test]
async fn missing_failed_malformed_or_hung_manager_keeps_inherited_environment_and_recovers() {
    let Some(root) = isolated() else { return };
    let baseline = Environment::inherited();
    for script in [
        "exit 1",
        "printf 'not json'",
        "exec /bin/sleep 30",
        "printf '{}'",
    ] {
        tool(&root, script);
        assert_eq!(Environment::current().await, baseline);
    }
    tool(&root, "printf '{\"DISPLAY\":\":recovered\"}'");
    assert_eq!(
        Environment::current().await.get("DISPLAY"),
        Some(":recovered")
    );
}

#[test]
fn new_desktop_snapshot_clears_old_login_paths_without_importing_unrelated_values() {
    let mut environment = Environment {
        values: BTreeMap::from([
            ("DISPLAY".into(), ":old".into()),
            ("WAYLAND_DISPLAY".into(), "old-wayland".into()),
            ("XAUTHORITY".into(), "/old/auth".into()),
            ("PATH".into(), "/inherited/bin".into()),
        ]),
    };
    environment.refresh_desktop(&BTreeMap::from([
        ("DISPLAY".into(), ":new".into()),
        ("PATH".into(), "/other/bin".into()),
    ]));
    assert_eq!(environment.get("DISPLAY"), Some(":new"));
    assert!(environment.get_os("WAYLAND_DISPLAY").is_none());
    assert!(environment.get_os("XAUTHORITY").is_none());
    assert_eq!(environment.get("PATH"), Some("/inherited/bin"));
}

#[tokio::test]
async fn clipboard_tools_receive_current_display_and_keep_gnome_xwayland_policy() {
    let Some(root) = isolated() else { return };
    tool(&root, "/bin/cat \"$SLOPD_TEST_ROOT/manager.json\"");
    let xclip = root.join("xclip");
    std::fs::write(
        &xclip,
        "#!/bin/sh\nprintf '%s|%s|%s' \"$DISPLAY\" \"$XAUTHORITY\" \"$*\"\n",
    )
    .unwrap();
    std::fs::set_permissions(&xclip, std::fs::Permissions::from_mode(0o700)).unwrap();
    manager(
        &root,
        &serde_json::json!({"DISPLAY": ":77", "XAUTHORITY": "/current/auth", "XDG_CURRENT_DESKTOP": "GNOME"}),
    );
    assert_eq!(
        crate::clipboard::read_text().await.unwrap(),
        ":77|/current/auth|-selection clipboard -target UTF8_STRING -out"
    );
    assert_eq!(
        crate::clipboard::read_primary_text().await.unwrap(),
        ":77|/current/auth|-selection primary -target UTF8_STRING -out"
    );
    // Change the owner-side helper to a write sink; no test touches the real clipboard.
    std::fs::write(&xclip, "#!/bin/sh\nprintf '%s|%s' \"$DISPLAY\" \"$XAUTHORITY\" > \"$SLOPD_TEST_ROOT/copy-environment\"\n/bin/cat > \"$SLOPD_TEST_ROOT/copy-input\"\n").unwrap();
    crate::clipboard::write("test text").await.unwrap();
    assert_eq!(
        std::fs::read_to_string(root.join("copy-environment")).unwrap(),
        ":77|/current/auth"
    );
    assert_eq!(
        std::fs::read_to_string(root.join("copy-input")).unwrap(),
        "test text"
    );
}

#[tokio::test]
async fn absent_systemctl_preserves_a_manually_started_daemons_environment() {
    let Some(_root) = crate::test_support::isolated_with_env(|command, root| {
        command
            .env("PATH", root)
            .env("DISPLAY", ":manual")
            .env_remove("SLOPD_RUNTIME");
    }) else {
        return;
    };
    assert_eq!(Environment::current().await, Environment::inherited());
}

#[tokio::test]
async fn sidecar_does_not_query_the_host_user_manager() {
    let Some(root) = crate::test_support::isolated_with_env(|command, root| {
        command.env("PATH", root).env("SLOPD_RUNTIME", "slopcar");
    }) else {
        return;
    };
    tool(&root, ": > \"$SLOPD_TEST_ROOT/queried\"; exit 1");
    assert_eq!(Environment::current().await, Environment::inherited());
    assert!(!root.join("queried").exists());
}
