//! Coordinate client peer registration, selection, and secret-free listing.
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

const CONNECT_USAGE: &str = "usage: slopworld [--sidecar] connect NAME [http://HOST:PORT]\n";

pub(super) fn try_run(args: &[String], mode: Mode) -> Result<bool, String> {
    match args.first().map(String::as_str) {
        Some("connect") => {
            if matches!(args.get(1).map(String::as_str), Some("--help" | "-h")) {
                print!("{CONNECT_USAGE}");
                return Ok(true);
            }
            let (name, url) = match args {
                [_, name] => (name.as_str(), None),
                [_, name, url] => (name.as_str(), Some(url.as_str())),
                _ => return Err(CONNECT_USAGE.trim().into()),
            };
            validate_endpoint_name(name)?;
            connect(&mode.config_dir(), name, url, prompt::read_key)?;
            let launch = if mode == Mode::Sidecar {
                "slopworld --sidecar"
            } else {
                "slopworld"
            };
            println!("Selected peer {name}. Run `{launch}` to launch its profile.");
            Ok(true)
        }
        Some("peers") => {
            if args.len() == 2 && matches!(args.get(1).map(String::as_str), Some("--help" | "-h")) {
                println!("usage: slopworld [--sidecar] peers");
                return Ok(true);
            }
            if args.len() != 1 {
                return Err("usage: slopworld [--sidecar] peers".into());
            }
            let listing = list(&mode.config_dir())?;
            print!("{listing}");
            Ok(true)
        }
        _ => Ok(false),
    }
}

fn connect(
    config: &Path,
    name: &str,
    url: Option<&str>,
    mut key: impl FnMut() -> Result<String, String>,
) -> Result<(), String> {
    validate_endpoint_name(name)?;
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
            return Err("authentication failed; peer and selection were not saved".into());
        }
    }
    store::save(config, name, &peer)
}

fn list(config: &Path) -> Result<String, String> {
    let selected = super::application_paths::selected_endpoint_in(&config.join("client.toml"))?;
    let entries = match fs::read_dir(config.join("endpoints")) {
        Ok(entries) => entries,
        Err(error) if error.kind() == io::ErrorKind::NotFound => {
            return Ok("No saved peers. Use `slopworld connect NAME URL`.\n".into());
        }
        Err(error) => return Err(format!("listing peers: {error}")),
    };
    let mut paths = entries
        .map(|entry| entry.map(|entry| entry.path()))
        .collect::<io::Result<Vec<_>>>()
        .map_err(|error| format!("listing peers: {error}"))?;
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
        output.push_str(&format!(
            "{} {name}\t{url}\n",
            if name == selected { "*" } else { " " }
        ));
    }
    if output.is_empty() {
        output.push_str("No saved peers. Use `slopworld connect NAME URL`.\n");
    }
    Ok(output)
}

#[cfg(test)]
#[path = "peers_tests.rs"]
mod tests;
