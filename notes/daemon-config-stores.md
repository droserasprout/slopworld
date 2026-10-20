# Configuration ownership

The daemon owns machine settings and workspace records. `config/settings.rs` owns
root settings; `config/model.rs` assembles client views. `config/validation.rs` owns
configuration and project invariants; the session adapter checks live constraints.
`config/patch.rs` converts JSON patches before settings document preparation. `storage/` owns record
schemas, accepted indexes, targeted persistence and bound transaction recovery.
`config/transactions/` owns the persistence gate, prepared changes, accepted record
indexes, cache-link rollback, and library revision acceptance. Its runtime adapter
in `session/manager/config/` retains session/worktree operation guards, validates
live constraints, delegates accepted publication to `application/config_authority.rs`, and
delegates endpoint credential updates to `application/workspace.rs` before releasing
the persistence gate. `ConfigAuthority` holds the single accepted configuration lock, grants, and credential epochs;
it publishes root-token changes and invalidation under that lock. Session adapters
validate grant identities and retain operation guards through credential changes.
Startup consumes the loaded configuration into accepted state and passes the
remaining storage state to `application/workspace.rs`. Application startup constructs
that workspace owner and seeds the library snapshot from the injected authority.
The workspace owns configuration transactions, the endpoint path, and the worktree
mutation gate; Manager retains shared handles and session operation guards.
HTTP and WebSocket admission,
credential generations, notifications, and grant counts use `ConfigAuthority`
directly. `session/manager/caps.rs` retains session identity checks and guarded
grant mutations.
Client announcements follow the completed transaction. `config/accepted.rs` gives background services
read-only daemon-settings snapshots from that same lock; there is no secondary
settings cache or independent publication step. `application/catalogs.rs` holds independent
catalog polling deadlines and timestamps.
`storage/layout.rs` recovers and loads the workspace once, then hands the validated
configuration, typed record indexes and sampled library revision to application startup.
The session manager receives the constructed workspace; it never reloads workspace
records during initialization or ordinary mutations.

The mod owns offline profile preferences. It reads `endpoint.toml` for credentials
and uses daemon APIs; it must not read or rewrite daemon TOML. Locations and overrides
belong to [paths](ops-paths.md).

| Store | Owner and contract |
| --- | --- |
| `config.toml` | `config/settings_document.rs`; settings and extensions only, never workspace membership. |
| Config `projects/` | `storage/workspace.rs`; [projects](daemon-projects.md). |
| Data `agents/`, `host_shells/` | `storage/sessions.rs`; [host tabs](daemon-host-terminals.md). |
| Data `worktrees/` | `storage/workspace.rs`; [worktree ownership](daemon-worktrees.md). |
| Data `tasks/`, `grants.toml` | [Tasks](agent-tasks.md), [grants](agent-grants.md). |
| `prompts/`, `breadcrumbs/`, `file_actions/`, `shell_scripts/` | `config/catalog.rs`; [library](daemon-library.md). |
| `agent_templates/` | `agent_templates/`; [templates](daemon-agent-templates.md). |
| Config `sandbox_presets/`, `app_presets/` | `presets.rs`, `presets/edit.rs`; [presets](daemon-presets.md). |
| Config `jukebox/` | `jukebox.rs`; [jukebox](mod-jukebox.md). |

Settings and workspace records are API-owned while running. Offline edits load at
startup; ordinary mutations use accepted indexes and touch only their declared
owners. Independent library items retain live reload, with membership and every
file revision checked before publication. A newer sibling cannot hide deletion.
Library reload recovers the shared journal before accepting revisions. Shared
read requests can skip reload when the accepted revision matches and no journal
is present. Failed recovery retains the accepted catalog and revision for retry.

Record updates retain unknown extensions without resurrecting known cleared fields.
Settings patches preserve omitted values; replacements preserve accepted workspace
records. Both reject inline `project`, `session`, `host_terminal`, and `library`.
The redacted-token sentinel retains the stored secret; blank tokens are rejected.
First startup provisions a secure token only when the settings file is absent.
The config HTTP response projects the assembled view into the existing wire schema;
host-shell storage IDs stay internal and are omitted only from the response copy.

Workspace transactions carry explicit targets bound to normalized config/data roots
and the settings filename. Recovery rejects a changed mapping before any write. Startup reserves the configured and undo-document endpoints
before recovery, using the existing listener exclusion.
Hostname resolution is retained through recovery. Usable resolved addresses are
reserved together; an occupied address cannot be bypassed with another DNS answer.
The data-root undo journal covers multi-record configuration changes, including
project/reference edits; ordinary task batches commit independently. Atomic writes
and journal recovery cover process interruption, without fsync power-loss guarantees.

Structured commits retain session/worktree guards and the persistence gate through
commit, rollback and accepted-state publication, even after requester cancellation.
Root and library operations cannot accept revisions belonging to another owner.

Manager/domain fixtures use the production record stores and transaction owner.
Schema tests deserialize individual documents; manager task tests use the production
async I/O owner for mutations and reads.

Public read-model contracts belong to [the API](../docs/src/reference/api.md);
draft/save behavior to [Settings](ui-settings.md); protected filesystem boundaries
to [sandbox isolation](sandbox-isolation.md).
`config/commands.rs` resolves Auto reader tools using the daemon's PATH.
