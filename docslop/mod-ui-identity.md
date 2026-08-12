# Rectangular SlopWorld

The UI is a dark instrument panel, not a theme derivative: dark wells, restrained edges,
pale text, a blue action signal, and semantic green/yellow/red states. `SlopWidgets` owns
the only colour *names* anything else in the mod knows; terminal ANSI colours stay in
`TerminalTheme`.

## Colour schemes

The values behind those names are a `UIScheme`, picked on the Appearance page and held in
`SlopSettings.uiScheme`. A scheme is a table of colours and nothing else — no geometry, no
gaps, no shapes — so the panel is the same instrument under every one of them, standing in
a different light. `slopworld` is the house scheme above; `onedark` is the editor palette,
its three greys as the surfaces and its syntax hues as the signals.

Schemes are written in hex, `#rrggbb` or `#rrggbbaa`, parsed by `TerminalTheme.TryHex`. The
eighth digit is what makes the table readable: a structural line, a hovered row and a text
ramp are one colour at several strengths, and they belong on one line each. A scheme lays
those washes in its own foreground rather than in white — white over a blue-grey panel is a
colder line than the panel was drawn expecting.

`SlopWidgets` reads `UIScheme.Current` per access and `Current` re-resolves against the
setting, so a pick lands on the next frame with nothing to invalidate and nothing to tell.
That works because no chrome colour is ever baked into a texture — unlike the pane, whose
row cache is keyed on `TerminalTheme.Rev`. Anything that starts baking one has to grow the
same counter.

Everything is rectangular. `Slab` draws opaque faces, one-screen-pixel edges, focus rings,
and rules snapped to the screen grid. There is no corner radius, gradient, elevation, or
drop shadow. Hover and press change colour, never geometry.

Spacing follows a 4/8/16/24 rhythm (`GapXS`, `GapS`, `GapM`, `GapL`). Shared sizes cover
buttons, fields, rows, menus, icons, status markers, and the 18px scrollbar gutter; callers
derive their layout from those tokens rather than reserving nearby values.

Focus rings appear only around real text input. Status uses a rectangular outlined badge and
square marker rather than a capsule. The one circle is an agent's state badge in the corner
of its sidebar portrait: that marker sits on a face rather than in a panel, and presence on a
face is a dot everywhere else a person has ever seen one.
