use super::*;
use crate::application::file_actions::FileActions;
use crate::session::{ExecutionSource, HostLifetime, LaunchDestination};

use serde_json::json;
fn decode_launch(value: serde_json::Value) -> crate::application::launch::ExplicitLaunch {
    serde_json::from_value::<RunReq>(value).unwrap().into()
}

#[tokio::test]
async fn reader_launches_reject_unknown_intents_and_missing_paths() {
    let manager = crate::application::test_application(crate::config::Config::default());
    for (intent, message) in [
        ("unknown", "Unknown terminal intent"),
        ("view", "needs a path"),
        ("edit", "needs a path"),
        ("search", "needs a path"),
    ] {
        let request = decode_launch(json!({"intent": intent, "temp": true}));
        let Err(error) =
            crate::application::launch::plan_explicit_launch(&FileActions::new(&manager), request)
                .await
        else {
            panic!("invalid reader launch accepted");
        };
        assert!(error.to_string().contains(message), "{}", error);
    }
}

#[tokio::test]
async fn host_shell_persistence_requires_a_project_and_no_explicit_command() {
    let manager = crate::application::test_application(crate::config::Config::default());
    for (project, command, temp, persistent, temporary) in [
        (" repo ", " ", false, true, false),
        ("repo", "echo hello", false, false, false),
        ("repo", "", true, false, true),
        ("", "", false, false, true),
    ] {
        let request = decode_launch(json!({
            "kind": "shell", "host": true, "project": project,
            "command": command, "temp": temp
        }));
        let plan =
            crate::application::launch::plan_explicit_launch(&FileActions::new(&manager), request)
                .await
                .unwrap();
        assert_eq!(
            matches!(
                plan.execution,
                ExecutionSource::Host(HostLifetime::Persistent)
            ),
            persistent
        );
        assert_eq!(
            matches!(plan.destination, LaunchDestination::Temporary),
            temporary
        );
        assert!(matches!(plan.execution, ExecutionSource::Host(_)));
        if !temporary {
            assert_eq!(
                plan.destination,
                LaunchDestination::Project(project.trim().into())
            );
        }
        assert_eq!(
            plan.content.command.as_deref(),
            (!command.trim().is_empty()).then_some(command.trim())
        );
        assert!(!plan.content.name.is_empty());
    }
}

#[tokio::test]
async fn reader_plan_preserves_source_identity_geometry_and_template() {
    let manager = crate::application::test_application(crate::config::Config::default());
    let request = decode_launch(json!({
        "kind": "shell", "project": " repo ", "command": " less file ",
        "label": " reader ", "intent": "view", "worktree": "checkout",
        "reader": {"path": "file", "key": "key", "scope": "scope", "line": 17, "pinned": true},
        "cols": 90, "rows": 30, "agent_template": "review", "random_tips": ["tip"]
    }));
    let plan =
        crate::application::launch::plan_explicit_launch(&FileActions::new(&manager), request)
            .await
            .unwrap();
    assert_eq!(plan.content.name, "reader");
    assert_eq!(plan.content.command.as_deref(), Some("less file"));
    assert_eq!(plan.execution, ExecutionSource::Template("review".into()));
    assert!(!matches!(
        plan.execution,
        ExecutionSource::Host(HostLifetime::Persistent)
    ));
    assert!(!matches!(plan.execution, ExecutionSource::Host(_)));
    let destination = plan.options;
    assert_eq!(destination.worktree, "checkout");
    assert_eq!(destination.context.intent, "view");
    assert_eq!(destination.context.reader.path, "file");
    assert_eq!(destination.context.reader.key, "key");
    assert_eq!(destination.context.reader.scope, "scope");
    assert_eq!(destination.context.reader.line, 17);
    assert!(destination.context.reader.pinned);
    assert_eq!((destination.cols, destination.rows), (Some(90), Some(30)));
    assert_eq!(destination.random_tips, ["tip"]);
}

