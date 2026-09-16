# Configuration ownership

The daemon owns machine configuration; the mod owns offline-capable profile preferences.
The mod talks to daemon APIs and reads `endpoint.toml` for connection credentials; it must
not read or rewrite daemon TOML directly. Locations and overrides: [paths](ops-paths.md).

Personal agent templates are a separate daemon-owned `agent-templates.toml` store beside
the main config. `session/agent_templates.rs` owns its typed definition and snapshot rules;
the manager loads it at startup and serializes mutations atomically. Personal template origin is display metadata only. Repository Library definitions are read
from registered checkouts; see [Library ownership](daemon-library.md).

Config patches deep-merge the original TOML document, preserving omitted and unknown fields.
Typed `Config::save` serializes modeled fields only. Preserve this distinction when adding
editors. The redacted-token sentinel means keep the stored token; empty means replace it
with empty. Never round-trip the endpoint secret through a settings draft.

Project network/DNS values are defaults, not upper bounds. Session overrides may widen
network access. The wire exposes effective values separately from nullable overrides;
editors must save the override. Changes apply at next agent start. Omitted DNS follows the
daemon's resolver, including the container resolver in sidecar mode.

`GET /api/config` is also the client read model for daemon policy: it returns effective values
and a response-only factory-default snapshot, usage catalog, temporary-root preview policy and
terminal limits. The mod must use those values for field initialization/reset and never recreate
them from generated constants. Missing metadata leaves reset and policy-preview controls
unavailable; it is not an authoritative new default.

Instruction and discovery-breadcrumb previews are rendered by the daemon from unsaved values.
An omitted breadcrumb uses the saved template; an explicitly empty draft remains empty.

See [Settings behavior](ui-settings.md) for draft and save ownership, and
[sandbox isolation](sandbox-isolation.md) for private-state boundaries.

`config/resolution.rs` supplies editor settings previews through `/api/settings/preview`,
using the same scalar and dependency resolvers as launch. The response labels contribution
sources and describes the next start, not an existing process's actual sandbox. Snapshot
metadata for editor pickers stays on this root-only boundary rather than session broadcasts.
