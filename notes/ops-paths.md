# Runtime paths

The [paths reference](../docs/src/reference/paths.md) owns locations, overrides,
permissions, and the private tmux socket name.

`shared/application_paths.rs` resolves independent application config/data/cache
roots for the daemon, CLI, launcher, and host slopcar. Home-relative overrides
expand in the resolver before any consumer receives them. Sidecar mode derives host
config, daemon data, profile, and game-log paths below those roots; container
mount targets remain fixed. Read-only `paths` commands expose the resolver to
recipes without requiring a daemon, game, or Docker. `paths.rs` supplies daemon test overrides
and canonical comparison of paths with missing suffixes; sandbox guards reuse
that comparison so existing symlink aliases fail closed.

Settings, catalogs, endpoint discovery, client selection, and the saved game
directory follow the config root. Client resolution reads `client.toml`; daemon
descriptor publication does not. Automatic profiles follow saved peer names. Private session state, default game profiles, and mod-owned jukebox
likes follow the data root. Explicit endpoint and profile overrides take precedence.
The launcher and CLI share the game-log resolver: `<CACHE>/Player.log`, overridden
by `SLOPWORLD_GAME_LOG`. The launcher passes absolute resolved roots to the game;
`Bootstrap/ApplicationPaths.cs` owns matching mod resolution.

Behavior owners are [session state](daemon-session-state.md) for recovered activity,
[sandbox isolation](sandbox-isolation.md) for private identity, launch plans, and
cleanup, and [profiles](ops-profile.md) for launcher/profile behavior.
