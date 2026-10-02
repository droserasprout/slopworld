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
stopped agents retain it; reset/delete move it to trash, while temporary errands own
cleanup. Persistent `/tmp` follows that state lifetime. Seeding must not follow source
symlinks, and trash inventory measures links without following them. Shared-file
write/delete consequences belong to [credential guidance](../docs/src/guides/configuring-sandboxes.md#credentials).

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
