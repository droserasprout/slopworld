# Configuring agents

## Projects

A project owns a directory and the shared path mounts visible to its agents.
Inside the sandbox, the project is mounted at its configured absolute path;
`/mnt/<project-name>` is a compatibility symlink to that same directory. Mount changes
apply when agents start again, so running sandboxes are unchanged.

Create a project in the sidebar's add strip or through the command palette. The project
directory is mounted read-write by default. A row with the project directory in both
**From** and **To** can make it read-only.

| Field | Description |
| --- | --- |
| `dir` | Project directory on the host. |
| `mounts` | Host `from` path, sandbox `to` path, and access `mode` (`ro` or `rw`) for each shared bind. |

The project form has **General** and **Mounts** tabs. **Mounts** has editable **From**, **To**, and access-mode columns. **Add path**
adds a blank row. **Add project** copies the selected project's current directory into From
and `/mnt/<name>` into To (its own directory for the primary project). These are ordinary
editable values: later project renames, directory edits, or deletion do not retarget the row.
Remove a row with **×**. Paths must be absolute after daemon expansion; sources may be files
or directories and must exist at launch. Protected daemon and private-state paths remain blocked.

## Agents

Each agent belongs to a project and names a command preset. The command preset
identifies the software so the sandbox can mount its configuration paths.

| Field | Description |
| --- | --- |
| `command` | A command preset name. |
| `cmd` | Optional raw command line, used instead of the preset's default. |
| `sandbox` | Additional sandbox presets for this agent. |
| `network` | Network mode for this agent: `none`, `private`, or `host`. |
| `dns` | `resolved` to follow the daemon's current resolver, or up to two explicit IPv4 servers. |
| `limits` | Final resource limits: `memory_mb`, `pids`, `nofile`, `cpu_pct`; an unset field means no cap. |
| `persistent_tmp` | Keep a private `/tmp` for this agent across restarts. It lives in the agent's durable state and moves with reset/delete. |
| `autostart` | Start this agent automatically when the daemon starts. |
| `auto_resume` | When enabled, the daemon pastes `/resume` and submits after the agent settles on startup. |

An agent with a raw `cmd` and no `command` gets the sandbox but none of the
CLI-specific wiring.

## Agent templates

Open **+ > Agent**, then choose a template or **Custom**. A template copies its
customizations once before the editor opens; every copied setting remains editable. To make
one, open an existing agent and choose **Save as template**. The daemon stores personal templates in
`agent_templates/` beside its main configuration.

Templates appear in **Library** alongside prompts, errands, breadcrumbs, and file actions.
Click a template to open the agent editor in template mode, or use
**+ > Library > Agent template** to create one. General, Sandbox, Resource limits,
and Preview use the same controls as agent editing; templates omit project,
mounts, and private-state actions. Right-click a template to duplicate or delete it.
Duplication opens a new personal draft; save it to add it to Library. Capture an existing
agent with its editor's **Save as template** action. Capture copies the saved agent's own
customizations and their dependencies, excluding project contributions. Unsaved editor changes
are not captured. A failed save keeps the draft; use **Reload** only when you want to discard
it and reconcile with a newer catalog revision. Existing agents
are not changed when their source template is edited or deleted.

Templates are one-time recipes. Applying one copies its specified choices into the new
agent; later template edits or deletion do not change that agent. Command and sandbox
snapshots remain stable, including when the live catalog changes. A template never captures
mounts, labels, private state identity, or credentials.

In a template, **Session default** leaves a startup choice unspecified. Network and DNS
choices are agent settings; omitted values use the documented agent defaults (`private` and
the system resolver). An unset limit means no cap. **Daemon default** selects the destination
daemon's default command. **Custom** pins a value. Limits require a positive whole
number; a blank field means no configured cap.

## Ownership and previews

Projects supply the workspace and shared mounts. Agents supply command, sandbox additions,
network, DNS, resource limits, and startup/private-state behavior. Templates copy those agent
settings and captured dependencies once; they do not remain attached to the source or
destination project.

