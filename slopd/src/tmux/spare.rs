//! One unused silent host pane. Promotion leaves reader attachment and execution to startup.

use super::Tmux;
use anyhow::Result;

// Whitespace is forbidden in application session names. Hide this transport-only
// session from enumeration/adoption, including after a daemon restart.
pub(super) const NAME: &str = "slopworld spare";

impl Tmux {
    /// Maintenance fills the slot; foreground launches never wait for its mutex.
    pub(crate) async fn warm_spare(&self) -> Result<()> {
        let Ok(mut slot) = self.spare.try_lock() else {
            return Ok(());
        };
        if *slot {
            return Ok(());
        }
        let target = format!("={NAME}");
        // Retire an unused pane left by a previous daemon or interrupted fill.
        drop(self.kill(&target).await);
        self.spawn(
            NAME,
            "/",
            80,
            24,
            &["sleep".into(), "2147483647".into()],
            true,
        )
        .await?;
        *slot = true;
        Ok(())
    }

    /// Claim once, resize before reader attachment, and preserve the pane's identity.
    /// The real command later supplies its own working directory through respawn-pane.
    pub(crate) async fn take_spare(&self, name: &str, cols: u16, rows: u16) -> bool {
        let Ok(mut slot) = self.spare.try_lock() else {
            return false;
        };
        if !std::mem::take(&mut *slot) {
            return false;
        }
        // Target the reserved name exactly. A server restart can reuse numeric
        // session IDs for an unrelated pane while our cached readiness is stale.
        let target = format!("={NAME}");
        // Keep the slot locked until rename/cleanup: a refill must not kill the
        // still-hidden pane while promotion is in flight.
        let result = async {
            let window = format!("{target}:");
            let cols = cols.to_string();
            let rows = rows.to_string();
            self.run_batch(&[
                &["resize-window", "-t", &window, "-x", &cols, "-y", &rows],
                &["rename-session", "-t", &target, name],
            ])
            .await?;
            anyhow::Ok(())
        }
        .await;
        if let Err(error) = result {
            drop(self.kill(&target).await);
            tracing::debug!("discarding unavailable tmux spare: {error:#}");
            return false;
        }
        true
    }
}

#[cfg(test)]
#[path = "spare_tests.rs"]
mod tests;
