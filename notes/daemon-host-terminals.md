# Host terminal tabs

Host shells from project headings are persistent sidebar tabs. The daemon writes one
`[[host_terminal]]` record per project/shell tab in `config.toml`.
The record stores the stable tmux name, project grouping, last observed working directory, and autostart policy. A missing `autostart` field means true.

The live tmux session also carries `@slopworld_host_project` and
`@slopworld_host_path`. After redeployment, the daemon uses these options to adopt the existing shell with its grouping and working directory.
After a machine reboot, it uses the configuration record to recreate the shell. `retick` polls the pane cwd and updates the record, so a later
restart starts where the shell was left.

Sidebar title rendering restores the host working directory from persistent `Dir` when tmux truncates the path title to its width.
It preserves command and application titles.

Host tabs have no colonist or sandboxed agent configuration.
Start, stop, terminal, Label, and Remove actions remain available.
Agent edit and duplicate actions do not apply to host tabs. Label stores a fixed title in the host-terminal record.
Clearing it restores the terminal application's title. Stopping a host tab
kills its pane but keeps the tab and its saved path. Remove kills the pane and
removes the durable tab record.
