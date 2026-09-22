//! Scoped, ephemeral credentials: one agent allowed to watch or drive another, and never the
//! host. See [notes/agent-grants.md]. The token is no longer all-or-nothing - the mod's
//! `[daemon] token` is the *root*, and any other live credential is a `Grant` the user minted,
//! held in memory and gone when its grantor session goes.

use std::collections::{HashMap, HashSet};
use std::sync::{
    atomic::{AtomicBool, Ordering},
    Arc,
};

/// What a grant lets its holder do to the sessions it names.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
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

/// An in-memory credential, revoked when its grantor or any target disappears.
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

/// The live grants, by their token. In memory only; nothing here is written to `config.toml`.
#[derive(Default)]
pub struct Grants {
    by_token: HashMap<String, Grant>,
}

impl Grants {
    /// The capability a request carries. `root` is `[daemon] token`. An empty root is the
    /// "no auth" contract - fine on a loopback bind - and resolves Root for anyone. A set root
    /// matches only itself; any other token must name a live grant, or the request is nobody.
    pub fn resolve(&self, presented: Option<&str>, root: &str) -> Option<Cap> {
        if root.is_empty() {
            return Some(Cap::Root);
        }
        match presented {
            Some(t) if t == root => Some(Cap::Root),
            Some(t) => self.by_token.get(t).cloned().map(Cap::Scoped),
            None => None,
        }
    }

    /// Mint a grant, returning the fresh token that names it. The caller has already refused any
    /// host session in the scope; this only stores it.
    pub fn mint(&mut self, grant: Grant) -> String {
        let token = gen_token();
        self.by_token.insert(token.clone(), grant);
        token
    }

    /// Drop every grant a session owned. Called when the session is forgotten, which is what
    /// makes the whole scheme ephemeral: no exit path leaves a live credential behind.
    pub fn revoke_grantor(&mut self, grantor: &str) {
        self.revoke_matching(|g| g.grantor == grantor);
    }

    /// Losing any target or the grantor revokes the entire grant before name reuse.
    pub fn invalidate_session(&mut self, name: &str) -> bool {
        self.revoke_matching(|g| g.grantor == name || g.sessions.contains(name))
    }

    fn revoke_matching(&mut self, matches: impl Fn(&Grant) -> bool) -> bool {
        let mut changed = false;
        self.by_token.retain(|_, grant| {
            if matches(grant) {
                grant.revoked.store(true, Ordering::Release);
                changed = true;
                return false;
            }
            true
        });
        changed
    }

    /// Grants the given session may touch, for the mod's readout. Not scoped by host-ness here:
    /// a mint never names one.
    pub fn count(&self) -> usize {
        self.by_token.len()
    }
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
