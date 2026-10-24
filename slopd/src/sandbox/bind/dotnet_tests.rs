use super::*;
use crate::config::{NetworkMode, SessionCfg};

fn preset() -> SandboxPreset {
    SandboxPreset {
        private: vec![CLI_HOME.into()],
        setenv: [("DOTNET_CLI_HOME".into(), CLI_HOME.into())].into(),
        ..Default::default()
    }
}

fn aliases(presets: &[&SandboxPreset], ro: &[String], rw: &[String]) -> Result<Vec<String>> {
    let session = SessionCfg::default();
    let bind = BindContext {
        s: &session,
        presets,
        ro,
        rw,
        dev: &[],
        network: NetworkMode::default(),
        resolv: None,
        tmux: false,
        daemon_config: false,
    };
    let mut args = Vec::new();
    push_nuget_binds(&mut args, &bind, presets, "/home/test")?;
    Ok(args)
}

#[test]
fn aliases_preserve_selected_sources_and_access_without_granting_siblings() {
    let selected = preset();
    let args = aliases(
        &[&selected],
        &[
            "/home/test/.nuget/NuGet.Config".into(),
            "/home/test/.local/share/NuGet/config".into(),
            "/home/test/.nuget-other/secret".into(),
            "/elsewhere/.nuget/secret".into(),
        ],
        &["/home/test/.nuget/packages".into()],
    )
    .unwrap();
    assert_eq!(
        args,
        [
            "--ro-bind",
            "/home/test/.nuget/NuGet.Config",
            "/tmp/slopworld-dotnet-cli/.nuget/NuGet.Config",
            "--ro-bind",
            "/home/test/.local/share/NuGet/config",
            "/tmp/slopworld-dotnet-cli/.local/share/NuGet/config",
            "--bind",
            "/home/test/.nuget/packages",
            "/tmp/slopworld-dotnet-cli/.nuget/packages",
        ]
    );
}

#[test]
fn aliases_require_private_cli_home_and_the_final_environment_selection() {
    let selected = preset();
    let source = ["/home/test/.nuget/packages".into()];
    let mut override_preset = preset();
    override_preset
        .setenv
        .insert("DOTNET_CLI_HOME".into(), "/other".into());
    assert!(
        aliases(&[&selected, &override_preset], &source, &[])
            .unwrap()
            .is_empty()
    );
    assert!(
        !aliases(&[&override_preset, &selected], &source, &[])
            .unwrap()
            .is_empty()
    );
    let mut no_private = preset();
    no_private.private.clear();
    assert!(aliases(&[&no_private], &source, &[]).unwrap().is_empty());
    assert!(aliases(&[], &source, &[]).unwrap().is_empty());
}

#[test]
fn aliases_reject_traversal_and_private_roots_ancestors_and_descendants() {
    let mut selected = preset();
    let error = aliases(&[&selected], &["/home/test/.nuget/../secret".into()], &[]).unwrap_err();
    assert!(error.to_string().contains("parent traversal"));
    selected.private.push("/home/test/.nuget/private".into());
    for source in [
        "/home/test/.nuget",
        "/home/test/.nuget/private",
        "/home/test/.nuget/private/token",
    ] {
        for writable in [false, true] {
            let sources = [source.into()];
            let (ro, rw) = if writable {
                (&[][..], &sources[..])
            } else {
                (&sources[..], &[][..])
            };
            assert!(
                aliases(&[&selected], ro, rw)
                    .unwrap_err()
                    .to_string()
                    .contains("overlaps private")
            );
        }
    }
}
