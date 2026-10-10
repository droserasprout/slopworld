//! Persistent config seeding; existing daemon config remains owned by slopd.
use anyhow::{Context, Result, bail};
use rand::RngCore;
use std::{fs, io::Write, net::SocketAddr, path::Path};

pub(crate) fn seed(config: &Path, data: &Path, port: u16) -> Result<()> {
    for directory in [config, data] {
        let existed = directory.exists();
        fs::create_dir_all(directory)?;
        // Only secure newly created directories; existing host ownership is preserved.
        #[cfg(unix)]
        if !existed {
            use std::os::unix::fs::PermissionsExt;
            fs::set_permissions(directory, fs::Permissions::from_mode(0o700))?;
        }
    }
    let path = config.join("config.toml");
    if path.try_exists()? {
        return validate(&path, port);
    }
    let mut random = [0; 32];
    rand::rngs::OsRng
        .try_fill_bytes(&mut random)
        .context("could not generate daemon token")?;
    let token: String = random.iter().map(|byte| format!("{byte:02x}")).collect();
    // Publish a complete file without overwriting a config created concurrently.
    let mut temporary = tempfile::NamedTempFile::new_in(config)?;
    writeln!(
        temporary,
        "[daemon]\nbind = \"0.0.0.0:{port}\"\ntoken = \"{token}\""
    )?;
    temporary.as_file().sync_all()?;
    match temporary.persist_noclobber(&path) {
        Ok(_) => println!("slopcar: created {} with a random token", path.display()),
        Err(error) if error.error.kind() == std::io::ErrorKind::AlreadyExists => {
            return validate(&path, port);
        }
        Err(error) => return Err(error.error.into()),
    }
    Ok(())
}

fn validate(path: &Path, port: u16) -> Result<()> {
    if !path.is_file() {
        bail!("{} is not a regular file", path.display());
    }
    let config: toml::Value = toml::from_str(&fs::read_to_string(path)?)
        .with_context(|| format!("invalid config: {}", path.display()))?;
    if let Some(bind) = config.get("daemon").and_then(|daemon| daemon.get("bind")) {
        let bind: SocketAddr = bind
            .as_str()
            .context("daemon.bind must be a string")?
            .parse()
            .context("daemon.bind must be an IP socket address")?;
        if bind.port() != port {
            bail!(
                "{} already binds port {} but requested port is {port}; reuse that port or use a fresh SLOPCAR_CONFIG_DIR",
                path.display(),
                bind.port()
            );
        }
    }
    Ok(())
}

#[cfg(test)]
#[path = "config_tests.rs"]
mod tests;
