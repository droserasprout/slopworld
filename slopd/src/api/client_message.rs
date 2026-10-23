use super::types::*;
use crate::shared::wire;
use prost::Message;
pub(super) fn decode(bytes: &[u8]) -> anyhow::Result<ClientMsg> {
    use wire::client_message::Payload as P;
    let message = wire::ClientMessage::decode(bytes)?;
    fn convert<T: serde::de::DeserializeOwned>(value: impl serde::Serialize) -> anyhow::Result<T> {
        Ok(serde_json::from_value(serde_json::to_value(value)?)?)
    }
    Ok(
        match message
            .payload
            .ok_or_else(|| anyhow::anyhow!("missing command"))?
        {
            P::Redraw(v) => ClientMsg::Redraw {
                cols: v.cols.map(u16::try_from).transpose()?,
                rows: v.rows.map(u16::try_from).transpose()?,
            },
            P::Sub(v) => ClientMsg::Sub {
                name: v.name.ok_or_else(|| anyhow::anyhow!("missing name"))?,
            },
            P::Unsub(v) => ClientMsg::Unsub {
                name: v.name.ok_or_else(|| anyhow::anyhow!("missing name"))?,
            },
            P::Keys(v) => ClientMsg::Keys(convert(v)?),
            P::Resize(v) => ClientMsg::Resize(convert(v)?),
            P::Scroll(v) => ClientMsg::Scroll(convert(v)?),
            P::Mouse(v) => ClientMsg::Mouse(convert(v)?),
            // Move image payloads directly; a JSON projection expands every byte into a value.
            P::Paste(v) => ClientMsg::Paste(PasteReq {
                name: v.name.ok_or_else(|| anyhow::anyhow!("missing name"))?,
                text: v.text.unwrap_or_default(),
                trace_id: v.trace_id.unwrap_or_default(),
                run_id: v.run_id,
                image_data: v.image_data,
            }),
            P::Breadcrumb(v) => ClientMsg::Breadcrumb(convert(v)?),
            P::Audio(v) => ClientMsg::Audio(AudioReq {
                volume: v.volume,
                selection: match v.change {
                    None => None,
                    Some(wire::audio_request::Change::Stop(_)) => Some(None),
                    Some(wire::audio_request::Change::Selection(s)) => Some(Some(convert(s)?)),
                },
            }),
        },
    )
}

#[cfg(test)]
#[path = "client_message_tests.rs"]
mod tests;
