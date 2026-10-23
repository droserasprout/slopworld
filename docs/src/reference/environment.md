# Environment variables

Set variables before starting the process that reads them. For a systemd-managed
daemon, set them in the service environment and restart it.

Defaults below are for native mode unless noted. `<CONFIG>`, `<DATA>`, and `<CACHE>`
are the application roots described in [Paths and files](paths.md). Command-line
options take precedence where available. Build recipe settings belong to
[Build from source](../development/build.md).

| Variable | Read by | Default | Effect |
| --- | --- | --- | --- |
| `MAC_RIMWORLD` | `slopworld mod` on macOS | `~/Documents/RimWorld.app` | Game app location when `RIMWORLD` is absent. Also used by macOS recipes. |
| `OPENROUTER_API_KEY` | Daemon | None | Key for prompt summaries and OpenRouter balance polling when no key file is configured. |
| `RIMWORLD` | `slopworld mod` | Platform game location | Game installation for mod management. Also used by build recipes. |
| `SLOPCAR_CONTAINER` | `slopcar`, `slopctl --sidecar logs daemon` | `slopcar` | Sidecar container name. |
| `SLOPCAR_CPUS` | `slopcar` | `4` | Container CPU limit. |
| `SLOPCAR_IMAGE` | `slopcar` | `slopcar:local` | Container image to build or run. |
| `SLOPCAR_MEMORY` | `slopcar` | `8g` | Container memory limit. |
| `SLOPCAR_PIDS_LIMIT` | `slopcar` | `4096` | Container process limit. |
| `SLOPCAR_PORT` | `slopcar` | `7718` | Loopback host port and initial daemon bind port. |
| `SLOPD_CACHE` | Daemon, launcher, CLI | Platform cache directory | Application cache root.[^paths] |
| `SLOPD_CONFIG_ROOT` | Daemon, mod, launcher, CLI | Platform config directory | Configuration root for settings, catalogs, endpoint discovery, and the saved game directory.[^paths] |
| `SLOPD_CREDITS_URL` | Daemon | `https://openrouter.ai/api/v1/credits` | OpenRouter balance endpoint. |
| `SLOPD_DATA` | Daemon, mod, launcher, CLI | Platform data directory | Data root for workspace state, jukebox likes, and the default game profile.[^paths] |
| `SLOPD_ENDPOINT` | Daemon, mod, launcher, `slopctl` | `<CONFIG>/endpoints/local.toml` | Explicit descriptor path; clients default to `endpoints/local.toml`.[^connection] |
| `SLOPD_LOG` | Daemon | `slopd=info` | Tracing filter; use `slopd=debug` for more detail. |
| `SLOPD_OPENAI_USAGE_URL` | Daemon | `https://chatgpt.com/backend-api/wham/usage` | OpenAI usage endpoint. |
| `SLOPD_TITLE_URL` | Daemon | `https://openrouter.ai/api/v1/chat/completions` | Prompt summary endpoint. |
| `SLOPD_TMUX_SOCKET` | Daemon | `slopworld` | Private tmux socket name. |
| `SLOPD_TOKEN` | `slopctl` | Descriptor token | Root or scoped token paired with `SLOPD_URL`.[^connection] |
| `SLOPD_URL` | `slopctl` | Descriptor URL | Daemon URL override; requires `SLOPD_TOKEN`.[^connection] |
| `SLOPD_USAGE_URL` | Daemon | `https://api.anthropic.com/api/oauth/usage` | Anthropic usage endpoint. |
| `SLOPWORLD_DAEMON_UNIT` | `slopctl logs` | `slopd.service` | Systemd user unit to read. |
| `SLOPWORLD_DEBUG` | Daemon, mod | Disabled | Enables performance and terminal-latency instrumentation with `1` or `true` (case-insensitive). |
| `SLOPWORLD_GAME` | `slopworld` | Saved or discovered game directory | Game directory when `--game` is absent. |
| `SLOPWORLD_GAME_LOG` | Launcher, `slopctl logs`, `just logs` | `<CACHE>/Player.log` | Unity log path, passed to the game with `-logFile`; sidecar defaults to `<CACHE>/sidecar/Player.log`. |
| `SLOPWORLD_PROFILE` | `slopworld` | `<DATA>/profiles/<name>` | Override the selected peer’s profile when `--profile` is absent; sidecar profiles use `<DATA>/sidecar/profiles/<name>`. |
| `SLOPWORLD_PROJECT` | Session commands | Set by daemon | Project name supplied to agent and host-shell processes. |
| `SLOPWORLD_SESSION` | `slopctl` | `host` | Caller identity; supplied by the daemon to agent and host-shell processes. |
| `SLOPWORLD_TASK_ID` | `slopctl` | Set for workers | Task ID used when a task command omits it. |

[^paths]: Use absolute paths and the same roots for processes sharing an installation.
    Host `slopcar` also reads these roots and derives its paths beneath them;
    container paths stay fixed. See [Sidecar mode](../installation/sidecar.md).

[^connection]: The daemon publishes the local descriptor; `slopworld connect` stores
    named endpoints without changing the default `local` endpoint. `--endpoint NAME` overrides environment
    connection settings for one client invocation. Otherwise, `SLOPD_URL` and
    `SLOPD_TOKEN` override the CLI connection together; they do not configure the
    daemon listener or the mod. Workers receive a scoped URL/token pair automatically.
    See [Agent collaboration](../agents/agent-collaboration.md).

`XDG_CONFIG_HOME`, `XDG_DATA_HOME`, and `XDG_CACHE_HOME` supply directory defaults
on Linux. The daemon's `PATH` determines which tools it can find. Desktop and audio
integrations use the host session environment, including `DISPLAY`,
`WAYLAND_DISPLAY`, `XDG_RUNTIME_DIR`, `DBUS_SESSION_BUS_ADDRESS`, and `PULSE_SERVER`.

Sandboxed agents start with a cleared environment. The daemon supplies terminal
settings and the configured agent `SHELL`. See [Sandbox environment](../sandbox/sandbox-presets.md#environment)
for forwarding variables with preset `env` and setting values with `setenv`.
