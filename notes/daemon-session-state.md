# Session state and the emulator

`State` is `Down | Working | Waiting | Idle`, serialised lowercase.
`Manager::classify` tries the `[[state_rule]]` regexes against the pane's plain
text (SGR stripped); with no hit, a pane that moved inside `IDLE_MS` is working.
Down is not a rule - it comes from the control reader ending on `%exit` or EOF.

Rules scan the last `TAIL_LINES` non-blank lines upward. The lowest matching line
wins; configuration order breaks ties within a line. This prevents an old prompt
higher on the screen from keeping a working agent in `waiting`.

## State clocks

`last_change` records pane redraws and decides when a quiet pane becomes idle.
`state_since` tracks time in the current state. All transitions must use
`Live::set_state` so frequent redraws cannot reset the state age.

`state_since` is also written to private options on the tmux server when a durable session
changes state. If its tmux pane survives a daemon restart, the first capture is treated as a
snapshot and restores that age; `session-activity.toml` is only a fallback when those options
are absent. An intentional stop, start, exit or removal clears the record; a rename carries it
with the tmux session and moves the fallback entry. Ephemeral sessions are never cached.

## The emulator

A control-mode client (`tmux -C attach`, on a pty) feeds `%output` into the
emulator, which renders on an **8ms coalescing tick**. `%output` is the pane's
bytes *raw* - tmux parses them for its own screen and copies them to control
clients untouched - so escapes an app aims at its terminal arrive here.

tmux is the pane's terminal and answers terminal queries itself. The local mirror ignores its
VT engine's `PtyWrite` events: injecting a second device-attributes response after tmux's answer
leaves the duplicate in the shell input queue (visible as `?6c`).

The emulator captures OSC titles, clipboard writes, and bells. Title changes dirty
the frame even when screen text is unchanged. OSC 52 is write-only
(`Osc52::OnlyCopy`) and handles CLIPBOARD; PRIMARY is read separately by host
clipboard tools. Clipboard writes use one pending slot and one subprocess at a time.

Bells are consumed by live rendering, not scroll snapshots. `Live::bell` retains
the notification until a client subscribes. Titles and bells also dirty the session
list, including when the screen did not change, so inactive tabs receive updates.
A session without a retained frame has no terminal title.

## Session views

`BOOT_COLS`/`BOOT_ROWS` in `session/mod.rs` set the initial pane size until a client
negotiates it; see [mod-terminal](mod-terminal.md) for `NegotiateSize`.

`SessionView.slopworld_md` echoes the durable opt-in that gives an agent the generated
project-root runtime manifest. Its mount destination and optional discovery breadcrumb come
from `[daemon.instructions]`; `SessionView.instructions_breadcrumb` echoes the agent's
default-on opt-out for that discovery line.
`SessionView.persistent_tmp` echoes the durable opt-in that binds the agent's private state
`tmp` directory over the sandbox's per-run `/tmp` tmpfs.
