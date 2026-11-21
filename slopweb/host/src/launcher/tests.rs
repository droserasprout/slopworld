//! Behavioral contracts for immutable preparation, AOT recovery, and host-only inputs.
use super::*;
use anyhow::{Result, bail};
use clap::Parser;
use serde_json::{Value, json};
use std::{
    collections::BTreeMap,
    fs::{self, File},
    os::unix::fs::{PermissionsExt, symlink},
    path::Path,
    sync::Mutex,
    time::Duration,
};

fn write(path: impl AsRef<Path>, bytes: impl AsRef<[u8]>) {
    let path = path.as_ref();
    fs::create_dir_all(path.parent().unwrap()).unwrap();
    fs::write(path, bytes).unwrap();
}
fn options(args: &[&str]) -> cli::Options {
    cli::Options::try_parse_from(std::iter::once("slopweb").chain(args.iter().copied())).unwrap()
}

#[test]
fn cli_paths_accept_global_options_on_either_side_and_reject_unsafe_combinations() {
    assert_eq!(
        options(&["--endpoint", "remote", "paths", "profile"])
            .endpoint
            .as_deref(),
        Some("remote")
    );
    assert_eq!(
        options(&["paths", "profile", "--endpoint", "remote"])
            .endpoint
            .as_deref(),
        Some("remote")
    );
    for args in [
        vec!["slopweb", "--url", "http://local"],
        vec!["slopweb", "--token-file", "token"],
        vec![
            "slopweb",
            "--endpoint",
            "local",
            "--url",
            "http://local",
            "--token-file",
            "token",
        ],
        vec!["slopweb", "--interpreted", "--aot-sdk", "/sdk"],
        vec!["slopweb", "--port", "0"],
    ] {
        assert!(cli::Options::try_parse_from(args).is_err());
    }
}
#[test]
fn public_origin_requires_exact_https_authority() {
    for valid in [
        "https://example.com",
        "https://example.com:8443",
        "https://[::1]:8443",
    ] {
        assert!(cli::public_origin(valid).is_ok());
    }
    for invalid in [
        "http://example.com",
        "https://example.com/",
        "https://example.com:443",
        "https://user@example.com",
        "https://example.com?x=1",
        "https://example.com/path",
        "https://EXAMPLE.com",
    ] {
        assert!(cli::public_origin(invalid).is_err(), "{invalid}");
    }
}
#[test]
fn presentation_filters_secrets_and_preserves_native_types() {
    let temp = tempfile::tempdir().unwrap();
    write(
        temp.path().join("Config/SlopWorld.toml"),
        "fontSize = 18\nsidebarHidden = true\ntoken = 'secret'\nurl = 'private'\n",
    );
    let values = appearance::preferences(temp.path()).unwrap();
    assert_eq!(values["fontSize"], 18);
    assert_eq!(values["sidebarHidden"], true);
    assert!(!values.contains_key("token") && !values.contains_key("url"));
    assert!(values.contains_key("hostHome"));
}
#[test]
fn persisted_file_stamps_detect_same_size_edits_and_replacements_with_restored_mtime() {
    let temp = tempfile::tempdir().unwrap();
    let path = temp.path().join("input");
    write(&path, "first");
    let times = fs::metadata(&path).unwrap().modified().unwrap();
    let inputs = BTreeMap::from([("input".into(), path.clone())]);
    let index = temp.path().join("index");
    let manifest = || {
        let mut cache = files::DigestCache::load(index.clone());
        let hashes = cache.manifest(&inputs).unwrap();
        cache.save().unwrap();
        hashes
    };
    let first = manifest();
    let saved = fs::metadata(&index).unwrap().modified().unwrap();
    assert_eq!(manifest(), first);
    assert_eq!(fs::metadata(&index).unwrap().modified().unwrap(), saved);
    write(&path, "other");
    File::options()
        .write(true)
        .open(&path)
        .unwrap()
        .set_times(std::fs::FileTimes::new().set_modified(times))
        .unwrap();
    let second = manifest();
    assert_ne!(first, second);
    let replacement = temp.path().join("replacement");
    write(&replacement, "third");
    File::open(&replacement)
        .unwrap()
        .set_times(std::fs::FileTimes::new().set_modified(times))
        .unwrap();
    fs::rename(replacement, path).unwrap();
    assert_ne!(manifest(), second);
    write(&index, "incomplete index");
    assert_eq!(manifest()["input"], files::digest_bytes(b"third"));
}
fn assert_published_eco_digest(root: &Path, bytes: &[u8]) {
    use crate::archive_identity::{ARCHIVE, MARKER, content_tag, metadata_stamp};
    let stamp = metadata_stamp(&fs::metadata(root.join(ARCHIVE)).unwrap());
    let marker = File::open(root.join(MARKER)).unwrap();
    let tag = format!("\"sha256-{}\"", files::digest_bytes(bytes));
    assert_eq!(content_tag(marker, &stamp).as_deref(), Some(tag.as_str()));
}

