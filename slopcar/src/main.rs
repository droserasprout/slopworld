//! Host CLI coordination; mounts, configuration and Docker transport have separate owners.

#[path = "../../slopd/src/shared/cli_completions.rs"]
mod cli_completions;
#[path = "../../slopd/src/shared/cli_styles.rs"]
mod cli_styles;
const NOTICES: &str = include_str!("../../licenses/slopcar/THIRD-PARTY-NOTICES.txt");

#[path = "../../slopd/src/shared/application_paths.rs"]
#[cfg_attr(
    not(test),
    expect(
        dead_code,
        reason = "application paths are shared by different binary consumers"
    )
)]
mod application_paths;
mod cli;
mod config;
mod docker;
mod lifecycle;
mod mounts;
mod settings;

fn main() {
    if let Err(error) = cli::parse(std::env::args_os().skip(1).collect()).and_then(|command| {
        if let cli::Command::Paths(name) = &command {
            println!(
                "{}",
                application_paths::Mode::Sidecar
                    .query(name, None)
                    .map_err(anyhow::Error::msg)?
                    .display()
            );
            return Ok(());
        }
        if matches!(command, cli::Command::Notices) {
            print!("{NOTICES}");
            return Ok(());
        }
        if let cli::Command::Help(text) = &command {
            anstream::print!("{}", text.ansi());
            return Ok(());
        }
        if let cli::Command::Completions(text) = &command {
            print!("{text}");
            return Ok(());
        }
        lifecycle::run(command, &settings::Settings::load()?)
    }) {
        if let Some(error) = error.downcast_ref::<clap::Error>() {
            anstream::eprintln!("slopcar: {}", error.render().ansi());
        } else {
            eprintln!("slopcar: {error:#}");
        }
        std::process::exit(1);
    }
}

#[cfg(test)]
mod test_support;
