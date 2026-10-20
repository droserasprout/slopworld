use super::*;

#[test]
fn check_project_rejects_unsafe_names() {
    for name in ["../escape", "one/two", "/tmp/escape", ".", "..", r"one\two"] {
        let error = check_project(&ProjectCfg {
            name: name.into(),
            dir: "/tmp".into(),
            ..Default::default()
        })
        .unwrap_err()
        .to_string();
        assert!(error.contains("path component"), "{name:?}: {error}");
    }
}
