# Configuration ownership

The daemon owns machine configuration; the mod owns offline-capable profile preferences.
The mod talks to daemon APIs and reads `endpoint.toml` for connection credentials; it must
not read or rewrite daemon TOML directly. Locations and overrides: [paths](ops-paths.md).

Personal agent templates are a separate daemon-owned `agent-templates.toml` store beside
the main config. `session/agent_templates.rs` owns its typed definition and snapshot rules;
the manager loads it at startup and serializes mutations atomically. Personal template origin is display metadata only. Repository Library definitions are read
from registered checkouts; see [Library ownership](daemon-library.md).

Config patches deep-merge the original TOML document, preserving omitted and unknown fields.
Typed `Config::save` serializes modeled fields and preserves unrelated document fields when
editing an existing file. The redacted-token sentinel means keep the stored token; empty means
replace it with empty. Never round-trip the endpoint secret through a settings draft.

Projects own directories, temporary-project behavior, and shared mounts. Mount rows store
literal `from`/`to` paths and modes, read when each agent starts. The project shortcut copies paths once; a running sandbox is not rebuilt. Agents own command, sandbox additions,
network, DNS, resource limits, and startup/private-state behavior. Network defaults to private,
DNS `resolved` follows the current daemon/container resolver, and an unset limit means no cap.
The daemon default `agent_shell` is resolved to an absolute executable path at each agent start and
emitted as sandbox `SHELL`; it is separate from the shell preset used by host shell errands.

`GET /api/config` is also the client read model for daemon policy: it returns effective values
and a response-only factory-default snapshot, usage catalog, temporary-root preview policy and
terminal limits. The mod must use those values for field initialization/reset and never recreate
them from generated constants. Missing metadata leaves reset and policy-preview controls
unavailable; it is not an authoritative new default.

Library breadcrumbs are independent reusable content and are inserted manually; they are not
selected or injected automatically at agent startup.

See [Settings behavior](ui-settings.md) for draft and save ownership, and
[sandbox isolation](sandbox-isolation.md) for private-state boundaries.

`config/resolution.rs` supplies editor settings previews through `/api/settings/preview`,
using the same scalar and dependency resolvers as launch. The response labels direct agent
settings, project mounts, and captured dependencies and describes the next start, not an
existing process's actual sandbox. Snapshot metadata for editor pickers stays on this root-only
boundary rather than session broadcasts.

Config and template loads read the current schema without rewriting files. API writes validate
current request fields; repository definitions remain read-only.

Configuration and personal-template stores use `paths::write_atomic_async` for owner-only
`0600` replacement without blocking Tokio workers. Preset catalog writes use the synchronous
`paths::write_atomic` helper and retain their existing umask-controlled permissions; preset
HTTP mutations run that synchronous work on a blocking executor. Both helpers remove their
temporary file on write, permission, or rename failure.

Preset responses intentionally keep an explicit `source` projection. The current response shape
is a public contract, so automatically flattening future serialized fields would broaden it
without an explicit API decision; this cleanup remains declined.

Unknown-field preservation compares the original document with its old typed representation;
known fields omitted by serialization are clears, not extensions to restore.
