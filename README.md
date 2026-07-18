# SlopWorld

RimWorld with the colony sim stripped out. Every colonist is a live AI coding
agent: a tmux session running under bubblewrap on the host, managed by a systemd
daemon. Select a colonist, hit Terminal, and you are typing at the agent.

The map becomes an ambient status board. A colonist with `! INPUT` over their
head is an agent blocked on a permission prompt.

## Shape

RimWorld runs inside Wine; tmux and bwrap run on the host. The mod cannot fork
host processes, so the split is:

- **`slopd`** (Rust, systemd user service) owns tmux, the sandboxes and
  `config.toml`. Serves HTTP + WebSocket on loopback, which passes straight
  through Wine.
- **`SlopWorld`** (C# mod, Harmony) declines to tick the sim, maps sessions to
  colonists, and renders the terminal.

The mod contains no terminal emulator. `tmux capture-pane -e` returns a screen
that tmux has *already* rendered, carrying only SGR colour escapes, so the mod
parses colour runs and nothing else. Input goes back as `tmux send-keys`.

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

The daemon scrapes the pane and classifies it. The rules are config, not code,
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

## Terminal keys

Everything is forwarded to the agent, including bare `Escape`. To leave the
terminal use **Shift+Escape** or the Close button. `Ctrl+V` pastes from the
system clipboard.
