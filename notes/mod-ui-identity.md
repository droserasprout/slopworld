# UI identity and color schemes

`UiWidgets` owns named chrome colors and semantic states; terminal ANSI colors stay in
`TerminalTheme`.

## Color schemes

The values behind those names are a `UIScheme`, picked on the Appearance page and held in
`ModSettings.uiScheme`. A scheme is a color table; geometry, spacing, and shapes come from
the shared panel. The three SlopWorld Warm, Cold, and Calm entries are complete house tables. The remaining
entries are named palettes adapted to the UI's semantic roles.

The catalog deliberately uses stable IDs for persisted settings and human labels for the
picker. Unknown IDs resolve to the default Warm scheme.

Named palette values are adapted to SlopWorld's semantic roles rather than assuming that an
external palette defines every widget color. The adapter keeps text and structural roles
separate, and solid faces carry matching foreground roles.

Schemes use `#rrggbb` or `#rrggbbaa` values parsed by `TerminalTheme.TryHex`. Structural
lines, hovered rows, and text ramps use the scheme's foreground roles rather than a fixed
white, so palettes retain their intended contrast.

`UiWidgets` reads `UIScheme.Current` per access and `Current` re-resolves against the
setting, so a pick lands on the next frame with nothing to invalidate and nothing to tell.
That works because no chrome color is ever baked into a texture — unlike the pane, whose
row cache is keyed on `TerminalTheme.Rev`. Anything that starts baking one has to grow the
same counter.

`Slab` draws opaque faces, one-screen-pixel edges, focus rings, and rules snapped to the
screen grid. There is no corner radius, gradient, elevation, or drop shadow. Hover and press
change color, never geometry.

Spacing follows a 4/8/16/24 rhythm (`GapXS`, `GapS`, `GapM`, `GapL`). Shared sizes cover
buttons, fields, rows, menus, icons, status markers, and the 18px scrollbar gutter; callers
derive their layout from those tokens rather than reserving nearby values.

Focus rings appear only around real text input. Status uses a rectangular outlined badge and
square marker. An agent's state badge is the sole circular marker, placed in the corner of
its sidebar portrait.

Warm uses dark brown surfaces and a muted amber accent; Cold uses charcoal and blue;
Calm keeps Cold surfaces with a muted steel accent. New settings default to Warm with
the terminal set to Match UI.
