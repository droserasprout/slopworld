//! Scoped credentials let an agent monitor or control another agent, but not a host session.
//! See [notes/agent-grants.md]. The mod uses `[daemon] token` as the root credential.
//! The daemon stores each other live credential in the data-root grant store.
//! It revokes the grant when the grantor or a target session no longer exists.

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
pub(crate) struct GrantPersistence;

impl std::fmt::Display for GrantPersistence {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("persisting scoped grant")
    }
}

/// The operations a grant permits on its specified sessions.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Level {
    /// List, read, watch: state, screen, `capture-pane`.
    Ro,
    /// Ro plus input and lifecycle: keys, resize, start, stop, restart.
    Rw,
}

impl Level {
    /// `Rw` satisfies either access level. `Ro` satisfies only `Ro`.
    pub fn satisfies(self, need: Level) -> bool {
        matches!((self, need), (Level::Rw, _) | (Level::Ro, Level::Ro))
    }
}

/// A credential that the daemon revokes when its grantor or any target no longer exists.
#[derive(Debug, Clone)]
pub struct Grant {
    /// The grantor session. The daemon revokes the grant when this session no longer exists.
    pub grantor: String,
    /// The sessions the holder may access. Grant creation excludes host sessions.
    pub sessions: HashSet<String>,
    pub level: Level,
    /// All resolved copies share revocation state. Scopes do not change after grant creation.
    pub(crate) revoked: Arc<AtomicBool>,
}

/// The capability that `auth` resolves from a request token.
/// `Root` gives the mod read and write access to all sessions, including host sessions.
/// `Scoped` is a grant that excludes host sessions.
#[derive(Debug, Clone)]
pub enum Cap {
    Root,
    Scoped(Grant),
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
    /// A scoped grant must be valid, name the session, and satisfy the required access level.
    /// This check also excludes host sessions, although grant creation already excludes them.
    pub fn allows(&self, session: &str, is_host: bool, need: Level) -> bool {
        match self {
            Cap::Root => true,
            Cap::Scoped(g) => {
                self.is_valid()
                    && !is_host
                    && g.level.satisfies(need)
                    && g.sessions.contains(session)
            }
        }
    }

    /// Check whether a list can show `session` to this holder.
    /// Lists exclude session names when the grant does not permit read access.
    pub fn can_see(&self, session: &str, is_host: bool) -> bool {
        self.allows(session, is_host, Level::Ro)
    }

    /// Only root can create sessions. Grants permit access to existing sessions.
    pub fn may_create(&self) -> bool {
        matches!(self, Cap::Root)
    }
}

#[derive(Debug, Clone)]
struct Entry {
    grant: Grant,
    /// Stable agent identities prevent grants from attaching to reused session names after a daemon restart.
    /// Host terminals have no persistent state ID and leave this field empty.
    grantor_state_id: String,
    session_state_ids: BTreeMap<String, String>,
}

#[derive(Debug, Serialize, Deserialize)]
struct File {
    #[serde(default)]
    grants: Vec<StoredGrant>,
}

#[derive(Debug, Serialize, Deserialize)]
struct StoredGrant {
    token: String,
    grantor: String,
    #[serde(default)]
    grantor_state_id: String,
    sessions: Vec<String>,
    session_state_ids: BTreeMap<String, String>,
    level: Level,
}

/// This store indexes active grants by bearer token and saves them in a private sibling file.
/// The default store supports capability tests that do not need disk persistence.
#[derive(Default)]
pub struct Grants {
    path: Option<PathBuf>,
    by_token: HashMap<String, Entry>,
}

impl Grants {
    #[cfg(test)]
    pub fn load(config: &Path) -> Result<Self> {
        Self::load_path(config.with_file_name("grants.toml"))
    }

