use super::*;

fn sandbox() -> PresetDefinition {
    PresetDefinition::Sandbox(Box::new(SandboxPreset {
        name: "dependency".into(),
        ..Default::default()
    }))
}

fn command() -> PresetDefinition {
    PresetDefinition::Command(Box::new(CommandPreset {
        name: "dependent".into(),
        cmd: "true".into(),
        sandbox: vec!["dependency".into()],
        ..Default::default()
    }))
}

#[test]
fn mutations_validate_committed_files_without_waiting_for_catalog_reload() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    std::env::set_var("SLOPD_PRESETS", root.join("presets"));
    assert!(!table().contains(PresetKind::SandboxPresets, "dependency"));
    validate_and_save(sandbox()).unwrap();
    validate_and_save(command()).unwrap();
    assert!(matches!(
        delete_user(PresetKind::SandboxPresets, "dependency"),
        Err(PresetError::Invalid(_))
    ));
    delete_user(PresetKind::AppPresets, "dependent").unwrap();
    delete_user(PresetKind::SandboxPresets, "dependency").unwrap();
    assert!(matches!(
        validate_and_save(command()),
        Err(PresetError::Invalid(_))
    ));
}

#[test]
fn concurrent_delete_and_dependency_creation_cannot_commit_an_unresolved_reference() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    std::env::set_var("SLOPD_PRESETS", root.join("presets"));
    validate_and_save(sandbox()).unwrap();
    let guard = mutation_guard().unwrap();
    let start = Arc::new(std::sync::Barrier::new(3));
    let delete_start = start.clone();
    let delete = std::thread::spawn(move || {
        delete_start.wait();
        delete_user(PresetKind::SandboxPresets, "dependency")
    });
    let save_start = start.clone();
    let save = std::thread::spawn(move || {
        save_start.wait();
        validate_and_save(command())
    });
    start.wait();
    drop(guard);
    let deleted = delete.join().unwrap().is_ok();
    let saved = save.join().unwrap().is_ok();
    assert_ne!(deleted, saved);
    let current = Table::try_load_from(&Table::dir()).unwrap();
    if saved {
        assert!(current.contains(PresetKind::SandboxPresets, "dependency"));
        assert!(current.contains(PresetKind::AppPresets, "dependent"));
    } else {
        assert!(!current.contains(PresetKind::SandboxPresets, "dependency"));
        assert!(!current.contains(PresetKind::AppPresets, "dependent"));
    }
}
