# Flat UI

The flat UI migration is complete and has been checked in-game. `SlopWidgets` owns the
scheme-driven controls, `Slab` owns shapes, and `TerminalTheme` remains the terminal's
separate colour system. Use those helpers for new UI; `Listing_Standard`, `Widgets.Label`,
and IMGUI fields are only layout/text/input primitives behind the shared styling.

Measure with `LineHOf`, `Wide`, and `RowLabel`; use the shared gaps and page geometry
helpers. Forms use stable names such as `form.field`, and long `Listing_Standard` pages
need a tall rect with `maxOneColumn = true`. RimWorld-owned `Dialog_Options` and
`Dialog_ModSettings` remain API hosts; their SlopWorld controls use these helpers.
