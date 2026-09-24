# Sandbox boundaries

Start in `sandbox/bind.rs` for path guards and `sandbox/state.rs` for private storage.
Presets define access.
[Scoped grants](agent-grants.md) independently define API authority.
Host escapes are explicit exceptions, not proof that the filesystem sandbox failed.

The daemon must reject ordinary binds to protected paths and their ancestors, including config,
endpoint, preset and state roots. Apply private overlays last so another bind cannot recover
the host original. An invalid project root stops startup.
Silently removing it would change what the agent edits.

Private state uses opaque IDs that the daemon assigns.
Display-name changes preserve these IDs, so reused names do not cause collisions.
The daemon does not delete private state because of age for configured agents in the Down state.
Reset and delete move state into trash.
Temporary errands control their own cleanup. Persistent `/tmp` follows that same state lifetime.

Shared credential files are writable host inodes overlaid on private state. Mountpoints prevent deletion but permit truncation and overwrite.
Atomic replacement can fail with EBUSY.
See [blast radius](sandbox-blast-radius.md).

Network, DNS, and resource limits are direct agent settings. Private DNS must follow the
actual host/container resolver. Resource limits wrap the full process tree. When an agent
requests them, the daemon must enforce them or fail launch. Resource limits are optional.
Private networking forwards only the daemon TCP port to the host for the default
IPv4 listener. Scoped grants still decide API access. No other host loopback
service is mapped.
There is no disk quota or general seccomp policy. At launch, the daemon resolves the
agent-shell setting from its command preset or custom command to an absolute executable path.
It sets sandbox `SHELL` to that path.
A missing or non-executable selection causes the launch to fail. Project mounts are literal source/destination paths applied at each
start with their saved access mode. Check both paths.
Missing literal ro/rw sources cause the launch to fail. Cache mounts create missing directories and bind them writable across worktrees.
Blank cache sources use project storage outside checkouts.
Explicit sources remain literal paths. Workspace
binds precede private state and DNS overlays, and source aliases cannot expose effective private
preset roots, including aliases used as the primary project directory. A containing primary
workspace keeps private overlays at their original paths. Writable project `.git` intentionally permits hook/config changes.

Project mount sources remain absolute host paths.
Sandbox destinations may be relative to the expanded project directory.
The daemon resolves them before checking paths and constructing bwrap arguments. A same-path
mount row can override the primary project bind with read-only access. Host Git inspection uses
`git.rs` and `git_exec.rs`. The daemon disables known helpers. Linux
seccomp blocks child processes while allowing index threads. This also blocks clean/process
filters without racing repository configuration edits. Required filters or unavailable seccomp
can make inspection fail.
Repository and global configuration remain writable and unchanged.

`slopworld-debug` deliberately exposes host control. Consult its actual preset before making
claims about process, token, Docker or desktop isolation.

Launch inspection treats live argv, including process titles and option names, as untrusted.
Only fixed diagnostic flags remain after redaction. Cgroup expansion requires the saved per-launch
scope identity and a pane descendant in that scope. Older plans use ancestry alone, never the
shared tmux cgroup, to avoid reporting unrelated sessions.

Linked project worktrees mount only their Git metadata at its real paths. Worktree removal
runs Git in a minimal Bubblewrap namespace because Git creates a status helper process.
Allocation and inspection retain the host restriction on child processes. See [worktree ownership](daemon-worktrees.md).
