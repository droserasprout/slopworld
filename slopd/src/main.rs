//! Daemon startup, background services, HTTP authentication, and shutdown.

mod activity;
mod api;
mod audio;
mod benchmark;
mod clipboard;
mod clock;
mod config;
mod emu;
mod endpoint;
mod git;
mod grant;
mod jukebox;
mod latency;
mod paths;
mod perf;
mod presets;
mod process;
mod runtime;
mod sandbox;
mod session;
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

use anyhow::Result;
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
    let cfg = Config::load_records(&cfg_path).await?;
    runtime::validate_runtime_name()?;
    session::validate_config(&cfg)?;
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
    let m = Manager::new(cfg, cfg_path).await?;

    // Warm Git caches before publishing the endpoint so initial status reads are incremental.
    git::warm_projects(git_dirs).await;

    // Background maintenance and media services share the manager.
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
    let usage = usage::spawn(m.clone());

    // Monitor the jukebox. The mod selects what to play.
    let audio = audio::spawn(m.clone());

    // Publish discovery information only after the listener is bound.
    let app = api::router(m.clone())
        .layer(middleware::from_fn_with_state(m.clone(), auth))
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
    State(m): State<Arc<Manager>>,
    mut req: Request,
    next: Next,
) -> Result<impl IntoResponse, StatusCode> {
    let presented = api::presented_token(req.headers());
    match m.resolve_cap(presented.as_deref()).await {
        Some(cap) => {
            req.extensions_mut().insert(cap);
            Ok(next.run(req).await)
        }
        None => Err(StatusCode::UNAUTHORIZED),
    }
}

#[cfg(test)]
#[path = "main_tests.rs"]
mod tests;
