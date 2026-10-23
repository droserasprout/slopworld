//! Atomic private remote peer persistence; registration never changes client selection.
use serde::{Deserialize, Serialize};
use std::fs;
use std::io::{self, Write};
use std::path::Path;

#[derive(Serialize, Deserialize)]
pub(super) struct Peer {
    pub(super) url: String,
    #[serde(default)]
    pub(super) token: String,
}

pub(super) fn load(path: &Path) -> Result<Peer, String> {
    let text =
        fs::read_to_string(path).map_err(|error| format!("reading {}: {error}", path.display()))?;
    // TOML errors can include the source line, which may contain a credential.
    toml::from_str(&text).map_err(|_error| format!("invalid peer descriptor: {}", path.display()))
}

pub(super) fn save(config: &Path, name: &str, peer: &Peer) -> Result<(), String> {
    if name == "local" {
        return Err("local is owned by the daemon".into());
    }
    let path = config.join("endpoints").join(format!("{name}.toml"));
    let descriptor =
        toml::to_string(peer).map_err(|_error| "encoding peer descriptor".to_owned())?;
    save_private(&path, &descriptor)
}

pub(super) fn save_private(path: &Path, text: &str) -> Result<(), String> {
    let parent = path
        .parent()
        .ok_or_else(|| "client file needs a parent directory".to_owned())?;
    fs::create_dir_all(parent)
        .map_err(|error| format!("creating {}: {error}", parent.display()))?;
    let temporary = parent.join(format!(".peer-{}.tmp", uuid::Uuid::new_v4()));
    let mut options = fs::OpenOptions::new();
    options.write(true).create_new(true);
    #[cfg(unix)]
    {
        use std::os::unix::fs::OpenOptionsExt;
        options.mode(0o600);
    }
    let mut file = options
        .open(&temporary)
        .map_err(|error| format!("creating client file: {error}"))?;
    let result = (|| -> io::Result<()> {
        file.write_all(text.as_bytes())?;
        file.sync_all()?;
        drop(file);
        fs::rename(&temporary, path)
    })();
    if result.is_err() {
        drop(fs::remove_file(&temporary));
    }
    result.map_err(|error| format!("saving {}: {error}", path.display()))
}
