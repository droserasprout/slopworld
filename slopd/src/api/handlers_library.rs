use crate::api::protobuf::{domain, reply, Proto};
use crate::shared::wire;
use axum::extract::{Path, Query, State};

use serde_json::json;

use crate::config::{LibraryItemCfg, LibraryItemKind};
use crate::session::RunWhere;

use super::super::types::*;
use super::{err, ApiResult, Mgr};

pub(crate) async fn list_projects(State(m): State<Mgr>) -> ApiResult<wire::ProjectsReply> {
    reply(json!({ "projects": m.projects().await }))
}

pub(crate) async fn one_project(
    State(m): State<Mgr>,
    Path(name): Path<String>,
) -> ApiResult<wire::Project> {
    m.projects()
        .await
        .into_iter()
        .find(|p| p.config.name == name)
        .map(|p| reply(json!(p)))
        .ok_or_else(|| {
            err(
                axum::http::StatusCode::NOT_FOUND,
                format!("no such project: {name}"),
            )
        })?
}

pub(crate) async fn create_project(
    State(m): State<Mgr>,
    Proto(value): Proto<wire::Project>,
) -> ApiResult<wire::Ack> {
    let p = crate::api::parse_project(domain(value)?)?;
    super::ok_json(m.add_project(p).await)
}

pub(crate) async fn project_preview(
    Proto(req): Proto<wire::ProjectPreviewReq>,
) -> ApiResult<wire::ProjectPreviewResult> {
    let req: ProjectPreviewReq = domain(req)?;
    reply(json!({
        "name": req.name,
        "temp": req.temp,
        "dir": if req.temp { crate::config::temp_dir(&req.name) } else { String::new() },
    }))
}

pub(crate) async fn update_project(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    Proto(value): Proto<wire::Project>,
) -> ApiResult<wire::Ack> {
    let p = crate::api::parse_project(domain(value)?)?;
    super::ok_json(m.update_project(&name, p).await)
}

pub(crate) async fn destroy_project(
    State(m): State<Mgr>,
    Path(name): Path<String>,
) -> ApiResult<wire::Ack> {
    super::ok_json(m.remove_project(&name).await)
}

pub(crate) async fn list_library(State(m): State<Mgr>) -> ApiResult<wire::LibraryReply> {
    reply(json!({ "library": m.library().await }))
}

pub(crate) async fn create_library_item(
    State(m): State<Mgr>,
    Proto(sc): Proto<wire::LibraryItem>,
) -> ApiResult<wire::Ack> {
    let sc: LibraryItemCfg = domain(sc)?;
    super::ok_json(m.add_library_item(sc).await)
}

pub(crate) async fn update_library_item(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    Proto(sc): Proto<wire::LibraryItem>,
) -> ApiResult<wire::Ack> {
    let sc: LibraryItemCfg = domain(sc)?;
    super::ok_json(m.update_library_item(&name, sc).await)
}

pub(crate) async fn destroy_library_item(
    State(m): State<Mgr>,
    Path(name): Path<String>,
) -> ApiResult<wire::Ack> {
    super::ok_json(m.remove_library_item(&name).await)
}

/// Answers with the temporary agent's name as soon as it is up; the text lands well past the
/// point this client would have given up waiting. The body says where to run -
/// `{"project":"..."}` or `{"temp":true}` - and `Option<Json<_>>` is so a bodyless curl still
/// runs the errands that already know.
pub(crate) async fn run_library_item(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    want: Option<Proto<wire::RunWhere>>,
) -> ApiResult<wire::SessionResult> {
    let session = m
        .run_library_item(
            &name,
            want.map(|Proto(w)| domain::<RunWhere>(w))
                .transpose()?
                .unwrap_or_default(),
        )
        .await
        .map_err(|e| err(axum::http::StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "ok": true, "session": session }))
}

