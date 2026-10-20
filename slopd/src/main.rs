//! Daemon startup, background services, HTTP authentication, and shutdown.

mod activity;
mod agent_templates;
mod api;
mod application;
mod audio;
mod benchmark;
mod clipboard;
mod clock;
mod config;
mod emu;
mod endpoint;
mod files;
mod git;
mod grant;
mod jukebox;
mod latency;
mod music;
mod paths;
mod perf;
mod presets;
mod process;
mod prompt;
mod runtime;
mod sandbox;
mod session;
#[expect(
    dead_code,
    reason = "the daemon shares path query helpers with the host launchers"
)]
mod shared;
mod storage;
mod storage_id;
mod tasks;
#[cfg(test)]
mod test_http;
#[cfg(test)]
mod test_support;
mod title;
mod tmux;
mod usage;
#[cfg(test)]
mod version;
mod worktrees;

use std::sync::Arc;

use anyhow::{Context, Result};
use axum::extract::{Request, State};
use axum::http::StatusCode;
use axum::middleware::{self, Next};
use axum::response::IntoResponse;

use crate::config::Config;
use crate::session::Manager;

#[tokio::main]
async fn main() -> Result<()> {
    let args: Vec<_> = std::env::args().skip(1).collect();
    anyhow::ensure!(
        args.is_empty() || args == ["--perf-bench"],
        "unsupported daemon arguments; run slopd without arguments"
    );
    if args == ["--perf-bench"] {
        return benchmark::run();
    }

    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_env("SLOPD_LOG")
                .unwrap_or_else(|_| "slopd=info".into()),
        )
        .init();

    // Load and validate configuration before starting services.
    let cfg_path = Config::path_in_use();
    let binding = storage::target::StorageBinding::resolved(&cfg_path)?;
    let reservations = storage::startup::reserve(&binding).await?;
    let loaded = storage::layout::load(&binding).await?;
    let cfg = &loaded.config;
    runtime::validate_runtime_name()?;
    tracing::info!("config: {}", cfg_path.display());
    tracing::info!("runtime: {}", runtime::capabilities().runtime);

    let table = presets::table();
    tracing::info!(
        "presets: {} sandbox, {} command, user files from {}",
        table.sandbox.len(),
        table.commands.len(),
        presets::Table::dir().display()
    );
    drop(table);

    let jukebox = jukebox::catalog();
    tracing::info!(
        "jukebox: {} stations, user files from {}",
        jukebox.stations.len(),
        jukebox::Catalog::dir().display()
    );
    drop(jukebox);

    let git_dirs = cfg
        .projects
        .iter()
        .filter(|project| !project.dir.is_empty())
        .map(|project| std::path::PathBuf::from(crate::config::expand(&project.dir)))
        .collect();
    let bind = cfg.daemon.bind.clone();
    let listener = storage::startup::serving_listener(reservations, &bind).await?;
    let tmux = tmux::Tmux::new(tmux::tmux_socket());
    let services =
        application::Services::load(&loaded.binding.data, &loaded.binding.config, tmux.clone())
            .await?;
    let (config, storage) = loaded.into_parts();
    let config_authority = Arc::new(application::ConfigAuthority::new(
        config,
        grant::Grants::load_data(&storage.binding.data).with_context(|| {
            format!(
                "loading grant store for {}",
                storage.binding.settings.display()
            )
        })?,
    ));
    let m = Manager::new(storage, services.clone(), tmux, config_authority.clone()).await?;
    // Player recovery needs adopted host terminals, before polling or serving requests.
    services.music.recover(&m).await;

    // Warm Git caches before publishing the endpoint so initial status reads are incremental.
    git::warm_projects(git_dirs).await;

    // Session maintenance retains lifecycle coordination.
    let poller = {
        let m: Arc<Manager> = m.clone();
        tokio::spawn(async move {
            loop {
                let delay = m.maintenance_delay().await;
                let wake = m.maintenance_wake();
                tokio::select! {
                    () = tokio::time::sleep(delay) => m.retick().await,
                    () = wake.notified() => {}
                }
            }
        })
    };

    // Spawn even when disabled so polling can be enabled without a restart.
    let usage = services.spawn_usage_poll(config_authority.daemon_settings());

    // Monitor the jukebox. The mod selects what to play.
    let audio = services.spawn_music_poll();

    // Publish discovery information only after the listener is bound.
    let app = api::router(m.clone())
        .layer(middleware::from_fn_with_state(
            config_authority.clone(),
            auth,
        ))
        .layer(middleware::from_fn(api::protobuf::normalize_errors));

    tracing::info!("listening on http://{bind}");
    endpoint::write(&bind, &m.config().await.daemon.token).await?;

    axum::serve(listener, app)
        .with_graceful_shutdown(async {
            shutdown().await;
            tracing::info!("shutting down (tmux sessions keep running)");
        })
        .await?;

    endpoint::remove();

    poller.abort();
    usage.abort();
    audio.abort();
    Ok(())
}

/// Wait for Ctrl+C or SIGTERM to begin graceful HTTP shutdown.
async fn shutdown() {
    use tokio::signal::unix::{SignalKind, signal};
    let mut interrupt = signal(SignalKind::interrupt())
        .map_err(|error| {
            tracing::error!("cannot listen for SIGINT: {error:#}");
        })
        .ok();
    let mut terminate = signal(SignalKind::terminate())
        .map_err(|error| {
            tracing::error!("cannot listen for SIGTERM: {error:#}");
        })
        .ok();
    // A failed registration disables only that listener. It is never a shutdown event.
    tokio::select! {
        () = receive_signal(&mut interrupt) => {}
        () = receive_signal(&mut terminate) => {}
    }
}

async fn receive_signal(signal: &mut Option<tokio::signal::unix::Signal>) {
    if let Some(signal) = signal {
        if signal.recv().await.is_some() {
            return;
        }
        tracing::error!("shutdown signal listener closed");
    }
    std::future::pending::<()>().await;
}

/// Attach the token’s capability for handlers, or reject unauthorized requests.
async fn auth(
    State(config_authority): State<Arc<application::ConfigAuthority>>,
    mut req: Request,
    next: Next,
) -> Result<impl IntoResponse, StatusCode> {
    let root_generation = config_authority.root_generation();
    let presented = api::presented_token(req.headers());
    match config_authority.resolve_cap(presented.as_deref()).await {
        Some(cap) => {
            if cap.may_create() {
                if root_generation != config_authority.root_generation() {
                    return Err(StatusCode::UNAUTHORIZED);
                }
                req.extensions_mut()
                    .insert(api::RootGeneration(root_generation));
            }
            req.extensions_mut().insert(cap);
            Ok(next.run(req).await)
        }
        None => Err(StatusCode::UNAUTHORIZED),
    }
}

#[cfg(test)]
#[path = "main_tests.rs"]
mod tests;
