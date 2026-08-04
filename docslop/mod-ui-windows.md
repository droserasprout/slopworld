# Dialogs and options pages

The GUI windows write straight through to the daemon; `TerminalSettingsWindow` is
the exception, editing [mod settings](mod-settings.md) instead.

- **`ProjectsWindow`** - the first button in the bottom bar, ahead of `agents`,
  because nothing can be added there until there is somewhere to add it. Preset
  checkboxes come from `GET /api/presets`; an unknown preset is warned about and
  ignored. Greyed-and-shown beats hidden throughout.
- **`PresetList`** - those checkboxes wherever they are ticked (a project's, and one
  agent's own), grouped by the `category` each preset states, because the table is
  a directory of files and not a list this half keeps in step. Fetched on every
  dialog open rather than once per process, for the same reason. What an agent's
  *command* asks for is drawn ticked and refused: "why is `~/.claude` bound" is the
  question that answers.
- **`EditSessionDialog`** - picks a command preset, or neither: `Command` empty with
  a `Cmd` typed is a command line of its own; empty with no `Cmd` is whatever
  `[defaults] agent` names. `Agent` is the daemon's resolved answer and is **never
  written back** - it would pin today's default into the file.
- **`EditProjectDialog`** - its three path boxes are what the project *adds*;
  `DoEffective` draws the merge (`[sandbox]`, presets, boxes, deduplicated the way
  `paths()` does, then *sorted*). It says **asked for** rather than handed over,
  because `paths()` drops a bind whose path is not on this machine and only the
  daemon knows which. `PresetInfo` keeps `Ro`/`Rw`/`Env` apart for this, `Gives`
  being the flattened tooltip view.
- **`ConfigPage`** - one page: the daemon, `[defaults]`, and the base every sandbox
  is built on. The field column is a scroll view sized from the previous frame's
  `CurHeight`, its listing begun on a rect far taller than it needs so nothing
  breaks to a second column ([gotchas](gotchas.md)). The connection is *stated*
  there, not edited - mod settings owns it because that is the half still
  changeable with the socket down - and the button goes through to
  `Dialog_ModSettings`. `bind` and `token` stay in `SlopConfig` undrawn: a field
  missing from `ToJson` is one the next unrelated save resets to its serde
  default. Not a `Window`: it is the first category of the options menu, see
  [mod-patches-strip](mod-patches-strip.md).
- **`UsagePage`** - the second category, and its two halves are saved by different
  roads on purpose. The switches, the key file and the poll interval are
  `config.toml` under two headings, one per seller, so they go over HTTP and need
  a daemon; the icons are mod settings written on the click, so they can be set
  with the socket down and survive it. A row is offered for every window the
  daemon is currently reporting **plus** `session`, `week`, `spend` and `balance`,
  so a quota can be dressed before it is first seen - or before its own switch is
  even on. The palette is a fixed handful of `ThingDef`s resolved by name on first
  use, skipping any this build has not got; "auto" is a button rather than a cell,
  "whichever you would have picked" not being a thing.
- **`ShortcutsWindow`** - Run closes the window on the *answer*, opening a terminal
  on whatever the daemon started; an `ask` errand's Run opens a float menu of every
  project plus a temporary one. A temporary agent draws without Edit or Del,
  because the daemon would refuse both.

**No sandbox switch anywhere**: every agent runs in one, and `ProjectCfg::sandbox`
is the preset list. A switch that could be off in one place silently beat every
checkbox in the other.
