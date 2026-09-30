# Configuring agents

## Projects

A project defines a directory and shared path mounts for its agents.
The sandbox mounts the project at its configured absolute path.
Mount changes apply when agents start again. They do not change running sandboxes.

Use **Add project** in the sidebar or choose the project command in the command palette.
The sandbox mounts the project directory as read-write by default.

To make the project directory read-only, add a mount row:

1. Select **Add path**.
2. Enter the project directory in **From** and **To**.
3. Select `ro`.

| Field | Description |
| --- | --- |
| `dir` | Project directory on the host. |
| `mounts` | Host path, sandbox path, and mode (`ro`, `rw`, or `cache`) for each mount. |

The project form has **General**, **Mounts**, and **Worktrees** tabs. The **Mounts** tab shows
**From**, **To**, and access mode. Select **Add path** to add a blank row.

**Add project** copies the selected project's current directory into **From**. It copies that
project's configured directory into **To**. You can edit both paths. Later renames, directory
edits, or deletion do not change the copied paths. Select **×** to remove a row.

The daemon expands variables in source paths. Each source must then be absolute. A destination
can be absolute or relative to the selected checkout. For `ro` or `rw`, the source can be a
file or directory. It must exist when the agent starts. For `cache`, the daemon creates a
missing source as a shared writable directory.

Leave **From** blank to use managed storage. **Settings > Storage** lists managed and external
caches. See [project worktrees and cache mounts](project-worktrees.md). The daemon blocks paths
that overlap protected files or private state.

## Agents

Each agent belongs to a project and names a command preset. The command preset
identifies the software so the sandbox can mount its configuration paths.

| Field | Description |
| --- | --- |
| `command` | A command preset name. |
| `cmd` | Optional raw command line, used instead of the preset's default. |
| `args` | Extra arguments appended to the preset, daemon default, or overridden command line. Quote values containing spaces. |
| `sandbox` | Additional sandbox presets for this agent. |
| `network` | Network mode for this agent: `none`, `private`, or `host`. |
| `dns` | `resolved` to use the daemon's current resolver, or one or two explicit IPv4 server addresses. |
| `limits` | Process limits: `memory_mb`, `pids`, `nofile`, `cpu_pct`. Unset means no cap. |
| `persistent_tmp` | Keep a private `/tmp` across restarts. Reset and remove move it to the state trash. |
| `autostart` | Start this agent automatically when the daemon starts. |
| `auto_resume` | Send `/resume` after a fresh agent process reaches a settled prompt. |

Without `command`, `cmd` does not inherit the command preset's sandbox dependencies.

Use **Arguments** in the agent or template editor to extend the command without replacing it.
For example, `args = '--model "my model"'` adds two arguments to the preset command.
Templates copy these arguments into both agents and workers. Blank arguments leave the command unchanged.

## Agent templates

Open **+ > Agent**. Select a template or **Custom**.
The editor starts with a copy of the template settings. You can edit those settings.
To make a template from an agent, open the agent. Select **Save as template**.
The daemon stores personal templates in `agent_templates/` beside its main configuration.

Templates appear in **Library** with other entries. Click a template to open the agent editor
in template mode. Use **+ > Library > Agent template** to create a template. The General,
Sandbox, Resource limits, and Preview tabs use the agent editor's controls.

Templates do not store a project, mounts, or private-state identity. Right-click a template to
duplicate or delete it. Duplication opens a personal draft. Save the draft to add it to Library.
**Save as template** copies an agent's saved settings and dependencies. It does not copy project
settings or unsaved editor changes.

A failed save keeps the draft. If the template still exists, select **Reload** to load its latest
saved version. Reload discards the draft.
Template edits and deletion do not change existing agents.

A template supplies initial settings. Applying it copies its specified choices into the new agent.
Later edits or deletion do not change that agent. The agent keeps command and sandbox snapshots
when the live catalog changes. A template never captures project mounts, labels, private-state
identity, or credentials.

In a template, **Session default** leaves `persistent_tmp`, `autostart`, or `auto_resume`
unspecified. New agents use the default value (`false`) for those flags. A template can also set
network mode and DNS. If it omits them, the agent uses `private` networking and the daemon's
current resolver. An unset limit means no limit. Limits must be positive whole numbers. A blank
field means no configured limit.

**Daemon default** uses the destination daemon's default command. Select **Custom command** to
set a command line in the template.

## Ownership and previews

Projects supply the workspace and shared mounts. Agents define the command, sandbox additions,
network, DNS, resource limits, and startup and private-state behavior. Templates copy these agent
settings and their dependencies once. They do not stay attached to either project.

