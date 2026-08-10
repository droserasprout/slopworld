# Presets and the sandbox

`presets.rs` reads one TOML file per piece of software, each stating a
`[[sandbox]]` preset, a `[[command]]` preset, or both under one name. Builtins
are `include_str!` of `slopd/presets/*.toml`, so they can never be older than the
binary reading them; user files are `~/.config/slopworld/presets/*.toml`
(`SLOPD_PRESETS` points elsewhere) and replace a builtin **by entry name, in
place**, so the GUI never draws two of one.

`global.toml` is the implicit system preset. It is added to every sandbox before
the command, project and session presets; it is not a project checkbox. Copying
it to the user list creates the machine-wide `global` override, which is edited
and reset like any other system preset.

- The directory is re-read when its newest mtime moves, on the same two-second
  check `config.toml` is (`reload_presets_if_changed`), and the sessions are
  re-announced because what an agent runs may have just changed under it.
- `category` is free text: an unknown one is a heading in the GUI, not an error.
  `escapes` is non-empty on a preset that hands the sandbox a way back out and is
  what the GUI draws in `Warn`.
- `requires` names sandbox presets that must be included before this one; it is a
  dependency closure, cycle-safe in the daemon, and its implied boxes are shown
  disabled in the mod. `systemd` requires `dbus`; language caches likewise require
  their read-only toolchain preset (`python-cache` requires `python`, for example).
- `GET /api/presets` returns `source` as `system`, `user` or `override`, plus the
  complete effective definition. The settings page separates system entries from
  user entries; a user entry with the same name as a builtin is an override.
- Root-only `POST /api/presets/:kind/:name/copy`, `PUT` and `DELETE` manage user
  definitions for `sandbox` and `command`. Copying without a new name creates an
  override; saving is daemon-owned TOML, validated and atomically replaced. Deleting
  an override reveals the builtin again. A user-only sandbox cannot be deleted while
  a command still requires it.
- A `[[sandbox]]` states six kinds of path: `ro`, `rw`, `dev` (which needs
  `--dev-bind` to survive the `--dev` tmpfs), `private` (a per-session copy,
  not the host's), `seed` (what a fresh copy is filled with) and `shared` (the
  host's own file, read-write, cut back *into* a private tree - see
  [sandbox-isolation](sandbox-isolation.md)). `skip` cuts back out of both
  `seed` and the files at the top of a private directory.
- A name the table has no preset for is warned about and dropped rather than
  refused - the files outlive the binary - but one *typed* into a dialog is
  refused (`check_presets`), that being where it can be fixed.
- `[defaults] agent` and `shell` name command presets and nothing else, refused on
  the way in if there is no file behind them (`update_sections`). Changing what
  the agent *runs* means editing a preset, not this section.
- A session naming a preset there is no file for has no command at all:
  `command_of` answers empty and `start` refuses before it makes a directory or
  hands tmux an empty argv.
- `GET /api/presets` is how the mod learns both tables, so a file added while the
  game is up is a checkbox and a dropdown entry with nothing rebuilt. The Settings
  page uses the same response as a small editor: Presets are sandbox definitions
  (with the implicit `global` definition shown first and highlighted), and Commands
  are command definitions with their sandbox dependencies.

## Sandbox (bubblewrap) rules

- Every bind is skipped unless the path exists. What makes that safe is that a
  path naming a variable this machine has not set expands to **nothing**, whole
  - not variable by variable, which used to leave the separator behind and turn
  `$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY` into `"/"`.
- A path that reaches the token, the preset files or another session's state is
  warned about and dropped whoever asked for it - see
  [sandbox-isolation](sandbox-isolation.md).
- Order: implicit `global`, presets, project, deduplicated, **rw after ro**, so a path in
  both ends up writable. `private` lands after all three: a session's own copy
  is what a project asking for the original gets.
- Binds go down *after* the skeleton (`--proc`, `--dev`, `--tmpfs /tmp`) or the
  tmpfs buries them. `resolv.conf` is emitted last of the read-only ones, because
  a preset can bind the directory it sits in.
- A socket is bound by its *directory*, wherever its owner recreates it. `dbus`
  and `wayland` name sockets directly because those outlive every session. SSH is split:
  `ssh` exposes config and known hosts only; `ssh-agent` is the explicit host-signing
  capability and is marked as an escape. GnuPG follows the same shape: `gpg` exposes
  public configuration only, while `gpg-agent` is the explicit signing/decryption capability.
- Agent presets do **not** forward configuration-root variables such as `CODEX_HOME`:
  the default path under `HOME` is the private bind. Forwarding one could point the
  tool back at a writable project directory and undo that isolation.
- `env` forwards names out of slopd's environment; `setenv` sets literals, applied
  last (`SYSTEMCTL_FORCE_BUS=1`, which is why `systemd` is useless without `dbus`).
- bwrap gets `--clearenv`; `BASE_ENV` survives regardless, `TERM`/`COLORTERM` are
  stated rather than forwarded.
