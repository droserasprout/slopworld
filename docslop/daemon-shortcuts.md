# Shortcuts (errands)

`[[shortcut]]` names a project, a command and text to deliver. `kind` is `prompt` or
`shell`; an empty `command` uses `[defaults] agent` or `shell`. `kind = "breadcrumb"`
defines named guidance instead of an errand, and projects/agents attach breadcrumb
names through `breadcrumbs`.

`kind = "fa"` defines a Files-sidebar action. Its `command` is a command line. By default the
selected absolute path is appended; `{{ absolute_path }}` and `{{ relative_path }}` substitute the
quoted absolute path and project-relative path respectively. A project root's relative path is
`.`. It is offered from a file or directory context menu as captured output in the SlopWorld
alert window or an interactive temporary terminal; terminal runs leave a shell open after the
command so short output remains readable. File actions are not agent errands and do not attach
through `breadcrumbs`.

- `builtin` entries come from `config::builtin_shortcuts`, are not written to
  `config.toml`, and cannot be updated/removed. A file entry with the same name shadows
  the builtin and becomes editable. `shortcuts_all` is the wire list; the sidebar hides
  builtins, while breadcrumb lists show them. `Useful tips` is the built-in five-tip
  breadcrumb.
- `{{ random_tip }}` is filled from loading-screen tips supplied by the game in
  `random_tips`; each mention draws without replacement, restarting the pool if needed.
  Empty input or unknown expressions remain literal. Breadcrumbs also resolve
  `{{ agent }}`, `{{ project }}`, `{{ directory }}` and `{{ command }}` from the target
  session.
- `link` (`project`|`temp`|`ask`) controls how `project` is read: named project, a
  temporary copied sandbox, or no project. `check_shortcut` requires a project only for
  `project`; `RunWhere` gives `temp` precedence and rejects `ask` with neither.
- `temp` projects live in `Manager::temp` until `forget`; `/tmp` cleanup removes their
  directory. Reads combine `live` then `temp`.
- `Config::session_for` uses the default agent preset for an empty prompt command, the
  named preset when supplied, or a literal command. A shell errand uses the shell preset
  with its text as `cmd`.

## Ephemeral agents

`Live.ephemeral` entries are never written to config and have no `Down` state. `mark_down`
and `stop` remove them because killing tmux aborts the control reader first. `remove`
writes nothing; `Manager::session_cfg` reads config or the live table.

Unknown sessions under the daemon socket are adopted as projectless ephemeral agents:
they cannot restart, but watching, typing and killing still work.

## Delivery

- `POST /api/shortcuts/NAME/run` returns the session name and delivers asynchronously;
  the mod's five-second HTTP timeout is shorter than agent startup.
- `deliver` pastes text and sends Enter separately. For agents with breadcrumbs, the
  daemon queues breadcrumb text, an `ENTER_GAP_MS` `Input::Gap`, then the first user
  Enter, so the breadcrumb is submitted with the draft and later typing stays behind it.
  A restart arms this again.
- `breadcrumb_block` puts one-line entries in a shared bullet list and multiline entries
  in their own paragraph. `Live.breadcrumbs_pending` tells the window whether the next
  Enter carries breadcrumbs; `send_keys` clears it.
- `SessionCfg.breadcrumb_yolo` (default true for old/new entries unless disabled) controls
  that splice. The terminal menu can paste any breadcrumb without Enter through the
  websocket `breadcrumb` message; the daemon validates and renders it.
- `wait_ready` waits for output plus `SETTLE_MS` silence; reaching `READY_MS` alone does
  not cancel delivery.
- `POST /api/run` is the inline form: the body is the errand. `run_shortcut` looks up an
  entry; `Manager::run_errand` performs it. `deliver` is spawned only for non-empty text,
  so a command-only errand such as Files' `less` does not wait to type nothing.
