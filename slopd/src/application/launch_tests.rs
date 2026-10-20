use super::*;

#[test]
fn library_launches_resolve_overrides_without_carrying_catalog_fields() {
    use crate::config::{LibraryItemCfg, LibraryItemKind, LibraryItemLink};
    let item = LibraryItemCfg {
        name: "review".into(),
        kind: LibraryItemKind::Prompt,
        project: "saved".into(),
        link: LibraryItemLink::Temp,
        text: "review changes".into(),
        agent_template: " reviewer ".into(),
        builtin: true,
        ..Default::default()
    };
    let temporary =
        crate::application::launch::library_launch(item.clone(), RunWhere::default()).unwrap();
    assert_eq!(temporary.destination, LaunchDestination::Temporary);
    assert_eq!(
        temporary.execution,
        ExecutionSource::Template("reviewer".into())
    );
    let selected = crate::application::launch::library_launch(
        item.clone(),
        RunWhere {
            project: Some(" selected ".into()),
            worktree: "checkout".into(),
            ..Default::default()
        },
    )
    .unwrap();
    assert_eq!(
        selected.destination,
        LaunchDestination::Project("selected".into())
    );
    assert_eq!(selected.options.worktree, "checkout");
    assert_eq!(selected.content.text, item.text);
    let forced = crate::application::launch::library_launch(
        item.clone(),
        RunWhere {
            project: Some("selected".into()),
            temp: true,
            ..Default::default()
        },
    )
    .unwrap();
    assert_eq!(forced.destination, LaunchDestination::Temporary);
    let mut ask = item;
    ask.link = LibraryItemLink::Ask;
    assert!(
        crate::application::launch::library_launch(ask, RunWhere::default())
            .unwrap_err()
            .to_string()
            .contains("Choose a project")
    );
}

struct FilePreparation {
    calls: std::sync::atomic::AtomicUsize,
    fail: bool,
}
impl Runtime for FilePreparation {
    async fn file_action_command(
        &self,
        project: &str,
        worktree: &str,
        path: &str,
        command: &str,
        host: bool,
    ) -> Result<String> {
        self.calls
            .fetch_add(1, std::sync::atomic::Ordering::Relaxed);
        assert_eq!(
            (project, worktree, path, command, host),
            ("repo", "checkout", "file", "cat {{ absolute_path }}", false)
        );
        if self.fail {
            bail!("file scope unavailable");
        }
        Ok("cat /resolved/file".into())
    }
}

#[tokio::test]
async fn explicit_planning_preserves_file_scope_and_propagates_preparation_failure() {
    for fail in [false, true] {
        let runtime = FilePreparation {
            calls: Default::default(),
            fail,
        };
        let result = plan_explicit_launch(
            &runtime,
            ExplicitLaunch {
                project: " repo ".into(),
                worktree: "checkout".into(),
                path: "file".into(),
                command: " cat {{ absolute_path }} ".into(),
                kind: LibraryItemKind::Shell,
                ..Default::default()
            },
        )
        .await;
        assert_eq!(runtime.calls.load(std::sync::atomic::Ordering::Relaxed), 1);
        if fail {
            assert_eq!(result.unwrap_err().to_string(), "file scope unavailable");
        } else {
            let plan = result.unwrap();
            assert_eq!(
                plan.execution,
                ExecutionSource::Host(HostLifetime::Disposable)
            );
            assert_eq!(plan.destination, LaunchDestination::Project("repo".into()));
            assert_eq!(plan.content.command.as_deref(), Some("cat /resolved/file"));
        }
    }
}

#[tokio::test]
async fn invalid_reader_intent_is_rejected_before_file_preparation() {
    let runtime = FilePreparation {
        calls: Default::default(),
        fail: true,
    };
    let result = plan_explicit_launch(
        &runtime,
        ExplicitLaunch {
            intent: "unknown".into(),
            path: "file".into(),
            ..Default::default()
        },
    )
    .await;
    assert!(
        result
            .unwrap_err()
            .to_string()
            .contains("Unknown terminal intent")
    );
    assert_eq!(runtime.calls.load(std::sync::atomic::Ordering::Relaxed), 0);
}
