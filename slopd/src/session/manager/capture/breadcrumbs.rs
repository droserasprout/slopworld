//! Render explicitly selected library breadcrumbs for paste without submission.
//! Terminal admission and delivery remain in input.rs.

use super::*;
use anyhow::anyhow;

impl Manager {
    /// Render a breadcrumb and paste it into this agent without submitting.
    pub async fn paste_breadcrumb(
        self: &Arc<Self>,
        name: &str,
        breadcrumb: &str,
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
        let b = cfg
            .library_item(breadcrumb)
            .filter(|b| b.kind == LibraryItemKind::Breadcrumb)
            .ok_or_else(|| anyhow!("no such breadcrumb: {breadcrumb}"))?;
        let command = cfg.command_of(&s);
        let directory = expand(&p.dir);
        let vars = PromptVars {
            agent: &s.name,
            project: &p.name,
            directory: &directory,
            command: &command,
        };
        let text = render_prompt_with(&b.text, &random_tips, Some(&vars));
        drop(cfg);
        self.ensure_paste_ready(name).await?;
        self.queue_paste(name, text.into_bytes()).await;
        Ok(())
    }
}

#[cfg(test)]
#[path = "breadcrumbs_tests.rs"]
mod tests;
