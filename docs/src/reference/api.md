# API

The daemon listens on `127.0.0.1:7717` (or the configured bind address). The mod and
`slopctl` authenticate with the token from `~/.config/slopworld/endpoint.toml`.

## HTTP

Write operations use HTTP so the caller can inspect daemon error bodies.

### Query routes

| Route | Description |
| --- | --- |
| `/api/health` | Daemon version, hostname, and runtime metadata. |
| `/api/capabilities` | Runtime flags: native audio, per-agent limits, sidecar mode. |
| `/api/sessions` | All sessions and their state. |
| `/api/sessions/:name/cwd` | Agent's current working directory. |
| `/api/usage` | Current quota windows. |
| `/api/presets` | Sandbox and command presets. |
| `/api/jukebox` | Jukebox state and station catalog. |
| `/api/audio` | Current audio playback state. |
| `/api/browse` | Directory listing. `files=1` for files, `hidden=1` for dotfiles, `limit` capped at 500. |
| `/api/read` | Bounded UTF-8 file text (root-only). |
| `/api/image` | Bounded base64 image bytes (root-only). |
| `/api/search` | `rg`-based workspace search. Requires project path and query. Supports regex/case/word/hidden flags. Capped at 200 results. |
| `/api/git` | Repository root, branch, per-file porcelain/numstat; large status streams are capped with `truncated`. Non-repositories return `repo: false`. |
| `/api/open-apps` | Host desktop applications associated with a path. |
| `/api/clipboard` | GET reads host CLIPBOARD; POST writes it. `/api/clipboard/text` is text-only. |

PRIMARY selection uses `/primary` in place of `/clipboard`.

### Write routes

| Route | Method | Description |
| --- | --- | --- |
| `/api/sessions` | POST | Create or modify sessions. |
| `/api/projects` | POST | Create or modify projects. |
| `/api/shortcuts` | POST | Create or modify shortcuts. |
| `/api/config` | POST | Replace configuration. |
| `/api/config/patch` | PUT | Deep-merge JSON into configuration. Omitted fields are preserved. |
| `/api/run` | POST | Create an ephemeral errand. See below. |
| `/api/file-action` | POST | Run a bounded non-interactive command in a project sandbox or on the host (root-only). |
| `/api/highlight` | POST | Syntax-highlight code via host highlighter (root-only). |

### Ephemeral errands

`POST /api/run` creates an unnamed errand. `host` errands run outside bwrap with the
tmux environment plus `TERM`, `COLORTERM`, and `SLOPWORLD_*`. `like` names an existing
session whose sandbox config (presets, network, DNS, limits, mounts) is copied onto the
new errand. An empty shell command uses the daemon's `$SHELL`.

### Private state

Reset moves agent private state to 14-day trash. Root-only inventory reports active,
orphan, and trash entries with sizes. Permanent deletion is limited to orphan and trash
entries. Restore works only while the agent exists without a replacement tree.

### Configuration patching

The patch route deep-merges nested JSON, validates the result, and preserves omitted
fields. Project JSON carries the network default; session JSON carries effective network
plus nullable `network_override`. DNS is optional tagged JSON:
`{"mode":"resolved"}` or `{"mode":"servers","servers":["IPv4", ...]}`.

## WebSocket

### Server events

| Event | Description |
| --- | --- |
| `capabilities` | Runtime integration flags. Sent on connect. |
| `sessions` | Session state, title, and bell. |
| `screen` | Terminal content. Scrolled replies include `off`, `request_id`, and `history` (total scrollback rows). |
| `usage` | Quota window updates. |
| `projects` | Project catalog. |
| `shortcuts` | Shortcut catalog. |
| `jukebox` | Jukebox state. |

Catalogs arrive on connect and are resent when changed.

### Client messages

| Message | Description |
| --- | --- |
| `sub` / `unsub` | Subscribe or unsubscribe to a session's screen updates. |
| `keys` | Send keystrokes to an agent. |
| `resize` | Negotiate terminal size. |
| `scroll` | Scroll the terminal. |
| `mouse` | Send mouse events. |
| `paste` | Paste text. |
| `audio` | Audio control. Always includes `volume`; `selection` is a station key, local file, or `null` to stop. |

### Audio state

Audio state is `{playing, source, volume, error, title}`. Station titles come from ICY
metadata. Stream URLs are not forwarded to the mod.

### Files mutations

Root-only: create, single-component rename, and recursive delete. Names cannot contain
slash, backslash, `.`, or `..`. Existing targets are preserved.
