//! Socket command authorization and dispatch into manager operations.
//! The connection owns scroll admission; manager services own session and playback policy.

use crate::grant::{Cap, Level};
use crate::session::{Event, EventMessage};

use super::super::Mgr;
use super::super::types::{
    AudioReq, BreadcrumbReq, ClientMsg, KeysReq, MouseReq, PasteReq, ResizeReq,
};
use super::{WsSubs, WsTx, send};

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
    // These commands only update connection state or schedule background work.
    // They must not block intake behind an unrelated lifecycle transaction.
    match cm {
        ClientMsg::Unsub { name } => {
            handle_unsub(name, subs).await;
            return true;
        }
        ClientMsg::Redraw { cols, rows } => {
            handle_redraw(m, cap, cols.zip(rows));
            return true;
        }
        _ => {}
    }
    let trace = match &cm {
        ClientMsg::Keys(r) => crate::latency::InputTrace::begin(&r.trace_id),
        ClientMsg::Mouse(r) => crate::latency::InputTrace::begin(&r.trace_id),
        ClientMsg::Paste(r) => crate::latency::InputTrace::begin(&r.trace_id),
        _ => None,
    };
    // Root input cannot be revoked by a worker's lifecycle. Protect its target
    // directly so unrelated session removal does not stall socket intake.
    let terminal_name = match &cm {
        ClientMsg::Keys(req) => Some(&req.name),
        ClientMsg::Mouse(req) => Some(&req.name),
        ClientMsg::Paste(req) => Some(&req.name),
        ClientMsg::Resize(req) => Some(&req.name),
        _ => None,
    };
    let terminal_guard = if cap.may_create() {
        if let Some(name) = terminal_name {
            let Some(guard) = m.terminal_input_guard(name).await else {
                return true;
            };
            Some(guard)
        } else {
            None
        }
    } else {
        None
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
            ClientMsg::Unsub { name } => handle_unsub(name, subs).await,
            ClientMsg::Keys(req) => handle_keys(req, m, cap).await,
            ClientMsg::Resize(req) => handle_resize(req, m, cap).await,
            // Scroll requests are intercepted by ws_run so capture work can run in its bounded
            // lane without stopping command intake.
            ClientMsg::Sub { .. } | ClientMsg::Scroll(_) => {}
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
    if terminal_guard.is_some() {
        if let Some(trace) = trace {
            crate::latency::CURRENT.scope(Some(trace), operation).await
        } else {
            operation.await
        }
    } else if shared {
        if let Some(trace) = trace {
            Box::pin(
                m.session_read_operation(crate::latency::CURRENT.scope(Some(trace), operation)),
            )
            .await
        } else {
            Box::pin(m.session_read_operation(operation)).await
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
    let snapshot = async {
        if !m.cap_ok(cap, &name, Level::Ro).await {
            return None;
        }
        subs.lock().await.insert(name.clone(), m.watching(&name));
        m.clear_bell(&name).await;
        m.screen(&name).await
    };
    let screen = if cap.may_create() {
        let Some(_terminal) = m.terminal_input_guard(&name).await else {
            return true;
        };
        snapshot.await
    } else {
        m.session_read_operation(snapshot).await
    };
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
    m.send_keys(&req.name, req.keys, req.literal).await;
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
    m.select_music(req).await
}
