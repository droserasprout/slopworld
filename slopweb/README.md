# slopweb

Local browser Eco workspace using the installed Waylith/RimWorld 1.7 game and
SlopWorld mod in WebAssembly. No colony is created or loaded. Agent processes,
tmux, sandboxes, and daemon credentials stay on the host.

## Install and run

From a checkout with the .NET 10 SDK, Node.js 22+, Python/uv, Just, and a local
Waylith/RimWorld installation:

```sh
just slopweb-install
slopweb
# Open http://127.0.0.1:8787/index.html?entry&assets&eco&workspace&live&app
```

Source installation defaults to `~/.local`; ensure `~/.local/bin` is on PATH.
Pass a prefix to `just slopweb-install PREFIX` or `just slopweb-uninstall PREFIX`.
Uninstall preserves private runtime caches and daemon configuration.

Installed packages require the .NET 10 runtime, Node.js 22+, and Python 3.11+;
they do not need the SDK, Just, uv, or a checkout. Start slopd first. The launcher
reads its host-side local endpoint descriptor, respecting `SLOPD_CONFIG_ROOT`
and `SLOPD_ENDPOINT`. `SLOPD_URL` and `SLOPD_TOKEN` override that descriptor.
Use a root token for session creation and configuration. `--endpoint URL` and
`--token-file PATH` also select a host-side connection. Tokens are never placed
in browser assets or URLs.

`--game PATH` (or `RIMWORLD`) selects another game installation. `--port` defaults
to 8787. First launch prepares local game files under `$XDG_CACHE_HOME/slopweb`
(default `~/.cache/slopweb`); subsequent launches reuse that private runtime.
`--prepare` prepares without serving; `--rebuild` refreshes after game data changes.
The server binds only to 127.0.0.1. Stop it with Ctrl+C; agents continue running.

## Packages

```sh
just package-web arch
just package-web debian
just package-web fedora
# Equivalent: just package-product DISTRIBUTION slopweb
```

Use the corresponding distribution's packaging tools. slopweb is independent of
the ordinary native package group. Packages include our browser tools, mod UI
assets, and redistributable dependencies. They exclude game assemblies, Core
data, shaders, saves, daemon credentials, and generated runtime caches. Those game
files are read from the user's own installation during local preparation.

## Interface

The sidebar selects agents; `+` creates sessions. Session-only agent rows use the
mod's robot-face artwork without colony pawns. Ctrl+V pastes browser text; selected
terminal text copies with Ctrl+C or Ctrl+Shift+C. Without a selection, Ctrl+C still
interrupts the terminal process. Browser clipboard permissions apply to context
menu actions. Settings are available through the gear icon.

Web and game may share a terminal: the browser sends new pane sizes on local
geometry changes or reconnect, and does not continually override another client's
size. A shared tmux session still has one grid, so a wider client can leave content
outside a smaller pane's visible area.

This remains a prototype: an existing live session is required at startup, font
metrics are approximate, and clipboard images/full IME remain unsupported.
Profile settings do not survive reload. Reconnect recovery and the full agent
launch/edit dialog have not been exhaustively validated. See the checkout's
`experiments/browser/README.md` for focused checks and implementation boundaries.
