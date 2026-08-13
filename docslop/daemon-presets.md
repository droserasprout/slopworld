# Presets and the sandbox

`presets.rs` reads one TOML file per tool. A file may define `[[sandbox]]`,
`[[command]]`, or both. Builtins are `include_str!` from `slopd/presets/*.toml`;
user files live under `~/.config/slopworld/presets/*.toml` (`SLOPD_PRESETS` overrides)
and replace builtins by entry name, in place.

`global.toml` is implicit and precedes command, project and session presets. It is not
a project checkbox; copying it creates the user `global` override.

- Presets reload when the directory's newest mtime changes, using the same two-second
  check as `config.toml`; sessions are re-announced after reload.
- `category` is free text and unknown values become GUI headings. Non-empty `escapes`
  marks a host-reachable capability and is shown by `Warn`.
- `requires` forms a cycle-safe dependency closure. Implied boxes are disabled in the
  mod; for example `systemd` requires `dbus` and `python-cache` requires `python`.
- `tmux = true` is a deliberate host escape for a debugging preset: the daemon's configured
  tmux socket is mounted into the guest's uid-0 socket directory so `tmux -L slopworld` can
  inspect the live terminals.
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
  followed by sandbox definitions and command definitions with dependencies.

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
