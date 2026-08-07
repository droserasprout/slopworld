# Projects and sessions

A session is an agent in a project: name, `command` (a command preset's name),
and this agent's own `cmd`, `sandbox` and `env` over it. `[[project]]` is a
directory plus a sandbox, `sandbox` being its preset list - there is no switch,
every agent runs in one.

- `temp` projects name no directory; `settle` coins `/tmp/slopworld/<name>` on the
  way in. Hence `dir` is `serde(default)`, and `check_project` still refuses an
  ordinary project without one.
- A command is a *preset* rather than a command string: knowing it is Claude Code
  is what lets the sandbox hand it `~/.claude` (`Config::sandbox_of`). An entry
  stating a `cmd` and no `command` is handed none - a command line is nobody in
  particular.
- `ro_paths`, `rw_paths` and `pass_env` are what this ground *adds* to the global
  `[sandbox]`. `seed` is not one of them: it is not a bind but what a fresh agent
  here is copied, on top of what its presets seed - `~/.claude/plugins` being the
  reason it exists. See [sandbox-isolation](sandbox-isolation.md).
- `check_belongs` runs on add and update, not at start. A project with agents
  refuses deletion. A rename carries its sessions in the same write.
- **Nothing is migrated.** A field this build does not know is dropped on the next
  write, which is what `Config::parse` being one `toml::from_str` means.

See [daemon-presets](daemon-presets.md) for what a preset name resolves to.
