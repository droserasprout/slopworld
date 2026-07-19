# SlopWorld

RimWorld, but colonists are clankers ¯\_(ツ)_/¯

## Shape

RimWorld runs inside Wine; tmux and bwrap run on the host. The mod cannot fork
host processes, so the split is:

- **`slopd`** (Rust, systemd user service) owns tmux, the sandboxes and
  `config.toml`. Serves HTTP + WebSocket on loopback, which passes straight
  through Wine.
- **`SlopWorld`** (C# mod, Harmony) declines to tick the sim, maps sessions to
  colonists, and renders the terminal. With the mod loaded the vanilla game is a
  viewer: most of RimWorld's UI, notifications and the starting colonists are
  stripped. See [DROPS.md](DROPS.md) for the full list.

The terminal emulator lives in `slopd`. It attaches to each tmux session in
control mode (`tmux -C`) and feeds the raw byte stream into a real VT engine
(`alacritty_terminal`), so it owns the full grid: wide chars, cursor shape,
scrollback, and the app's mouse / alt-screen / bracketed-paste modes. Every
frame is serialized back to SGR colour runs (plus column markers for wide chars)
that the mod draws and nothing more. Input goes back as `tmux send-keys`; mouse
events and pastes are encoded to the app's own protocol first.

## Install

    make install

That builds both halves, enables `slopd.service`, and drops the mod in
RimWorld's `Mods/`. Enable it in the in-game mod list and restart.

Override paths if your install is elsewhere:

    make install RIMWORLD="$HOME/.steam/.../RimWorld"

## Use

Sessions live in `~/.config/slopworld/config.toml`, editable by hand or from the
**Agents** tab in game (which also has add / edit / remove / restart and a raw
config editor).

```toml
[[session]]
name = "killergram"          # also the colonist's name
dir = "~/git/Killergram"
agent = "claude"             # optional; falls back to [defaults].agent
net = true
sandbox = true
autostart = true
```

Each session runs as `tmux -L slopworld` + `bwrap`, so you can also reach one
from a real terminal:

    tmux -L slopworld attach -t killergram

### Agent state

The daemon classifies the rendered screen text. The rules are config, not code,
so retarget them at whatever agent you run:

```toml
[[state_rule]]
state = "waiting"
pattern = "(?i)(do you want|❯\\s*1\\.)"

[[state_rule]]
state = "working"
pattern = "(?i)esc to interrupt"
```

No rule matching falls back to: pane changed recently → `working`, else `idle`.
A missing tmux session is `dead`.

### Sandbox

Each session gets its own mount namespace: the project dir read-write, the
agent's state dir (`~/.claude`) read-write, `/usr` and `/etc` read-only, and
nothing else. `net = false` also unshares the network namespace. Tune the binds
under `[sandbox]`.

When the network is shared, slopd fixes up `/etc/resolv.conf`, which
systemd-resolved / NetworkManager make a symlink into `/run` that the sandbox
otherwise doesn't mount (leaving it dangling, so lookups fail with ENOENT and
agents see an API error). It prefers resolved's stub listener `127.0.0.53`,
reachable over the shared loopback, so split-DNS - e.g. a Tailscale uplink -
routes correctly instead of an upstream answering NOTIMP.

## Terminal keys

Everything is forwarded to the agent, including bare `Escape`. To leave the
terminal use **Shift+Escape** or the Close button.

The mouse works inside TUIs: when the app asks for the mouse (Claude Code,
`less`, vim) the wheel, clicks and drags are forwarded to it; otherwise the wheel
walks scrollback and drag selects text. **Shift** forces local selection even
when the app wants the mouse. `Ctrl+V` pastes from the system clipboard, wrapped
in bracketed-paste markers when the app supports them.
