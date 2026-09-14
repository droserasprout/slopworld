# Sandbox boundaries

Start in `sandbox/bind.rs` for path guards and `sandbox/state.rs` for private storage.
Presets define access; [scoped grants](agent-grants.md) independently define API authority.
Host escapes are explicit exceptions, not proof that the filesystem sandbox failed.

Protected paths and their ancestors must be rejected from ordinary binds, including config,
endpoint, preset and state roots. Private overlays apply last so another bind cannot recover
the host original. An invalid project root aborts launch; silently dropping it would change
what the agent edits.

Private state uses daemon-owned opaque IDs, surviving display-name rename without name-reuse
collisions. Down configured agents are not age-pruned. Reset/delete moves state into trash;
ephemeral errands own cleanup. Persistent `/tmp` follows that same state lifetime.

Shared credential files are writable host inodes overlaid on private state. Mountpoints
resist deletion but not truncation/overwrite; atomic replacement can fail with EBUSY.
See [blast radius](sandbox-blast-radius.md).

Generated project manifests are shared snapshots. Protect both project-root and configured
mount spellings, including aliases; never overwrite a user-authored manifest or ignore rule.
Disabling generation does not remove existing mounts until restart.

Project network policy is a default that agents may override. Private DNS must follow the
actual host/container resolver. Resource limits wrap the full process tree and must either
be enforced or fail launch. They are optional; there is no disk quota or general seccomp
policy. Writable project `.git` intentionally permits hook/config changes.

`slopworld-debug` deliberately exposes host control. Consult its actual preset before making
claims about process, token, Docker or desktop isolation.
