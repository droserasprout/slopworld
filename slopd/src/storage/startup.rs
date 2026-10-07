//! Reuse endpoint exclusion before recovery can mutate stores.
//! Pending settings edits may change bind; reserve both the current and undo addresses.
use super::target::StorageBinding;
use anyhow::{Context, Result};
use std::collections::BTreeMap;
use std::net::SocketAddr;
use tokio::net::TcpListener;

/// Pin hostname resolution through recovery and retain all usable addresses for
/// exclusion. Aliases and narrower endpoints can share a reserved listener.
#[derive(Debug)]
pub(crate) struct Reservations {
    endpoints: BTreeMap<String, Vec<SocketAddr>>,
    listeners: BTreeMap<SocketAddr, TcpListener>,
}

pub(crate) async fn reserve(binding: &StorageBinding) -> Result<Reservations> {
    let mut recovered = Vec::new();
    if let Some(text) = super::transaction::recovery_settings(binding).await? {
        recovered.push(text);
    }
    let current = match tokio::fs::read_to_string(&binding.settings).await {
        Ok(text) => text,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => String::new(),
        Err(e) => return Err(e.into()),
    };
    let mut addresses = std::collections::BTreeSet::new();
    for text in &recovered {
        addresses.insert(toml::from_str::<crate::config::Settings>(text)?.daemon.bind);
    }
    match toml::from_str::<crate::config::Settings>(&current) {
        Ok(settings) => {
            addresses.insert(settings.daemon.bind);
        }
        Err(error) if recovered.is_empty() => return Err(error.into()),
        Err(_) => {} // An undo document will replace the malformed current root.
    }
    let mut resolved = Vec::new();
    for text in addresses {
        let addresses: Vec<_> = tokio::net::lookup_host(text.as_str())
            .await
            .with_context(|| format!("resolving daemon endpoint {text}"))?
            .collect();
        anyhow::ensure!(
            !addresses.is_empty(),
            "daemon endpoint {text} has no addresses"
        );
        resolved.push((text, addresses));
    }
    // A wildcard reserves its narrower addresses too. Bind it first so an undo
    // from a wildcard to a specific address does not conflict with our own socket.
    resolved.sort_by_key(|(_, addresses)| {
        !addresses
            .iter()
            .any(|address| address.ip().is_unspecified())
    });
    let mut reservations = Reservations {
        endpoints: BTreeMap::new(),
        listeners: BTreeMap::new(),
    };
    for (text, addresses) in resolved {
        let mut usable = Vec::new();
        let mut last_error = None;
        for address in addresses {
            if covering_listener(&reservations.listeners, address)?.is_some() {
                usable.push(address);
                continue;
            }
            match TcpListener::bind(address).await {
                Ok(listener) => {
                    reservations.listeners.insert(address, listener);
                    usable.push(address);
                }
                Err(error) if error.kind() == std::io::ErrorKind::AddrInUse => {
                    // Do not use a second DNS answer to bypass another daemon.
                    return Err(error).with_context(|| {
                        format!("reserving daemon endpoint {text} ({address}); another daemon may already be running")
                    });
                }
                Err(error) => last_error = Some(error),
            }
        }
        if usable.is_empty() {
            return Err(last_error.context("daemon endpoint has no usable addresses")?)
                .with_context(|| format!("reserving daemon endpoint {text}"));
        }
        reservations.endpoints.insert(text, usable);
    }
    Ok(reservations)
}

fn covering_listener(
    listeners: &BTreeMap<SocketAddr, TcpListener>,
    requested: SocketAddr,
) -> Result<Option<SocketAddr>> {
    for (address, listener) in listeners {
        let bound = listener.local_addr()?;
        // The map key also identifies ephemeral-port requests after binding.
        if *address == requested
            || (bound.port() == requested.port()
                && (bound.ip() == requested.ip()
                    || (bound.ip().is_unspecified() && bound.is_ipv4() == requested.is_ipv4())))
        {
            return Ok(Some(*address));
        }
    }
    Ok(None)
}

pub(crate) async fn serving_listener(
    mut reservations: Reservations,
    address: &str,
) -> Result<TcpListener> {
    let requested = *reservations
        .endpoints
        .get(address)
        .and_then(|addresses| addresses.first())
        .context("recovered daemon endpoint was not reserved")?;
    let covered = covering_listener(&reservations.listeners, requested)?
        .context("recovered daemon endpoint was not reserved")?;
    let listener = reservations
        .listeners
        .remove(&covered)
        .context("reserved listener disappeared")?;
    if listener.local_addr()?.ip() == requested.ip() {
        return Ok(listener);
    }
    // Recovery may narrow a wildcard address. Rebind before starting any runtime
    // services; never serve on a broader address than the recovered settings.
    // Use the pinned address rather than resolving the hostname again.
    drop(reservations);
    drop(listener);
    Ok(TcpListener::bind(requested).await?)
}

#[cfg(test)]
#[path = "startup_tests.rs"]
mod tests;
