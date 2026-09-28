//! Pending context consumption and library breadcrumb rendering.
//! Terminal admission and delivery remain in input.rs.

use super::*;
use anyhow::anyhow;

impl Manager {
    pub(crate) async fn consume_breadcrumbs(
        &self,
        name: &str,
        random_tips: &[String],
    ) -> Option<Vec<u8>> {
        let mut live = self.live.write().await;
        let session = live.get_mut(name)?;
        if !session.input.breadcrumbs_pending {
            return None;
        }

        // Consume once, even when rendering produces an empty prompt.
        session.input.breadcrumbs_pending = false;
        let text = String::from_utf8_lossy(&session.input.breadcrumbs);
        Some(render_prompt(&text, random_tips).into_bytes())
    }

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
