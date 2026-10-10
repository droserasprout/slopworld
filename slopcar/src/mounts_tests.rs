#![allow(
    clippy::unwrap_used,
    reason = "Test setup and assertions fail immediately"
)]
use super::*;
#[test]
fn rejects_home_root_state_overlap_and_ambiguous_paths() {
    let root = tempfile::tempdir().unwrap();
    let settings = crate::test_support::settings(root.path());
    std::fs::create_dir_all(settings.config.join("child")).unwrap();
    for path in [
        Path::new("/"),
        &settings.home,
        &settings.config,
        &settings.config.join("child"),
    ] {
        assert!(workspace(path, &settings).is_err(), "{}", path.display());
    }
    for path in [
        "relative",
        "/work/../secret",
        "/work/./secret",
        "/work,a",
        "/work\na",
    ] {
        assert!(check_path(Path::new(path)).is_err());
    }
    let work = root.path().join("work with spaces");
    std::fs::create_dir(&work).unwrap();
    assert!(
        workspace(&work, &settings)
            .unwrap()
            .contains("work with spaces")
    );
}
#[test]
fn credentials_cannot_cover_state_or_expose_docker_configuration() {
    let root = tempfile::tempdir().unwrap();
    let settings = crate::test_support::settings(root.path());
    let auth = settings.home.join("auth");
    std::fs::write(&auth, "secret").unwrap();
    for target in [
        "/",
        "/home/slop",
        "/home/slop/.config",
        "/home/slop/.config/slopworld/x",
        "/home/slop/.local/share",
        "/home/slop/../slop/auth",
    ] {
        assert!(
            credential(
                OsStr::new(&format!("{}={target}", auth.display())),
                true,
                &settings
            )
            .is_err()
        );
    }
    let spec = format!("{}=/home/slop/.codex/auth.json", auth.display());
    assert!(
        credential(OsStr::new(&spec), true, &settings)
            .unwrap()
            .ends_with(",readonly")
    );
    assert!(
        !credential(OsStr::new(&spec), false, &settings)
            .unwrap()
            .ends_with(",readonly")
    );
    let docker = settings.home.join(".docker");
    std::fs::create_dir(&docker).unwrap();
    assert!(
        credential(
            OsStr::new(&format!("{}=/home/slop/auth", docker.display())),
            false,
            &settings
        )
        .is_err()
    );
}
#[cfg(unix)]
#[test]
fn resolves_symlinked_state_ancestors_and_rejects_credential_symlinks() {
    use std::os::unix::fs::symlink;
    let root = tempfile::tempdir().unwrap();
    let mut settings = crate::test_support::settings(root.path());
    let work = root.path().join("work");
    std::fs::create_dir(&work).unwrap();
    let alias = root.path().join("alias");
    symlink(&work, &alias).unwrap();
    settings.config = alias.join("not-created-yet");
    assert!(workspace(&work, &settings).is_err());
    let file = root.path().join("auth");
    std::fs::write(&file, "secret").unwrap();
    let link = root.path().join("auth-link");
    symlink(&file, &link).unwrap();
    assert!(
        credential(
            OsStr::new(&format!("{}=/home/slop/auth", link.display())),
            false,
            &settings
        )
        .is_err()
    );
    let dangling = root.path().join("dangling");
    symlink(root.path().join("missing"), &dangling).unwrap();
    settings.config = dangling.join("config");
    assert!(workspace(&file, &settings).is_err());
}
