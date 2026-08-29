# Configuring agents

## Projects

A project is a directory, a sandbox preset list, and a network default. Agents belong
to a project and inherit its sandbox and network settings. Inside the sandbox, the
project is mounted at its configured absolute path; `/mnt/<project-name>` is a
compatibility symlink to that same directory.

Create a project in the sidebar's add strip or through the command palette. The project
directory is always mounted read-write inside the sandbox.

| Field | Description |
| --- | --- |
| `dir` | Project directory on the host. |
| `sandbox` | Preset names added on top of the implicit `global` preset. |
| `network` | Default for agents in this project: `none`, `private`, or `host`. |
| `dns` | Up to two explicit IPv4 DNS servers. Absent means follow the host's `/etc/resolv.conf`. |
| `limits` | Resource limits: `memory_mb`, `pids`, `nofile`, `cpu_pct`. |

## Agents

Each agent belongs to a project and names a command preset. The command preset
identifies the software (Claude Code, Codex, a shell) so the sandbox can mount the
right configuration paths.

| Field | Description |
| --- | --- |
| `command` | A command preset name. |
| `cmd` | Optional raw command line, used instead of the preset's default. |
| `sandbox` | Additional sandbox presets for this agent. |
| `network` | Optional override of the project's network default. |
| `dns` | Optional override of the project's DNS servers. |
| `limits` | Optional override of the project's resource limits. Agent values win. |
| `mounts` | Additional project directories mounted under `/mnt/<project-name>`. Each entry names a project and an optional `mode` (`ro` or `rw`, default `rw`). |
| `slopworld_md` | Mount generated `SLOPWORLD.md` read-only at the project root, add a first-prompt discovery breadcrumb, and exclude the file through the repository's `.git/info/exclude`. |
| `autostart` | Start this agent automatically when the daemon starts. |
| `auto_resume` | When enabled, the daemon pastes `/resume` and submits after the agent settles on startup. |
| `breadcrumbs` | Named breadcrumb blocks delivered alongside the first prompt. |

An agent with a raw `cmd` and no `command` gets the sandbox but none of the
CLI-specific wiring.

## Command presets

A command preset names a piece of software. Builtins exist for Codex, Claude Code, Pi,
and the shell family (bash, zsh, fish, Nushell, pwsh). User presets in
`~/.config/slopworld/presets/*.toml` replace builtins by name.

A file may define `[[command]]`, `[[sandbox]]`, or both. The `global.toml` sandbox
preset is implicit and precedes all others. See
[Configuring sandboxes](configuring-sandboxes.md) for the sandbox side.

## Network

Projects set the network default; agents may override it:

| Mode | Behavior |
| --- | --- |
| `none` | Private network namespace. No connectivity. |
| `private` | `pasta` wraps the sandbox with synthetic DNS. No port forwarding. Blocks loopback but does not restrict egress. |
| `host` | Full host networking, including local services. |

DNS defaults to up to two IPv4 nameservers from the daemon's `/etc/resolv.conf`. An
explicit `dns` on the project or agent selects up to two IPv4 servers for `pasta`.
Network and DNS changes take effect on the next agent start.

## When changes take effect

| Change | Takes effect |
| --- | --- |
| Agent name, command, cmd | Next start. |
| Sandbox presets | Next start. |
| Network mode, DNS | Next start. |
| Resource limits | Next start. |
| Mounts | Next start. |
| Autostart, auto-resume, breadcrumbs, `slopworld_md` | Next start. |
| Project directory | Immediately for new agents; running agents keep their current mount. |

Running agents are not rebuilt from changed defaults. Restart the agent to apply
sandbox, network, mount, preset, or `slopworld_md` changes. An enabled manifest is
regenerated when configuration is synchronized and before each start; it is a
snapshot for an already-running sandbox. SlopWorld refuses to overwrite a
project-owned `SLOPWORLD.md`.

## Shell

Right-click an agent and select **Shell** to open a shell inside the same sandbox.
The shell inherits the agent's sandbox presets, network mode, DNS, resource limits,
and mounts, so it sees the same filesystem the agent does. Host shells from the
project heading do not carry per-agent overrides.