#[tokio::test]
async fn explicit_launches_reject_ambiguous_authority_before_allocation() {
    let manager = crate::application::test_application(crate::config::Config::default());
    for fields in [
        json!({}),
        json!({"host": true, "agent_template": "review"}),
        json!({"host": true, "like": "source"}),
        json!({"agent_template": "review", "like": "source"}),
    ] {
        let mut request = json!({"kind": "shell", "temp": true});
        request
            .as_object_mut()
            .unwrap()
            .extend(fields.as_object().unwrap().clone());
        crate::application::launch::plan_explicit_launch(
            &FileActions::new(&manager),
            decode_launch(request),
        )
        .await
        .unwrap_err();
        assert!(manager.sessions.views().await.is_empty());
    }
    let request = decode_launch(json!({"kind":"shell", "temp":true, "like":" source "}));
    let plan =
        crate::application::launch::plan_explicit_launch(&FileActions::new(&manager), request)
            .await
            .unwrap();
    assert_eq!(plan.execution, ExecutionSource::CloneAgent("source".into()));
}

#[tokio::test]
async fn file_launches_preserve_prepared_paths_and_keep_disposable_host_lifetime() {
    let manager = crate::application::test_application(crate::config::Config::default());
    let request = decode_launch(json!({
        "kind": "shell", "host": true, "path": "/tmp/a'b", "hold": true,
        "command": "cat '/tmp/a'\\''b'"
    }));
    let plan =
        crate::application::launch::plan_explicit_launch(&FileActions::new(&manager), request)
            .await
            .unwrap();
    assert_eq!(
        plan.execution,
        ExecutionSource::Host(HostLifetime::Disposable)
    );
    assert_eq!(plan.destination, LaunchDestination::Temporary);
    let argv = crate::sandbox::shell_split(plan.content.command.as_deref().unwrap()).unwrap();
    assert_eq!(&argv[..2], &["bash", "-lc"]);
    assert_eq!(argv[2], "cat '/tmp/a'\\''b'; exec \"${SHELL:-bash}\"");
}

#[tokio::test]
async fn held_file_launches_never_resubstitute_placeholder_text_in_filenames() {
    let manager = crate::application::test_application(crate::config::Config::default());
    // Exact prepared text emitted by PagerCommands.FileActionCommand. The second
    // case uses a relative-path template, and the third uses both placeholders.
    // These commands are inspected, never executed.
    for (path, command, arguments) in [
        (
            "/tmp/{{ absolute_path }}$(id)",
            "cat '/tmp/{{ absolute_path }}$(id)'",
            vec!["/tmp/{{ absolute_path }}$(id)"],
        ),
        (
            "/tmp/{{ absolute_path }}'{{ relative_path }}$(id)",
            "cat '{{ absolute_path }}'\\''{{ relative_path }}$(id)'",
            vec!["{{ absolute_path }}'{{ relative_path }}$(id)"],
        ),
        (
            "/tmp/{{ absolute_path }}'{{ relative_path }}$(id)",
            "cat '/tmp/{{ absolute_path }}'\\''{{ relative_path }}$(id)' '{{ absolute_path }}'\\''{{ relative_path }}$(id)'",
            vec![
                "/tmp/{{ absolute_path }}'{{ relative_path }}$(id)",
                "{{ absolute_path }}'{{ relative_path }}$(id)",
            ],
        ),
    ] {
        for hold in [false, true] {
            let request = decode_launch(json!({
                "kind": "shell", "host": true, "path": path, "hold": hold,
                "command": command
            }));
            let plan = crate::application::launch::plan_explicit_launch(
                &FileActions::new(&manager),
                request,
            )
            .await
            .unwrap();
            let prepared = plan.content.command.unwrap();
            let script = if hold {
                let argv = crate::sandbox::shell_split(&prepared).unwrap();
                assert_eq!(&argv[..2], &["bash", "-lc"]);
                assert_eq!(argv.len(), 3);
                assert_eq!(argv[2], format!("{command}; exec \"${{SHELL:-bash}}\""));
                argv[2]
                    .strip_suffix("; exec \"${SHELL:-bash}\"")
                    .unwrap()
                    .to_owned()
            } else {
                assert_eq!(prepared, command);
                prepared
            };
            let argv = crate::sandbox::shell_split(&script).unwrap();
            assert_eq!(argv[0], "cat");
            assert_eq!(argv[1..], arguments);
        }
    }
}
