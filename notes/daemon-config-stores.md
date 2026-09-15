# Configuration ownership

The daemon owns machine configuration; the mod owns offline-capable profile preferences.
The mod talks to daemon APIs and reads `endpoint.toml` for connection credentials; it must
not read or rewrite daemon TOML directly. Locations and overrides: [paths](ops-paths.md).

Personal agent templates are a separate daemon-owned `agent-templates.toml` store beside
the main config. `session/agent_templates.rs` owns its typed definition and snapshot rules;
the manager loads it at startup and serializes mutations atomically. Template origin is
display metadata only, never a live project or checkout relationship.

Config patches deep-merge the original TOML document, preserving omitted and unknown fields.
Typed `Config::save` serializes modeled fields only. Preserve this distinction when adding
editors. The redacted-token sentinel means keep the stored token; empty means replace it
with empty. Never round-trip the endpoint secret through a settings draft.

Project network/DNS values are defaults, not upper bounds. Session overrides may widen
network access. The wire exposes effective values separately from nullable overrides;
editors must save the override. Changes apply at next agent start. Omitted DNS follows the
daemon's resolver, including the container resolver in sidecar mode.

See [Settings behavior](ui-settings.md) for draft and save ownership, and
[sandbox isolation](sandbox-isolation.md) for private-state boundaries.
