# Configuration ownership

The daemon owns machine settings and workspace records. `config/settings.rs` owns
root settings; `config/model.rs` assembles client views. `storage/` owns record
schemas, accepted indexes, targeted persistence and bound transaction recovery.
`session/manager/config/` serializes validation, runtime effects and publication.

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
| `agent_templates/` | `session/agent_templates/`; [templates](daemon-agent-templates.md). |
| `sandbox_presets/`, `app_presets/` | `presets.rs`, `presets/edit.rs`; [presets](daemon-presets.md). |
| `jukebox/` | `jukebox.rs`; [jukebox](mod-jukebox.md). |

Settings and workspace records are API-owned while running. Offline edits load at
startup; ordinary mutations use accepted indexes and touch only their declared
owners. Independent library items retain live reload, with membership and every
file revision checked before publication. A newer sibling cannot hide deletion.

Record updates retain unknown extensions without resurrecting known cleared fields.
Settings patches preserve omitted values; replacements preserve accepted workspace
records. Both reject inline `project`, `session`, `host_terminal`, and `library`.
The redacted-token sentinel retains the stored secret; an empty token clears it.
The config HTTP response projects the assembled view into the existing wire schema;
host-shell storage IDs stay internal and are omitted only from the response copy.

Workspace transactions carry explicit targets bound to normalized config/data roots
and the settings filename. Recovery rejects a changed mapping before any write. Startup reserves the configured and undo-document endpoints
before recovery, using the existing listener exclusion.
The data-root undo journal covers multi-record configuration changes, including
project/reference edits; ordinary task batches commit independently. Atomic writes
and journal recovery cover process interruption, without fsync power-loss guarantees.

Structured commits retain session/worktree guards and the persistence gate through
commit, rollback and accepted-state publication, even after requester cancellation.
Root and library operations cannot accept revisions belonging to another owner.

Startup refuses legacy layouts and directs the operator to the explicit offline
[migration command](../docs/src/guides/storage-migration.md). Migration-only decoding
and old-journal recovery are isolated under `storage/migration` and `config/transaction`.
Legacy writable adapters remain compiled only for historical test fixtures.

Public read-model contracts belong to [the API](../docs/src/reference/api.md);
draft/save behavior to [Settings](ui-settings.md); protected filesystem boundaries
to [sandbox isolation](sandbox-isolation.md).
`config/commands.rs` resolves Auto reader tools using the daemon's PATH.
