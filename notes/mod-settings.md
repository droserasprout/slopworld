# `ModSettings`

`ModSettings` is RimWorld install/profile state, reached through the static `Settings`
shim. It is written to `Config/SlopWorld.toml`; it is not the daemon's machine-wide
`config.toml`.

The fields cover connection, sidebar state, command-palette history, quota display,
terminal/UI fonts and themes, cursor, radio, status-bar readouts, Grandma mode and
Eco mode. Adding one requires a field, one typed `Fields` table entry, a shim property and
a widget unless it is a screen cache such as command-palette history.

Appearance > Interface > Display stores `displayMode` (`game`, `sync`, `limit`) and `foregroundFps`
(default 60, effective range 30–360). Missing or unknown modes use Game default,
preserving existing foreground settings. Changes apply live in both Eco and gameplay;
unfocused windows use 15 FPS. `FramePolicy` saves and restores the game's pacing pair.

## Fields without a Settings-page widget

Sidebar width, folds, selected tab, agent status filtering and hidden-row filtering are
written directly by
`AgentSidebar`; quota icon choices are written by `UsageReadout.Choose`. They describe
the current screen and must remain readable when the daemon is offline. `usageSpent`
selects left versus spent quota globally. `radio` is selected by the map jukebox;
mute and stop-on-exit are also exposed on Audio. Status-bar flags only hide readouts
and doors; they do not disable polling, audio or map objects.

Command-palette history stores up to eight command IDs, newest first, in the mod profile;
the palette drops IDs absent from its catalogue when it loads.

`statusbarClockPosition` is `right`, `center` or `hidden`. `timeFormat` is `24-hour`
or `12-hour` and controls the status-bar clock and its tooltip.

`statusbarAgentIndicators` controls the optional tiny `a`/`r`/`h`/`t` flags on the second line
of Agents rows: autostart, resume on start, effective host networking, and persistent `/tmp`.
Unlike the other status-bar flags, it only changes sidebar text.

`sidebar` is the layout mode, not daemon configuration. It is changed from Appearance >
Sidebar and the gear menu, while General's Save button belongs only to the daemon's
Experimental switches. Storage inventory is likewise an operation view: it reads daemon
state and owns reset, restore and delete actions.

`ConfigPage` also refreshes `/api/health` and shows connection state, host/sidecar runtime,
daemon version and hostname, followed by the mod and RimWorld versions. Missing health
metadata displays `?`; it does not prevent the page from showing the online state.

## Writing and invalidation

The connection comes from `endpoint.toml`. `AppearancePage` edits the global interface and
display settings, `SidebarPage` edits navigation layout and agent indicators, `TerminalPage`
edits pane settings, and `StatusbarPage` edits statusbar presentation; `ConfigPage` edits
daemon configuration.
Mod settings are written atomically by `ModSettings.Write` when the Settings view closes,
and dirty values also flush periodically.
The table uses typed field references with the existing parsers and formatters; runtime
dirty state is excluded. Game-free tests round-trip every public setting through disk.

Font or size changes invalidate `TerminalFont`; terminal scheme changes invalidate
`TerminalTheme`. Cursor and UI scheme resolve on read and need no cache invalidation.
The open terminal belongs to the colony save and is handled by `TerminalRecall`, not
by writing mod settings on every selection.
`UiControls.SetSetting`, `CheckboxSetting`, and the integer `SliderSetting` assign and
mark dirty only on changes. Callers explicitly invalidate font/theme caches when needed.
