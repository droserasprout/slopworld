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
- **`ConfigPage`** - one page: the daemon, `[defaults]`, and the game. The sandbox base
  and presets moved to `SandboxPage`. The field column is a scroll view sized from the
  previous frame's `CurHeight`, its listing begun on a rect far taller than it needs so
  nothing breaks to a second column ([gotchas](gotchas.md)). The connection is *stated*
  there, not edited - mod settings owns it because that is the half still
  changeable with the socket down - and the button goes through to
  `Dialog_ModSettings`. `bind` and `token` stay in `SlopConfig` undrawn: a field
  missing from `ToJson` is one the next unrelated save resets to its serde
  default. Not a `Window`: it is the first category of the options menu, see
  [mod-patches-strip](mod-patches-strip.md).
- **`SandboxPage`** - the base every sandbox is built on, and the presets that add to
  it, grouped under the headings "Global" and "Presets" on one tab. Global is
  `[sandbox]` in `config.toml`, saved over HTTP like the rest of the machine's
  config; Presets is the daemon's preset directory drawn *read-only* - which presets
  a project uses is that project's checkbox, ticked on `EditProjectDialog`. The page
  is the second category of the options menu, see
  [mod-patches-strip](mod-patches-strip.md).
- **`UsagePage`** - the second category, and its two halves are saved by different
  roads on purpose. The switches, the key file and the poll interval are
  `config.toml` under two headings, one per seller, so they go over HTTP and need
  a daemon; the icons are mod settings written on the click, so they can be set
  with the socket down and survive it. A row is offered for every window the
  daemon is currently reporting **plus** `session`, `week`, `spend` and `balance`,
  so a quota can be dressed before it is first seen - or before its own switch is
  even on. The palette is a hand-picked handful of `ThingDef`s resolved by name on
  first use and dropped if unresolved - vanilla counts a hundred and twenty-odd
  resources and generates a meat def per animal, and twenty leathers are cells
  rather than choices. Names are checked against the game's own defs, eleven
  having been typed rather than looked up (`FineMeal` for `MealFine`), which had
  the grid quietly drawing twenty-five cells of thirty-six. **Automatic is the
  first cell of the grid**, not a button beside the icon: it is something you
  pick, the same way silver is, and null all the way through - picking it is what
  clears the line.
- **`ShortcutsWindow`** - Run closes the window on the *answer*, opening a terminal
  on whatever the daemon started; an `ask` errand's Run opens a float menu of every
  project plus a temporary one. A temporary agent draws without Edit or Del,
  because the daemon would refuse both.

## `CommandPalette` and `Fuzzy`

F1, top centre, and the reason nothing is only reachable from a dialog nobody has
found: every window and button is an `Entry` here, recently used first (`RecentMax`
8). An entry with a `SubAction` asks a second question in the same box rather than
opening a dialog - "Agent: Stop" then picks which agent - and Backspace with an
empty filter is the way back out of one.

F1 is answered there too while the options menu is open (`Patch_OptionsHotkeys`): a
window absorbing input makes `HandleEventsHighPriority` use every KeyDown ahead of
the game components, so the command palette is the one place still allowed to hear
it. F12 likewise closes the options and reveals the terminal pane underneath.

`Fuzzy` is the search behind it, and a substring test is the wrong shape for a
list of `Noun: Verb` rows: nobody types the colon, so `v c` and `vc` both have to
find "View: Config". The query splits on whitespace, **every term must appear as a
subsequence** and the terms may arrive in any order. Ranking is the whole of the
tuning and is where a matched character sits - `Head` (front of the text) beats
`Boundary` (after a separator or a camelCase hump) beats `Consecutive` beats
`Base`; a term found whole is worth `Verbatim` again, `AtWord` more if it starts
one, and `LeadMax` caps what a late first character can cost. `Match` hands back
the matched positions with the score and `Highlight` wraps them in `Mark`, a fuzzy
hit whose reason cannot be seen reading as a wrong one.

**No sandbox switch anywhere**: every agent runs in one, and `ProjectCfg::sandbox`
is the preset list. A switch that could be off in one place silently beat every
checkbox in the other.
