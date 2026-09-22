//! Scoped credentials: one agent allowed to watch or drive another, and never the host. See
//! [notes/agent-grants.md]. The token is no longer all-or-nothing - the mod's `[daemon] token` is
//! the *root*, and any other live credential is a `Grant` persisted beside daemon config and
//! revoked when its grantor or a target session goes away.

use anyhow::{bail, Context, Result};
use serde::{Deserialize, Serialize};
use std::collections::{BTreeMap, HashMap, HashSet};
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::{
    atomic::{AtomicBool, Ordering},
    Arc,
};

/// What a grant lets its holder do to the sessions it names.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Level {
    /// List, read, watch: state, screen, `capture-pane`.
    Ro,
    /// Ro plus input and lifecycle: keys, resize, start, stop, restart.
    Rw,
}

impl Level {
    /// rw answers a demand for either; ro answers only ro.
    pub fn satisfies(self, need: Level) -> bool {
        matches!((self, need), (Level::Rw, _) | (Level::Ro, Level::Ro))
    }
}

/// A credential, revoked when its grantor or any target disappears.
#[derive(Debug, Clone)]
pub struct Grant {
    /// The session this grant's life is tied to. When it goes, the grant goes.
    pub grantor: String,
    /// The sessions the holder may touch. A mint never puts a host session here.
    pub sessions: HashSet<String>,
    pub level: Level,
    /// Every resolved copy shares revocation; scopes never change after minting.
    pub(crate) revoked: Arc<AtomicBool>,
}

/// What `auth` resolved a request's token to. `Root` is the mod: every session, host ones
/// included, read and write. `Scoped` is a minted grant, which never covers a host session.
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
    /// May this capability touch `session` at `need`? `is_host` is the session's own flag,
    /// looked up by the caller. Root touches anything; a scoped grant touches a session it
    /// names, at a level that satisfies the demand, and never a host one - the last a
    /// backstop, since a mint never scopes a host session to begin with.
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

    /// Whether a list should show this holder `session`. The read half of `allows`: a name a
    /// grant cannot read is a name it never learns exists.
    pub fn can_see(&self, session: &str, is_host: bool) -> bool {
        self.allows(session, is_host, Level::Ro)
    }

    /// Only the root creates sessions - a new agent, an errand. A grant is a handle on what is
    /// already there.
    pub fn may_create(&self) -> bool {
        matches!(self, Cap::Root)
    }
}

#[derive(Debug, Clone)]
struct Entry {
    grant: Grant,
    /// Stable agent identities prevent a grant from silently attaching to a reused session name
    /// after the daemon was down. Host terminals have no persistent state id and store this empty.
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

/// Active grants are indexed by bearer token and persisted in a private sibling file. A default
/// store remains available for capability-only unit fixtures that do not need disk persistence.
#[derive(Default)]
pub struct Grants {
    path: Option<PathBuf>,
    by_token: HashMap<String, Entry>,
}

impl Grants {
    pub fn load(config: &Path) -> Result<Self> {
        let path = config.with_file_name("grants.toml");
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            let metadata = match fs::metadata(&path) {
                Ok(metadata) => metadata,
                Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
                    return Ok(Self {
                        path: Some(path),
                        by_token: HashMap::new(),
                    })
                }
                Err(error) => {
                    return Err(error).with_context(|| format!("checking {}", path.display()))
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
                })
            }
            Err(error) => return Err(error).with_context(|| format!("reading {}", path.display())),
        };
        let file: File =
            toml::from_str(&text).with_context(|| format!("parsing {}", path.display()))?;
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
            path: Some(path),
            by_token,
        })
    }

    /// The capability a request carries. `root` is `[daemon] token`. An empty root is the
    /// "no auth" contract - fine on a loopback bind - and resolves Root for anyone. A set root
    /// matches only itself; any other token must name a live grant, or the request is nobody.
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

    /// Mint an in-memory grant for capability-only callers that do not own a persistent store.
    pub fn mint(&mut self, grant: Grant) -> String {
        self.mint_with_identity(grant, String::new(), BTreeMap::new())
            .expect("in-memory grant cannot fail to persist")
    }

    /// Mint and persist before returning the token to its caller. The manager supplies stable
    /// session identities so startup can discard grants for removed or replaced sessions.
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
            let token = gen_token();
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
            return Err(error).context("persisting scoped grant");
        }
        Ok(token)
    }

    /// Drop every grant a session owns and persist the revocation before returning.
    pub fn revoke_grantor(&mut self, grantor: &str) {
        let _ = self.try_revoke_grantor(grantor);
    }

    pub fn try_revoke_grantor(&mut self, grantor: &str) -> Result<bool> {
        self.revoke_matching(|entry| entry.grant.grantor == grantor)
    }

    /// Losing any target or the grantor revokes the entire grant before name reuse.
    pub fn invalidate_session(&mut self, name: &str) -> bool {
        self.try_invalidate_session(name).unwrap_or(false)
    }

    pub fn try_invalidate_session(&mut self, name: &str) -> Result<bool> {
        self.revoke_matching(|entry| {
            entry.grant.grantor == name || entry.grant.sessions.contains(name)
        })
    }

    /// Drop credentials that do not bind to the current session identities. This also removes
    /// grants whose sessions disappeared while the daemon was not running.
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
            // If the reduced store cannot be written, clear the previous bearer tokens so a
            // restart cannot restore credentials that this process has already revoked.
            if let Some(path) = &self.path {
                match fs::remove_file(path) {
                    Ok(()) => {
                        return Err(error).context("grant store cleared after revoke write failure")
                    }
                    Err(clear_error) if clear_error.kind() == std::io::ErrorKind::NotFound => {
                        return Err(error).context("grant store absent after revoke write failure")
                    }
                    Err(clear_error) => {
                        return Err(error).context(format!(
                            "grant revoke write failed and {} could not be cleared: {clear_error}",
                            path.display()
                        ))
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

    /// Grants the given session may touch, for the mod's readout. Not scoped by host-ness here:
    /// a mint never names one.
    pub fn count(&self) -> usize {
        self.by_token.len()
    }
}

fn valid_token(token: &str) -> bool {
    token.len() == 32 && token.bytes().all(|byte| byte.is_ascii_hexdigit())
}

/// 128 bits of urandom, hex. A grant token is a bearer secret, so it comes from the same place
/// a session key would; the fallback is only for a machine with no `/dev/urandom`, which is not
/// Linux, and even then two grants do not collide within a daemon's life.
fn gen_token() -> String {
    use std::io::Read;
    let mut buf = [0u8; 16];
    if std::fs::File::open("/dev/urandom")
        .and_then(|mut f| f.read_exact(&mut buf))
        .is_err()
    {
        // Time and a bump, so a urandom-less host still gets distinct tokens.
        use std::sync::atomic::{AtomicU64, Ordering};
        static BUMP: AtomicU64 = AtomicU64::new(0);
        let n = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_nanos() as u64)
            .unwrap_or(0)
            ^ BUMP.fetch_add(0x9E37_79B9_7F4A_7C15, Ordering::Relaxed);
        buf[..8].copy_from_slice(&n.to_le_bytes());
        buf[8..].copy_from_slice(&n.rotate_left(32).to_le_bytes());
    }
    buf.iter().map(|b| format!("{b:02x}")).collect()
}

#[cfg(test)]
#[path = "grant_tests.rs"]
mod tests;
