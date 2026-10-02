# Mod agent templates

`HubCatalog` and `SessionHub` own the daemon-backed template catalog boundary.
`EditSessionDialog` owns the shared agent/template form. The daemon owns persistence,
captured snapshots, instantiation, and effective preview resolution. RimWorld profile
settings never store templates.

Templates carry portable settings and captured command/sandbox dependencies; project
mounts and identity remain contextual. Snapshot-aware pickers prefer captured
sources. Naming capture uses the agent's saved configuration, not unsaved editor
changes. Failed saves retain drafts, and workers cannot be captured.

Daemon contracts belong to [templates](daemon-agent-templates.md); catalog and
preview lifetimes belong to [the client](mod-client.md). Menu/editor workflows and
blank-as-unlimited resource fields belong to
[Configuring agents](../docs/src/guides/configuring-agents.md).

The `+ > Agent` menu chooses a template or Custom before opening the editor.
`SessionHub.CreateFromTemplate` submits the selected template and overrides.
`DaemonSettingsPreview` retains a response for a draft and invalidates it on draft,
catalog, or connection changes. Template GET revisions reject stale replies; there
is no pushed template-snapshot handler.
