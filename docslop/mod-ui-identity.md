# Rectangular SlopWorld

The UI is a dark instrument panel, not a theme derivative: dark wells, restrained edges,
pale text, a blue action signal, and semantic green/yellow/red states. `SlopWidgets` is the
single palette; terminal ANSI colours stay in `TerminalTheme`.

Everything is rectangular. `Slab` draws opaque faces, one-screen-pixel edges, focus rings,
and rules snapped to the screen grid. There is no corner radius, gradient, elevation, or
drop shadow. Hover and press change colour, never geometry.

Spacing follows a 4/8/16/24 rhythm (`GapXS`, `GapS`, `GapM`, `GapL`). Shared sizes cover
buttons, fields, rows, menus, icons, status markers, and the 18px scrollbar gutter; callers
derive their layout from those tokens rather than reserving nearby values.

Focus rings appear only around real text input. Status uses a rectangular outlined badge and
square marker rather than a capsule or dot.
