# Sandbox boundaries

Source paths below are relative to `slopd/src/`. `sandbox/bind/` owns launch/mount
construction; `sandbox/paths.rs` owns guards; `sandbox/state/` owns private storage
and inventory; `sandbox/seed.rs` owns initialization; `sandbox/network.rs` owns DNS.
Presets select access, while [grants](agent-grants.md) independently select API authority.

Protected paths and unresolved safety aliases fail closed. An invalid project root
stops launch rather than silently changing the workspace. Private overlays must hide
host originals from earlier binds, and the PID namespace's `/proc` is restored after
all overlays. Invalid selected presets reject launch and settings preview.

Private state uses daemon-assigned opaque IDs preserved through rename. Configured
identities accept the shared 16-character lowercase hexadecimal format and canonical
legacy UUIDs; validation belongs to `storage_id.rs`. New configured agents and workers
allocate through `session/manager/identity.rs` under the session boundary. Allocation
checks configured/live identities, task participants, record destinations, and retained
private state; unreadable trash metadata rejects allocation rather than making an
identity available. Edits and restores retain their existing identity. Stopped agents retain their state;
reset/delete move it to trash, while temporary errands own
cleanup. Persistent `/tmp` follows that state lifetime. Seeding must not follow source
symlinks, and trash inventory measures links without following them. Shared-file
mounts permit in-place host writes but block unlinking the mountpoint. Recursive
removal can delete private siblings before failing on a shared credential; restart
does not reseed an existing copy. Repair or reset is required. User-facing guidance
belongs to the [security model](../docs/src/reference/security.md).

Network/DNS and optional resource limits apply at launch. Requested limits cover the
process tree and must be enforced or fail launch. Agent launches have no general
seccomp policy or disk quota; host Git inspection is a separate
[Git boundary](daemon-git.md). Private networking maps only the daemon port for an
IPv4 loopback/unspecified listener, not other host loopback services.

Mount configuration belongs to [projects](daemon-projects.md), cache/checkouts to
[worktrees](daemon-worktrees.md), selection/host escapes to [presets](daemon-presets.md),
and inspection operations to [Using slopctl](../docs/src/guides/slopctl.md).
The debug preset deliberately exposes host control, including a readable daemon root
token; read-only credential mounts do not restrict credential use.

Preset binds are grouped read-only, read-write, then devices. Project mounts
precede persistent `/tmp`, capability mounts, private copies, shared files, and
the private-network resolver. Read-write binds win over read-only duplicates;
private and shared overlays must preserve their intended precedence.

Live inspection compares the saved command executable with an observable pane
process tree, not every saved-plan field. Without a usable PID/tree or executable,
comparison is unavailable. Saved sanitized plans survive process exit and restart.

A personal debug-preset override that binds host `/proc` can break PID-namespace
self lookup and hang nested Bubblewrap. Keep `/proc` private and remove such binds
before relaunching.
