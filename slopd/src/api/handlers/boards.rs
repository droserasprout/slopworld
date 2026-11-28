//! Shared, authenticated board operations. The session read lease stabilizes the
//! project catalog through commit even when the caller disconnects.
use super::{ApiResult, App, Cap, Proto, err, wire};
use axum::{
    Extension,
    extract::{Query, State},
    http::StatusCode,
};

#[derive(serde::Deserialize)]
pub(crate) struct BoardFilter {
    pub(crate) task: Option<String>,
}
pub(crate) async fn get_boards(
    State(app): State<App>,
    Extension(cap): Extension<Cap>,
    Query(filter): Query<BoardFilter>,
) -> ApiResult<wire::BoardCatalog> {
    access(app, cap, None, filter.task).await
}

pub(crate) async fn edit_board(
    State(app): State<App>,
    Extension(cap): Extension<Cap>,
    Proto(edit): Proto<wire::BoardEdit>,
) -> ApiResult<wire::BoardCatalog> {
    access(app, cap, Some(edit), None).await
}

async fn access(
    app: App,
    cap: Cap,
    edit: Option<wire::BoardEdit>,
    task: Option<String>,
) -> ApiResult<wire::BoardCatalog> {
    app.sessions
        .clone()
        .owned_session_read_operation(async move {
            let projects =
                crate::application::boards::identities(&*app.config_authority.read_config().await);
            let scope = if let Some(name) = cap.principal() {
                let denied = || err(StatusCode::FORBIDDEN, "board permission denied");
                let agent = app.sessions.session_cfg(name).await.ok_or_else(denied)?;
                let project = projects
                    .iter()
                    .find(|(_, name)| *name == agent.project)
                    .map(|(id, _)| id.clone());
                let permissions = agent.permissions;
                if edit
                    .as_ref()
                    .is_some_and(|e| matches!(e.action.as_str(), "enable-board" | "disable-board"))
                    || !cap.is_valid()
                    || permissions.board_access < required_access(edit.as_ref())
                    || edit
                        .as_ref()
                        .is_some_and(|e| !permissions.sees_board(&e.board, project.as_deref()))
                {
                    return Err(denied());
                }
                Some((permissions, project))
            } else {
                None
            };
            let edit = edit.map(|e| (e, cap.principal().unwrap_or("host").to_owned()));
            let boards = &app.services.boards;
            let mut catalog = match task {
                Some(id) => boards.linked_cards(projects, id).await,
                None => boards.run(projects, edit).await,
            }
            .map_err(|e| err(StatusCode::CONFLICT, format!("{e:#}")))?;
            let level = scope
                .as_ref()
                .map_or(crate::config::BoardAccess::Admin, |(p, _)| p.board_access);
            catalog.can_write = level >= crate::config::BoardAccess::Write;
            catalog.can_admin = level == crate::config::BoardAccess::Admin;
            if let Some((permissions, project)) = scope {
                catalog
                    .boards
                    .retain(|b| permissions.sees_board(&b.id, project.as_deref()));
            }
            Ok(Proto(catalog))
        })
        .await
}
fn required_access(edit: Option<&wire::BoardEdit>) -> crate::config::BoardAccess {
    use crate::config::BoardAccess;
    match edit.map(|e| e.action.as_str()) {
        None => BoardAccess::Read,
        Some(
            "add-column" | "rename-column" | "move-column" | "add-card" | "move-card" | "title"
            | "describe" | "assign" | "comment" | "link" | "unlink",
        ) => BoardAccess::Write,
        Some(_) => BoardAccess::Admin,
    }
}

#[cfg(test)]
#[path = "boards_tests.rs"]
mod tests;
