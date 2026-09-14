# UI colors and identity

`UIScheme` owns chrome semantic roles; `TerminalTheme` owns ANSI and pane colors. Persist
stable IDs, not picker labels. Match UI intentionally couples the terminal palette to chrome;
explicit terminal choices remain independent.

Named upstream palettes must preserve their values and roles. Adapt missing widget roles
explicitly rather than silently altering upstream colors to satisfy contrast checks.
[Palette references](reference-original-palettes.md) record source values and fidelity gaps;
house palettes can be tuned directly.

Chrome colors resolve on read. Any new texture that bakes them needs revision invalidation,
as terminal textures already do. Shared `Slab` geometry is flat and pixel-snapped; hover must
not change bounds. Spacing and text metrics belong to shared chrome, not individual schemes.
