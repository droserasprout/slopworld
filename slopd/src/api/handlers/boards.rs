//! Shared, authenticated board operations. The session read lease stabilizes the
//! project catalog through commit even when the caller disconnects.
use super::{ApiResult, App, Cap, Proto, err, wire};
use axum::{Extension, extract::State, http::StatusCode};
pub(crate) async fn get_boards(State(app): State<App>) -> ApiResult<wire::BoardCatalog> {
    access(app, None).await
}

pub(crate) async fn edit_board(
    State(app): State<App>,
    Extension(cap): Extension<Cap>,
    Proto(edit): Proto<wire::BoardEdit>,
) -> ApiResult<wire::BoardCatalog> {
    access(
        app,
        Some((edit, cap.principal().unwrap_or("host").to_owned())),
    )
    .await
}

async fn access(
    app: App,
    edit: Option<(wire::BoardEdit, String)>,
) -> ApiResult<wire::BoardCatalog> {
    app.sessions
        .clone()
        .owned_session_read_operation(async move {
            let projects =
                crate::application::boards::identities(&*app.config_authority.read_config().await);
            app.services.boards.run(projects, edit).await
        })
        .await
        .map(Proto)
        .map_err(|e| err(StatusCode::CONFLICT, format!("{e:#}")))
}

#[cfg(test)]
#[path = "boards_tests.rs"]
mod tests;
