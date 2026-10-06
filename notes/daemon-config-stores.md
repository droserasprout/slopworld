# Configuration ownership

The daemon owns machine configuration. `config/` owns its model, resolution,
validation, and persistence; `session/manager/config/` serializes accepted changes
and publication. Lifecycle reconciliation applies accepted configuration to live
sessions. Startup and polling belong to manager init and maintenance.

The mod owns offline profile preferences. It reads `endpoint.toml` for credentials
and uses daemon APIs; it must not read or rewrite daemon TOML. Locations and
overrides belong to [paths](ops-paths.md).

| Store | Owner and related contract |
| --- | --- |
| `config.toml` | `config/settings.rs` owns machine settings, `model.rs` assembles the read model, and `legacy.rs` prepares the current inline layout; [projects](daemon-projects.md) and [host tabs](daemon-host-terminals.md). |
| `prompts/`, `breadcrumbs/`, `file_actions/`, `shell_scripts/` | `config/library.rs`, `catalog.rs`, and `persistence.rs`; [library](daemon-library.md). |
| `agent_templates/` | `session/agent_templates/`; [templates](daemon-agent-templates.md). |
| `sandbox_presets/`, `app_presets/` | `presets.rs` and `presets/edit.rs`; [presets](daemon-presets.md). |
| `jukebox/` | `jukebox.rs`; [jukebox](mod-jukebox.md). |
| `worktrees.toml` | `worktrees/`; [worktree ownership](daemon-worktrees.md). |

Patches preserve omitted and unknown document fields. Typed saves preserve unrelated
fields but must not resurrect known fields intentionally cleared by serialization.
Existing TOML parse or type-conversion failures reject typed saves rather than
replacing the document. Config/library saves recover as one transaction; callers
serialize writes and recovery through the configuration gate. When main config is
missing, existing library files load before default creation.
The transaction owner accepts explicit file changes. Catalog replacement discovers
retirements in `catalog.rs`; committing an explicit root-only change does not scan
or retire library files. `Config` has no aggregate save method. Structured mutation callers declare their
store ownership, and the manager rejects changes outside that scope before preparing
explicit file changes. Workspace mutations currently select the inline root only;
library mutations select catalogs only. Each updates only its own accepted revision.
`legacy.rs` remains the selected disk adapter until the record-layout cutover.

Structured commits transfer their prepared candidate and persistence gate to owned
work. Session protection remains held through disk commit, rollback, and accepted-state
publication even if the requester disconnects. Owned work inherits the request
context, so it cannot reload disk state after authorization.

The redacted-token sentinel retains the stored secret; an empty token clears it.
Editable patches carry explicit leaf paths so false, zero, and empty lists remain
distinct from omission. Secrets and response metadata stay outside editable projections.
`GET /api/config` supplies factory defaults and policy metadata; clients must not
invent missing daemon defaults. Public read-model contracts belong to
[the API](../docs/src/reference/api.md);
draft/save behavior to [Settings](ui-settings.md); protected filesystem boundaries
to [sandbox isolation](sandbox-isolation.md).

`config/commands.rs` resolves Auto reader tools using the daemon's PATH. Config API
metadata carries `auto_commands` even when the saved choices are explicit; reader
launches use those commands while drafts and saves retain `auto`. Highlight theme
discovery and previews resolve request-local Auto choices through the same owner.
