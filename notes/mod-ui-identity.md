# UI colors and identity

`UIScheme` defines chrome semantic roles.
`TerminalTheme` defines ANSI and pane colors. Persist
stable IDs, not picker labels. Match UI intentionally connects the terminal palette to chrome.
Explicit terminal choices remain independent.

Shipped palettes live one theme per file under `mod/Themes/UI/` and `mod/Themes/Terminal/`.
`ThemeCatalog` loads those files at runtime.
Optional contiguous `order` fields place the three SlopWorld themes first.
All other IDs sort alphabetically. Run `make validate-themes` before you build the mod.
The target rejects duplicate IDs, missing roles, invalid colors, and ANSI rows other than 16.
`Well` and `Sel` remain derived roles. UI and terminal theme IDs match so Match UI needs no aliases.

Named upstream palettes must preserve their values and roles. Adapt missing widget roles
explicitly rather than silently altering upstream colors to satisfy contrast checks.
[Palette references](reference-original-palettes.md) record source values and differences from those values.
You can adjust SlopWorld palettes directly.

Chrome colors resolve on read. Any new texture that bakes them needs revision invalidation,
as terminal textures already do. Shared `Slab` geometry is flat and pixel-snapped.
Hover must not alter bounds. Spacing and text metrics belong to shared chrome, not individual schemes.
