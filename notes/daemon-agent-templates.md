# Daemon agent templates

`application::AgentTemplates` owns catalog loading, mutation serialization, and
cancellation-safe persistence/publication. Launches receive a read-only accepted
catalog handle: committed edits appear through it and failed saves stay invisible.
Application coordinates capture/instantiation and guarded draft registration;
`agent_templates/` owns portable definitions, snapshots, and storage.
Clients use APIs; locations belong to [configuration stores](daemon-config-stores.md).

Templates copy launch settings and explicit dependency snapshots once, with no source
agent/project relationship. Identity, credentials, runtime state, and project context
are excluded; the destination supplies mounts. Invalid selected sandbox references
reject capture/launch. Later template/preset edits do not rewrite existing instances.

Instantiation allocates fresh private state and preserves snapshots. Ordinary creation
clears snapshots; edits/previews/overrides preserve only still-selected snapshots
through `config/resolution.rs`. Explicit dependencies are captured; the implicit global
preset remains live. See [presets](daemon-presets.md).

Daemon-assigned versions reject stale edit/delete requests. Create/duplicate require
an absent destination; deletion leaves instances intact. The committed index selects
one generation directory. Without an index the catalog is empty, including after an
interrupted first save; loading never scans direct files.

See [API](../docs/src/reference/api.md#agent-templates), [worker visibility](daemon-workers.md),
[mod editors](mod-agent-templates.md), and
[Configuring agents](../docs/src/agents/configuring-agents.md).
