//! Agent bearer credentials and revocation. Tokens identify one session; current policy
//! in `session/caps.rs` authorizes other targets. Root credentials remain separate.
use anyhow::{Context, Result, bail};
use serde::{Deserialize, Serialize};
use std::collections::{BTreeMap, HashMap, HashSet};
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::{
    Arc,
    atomic::{AtomicBool, Ordering},
};

#[derive(Debug)]
pub(crate) struct CredentialPersistence;

impl std::fmt::Display for CredentialPersistence {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("persisting agent credential")
    }
}

/// Operation categories used by self-access and transport checks.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Level {
    /// List, read, watch: state, screen, `capture-pane`.
    Ro,
    /// Ro plus input and lifecycle: keys, resize, start, stop, restart.
    Rw,
}

/// An agent identity with shared revocation state.
#[derive(Debug, Clone)]
pub struct AgentCredential {
    /// The authenticated agent name, bound to its saved identity by the credential store.
    pub grantor: String,
    // Legacy test fixtures deliberately carry ignored scopes to detect bypasses.
    #[cfg(test)]
    pub sessions: HashSet<String>,
    #[cfg(test)]
    pub level: Level,
    /// All resolved copies share revocation state.
    pub(crate) revoked: Arc<AtomicBool>,
}

/// The capability that `auth` resolves from a request token.
/// `Root` gives the mod read and write access to all sessions, including host sessions.
/// Agent credentials never authorize host sessions.
#[derive(Debug, Clone)]
pub enum Cap {
    Root,
    Scoped(AgentCredential),
}

impl Cap {
    pub fn is_valid(&self) -> bool {
        match self {
            Cap::Root => true,
            Cap::Scoped(g) => !g.revoked.load(Ordering::Acquire),
        }
    }

    pub fn principal(&self) -> Option<&str> {
        match self {
            Cap::Root => None,
            Cap::Scoped(g) => Some(&g.grantor),
        }
    }
    /// Check whether this capability permits access to `session` at level `need`.
    /// The caller supplies the session flag `is_host`. Root permits all access.
    /// The credential supplies self-access only. Other targets require current policy.
    pub fn allows(&self, session: &str, is_host: bool, _need: Level) -> bool {
        match self {
            Cap::Root => true,
            Cap::Scoped(g) => self.is_valid() && !is_host && g.grantor == session,
        }
    }

    /// Only root can create sessions directly.
    pub fn may_create(&self) -> bool {
        matches!(self, Cap::Root)
    }
}

#[derive(Debug, Clone)]
struct Entry {
    /// Test fixtures can model credentials without a running process.
    launch: bool,
    grant: AgentCredential,
    /// Stable identity checked before startup restores a process credential.
    grantor_state_id: String,
}

#[derive(Debug, Serialize, Deserialize)]
struct File {
    #[serde(default)]
    credentials: Vec<StoredCredential>,
}

#[derive(Debug, Serialize, Deserialize)]
struct StoredCredential {
    token: String,
    grantor: String,
    grantor_state_id: String,
}

/// Private persisted process credentials, indexed by bearer token.
/// The default store supports capability tests that do not need disk persistence.
#[derive(Default)]
pub struct CredentialStore {
    path: Option<PathBuf>,
    by_token: HashMap<String, Entry>,
}

impl CredentialStore {
    #[cfg(test)]
    pub fn load(config: &Path) -> Result<Self> {
        Self::load_path(config.with_file_name("grants.toml"))
    }

    pub(crate) fn load_data(data: &Path) -> Result<Self> {
        let path = crate::paths::normalize(data)?.join("credentials.toml");
        anyhow::ensure!(
            crate::paths::normalize(&path)? == path,
            "credential store aliases another path"
        );
        Self::load_path(path)
    }