#[test]
fn preparation_reuses_independent_phases_and_failed_rebuild_keeps_active_runtime() {
    let temp = tempfile::tempdir().unwrap();
    let bundle = temp.path().join("bundle");
    let game = temp.path().join("game");
    let cache = temp.path().join("cache");
    write(bundle.join("web/index.html"), "page");
    write(bundle.join("Adapters.dll"), "adapter");
    write(bundle.join("mod/SlopWorld.dll"), "mod");
    write(bundle.join("ost/song.ogg"), "song");
    write(game.join("Assembly-CSharp.dll"), "game");
    write(game.join("Assets.zip"), "archive");
    let calls = Mutex::new(Vec::new());
    let build = |phase: &str, root: &Path, values: &serde_json::Map<String, Value>| -> Result<()> {
        calls.lock().unwrap().push(phase.to_owned());
        if phase == "fonts" {
            appearance::write_preferences(root, values)?;
        } else if phase == "assets" {
            write(root.join(crate::archive_identity::ARCHIVE), "eco");
        } else {
            write(root.join(format!("{phase}.fixture")), phase);
        }
        Ok(())
    };
    let mut values = serde_json::Map::new();
    let pinned = files::PinnedBundle::open(bundle.clone()).unwrap();
    let first = preparation::prepare(
        &pinned,
        &game,
        &cache,
        false,
        &values,
        "fixture-fonts",
        build,
    )
    .unwrap();
    assert_eq!(
        fs::read(first.join("assets/ost/song.ogg")).unwrap(),
        b"song"
    );
    assert_eq!(
        preparation::prepare(
            &pinned,
            &game,
            &cache,
            false,
            &values,
            "fixture-fonts",
            build
        )
        .unwrap(),
        first
    );
    assert_eq!(calls.lock().unwrap().len(), 4);
    write(bundle.join("web/index.html"), "other page");
    let second = preparation::prepare(
        &pinned,
        &game,
        &cache,
        false,
        &values,
        "fixture-fonts",
        build,
    )
    .unwrap();
    assert_ne!(first, second);
    assert_published_eco_digest(&first, b"eco");
    assert_published_eco_digest(&second, b"eco");
    assert_eq!(calls.lock().unwrap().len(), 4);
    values.insert("sidebarHidden".into(), json!(true));
    preparation::prepare(
        &pinned,
        &game,
        &cache,
        false,
        &values,
        "fixture-fonts",
        build,
    )
    .unwrap();
    assert_eq!(calls.lock().unwrap().len(), 4);
    values.insert("fontSize".into(), json!(18));
    preparation::prepare(
        &pinned,
        &game,
        &cache,
        false,
        &values,
        "fixture-fonts",
        build,
    )
    .unwrap();
    assert_eq!(calls.lock().unwrap().last().unwrap(), "fonts");
    assert_eq!(calls.lock().unwrap().len(), 5);
    write(game.join("Data/Core/data.xml"), "new asset");
    preparation::prepare(
        &pinned,
        &game,
        &cache,
        false,
        &values,
        "fixture-fonts",
        build,
    )
    .unwrap();
    assert_eq!(calls.lock().unwrap().last().unwrap(), "assets");
    assert_eq!(calls.lock().unwrap().len(), 6);
    let refreshed = preparation::prepare(
        &pinned,
        &game,
        &cache,
        true,
        &values,
        "fixture-fonts",
        build,
    )
    .unwrap();
    assert!(first.join(".ready").is_file());
    assert_eq!(
        preparation::prepare(
            &pinned,
            &game,
            &cache,
            false,
            &values,
            "fixture-fonts",
            build
        )
        .unwrap(),
        refreshed
    );
    let failed = preparation::prepare(
        &pinned,
        &game,
        &cache,
        true,
        &values,
        "fixture-fonts",
        |phase, root, values| {
            if phase == "background" {
                bail!("fixture failed");
            }
            build(phase, root, values)
        },
    );
    assert!(failed.is_err());
    assert_eq!(
        preparation::prepare(
            &pinned,
            &game,
            &cache,
            false,
            &values,
            "fixture-fonts",
            build
        )
        .unwrap(),
        refreshed
    );
    assert_eq!(
        fs::read_to_string(first.join("index.html")).unwrap(),
        "page"
    );
}
#[test]
fn replacing_authored_bytes_during_preparation_cannot_publish_under_the_old_key() {
    let temp = tempfile::tempdir().unwrap();
    let bundle = temp.path().join("bundle");
    let game = temp.path().join("game");
    let cache = temp.path().join("cache");
    let main = bundle.join("web/main.js");
    write(&main, "first");
    write(bundle.join("ost/track.ogg"), "song");
    let appearance = serde_json::Map::new();
    let pinned = files::PinnedBundle::open(bundle.clone()).unwrap();
    let error = preparation::prepare(
        &pinned,
        &game,
        &cache,
        false,
        &appearance,
        "fonts",
        |phase, _, _| {
            if phase == "managed" {
                // A cooperating restager waits. An in-place edit with the same
                // length still has to be detected on the private copy.
                let parent = File::open(bundle.parent().unwrap())?;
                assert_eq!(
                    fs2::FileExt::try_lock_exclusive(&parent)
                        .unwrap_err()
                        .kind(),
                    std::io::ErrorKind::WouldBlock
                );
                write(&main, "other");
            }
            Ok(())
        },
    )
    .unwrap_err();
    assert!(
        error
            .to_string()
            .contains("web/main.js changed during preparation")
    );
    write(&main, "first");
    let ready = preparation::prepare(
        &pinned,
        &game,
        &cache,
        false,
        &appearance,
        "fonts",
        |_, _, _| Ok(()),
    )
    .unwrap();
    assert_eq!(fs::read(ready.join("main.js")).unwrap(), b"first");
}

