//! Root-only planner transport. Long provider calls run outside lifecycle admission;
//! execution rechecks root generation and document revision at its commit boundary.
use super::{ApiResult, App, err};
use crate::api::{
    RootGeneration,
    protobuf::{Proto, reply},
};
use crate::{application::planner, grant::Cap, shared::wire};
use axum::{Extension, extract::State, http::StatusCode};

pub(crate) async fn get_planner(State(app): State<App>) -> ApiResult<wire::PlannerDocument> {
    let document = app
        .services
        .planner
        .read()
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let tasks = app
        .sessions
        .task_store()
        .all_tasks_async()
        .await
        .map_err(|e| err(StatusCode::INTERNAL_SERVER_ERROR, e))?;
    reply(document.with_tasks(&tasks))
}

pub(crate) async fn save_planner(
    State(app): State<App>,
    Proto(request): Proto<wire::PlannerSaveReq>,
) -> ApiResult<wire::PlannerDocument> {
    let sessions = app.sessions.clone();
    let document = sessions
        .owned_session_operation(async move {
            app.services
                .planner
                .save(request.text, request.revision)
                .await
        })
        .await
        .map_err(|e| err(StatusCode::CONFLICT, e))?;
    reply(document)
}

pub(crate) async fn planner_action(
    State(app): State<App>,
    Extension(cap): Extension<Cap>,
    generation: Option<Extension<RootGeneration>>,
    Proto(request): Proto<wire::PlannerActionReq>,
) -> ApiResult<wire::PlannerActionReply> {
    super::guard_root(&cap)?;
    let generation = generation
        .ok_or_else(|| err(StatusCode::UNAUTHORIZED, "Missing root credentials."))?
        .0;
    if app.config_authority.root_generation() != generation.0 {
        return Err(err(StatusCode::UNAUTHORIZED, "Root credentials changed."));
    }
    let result = planner::act(
        &app,
        planner::Action {
            action: request.action,
            revision: request.revision,
            line: request.line,
            project: request.project,
            template: request.template,
        },
        generation.0,
    )
    .await
    .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(result)
}

#[cfg(test)]
#[path = "planner_tests.rs"]
mod tests;
