//! Manual todo.txt planning. Store owns text/revision and dispatch reservations;
//! existing Tasks own worker progress. Agents never receive file write authority.
mod store;
use super::workers::{WorkerCheckout, WorkerRequest, Workers};
use super::{Application, Configuration};
use anyhow::{Result, bail};
use serde::Serialize;
pub(crate) use store::Store;

#[derive(Debug, Serialize)]
pub(crate) struct ActionResult {
    pub output: String,
    pub task_id: String,
}

pub(crate) struct Action {
    pub action: String,
    pub revision: String,
    pub line: u32,
    pub project: String,
    pub template: String,
}

pub(crate) async fn act(
    app: &Application,
    request: Action,
    generation: u64,
) -> Result<ActionResult> {
    let document = app.services.planner.read().await?;
    store::check_revision(&document.text, &request.revision)?;
    let settings = app.config_authority.daemon_settings().snapshot().await;
    let (input, prompt) = match request.action.as_str() {
        "summarize" => (document.text.clone(), settings.planner.summarize_prompt),
        "plan" => (document.text.clone(), settings.planner.plan_prompt),
        "execute" => {
            let item = store::selected(&document.text, request.line)?;
            if !item.task_id.is_empty() {
                bail!("This item has already been dispatched. Refresh and inspect its Task.");
            }
            if request.project.is_empty() || request.template.is_empty() {
                bail!("Select a project and worker template before executing.");
            }
            (item.text, settings.planner.execute_prompt)
        }
        _ => bail!("Unknown planner action."),
    };
    if input.trim().is_empty() {
        bail!("Add and save a task first.");
    }
    // Provider I/O holds neither the document gate nor the session boundary.
    let output = crate::title::complete_async(
        input,
        prompt,
        settings.openrouter_key_file,
        settings.planner.model,
    )
    .await?;
    if request.action != "execute" {
        if app.config_authority.root_generation() != generation {
            bail!("Root credentials changed during this request.");
        }
        store::check_revision(&app.services.planner.read().await?.text, &request.revision)?;
        return Ok(ActionResult {
            output,
            task_id: String::new(),
        });
    }
    let app = app.clone();
    let sessions = app.sessions.clone();
    Configuration::new(&app)
        .session_request(async move {
            sessions
                .owned_session_operation(Box::pin(async move {
                    dispatch(&app, request, generation, output).await
                }))
                .await
        })
        .await
}

// Session admission and its owned guard must remain held through reservation and startup.
async fn dispatch(
    app: &Application,
    request: Action,
    generation: u64,
    output: String,
) -> Result<ActionResult> {
    if app.config_authority.root_generation() != generation {
        bail!("Root credentials changed during this request.");
    }
    let _gate = app.services.planner.gate.lock().await;
    let text = app.services.planner.read_text().await?;
    store::check_revision(&text, &request.revision)?;
    let item = store::selected(&text, request.line)?;
    if !item.task_id.is_empty() {
        bail!("This item has already been dispatched.");
    }
    let worker_request = WorkerRequest {
        caller: crate::tasks::HOST.into(),
        project: request.project,
        template: request.template,
        body: format!(
            "{}\n\nOriginal todo.txt item (authoritative scope):\n{}\n\nWork order:\n{}\n\nReport progress and completion through your SlopWorld Task. Do not edit TODO.txt.",
            store::marker(&item.id),
            item.text,
            output
        ),
        durable: true,
        checkout: WorkerCheckout::Existing(String::new()),
    };
    // Reject ordinary policy errors before consuming the one-shot reservation.
    let cfg = Configuration::new(app).config().await;
    let templates = app.services.templates.list().await;
    super::workers::prepare(
        &cfg,
        &super::workers::Caller::Root,
        &templates,
        WorkerRequest {
            caller: worker_request.caller.clone(),
            project: worker_request.project.clone(),
            template: worker_request.template.clone(),
            body: worker_request.body.clone(),
            durable: true,
            checkout: WorkerCheckout::Existing(String::new()),
        },
    )?;
    // Persist before any worker side effect. An interrupted/failed dispatch never
    // silently retries, even after task pruning or a lost HTTP response.
    let reserved = store::link(&text, request.line, "pending")?;
    app.services.planner.write_text(&reserved).await?;
    let worker = Workers::new(app).create_worker(worker_request).await?;
    if app.services.planner.read_text().await? != reserved {
        bail!(
            "TODO.txt changed during worker startup. Task {} was created; refresh to inspect it.",
            worker.task.id
        );
    }
    let linked = store::link(&reserved, request.line, &worker.task.id)?;
    app.services.planner.write_text(&linked).await?;
    Ok(ActionResult {
        output,
        task_id: worker.task.id,
    })
}

#[cfg(test)]
mod tests;
