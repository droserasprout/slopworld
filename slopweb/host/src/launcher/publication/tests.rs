//! Publication commits, immutable reader leases, and bounded compilation caches.
use super::{retirement::retire_after_readers, *};
use base64::{Engine, engine::general_purpose::STANDARD};
use sha2::{Digest, Sha256};
use std::sync::{Arc, Barrier};

fn candidate(cache: &Path, name: &str) -> PathBuf {
    let root = cache.join(name);
    let framework = root.join("_framework");
    fs::create_dir_all(&framework).unwrap();
    for asset in ["index.html", "bootstrap.js", "main.js", "assembly-mode.js"] {
        fs::write(root.join(asset), asset).unwrap();
    }
    let digest = STANDARD.encode(Sha256::digest(name.as_bytes()));
    let manifest = serde_json::json!({"resources":{"wasmNative":[{"name":"dotnet.native.wasm","hash":format!("sha256-{digest}")}]}});
    fs::write(
        framework.join("dotnet.js"),
        format!("/*json-start*/{manifest}/*json-end*/"),
    )
    .unwrap();
    fs::write(framework.join("dotnet.native.wasm"), name).unwrap();
    fs::write(root.join(".ready"), generation_key(&root).unwrap()).unwrap();
    root
}

#[test]
fn invalid_candidate_leaves_current_intact_and_retry_collects_superseded_outputs() {
    let temp = tempfile::tempdir().unwrap();
    let cache = temp.path();
    let publication = Publication::begin(cache).unwrap();
    let first = candidate(cache, &"1".repeat(24));
    drop(publication.commit(&first, &[]).unwrap());
    let second = candidate(cache, &format!("aot-runtimes/{}", "2".repeat(64)));
    let wasm = second.join("_framework/dotnet.native.wasm");
    fs::write(&wasm, "damaged").unwrap();
    assert!(publication.commit(&second, &[]).is_err());
    assert_eq!(fs::canonicalize(cache.join("current")).unwrap(), first);
    assert!(!second.exists());
    candidate(cache, second.strip_prefix(cache).unwrap().to_str().unwrap());

    let previous_build = cache.join("aot-builds").join("a".repeat(64));
    let workspace = cache.join("aot-workspaces").join("b".repeat(64));
    for path in [&previous_build, &workspace] {
        fs::create_dir_all(path).unwrap();
        File::create(path.join("build.lock")).unwrap();
    }
    let inputs = vec![workspace.clone()];
    drop(publication.commit(&second, &inputs).unwrap());
    assert_eq!(
        fs::read_link(cache.join("current")).unwrap(),
        second.strip_prefix(cache).unwrap()
    );
    assert!(!first.exists());
    assert!(!previous_build.exists());
    assert!(workspace.exists());
    drop(publication.commit(&second, &inputs).unwrap());
    assert_eq!(Runtime::open(&cache.join("current")).unwrap().root, second);
}

#[test]
fn last_reader_release_reclaims_the_old_runtime_and_preserves_the_new_selection() {
    let temp = tempfile::tempdir().unwrap();
    let cache = temp.path();
    let first = candidate(cache, &"1".repeat(24));
    let publication = Publication::begin(cache).unwrap();
    let reader = publication.commit(&first, &[]).unwrap();
    let other = Runtime::open(&cache.join("current")).unwrap();
    let second = candidate(cache, &"2".repeat(24));
    // Stand in for the detached collector and drive its lifetime explicitly.
    let marker = File::open(first.join(".ready")).unwrap();
    FileExt::lock_exclusive(&marker).unwrap();
    drop(publication.commit(&second, &[]).unwrap());
    drop(publication);
    assert_eq!(fs::canonicalize(cache.join("current")).unwrap(), second);
    drop(reader);
    assert!(other.root.join("_framework/dotnet.js").is_file());
    let started = Arc::new(Barrier::new(2));
    std::thread::scope(|scope| {
        let barrier = started.clone();
        let relative = first.strip_prefix(cache).unwrap();
        let cleanup = scope.spawn(move || {
            barrier.wait();
            retire_after_readers(cache, relative).unwrap();
        });
        started.wait();
        assert!(first.exists());
        drop(other);
        cleanup.join().unwrap();
    });
    assert!(!first.exists());
    assert!(second.exists());
}

#[test]
fn delayed_retirement_rechecks_the_selected_runtime_and_its_compiler_input() {
    let temp = tempfile::tempdir().unwrap();
    let cache = temp.path();
    let base = candidate(cache, &"1".repeat(24));
    let compiled = candidate(cache, &format!("aot-runtimes/{}", "2".repeat(64)));
    drop(
        Publication::begin(cache)
            .unwrap()
            .commit(&compiled, std::slice::from_ref(&base))
            .unwrap(),
    );
    for path in [&base, &compiled] {
        retire_after_readers(cache, path.strip_prefix(cache).unwrap()).unwrap();
        assert!(path.is_dir());
    }
    assert!(retire_after_readers(cache, Path::new("../foreign")).is_err());
}

#[test]
fn a_damaged_selected_runtime_is_repaired_in_a_new_directory() {
    let temp = tempfile::tempdir().unwrap();
    let cache = temp.path();
    let key = "5".repeat(24);
    let root = candidate(cache, &key);
    let publication = Publication::begin(cache).unwrap();
    drop(publication.commit(&root, &[]).unwrap());
    fs::remove_file(root.join("_framework/dotnet.js")).unwrap();
    assert!(publication.commit(&root, &[]).is_err());
    assert_eq!(fs::canonicalize(cache.join("current")).unwrap(), root);
    let fresh = files::selected(cache, &key).unwrap();
    assert_ne!(root, fresh);
    candidate(cache, fresh.file_name().unwrap().to_str().unwrap());
    drop(publication.commit(&fresh, &[]).unwrap());
    assert_eq!(fs::canonicalize(cache.join("current")).unwrap(), fresh);
    assert!(!root.exists());
}

#[test]
fn active_compiler_workspace_is_reclaimed_only_after_its_lock_is_released() {
    let temp = tempfile::tempdir().unwrap();
    let cache = temp.path();
    let publication = Publication::begin(cache).unwrap();
    let root = candidate(cache, &format!("aot-runtimes/{}", "5".repeat(64)));
    let old = cache.join("aot-workspaces").join("6".repeat(64));
    let compiler = files::lock(&old.join("build.lock")).unwrap();
    let keep = cache.join("aot-workspaces").join("7".repeat(64));
    fs::create_dir_all(&keep).unwrap();
    let inputs = vec![keep];
    drop(publication.commit(&root, &inputs).unwrap());
    assert!(old.is_dir());
    drop(compiler);
    drop(publication.commit(&root, &inputs).unwrap());
    assert!(!old.exists());
}
