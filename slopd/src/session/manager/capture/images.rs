//! Desktop-authorized image paste admission. Sandbox images owns import/storage policy;
//! the terminal boundary and existing queue own run lifetime and input ordering.

use super::*;
use crate::sandbox::images;

impl Manager {
    /// Caller holds the terminal/session operation guard through import and queue admission.
    pub(crate) async fn paste_host_file_image(
        self: &Arc<Self>,
        name: &str,
        text: &str,
        expected_run: Option<u64>,
    ) -> Result<()> {
        let (session, run_id, host) = {
            let live = self.live.read().await;
            let row = live
                .get(name)
                .ok_or_else(|| anyhow!("no such session: {name}"))?;
            anyhow::ensure!(
                expected_run == Some(row.run_id),
                "paste belongs to a stale session run"
            );
            (row.cfg.clone(), row.run_id, row.host)
        };
        self.ensure_paste_ready(name).await?;
        // The client flag is an intent, never authority or proof of target type.
        let codex = !host && self.cfg.read().await.command_name(&session) == "codex";
        if !codex {
            return self.paste(name, text).await;
        }
        let Some(source) = images::local_image_uri(text)? else {
            return self.paste(name, text).await;
        };
        anyhow::ensure!(
            !crate::runtime::is_slopcar(),
            "host image imports are unavailable in the sidecar"
        );
        let identity = session.state_id.clone();
        anyhow::ensure!(
            self.tmux.option(name, images::MOUNT_STATE).await.as_deref() == Some(identity.as_str()),
            "restart this agent to enable host image imports"
        );
        let imported =
            tokio::task::spawn_blocking(move || images::import(&session, &source)).await??;
        self.commit_image_paste(name, &identity, run_id, imported)
            .await
    }

    async fn commit_image_paste(
        self: &Arc<Self>,
        name: &str,
        identity: &str,
        run_id: u64,
        imported: images::ImportedImage,
    ) -> Result<()> {
        anyhow::ensure!(
            self.live.read().await.get(name).is_some_and(|row| {
                row.cfg.state_id == identity && row.run_id == run_id && !row.host
            }),
            "image import belongs to a stale session run"
        );
        // tmux paste_bytes supplies bracketed-paste markers when the application enables
        // them. Codex 0.162.0 paste_input.rs calls handle_paste_image_path for this event,
        // validates dimensions and attaches the local image. No desktop clipboard mutation.
        self.queue_paste(name, imported.guest().as_bytes().to_vec())
            .await;
        imported.commit();
        Ok(())
    }
}

#[cfg(test)]
#[path = "images_tests.rs"]
mod tests;
