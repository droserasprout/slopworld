# UI and terminal theme identity

`UIScheme` owns chrome semantic roles; `TerminalTheme` owns ANSI and pane colors.
Settings persist stable IDs rather than picker labels. Match UI follows the current
UI scheme's same-ID terminal theme. Explicit terminal choices remain independent.

Shipped definitions live one theme per file under `mod/Themes/UI/` and
`mod/Themes/Terminal/`; `ThemeCatalog` loads them at runtime. Every UI scheme must
have a same-ID terminal theme, while terminal-only IDs are allowed. `Well` and `Sel`
are derived roles, not editable catalog roles. Accent, destructive, and checkbox
faces must be opaque because their contrast-derived text has no backing-surface input.

`ThemeLoader` shares runtime loading and error recovery. `UIScheme` and
`TerminalTheme` retain their own lazy caches, record conversion, and fallback palettes.

[Palette references](reference-original-palettes.md) own upstream value and role
policy. [Build commands](build-commands.md) own catalog validation. Shared geometry
and metrics belong to [chrome](mod-ui-chrome.md); terminal texture invalidation
belongs to [terminal rendering](mod-terminal-rendering.md).
