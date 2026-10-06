//! Reuse endpoint exclusion before recovery or offline migration can mutate stores.
//! Pending settings edits may change bind; reserve both the current and undo addresses.
use super::target::StorageBinding;
use anyhow::{Context, Result};
use std::collections::BTreeMap;
use tokio::net::TcpListener;

pub(crate) async fn reserve(binding: &StorageBinding) -> Result<BTreeMap<String, TcpListener>> {
    let mut recovered = Vec::new();
    if let Some(text) = super::transaction::recovery_settings(binding).await? {
        recovered.push(text);
    }
    // TODO(remove after user tests and approves workspace store migration):
    // priv/notes/plan-storage-main.md. Old transactions can also change the bind.
    if let Some(text) = crate::config::legacy::recovery_settings(&binding.settings).await? {
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
    let mut addresses = addresses
        .into_iter()
        .map(|text| {
            Ok((
                text.parse::<std::net::SocketAddr>()
                    .context("invalid daemon bind address")?,
                text,
            ))
        })
        .collect::<Result<Vec<_>>>()?;
    // A wildcard reserves its narrower addresses too. Bind it first so an undo
    // from a wildcard to a specific address does not conflict with our own socket.
    addresses.sort_by_key(|(address, _)| (address.port(), !address.ip().is_unspecified()));
    let mut listeners = BTreeMap::new();
    for (_, address) in addresses {
        if covers(&listeners, &address)? {
            continue;
        }
        let listener = TcpListener::bind(&address).await.with_context(|| {
            format!("reserving daemon endpoint {address}; stop the daemon before migration")
        })?;
        listeners.insert(address, listener);
    }
    Ok(listeners)
}

fn covers(listeners: &BTreeMap<String, TcpListener>, requested: &str) -> Result<bool> {
    let requested: std::net::SocketAddr = requested.parse()?;
    for listener in listeners.values() {
        let bound = listener.local_addr()?;
        if bound.port() == requested.port()
            && (bound.ip() == requested.ip()
                || (bound.ip().is_unspecified() && bound.is_ipv4() == requested.is_ipv4()))
        {
            return Ok(true);
        }
    }
    Ok(false)
}

pub(crate) async fn serving_listener(
    mut listeners: BTreeMap<String, TcpListener>,
    address: &str,
) -> Result<TcpListener> {
    if let Some(listener) = listeners.remove(address) {
        return Ok(listener);
    }
    anyhow::ensure!(
        covers(&listeners, address)?,
        "recovered daemon endpoint was not reserved"
    );
    // Recovery may narrow a wildcard address. Rebind before starting any runtime
    // services; never serve on a broader address than the recovered settings.
    drop(listeners);
    Ok(TcpListener::bind(address).await?)
}
