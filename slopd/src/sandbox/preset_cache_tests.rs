use super::*;

#[test]
fn launch_prepares_planned_cache_after_catalog_replacement() {
    use crate::config::{Config, ProjectCfg, SessionCfg};
    use crate::presets::{PresetDefinition, validate_and_save};

    let Some(root) = crate::test_support::isolated_with_env(|command, root| {
        command.env("SLOPD_CONFIG_ROOT", root.join("config"));
    }) else {
        return;
    };
    let original = root.join("original-cache").to_string_lossy().into_owned();
    let replacement = root
        .join("replacement-cache")
        .to_string_lossy()
        .into_owned();
    let mut preset = SandboxPreset {
        name: "cache-test".into(),
        cache: vec![original.clone()],
        ..Default::default()
    };
    validate_and_save(PresetDefinition::Sandbox(Box::new(preset.clone()))).unwrap();
    assert!(crate::presets::reload());
    let cfg = Config::default();
    let session = SessionCfg {
        name: "agent".into(),
        project: "project".into(),
        sandbox: vec![preset.name.clone()],
        ..Default::default()
    };
    let workspace = root.join("workspace");
    std::fs::create_dir(&workspace).unwrap();
    let project = ProjectCfg {
        name: "project".into(),
        dir: workspace.to_string_lossy().into_owned(),
        ..Default::default()
    };
    // Fix the ordering explicitly: publish the new catalog only after planning.
    let plan = crate::sandbox::build_plan(&cfg, &session, &project).unwrap();
    assert!(!Path::new(&original).exists());
    preset.cache = vec![replacement.clone()];
    validate_and_save(PresetDefinition::Sandbox(Box::new(preset))).unwrap();
    assert!(crate::presets::reload());
    let next = crate::sandbox::build_plan(&cfg, &session, &project).unwrap();
    assert_eq!(next.cache_dirs, vec![replacement.clone()]);

    crate::sandbox::prepare_preset_caches(&plan.cache_dirs).unwrap();
    assert!(Path::new(&original).is_dir());
    assert!(!Path::new(&replacement).exists());
    assert!(
        plan.mounts
            .windows(3)
            .any(|args| { args[0] == "--bind" && args[1] == original && args[2] == original })
    );
}

#[test]
fn preview_does_not_create_cache_and_launch_preparation_is_repeatable() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let cache = root.join("missing/cache");
    let preset = SandboxPreset {
        name: "cache-test".into(),
        cache: vec![cache.to_string_lossy().into_owned()],
        ..Default::default()
    };
    let resolved = paths(&[&preset, &preset]).unwrap();
    assert_eq!(resolved.len(), 1);
    assert!(!cache.exists());
    prepare(&resolved).unwrap();
    std::fs::write(cache.join("retained"), "data").unwrap();
    prepare(&resolved).unwrap();
    assert_eq!(
        std::fs::read_to_string(cache.join("retained")).unwrap(),
        "data"
    );
}

#[test]
fn occupied_cache_file_fails_preparation_without_removing_it() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let cache = root.join("occupied-cache-file");
    std::fs::write(&cache, "retained").unwrap();
    prepare(&[cache.to_string_lossy().into_owned()]).unwrap_err();
    assert_eq!(std::fs::read_to_string(cache).unwrap(), "retained");
}

#[test]
fn relative_cache_path_is_rejected_before_creation() {
    let preset = SandboxPreset {
        name: "cache-test".into(),
        cache: vec!["relative/cache".into()],
        ..Default::default()
    };
    paths(&[&preset]).unwrap_err();
}
