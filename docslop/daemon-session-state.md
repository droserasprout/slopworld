# Session state and the emulator

`State` is `Down | Working | Waiting | Idle`, serialised lowercase.
`Manager::classify` tries the `[[state_rule]]` regexes against the pane's plain
text (SGR stripped); with no hit, a pane that moved inside `IDLE_MS` is working.
Down is not a rule - it comes from the control reader ending on `%exit` or EOF.

## How a session went, and the siren

`quit` rides beside the state: the process left on purpose. The mod's colonist
lies down either way, but only a `Down` it was not shown the door for sounds an
alarm - a Ctrl+C, a Ctrl+D or an `exit 0` is not an emergency.

tmux keeps no exit status for a session it has already destroyed, and
`remain-on-exit` would leave a dead pane where `start` expects nothing at all. So
`wrap_exit` runs the agent under one line of shell that writes the status into
`$XDG_RUNTIME_DIR/slopworld/exit/NAME` on the way out; `mark_down` takes the file
and `quit` is that status being zero. The path rides as `$0` and the argv as
`"$@"`, so nothing is quoted into a script a command line could break out of, and
the wrapper is the pane's process with bwrap as its child - outside the sandbox,
so the write needs no bind.

The path is derived from the name rather than remembered, or a daemon restart
would leave the panes it reattached to writing where nothing reads. **A shell
killed by a signal writes nothing**, and a session with no file behind it reads as
one that fell over, which is the answer we had before any of this. `stop` sets
`quit` itself for that reason - hanging up on a pane kills the wrapper too - and
sets it **before the kill**, the control reader being able to get through
`mark_down` while `stop` is still waiting on tmux; `mark_down` or's rather than
assigns for the same race. `start` clears the flag and the file both, so neither
ever speaks for an older process.

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

## The emulator

A control-mode client (`tmux -C attach`, on a pty) feeds `%output` into the
emulator, which renders on an **8ms coalescing tick**. `%output` is the pane's
bytes *raw* - tmux parses them for its own screen and copies them to control
clients untouched - so escapes an app aims at its terminal arrive here.

`emu.rs`'s `Side`:

- **OSC 0/2 (title)** onto `Frame::title`, and it counts toward a frame being
  *changed*, or a title moving on a still screen would never be sent.
- **OSC 52 (copy)** onto the host clipboard: the only word we get when an app draws
  its own selection, as Claude Code does. `Osc52::OnlyCopy` - it may write, never
  read. Only the `c` selection; we have no tool for PRIMARY.
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
