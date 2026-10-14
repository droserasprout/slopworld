//! Persistent title ordering: hooks installed by `server`, format used by capture.

use super::Tmux;
use anyhow::{Context, Result};

// Unmarked panes use automatic-rename until the next title update.
// CaptureMetadata expects the title last.
pub(super) const CAPTURE_FORMAT: &str = "#{bracket_paste_flag} #{cursor_x} #{cursor_y} #{alternate_on} #{mouse_standard_flag} #{mouse_button_flag} #{mouse_all_flag} #{mouse_utf8_flag} #{mouse_sgr_flag} #{?#{==:#{@slopworld_title_source},pane},#{pane_title},#{?automatic-rename,#{pane_title},#{window_name}}}";

impl Tmux {
    pub(super) async fn configure_application_titles(&self) -> Result<()> {
        // ESC k disables automatic-rename even after a later OSC title.
        // Hooks retain source order without embedding title text in commands.
        // Omit -t to use the notification's window context.
        for (hook, command) in [
            (
                "pane-title-changed",
                "set-option -w @slopworld_title_source pane",
            ),
            (
                "window-renamed",
                "set-option -wF @slopworld_title_source '#{?automatic-rename,#{@slopworld_title_source},window}'",
            ),
        ] {
            self.run(&["set-hook", "-g", hook, command])
                .await
                .context("retaining application title order on the private tmux server")?;
        }
        // Retain ESC k titles (used by zsh) while readers are absent.
        self.run(&["set-option", "-g", "-w", "allow-rename", "on"])
            .await
            .context("enabling application titles on the private tmux server")?;
        Ok(())
    }
}

#[cfg(test)]
#[path = "titles_tests.rs"]
mod tests;
