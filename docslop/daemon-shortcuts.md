# Shortcuts (errands)

`[[shortcut]]` is an errand: a project, something to run there, a line to type
into it. `kind` is `prompt` or `shell`; empty `command` means `[defaults] agent`
or the `shell` preset.

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
  paste takes a pasted newline as a newline.
- `wait_ready` waits for output followed by `SETTLE_MS` of silence, not for a
  pattern; hitting `READY_MS` does not cancel delivery.
- `POST /api/run` is the same errand with nothing written down: the body *is* the
  entry. `Manager::run_errand` is everything past the check; `run_shortcut` is a
  lookup in front of it. The inline road does not repeat `check_shortcut`'s "must
  have something to send" - the files view's `less` is a command that is the whole
  errand - so `deliver` is spawned only for a non-empty `text`, or an agent with
  nothing to type would sit through `READY_MS` to type it.
