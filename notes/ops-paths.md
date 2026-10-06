# Runtime paths

The [paths reference](../docs/src/reference/paths.md) owns locations, overrides,
permissions, and the private tmux socket name.

`paths.rs` resolves independent daemon config/data/cache roots and per-store override
precedence. It also owns canonical comparison of paths with missing suffixes;
sandbox guards reuse it so existing symlink aliases fail closed. Endpoint discovery
and launcher/game paths retain their separate contracts.

Behavior owners are [session state](daemon-session-state.md) for recovered activity,
[sandbox isolation](sandbox-isolation.md) for private identity, launch plans, and
cleanup, and [profiles](ops-profile.md) for launcher/profile behavior.
