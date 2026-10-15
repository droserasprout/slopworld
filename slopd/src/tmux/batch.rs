//! Ordered tmux command queues. Callers group only commands sharing failure policy.

use super::Tmux;
use anyhow::Result;

impl Tmux {
    /// Run one client; tmux stops the queue on the first error. Earlier commands
    /// remain applied, so callers still own rollback of partial success.
    pub(super) async fn run_batch(&self, commands: &[&[&str]]) -> Result<String> {
        let mut args = Vec::new();
        for command in commands {
            if !args.is_empty() {
                args.push(";".to_owned());
            }
            for arg in *command {
                // tmux recognizes a trailing semicolon even in an argv value.
                // Escape data delimiters; only the separators above start commands.
                args.push(match arg.strip_suffix(';') {
                    Some(prefix) => format!("{prefix}\\;"),
                    None => (*arg).to_owned(),
                });
            }
        }
        let args = args.iter().map(String::as_str).collect::<Vec<_>>();
        self.run(&args).await
    }
}

#[cfg(test)]
#[path = "batch_tests.rs"]
mod tests;
