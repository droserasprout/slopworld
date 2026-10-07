use super::target::{StorageBinding, Target};

#[test]
fn resolved_bindings_follow_independent_overrides() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    crate::test_support::set_env("SLOPD_CONFIG_ROOT", root.join("config"));
    crate::test_support::set_env("SLOPD_DATA", root.join("data"));
    let binding = StorageBinding::resolved(&root.join("custom/settings.toml")).unwrap();
    assert_eq!(binding.config, root.join("config"));
    assert_eq!(binding.data, root.join("data"));
    assert_eq!(
        Target::Settings.resolve(&binding).unwrap(),
        root.join("custom/settings.toml")
    );
    for target in [
        Target::Config("../escape.toml".into()),
        Target::Data("tasks/../escape.toml".into()),
        Target::Data("agents/not-an-id.toml".into()),
        Target::Config("tasks.toml".into()),
        Target::Data("tasks.journal".into()),
    ] {
        target.resolve(&binding).unwrap_err();
    }
    for target in [
        Target::Config("projects/0123456789abcdef.toml".into()),
        Target::Config("prompts/a prompt.toml".into()),
        Target::Data("agents/1111111111114111.toml".into()),
        Target::Data("tasks/0123456789abcdef.toml".into()),
    ] {
        target.resolve(&binding).unwrap();
    }
}
