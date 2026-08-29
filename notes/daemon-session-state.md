# Session state and the emulator

`State` is `Down | Working | Waiting | Idle`, serialised lowercase.
`Manager::classify` tries the `[[state_rule]]` regexes against the pane's plain
text (SGR stripped); with no hit, a pane that moved inside `IDLE_MS` is working.
Down is not a rule - it comes from the control reader ending on `%exit` or EOF.

**The rules read the bottom of the screen, not the screen** (`match_rules`,
`TAIL_LINES`). A pane is not a transcript: the question an agent asked is still
standing after it has been answered, so rules run over the whole thing keep
answering `waiting` while the agent works. The last `TAIL_LINES` non-blank lines
are walked **upwards** and the lowest line any rule matches decides; config order
only breaks a tie inside one line. That ordering is the point - what is live on a
screen is always below what is finished, a spinner's `esc to interrupt` being the
last line there is - and it is why the fix is not a reordering of the two seeded
rules, which are a config somebody already has on disk. Out of reach of the tail
a rule says nothing at all, and the clock decides: a screen that said `do you
want` a page ago is idle, not waiting.

## Two clocks, neither standing in for the other

- `last_change` is the *pane's*, reset by every redraw. `classify` reads it to
  decide a quiet pane has gone idle.
- `state_since` is `Live::set_state`'s, and the only answer to "how long has it
  been working" - a working agent redraws several times a second, so its
  `last_change` is always now.

Every road to a new state goes through `set_state` for exactly that reason.

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

`emu.rs`'s `Side`:

- **OSC 0/2 (title)** onto `Frame::title`, and it counts toward a frame being
  *changed*, or a title moving on a still screen would never be sent.
- **OSC 52 (copy)** onto the host clipboard: the only word we get when an app draws
  its own selection, as Claude Code does. `Osc52::OnlyCopy` - it may write, never
  read. Only the `c` selection; terminal middle-click reads PRIMARY separately through
  the host clipboard tools.
- **BEL** onto `Frame::bell`, taken in `render` rather than `render_frame` so a
  wheel's scroll snapshot cannot swallow one. True for exactly one frame; what
  holds a ring afterwards is `Live::bell`, cleared by `clear_bell` when a client
  *subscribes* to that pane - a pane on screen being the only evidence here that
  somebody looked.

The clip is one slot, not a queue, and the control loop leaves it there while a
write is in flight, so an app stating OSC 52 every frame gets one `wl-copy` at a
time.

## `SessionView`

Carries `title` and `bell` for *every* session rather than the subscribed one - a
session is a pane the mod cannot see unless it is showing it, and a title is the
one line of status any TUI hands over without being parsed for it. Both dirty the
session list, which is why the bell is asked about ahead of `apply_frame`'s early
way out: it is a list event on a screen that never moved. A title is read off the
last frame, so a session `mark_down` has dropped the screen of states none.

`BOOT_COLS`/`BOOT_ROWS` in `session.rs` is what a pane wears until someone looks
at it - see [mod-terminal](mod-terminal.md) for `NegotiateSize`.

`SessionView.slopworld_md` echoes the durable opt-in that gives an agent the generated
project-root runtime manifest and its discovery breadcrumb.