    /// Never merge independent authority
    /// sources or silently ignore a remaining legacy grants file.
    pub(crate) fn load_data(config: &Path, data: &Path) -> Result<Self> {
        let path = crate::paths::normalize(data)?.join("grants.toml");
        anyhow::ensure!(
            crate::paths::normalize(&path)? == path,
            "grant store aliases another path"
        );
        let legacy = config.with_file_name("grants.toml");
        if crate::paths::normalize(&legacy)? != path {
            match fs::symlink_metadata(&legacy) {
                Ok(_) => bail!(
                    "legacy grants remain; finish offline migration before selecting the data store"
                ),
                Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
                Err(error) => return Err(error).context("checking legacy grant authority"),
            }
        }
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

    pub(crate) fn validate_document(text: &str) -> Result<()> {
        Self::decode(text, Path::new("grants.toml")).map(|_| ())
    }

    fn decode(text: &str, path: &Path) -> Result<Self> {
        let file: File =
            toml::from_str(text).with_context(|| format!("parsing {}", path.display()))?;
        let mut by_token = HashMap::new();
        for stored in file.grants {
            if !valid_token(&stored.token)
                || stored.grantor.trim().is_empty()
                || stored
                    .sessions
                    .iter()
                    .any(|session| session.trim().is_empty())
            {
                bail!("invalid grant record in {}", path.display());
            }
            let sessions: HashSet<String> = stored.sessions.iter().cloned().collect();
            if sessions.len() != stored.sessions.len()
                || sessions.len() != stored.session_state_ids.len()
                || sessions
                    .iter()
                    .any(|session| !stored.session_state_ids.contains_key(session))
                || stored.session_state_ids.values().any(String::is_empty)
            {
                bail!("invalid grant scope in {}", path.display());
            }
            let entry = Entry {
                grant: Grant {
                    grantor: stored.grantor,
                    sessions,
                    level: stored.level,
                    revoked: Default::default(),
                },
                grantor_state_id: stored.grantor_state_id,
                session_state_ids: stored.session_state_ids,
            };
            if by_token.insert(stored.token, entry).is_some() {
                bail!("duplicate grant token in {}", path.display());
            }
        }

        Ok(Self {
            path: Some(path.to_owned()),
            by_token,
        })
    }

    /// Resolve the capability for a request. `root` is `[daemon] token`.
    /// An empty root disables authentication and gives every request `Root` access.
    /// A nonempty root gives `Root` access only when the token matches.
    /// Any other token must identify a live grant. Otherwise, the request has no capability.
    pub fn resolve(&self, presented: Option<&str>, root: &str) -> Option<Cap> {
        if root.is_empty() {
            return Some(Cap::Root);
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
    pub fn mint(&mut self, grant: Grant) -> String {
        self.mint_with_identity(grant, String::new(), BTreeMap::new())
            .expect("in-memory grant cannot fail to persist")
    }

    /// Create and save a grant before returning its token.
    /// The manager supplies stable session identities.
    /// These let the daemon remove grants for deleted or replaced sessions at startup.
    pub fn mint_persisted(
        &mut self,
        grant: Grant,
        grantor_state_id: String,
        session_state_ids: BTreeMap<String, String>,
    ) -> Result<String> {
        self.mint_with_identity(grant, grantor_state_id, session_state_ids)
    }

    fn mint_with_identity(
        &mut self,
        grant: Grant,
        grantor_state_id: String,
        session_state_ids: BTreeMap<String, String>,
    ) -> Result<String> {
        let token = loop {
            let token = gen_token().context(GrantPersistence)?;
            if !self.by_token.contains_key(&token) {
                break token;
            }
        };
        self.by_token.insert(
            token.clone(),
            Entry {
                grant,
                grantor_state_id,
                session_state_ids,
            },
        );
        if let Err(error) = self.persist() {
            self.by_token.remove(&token);
            return Err(error).context(GrantPersistence);
        }
        Ok(token)
    }

    /// Revoke every grant that a session owns. Save the revocation before returning.
    #[cfg(test)]
    pub fn revoke_grantor(&mut self, grantor: &str) {
        drop(self.try_revoke_grantor(grantor));
    }

    pub fn try_revoke_grantor(&mut self, grantor: &str) -> Result<bool> {
        self.revoke_matching(|entry| entry.grant.grantor == grantor)
    }

    /// Revoke the entire grant when any target or the grantor no longer exists, before session name reuse.
    #[cfg(test)]
    pub fn invalidate_session(&mut self, name: &str) -> bool {
        self.try_invalidate_session(name).unwrap_or(false)
    }

    pub fn try_invalidate_session(&mut self, name: &str) -> Result<bool> {
        self.revoke_matching(|entry| {
            entry.grant.grantor == name || entry.grant.sessions.contains(name)
        })
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
            let targets = entry.grant.sessions.iter().all(|session| {
                !host_sessions.contains(session)
                    && state_ids.get(session) == entry.session_state_ids.get(session)
            });
            !grantor || !targets
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
                            .context("grant store cleared after revoke write failure");
                    }
                    Err(clear_error) if clear_error.kind() == std::io::ErrorKind::NotFound => {
                        return Err(error).context("grant store absent after revoke write failure");
                    }
                    Err(clear_error) => {
                        return Err(error).context(format!(
                            "grant revoke write failed and {} could not be cleared: {clear_error}",
                            path.display()
                        ));
                    }
                }
            }
            return Err(error).context("persisting grant revocation");
        }
        Ok(true)
    }

    fn persist(&self) -> Result<()> {
        let Some(path) = &self.path else {
            return Ok(());
        };
        let mut grants: Vec<StoredGrant> = self
            .by_token
            .iter()
            .map(|(token, entry)| {
                let mut sessions: Vec<String> = entry.grant.sessions.iter().cloned().collect();
                sessions.sort();
                StoredGrant {
                    token: token.clone(),
                    grantor: entry.grant.grantor.clone(),
                    grantor_state_id: entry.grantor_state_id.clone(),
                    sessions,
                    session_state_ids: entry.session_state_ids.clone(),
                    level: entry.grant.level,
                }
            })
            .collect();
        grants.sort_by(|a, b| a.token.cmp(&b.token));
        let text = toml::to_string(&File { grants })?;
        crate::paths::write_private_toml(path, &text)
            .with_context(|| format!("writing {}", path.display()))
    }

    /// The total number of stored grants, for the mod's display.
    pub fn count(&self) -> usize {
        self.by_token.len()
    }
}

fn valid_token(token: &str) -> bool {
    token.len() == 32 && token.bytes().all(|byte| byte.is_ascii_hexdigit())
}

/// Generate a hexadecimal token from 128 bits of `/dev/urandom` data.
/// A grant token is a bearer secret, like a session key.
/// Failure to read secure entropy must fail grant creation, not mint a guessable secret.
fn gen_token() -> Result<String> {
    let mut file = std::fs::File::open("/dev/urandom")
        .context("opening secure randomness for scoped grant")?;
    gen_token_from(&mut file)
}

fn gen_token_from(mut source: impl std::io::Read) -> Result<String> {
    let mut buf = [0u8; 16];
    source
        .read_exact(&mut buf)
        .context("reading secure randomness for scoped grant")?;
    Ok(buf.iter().map(|b| format!("{b:02x}")).collect())
}

#[cfg(test)]
#[path = "grant_tests.rs"]
mod tests;
