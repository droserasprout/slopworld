# Projects and sessions

Projects supply working directories and shared mounts. Sessions add command, sandbox additions,
network/DNS, limits, private identity and startup behavior. Start in `config.rs` for resolution
and `manager/sessions.rs` for mutations; [configuration stores](daemon-config-stores.md) covers
effective versus saved values.

A named command preset contributes tool-state access. An explicit `cmd` without a named
`command` does not. Do not infer credential mounts from executable text.

Temporary projects have daemon-owned directories and lifetimes. Project rename must carry
its sessions in the same write; deleting a project with agents is refused. Mount rows store
literal host/guest paths, so project rename/delete does not update them. The editor's project
shortcut copies current paths once. Mount edits apply at next start; running sandboxes stay intact.

The optional generated `SLOPWORLD.md` is a project-scoped snapshot shared by agents, not a
per-agent private file or an authoritative project instruction. See [isolation](sandbox-isolation.md)
for mount protection and [task discovery](agent-task-discovery.md) for prompt delivery.
