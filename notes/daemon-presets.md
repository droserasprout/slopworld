# Presets and the sandbox

`presets.rs` reads one TOML file per tool. A file may define `[[sandbox]]`,
`[[command]]`, or both. Builtins are `include_str!` from `slopd/presets/*.toml`;
user files live under `~/.config/slopworld/presets/*.toml` (`SLOPD_PRESETS` overrides)
and replace builtins by entry name, in place.

Preset categories are not part of either table. Unknown fields reject a preset file so
stale definitions cannot be silently rewritten into the current shape.

`global.toml` is implicit and precedes command, project and session presets. It is not
a project checkbox; copying it creates the user `global` override.

- `rust-cache` keeps `~/.cargo/registry`, `~/.cargo/git`, `~/.rustup/toolchains` and
  `~/.rustup/update-hashes` writable, while `claude` also makes `/tmp/claude-0` private.
  The latter needs an empty per-session state directory even though the host path is absent:
  the guest's `/tmp` is a tmpfs.

- Presets reload when the directory's newest mtime changes, using the same two-second
  check as `config.toml`; sessions are re-announced after reload.
- Non-empty `escapes` marks a host-reachable capability and is shown by `Warn`.
- `requires` forms a cycle-safe dependency closure. Implied boxes are disabled in the
  mod; for example `systemd` requires `dbus` and `python-cache` requires `python`.
- `tmux = true` is a deliberate host escape for a debugging preset: the daemon's private
  tmux socket is mounted into the guest's uid-0 socket directory so `tmux -L slopworld` can
  inspect the live terminals.
- `daemon_config = true` mounts only the effective `config.toml` and `endpoint.toml`
  read-only. It is the narrow diagnostic exception to the guard that rejects those files from
  every ordinary bind list.
- `slopworld-debug` is the intentionally unsafe game-development bundle: it includes the
  game/profile tree and user-owned jukebox data read-write, host process and device diagnostics,
  X11/Wayland/audio/GPU, desktop application metadata, systemd/D-Bus, tmux, writable
  launcher/service-install paths, and Rust/.NET/Python development caches.
- `android-dev` and `ios-dev` expose their platform toolchains and writable build state without
  device access. `android-debug` adds Android USB/KVM/display access, while `ios-debug` adds
  simulator/device state and USB access; both are marked as host escapes.
- `GET /api/presets` returns the complete effective definition and `source` (`system`,
  `user` or `override`). Root-only `POST /api/presets/:kind/:name/copy`, `PUT` and
  `DELETE` edit user `sandbox`/`command` entries. Saves are validated and atomically
  replaced; deleting an override reveals the builtin. A required user-only sandbox
  cannot be deleted.
- `[[sandbox]]` path kinds are `ro`, `rw`, `dev`, `private`, `seed`, `shared`, plus
  `skip`. `dev` needs `--dev-bind`; `private` is per-session; `seed` fills a new copy;
  `shared` binds a host-owned file read-write into private state; `skip` removes paths
  from seed and private top-level files ([sandbox-isolation](sandbox-isolation.md)).
- Unknown preset names from files are warned and dropped; names entered in a dialog are
  rejected by `check_presets`. `[defaults] agent` and `shell` must name command presets,
  and `start` refuses a session whose command preset is missing.
- The mod learns both tables from `GET /api/presets`; files added while the game runs
  become settings-page entries without rebuilding. `global` is shown first/highlighted,
  followed by uncategorized sandbox and command definitions with dependencies.
- Builtin shell commands are `bash`, `zsh`, `fish`, `nu` (Nushell), and `pwsh`; their matching
  `*-userdata` sandbox presets are separate and opt-in. They expose startup/config files
  read-only and history/data paths read-write, so choosing a shell does not share host dotfiles.

## Sandbox (bubblewrap) rules

- Binds are skipped when their paths do not exist. An unset variable expands to the
  whole empty path, not an empty component; this prevents
  `$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY` from becoming `/`.
- Paths reaching the token, preset files or another session's state are warned and
  dropped ([sandbox-isolation](sandbox-isolation.md)).
- Order is implicit `global`, presets, project, then deduplicated paths with `rw` after
  `ro`; `private` is applied last so a project's original path can resolve to its copy.
- Binds follow the skeleton (`--proc`, `--dev`, `--tmpfs /tmp`); `resolv.conf` is the last
  read-only bind. Sockets are bound by directory except long-lived `dbus`/`wayland`
  sockets. `ssh` exposes public config; `ssh-agent` is the explicit host-signing escape.
  `gpg`/`gpg-agent` follow the same split.
- Configuration-root variables such as `CODEX_HOME` are not forwarded: the default path
  under `HOME` is private. `env` forwards names; `setenv` writes literals last.
- bwrap starts with `--clearenv`; `BASE_ENV` survives, while `TERM`/`COLORTERM` are set
  explicitly.
