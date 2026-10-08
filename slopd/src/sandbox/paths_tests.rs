use super::*;
use crate::sandbox::state_root;

#[test]
fn every_preset_path_field_rejects_relative_paths_after_expansion() {
    let Some(_) = crate::test_support::isolated_with_env(|command, _| {
        command.env("SLOPD_TEST_PRESET_PATH", "relative");
    }) else {
        return;
    };
    for kind in ["ro", "rw", "dev", "private", "seed", "skip", "shared"] {
        for raw in [".", "../outside", "relative", "$SLOPD_TEST_PRESET_PATH"] {
            let mut preset = SandboxPreset {
                name: "relative-path".into(),
                ..Default::default()
            };
            let paths = match kind {
                "ro" => &mut preset.ro,
                "rw" => &mut preset.rw,
                "dev" => &mut preset.dev,
                "private" => &mut preset.private,
                "seed" => &mut preset.seed,
                "skip" => &mut preset.skip,
                "shared" => &mut preset.shared,
                _ => unreachable!(),
            };
            paths.push(raw.into());
            let error = validate_preset(&preset, &Table::builtins())
                .unwrap_err()
                .to_string();
            assert!(
                error.contains("must be absolute after expansion"),
                "{kind}: {error}"
            );
        }
    }
}

// Shipped definitions must be valid independently of installed tools, dangling
// host aliases, and user preset overrides. Environment changes stay in a child.
fn isolated_preset_environment() -> Option<std::path::PathBuf> {
    crate::test_support::isolated_with_env(|command, root| {
        let home = root.join("home");
        std::fs::create_dir_all(&home).unwrap();
        for (variable, path) in [
            ("HOME", home.clone()),
            ("XDG_CONFIG_HOME", home.join(".config")),
            ("XDG_DATA_HOME", home.join(".local/share")),
            ("XDG_CACHE_HOME", home.join(".cache")),
            ("XDG_RUNTIME_DIR", root.join("runtime")),
            ("SLOPD_CONFIG", home.join(".config/slopworld/config.toml")),
            ("SLOPD_PRESETS", home.join(".config/slopworld")),
        ] {
            command.env(variable, path);
        }
        for variable in [
            "XAUTHORITY",
            "WAYLAND_DISPLAY",
            "ANDROID_HOME",
            "ANDROID_SDK_ROOT",
            "JAVA_HOME",
            "DEVELOPER_DIR",
            "SSH_AUTH_SOCK",
            "SLOPWORLD_PROFILE",
            "SLOPWORLD_GAME",
        ] {
            command.env(variable, root.join(variable));
        }
    })
}

/// Reject protected paths, their ancestors, and their descendants.
/// Mounting `~/.config` can expose the token just as mounting the token file can.
#[test]
fn no_bind_list_reaches_the_token_the_presets_or_another_session() {
    assert!(refused("/").is_some());
    if let Some(home) = dirs::home_dir() {
        assert!(refused(&home.to_string_lossy()).is_some());
        // Permit an unprotected directory under home, but reject home itself.
        assert!(refused(&home.join("src").to_string_lossy()).is_none());
    }

    for kept in [
        Config::path_in_use(),
        Config::recovery_path_for(&Config::path_in_use()),
        Table::dir(),
        state_root(),
    ] {
        assert!(
            refused(&kept.to_string_lossy()).is_some(),
            "{} is bindable",
            kept.display()
        );
        assert!(
            refused(&kept.join("within").to_string_lossy()).is_some(),
            "something under {} is bindable",
            kept.display()
        );
        let above = kept.parent().expect("a parent");
        assert!(
            refused(&above.to_string_lossy()).is_some(),
            "{} contains {} and is bindable",
            above.display(),
            kept.display()
        );
    }

    // The ordinary case, and the one every preset depends on.
    assert!(refused("/usr").is_none());
    assert!(refused("/etc").is_none());
}

/// Check that the path guard permits preset paths.
/// A rejected path produces a log message but can leave the session without required files.
/// For example, `~/.local/share/pnpm` shares a parent directory with `state_root`.
#[test]
fn no_shipped_preset_asks_for_something_refused() {
    let Some(_) = isolated_preset_environment() else {
        return;
    };
    let t = Table::builtins();
    for pr in &t.sandbox {
        for path in pr
            .ro
            .iter()
            .chain(&pr.rw)
            .chain(&pr.dev)
            .chain(&pr.private)
            .chain(&pr.seed)
        {
            let full = expand(path);
            if full.is_empty() {
                continue; // an unset $VAR, which `paths` drops anyway
            }
            assert!(
                refused(&full).is_none(),
                "preset {} asks for {path}, which the guard refuses: {}",
                pr.name,
                refused(&full).unwrap_or_default()
            );
        }
    }
}
#[test]
fn preset_validation_covers_private_seed_skip_and_shared_paths() {
    let root = std::env::temp_dir().join(format!("slopd-validation-{}", std::process::id()));
    let private = root.join(".tool");
    let mut p = SandboxPreset {
        name: "tool".into(),
        private: vec![private.to_string_lossy().into_owned()],
        shared: vec![
            root.join(".toolbox/credentials")
                .to_string_lossy()
                .into_owned(),
        ],
        ..Default::default()
    };
    let table = Table {
        sandbox: vec![p.clone()],
        commands: Vec::new(),
    };

    let error = validate_preset(&p, &table).unwrap_err().to_string();
    assert!(error.contains("must be inside a private path"), "{error}");

    p.shared.clear();
    p.seed = vec![root.join("outside").to_string_lossy().into_owned()];
    let error = validate_preset(&p, &table).unwrap_err().to_string();
    assert!(error.contains("seed path"), "{error}");

    p.seed.clear();
    p.skip = vec![root.join("outside").to_string_lossy().into_owned()];
    let error = validate_preset(&p, &table).unwrap_err().to_string();
    assert!(error.contains("skip path"), "{error}");

    drop(std::fs::remove_dir_all(&root));
}

