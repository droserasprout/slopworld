# Where things land

- Daemon config: `~/.config/slopworld/config.toml`, seeded on first run.
- User presets: `~/.config/slopworld/presets/*.toml` (`SLOPD_PRESETS` overrides).
- Profile: `$XDG_DATA_HOME/slopworld/profile`. Saves, screenshots, `Config/`.
- Session state: `$XDG_DATA_HOME/slopworld/sessions/<session>/` (`SLOPD_STATE`
  overrides). The copy of `~/.claude` and the like that one agent gets to
  itself; deleting one hands that session a fresh start. See
  [sandbox-isolation](sandbox-isolation.md).
- Mod settings: `Config/Mod_SlopWorld_SlopWorldMod.xml` inside the profile.
- Game log: `~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log`.
  Harmony and mod exceptions land there, not in the launching terminal. Unity does
  not know about the profile, so a second profile would share this log.
- tmux server: private socket `slopworld` (`tmux -L slopworld`).

See [profile](profile.md) for what makes the profile folder.
