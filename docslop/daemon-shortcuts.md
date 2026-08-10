# Shortcuts (errands)

`[[shortcut]]` is an errand: a project, something to run there, a line to type
into it. `kind` is `prompt` or `shell`; empty `command` means `[defaults] agent`
or the `shell` preset. `kind = "breadcrumb"` is the third kind: its `text` is a
named piece of guidance, not an errand. Projects and agents attach breadcrumb names
through `breadcrumbs` lists.

- `builtin` marks an entry the daemon ships (`config::builtin_shortcuts`), never
  written to `config.toml` and refused by `update_shortcut`/`remove_shortcut`.
  `Config::shortcut` reads the file first, so an entry a person writes with that
  name shadows it and is editable again, the way a user preset replaces a shipped
  one. `shortcuts_all` is what the wire sees. The sidebar's table hides builtins -
  there is nothing to do to one there - and the breadcrumb lists offer them.
  `Useful tips` is the only one: five `{{ random_tip }}` bullets under a rule.
- `{{ random_tip }}` is the one dynamic template function (`render_template`), filled
  from the loading screen's tips - the *game* holds those, so they ride in on
  `random_tips` with the run or the keystroke. Each mention *of one text* spends its
  own, so five bullets are five different lines; a pool shorter than the text starts
  again, and a second text renders off the same pool from the top.
  Empty - anything driving the daemon from outside the game - leaves the text as
  written rather than blanking it, as does a variable this build does not know.
  Breadcrumb rendering also knows `{{ agent }}`, `{{ project }}`, `{{ directory }}`
  and `{{ command }}`. They are resolved by the daemon from the target session; unknown
  expressions remain literal so the renderer can grow without corrupting old text.

- `link` (`project`|`temp`|`ask`) is how the entry's `project` field is *read*:
  where to run, the sandbox a scratch project copies, or nothing.
  `check_shortcut` insists on one only in the first case. `RunWhere` overrides;
  `temp` beats a named project, and an `ask` entry run with neither is the one
  refusal.
- A `temp` errand's project is coined in `run_shortcut`, held in `Manager::temp`,
  dropped by `forget`. The directory is not - /tmp is the machine's to clear. Both
  tables are read `live` then `temp`.
- `Config::session_for`: a prompt shortcut with no command runs the preset
  `[defaults] agent` names, because a preset is what hands it `~/.claude`; one
  naming a preset gets it; one naming a command line runs that. A shell errand is
  the `shell` preset with its line as that agent's `cmd`.

## Ephemeral agents

`Live.ephemeral`, never written to config, no `Down` state: `mark_down` forgets
it, and `stop` must too, by hand, because killing tmux aborts the control reader
first. `remove` writes no config. `Manager::session_cfg` reads config *or* the
live table, since `start` has nothing in the file to look up.

Anything running under our socket that config knows nothing about is `adopt`ed as
one of these with no project: blank directory, refuses to restart, while watching,
typing and killing work.

## Delivery

- `POST /api/shortcuts/NAME/run` answers with the session name and leaves typing
  to a task behind it: the mod's HTTP client gives up after five seconds, an agent
  is tens of seconds from ready.
- `deliver` pastes, then sends Enter as a *separate* keypress, because bracketed
  paste takes a pasted newline as a newline. Ordinary agents with attached
  breadcrumbs get the same treatment around the first user Enter: the daemon splices
  the breadcrumb paste, an `ENTER_GAP_MS` `Input::Gap` and then that key into the
  pane's own queue, so the draft becomes `fix this bug\n\n- never commit\n- always
  lint` and *is* submitted. The gap is the queue's, not a task's, so anything typed
  behind it stays behind it. A restart arms it again.
- `breadcrumb_block` decides the shape: one-liners share a bullet list, a breadcrumb
  that is already several lines is its own paragraph - `- ___` with the rest of
  `Useful tips` orphaned under it is what that avoids.
- `Live.breadcrumbs_pending` is on the session view, because the window reads it to
  decide whether an Enter is worth carrying a dozen tips for. `send_keys` clears it
  and announces, so the next Enter is an ordinary one.
- `SessionCfg.breadcrumb_yolo`, true for old and new entries unless explicitly disabled,
  controls that first-Enter splice. The terminal menu can paste any effective project or
  agent breadcrumb without Enter through the websocket `breadcrumb` message; the daemon
  validates membership and owns rendering on both roads.
- `wait_ready` waits for output followed by `SETTLE_MS` of silence, not for a
  pattern; hitting `READY_MS` does not cancel delivery.
- `POST /api/run` is the same errand with nothing written down: the body *is* the
  entry. `Manager::run_errand` is everything past the check; `run_shortcut` is a
  lookup in front of it. The inline road does not repeat `check_shortcut`'s "must
  have something to send" - the files view's `less` is a command that is the whole
  errand - so `deliver` is spawned only for a non-empty `text`, or an agent with
  nothing to type would sit through `READY_MS` to type it.
