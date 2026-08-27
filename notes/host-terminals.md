# Host terminal tabs

Project-heading host shells are durable sidebar tabs. The daemon writes one
`[[host_terminal]]` record per project/shell tab in `config.toml`; it stores the
stable tmux name, project grouping, last observed working directory, and the
autostart policy. A missing `autostart` field means true.

The live tmux session also carries `@slopworld_host_project` and
`@slopworld_host_path`. Those options let a daemon redeploy adopt the existing
shell with its grouping and `cd`, while the config record recreates it after a
machine reboot. `retick` polls the pane cwd and updates the record, so a later
restart starts where the shell was left.

Host tabs deliberately remain ghost rows: they have no colonist or sandboxed
agent config. Start, stop, terminal and Remove actions remain available; agent
edit, label and duplicate actions are not valid for them. Stopping a host tab
kills its pane but keeps the tab and its saved path. Remove kills the pane and
deletes the durable tab record.