#[test]
fn preset_validation_rejects_protected_private_paths_and_dependency_cycles() {
    let protected = Config::path_in_use().to_string_lossy().into_owned();
    let p = SandboxPreset {
        name: "tool".into(),
        private: vec![protected],
        ..Default::default()
    };
    let table = Table {
        sandbox: vec![p.clone()],
        commands: Vec::new(),
    };
    let error = validate_preset(&p, &table).unwrap_err().to_string();
    assert!(error.contains("exposes"), "{error}");

    let table = Table {
        sandbox: vec![
            SandboxPreset {
                name: "a".into(),
                requires: vec!["b".into()],
                ..Default::default()
            },
            SandboxPreset {
                name: "b".into(),
                requires: vec!["a".into()],
                ..Default::default()
            },
        ],
        commands: Vec::new(),
    };
    let error = validate_preset_name("a", &table).unwrap_err().to_string();
    assert!(error.contains("dependency cycle"), "{error}");
}

#[test]
fn sandbox_validation_uses_the_candidate_replacement() {
    let dependency = SandboxPreset {
        name: "dependency".into(),
        ..Default::default()
    };
    let candidate = SandboxPreset {
        name: "tool".into(),
        requires: vec!["dependency".into()],
        ..Default::default()
    };
    let table = Table {
        sandbox: vec![
            SandboxPreset {
                name: "tool".into(),
                ro: vec!["/".into()],
                ..Default::default()
            },
            dependency,
        ],
        commands: Vec::new(),
    };

    validate_preset(&candidate, &table).unwrap();
    let invalid = SandboxPreset {
        ro: vec!["/".into()],
        ..candidate.clone()
    };
    let valid_table = Table {
        sandbox: vec![candidate.clone()],
        commands: vec![],
    };
    assert!(
        validate_preset(&invalid, &valid_table)
            .unwrap_err()
            .to_string()
            .contains("exposes")
    );
}

#[test]
fn refused_normalizes_parent_components_before_checking_protected_paths() {
    let config = Config::path_in_use();
    let parent = config.parent().expect("config parent");
    let alias = parent
        .join("..")
        .join(parent.file_name().expect("config directory"))
        .join(config.file_name().expect("config file"));
    assert!(refused(&alias.to_string_lossy()).is_some());
}

#[cfg(unix)]
#[test]
fn refused_follows_existing_symlink_parents_before_checking_protected_paths() {
    use std::os::unix::fs::symlink;

    let root = std::env::temp_dir().join(format!("slopd-symlink-{}", std::process::id()));
    drop(std::fs::remove_dir_all(&root));
    std::fs::create_dir_all(&root).unwrap();
    let home = dirs::home_dir().expect("home directory");
    let link = root.join("home");
    symlink(home, &link).unwrap();
    assert!(refused(&link.to_string_lossy()).is_some());
    // Resolve the link before preserving a missing suffix under protected state.
    let real = root.join("real");
    std::fs::create_dir_all(&real).unwrap();
    let real_link = root.join("real-link");
    symlink(&real, &real_link).unwrap();
    assert_eq!(
        safety_path(&real_link.join("missing-child")).unwrap(),
        real.join("missing-child")
    );
    let state_link = root.join("state");
    symlink(state_root(), &state_link).unwrap();
    assert!(refused(&state_link.join("missing-child").to_string_lossy()).is_some());
    drop(std::fs::remove_dir_all(&root));
}

#[test]
fn shipped_presets_pass_the_central_validator() {
    let Some(_) = isolated_preset_environment() else {
        return;
    };
    let table = Table::builtins();
    for preset in &table.sandbox {
        validate_preset_name(&preset.name, &table)
            .unwrap_or_else(|e| panic!("{} is invalid: {e:#}", preset.name));
    }
}

#[cfg(unix)]
#[test]
fn unresolved_symlinks_and_lookup_errors_fail_closed() {
    use std::os::unix::fs::symlink;
    let root = std::env::temp_dir().join(format!("slopd-safety-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&root).unwrap();
    let looped = root.join("loop");
    symlink(&looped, &looped).unwrap();
    let dangling = root.join("dangling");
    symlink(root.join("missing"), &dangling).unwrap();
    for path in [&looped, &dangling] {
        safety_path(path).unwrap_err();
        assert!(refused(&path.to_string_lossy()).is_some());
        assert!(overlaps(&path.to_string_lossy(), "/usr"));
    }
    std::fs::remove_dir_all(root).unwrap();
}

#[cfg(unix)]
#[test]
fn missing_suffix_parent_traversal_rechecks_existing_aliases() {
    let root = std::env::temp_dir().join(format!("slopd-safety-parent-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&root).unwrap();
    std::os::unix::fs::symlink(dirs::home_dir().unwrap(), root.join("home")).unwrap();
    assert!(refused(&root.join("missing/../home").to_string_lossy()).is_some());
    std::fs::remove_dir_all(root).unwrap();
}
