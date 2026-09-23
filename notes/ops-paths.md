# Runtime paths

The [paths reference](../docs/src/reference/paths.md) owns file locations,
environment overrides, permissions, and the private tmux socket name.

Developer constraints:

- State ages in existing tmux sessions are authoritative.
  `session-activity.toml` is a fallback. See [session state](daemon-session-state.md).
- Agent state uses opaque IDs so renames and name reuse cannot inherit another
  agent's storage. See [sandbox isolation](sandbox-isolation.md).
- `launch-plan.json` stores sanitized sandbox launch data directly under each state ID.
  It uses mode `0600`. Reset and delete move it into 14-day trash with the rest of the state.
  Temporary sessions delete it during cleanup. The daemon never mounts it in the guest.
  `slopctl sandbox inspect NAME` compares it with live tmux panes when they exist.
- Profiles share Unity's game log. The launcher does not redirect it.
  See [profile](ops-profile.md).
