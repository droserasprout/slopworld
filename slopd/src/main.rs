mod api;
mod config;
mod emu;
mod sandbox;
mod session;
mod tmux;

use std::sync::Arc;
use std::time::Duration;

use anyhow::Result;
use axum::extract::{Request, State};
use axum::http::StatusCode;
use axum::middleware::{self, Next};
use axum::response::IntoResponse;

use crate::config::Config;
use crate::session::Manager;

#[tokio::main]
async fn main() -> Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_env("SLOPD_LOG")
                .unwrap_or_else(|_| "slopd=info".into()),
        )
        .init();

    let cfg_path = std::env::var("SLOPD_CONFIG")
        .map(std::path::PathBuf::from)
        .unwrap_or_else(|_| Config::path());
    let cfg = Config::load(&cfg_path)?;
    tracing::info!("config: {}", cfg_path.display());

    let bind = cfg.daemon.bind.clone();
    let poll_ms = cfg.daemon.poll_ms.max(20);
    let m = Manager::new(cfg, cfg_path).await;

    let poller = {
        let m: Arc<Manager> = m.clone();
        tokio::spawn(async move {
            let mut tick = tokio::time::interval(Duration::from_millis(poll_ms));
            tick.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Delay);
            loop {
                tick.tick().await;
                m.retick().await;
            }
        })
    };

    let app = api::router(m.clone()).layer(middleware::from_fn_with_state(m.clone(), auth));

    let listener = tokio::net::TcpListener::bind(&bind).await?;
    tracing::info!("listening on http://{bind}");

    axum::serve(listener, app)
        .with_graceful_shutdown(async {
            let _ = tokio::signal::ctrl_c().await;
            tracing::info!("shutting down (tmux sessions keep running)");
        })
        .await?;

    poller.abort();
    Ok(())
}

/// The /ws route re-checks the token itself, because browsers and the mod's
/// hand-rolled client both send it as a header on the upgrade request only.
async fn auth(
    State(m): State<Arc<Manager>>,
    req: Request,
    next: Next,
) -> Result<impl IntoResponse, StatusCode> {
    let token = m.config().await.daemon.token;
    if api::token_ok(req.headers(), &token) {
        Ok(next.run(req).await)
    } else {
        Err(StatusCode::UNAUTHORIZED)
    }
}
