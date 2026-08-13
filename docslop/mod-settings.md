# `SlopSettings`

`SlopSettings` is RimWorld install/profile state, reached through the static `Settings`
shim. It is written to `Config/Mod_SlopWorld_SlopWorldMod.xml` with `Scribe_Values`;
it is not the daemon's machine-wide `config.toml`.

The fields cover connection, sidebar state, quota display, terminal/UI fonts and
themes, cursor, radio, status-bar readouts, Grandma mode and Eco mode. Adding one
requires a field, `Scribe_Values.Look`, a shim property and a widget. Scribe omits
defaults, so an otherwise empty settings element is valid.

## Fields without a Settings-page widget

Sidebar width, folds, selected tab and hidden-row filtering are written directly by
`AgentSidebar`; quota icon choices are written by `UsageReadout.Choose`. They describe
the current screen and must remain readable when the daemon is offline. `usageSpent`
selects left versus spent quota globally. `radio` is selected by the map jukebox;
mute and stop-on-exit are also exposed on Audio. Status-bar flags only hide readouts
and doors; they do not disable polling, audio or map objects.

`sidebar` is the layout mode, not daemon configuration. It is changed from the
configuration page and gear menu, while that page's Save button belongs to the daemon
file. Storage inventory is likewise an operation view: it reads daemon state and owns
reset, restore and delete actions.

## Writing and invalidation

The connection comes from `endpoint.json`. `AppearancePage` edits the global interface
settings and `TerminalPage` edits pane settings; `ConfigPage` edits daemon configuration.
Mod settings are written once in `PostClose` by `ModSettings.Write`, not through
`Mod.WriteSettings`, because the latter reconnects.

Font or size changes invalidate `TerminalFont`; terminal scheme changes invalidate
`TerminalTheme`. Cursor and UI scheme resolve on read and need no cache invalidation.
The open terminal belongs to the colony save and is handled by `TerminalRecall`, not
by writing mod settings on every selection.