    fn load_path(path: PathBuf) -> Result<Self> {
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            let metadata = match fs::metadata(&path) {
                Ok(metadata) => metadata,
                Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
                    return Ok(Self {
                        path: Some(path),
                        by_token: HashMap::new(),
                    });
                }
                Err(error) => {
                    return Err(error).with_context(|| format!("checking {}", path.display()));
                }
            };
            if metadata.permissions().mode() & 0o777 != 0o600 {
                fs::set_permissions(&path, fs::Permissions::from_mode(0o600))
                    .with_context(|| format!("securing {}", path.display()))?;
            }
        }

        let text = match fs::read_to_string(&path) {
            Ok(text) => text,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
                return Ok(Self {
                    path: Some(path),
                    by_token: HashMap::new(),
                });
            }
            Err(error) => return Err(error).with_context(|| format!("reading {}", path.display())),
        };
        Self::decode(&text, &path)
    }

    fn decode(text: &str, path: &Path) -> Result<Self> {
        let file: File =
            toml::from_str(text).with_context(|| format!("parsing {}", path.display()))?;
        let mut by_token = HashMap::new();
        for stored in file.credentials {
            if !valid_token(&stored.token)
                || stored.grantor.trim().is_empty()
                || stored.grantor_state_id.is_empty()
            {
                bail!("invalid identity credential in {}", path.display());
            }
            let entry = Entry {
                launch: true,
                grant: AgentCredential {
                    grantor: stored.grantor,
                    #[cfg(test)]
                    sessions: HashSet::new(),
                    #[cfg(test)]
                    level: Level::Rw,
                    revoked: Default::default(),
                },
                grantor_state_id: stored.grantor_state_id,
            };
            if by_token.insert(stored.token, entry).is_some() {
                bail!("duplicate credential token in {}", path.display());
            }
        }

        Ok(Self {
            path: Some(path.to_owned()),
            by_token,
        })
    }

    /// Resolve the capability for a request. `root` is `[daemon] token`.
    /// A missing or whitespace-only root fails closed, even for live credentials.
    /// A valid root gives `Root` access only when the token matches.
    /// Any other token must identify a live credential. Otherwise, the request has no capability.
    pub fn resolve(&self, presented: Option<&str>, root: &str) -> Option<Cap> {
        if root.trim().is_empty() {
            return None;
        }
        match presented {
            Some(t) if t == root => Some(Cap::Root),
            Some(t) => self
                .by_token
                .get(t)
                .map(|entry| Cap::Scoped(entry.grant.clone())),
            None => None,
        }
    }

    /// Create an in-memory grant for callers that do not own a persistent store.
    #[cfg(test)]
    pub fn mint(&mut self, grant: AgentCredential) -> String {
        self.mint_with_identity(grant, String::new(), BTreeMap::new(), false)
            .expect("in-memory grant cannot fail to persist")
    }

    /// Create and save a grant before returning its token.
    /// The manager supplies stable session identities.
    /// These let the daemon remove grants for deleted or replaced sessions at startup.
    #[cfg(test)]
    pub fn mint_persisted(
        &mut self,
        grant: AgentCredential,
        grantor_state_id: String,
        session_state_ids: BTreeMap<String, String>,
    ) -> Result<String> {
        self.mint_with_identity(grant, grantor_state_id, session_state_ids, false)
    }

    /// Process credentials belong to one launch.
    pub(crate) fn mint_launch(&mut self, name: &str, state_id: &str) -> Result<String> {
        self.mint_with_identity(
            AgentCredential {
                grantor: name.into(),
                #[cfg(test)]
                sessions: [name.into()].into(),
                #[cfg(test)]
                level: Level::Rw,
                revoked: Default::default(),
            },
            state_id.into(),
            [(name.into(), state_id.into())].into(),
            true,
        )
    }

    pub(crate) fn revoke_launch(&mut self, name: &str) -> Result<bool> {
        self.revoke_matching(|entry| entry.launch && entry.grant.grantor == name)
    }

    pub(crate) fn prune_absent_launches(&mut self, panes: &HashSet<String>) -> Result<bool> {
        self.revoke_matching(|entry| entry.launch && !panes.contains(&entry.grant.grantor))
    }

    fn mint_with_identity(
        &mut self,
        grant: AgentCredential,
        grantor_state_id: String,
        _session_state_ids: BTreeMap<String, String>,
        launch: bool,
    ) -> Result<String> {
        let token = loop {
            let token = gen_token().context(CredentialPersistence)?;
            if !self.by_token.contains_key(&token) {
                break token;
            }
        };
        self.by_token.insert(
            token.clone(),
            Entry {
                launch,
                grant,
                grantor_state_id,
            },
        );
        if let Err(error) = self.persist() {
            self.by_token.remove(&token);
            return Err(error).context(CredentialPersistence);
        }
        Ok(token)
    }

    #[cfg(test)]
    pub fn try_revoke_grantor(&mut self, grantor: &str) -> Result<bool> {
        self.revoke_matching(|entry| entry.grant.grantor == grantor)
    }

    /// Revoke the entire grant when any target or the grantor no longer exists, before session name reuse.
    #[cfg(test)]
    pub fn invalidate_session(&mut self, name: &str) -> bool {
        self.try_invalidate_session(name).unwrap_or(false)
    }

    pub fn try_invalidate_session(&mut self, name: &str) -> Result<bool> {
        self.revoke_matching(|entry| entry.grant.grantor == name)
    }

    /// Remove credentials that do not match the current session identities.
    /// This also removes grants for sessions that disappeared while the daemon was not running.
    pub fn prune_stale(
        &mut self,
        state_ids: &HashMap<String, String>,
        host_sessions: &HashSet<String>,
    ) -> Result<bool> {
        self.revoke_matching(|entry| {
            let grantor = if entry.grantor_state_id.is_empty() {
                host_sessions.contains(&entry.grant.grantor)
            } else {
                !host_sessions.contains(&entry.grant.grantor)
                    && state_ids.get(&entry.grant.grantor) == Some(&entry.grantor_state_id)
            };
            !grantor
        })
    }

    fn revoke_matching(&mut self, matches: impl Fn(&Entry) -> bool) -> Result<bool> {
        let removed: Vec<String> = self
            .by_token
            .iter()
            .filter(|(_, entry)| matches(entry))
            .map(|(token, _)| token.clone())
            .collect();
        if removed.is_empty() {
            return Ok(false);
        }

        for token in removed {
            if let Some(entry) = self.by_token.remove(&token) {
                entry.grant.revoked.store(true, Ordering::Release);
            }
        }
        if let Err(error) = self.persist() {
            // If saving the reduced store fails, delete the stored bearer tokens.
            // This prevents a restart from restoring credentials that this process already revoked.
            if let Some(path) = &self.path {
                match fs::remove_file(path) {
                    Ok(()) => {
                        return Err(error)
                            .context("credential store cleared after revoke write failure");
                    }
                    Err(clear_error) if clear_error.kind() == std::io::ErrorKind::NotFound => {
                        return Err(error)
                            .context("credential store absent after revoke write failure");
                    }
                    Err(clear_error) => {
                        return Err(error).context(format!(
                            "credential revoke write failed and {} could not be cleared: {clear_error}",
                            path.display()
                        ));
                    }
                }
            }
            return Err(error).context("persisting credential revocation");
        }
        Ok(true)
    }

    fn persist(&self) -> Result<()> {
        let Some(path) = &self.path else {
            return Ok(());
        };
        let mut credentials: Vec<StoredCredential> = self
            .by_token
            .iter()
            .map(|(token, entry)| StoredCredential {
                token: token.clone(),
                grantor: entry.grant.grantor.clone(),
                grantor_state_id: entry.grantor_state_id.clone(),
            })
            .collect();
        credentials.sort_by(|a, b| a.token.cmp(&b.token));
        let text = toml::to_string(&File { credentials })?;
        crate::paths::write_private_toml(path, &text)
            .with_context(|| format!("writing {}", path.display()))
    }

    /// The total number of stored credentials.
    pub fn count(&self) -> usize {
        self.by_token.len()
    }
}

fn valid_token(token: &str) -> bool {
    token.len() == 32 && token.bytes().all(|byte| byte.is_ascii_hexdigit())
}

/// Generate a root or scoped bearer token from 128 bits of `/dev/urandom` data.
/// Failure to read secure entropy must fail credential creation, not mint a guessable secret.
pub(crate) fn gen_token() -> Result<String> {
    let mut file = std::fs::File::open("/dev/urandom")
        .context("opening secure randomness for daemon credential")?;
    gen_token_from(&mut file)
}

fn gen_token_from(mut source: impl std::io::Read) -> Result<String> {
    let mut buf = [0u8; 16];
    source
        .read_exact(&mut buf)
        .context("reading secure randomness for agent credential")?;
    Ok(buf.iter().map(|b| format!("{b:02x}")).collect())
}

#[cfg(test)]
#[path = "credentials_tests.rs"]
mod tests;