The agent editor's Preview tab shows direct agent settings, captured dependencies, and the
selected project's mounts. The project editor's Mounts tab shows the shared list and warns that
changes apply on the next start. Projects have no defaults for process settings or breadcrumbs.
Configure their shared mounts separately in the project editor.

The daemon calculates the preview from the unsaved form.
It shows settings for the next start.
Saving a project or agent does not replace a running process's network or sandbox.
Use **Refresh preview** after external file edits. At startup, the daemon checks each requested
bind against path safety and sandbox rules.

## Summaries

Settings > Agents > Summaries controls title and delegated-task summary policies, the
model, minimum prompt length, and the shared `summary_prompt`.
The daemon sends the configured instruction before each submitted prompt.
Changing the instruction also selects a separate summary cache entry.

## Workers

**Settings > Agents > Workers** sets the worker prompt and selects templates that agents can use
to create task workers. Select a template to add it to the allowlist. New templates do not enter
the allowlist automatically. The human **Worker** menu can use any template in the catalog.

The prompt can refer to `$SLOPWORLD_TASK_ID`. The default prompt includes the worker task
procedure. In the config file, the prompt is `[daemon.instructions].worker_prompt`. The allowlist
is `[daemon].worker_templates`.
See [Using slopctl](slopctl.md).

## Command apps

A command app defines software and its sandbox dependencies. Set `kind` to `agent` or `shell`.
The **Settings > Commands > Apps** page lists command definitions from the current catalog.
In **Settings > Commands > Defaults**, agent apps appear in **Agent**. Shell apps appear in
**Agent shell** and **Shell**. User apps in
`~/.config/slopworld/app_presets/*.toml` replace supplied apps with the same name.
If you omit `kind`, SlopWorld uses `agent`.

A file in `app_presets` contains one app definition. The `global.toml` sandbox
preset is implicit and precedes all others. See
[Configuring sandboxes](configuring-sandboxes.md) for sandbox configuration.

## Network

Agents own their network mode:

| Mode | Behavior |
| --- | --- |
| `none` | The agent has a private network namespace with no network access. |
| `private` | `pasta` provides synthetic DNS and outbound access. It forwards only the daemon TCP port back to the host when the daemon listens on `127.0.0.1` or all IPv4 interfaces. |
| `host` | Uses the host network and can reach local services. |

With `private`, `pasta` routes DNS requests. `resolved` uses the daemon's current resolver.
An explicit `dns` value supplies one or two IPv4 servers to `pasta`.

With `host`, `resolved` uses the host resolver. The daemon writes explicit server addresses to a
resolver file and mounts it in the sandbox. With `none`, the sandbox has no network access. DNS
settings have no effect.
Network and DNS changes take effect on the next agent start.

## When changes take effect

Restart an agent after you change its command, sandbox settings, or mounts. The daemon reads
project mount changes when an agent starts. It does not rebuild a running sandbox.

Auto-resume runs only for a new agent process.
A daemon restart that adopts an existing tmux pane does not submit `/resume` again.

## Shell

Right-click an agent.
Select **Shell** to open a shell inside the same sandbox.
The shell inherits the agent's sandbox presets, network mode, DNS, resource limits, and
selected project's mounts.
It sees the same filesystem as the agent. Host shells from
the project heading do not carry per-agent settings.

Settings > Commands > Defaults > **Agent shell** controls the `SHELL` environment
variable inside sandboxed agent sessions. It defaults to `bash`, independently of
the **Shell** default used by shell errands. SlopWorld resolves the selected shell
preset (or custom executable) to an absolute executable path before launch. Clients such as
Codex reject a `bash` value without an absolute path. They use the account's login shell instead.
After you change **Agent shell**, restart the agent. Explicit
tool-call shell overrides, host panes, and shell errands are unaffected.

## Library errands

Prompts and shell commands create temporary sessions.
Their Library editor has these execution choices:

- **Run using > Host** runs the session outside the sandbox.
- **Agent template: [name]** copies the template's command, sandbox additions, network, DNS,
limits, and private-state choices.
The selected project supplies the working directory and mounts.
A temporary project starts with an empty workspace.
Shell errands retain their shell executable.
Prompt errands use the template's command unless you supply a command override.

Each run copies the current template once and gets a fresh private identity. Editing or deleting
the template does not change an errand that already exists. Errands do not autostart or auto-resume.
Choose how to run an entry before you start it. File actions run on the daemon host.
