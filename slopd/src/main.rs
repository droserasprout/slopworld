mod activity;
mod api;
mod audio;
mod benchmark;
mod clipboard;
mod config;
mod emu;
mod endpoint;
mod git;
mod grant;
mod jukebox;
mod paths;
mod perf;
mod presets;
mod process;
mod runtime;
mod sandbox;
mod session;
mod shared;
mod tasks;
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
    if std::env::args().any(|arg| arg == "--perf-bench") {
        return benchmark::run();
    }

    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_env("SLOPD_LOG")
                .unwrap_or_else(|_| "slopd=info".into()),
        )
        .init();

    let cfg_path = Config::path_in_use();
    let cfg = Config::load(&cfg_path).await?;
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
    let m = Manager::new(cfg, cfg_path).await;

    // Scan tracked files before the player opens the Git sidebar.
    // Initialize Git's filesystem and untracked caches before publishing the daemon endpoint.
    // The next status request can use these caches for an incremental scan.
    git::warm_projects(git_dirs).await;

    let poller = {
        let m: Arc<Manager> = m.clone();
        tokio::spawn(async move {
            loop {
                let delay = m.maintenance_delay().await;
                let wake = m.maintenance_wake();
                tokio::select! {
                    _ = tokio::time::sleep(delay) => m.retick().await,
                    _ = wake.notified() => {}
                }
            }
        })
    };

    // The usage task waits while polling is disabled. Users can enable polling without restarting the daemon.
    let usage = usage::spawn(m.clone());

    // Monitor the jukebox. The mod selects what to play.
    let audio = audio::spawn(m.clone());

    let app = api::router(m.clone())
        .layer(middleware::from_fn_with_state(m.clone(), auth))
        .layer(middleware::from_fn(api::protobuf::normalize_errors));

    let listener = tokio::net::TcpListener::bind(&bind).await?;
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

/// Handle SIGTERM so daemon installation can restart the unit while the game runs.
/// Close sockets cleanly to avoid interrupting a frame.
async fn shutdown() {
    use tokio::signal::unix::{signal, SignalKind};
    let mut term = match signal(SignalKind::terminate()) {
        Ok(s) => s,
        Err(e) => {
            tracing::warn!("cannot listen for SIGTERM: {e:#}");
            let _ = tokio::signal::ctrl_c().await;
            return;
        }
    };
    tokio::select! {
        _ = tokio::signal::ctrl_c() => {}
        _ = term.recv() => {}
    }
}

/// Resolve the request token to a capability and attach it to the request.
/// Handlers use this capability to check access without reading the header.
/// Permit root credentials and grants. Return 401 for other credentials before calling a handler.
/// The /ws route resolves credentials again because the mod sends the header only with the upgrade request.
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
