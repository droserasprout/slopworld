//! Socket command authorization and dispatch into manager operations.
//! The connection owns scroll admission; manager services own session and playback policy.

use crate::grant::{Cap, Level};
use crate::session::{Event, EventMessage};
use anyhow::bail;

use super::super::types::{
    AudioReq, AudioSelection, BreadcrumbReq, ClientMsg, KeysReq, MouseReq, PasteReq, ResizeReq,
};
use super::super::Mgr;
use super::{send, WsSubs, WsTx};

/// Handle one client message. Ignore unauthorized messages without a response.
/// A failed direct response indicates a closed socket and tells the caller to stop reading.
pub(super) async fn handle_client_msg(
    cm: ClientMsg,
    m: &Mgr,
    cap: &Cap,
    tx: &WsTx,
    subs: &WsSubs,
) -> bool {
    if let ClientMsg::Sub { name } = cm {
        return handle_sub(name, m, cap, tx, subs).await;
    }
    let trace = match &cm {
        ClientMsg::Keys(r) => crate::latency::InputTrace::begin(&r.trace_id),
        ClientMsg::Mouse(r) => crate::latency::InputTrace::begin(&r.trace_id),
        ClientMsg::Paste(r) => crate::latency::InputTrace::begin(&r.trace_id),
        _ => None,
    };
    let shared = matches!(
        &cm,
        ClientMsg::Keys(_)
            | ClientMsg::Resize(_)
            | ClientMsg::Mouse(_)
            | ClientMsg::Paste(_)
            | ClientMsg::Breadcrumb(_)
    );
    let operation = async {
        match cm {
            ClientMsg::Redraw { cols, rows } => handle_redraw(m, cap, cols.zip(rows)),
            ClientMsg::Sub { .. } => unreachable!(),
            ClientMsg::Unsub { name } => handle_unsub(name, subs).await,
            ClientMsg::Keys(req) => handle_keys(req, m, cap).await,
            ClientMsg::Resize(req) => handle_resize(req, m, cap).await,
            // Scroll requests are intercepted by ws_run so capture work can run in its bounded
            // lane without stopping command intake.
            ClientMsg::Scroll(_) => {}
            ClientMsg::Mouse(req) => handle_mouse(req, m, cap).await,
            ClientMsg::Paste(req) => handle_paste(req, m, cap).await,
            ClientMsg::Breadcrumb(req) => handle_breadcrumb(req, m, cap).await,
            ClientMsg::Audio(req) => {
                if let Some(audio) = handle_audio(req, m, cap).await {
                    // The game may already be closing its read side after sending Stop.
                    // A failed audio reply must not prevent draining that queued command.
                    m.emit(Event::Audio { audio });
                }
            }
        }
        true
    };
    if shared {
        if let Some(trace) = trace {
            m.session_read_operation(crate::latency::CURRENT.scope(Some(trace), operation))
                .await
        } else {
            m.session_read_operation(operation).await
        }
    } else {
        m.session_operation(operation).await
    }
}

fn handle_redraw(m: &Mgr, cap: &Cap, shape: Option<(u16, u16)>) {
    // A panel refresh reaches every live tmux session, so it is deliberately root-only. A
    // scoped grant must never be able to cause work or visible flicker in sessions it cannot
    // see.
    if cap.may_create() {
        m.request_redraw(shape);
    }
}

async fn handle_sub(name: String, m: &Mgr, cap: &Cap, tx: &WsTx, subs: &WsSubs) -> bool {
    let screen = m
        .session_read_operation(async {
            if !m.cap_ok(cap, &name, Level::Ro).await {
                return None;
            }
            subs.lock().await.insert(name.clone(), m.watching(&name));
            m.clear_bell(&name).await;
            m.screen(&name).await
        })
        .await;
    // Socket backpressure must not hold the session boundary.
    if let Some(s) = screen {
        return send(tx, cap, &EventMessage::new(Event::Screen { screen: s }))
            .await
            .is_ok();
    }
    true
}

async fn handle_unsub(name: String, subs: &WsSubs) {
    subs.lock().await.remove(&name);
}

