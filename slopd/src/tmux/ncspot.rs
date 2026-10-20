//! Private managed-player markers and applied-volume recovery metadata.
use anyhow::Result;

impl crate::tmux::Tmux {
    pub(crate) async fn mark_ncspot(&self, name: &str) -> Result<()> {
        self.set_option(name, "@slopworld_ncspot", "1").await?;
        Ok(())
    }

    pub(crate) async fn is_ncspot(&self, name: &str) -> bool {
        self.option(name, "@slopworld_ncspot").await.as_deref() == Some("1")
    }

    pub(crate) async fn ncspot_volume(&self, name: &str) -> Option<f32> {
        self.option(name, "@slopworld_ncspot_volume")
            .await?
            .parse::<f32>()
            .ok()
            .filter(|volume| (0.0..=1.0).contains(volume))
    }

    pub(crate) async fn set_ncspot_volume(&self, name: &str, volume: f32) -> Result<()> {
        self.set_option(name, "@slopworld_ncspot_volume", &volume.to_string())
            .await?;
        Ok(())
    }
}

#[cfg(test)]
#[path = "ncspot_tests.rs"]
mod tests;