#[test]
fn malicious_private_cache_pointer_and_symlink_inputs_are_rejected() {
    let temp = tempfile::tempdir().unwrap();
    write(temp.path().join("key/.ready"), "wrong generation");
    assert!(!files::ready(&temp.path().join("key"), "key").unwrap());
    write(temp.path().join(".active-key"), "../key-secret");
    assert!(files::selected(temp.path(), "key").is_err());
    write(temp.path().join("outside"), "secret");
    symlink(temp.path().join("outside"), temp.path().join("alias")).unwrap();
    assert!(files::files(temp.path()).is_err());
}
#[test]
fn core_archive_contains_only_core_and_shaders_and_rejects_dlls() {
    let temp = tempfile::tempdir().unwrap();
    let game = temp.path().join("game");
    let archive = temp.path().join("Core.zip");
    write(game.join("Data/Core/Defs/a.xml"), "core");
    write(game.join("Rendering/Shaders/a.shader"), "shader");
    write(game.join("Data/Expansion/private.xml"), "expansion");
    preparation::core_archive(&game, &archive).unwrap();
    let mut zip = zip::ZipArchive::new(File::open(&archive).unwrap()).unwrap();
    assert_eq!(zip.len(), 2);
    assert!(zip.by_name("Data/Core/Defs/a.xml").is_ok());
    write(game.join("Data/Core/unsafe.DLL"), "dll");
    assert!(preparation::core_archive(&game, &archive).is_err());
}
#[test]
fn aot_loader_adapts_shipped_assembly_boundary_and_rejects_mixed_loading() {
    let loader = include_str!("../../../runtime/wwwroot/main.js");
    let changed = aot::published_loader(loader).unwrap();
    assert!(changed.contains("const assemblyRequest = null;"));
    assert!(changed.contains("LoadPublishedAssembly(file.replace"));
    assert_eq!(changed.matches("LoadPublishedAssembly(").count(), 1);
    assert!(!changed.contains("Probe.LoadAssembly("));
    assert!(aot::published_loader(&changed).is_err());
    assert!(aot::published_loader("unknown").is_err());
    assert!(
        aot::published_loader(&format!("{loader}\nexports.Probe.LoadAssembly(bytes);")).is_err()
    );
}