async fn handle_keys(req: KeysReq, m: &Mgr, cap: &Cap) {
    if !m.cap_ok(cap, &req.name, Level::Rw).await {
        return;
    }
    m.send_keys(&req.name, req.keys, req.literal, req.random_tips)
        .await;
}

async fn handle_resize(req: ResizeReq, m: &Mgr, cap: &Cap) {
    if !m.cap_ok(cap, &req.name, Level::Rw).await {
        return;
    }
    if let Err(e) = m.resize(&req.name, req.cols, req.rows).await {
        tracing::debug!("resize: {e:#}");
    }
}

async fn handle_mouse(req: MouseReq, m: &Mgr, cap: &Cap) {
    if !m.cap_ok(cap, &req.name, Level::Rw).await {
        return;
    }
    let Some(action) = crate::emu::MouseAction::parse(&req.action) else {
        tracing::debug!("unknown mouse action: {}", req.action);
        return;
    };
    let ev = crate::emu::MouseInput {
        action,
        button: req.button,
        col: req.col,
        row: req.row,
    };
    m.send_mouse(&req.name, ev, req.count).await;
}

async fn handle_paste(req: PasteReq, m: &Mgr, cap: &Cap) {
    if !m.cap_ok(cap, &req.name, Level::Rw).await {
        return;
    }
    // Authorization is already complete. Report delivery errors so failed pastes are visible in the log.
    if let Err(e) = m.paste(&req.name, &req.text).await {
        tracing::warn!("paste to {}: {e:#}", req.name);
    }
}

async fn handle_breadcrumb(req: BreadcrumbReq, m: &Mgr, cap: &Cap) {
    if !m.cap_ok(cap, &req.name, Level::Rw).await {
        return;
    }
    if let Err(e) = m
        .paste_breadcrumb(&req.name, &req.breadcrumb, req.random_tips)
        .await
    {
        tracing::warn!("breadcrumb to {}: {e:#}", req.name);
    }
}

pub(super) async fn handle_audio(
    req: AudioReq,
    m: &Mgr,
    cap: &Cap,
) -> Option<crate::audio::AudioState> {
    if !cap.may_create() {
        return None;
    }
    let spotify = req
        .selection
        .as_ref()
        .and_then(Option::as_ref)
        .is_some_and(|s| s.ncspot);
    let _transition = m.music.transition.lock().await;
    let result = match req.selection {
        Some(Some(selection)) if selection.ncspot => {
            if selection.station.is_some() || selection.stream.is_some() || selection.file.is_some()
            {
                Err(anyhow::anyhow!(
                    "Select ncspot or another audio source, not both."
                ))
            } else {
                m.open_ncspot(None, None, Some(req.volume))
                    .await
                    .map(|_| ())
            }
        }
        Some(selection) => match m.stop_ncspot().await {
            Err(e) => Err(e),
            Ok(()) => match selection {
                Some(s) => match resolve_audio_source(s) {
                    Ok(source) => {
                        m.music.audio.play(&source, req.volume);
                        Ok(())
                    }
                    Err(e) => Err(e),
                },
                None => {
                    m.music.audio.stop();
                    Ok(())
                }
            },
        },
        None => m.music_volume(req.volume).await,
    };
    if let Err(e) = result {
        let error = format!("{e:#}");
        m.music.audio.reject(error.clone());
        if spotify {
            return Some(crate::audio::AudioState {
                source: Some("ncspot".into()),
                error: Some(error),
                ..Default::default()
            });
        }
    }
    // Reply even when the player was already open and its broadcast state did not change.
    // Launch and shutdown now use one ordered socket. No late HTTP launch can undo a stop.
    if spotify {
        Some(m.music_state().await)
    } else {
        None
    }
}

pub(super) fn resolve_audio_source(selection: AudioSelection) -> anyhow::Result<String> {
    anyhow::ensure!(
        !selection.ncspot,
        "The daemon does not decode audio from ncspot."
    );
    match (selection.station, selection.stream, selection.file) {
        (Some(station), Some(stream), None) => crate::jukebox::catalog()
            .resolve(&station, &stream)
            .map_err(|e| anyhow::anyhow!("jukebox selection rejected: {e:#}")),
        (None, None, Some(file)) => Ok(file),
        _ => bail!("Select a station and stream, or select a file."),
    }
}