pub(crate) async fn run(
    State(m): State<Mgr>,
    Proto(q): Proto<wire::RunReq>,
) -> ApiResult<wire::SessionResult> {
    let q: RunReq = domain(q)?;
    let project = q.project.trim();
    let command = if q.path.trim().is_empty() {
        q.command.trim().to_string()
    } else {
        m.file_action_command(project, &q.path, q.command.trim(), q.host)
            .await
            .map_err(|e| err(axum::http::StatusCode::BAD_REQUEST, e))?
    };
    let command = command.trim();
    let command = if q.hold && !q.path.trim().is_empty() {
        crate::session::hold_action_command(command)
    } else {
        command.to_string()
    };
    // A shell errand with nothing to run is a shell - `[defaults] shell` inside the sandbox,
    // `$SHELL` on the host - and saying so again here would be the caller guessing at this
    // machine's answer. Every other kind has to say: a prompt with no command is an agent
    // nobody named.
    if command.is_empty() && q.kind != LibraryItemKind::Shell {
        return Err(err(
            axum::http::StatusCode::BAD_REQUEST,
            "an errand must say what to run",
        ));
    }
    if project.is_empty() && !q.temp && !q.host {
        return Err(err(
            axum::http::StatusCode::BAD_REQUEST,
            "an errand must name a project or ask for a temporary one",
        ));
    }

    let label = match q.label.trim() {
        // A host errand names itself for the project it opened on and the shell it opens -
        // `slopworld-zsh`. The game cannot know which shell that is, so it sends no label and
        // reads the name back off the answer, the same as it does for the session itself.
        "" if q.host => crate::sandbox::host_session_name(project),
        "" => "run".to_string(),
        l => l.to_string(),
    };
    let sc = LibraryItemCfg {
        name: label,
        kind: q.kind,
        link: crate::config::LibraryItemLink::Project,
        project: project.to_string(),
        text: q.text,
        // None rather than an empty string: `session_for` reads "no command of its own" off
        // the Option, and that is what falls through to the preset.
        command: (!command.is_empty()).then_some(command),
        mode: crate::config::FileActionMode::Ask,
        builtin: false,
        host: q.host,
        agent_template: q.agent_template,
    };

    // The project is checked by `run_errand` itself, which is also where a temporary one is
    // coined - so `temp` rides over as the override it already is rather than a second road.
    let want = RunWhere {
        cols: q.cols,
        rows: q.rows,
        project: None,
        // A host reader for a private-state directory has no project to attach to. Give it a
        // disposable project only so the existing errand/session machinery can own its cwd;
        // the command itself carries the selected absolute path.
        temp: q.temp || (q.host && project.is_empty()),
        random_tips: q.random_tips,
    };
    // Explicit commands (viewers, editors and diffs) are disposable even with a project.
    let persistent_host = q.host
        && q.kind == LibraryItemKind::Shell
        && !project.is_empty()
        && q.path.trim().is_empty()
        && q.command.trim().is_empty()
        && !q.temp;
    let like = q.like.trim().to_string();
    let session = m
        .run_errand(
            sc,
            want,
            q.host || !q.path.trim().is_empty(),
            persistent_host,
            &like,
        )
        .await
        .map_err(|e| err(axum::http::StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "ok": true, "session": session }))
}

/// Run a non-interactive Files or Git action and return a small result for a game message. Project
/// paths are validated against their project, but all file actions execute on the host.
/// Interactive actions use `/api/run`, since their terminal needs a tmux session and a persistent
/// screen.
pub(crate) async fn file_action(
    State(m): State<Mgr>,
    Proto(q): Proto<wire::FileActionReq>,
) -> ApiResult<wire::OutputResult> {
    let q: FileActionReq = domain(q)?;
    let output = m
        .file_action(&q.project, &q.path, &q.command, q.host)
        .await
        .map_err(|e| err(axum::http::StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "ok": true, "output": output }))
}

