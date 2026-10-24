//! Daemon command-line syntax and generated help; main owns startup dispatch.

use clap::Parser;

#[derive(Debug, Parser)]
#[command(
    name = "slopd",
    styles = crate::cli_styles::styles(),
    about = "Run the SlopWorld session daemon",
    version = env!("SLOPWORLD_VERSION")
)]
pub(crate) struct Args {
    /// Run game-free performance benchmarks without loading daemon configuration
    #[arg(long)]
    pub perf_bench: bool,
}

#[cfg(test)]
mod tests;
