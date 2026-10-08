# Daemon agent templates

`Manager` owns the personal template catalog. `session/agent_templates/mod.rs`
defines portable fields and snapshot rules; its persistence module owns storage.
The [configuration store](daemon-config-stores.md) owns the `agent_templates/`
location. The mod and CLI use daemon APIs, never direct file access.

Templates copy optional launch settings and explicit dependency snapshots once.
They retain no source-agent or source-project relationship. Project settings/mounts,
worktree selection, names, labels, state IDs, runtime state, worker hierarchy, and
credentials are excluded. The destination project supplies mounts at launch.
Invalid selected sandbox references reject capture and launch.

Template creation preserves captured snapshots and allocates fresh private state.
Ordinary creation clears snapshots; ordinary edits, settings previews, and template
overrides preserve only still-selected snapshots through `config/resolution.rs`.
Explicit dependencies are captured, while the implicit global launch preset stays
live. Later template or preset edits do not rewrite existing instances.
See [preset resolution](daemon-presets.md).

Definitions carry daemon-assigned versions. Stale edit/delete requests conflict;
create/duplicate require an absent destination. Deleting a template never deletes
instantiated agents. Route and request contracts belong to the
[API reference](../docs/src/reference/api.md#agent-templates), scoped visibility to
[workers](daemon-workers.md), and editor behavior to [mod templates](mod-agent-templates.md).
Defaults and user workflow belong to [Configuring agents](../docs/src/agents/configuring-agents.md).
