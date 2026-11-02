//! Timer action dispatch. Worker and launch owners retain allocation, task and cleanup policy.
use super::*;
use crate::application::workers::{WorkerCheckout, WorkerRequest, Workers};
use crate::session::RunWhere;

pub(super) async fn execute(
    cfg: &Config,
    app: &Application,
    targeted: &mut HashSet<String>,
    timer: &TimerCfg,
) -> Result<String> {
    match timer.action.as_str() {
        "prompt" => {
            let Some(target) = cfg
                .sessions
                .iter()
                .find(|s| !timer.target.is_empty() && s.state_id == timer.target)
            else {
                return Ok("Skipped: target missing".into());
            };
            if !targeted.insert(target.state_id.clone()) {
                return Ok("Skipped: another Timer targeted this agent".into());
            }
            Ok(if app
                .sessions
                .submit_scheduled_prompt(cfg, target, &timer.prompt)
                .await?
            {
                "Submitted"
            } else {
                "Skipped: busy, down or not ready"
            }
            .into())
        }
        "worker" => {
            let prompt = cfg
                .library_item(&timer.prompt)
                .filter(|item| item.kind == LibraryItemKind::Prompt)
                .ok_or_else(|| anyhow::anyhow!("Choose an existing Library Prompt."))?;
            let spawned = Workers::new(app)
                .create_prompt_worker(
                    WorkerRequest {
                        name: String::new(),
                        caller: crate::tasks::HOST.into(),
                        project: timer.project.clone(),
                        template: timer.template.clone(),
                        body: prompt.text,
                        durable: false,
                        checkout: WorkerCheckout::Existing(String::new()),
                    },
                    timer.prompt.clone(),
                )
                .await?;
            Ok(format!("Worker created: {}", spawned.session))
        }
        "shell" => {
            let item = cfg
                .library_item(&timer.shell)
                .filter(|item| item.kind == LibraryItemKind::Shell)
                .ok_or_else(|| anyhow::anyhow!("Choose an existing Library shell script."))?;
            let request = super::super::launch::library_launch(item, RunWhere::default())?;
            let session = super::super::launch::Launches::new(app)
                .run_launch(request)
                .await?;
            Ok(format!("Shell started: {session}"))
        }
        _ => anyhow::bail!("Choose a Timer action: Prompt, Worker, or Shell script."),
    }
}
