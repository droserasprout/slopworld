# Rectangular SlopWorld

The UI is a dark instrument panel, not a theme derivative: dark wells, restrained edges,
pale text, a blue action signal, and semantic green/yellow/red states. `SlopWidgets` owns
the only color *names* anything else in the mod knows; terminal ANSI colors stay in
`TerminalTheme`.

## Color schemes

The values behind those names are a `UIScheme`, picked on the Appearance page and held in
`SlopSettings.uiScheme`. A scheme is a table of colors and nothing else — no geometry, no
gaps, no shapes — so the panel is the same instrument under every one of them, standing in
a different light. `slopworld` is the only house scheme. The remaining entries are named
classic palettes: One Dark, Dracula, GNOME light/dark, Tango light/dark, Solarized
light/dark, Gruvbox, Nord, Monokai and VS Code Dark+.

The catalog deliberately uses stable IDs for persisted settings and human labels for the
picker. `onedark`, `gruvbox` and `nord` retain their earlier IDs; `solarized` remains the
terminal ID for Solarized Dark, while the UI catalog uses the explicit `solarized-dark` ID.
Unknown or retired IDs resolve to SlopWorld.

Named palette values are adapted to SlopWorld's semantic roles rather than pretending that
an editor theme publishes every widget color. The UI remains rectangular and keeps its
geometry, but surfaces, text ramps, accents and state colors follow the selected palette.

Schemes are written in hex, `#rrggbb` or `#rrggbbaa`, parsed by `TerminalTheme.TryHex`. The
eighth digit is what makes the table readable: a structural line, a hovered row and a text
ramp are one color at several strengths, and they belong on one line each. A scheme lays
those washes in its own foreground rather than in white — white over a blue-grey panel is a
colder line than the panel was drawn expecting.

`SlopWidgets` reads `UIScheme.Current` per access and `Current` re-resolves against the
setting, so a pick lands on the next frame with nothing to invalidate and nothing to tell.
That works because no chrome color is ever baked into a texture — unlike the pane, whose
row cache is keyed on `TerminalTheme.Rev`. Anything that starts baking one has to grow the
same counter.

Everything is rectangular. `Slab` draws opaque faces, one-screen-pixel edges, focus rings,
and rules snapped to the screen grid. There is no corner radius, gradient, elevation, or
drop shadow. Hover and press change color, never geometry.

Spacing follows a 4/8/16/24 rhythm (`GapXS`, `GapS`, `GapM`, `GapL`). Shared sizes cover
buttons, fields, rows, menus, icons, status markers, and the 18px scrollbar gutter; callers
derive their layout from those tokens rather than reserving nearby values.

Focus rings appear only around real text input. Status uses a rectangular outlined badge and
square marker rather than a capsule. The one circle is an agent's state badge in the corner
of its sidebar portrait: that marker sits on a face rather than in a panel, and presence on a
face is a dot everywhere else a person has ever seen one.
