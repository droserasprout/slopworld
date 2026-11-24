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
