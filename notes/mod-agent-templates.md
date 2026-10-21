# Mod agent templates

`HubCatalog` and `SessionHub` own the daemon-backed catalog; `EditSessionDialog` shares
the agent/template form. The daemon owns persistence, snapshots, instantiation, and
effective previews. Templates never live in profile settings.

Pickers prefer captured dependencies, even when absent from current catalogs. Project
mounts and identity remain contextual. Capture uses saved agent configuration, not
unsaved editor fields; workers cannot be captured. Failed saves retain drafts.

`DaemonSettingsPreview` invalidates on draft, catalog, or connection changes. Catalog
GET revisions reject stale replies; there is no pushed template-snapshot handler.
See [daemon templates](daemon-agent-templates.md) for contracts and
[Configuring agents](../docs/src/agents/configuring-agents.md) for editing.
