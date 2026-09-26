# Projects and sessions

Projects supply working directories and shared mounts. Sessions add command, sandbox additions,
network/DNS, limits, private identity and startup behavior. Start in `config/mod.rs` for resolution
and `manager/sessions.rs` for mutations.
[Configuration stores](daemon-config-stores.md) explains effective and saved values.

A named command preset contributes tool-state access. An explicit `cmd` without a named
`command` does not. Do not infer credential mounts from executable text.

Temporary projects have daemon-owned directories and lifetimes. The daemon fixes temporary mode
when it creates a project. Update a project's sessions in the same write when you rename it.
The daemon refuses to delete a project with agents.

Mount rows store literal host paths and sandbox destinations.
Relative destinations resolve under the project's directory at startup. The primary project bind
is implicit but may be represented by a same-source/same-destination row to select read-only
access. The editor's project shortcut copies current paths once. Mount edits apply at the next start.
Running sandboxes do not change.