struct AotFixture {
    _temp: tempfile::TempDir,
    bundle: std::path::PathBuf,
    prepared: std::path::PathBuf,
    cache: std::path::PathBuf,
    sdk: aot::Toolchain,
}
impl AotFixture {
    fn new() -> Self {
        let temp = tempfile::tempdir().unwrap();
        let bundle = temp.path().join("bundle");
        let prepared = temp.path().join("prepared");
        let cache = temp.path().join("cache");
        write(
            bundle.join("aot-src/Probe.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk.WebAssembly\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include=\"Probe.cs\" /></ItemGroup></Project>",
        );
        write(bundle.join("aot-src/Probe.cs"), "class Probe {}");
        for name in [
            "game/manifest.json",
            "game/Assembly-CSharp.dll",
            "game/Waylith.dll",
            "game/Waylith.Injection.dll",
            "game/RimWorldWaylith.PrepatcherCompat.dll",
            "mod/SlopWorld.dll",
            "Adapters.dll",
            "assets/fixture",
        ] {
            write(prepared.join(name), name);
        }
        write(
            prepared.join("main.js"),
            include_str!("../../../runtime/wwwroot/main.js"),
        );
        let sdk_path = temp.path().join("dotnet");
        write(
            &sdk_path,
            "#!/bin/sh\nset -eu\nwhile [ \"$1\" != '-o' ]; do shift; done\nshift\nmkdir -p \"$1/wwwroot/_framework\"\nprintf js > \"$1/wwwroot/_framework/dotnet.js\"\nprintf wasm > \"$1/wwwroot/_framework/dotnet.native.wasm\"\nprintf publish\\n\n",
        );
        fs::set_permissions(&sdk_path, fs::Permissions::from_mode(0o700)).unwrap();
        let sdk = aot::Toolchain {
            sdk: sdk_path,
            identity: json!({"version":"10.0.100", "target_runtime":"10.0.0"}),
        };
        Self {
            _temp: temp,
            bundle,
            prepared,
            cache,
            sdk,
        }
    }
    fn pin_bundle(&self) -> files::PinnedBundle {
        files::PinnedBundle::open(self.bundle.clone()).unwrap()
    }
    fn run(&self) -> Result<std::path::PathBuf> {
        aot::prepare(self.pin_bundle(), &self.prepared, &self.cache, &self.sdk)
    }
    fn workspace(&self) -> std::path::PathBuf {
        fs::read_dir(self.cache.join("aot-workspaces"))
            .unwrap()
            .next()
            .unwrap()
            .unwrap()
            .path()
    }
}

#[test]
fn restage_cannot_split_interpreted_runtime_and_aot_source_capture() {
    let f = AotFixture::new();
    files::copy_tree(&f.prepared, &f.bundle.join("web"), |_| false).unwrap();
    fs::create_dir(f.bundle.join("ost")).unwrap();
    let bundle = f.pin_bundle();
    let interpreted = preparation::prepare(
        &bundle,
        &f.prepared.join("game"),
        &f.cache,
        false,
        &serde_json::Map::new(),
        "fixture-fonts",
        |_, _, _| Ok(()),
    )
    .unwrap();
    let stager = File::open(f.bundle.parent().unwrap()).unwrap();
    assert_eq!(
        fs2::FileExt::try_lock_exclusive(&stager)
            .unwrap_err()
            .kind(),
        std::io::ErrorKind::WouldBlock
    );
    let runtime = aot::prepare(bundle, &interpreted, &f.cache, &f.sdk).unwrap();
    fs2::FileExt::try_lock_exclusive(&stager).unwrap();
    assert_eq!(
        fs::read(runtime.join("Adapters.dll")).unwrap(),
        b"Adapters.dll"
    );
    assert_eq!(
        fs::read(f.workspace().join("candidate/src/Probe.cs")).unwrap(),
        b"class Probe {}"
    );
}
#[cfg(target_os = "linux")]
#[test]
fn warm_aot_reuses_the_publication_without_reading_assemblies_or_asset_payloads() {
    use std::{
        ffi::CString,
        io::Read,
        os::fd::{AsRawFd, FromRawFd},
    };

    let f = AotFixture::new();
    let first = f.run().unwrap();
    let build = fs::read_dir(f.cache.join("aot-builds"))
        .unwrap()
        .next()
        .unwrap()
        .unwrap()
        .path();
    // Preserve the cache key used by publishers that hash the materialized tree.
    let candidate_files = files::files(&build.join("candidate")).unwrap();
    let candidate_hashes = files::DigestCache::load(f.cache.join("unused"))
        .manifest(&candidate_files)
        .unwrap();
    assert_eq!(
        build.file_name().unwrap().to_str().unwrap(),
        files::digest(&candidate_hashes, &f.sdk.identity).unwrap()
    );
    fs::remove_dir_all(build.join("candidate")).unwrap();

    // Opening a watched file catches either a rehash or a transient candidate
    // copy, even if that copy is removed again before prepare returns.
    let fd = unsafe { libc::inotify_init1(libc::IN_NONBLOCK | libc::IN_CLOEXEC) };
    assert!(fd >= 0);
    let mut watch = unsafe { File::from_raw_fd(fd) };
    for path in [
        f.bundle.join("aot-src/Probe.cs"),
        f.prepared.join("game/Assembly-CSharp.dll"),
        f.prepared.join("assets/fixture"),
    ] {
        let path = CString::new(path.as_os_str().as_encoded_bytes()).unwrap();
        assert!(
            unsafe { libc::inotify_add_watch(watch.as_raw_fd(), path.as_ptr(), libc::IN_OPEN) }
                >= 0
        );
    }
    assert_eq!(f.run().unwrap(), first);
    assert!(!build.join("candidate").exists());
    assert_eq!(
        watch.read(&mut [0; 1024]).unwrap_err().kind(),
        std::io::ErrorKind::WouldBlock
    );
}

#[test]
fn refreshed_outputs_with_the_same_preparation_marker_get_distinct_aot_roots() {
    let f = AotFixture::new();
    write(f.prepared.join(".ready"), "same preparation inputs");
    write(f.prepared.join(crate::archive_identity::ARCHIVE), "eco");
    crate::archive_identity::publish(&f.prepared, &files::digest_bytes(b"eco")).unwrap();
    let first = f.run().unwrap();
    let refreshed = f.prepared.with_file_name("refreshed");
    files::copy_tree(&f.prepared, &refreshed, |_| false).unwrap();
    write(refreshed.join("assets/fixture"), "fresh background");
    let second = aot::prepare(f.pin_bundle(), &refreshed, &f.cache, &f.sdk).unwrap();
    assert_ne!(first, second);
    assert_published_eco_digest(&first, b"eco");
    assert_published_eco_digest(&second, b"eco");
    assert_eq!(
        fs::read(first.join("assets/fixture")).unwrap(),
        b"assets/fixture"
    );
    assert_eq!(
        fs::read(second.join("assets/fixture")).unwrap(),
        b"fresh background"
    );
    assert_eq!(fs::read_dir(f.cache.join("aot-builds")).unwrap().count(), 1);
}

#[test]
fn aot_asset_changes_recompose_without_publish_and_source_changes_reuse_workspace() {
    let f = AotFixture::new();
    let first = f.run().unwrap();
    let workspace = f.workspace();
    write(workspace.join("candidate/obj/cached.o"), "compiler state");
    let unchanged = workspace.join("candidate/assemblies/Assembly-CSharp.dll");
    let timestamp = fs::metadata(&unchanged).unwrap().modified().unwrap();
    write(f.prepared.join("assets/fixture"), "changed asset");
    let second = f.run().unwrap();
    assert_ne!(first, second);
    assert_eq!(fs::read_dir(f.cache.join("aot-builds")).unwrap().count(), 1);
    write(
        f.bundle.join("aot-src/Probe.cs"),
        "class Probe { int changed; }",
    );
    let third = f.run().unwrap();
    assert_ne!(second, third);
    assert_eq!(f.workspace(), workspace);
    assert_eq!(
        fs::metadata(unchanged).unwrap().modified().unwrap(),
        timestamp
    );
    assert_eq!(
        fs::read_to_string(workspace.join("candidate/obj/cached.o")).unwrap(),
        "compiler state"
    );
    assert!(first.join(".ready").is_file());
    let project = f.bundle.join("aot-src/Probe.csproj");
    let bytes = fs::read_to_string(&project).unwrap();
    write(&project, format!("{bytes}\n  "));
    assert_eq!(f.run().unwrap(), third);
}
#[test]
fn failed_aot_refresh_preserves_previous_runtime_and_retry_resumes_workspace() {
    let f = AotFixture::new();
    let original = f.run().unwrap();
    let compiler = fs::read(&f.sdk.sdk).unwrap();
    let workspace = f.workspace();
    write(workspace.join("candidate/obj/state"), "retained");
    write(f.prepared.join("mod/SlopWorld.dll"), "changed");
    write(&f.sdk.sdk, "#!/bin/sh\necho compiler-failed\nexit 3\n");
    assert!(f.run().unwrap_err().to_string().contains("retry to resume"));
    assert!(original.join(".ready").is_file());
    assert!(!workspace.join("publish.complete").exists());
    assert_eq!(
        fs::read_to_string(workspace.join("candidate/obj/state")).unwrap(),
        "retained"
    );
    write(&f.sdk.sdk, compiler);
    let repaired = f.run().unwrap();
    assert_ne!(original, repaired);
    assert!(repaired.join(".ready").is_file());
    assert!(
        fs::read_dir(f.cache.join("aot-builds"))
            .unwrap()
            .filter_map(|e| fs::read_to_string(e.unwrap().path().join("publish.log")).ok())
            .any(|log| log.contains("compiler-failed")
                && log.matches("Publish attempt").count() == 2)
    );
}
#[test]
fn candidate_sync_removes_deleted_inputs_without_touching_compiler_state() {
    let temp = tempfile::tempdir().unwrap();
    let source = temp.path().join("source");
    let target = temp.path().join("target");
    fs::create_dir(&source).unwrap();
    write(target.join("assemblies/Removed.dll"), "old");
    write(target.join("obj/state"), "kept");
    aot::sync_candidate(&source, &target).unwrap();
    assert!(!target.join("assemblies/Removed.dll").exists());
    assert!(target.join("obj/state").exists());
}
#[test]
fn compiler_retains_workspace_lock_after_launcher_closes_its_descriptor() {
    use fs2::FileExt;
    use std::process::{Command, Stdio};
    let temp = tempfile::tempdir().unwrap();
    let path = temp.path().join("build.lock");
    let lock = files::lock(&path).unwrap();
    let mut command = Command::new("sh");
    command.args(["-c", "read release"]).stdin(Stdio::piped());
    aot::inherit_lock(&mut command, &lock);
    let mut child = command.spawn().unwrap();
    drop(lock);
    let contender = File::options().append(true).open(&path).unwrap();
    assert_eq!(
        contender.try_lock_exclusive().unwrap_err().kind(),
        std::io::ErrorKind::WouldBlock
    );
    drop(child.stdin.take());
    child.wait().unwrap();
    contender.try_lock_exclusive().unwrap();
}

#[test]
fn toolchain_selects_stable_ten_in_a_shared_sdk_and_requires_matching_packs() {
    let temp = tempfile::tempdir().unwrap();
    let sdk = temp.path().join("sdk-root/dotnet");
    write(
        &sdk,
        r#"#!/bin/sh
set -eu
case "$1" in
--version)
    if [ ! -f global.json ]; then echo 11.0.100; exit 0; fi
    grep -q '"version":"10.0.100"' global.json
    grep -q '"rollForward":"latestMinor"' global.json
    grep -q '"allowPrerelease":false' global.json
    echo 10.0.114;;
workload)
    grep -q '"version":"10.0.114"' global.json
    grep -q '"rollForward":"disable"' global.json
    echo 'wasm-tools 10.0.114/10.0.100';;
esac
"#,
    );
    fs::set_permissions(&sdk, fs::Permissions::from_mode(0o700)).unwrap();
    let root = sdk.parent().unwrap();
    write(
        root.join("sdk/10.0.114/Microsoft.NETCoreSdk.BundledVersions.props"),
        "<Project><ItemGroup><KnownRuntimePack Include=\"Microsoft.NETCore.App\" TargetFramework=\"net10.0\" LatestRuntimeFrameworkVersion=\"10.0.14\" /></ItemGroup></Project>",
    );
    for name in [
        "Microsoft.NET.Runtime.WebAssembly.Sdk.net10",
        "Microsoft.NETCore.App.Runtime.AOT.linux-x64.Cross.browser-wasm",
    ] {
        fs::create_dir_all(root.join("packs").join(name).join("10.0.14")).unwrap();
    }
    let first = aot::toolchain(root, false).unwrap();
    assert_eq!(first.identity["version"], "10.0.114");
    assert_eq!(first.identity["target_runtime"], "10.0.14");
    assert_eq!(first.identity["packs"].as_object().unwrap().len(), 2);
    fs::create_dir_all(root.join("packs/Microsoft.Android.Sdk/99.0")).unwrap();
    fs::create_dir_all(root.join("packs/Microsoft.NET.Runtime.WebAssembly.Wasi.Sdk/10.0.14"))
        .unwrap();
    assert_eq!(
        aot::toolchain(&sdk, false).unwrap().identity,
        first.identity
    );
    fs::remove_dir_all(root.join("packs/Microsoft.NET.Runtime.WebAssembly.Sdk.net10/10.0.14"))
        .unwrap();
    assert!(
        aot::toolchain(&sdk, false)
            .err()
            .unwrap()
            .to_string()
            .contains("install it separately")
    );
}

