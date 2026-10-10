//! Host CLI coordination; mounts, configuration and Docker transport have separate owners.
const NOTICES: &str = include_str!("../../licenses/slopcar/THIRD-PARTY-NOTICES.txt");

mod cli;
mod config;
mod docker;
mod lifecycle;
mod mounts;
mod settings;

fn main() {
    if let Err(error) = cli::parse(std::env::args_os().skip(1).collect()).and_then(|command| {
        if matches!(command, cli::Command::Notices) {
            print!("{NOTICES}");
            return Ok(());
        }
        if matches!(command, cli::Command::Help) {
            print!("{}", cli::HELP);
            return Ok(());
        }
        lifecycle::run(command, &settings::Settings::load()?)
    }) {
        eprintln!("slopcar: {error:#}");
        std::process::exit(1);
    }
}

#[cfg(test)]
mod test_support;
