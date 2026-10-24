//! Coordinate remote peer registration and secret-free listing; launch flags select peers.
//! peer_probe owns transport, peer_store owns persistence, and peer_prompt owns hidden input.
use super::application_paths::{Mode, validate_endpoint_name};
use std::fs;
use std::io;
use std::path::Path;

#[path = "peer_probe.rs"]
mod probe;
#[path = "peer_prompt.rs"]
mod prompt;
#[path = "peer_store.rs"]
mod store;
use probe::{normalize_url, verify};
use store::{Peer, load};

pub(super) fn register(mode: Mode, name: &str, url: Option<&str>) -> Result<(), String> {
    connect(&mode.config_dir(), name, url, prompt::read_key)?;
    let launch = if mode == Mode::Sidecar {
        "slopworld --sidecar"
    } else {
        "slopworld"
    };
    println!("Saved peer {name}. Run `{launch} --endpoint {name}` to launch its profile.");
    Ok(())
}

pub(super) fn print_list(mode: Mode) -> Result<(), String> {
    print!("{}", list(&mode.config_dir())?);
    Ok(())
}

fn connect(
    config: &Path,
    name: &str,
    url: Option<&str>,
    mut key: impl FnMut() -> Result<String, String>,
) -> Result<(), String> {
    validate_endpoint_name(name)?;
    // The daemon publishes and rotates this descriptor; registration never owns it.
    if name == "local" {
        return Err(
            "local is owned by the daemon; use another peer name for a remote endpoint".into(),
        );
    }
    let path = config.join("endpoints").join(format!("{name}.toml"));
    let saved = match fs::symlink_metadata(&path) {
        Ok(_) => Some(load(&path)?),
        Err(error) if error.kind() == io::ErrorKind::NotFound => None,
        Err(error) => return Err(format!("checking {}: {error}", path.display())),
    };
    let url = normalize_url(
        url.or_else(|| saved.as_ref().map(|peer| peer.url.as_str()))
            .ok_or_else(|| format!("unknown peer {name}; supply its URL"))?,
    )?;
    // Never send a saved key to a changed address.
    let token = saved
        .filter(|peer| normalize_url(&peer.url).ok().as_ref() == Some(&url))
        .map(|peer| peer.token)
        .unwrap_or_default();
    let mut peer = Peer { url, token };
    if !verify(&peer)? {
        peer.token = key()?;
        if peer.token.trim().is_empty() {
            return Err("the key cannot be blank".into());
        }
        if !verify(&peer)? {
            return Err("authentication failed; peer was not saved".into());
        }
    }
    store::save(config, name, &peer)
}

fn list(config: &Path) -> Result<String, String> {
    let entries = match fs::read_dir(config.join("endpoints")) {
        Ok(entries) => entries,
        Err(error) if error.kind() == io::ErrorKind::NotFound => {
            return Ok("No saved endpoints. Use `slopworld connect NAME URL`.\n".into());
        }
        Err(error) => return Err(format!("listing endpoints: {error}")),
    };
    let mut paths = entries
        .map(|entry| entry.map(|entry| entry.path()))
        .collect::<io::Result<Vec<_>>>()
        .map_err(|error| format!("listing endpoints: {error}"))?;
    paths.sort();
    let mut output = String::new();
    for path in paths {
        if path.extension().and_then(|ext| ext.to_str()) != Some("toml") {
            continue;
        }
        let Some(name) = path.file_stem().and_then(|name| name.to_str()) else {
            continue;
        };
        if validate_endpoint_name(name).is_err() {
            continue;
        }
        let peer = load(&path)?;
        let url = normalize_url(&peer.url)?;
        output.push_str(&format!("{name}\t{url}\n"));
    }
    if output.is_empty() {
        output.push_str("No saved endpoints. Use `slopworld connect NAME URL`.\n");
    }
    Ok(output)
}

#[cfg(test)]
#[path = "endpoints_tests.rs"]
mod tests;
