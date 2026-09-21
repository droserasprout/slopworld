# Projects and sessions

Projects supply working directories and shared mounts. Sessions add command, sandbox additions,
network/DNS, limits, private identity and startup behavior. Start in `config.rs` for resolution
and `manager/sessions.rs` for mutations; [configuration stores](daemon-config-stores.md) covers
effective versus saved values.

A named command preset contributes tool-state access. An explicit `cmd` without a named
`command` does not. Do not infer credential mounts from executable text.

Temporary projects have daemon-owned directories and lifetimes, and temporary mode is fixed when
the project is created. Project rename must carry its sessions in the same write; deleting a
project with agents is refused. Mount rows store literal host paths and sandbox destinations;
relative destinations resolve under the project's directory at launch. The primary project bind
is implicit but may be represented by a same-source/same-destination row to select read-only
access. The editor's project shortcut copies current paths once. Mount edits apply at next start;
running sandboxes stay intact.