/// List the host desktop applications associated with a file. This is root-only because the
/// query runs outside every project sandbox, in the same desktop environment that will launch
/// the selected application.
pub(crate) async fn open_apps(
    State(m): State<Mgr>,
    Query(q): Query<OpenAppsQuery>,
) -> ApiResult<wire::AppsReply> {
    let apps = m
        .open_apps(&q.path)
        .await
        .map_err(|e| err(axum::http::StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "apps": apps }))
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::Config;
    use axum::http::StatusCode;

    fn proto<T: serde::de::DeserializeOwned>(value: serde_json::Value) -> Proto<T> {
        Proto(serde_json::from_value(value).unwrap())
    }

    #[tokio::test]
    async fn library_crud_preserves_content_and_rejects_invalid_updates() {
        let m = crate::session::test_manager(Config::default());
        let item = json!({"name":"test-note", "kind":"breadcrumb", "text":"remember this", "builtin":true});
        assert!(
            create_library_item(State(m.clone()), proto(item.clone()))
                .await
                .unwrap()
                .0
                .ok
        );
        assert_eq!(
            create_library_item(State(m.clone()), proto(item))
                .await
                .unwrap_err()
                .0,
            StatusCode::BAD_REQUEST
        );
        assert_eq!(
            update_library_item(
                State(m.clone()),
                Path("test-note".into()),
                proto(json!({"name":"test-note","kind":"breadcrumb","text":""}))
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::BAD_REQUEST
        );
        let listed = list_library(State(m.clone())).await.unwrap().0;
        let item = listed
            .library
            .iter()
            .find(|item| item.name.as_deref() == Some("test-note"))
            .unwrap();
        assert_eq!(item.text.as_deref(), Some("remember this"));
        assert!(!item.builtin.unwrap_or(false));
        assert!(
            update_library_item(
                State(m.clone()),
                Path("test-note".into()),
                proto(json!({"name":"renamed-note","kind":"breadcrumb","text":"updated"}))
            )
            .await
            .unwrap()
            .0
            .ok
        );
        let listed = list_library(State(m.clone())).await.unwrap().0;
        assert!(!listed
            .library
            .iter()
            .any(|item| item.name.as_deref() == Some("test-note")));
        assert_eq!(
            listed
                .library
                .iter()
                .find(|item| item.name.as_deref() == Some("renamed-note"))
                .unwrap()
                .text
                .as_deref(),
            Some("updated")
        );
        assert!(
            destroy_library_item(State(m.clone()), Path("renamed-note".into()))
                .await
                .unwrap()
                .0
                .ok
        );
        assert_eq!(
            destroy_library_item(State(m.clone()), Path("renamed-note".into()))
                .await
                .unwrap_err()
                .0,
            StatusCode::BAD_REQUEST
        );
        assert!(m.config().await.library.is_empty());
    }

    #[tokio::test]
    async fn project_crud_returns_not_found_and_persists_rename() {
        let m = crate::session::test_manager(Config::default());
        let project = json!({"name":"test-project", "dir":std::env::temp_dir().to_str().unwrap()});
        assert!(
            create_project(State(m.clone()), proto(project.clone()))
                .await
                .unwrap()
                .0
                .ok
        );
        assert_eq!(
            create_project(State(m.clone()), proto(project.clone()))
                .await
                .unwrap_err()
                .0,
            StatusCode::BAD_REQUEST
        );
        assert_eq!(
            one_project(State(m.clone()), Path("test-project".into()))
                .await
                .unwrap()
                .0
                .name
                .as_deref(),
            Some("test-project")
        );
        let mut renamed = project;
        renamed["name"] = json!("renamed-project");
        assert!(
            update_project(
                State(m.clone()),
                Path("test-project".into()),
                proto(renamed)
            )
            .await
            .unwrap()
            .0
            .ok
        );
        assert_eq!(
            one_project(State(m.clone()), Path("test-project".into()))
                .await
                .unwrap_err()
                .0,
            StatusCode::NOT_FOUND
        );
        assert_eq!(
            list_projects(State(m.clone()))
                .await
                .unwrap()
                .0
                .projects
                .len(),
            1
        );
        assert!(
            destroy_project(State(m.clone()), Path("renamed-project".into()))
                .await
                .unwrap()
                .0
                .ok
        );
        assert!(list_projects(State(m)).await.unwrap().0.projects.is_empty());
    }

    #[tokio::test]
    async fn invalid_errands_and_file_actions_return_bad_request() {
        let m = crate::session::test_manager(Config::default());
        for (request, message) in [
            (
                json!({"kind":"prompt","command":" ","temp":true}),
                "what to run",
            ),
            (
                json!({"kind":"shell","command":"echo test"}),
                "name a project",
            ),
            (
                json!({"kind":"shell","project":"missing","path":"file","command":"cat"}),
                "no such project",
            ),
        ] {
            let (status, Proto(error)) = run(State(m.clone()), proto(request)).await.unwrap_err();
            assert_eq!(status, StatusCode::BAD_REQUEST);
            assert!(error.error.contains(message), "{}", error.error);
        }
        for want in [None, Some(proto(json!({"temp":true})))] {
            assert_eq!(
                run_library_item(State(m.clone()), Path("missing".into()), want)
                    .await
                    .unwrap_err()
                    .0,
                StatusCode::BAD_REQUEST
            );
        }
        assert_eq!(
            file_action(
                State(m),
                proto(json!({"project":"missing","path":"file","command":"cat"}))
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::BAD_REQUEST
        );
    }

    #[tokio::test]
    async fn preview_only_allocates_a_directory_for_temporary_projects() {
        for temp in [false, true] {
            let preview = project_preview(proto(json!({"name":"preview-project","temp":temp})))
                .await
                .unwrap()
                .0;
            assert_eq!(preview.name, "preview-project");
            assert_eq!(preview.temp, temp);
            assert_eq!(
                preview.dir,
                if temp {
                    crate::config::temp_dir("preview-project")
                } else {
                    String::new()
                }
            );
        }
    }
}
