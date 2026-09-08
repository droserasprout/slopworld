mod activity;
mod api;
mod audio;
mod clipboard;
mod config;
mod emu;
mod endpoint;
mod git;
mod grant;
mod jukebox;
mod manifest;
mod paths;
mod presets;
mod runtime;
mod sandbox;
mod session;
mod tasks;
mod title;
mod tmux;
mod usage;
#[cfg(test)]
mod version;

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
            let mut tick =
                tokio::time::interval(Duration::from_millis(crate::config::STATE_TICK_MS));
            tick.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Delay);
            loop {
                tick.tick().await;
                m.retick().await;
            }
        })
    };

    // It sleeps rather than exits when the setting is off, so turning it back on
    // needs no restart.
    let usage = usage::spawn(m.clone());

    // Watches the jukebox rather than driving it: what to play is the mod's to say.
    let audio = audio::spawn(m.clone());

    let app = api::router(m.clone()).layer(middleware::from_fn_with_state(m.clone(), auth));

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
mod tests {
    use super::*;
    use crate::config::{Daemon, SessionCfg};
    use crate::grant::Level;
    use axum::body::Body;
    use axum::http::Request;
    use futures::{SinkExt, StreamExt};
    use tokio_tungstenite::tungstenite::client::IntoClientRequest;
    use tower::ServiceExt;

    fn manager() -> Arc<Manager> {
        crate::session::test_manager(Config {
            daemon: Daemon {
                token: "root-secret".into(),
                ..Default::default()
            },
            sessions: vec![
                SessionCfg {
                    name: "grantor".into(),
                    ..Default::default()
                },
                SessionCfg {
                    name: "target".into(),
                    ..Default::default()
                },
            ],
            ..Default::default()
        })
    }

    fn app(manager: Arc<Manager>) -> axum::Router {
        api::router(manager.clone()).layer(middleware::from_fn_with_state(manager, auth))
    }

    async fn get(app: &axum::Router, path: &str, token: Option<&str>) -> StatusCode {
        let mut request = Request::builder().uri(path).body(Body::empty()).unwrap();
        if let Some(token) = token {
            request
                .headers_mut()
                .insert("x-slop-token", token.parse().unwrap());
        }
        app.clone().oneshot(request).await.unwrap().status()
    }

    #[tokio::test]
    async fn api_auth_requires_the_configured_root_token() {
        let app = app(manager());

        assert_eq!(
            get(&app, "/api/health", None).await,
            StatusCode::UNAUTHORIZED
        );
        assert_eq!(
            get(&app, "/api/health", Some("wrong")).await,
            StatusCode::UNAUTHORIZED
        );
        assert_eq!(
            get(&app, "/api/health", Some("root-secret")).await,
            StatusCode::OK
        );
    }

    #[tokio::test]
    async fn scoped_tokens_reach_shared_routes_but_not_root_routes() {
        let manager = manager();
        let scoped = manager
            .mint_grant("grantor".into(), vec!["target".into()], Level::Ro)
            .await
            .unwrap();
        let app = app(manager);

        assert_eq!(
            get(&app, "/api/health", Some(&scoped)).await,
            StatusCode::OK
        );
        assert_eq!(
            get(&app, "/api/usage", Some(&scoped)).await,
            StatusCode::FORBIDDEN
        );
        assert_eq!(
            get(&app, "/api/usage", Some("root-secret")).await,
            StatusCode::OK
        );
    }

    #[tokio::test]
    async fn revoking_a_grant_closes_an_existing_websocket_before_read_or_write() {
        let manager = manager();
        manager.sync_from_config().await;
        let scoped = manager
            .mint_grant("grantor".into(), vec!["target".into()], Level::Rw)
            .await
            .unwrap();
        let app = app(manager.clone());
        let revoke_app = app.clone();
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let address = listener.local_addr().unwrap();
        let server = tokio::spawn(async move {
            axum::serve(listener, app).await.unwrap();
        });

        let mut request = format!("ws://{address}/ws").into_client_request().unwrap();
        request
            .headers_mut()
            .insert("x-slop-token", scoped.parse().unwrap());
        let (mut socket, _) = tokio_tungstenite::connect_async(request).await.unwrap();

        let initial = tokio::time::timeout(std::time::Duration::from_secs(1), socket.next())
            .await
            .expect("websocket initial snapshot timed out")
            .expect("websocket closed before its initial snapshot")
            .expect("websocket initial snapshot failed");
        assert!(
            matches!(initial, tokio_tungstenite::tungstenite::Message::Text(text) if text.contains("target"))
        );

        socket
            .send(tokio_tungstenite::tungstenite::Message::Text(
                r#"{"t":"sub","name":"target"}"#.into(),
            ))
            .await
            .unwrap();
        socket
            .send(tokio_tungstenite::tungstenite::Message::Text(
                r#"{"t":"keys","name":"target","keys":["Enter"]}"#.into(),
            ))
            .await
            .unwrap();

        let revoke = Request::builder()
            .method("DELETE")
            .uri("/api/grants/grantor")
            .header("x-slop-token", "root-secret")
            .body(Body::empty())
            .unwrap();
        assert_eq!(
            revoke_app.oneshot(revoke).await.unwrap().status(),
            StatusCode::OK
        );

        let terminal = tokio::time::timeout(std::time::Duration::from_secs(1), socket.next())
            .await
            .expect("revoked websocket stayed open")
            .expect("revoked websocket did not terminate");
        assert!(matches!(
            terminal,
            Err(_) | Ok(tokio_tungstenite::tungstenite::Message::Close(_))
        ));
        assert!(socket
            .send(tokio_tungstenite::tungstenite::Message::Text(
                r#"{"t":"sub","name":"target"}"#.into(),
            ))
            .await
            .is_err());
        assert!(socket
            .send(tokio_tungstenite::tungstenite::Message::Text(
                r#"{"t":"keys","name":"target","keys":["Enter"]}"#.into(),
            ))
            .await
            .is_err());

        server.abort();
    }
}
