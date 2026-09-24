# Split daemon configuration records

Status: proposed

Move repeated project, agent and host-terminal records out of `config.toml` into one TOML
file per record.
Keep the existing in-memory `Config` and API value model.
Change only the on-disk representation. The main file retains `[daemon]`, `[defaults]`, `[commands]`,
ordered `[[state_rule]]` entries, and unknown root extensions.
State rules are few and ordered. They do not need one file each.

## Target layout

All paths are beside the configured `SLOPD_CONFIG`, not necessarily under the default XDG root.

| Data | Target | Form |
| --- | --- | --- |
| Daemon, defaults, commands, state rules | `config.toml` | Inline tables and ordered array entries |
| Projects and shared mounts | `projects/<project-id>.toml` | One record per file |
| Agent/session configuration | `sessions/<state-id>.toml` | One record per file |
| Host terminal configuration | `host_terminals/<name>.toml` | One record per file |

Keep prompts, breadcrumbs, file actions, shell scripts, agent templates, presets, jukebox
stations, tasks, worktrees, endpoint credentials and all `$XDG_DATA_HOME` state in their
existing stores.
See [configuration ownership](daemon-config-stores.md) and
[runtime paths](ops-paths.md). In particular, config-side `sessions/<state-id>.toml` files
describe agents. `$XDG_DATA_HOME/slopworld/sessions/<state-id>/` remains their separate
private runtime state, including `launch-plan.json` and `.trash`.

## Identity, loading and persistence

- Parse each per-record file as one top-level record, then assemble the existing vectors in
  `Config`. Reject duplicate identities, mismatched filename/record identities, unsafe file
  names and malformed records with path-specific errors. Ignore non-TOML files and load TOML
  paths in a deterministic order.
- Use project IDs for project filenames and preserve every non-empty ID because worktrees and
  managed caches refer to them. Assign IDs to legacy projects that lack them during migration.
  Use the existing canonical `state_id` UUID for agent filenames.
  Never derive that filename from the agent name, which can change. Host-terminal filenames use validated names.
- Decide and encode ordering for projects and agents before implementation. Directory order
  must not accidentally change any user-visible ordering currently inherited from the arrays.
  Preserve state-rule array order in `config.toml`.
- Keep each file owner-only (`0600`) and use the existing atomic-write helper. Preserve unknown
  fields at the root and within records across typed saves and configuration patches.
  Fields modeled previously and then cleared must remain cleared.
- Serialize mutations under the existing config persistence gate. Validate the complete
  candidate before changing disk. Define failure and reload behavior for a replace touching
  multiple files so the daemon cannot publish a partially written candidate.
  Use a staged snapshot/commit marker or an equivalent recoverable transaction.
- Watch file creation, modification and deletion in all three directories, in addition to
  `config.toml`. A directory timestamp alone does not detect edits to existing files.

Normal config loads currently do not rewrite files. Keep that rule: provide an explicit,
backup-preserving migration operation rather than silently migrating during `Config::load`.
It must be idempotent, validate and stage all records before removing the old inline arrays,
and leave the original config untouched on failure. If split files coexist with legacy inline
records, report a conflict rather than guessing which copy wins.

## API and editor boundary

Keep `/api/config` values, defaults metadata, token redaction/sentinel behavior and patch
semantics unchanged.
The mod must continue using the daemon API. It does not discover or read these files. The raw config editor currently reads and replaces a whole TOML document. Preserve
that logical editing surface with a deterministic assembled document, or change the editor to
clearly expose the component files.
Do not present `config.toml` as the complete physical configuration if it is not. Preserve unknown TOML data either way. Decide explicitly how comments in
per-record files behave in the assembled editor before shipping.

Update the paths reference, backup guidance and config-store ownership note with the resulting
locations. Remove this plan once migration, storage ownership and editor behavior are settled.
