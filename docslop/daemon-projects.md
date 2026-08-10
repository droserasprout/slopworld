# Projects and sessions

A session is an agent in a project: name, `command` (a command preset's name),
and this agent's own `cmd`, `sandbox` and optional network reduction over it.
`[[project]]` is a directory plus a sandbox and network ceiling; `sandbox` is
its preset list, and `network` is one of `none`, `private` or `host`.

An agent's `network` is optional: absent means inherit the project, and present
means a narrower mode. `Config::network_of` rejects a value wider than the
project before the agent is saved or started.

- `temp` projects name no directory; `settle` coins `/tmp/slopworld/<name>` on the
  way in. Hence `dir` is `serde(default)`, and `check_project` still refuses an
  ordinary project without one.
- A command is a *preset* rather than a command string: knowing it is Claude Code
  is what lets the sandbox hand it `~/.claude` (`Config::sandbox_of`). An entry
  stating a `cmd` and no `command` is handed none - a command line is nobody in
  particular.
- `sandbox` names the presets this ground adds to the implicit `global` preset. If a project
  needs different binds, seeded state or environment, make a user preset and attach it here
  (or to the agent). See [sandbox-isolation](sandbox-isolation.md).
- `check_belongs` runs on add and update, not at start. A project with agents
  refuses deletion. A rename carries its sessions in the same write.
- **Nothing is migrated.** A field this build does not know is dropped on the next
  write, which is what `Config::parse` being one `toml::from_str` means.

See [daemon-presets](daemon-presets.md) for what a preset name resolves to.
