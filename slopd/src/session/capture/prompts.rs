//! Resolve live context and admit explicitly selected prompts without submission.
//! Application library owns lookup and rendering.
//! Terminal admission and delivery remain in input.rs.

use super::*;
use anyhow::anyhow;

impl SessionRuntime {
    /// Render a prompt and paste it into this agent without submitting.
    pub async fn paste_prompt(
        self: &Arc<Self>,
        name: &str,
        prompt: &str,
        random_tips: Vec<String>,
    ) -> Result<()> {
        let cfg = self.config().await;
        let s = self
            .session_cfg(name)
            .await
            .ok_or_else(|| anyhow!("no such session: {name}"))?;
        let p = self
            .project_for(&cfg, &s)
            .await
            .ok_or_else(|| anyhow!("session {name} has no configured project"))?;
        let text = crate::application::library::prepare_prompt(&cfg, &s, &p, prompt, &random_tips)?;
        drop(cfg);
        self.ensure_paste_ready(name).await?;
        self.queue_paste(name, text.into_bytes()).await;
        Ok(())
    }
}

#[cfg(test)]
#[path = "prompts_tests.rs"]
mod tests;
