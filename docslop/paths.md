# Where things land

- Daemon config: `~/.config/slopworld/config.toml`, seeded on first run.
- Prompt-summary cache: `~/.config/slopworld/prompt-summaries.toml` (beside `SLOPD_CONFIG` when
  overridden), daemon-owned and mode `0600`.
- Daemon endpoint: `~/.config/slopworld/endpoint.toml` (`SLOPD_ENDPOINT` overrides),
  written while slopd is running with the effective URL and token, mode `0600`.
- User presets: `~/.config/slopworld/presets/*.toml` (`SLOPD_PRESETS` overrides).
- Task mailbox: `~/.config/slopworld/tasks.toml` (beside `SLOPD_CONFIG`).
- User jukebox definitions: `~/.config/slopworld/jukebox/*.toml` (`XDG_CONFIG_HOME`
  or `SLOPD_JUKEBOX` overrides). The shipped definitions are compiled into `slopd` from
  `slopd/jukebox/`.
- Jukebox likes: `~/.local/share/slopworld/jukebox.toml` (`XDG_DATA_HOME` overrides),
  containing `[[like]]` tables.
- Profile: `$XDG_DATA_HOME/slopworld/profile`. Saves, screenshots, `Config/`.
- Session state: `$XDG_DATA_HOME/slopworld/sessions/<state-id>/` (`SLOPD_STATE`
  overrides). The daemon assigns the opaque state id when an agent is created,
  so a rename or name reuse cannot inherit another agent's tool state. Deleted
  or reset state moves under `sessions/.trash/` for 14 days before reclamation.
  See [sandbox-isolation](sandbox-isolation.md).
- Mod settings: `Config/SlopWorld.toml` inside the profile.
- Game log: `~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log`.
  Harmony and mod exceptions land there, not in the launching terminal. Unity does
  not know about the profile, so a second profile would share this log.
- tmux server: private socket `slopworld` (`tmux -L slopworld`).

See [profile](profile.md) for what makes the profile folder.
