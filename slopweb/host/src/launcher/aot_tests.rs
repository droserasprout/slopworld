//! Cache identity of the MSBuild policy that controls mixed compilation.
use super::*;

#[test]
fn changing_only_interpreter_policy_invalidates_publication_and_workspace() {
    let cache = tempfile::tempdir().unwrap();
    let mut contents = files::DigestCache::load(cache.path().join("digests.json"));
    let identity = json!({});
    let keys = |contents: &mut files::DigestCache, target: &[u8]| {
        Candidate {
            generated: BTreeMap::from([("Directory.Build.targets".into(), target.to_vec())]),
            sources: Inputs::new(),
        }
        .keys(contents, &identity)
        .unwrap()
    };
    let target = include_bytes!("interpreter.targets.in");
    let original = keys(&mut contents, target);
    assert_eq!(original, keys(&mut contents, target));
    let changed = keys(&mut contents, b"<Project />");
    assert_ne!(original.0, changed.0);
    assert_ne!(original.1, changed.1);
}

#[test]
fn publication_and_workspace_follow_the_environment_seen_by_the_compiler() {
    let cache = tempfile::tempdir().unwrap();
    let sdk = Path::new("/test-sdk/dotnet");
    let mut contents = files::DigestCache::load(cache.path().join("digests.json"));
    let candidate = Candidate {
        generated: BTreeMap::new(),
        sources: Inputs::new(),
    };
    let mut run = |flags: Option<&str>, simd: Option<&str>, unlisted: &str| {
        let environment = CompilerEnvironment::from_vars(sdk, |name| match name {
            "EMCC_CFLAGS" => flags.map(Into::into),
            "WasmEnableSIMD" => simd.map(Into::into),
            "WasmNativeStrip" => Some(unlisted.into()),
            _ => None,
        })
        .unwrap();
        let toolchain = Toolchain::new(sdk.into(), json!({}), environment).unwrap();
        let env = toolchain
            .environment
            .command(Path::new("/usr/bin/env"))
            .output()
            .unwrap();
        assert!(env.status.success());
        let output = String::from_utf8(env.stdout).unwrap();
        let mut expected = vec![
            "DOTNET_ROOT=/test-sdk".to_owned(),
            "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1".into(),
            "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true".into(),
            "DOTNET_NOLOGO=1".into(),
        ];
        if let Some(flags) = flags {
            expected.push(format!("EMCC_CFLAGS={flags}"));
        }
        if let Some(simd) = simd {
            expected.push(format!("WasmEnableSIMD={simd}"));
        }
        assert_eq!(
            output.lines().collect::<BTreeSet<_>>(),
            expected.iter().map(String::as_str).collect()
        );
        candidate.keys(&mut contents, &toolchain.identity).unwrap()
    };
    let first = run(Some("-O2"), None, "true");
    assert_eq!(first, run(Some("-O2"), None, "false"));
    for changed in [
        run(Some("-O0"), None, "true"),
        run(None, None, "true"),
        run(Some("-O2"), Some("false"), "true"),
    ] {
        assert_ne!(first.0, changed.0);
        assert_ne!(first.1, changed.1);
    }
}
