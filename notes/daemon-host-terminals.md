# Host terminal tabs

Host shells from project headings are persistent sidebar tabs. The daemon writes one
`host_shells/<id>.toml` record per project/shell tab under the data root.
These tabs are separate from agent sessions and have no colonist or sandboxed agent
configuration. Confirmed host-pane exits are logged to the daemon journal; they
do not allocate private sandbox storage for exit evidence.
[Temporary host errands](daemon-library.md) have a different lifecycle.

The saved record retains terminal identity, project grouping, last observed working
directory, and autostart policy. A missing `autostart` field means true.
New shells receive a stable 16-character lowercase hexadecimal `id`, allocated by
`storage_id.rs` under the configuration mutation guard. Edits and renames retain it.
Stored IDs must be valid and unique within the host-shell collection; loading
never allocates one.
Tmux metadata lets the daemon adopt a surviving shell after redeployment. If tmux
state is lost, configuration restores the tab; a missing pane starts automatically
only when autostart is enabled. Otherwise, the tab returns as a stopped row.
Periodic working-directory polling updates saved tabs so their next launch uses
the last observed path.

Persistence belongs to [daemon configuration](daemon-config-stores.md); recovery
belongs to `session/manager/lifecycle/`, mapped in [daemon sources](daemon-files.md).
Title rendering belongs to the [sidebar](mod-sidebar.md). User actions are described
in [Host shells](../docs/src/terminals/host-shells.md).
