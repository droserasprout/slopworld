use super::*;

fn fixture() -> (PathBuf, PathBuf, PathBuf, SandboxPreset) {
    let root = std::env::temp_dir().join(format!("slopd-seed-review-{}", uuid::Uuid::new_v4()));
    let host = root.join("host");
    let copy = root.join("private/copy");
    std::fs::create_dir_all(host.join("selected")).unwrap();
    std::fs::write(host.join("auth"), "host credentials").unwrap();
    let preset = SandboxPreset {
        seed: vec![host.join("selected").to_string_lossy().into_owned()],
        ..Default::default()
    };
    (root, host, copy, preset)
}

#[test]
fn failed_seed_is_not_published_and_retry_preserves_completed_agent_changes() {
    use std::os::unix::ffi::OsStrExt;
    let (root, host, copy, preset) = fixture();
    // A socket is deterministically uncopyable, even for a privileged test user.
    let socket = host.join("selected/socket");
    let listener = std::os::unix::net::UnixListener::bind(&socket).unwrap();
    assert!(seed_into(&preset, host.to_str().unwrap(), &copy).is_err());
    assert!(!copy.exists());
    assert_eq!(
        std::fs::read_dir(copy.parent().unwrap()).unwrap().count(),
        0
    );
    drop(listener);
    std::fs::remove_file(socket).unwrap();
    seed_into(&preset, host.to_str().unwrap(), &copy).unwrap();
    std::fs::write(copy.join("auth"), "agent change").unwrap();
    std::fs::write(host.join("auth"), "new host value").unwrap();
    seed_into(&preset, host.to_str().unwrap(), &copy).unwrap();
    assert_eq!(std::fs::read(copy.join("auth")).unwrap(), b"agent change");
    // A lookup error must fail rather than become optional missing state.
    let invalid = root.join(std::ffi::OsStr::from_bytes(b"invalid\0path"));
    assert!(seed_into(&preset, invalid.to_str().unwrap(), &root.join("bad")).is_err());
    seed_into(
        &preset,
        root.join("missing").to_str().unwrap(),
        &root.join("absent"),
    )
    .unwrap();
    assert!(!root.join("absent").exists());
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn seed_omits_symlink_cycles_external_links_and_linked_seed_parents() {
    use std::os::unix::fs::symlink;
    let (root, host, copy, mut preset) = fixture();
    let outside = root.join("outside");
    std::fs::create_dir_all(&outside).unwrap();
    std::fs::write(outside.join("secret"), "never copy").unwrap();
    symlink(&host, host.join("selected/ancestor")).unwrap();
    symlink(&outside, host.join("selected/external")).unwrap();
    symlink(outside.join("secret"), host.join("top-link")).unwrap();
    symlink(&outside, host.join("linked")).unwrap();
    preset
        .seed
        .push(host.join("linked/secret").to_string_lossy().into_owned());
    std::fs::write(host.join("selected/kept"), "keep").unwrap();
    seed_into(&preset, host.to_str().unwrap(), &copy).unwrap();
    assert_eq!(std::fs::read(copy.join("selected/kept")).unwrap(), b"keep");
    for path in [
        "selected/ancestor",
        "selected/external",
        "top-link",
        "linked",
    ] {
        assert!(
            std::fs::symlink_metadata(copy.join(path)).is_err(),
            "{path}"
        );
    }
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn excessive_directory_nesting_fails_without_publishing() {
    let (root, host, copy, preset) = fixture();
    let mut deep = host.join("selected");
    for _ in 0..128 {
        deep.push("d");
    }
    std::fs::create_dir_all(deep).unwrap();
    assert!(seed_into(&preset, host.to_str().unwrap(), &copy).is_err());
    assert!(!copy.exists());
    std::fs::remove_dir_all(root).unwrap();
}

#[cfg(target_os = "linux")]
#[test]
fn publishing_never_replaces_an_existing_private_copy() {
    let (root, _host, copy, _preset) = fixture();
    std::fs::create_dir_all(copy.parent().unwrap()).unwrap();
    let stage = root.join("stage");
    std::fs::write(&stage, "initial copy").unwrap();
    std::fs::write(&copy, "agent change").unwrap();
    publish(&stage, &copy).unwrap();
    assert_eq!(std::fs::read(&copy).unwrap(), b"agent change");
    std::fs::remove_dir_all(root).unwrap();
}

/// Initialization copies top-level files and the subdirectories that the preset specifies.
/// It excludes other subdirectories. A second initialization preserves the agent's changes.
#[test]
fn a_private_tree_is_seeded_once_with_the_files_on_top_and_what_the_preset_names() {
    let root = std::env::temp_dir().join(format!("slopd-seed-{}", std::process::id()));
    drop(std::fs::remove_dir_all(&root));
    let host = root.join("host");
    std::fs::create_dir_all(host.join("agents")).unwrap();
    std::fs::create_dir_all(host.join("projects")).unwrap();
    std::fs::write(host.join("auth.json"), "secret").unwrap();
    std::fs::write(host.join("agents/one.md"), "mine").unwrap();
    std::fs::write(host.join("projects/bulk"), "not this").unwrap();

    let pr = SandboxPreset {
        name: "t".into(),
        private: vec![host.to_string_lossy().into_owned()],
        seed: vec![host.join("agents").to_string_lossy().into_owned()],
        ..Default::default()
    };
    let copy = root.join("copy");
    seed_into(&pr, &host.to_string_lossy(), &copy).unwrap();

    // Copy top-level credentials without an explicit seed entry.
    assert_eq!(
        std::fs::read_to_string(copy.join("auth.json")).unwrap(),
        "secret"
    );
    // Copy only the subdirectories that the preset specifies.
    assert_eq!(
        std::fs::read_to_string(copy.join("agents/one.md")).unwrap(),
        "mine"
    );
    assert!(!copy.join("projects").exists(), "unnamed bulk came across");

    // Preserve the agent's changes when the host source changes after initialization.
    std::fs::write(copy.join("auth.json"), "the agent's own").unwrap();
    std::fs::write(host.join("auth.json"), "changed since").unwrap();
    seed_into(&pr, &host.to_string_lossy(), &copy).unwrap();
    assert_eq!(
        std::fs::read_to_string(copy.join("auth.json")).unwrap(),
        "the agent's own"
    );

    drop(std::fs::remove_dir_all(&root));
}

/// Exclude top-level files that `skip` or `shared` specifies.
/// This keeps user prompt history out of the private copy.
/// Shared credentials use host bind mounts. Copying them could leave obsolete tokens in the session directory.
#[test]
fn the_files_on_top_are_cut_back_by_skip_and_by_what_is_shared() {
    let root = std::env::temp_dir().join(format!("slopd-top-{}", std::process::id()));
    drop(std::fs::remove_dir_all(&root));
    let host = root.join("host");
    std::fs::create_dir_all(&host).unwrap();
    std::fs::write(host.join("settings.json"), "wanted").unwrap();
    std::fs::write(host.join("history.jsonl"), "every prompt ever typed").unwrap();
    std::fs::write(host.join(".credentials.json"), "rotates").unwrap();

    let pr = SandboxPreset {
        name: "t".into(),
        private: vec![host.to_string_lossy().into_owned()],
        skip: vec![host.join("history.jsonl").to_string_lossy().into_owned()],
        shared: vec![host
            .join(".credentials.json")
            .to_string_lossy()
            .into_owned()],
        ..Default::default()
    };
    let copy = root.join("copy");
    seed_into(&pr, &host.to_string_lossy(), &copy).unwrap();

    assert!(
        copy.join("settings.json").exists(),
        "the ordinary file went"
    );
    assert!(
        !copy.join("history.jsonl").exists(),
        "the user's prompt history came across"
    );
    assert!(
        !copy.join(".credentials.json").exists(),
        "a copy of the credential was left in the session directory"
    );

    drop(std::fs::remove_dir_all(&root));
}

/// Copy a complete seed directory except the paths that `skip` specifies.
/// This permits model settings and nested configuration files without copying session transcripts.
#[test]
fn a_seeded_directory_comes_across_whole_bar_what_skip_names() {
    let root = std::env::temp_dir().join(format!("slopd-skip-{}", std::process::id()));
    drop(std::fs::remove_dir_all(&root));
    let host = root.join("host");
    std::fs::create_dir_all(host.join("agent/sessions")).unwrap();
    std::fs::create_dir_all(host.join("agent/nested/deep")).unwrap();
    std::fs::write(host.join("agent/models-store.json"), "opus").unwrap();
    std::fs::write(host.join("agent/nested/deep/kept.json"), "kept").unwrap();
    std::fs::write(host.join("agent/sessions/big.jsonl"), "21MB of talk").unwrap();

    let pr = SandboxPreset {
        name: "t".into(),
        private: vec![host.to_string_lossy().into_owned()],
        seed: vec![host.join("agent").to_string_lossy().into_owned()],
        skip: vec![host.join("agent/sessions").to_string_lossy().into_owned()],
        ..Default::default()
    };
    let copy = root.join("copy");
    seed_into(&pr, &host.to_string_lossy(), &copy).unwrap();

    // Preserve settings and nested files outside excluded paths.
    assert_eq!(
        std::fs::read_to_string(copy.join("agent/models-store.json")).unwrap(),
        "opus"
    );
    assert!(copy.join("agent/nested/deep/kept.json").exists());
    // Exclude the transcript directory and its contents.
    assert!(
        !copy.join("agent/sessions").exists(),
        "the transcripts came across"
    );

    drop(std::fs::remove_dir_all(&root));
}
