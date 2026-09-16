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

Network, DNS, and resource limits are direct agent settings. Private DNS must follow the
actual host/container resolver. Resource limits wrap the full process tree and must either
be enforced or fail launch. They are optional; there is no disk quota or general seccomp
policy. Project mounts are literal source/destination paths applied at each start with their saved
access mode. Validate both paths; missing sources fail launch. Workspace binds precede private
state and DNS overlays, and source aliases cannot expose effective private preset roots. Writable
project `.git` intentionally permits hook/config changes.

Host Git inspection uses `git.rs` and `git_exec.rs`: known helpers are disabled, and Linux
seccomp blocks child processes while allowing index threads. This also blocks clean/process
filters without racing repository configuration edits. Required filters or unavailable seccomp
can make inspection fail; repository and global configuration remain writable and unchanged.

`slopworld-debug` deliberately exposes host control. Consult its actual preset before making
claims about process, token, Docker or desktop isolation.

Launch inspection treats live argv (including process titles and option names) as untrusted;
only fixed diagnostic flags survive redaction. Cgroup expansion requires the saved per-launch
scope identity and a pane descendant in that scope. Older plans use ancestry alone, never the
shared tmux cgroup, to avoid reporting unrelated sessions.
