//! Daemon event wire projection and shared Protobuf encoding.
use crate::application::{Event, EventMessage};
use crate::session::ScreenView;
use crate::shared::wire;
use prost::Message;
use std::sync::Arc;

impl EventMessage {
    pub(crate) fn encoded(&self) -> Result<Arc<[u8]>, String> {
        self.encode_with(|event| {
            let _perf = crate::perf::timer("websocket-serialize");
            event
                .to_protobuf()
                .map(|event| Arc::from(event.encode_to_vec()))
                .map_err(|error| error.to_string())
        })
    }
}

fn project<T: serde::de::DeserializeOwned>(value: impl serde::Serialize) -> anyhow::Result<T> {
    Ok(serde_json::from_value(serde_json::to_value(value)?)?)
}
impl Event {
    pub(crate) fn to_protobuf(&self) -> anyhow::Result<wire::Event> {
        use wire::event::Payload;
        Ok(wire::Event {
            payload: Some(match self {
                Self::Screen { screen } => Payload::Screen(screen.into()),
                Self::Capabilities { capabilities } => {
                    Payload::Capabilities(project(capabilities)?)
                }
                Self::Sessions { sessions } => Payload::Sessions(wire::SessionsReply {
                    sessions: project(sessions)?,
                }),
                Self::Projects { projects } => Payload::Projects(wire::ProjectsReply {
                    projects: project(projects)?,
                }),
                Self::Library { library } => Payload::Library(wire::LibraryReply {
                    library: project(library)?,
                }),
                Self::Usage { usage } => Payload::Usage(project(usage)?),
                Self::Audio { audio } => Payload::Audio(project(audio)?),
                Self::Jukebox { jukebox } => Payload::Jukebox(project(jukebox)?),
            }),
        })
    }
}

#[cfg(test)]
#[path = "events_tests.rs"]
mod tests;

impl From<&ScreenView> for wire::ScreenView {
    fn from(s: &ScreenView) -> Self {
        Self {
            input_timings: s.input_timings.clone(),
            name: s.name.clone(),
            seq: s.seq,
            cols: Some(s.cols.into()),
            rows: Some(s.rows.into()),
            cx: s.cx.into(),
            cy: s.cy.into(),
            off: s.off,
            history: Some(s.history),
            cursor_shape: s.cursor_shape.into(),
            cursor_blink: Some(s.cursor_blink),
            app_mouse: s.app_mouse,
            app_drag: s.app_drag,
            alt_screen: s.alt_screen,
            title: s.title.clone(),
            request_id: s.request_id,
            lines: s.lines.iter().map(|s| s.to_string()).collect(),
        }
    }
}
