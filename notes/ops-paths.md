# Runtime paths

The [paths reference](../docs/src/reference/paths.md) owns file locations,
environment overrides, permissions, and the private tmux socket name.

Developer constraints:

- State ages in surviving tmux sessions are authoritative;
  `session-activity.toml` is a fallback. See [session state](daemon-session-state.md).
- Agent state uses opaque IDs so renames and name reuse cannot inherit another
  agent's storage. See [sandbox isolation](sandbox-isolation.md).
- Unity's game log is shared across profiles; the launcher does not redirect it.
  See [profile](ops-profile.md).
