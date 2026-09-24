# Private-network daemon access

Status: human approved
Approved by: user (requested landing on `main`)
Revision: bfec8656

Private-network sessions resolve `127.0.0.1` inside their own namespace. The
existing worker `--map-host-loopback 127.0.0.1` option does not make the host's
daemon listener available there. Worktree management through `slopctl` needs a
reachable listener and a scoped credential.

Completion criteria:

- Forward only the daemon TCP port to private sessions using Pasta.
- Keep root endpoint credentials out of ordinary agent and worker sandboxes.
- Preserve worker scoped grants and document how agents receive grants.
- Verify the launch arguments and a live private-network request to `slopd`.

Launch-argument tests pass. From a restarted private-network agent,
`slopctl status` reached the daemon and `slopctl worktree list --project slopworld`
showed the agent attached to its managed worktree.
