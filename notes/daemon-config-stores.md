# Configuration ownership

The daemon owns machine configuration.
`config/` owns its model, resolution, validation, and persistence.
The root records live in `model.rs`; daemon settings, sandbox policy types, and
library definitions have separate modules. Library lookups and supplied content live
with the library types. Token redaction belongs to configuration persistence;
diagnostic formatting also redacts root and worker credentials.
`catalog.rs` validates and serializes every library entry before mutation.
`transaction.rs` owns the private undo journal beside the main config, rollback, and
recovery on typed load/save. The journal commits when removed; an interrupted save
restores the previous main document and catalog before another load or save.
Callers must serialize writers and recovery through the configuration gate.
The journal contains old credentials and is protected by sandbox path guards.
Runtime DNS resolution belongs to `sandbox/network.rs`, temporary project paths to
`paths.rs`, and tmux socket identity and history limits to `tmux/mod.rs`.
`session/manager/config/mod.rs` serializes runtime changes and publication;
`manager/lifecycle/reconcile.rs` applies accepted configuration to live sessions.
`manager/config/cache.rs` coordinates cache links across projects and worktrees, tracking
added and removed links for rollback if reconciliation or saving fails. Rollback errors retain
the original cause. Save acknowledgements sample revisions before verifying accepted contents;
document saves acknowledge only the main TOML revision. Patch and replacement share document preparation
and commit helpers; callers retain the persistence gate until publication and endpoint updates finish.
Startup lives in `manager/init.rs`, and polling in `manager/maintenance.rs`.
`manager/config/removal.rs` deletes one session from an accepted TOML document under
the persistence gate. It saves before changing memory and leaves library stamps and
unrelated catalogs untouched. Recovery or a stale/unavailable document falls back to
the generic transaction. Deletion cannot introduce new worktree or mount references.
The mod owns profile preferences that remain available offline.
The mod uses daemon APIs and reads `endpoint.toml` for connection credentials.
It must not read or rewrite daemon TOML directly. Locations and overrides: [paths](ops-paths.md).

Personal agent templates are a separate daemon-owned `agent_templates/` store beside
the main config. `session/agent_templates/mod.rs` defines its types and snapshot rules.
The manager loads the store at startup and serializes mutations atomically. Templates retain no parent
agent or project metadata. Library items are one file per kind in `prompts/`, `breadcrumbs/`,
`file_actions/`, and `shell_scripts/`.
Each library item must set its `link` explicitly to `project`, `temp`, or `ask`.
Sandboxes, apps, and jukebox stations use `sandbox_presets/`, `app_presets/`, and `jukebox/`, respectively.

Config patches deep-merge the original TOML document, preserving omitted and unknown fields.
Typed `Config::save` serializes modeled fields and preserves unrelated document fields when
editing an existing file.
Whole-config identity comparisons and named TOML array preservation build temporary
indexes once per operation. Preserve first-match lookup semantics; per-row linear
searches make a single worker deletion quadratic in the session count.
The redacted-token sentinel means keep the stored token.
An empty token means replace the stored token with an empty value. Never round-trip the endpoint secret through a settings draft.

Projects own directories, temporary-project behavior, and shared mounts. Independent worktree
records are in `worktrees.toml` beside the configuration.
[Worktree ownership](daemon-worktrees.md) describes project IDs, session selection, and manual removal. Mount rows store
`from`/`to` paths and modes, read when each agent starts. Cache mode permits a blank
source for managed project storage.
Other sources remain literal host paths.
The project shortcut copies paths once. It does not rebuild a running sandbox.

Agents own command and optional `args`, sandbox additions, network, DNS, resource limits, and startup/private-state
behavior. Network defaults to `private`. DNS `resolved` follows the resolver seen by the daemon.
`config/resolution.rs` appends nonblank `args` to the resolved command, including a `cmd` override.
An unavailable command stays unavailable even when arguments are supplied.
An unset limit means no limit. DNS and resource-limit tables reject unknown keys;
resolved DNS rejects nonempty server lists (the wire format includes empty lists).
Unknown fields elsewhere retain the configuration preservation contract.

At startup, the daemon resolves `agent_shell` to an absolute executable path and sets the sandbox
`SHELL` variable. This default is separate from the host shell preset for errands.

`GET /api/config` is also the client read model for daemon policy: it returns effective values
and a response-only factory-default snapshot, usage catalog, temporary-root preview policy and
terminal limits. The mod must use those values for field initialization/reset and never recreate
them from generated constants. Missing metadata makes reset and policy-preview controls unavailable.
It does not define a new default.

`session/template.rs` scans placeholders and preserves unresolved text.
`session/prompt.rs` supplies prompt and breadcrumb values, including cycling through
tips selected by the client. Replacement text is inserted literally, without another scan.

Library breadcrumbs are independent reusable content.
Users insert them manually.
The daemon does not select or insert them automatically at agent startup.

See [Settings behavior](ui-settings.md) for draft and save ownership, and
[sandbox isolation](sandbox-isolation.md) for private-state boundaries.

`config/resolution.rs` owns effective launch resolvers and supplies editor settings previews through `/api/settings/preview`,
using the same scalar and dependency resolvers as startup. The response labels direct agent
settings, project mounts, and captured dependencies and describes the next start, not an
existing process's actual sandbox. Snapshot metadata for editor pickers stays on this root-only
boundary rather than session broadcasts.

Config and template loads read the current schema without rewriting files. API writes validate
current request fields.
When the main config is missing, load existing library files before creating its default.
Typed saves reject unreadable or malformed existing config instead of replacing it.
Library replacement and main-document writes share a recoverable transaction.
Failed commits restore original file bytes; failed restoration retains the journal
for retry and reports the recovery error.
User-level catalog definitions remain editable through their owning API.

Configuration and personal-template stores use `paths::write_atomic_async` for owner-only
`0600` replacement without blocking Tokio workers. Preset catalog writes use the synchronous
`paths::write_atomic` helper and retain their existing umask-controlled permissions.
Both writers exclusively create unique sibling files; private writes set `0600` before
content is written. Async writes flush before rename to catch background write errors.
Interrupted saves may leave siblings, but leftovers neither block retries nor get overwritten.
Preset HTTP mutations run that synchronous work on a blocking executor.
`paths.rs` shares sorted directory discovery for presets and jukebox; each catalog
keeps its extension matching, startup/reload error policy, and definition validation.
`presets/edit.rs` holds the catalog writer guard from fresh disk reads through validation
and commit; dependency validation does not use the cached runtime table.
Jukebox edits preserve legacy IDs at existing paths; new IDs require safe filenames.
Deleting a station ID removes all matching files, including shadowed definitions.
Both helpers remove their temporary file on write, permission, or rename failure.
Endpoint descriptors use the same private atomic writer, so token bytes never appear in a
temporary file with default permissions. Raw editor text is parsed before token redaction;
invalid TOML produces an error rather than exposing unredacted text.

Preset responses intentionally keep an explicit `source` projection. The current response shape
is a public contract, so automatically flattening future serialized fields would broaden it
without an explicit API decision.
Do not make this change.

Unknown-field preservation compares the original document with its old typed representation.
Serialization omits known fields to clear them. Do not restore these fields as extensions.