#[test]
fn aot_policy_changes_isolate_workspace_and_reject_escaped_sources() {
    let f = AotFixture::new();
    f.run().unwrap();
    let project = f.bundle.join("aot-src/Probe.csproj");
    let original = fs::read_to_string(&project).unwrap();
    write(
        &project,
        original.replace(
            "</PropertyGroup>",
            "<Nullable>enable</Nullable></PropertyGroup>",
        ),
    );
    f.run().unwrap();
    assert_eq!(
        fs::read_dir(f.cache.join("aot-workspaces"))
            .unwrap()
            .count(),
        2
    );
    write(
        &project,
        original.replace("Include=\"Probe.cs\"", "Include=\"../secret.cs\""),
    );
    assert!(f.run().unwrap_err().to_string().contains("explicit local"));
}

#[test]
fn different_aot_revisions_cannot_change_workspace_inputs_during_publish() {
    use std::sync::mpsc;
    let f = AotFixture::new();
    // A filesystem handshake stops the first compiler after it snapshots its
    // assembly. A contender cannot overwrite that input until publication ends.
    let sdk_script = "#!/bin/sh\nset -eu\nwhile [ \"$1\" != '-o' ]; do shift; done\nshift\nif [ ! -f ../../../compiler-entered ]; then\n  cp assemblies/SlopWorld.dll ../../../compiler-before\n  touch ../../../compiler-entered\n  while [ ! -f ../../../compiler-release ]; do sleep 0.01; done\n  cmp assemblies/SlopWorld.dll ../../../compiler-before\nfi\nmkdir -p \"$1/wwwroot/_framework\"\ntouch \"$1/wwwroot/_framework/dotnet.js\" \"$1/wwwroot/_framework/dotnet.native.wasm\"\n";
    write(&f.sdk.sdk, sdk_script);
    let second = f.prepared.with_file_name("second-prepared");
    files::copy_tree(&f.prepared, &second, |_| false).unwrap();
    write(second.join("mod/SlopWorld.dll"), "second revision");
    let (sent, received) = mpsc::channel();
    std::thread::scope(|scope| {
        let first = scope.spawn(|| f.run());
        let deadline = std::time::Instant::now() + Duration::from_secs(10);
        while !f.cache.join("compiler-entered").exists() {
            assert!(std::time::Instant::now() < deadline);
            std::thread::sleep(Duration::from_millis(5));
        }
        // The compiler uses the private candidate and no longer holds up restaging.
        let stager = File::open(f.bundle.parent().unwrap()).unwrap();
        if let Err(error) = fs2::FileExt::try_lock_exclusive(&stager) {
            write(f.cache.join("compiler-release"), "release");
            panic!("compilation still holds the bundle lock: {error}");
        }
        drop(stager);
        let contender = scope.spawn(|| {
            sent.send(()).unwrap();
            aot::prepare(f.pin_bundle(), &second, &f.cache, &f.sdk)
        });
        received.recv_timeout(Duration::from_secs(2)).unwrap();
        while fs::read_dir(f.cache.join("aot-builds"))
            .unwrap()
            .filter_map(|e| e.ok())
            .filter(|e| e.path().join("candidate").is_dir())
            .count()
            < 2
        {
            if std::time::Instant::now() >= deadline {
                write(f.cache.join("compiler-release"), "release");
                panic!("second revision did not reach the workspace boundary");
            }
            std::thread::sleep(Duration::from_millis(5));
        }
        {
            use fs2::FileExt;
            let contender = File::options()
                .append(true)
                .open(f.workspace().join("build.lock"))
                .unwrap();
            assert_eq!(
                contender.try_lock_exclusive().unwrap_err().kind(),
                std::io::ErrorKind::WouldBlock
            );
        }
        write(f.cache.join("compiler-release"), "release");
        let first = first.join().unwrap().unwrap();
        let second = contender.join().unwrap().unwrap();
        assert_ne!(first, second);
        assert_eq!(
            fs::read_to_string(first.join("mod/SlopWorld.dll")).unwrap(),
            "mod/SlopWorld.dll"
        );
        assert_eq!(
            fs::read_to_string(second.join("mod/SlopWorld.dll")).unwrap(),
            "second revision"
        );
    });
    assert_eq!(
        fs::read_dir(f.cache.join("aot-workspaces"))
            .unwrap()
            .count(),
        1
    );
}

