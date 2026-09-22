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

    // The first tracked-file scan of a large project is unavoidable, but it should not happen
    // while the player is waiting for the Git sidebar. Seed Git's filesystem and untracked
    // caches before publishing the daemon endpoint; the next status request is incremental.
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

    // It sleeps rather than exits when the setting is off, so turning it back on
    // needs no restart.
    let usage = usage::spawn(m.clone());

    // Watches the jukebox rather than driving it: what to play is the mod's to say.
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

/// SIGTERM is the case that matters: `make install-daemon` restarts the unit under a live
/// game, and without this sockets died mid-frame instead of closing.
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

/// Resolves the request's token to a capability and hangs it on the request, so a handler can
/// ask what this caller may touch without reading the header itself. Root or a minted grant
/// passes; anything else is 401 here and never reaches a handler. The /ws route re-resolves,
/// because the mod sends the header on the upgrade request only.
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
