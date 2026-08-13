# Flat UI

The flat UI migration is complete and has been checked in-game. `SlopWidgets` owns
the palette, `Slab`, buttons, fields, headings, spacing, row chrome and form heights;
`TerminalTheme` is the only separate colour system.

- Use `Slab` (or its `Snap`/`Hairline` helpers) for every new shape. UI scale is
  1.75 here; do not round GUI coordinates manually or use `ScreenToGUIPoint`.
- Measure with `LineHOf`, `Wide` and `RowLabel`. `GameFont.Tiny` can resolve to Small.
- Use `GapXS`/`GapS`/`GapM`/`GapL` and `Header`/`SectionHeading`/`PageCaption`/
  `PageBody`/`FooterBar` for shared page geometry.
- Forms use names such as `form.field`; equal names share IMGUI focus.
- Long `Listing_Standard` pages need a tall rect and `maxOneColumn = true`.

The palette uses the same controls and palette as the rest of the UI. Its command
registry and focus model remain local to `CommandPalette`; do not infer a shared
command system from the visual migration.

The remaining visual tuning is deliberately small: consider a shared `Chip` helper
for state pills and revisit `Slab.R` or the row accent only if an in-game screen
shows a consistency problem.
