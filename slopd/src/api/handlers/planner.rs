//! Root-only transport for reading and saving the Planner backlog.
use super::{ApiResult, App, err};
use crate::api::protobuf::{Proto, reply};
use crate::shared::wire;
use axum::{
    extract::{Path, State},
    http::StatusCode,
};

pub(crate) async fn get_planner_description(
    State(app): State<App>,
    Path(id): Path<String>,
) -> ApiResult<wire::PlannerDescription> {
    reply(
        app.services
            .planner
            .read_description(&id)
            .await
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?,
    )
}

pub(crate) async fn save_planner_description(
    State(app): State<App>,
    Path(id): Path<String>,
    Proto(request): Proto<wire::PlannerSaveReq>,
) -> ApiResult<wire::PlannerDescription> {
    let sessions = app.sessions.clone();
    let description = sessions
        .owned_session_operation(async move {
            app.services
                .planner
                .save_description(&id, request.text, request.revision)
                .await
        })
        .await
        .map_err(|e| err(StatusCode::CONFLICT, e))?;
    reply(description)
}

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
    reply(
        app.services
            .planner
            .project_links(document, &tasks)
            .await
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?,
    )
}

pub(crate) async fn save_planner(
    State(app): State<App>,
    Proto(request): Proto<wire::PlannerSaveReq>,
) -> ApiResult<wire::PlannerDocument> {
    let sessions = app.sessions.clone();
    let tasks = sessions.task_store();
    let services = app.services.clone();
    let document = sessions
        .owned_session_operation(async move {
            app.services
                .planner
                .save(request.text, request.revision)
                .await
        })
        .await
        .map_err(|e| err(StatusCode::CONFLICT, e))?;
    let tasks = tasks
        .all_tasks_async()
        .await
        .map_err(|e| err(StatusCode::INTERNAL_SERVER_ERROR, e))?;
    reply(
        services
            .planner
            .project_links(document, &tasks)
            .await
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?,
    )
}

pub(crate) async fn save_planner_links(
    State(app): State<App>,
    Path(id): Path<String>,
    Proto(request): Proto<wire::PlannerLinksReq>,
) -> ApiResult<wire::PlannerDocument> {
    let sessions = app.sessions.clone();
    let document = sessions
        .owned_session_operation(async move {
            let tasks = app.sessions.task_store().all_tasks_async().await?;
            let workers: Vec<_> = app
                .sessions
                .views()
                .await
                .into_iter()
                .filter(|session| session.worker.enabled)
                .map(|session| session.launch.state_id)
                .collect();
            let links = request
                .links
                .into_iter()
                .map(|link| crate::application::planner::Link {
                    kind: link.kind,
                    id: link.id,
                    label: link.label,
                    status: String::new(),
                })
                .collect();
            let document = app
                .services
                .planner
                .save_links(&id, links, &request.revision, &tasks, &workers)
                .await?;
            app.services.planner.project_links(document, &tasks).await
        })
        .await
        .map_err(|e| err(StatusCode::CONFLICT, e))?;
    reply(document)
}

#[cfg(test)]
#[path = "planner_tests.rs"]
mod tests;
