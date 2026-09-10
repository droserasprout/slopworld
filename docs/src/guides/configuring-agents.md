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
identifies the software so the sandbox can mount its configuration paths.

| Field | Description |
| --- | --- |
| `command` | A command preset name. |
| `cmd` | Optional raw command line, used instead of the preset's default. |
| `sandbox` | Additional sandbox presets for this agent. |
| `network` | Optional override of the project's network default. |
| `dns` | Optional override of the project's DNS servers. |
| `limits` | Optional override of the project's resource limits. Agent values win. |
| `mounts` | Additional project directories mounted under `/mnt/<project-name>`. Each entry names a project and an optional `mode` (`ro` or `rw`, default `rw`). |
| `slopworld_md` | Mount generated `SLOPWORLD.md` read-only at the Instructions mount path and exclude the source file through the repository's `.git/info/exclude`. |
| `instructions_breadcrumb` | Add the configured discovery breadcrumb when the manifest is mounted. Defaults to `true`; the agent editor's Breadcrumbs tab can turn it off. |
| `persistent_tmp` | Keep a private `/tmp` for this agent across restarts. It lives in the agent's durable state and moves with reset/delete. |
| `breadcrumb_yolo` | Automatically paste effective breadcrumbs before the first Enter after startup. Defaults to `true`; disable it when breadcrumbs should remain pending for manual use. |
| `autostart` | Start this agent automatically when the daemon starts. |
| `auto_resume` | When enabled, the daemon pastes `/resume` and submits after the agent settles on startup. |
| `breadcrumbs` | Named breadcrumb blocks delivered alongside the first prompt. |

An agent with a raw `cmd` and no `command` gets the sandbox but none of the
CLI-specific wiring.

## Summaries

Settings > Integrations > Summaries controls the title and delegated-task summary policies,
model, minimum submitted-prompt length, and the shared `summary_prompt`. The configured
instruction is sent before the submitted prompt; changing it also selects a separate summary
cache entry.

## Instructions

Settings > Integrations > Instructions edits the generated `SLOPWORLD.md` document.
The source file stays at the project root so Git ownership and the generated-file guard
remain predictable; `mount_path` controls the read-only destination inside the primary
project mount. It is relative to that project and defaults to `SLOPWORLD.md`.

The template supports `{{ runtime_context }}` for SlopWorld's live project snapshot,
plus `{{ project }}`, `{{ mount_path }}`, and `{{ file }}`. Unknown variables are kept
as written. The separate discovery `breadcrumb` supports `{{ project }}`, `{{ mount_path }}`,
and `{{ file }}`; it is pasted into an opted-in agent's first prompt. The Preview tab renders
unsaved body text for a selected project.

These settings live under `[daemon.instructions]`: `template`, `mount_path`,
`breadcrumb`, `breadcrumb_enabled`, `worker_prompt`, and `worker_breadcrumb`. Use Settings'
reset actions for current defaults. `worker_prompt` is submitted to each spawned task worker
and can refer to `$SLOPWORLD_TASK_ID`; `worker_breadcrumb` is pasted before its first prompt
when breadcrumb delivery is enabled and may be blank. The feature switches live under
`[daemon]` as `experimental_instructions` and `experimental_breadcrumbs`; Settings >
Integrations > Workers places the two worker fields under their respective gates. See
[Using slopctl](slopctl.md).

The per-agent `slopworld_md` switch still controls whether the document is mounted at
all. `breadcrumb_enabled` controls the additional discovery line globally, while
`instructions_breadcrumb` controls the per-agent opt-in; named breadcrumbs remain independent.
The agent editor's **Breadcrumbs** tab shows the generated discovery entry and defaults it on
when the manifest is mounted. Settings provides separate **Reset to default** actions for the
body and breadcrumb; reset changes the pending form and **Save** applies it.

## Command presets

A command preset names a piece of software and declares `kind = "agent"` or
`kind = "shell"`. The Settings > Commands page uses that field to keep the Agent and
Shell defaults separate while sourcing both lists from the live command catalog. User
presets in `~/.config/slopworld/presets/*.toml` replace builtins by name; an omitted kind
is treated as `agent` for compatibility.

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

Restart agents after changing their launch configuration, including commands,
sandbox settings, mounts, and startup instructions. Running agents retain their
current mounts when a project directory changes.

An enabled manifest is regenerated when configuration is synchronized and before each
start; it is a snapshot for an already-running sandbox. SlopWorld refuses to overwrite
a project-owned `SLOPWORLD.md`. The default body points agents to `README.md` and applicable
`AGENTS.md` files, while the default breadcrumb points them to the generated mount.
Auto-resume runs only for a fresh agent process; a daemon restart that adopts an existing
tmux pane does not submit `/resume` again.

## Shell

Right-click an agent and select **Shell** to open a shell inside the same sandbox.
The shell inherits the agent's sandbox presets, network mode, DNS, resource limits,
and mounts, so it sees the same filesystem the agent does. Host shells from the
project heading do not carry per-agent overrides.

Settings > Commands > Defaults > **Agent shell** controls the `SHELL` environment
variable inside sandboxed agent sessions. It defaults to `bash`, independently of
the **Shell** default used by shell errands. This avoids passing a host login shell
such as zsh to agent tools; restart an agent after changing it.
