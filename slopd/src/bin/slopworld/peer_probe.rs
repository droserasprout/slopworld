//! Bounded authenticated peer discovery and URL validation for the mod's HTTP transport.
use super::store::Peer;
use prost::Message;
use std::io::Read;
use std::time::Duration;

#[path = "../../shared/protocol.rs"]
mod protocol;
#[expect(
    dead_code,
    reason = "wire messages are shared with other protocol consumers"
)]
pub(super) mod wire {
    include!(concat!(env!("OUT_DIR"), "/slopworld.rs"));
}

pub(super) fn normalize_url(value: &str) -> Result<String, String> {
    let url = url::Url::parse(value)
        .map_err(|_error| "expected http://HOST[:PORT] or https://HOST[:PORT]".to_owned())?;
    if !matches!(url.scheme(), "http" | "https")
        || url.host_str().is_none()
        || url.port_or_known_default().is_none()
        || !url.username().is_empty()
        || url.password().is_some()
        || url.query().is_some()
        || url.fragment().is_some()
        || url.path() != "/"
    {
        return Err(
            "expected http://HOST[:PORT] or https://HOST[:PORT] without credentials, a path, query, or fragment".into(),
        );
    }
    Ok(url.as_str().trim_end_matches('/').to_owned())
}

/// Authentication failures permit one prompt; transport/protocol failures never ask for a key.
pub(super) fn verify(peer: &Peer) -> Result<bool, String> {
    let mut response = ureq::get(&format!("{}{}", peer.url, protocol::routes::HEALTH))
        .config()
        .max_redirects(0)
        .http_status_as_error(false)
        .timeout_global(Some(Duration::from_secs(10)))
        .build()
        .header(protocol::TOKEN_HEADER, &peer.token)
        .header(protocol::SESSION_HEADER, protocol::HOST_IDENTITY)
        .call()
        .map_err(|_error| "could not contact peer".to_owned())?;
    match response.status().as_u16() {
        401 | 403 => return Ok(false),
        200 => {}
        status => return Err(format!("peer health check returned HTTP {status}")),
    }
    if response
        .headers()
        .get("content-type")
        .and_then(|value| value.to_str().ok())
        != Some("application/x-protobuf")
    {
        return Err("peer did not return SlopWorld Protobuf".into());
    }
    let mut bytes = Vec::new();
    response
        .body_mut()
        .as_reader()
        .take(65537)
        .read_to_end(&mut bytes)
        .map_err(|_error| "reading peer health response".to_owned())?;
    if bytes.len() > 65536 {
        return Err("peer health response exceeds limit".into());
    }
    let health = wire::Health::decode(bytes.as_slice())
        .map_err(|_error| "invalid peer health response".to_owned())?;
    if !health.ok || health.protocol_version != 2 {
        return Err("peer does not support SlopWorld protocol 2".into());
    }
    Ok(true)
}
