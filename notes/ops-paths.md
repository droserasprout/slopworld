# Runtime paths

The [paths reference](../docs/src/reference/paths.md) owns file locations,
environment overrides, permissions, and the private tmux socket name.

Developer constraints:

- State ages in surviving tmux sessions are authoritative;
  `session-activity.toml` is a fallback. See [session state](daemon-session-state.md).
- Agent state uses opaque IDs so renames and name reuse cannot inherit another
  agent's storage. See [sandbox isolation](sandbox-isolation.md).
- A sanitized `launch-plan.json` lives directly under each state ID, with mode `0600`.
  Reset and delete move it with the rest of the state into 14-day trash; ephemeral sessions
  remove it during cleanup. It is never mounted into the guest. `slopctl sandbox inspect NAME`
  compares the saved intent with the live tmux pane tree when one exists.
- Unity's game log is shared across profiles; the launcher does not redirect it.
  See [profile](ops-profile.md).
