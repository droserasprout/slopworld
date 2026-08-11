# `SlopSettings`

In `SlopWorldMod.cs`, reached through the static `Settings` shim: `autoConnect`,
`sidebar`, `sidebarWidth`, `foldedProjects`, `sidebarTab`,
`sidebarShowHidden`, `usageIcons`, `fontSize`, `fontName`, `theme`, `cursorColor`,
`radio`, `radioMute`, `radioStopOnExit`, `grandmaMode`, `ecoMode`, `ecoDim`.

Adding one means a field, a `Scribe_Values.Look`, a shim property and a widget.

**Not `config.toml`**: RimWorld's own `ModSettings`, scribed into
`Config/Mod_SlopWorld_SlopWorldMod.xml` under the profile, named for the mod
folder and the `Mod` subclass. `Scribe_Values` writes nothing equal to its
default, so a file holding only `<ModSettings Class="SlopWorld.SlopSettings" />`
is an install where none of them was touched, not a failed save. The daemon's file
is about this *machine*; this one is about this *install*.

## The ones with no widget

`sidebarWidth`, `foldedProjects`, `sidebarTab`, `sidebarShowHidden` - the column
is dragged by its edge, folded by its headings and switched by its own strip, so
`AgentSidebar` writes all four itself. That is the whole reason they are settings
rather than fields on the sidebar: a width, a fold, a view and what it lists are
about this screen the way `sidebar` is, and they are wanted back tomorrow. An
unknown `sidebarTab` reads as the agents, that being the view always worth having.

`usageIcons` is the fifth of that kind: which quota wears which thing is the
daemon's row drawn on this screen, so it is settings rather than `config.toml` -
and it is still legible with the socket down, which is when somebody is in that
page reading rather than configuring. One `key=defName` per line, written on the
click by `UsageReadout.Choose` (`Settings.S.Write()`). A line for a key nothing
reports is a line nothing reads.

The jukebox's station, `radio`, has no options widget: the box on the map is its picker.
`radioMute` and `radioStopOnExit` are also drawn on the Audio options page; both roads go
through `Radio.Save` and write on the click. See [mod-jukebox](mod-jukebox.md).

`sidebar` is the one that is not about the daemon or a pane's legibility: both
layouts are this mod's and which one works is a question about the screen being
read (see `SlopLayout` in [mod-ui-chrome](mod-ui-chrome.md)). Drawn on
`ConfigPage`, the page a knob is looked for on, and on the gear, the one settings
window reachable with a pane over the bottom bar. `ConfigPage` writes it on the
click, its own Save button being the daemon's file.

## Writing

The connection is discovered from the daemon's `endpoint.json`. The pane's own
settings are edited in `TerminalSettingsWindow` off the gear, and the daemon
configuration is edited through `ConfigPage`.

The file is written **once, in `PostClose`, by `ModSettings.Write`** rather than
`Mod.WriteSettings` - the latter reconnects the socket.

- A size change also calls `TerminalFont.Invalidate`: the style rebuilds off the
  size, but the per-glyph fit verdicts are measured at one size and the pane's
  cache is keyed on the cell it was drawn at.
- The scheme calls `TerminalTheme.Invalidate`.
- The cursor field needs neither, since `Resolve` compares the hex it was given.

Which terminal was open belongs to a *colony*, so `TerminalRecall` scribes it into
the save; writing mod settings on every switch would also mean a reconnect.