The agent editor's Preview tab shows direct agent settings, captured dependencies, and the
selected project's mounts. The project editor's Mounts tab shows the shared list and warns that
changes apply on the next start. Projects have no process-setting or breadcrumb defaults;
their shared mounts are configured separately in the project editor.

Preview is calculated by the daemon from the unsaved form. It shows settings for the next
start; saving a project or agent does not replace a running process's network or sandbox.
Use **Refresh preview** after external file edits. Requested bindings still undergo path and
sandbox validation at launch.

## Summaries

Settings > Integrations > Summaries controls the title and delegated-task summary policies,
model, minimum submitted-prompt length, and the shared `summary_prompt`. The configured
instruction is sent before the submitted prompt; changing it also selects a separate summary
cache entry.

## Workers

Settings > Integrations > Workers controls both the bootstrap prompt and the templates that
agents may use to spawn workers. Check a template to add its name to the worker allowlist; new
templates are unchecked. The user Worker menu can use any catalog template. The prompt can refer
to `$SLOPWORLD_TASK_ID`; the default includes the
worker task workflow. The setting lives under `[daemon.instructions]` as
`worker_prompt`, with the allowlist under `[daemon] worker_templates`. See [Using slopctl](slopctl.md).

## Command apps

A command app names a piece of software and declares `kind = "agent"` or
`kind = "shell"`. The Settings > Commands > Apps page uses that field to keep the Agent and
Shell defaults separate while sourcing both lists from the live command catalog. User
apps in `~/.config/slopworld/app_presets/*.toml` replace builtins by name; an omitted kind
is treated as `agent`.

A file in `app_presets` contains one direct app definition. The `global.toml` sandbox
preset is implicit and precedes all others. See
[Configuring sandboxes](configuring-sandboxes.md) for the sandbox side.

## Network

Agents own their network mode:

| Mode | Behavior |
| --- | --- |
| `none` | Private network namespace. No connectivity. |
| `private` | `pasta` wraps the sandbox with synthetic DNS. No port forwarding. Blocks loopback but does not restrict egress. |
| `host` | Full host networking, including local services. |

DNS `resolved` follows the daemon's current resolver. An explicit `dns` on the agent selects
up to two IPv4 servers for `pasta`.
Network and DNS changes take effect on the next agent start.

## When changes take effect

Restart agents after changing their launch configuration, including commands, sandbox
settings, and mounts. Project mount edits are read at each start and do not rebuild a running
sandbox.

Auto-resume runs only for a fresh agent process; a daemon restart that adopts an existing
tmux pane does not submit `/resume` again.

## Shell

Right-click an agent and select **Shell** to open a shell inside the same sandbox.
The shell inherits the agent's sandbox presets, network mode, DNS, resource limits,
and selected project's mounts, so it sees the same filesystem the agent does. Host shells from
the project heading do not carry per-agent settings.

Settings > Commands > Defaults > **Agent shell** controls the `SHELL` environment
variable inside sandboxed agent sessions. It defaults to `bash`, independently of
the **Shell** default used by shell errands. SlopWorld resolves the selected shell
preset (or custom executable) to an absolute executable path before launch. This is
important for clients such as Codex, which reject a bare `bash` value and otherwise
fall back to the account's login shell; restart an agent after changing it. Explicit
tool-call shell overrides, host panes, and shell errands are unaffected.

## Library errands

Prompts and shell commands create temporary sessions. In their Library editor, choose
**Run using > Host** for execution outside the sandbox, or **Agent template: …** to copy
that template's command, sandbox additions, network, DNS, limits and private-state choices.
The selected project supplies the working directory and mounts; a temporary project starts
with an empty workspace. Shell errands retain their shell executable; prompt errands use
the template's command unless a command override is supplied.

Each run copies the current template once with a fresh private identity. Editing or deleting
the template does not change an errand already created. Errands do not autostart or auto-resume.
An execution choice is required before running an entry. File actions continue to execute on the daemon host.