#[test]
fn font_diagnostic_accepts_game_and_output_without_mutating_global_game_argument() {
    let parsed = options(&["check-fonts", "/game", "/output"]);
    assert!(parsed.game.is_none());
    assert!(
        matches!(parsed.command, Some(cli::LocalCommand::CheckFonts { install, output }) if install == Path::new("/game") && output == Path::new("/output"))
    );
}

#[test]
fn aot_reuses_authored_package_reference_and_rejects_version_drift() {
    let f = AotFixture::new();
    let project = f.bundle.join("aot-src/Probe.csproj");
    let original = fs::read_to_string(&project).unwrap();
    write(
        &project,
        original.replace(
            "</ItemGroup>",
            "<PackageReference Include=\"StbImageSharp\" Version=\"2.27.13\" /></ItemGroup>",
        ),
    );
    f.run().unwrap();
    let candidate = fs::read_to_string(f.workspace().join("candidate/Probe.csproj")).unwrap();
    assert_eq!(candidate.matches("Include=\"StbImageSharp\"").count(), 2); // package and trimming root
    write(
        &project,
        original.replace(
            "</ItemGroup>",
            "<PackageReference Include=\"StbImageSharp\" Version=\"0.0.0\" /></ItemGroup>",
        ),
    );
    assert!(
        f.run()
            .unwrap_err()
            .to_string()
            .contains("AOT package version differs")
    );
}

