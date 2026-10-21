# Sandbox boundaries

Within `slopd/src/`, `sandbox/bind/` builds mounts, `sandbox/paths.rs` guards paths,
and `sandbox/state/` owns private storage. `application/stored_state.rs` coordinates
inventory/trash deletion; session owns guarded reset/restore and live reconciliation.
[Presets](daemon-presets.md) select access; [grants](agent-grants.md) separately select
API authority. User guidance belongs to the [security model](../docs/src/sandbox/security.md).

Bubblewrap retains the daemon's numeric UID/GID so passwd-based home lookups match
mounted user files. Host root stays unmapped for non-root daemons. System SSH includes
still need private copies for OpenSSH ownership checks.

Protected paths and unresolved aliases fail closed. Invalid project roots or selected
presets stop launch rather than changing the workspace. Expanded preset paths must
be absolute because validation and Bubblewrap use different working directories.
Private overlays hide earlier host binds; aliases to protected originals reject launch.
Restore the PID namespace's `/proc` after overlays. Preserve mount precedence:
preset read-only, read-write, devices; then project mounts, persistent `/tmp`,
capabilities, private/shared files, and the private-network resolver. Image imports
mount last and read-only; see [clipboard](daemon-clipboard.md).
Persistent `/tmp` reapplies preset descendant binds before capabilities and private
overlays so host display and agent sockets remain reachable.
The reserved .NET CLI home receives aliases of selected NuGet mounts after its
private overlay; sources overlapping private preset roots are rejected.

Private state follows opaque identity, not names. `storage_id.rs` owns the format;
`session/identity.rs` allocates under the session boundary, accounting for live/saved
references and retained state. Unreadable trash metadata cannot free an identity.
Rename/restore preserve identity. Stop retains state; reset/delete move it to trash;
temporary errands clean it up. Persistent `/tmp` shares that lifetime.

Private roots must not nest, even across presets. Use one root with seed entries.
Validation and exclusions share resolved paths. Seeding and trash measurement must
not follow symlinks. Shared-file mounts allow in-place writes but block unlinking;
recursive removal can damage private siblings before failing on a shared credential.
Restart does not reseed an existing copy; repair or reset is required.

Command strings use POSIX word parsing without expansion. Network/DNS and optional
process-tree limits apply at launch; requested limits must be enforced or launch fails.
Agent launches have no general seccomp policy or disk quota. Host inspection uses
the separate [Git boundary](daemon-git.md). Private networking exposes only the daemon
port for an IPv4 loopback/unspecified listener, not arbitrary host loopback services.

The debug preset deliberately exposes host control. Read-only credentials still grant
full credential authority. Its opt-in `daemon_config` mounts existing config/endpoint
files, never parent catalogs, and is withheld from workers.

Inspection compares the saved executable with an observable pane process tree; it
does not verify every launch-plan field. Without usable evidence, comparison is
unavailable. Serialized plans remain redacted and survive exit/restart. Cache/checkouts
belong to [worktrees](daemon-worktrees.md).
