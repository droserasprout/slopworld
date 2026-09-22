use super::*;
use crate::sandbox::state_root;

/// The guard, in both directions: a path *inside* what must stay out of reach, and a path
/// *above* it. `~/.config` is as much a road to the token as the file itself.
#[test]
fn no_bind_list_reaches_the_token_the_presets_or_another_session() {
    assert!(refused("/").is_some());
    if let Some(home) = dirs::home_dir() {
        assert!(refused(&home.to_string_lossy()).is_some());
        // A directory *in* the home is ordinary; the home itself is not.
        assert!(refused(&home.join("src").to_string_lossy()).is_none());
    }

    for kept in [Config::path_in_use(), Table::dir(), state_root()] {
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

/// The other side of the guard: nothing that ships is caught by it. A refusal is silent
/// bar a log line, so a preset broken this way is a session that quietly cannot reach what
/// it was ticked for - and `~/.local/share/pnpm` sits one directory from `state_root`.
#[test]
fn no_shipped_preset_asks_for_something_refused() {
    let t = Table::load();
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
        shared: vec![root
            .join(".toolbox/credentials")
            .to_string_lossy()
            .into_owned()],
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

    let _ = std::fs::remove_dir_all(&root);
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
    assert!(error.contains("reaches"), "{error}");

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
        sandbox: vec![candidate.clone(), dependency],
        commands: Vec::new(),
    };

    validate_preset(&candidate, &table).unwrap();
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
    let _ = std::fs::remove_dir_all(&root);
    std::fs::create_dir_all(&root).unwrap();
    let home = dirs::home_dir().expect("home directory");
    let link = root.join("home");
    symlink(home, &link).unwrap();
    let alias = link;
    assert!(refused(&alias.to_string_lossy()).is_some());
    let _ = std::fs::remove_dir_all(&root);
}

#[test]
fn shipped_presets_pass_the_central_validator() {
    let table = Table::builtins();
    for preset in &table.sandbox {
        validate_preset_name(&preset.name, &table)
            .unwrap_or_else(|e| panic!("{} is invalid: {e:#}", preset.name));
    }
}