#[test]
fn assets_phase_stages_entry_and_workspace_members_without_full_pack() {
    use std::io::{Read, Write};
    let temp = tempfile::tempdir().unwrap();
    let game = temp.path().join("game");
    let root = temp.path().join("root");
    fs::create_dir_all(&game).unwrap();
    let keep = [
        "textures.info",
        "bake.manifest",
        "Resources/Fonts/ui.ttf",
        "Resources/Textures/UI/HeroArt/BGPlanet.png.dds.split",
        "Resources/Textures/Things/icon.dds.split",
        "Resources/Sounds/UI/click.ogg",
    ];
    let mut source = zip::ZipWriter::new(File::create(game.join("Assets.zip")).unwrap());
    for name in keep.into_iter().chain([
        "Resources/Sounds/Songs/theme.ogg",
        "Resources/Textures/UI/HeroArt/MenuBG_Anomaly.jpg.dds.split",
    ]) {
        source
            .start_file(name, zip::write::SimpleFileOptions::default())
            .unwrap();
        source.write_all(name.as_bytes()).unwrap();
    }
    source.finish().unwrap();
    preparation::native_phase(temp.path(), &game, "assets", &root).unwrap();
    assert!(!root.join("assets/Assets.zip").exists());
    let mut eco = zip::ZipArchive::new(File::open(root.join("assets/Eco.zip")).unwrap()).unwrap();
    assert_eq!(eco.len(), keep.len());
    for name in keep {
        let mut contents = String::new();
        eco.by_name(name)
            .unwrap()
            .read_to_string(&mut contents)
            .unwrap();
        assert_eq!(contents, name);
    }
}
