//! Application entry and ordered preparation; CLI, phase caches, AOT and portraits
//! have separate owners. Server and fonts retain transport and native discovery.
mod aot;
mod appearance;
mod cli;
mod files;
pub mod portraits;
mod preparation;
#[cfg(test)]
mod tests;

use crate::{
    fonts::FontService,
    server::{self, ConnectionSource, ServerConfig},
};
use anyhow::{Result, ensure};
use clap::{CommandFactory, Parser};
use std::{env, fs, path::Path, sync::Arc, time::Instant};

fn connection(options: &cli::Options) -> Result<ConnectionSource> {
    let source = if options.endpoint.is_some() {
        ConnectionSource::Descriptor(options.query("endpoint")?)
    } else if let Some(url) = &options.url {
        ConnectionSource::Direct {
            url: url.clone(),
            token: None,
            token_file: Some(cli::expand(options.token_file.as_ref().unwrap())?),
        }
    } else if let Ok(url) = env::var("SLOPD_URL") {
        ConnectionSource::Direct {
            url,
            token: env::var("SLOPD_TOKEN").ok(),
            token_file: None,
        }
    } else {
        ConnectionSource::Descriptor(options.query("endpoint")?)
    };
    // Validate before expensive preparation; the server still reloads the source
    // for each operation so a rotated descriptor/token never becomes stale.
    let (url, token) = match &source {
        ConnectionSource::Descriptor(path) => {
            let values: toml::Table = toml::from_str(&fs::read_to_string(path)?)?;
            (
                values
                    .get("url")
                    .and_then(toml::Value::as_str)
                    .unwrap_or_default()
                    .to_owned(),
                values
                    .get("token")
                    .and_then(toml::Value::as_str)
                    .unwrap_or_default()
                    .to_owned(),
            )
        }
        ConnectionSource::Direct {
            url,
            token,
            token_file,
        } => (
            url.clone(),
            match token_file {
                Some(path) => fs::read_to_string(path)?.trim().into(),
                None => token.clone().unwrap_or_default(),
            },
        ),
        ConnectionSource::Unconfigured => unreachable!(),
    };
    ensure!(
        !url.is_empty() && !token.is_empty(),
        "A daemon URL and token must be supplied together"
    );
    let parsed = url::Url::parse(&url)?;
    ensure!(
        matches!(parsed.scheme(), "http" | "https")
            && parsed.host_str().is_some()
            && parsed.username().is_empty()
            && parsed.password().is_none(),
        "Daemon URL must use HTTP or HTTPS without embedded credentials"
    );
    Ok(source)
}

pub async fn run() -> Result<()> {
    let options = cli::Options::parse();
    if let Some(command) = &options.command {
        match command {
            cli::LocalCommand::Paths { name } => println!("{}", options.query(name)?.display()),
            cli::LocalCommand::Completions { shell } => clap_complete::generate(
                *shell,
                &mut cli::Options::command(),
                "slopweb",
                &mut std::io::stdout(),
            ),
            cli::LocalCommand::CheckFonts { install, output } => {
                unsafe {
                    libc::umask(0o077);
                }
                let bundle = cli::bundle()?;
                let game = cli::game_directory(Some(install))?;
                let output = cli::expand(output)?;
                let service = FontService::new(
                    &bundle,
                    &game,
                    &options.query("web-cache")?.join("font-service"),
                )?;
                prepare_fonts(
                    &service,
                    &output,
                    &appearance::preferences(&options.profile_path()?)?,
                )?;
                println!("Prepared local font descriptors: {}", output.display());
            }
        }
        return Ok(());
    }
    // Set before spawning helpers or writing any private game-derived output.
    unsafe {
        libc::umask(0o077);
    }
    let bundle = cli::bundle()?;
    let profile = options.profile_path()?;
    let game = cli::game_directory(options.game.as_deref())?;
    let cache = options.query("web-cache")?;
    let connection = if options.prepare {
        ConnectionSource::Unconfigured
    } else {
        connection(&options)?
    };
    let started = Instant::now();
    let sdk = if options.use_aot() {
        Some(aot::toolchain(&options.sdk()?, options.ensure_workload)?)
    } else {
        None
    };
    let fonts = Arc::new(FontService::new(
        &bundle,
        &game,
        &cache.join("font-service"),
    )?);
    let appearance = appearance::preferences(&profile)?;
    // The installed font inventory is a phase input, including changed file bytes.
    let font_inventory = fonts.startup_fingerprint()?;
    let prepared = preparation::prepare(
        &bundle,
        &game,
        &cache,
        options.rebuild,
        &appearance,
        &font_inventory,
        |phase, root, settings| {
            if phase == "fonts" {
                prepare_fonts(&fonts, root, settings)
            } else {
                preparation::native_phase(&bundle, &game, phase, root)
            }
        },
    );
    let result = prepared.and_then(|root| match &sdk {
        Some(sdk) => aot::prepare(&bundle, &root, &cache, sdk),
        None => Ok(root),
    });
    println!(
        "Runtime preparation summary: {:.2}s",
        started.elapsed().as_secs_f64()
    );
    let root = result?;
    if options.prepare {
        println!("Prepared local runtime: {}", root.display());
        return Ok(());
    }
    println!("Open http://127.0.0.1:{}/", options.port);
    server::serve(
        ServerConfig {
            root,
            port: options.port,
            public_origin: options.public_origin,
            connection,
        },
        fonts,
    )
    .await
}

fn prepare_fonts(
    fonts: &FontService,
    root: &Path,
    settings: &serde_json::Map<String, serde_json::Value>,
) -> Result<()> {
    let resolved = fonts.prepare_startup(root, serde_json::Value::Object(settings.clone()))?;
    appearance::write_preferences(
        root,
        resolved
            .as_object()
            .ok_or_else(|| anyhow::anyhow!("Font startup returned invalid appearance"))?,
    )
}
