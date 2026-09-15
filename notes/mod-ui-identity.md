# UI colors and identity

`UIScheme` owns chrome semantic roles; `TerminalTheme` owns ANSI and pane colors. Persist
stable IDs, not picker labels. Match UI intentionally couples the terminal palette to chrome;
explicit terminal choices remain independent.

Shipped palettes live one theme per file under `mod/Themes/UI/` and `mod/Themes/Terminal/`.
`ThemeCatalog` loads those files at runtime; optional contiguous `order` fields pin the three
SlopWorld themes first, while all other IDs sort alphabetically. `make validate-themes` rejects
duplicate IDs, missing roles, invalid colors, and ANSI rows other than 16 before the mod is built.
`Well` and `Sel` remain derived roles, and the solarized Match UI alias remains in code.

Named upstream palettes must preserve their values and roles. Adapt missing widget roles
explicitly rather than silently altering upstream colors to satisfy contrast checks.
[Palette references](reference-original-palettes.md) record source values and fidelity gaps;
house palettes can be tuned directly.

Chrome colors resolve on read. Any new texture that bakes them needs revision invalidation,
as terminal textures already do. Shared `Slab` geometry is flat and pixel-snapped; hover must
not change bounds. Spacing and text metrics belong to shared chrome, not individual schemes.
