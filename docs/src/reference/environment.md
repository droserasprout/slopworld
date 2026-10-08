# Environment variables

This page covers runtime variables used by the daemon, mod, launcher, and `slopctl`.
Set a variable in the environment of the process that reads it. A shell export
does not change an already-running daemon or game. For a systemd-managed daemon,
configure the service environment and restart the service.

Build recipe settings belong to [Build from source](../development/build.md).
Container and macOS recipe settings belong to [Linux sidecar](../deployment/sidecar.md)
and [macOS](../deployment/macos.md).

## Connection and identity

| Variable | Read by | Effect |
| --- | --- | --- |
| `SLOPD_ENDPOINT` | Daemon, mod, launcher, `slopctl` | Selects the endpoint descriptor file. The daemon writes its URL and token here; clients read it. See [Paths and files](paths.md#daemon-configuration) for the default. |
| `SLOPD_URL` | `slopctl` | Uses this daemon URL instead of reading the endpoint descriptor. Requires `SLOPD_TOKEN` to be set. |
| `SLOPD_TOKEN` | `slopctl` | Token paired with `SLOPD_URL`. An explicitly empty value is accepted for a daemon without authentication. Setting only this variable does not override the descriptor's token. |
| `SLOPWORLD_SESSION` | `slopctl` | Caller identity. Unset or empty means `host`, which requires root authority. The daemon supplies the session name to agent and host-shell processes. |
| `SLOPWORLD_PROJECT` | Session commands | Project name supplied by the daemon to agent and host-shell processes. |
| `SLOPWORLD_TASK_ID` | `slopctl` | Default task ID when a task command omits it. Supplied automatically to workers. See [Task commands](slopctl.md#task-commands). |

`SLOPD_URL` and `SLOPD_TOKEN` select the CLI connection; they do not configure the
daemon's listener or authentication. Those settings live in `config.toml`.
The mod uses the endpoint descriptor, not these CLI overrides.

Workers receive a scoped URL/token pair automatically. See
[Agent collaboration](../agents/agent-collaboration.md) for granting access to
other sessions and [API authorization](api.md#authorization) for token scope.

For example, inspect a sidecar using its descriptor:

```sh
SLOPD_ENDPOINT="$HOME/.config/slopworld-car/endpoint.toml" slopctl status
```

## Daemon storage

Use absolute paths for storage overrides. Unset an override to use its default;
an empty path value is not treated as an unset variable by the storage helpers.
The [Paths and files](paths.md) reference owns the default locations and directory contents.

| Variable | Selects |
| --- | --- |
| `SLOPD_CONFIG_ROOT` | Application configuration root for catalogs and the default settings file. |
| `SLOPD_DATA` | Application data root for agent records, tasks, grants, and other durable workspace state. |
| `SLOPD_CACHE` | Application cache root. |

These roots are independent. Settings and catalogs follow `SLOPD_CONFIG_ROOT`;
private session state follows `SLOPD_DATA`. `SLOPD_CONFIG_ROOT` does not relocate endpoint discovery; use
`SLOPD_ENDPOINT` consistently for the daemon and its clients.

## Game and profile selection

| Variable | Read by | Effect |
| --- | --- | --- |
| `SLOPWORLD_GAME` | `slopworld` | Game directory when `--game` is absent, before saved and discovered locations. |
| `RIMWORLD` | `slopworld mod` commands | Game installation for mod management when an explicit destination is absent. Also used by build recipes. |
| `MAC_RIMWORLD` | `slopworld mod` commands on macOS | Game app location after `RIMWORLD`; defaults to `~/Documents/RimWorld.app`. Also used by macOS recipes. |
| `SLOPWORLD_PROFILE` | `slopworld` | Game profile when neither `--profile` nor `SLOPCAR_PROFILE` supplies one. |
| `SLOPCAR_PROFILE` | `slopworld` | Sidecar profile, ahead of `SLOPWORLD_PROFILE`. Also enables sidecar integration and requires `SLOPD_ENDPOINT` to name an existing descriptor. |

The launcher ignores blank values for these game/profile choices. An explicit
`--profile` wins profile selection, but setting `SLOPCAR_PROFILE` still enables
the sidecar endpoint check. See [Game profiles](../maintenance/game-profiles.md).

## Logs and diagnostics

| Variable | Read by | Default and effect |
| --- | --- | --- |
| `SLOPD_LOG` | Daemon | Tracing filter; defaults to `slopd=info`. For more daemon detail, use `slopd=debug`. |
| `SLOPWORLD_DEBUG` | Daemon and mod | Enables performance and terminal-latency instrumentation when `1` or `true` (case-insensitive). Disabled otherwise; set it before starting each process you want to trace. |
| `SLOPWORLD_GAME_LOG` | `slopctl logs` | Selects the game log to read; does not change where Unity writes logs. |
| `SLOPWORLD_DAEMON_UNIT` | `slopctl logs` | Selects the systemd user unit to read; defaults to `slopd.service`. |

See [Log locations](paths.md#logs) and [Troubleshooting](../help/troubleshooting.md).
The supplied service sets `SLOPD_LOG=slopd=info`; change its service environment
to override that value.

## tmux and runtime

| Variable | Read by | Effect |
| --- | --- | --- |
| `SLOPD_TMUX_SOCKET` | Daemon | Private tmux socket name; defaults to `slopworld` when unset or blank. |
| `TMUX_TMPDIR` | Daemon and tmux | Changes the tmux socket directory. See [Attaching to tmux](../terminals/terminal.md). |
| `SLOPD_RUNTIME` | Daemon | Set to `slopcar` by the sidecar entrypoint. Leave unset for native operation; other values are rejected. |
| `LESSUTFCHARDEF` | Daemon when preparing terminals | Overrides the supplied Unicode handling for `less`. The default preserves emoji components in terminal output. |

## Provider integrations

| Variable | Read by | Effect |
| --- | --- | --- |
| `OPENROUTER_API_KEY` | Daemon | Fallback key for prompt summaries and OpenRouter balance polling when the corresponding key-file setting is blank. |
| `SLOPD_TITLE_URL` | Daemon | Overrides the summary request endpoint; defaults to `https://openrouter.ai/api/v1/chat/completions`. |
| `SLOPD_USAGE_URL` | Daemon | Overrides the Anthropic usage endpoint; defaults to `https://api.anthropic.com/api/oauth/usage`. |
| `SLOPD_CREDITS_URL` | Daemon | Overrides the OpenRouter balance endpoint; defaults to `https://openrouter.ai/api/v1/credits`. |
| `SLOPD_OPENAI_USAGE_URL` | Daemon | Overrides the OpenAI usage endpoint; defaults to `https://chatgpt.com/backend-api/wham/usage`. |

Endpoint overrides are useful for integration testing; requests use the configured
provider credentials. See [Usage polling](../agents/usage-and-summaries.md#usage-polling) and
[Prompt summaries](../agents/usage-and-summaries.md#prompt-summaries) for normal
provider setup and credential-file settings.

## Standard variables and sandbox inheritance

`XDG_CONFIG_HOME`, `XDG_DATA_HOME`, and `XDG_CACHE_HOME` supply platform directory
bases; see [Paths and files](paths.md). The daemon's `PATH` determines which
installed tools it can find. Desktop and audio integrations also use their host
session environment, including `DISPLAY`, `WAYLAND_DISPLAY`, `XDG_RUNTIME_DIR`,
`DBUS_SESSION_BUS_ADDRESS`, and `PULSE_SERVER` where applicable.

Sandboxed agents start with a cleared environment. For forwarding names with
preset `env`, setting values with `setenv`, and precedence, see
[Sandbox environment](../sandbox/sandbox-presets.md#environment).
The daemon supplies terminal settings and the configured agent `SHELL`.
It sets `SLOPWORLD_PI_TITLES=never` for managed Pi commands so prompt titles have
one owner.

For a source-oriented inventory, including build-time and test-only
names, run `just refresh-reference` and read the generated repository-root
`reference.md`. That inventory is separate from this runtime guide.
