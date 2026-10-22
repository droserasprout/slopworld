//! Private peer descriptors and atomic client-selection persistence.
//! Publish credentials before selection so interrupted writes cannot select an absent peer.
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
    let path = config.join("endpoints").join(format!("{name}.toml"));
    // Read client settings before committing anything, retaining unrelated client fields.
    let settings_path = config.join("client.toml");
    let mut settings = match fs::read_to_string(&settings_path) {
        Ok(text) => toml::from_str::<toml::Table>(&text)
            .map_err(|_error| format!("invalid client settings: {}", settings_path.display()))?,
        Err(error) if error.kind() == io::ErrorKind::NotFound && !settings_path.is_symlink() => {
            toml::Table::new()
        }
        Err(error) => return Err(format!("reading {}: {error}", settings_path.display())),
    };
    settings.insert("endpoint".into(), toml::Value::String(name.into()));
    let descriptor =
        toml::to_string(peer).map_err(|_error| "encoding peer descriptor".to_owned())?;
    let selection =
        toml::to_string(&settings).map_err(|_error| "encoding client settings".to_owned())?;
    // Publish the descriptor first: interruption can leave an unselected valid peer,
    // but never selects a missing or partial credential file.
    save_private(&path, &descriptor)?;
    save_private(&settings_path, &selection)
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
