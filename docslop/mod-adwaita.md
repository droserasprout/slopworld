# Square Adwaita

The look is libadwaita's dark theme with every corner radius set to nought. Colours are
the library's own, named after it in `SlopWidgets`, hex in the comment beside each so a
drift from upstream is one grep away.

Two rules carry most of it:

- **Surfaces are opaque and differ by lightness.** `WindowBg` #242424, `ViewBg` #1e1e1e,
  `HeaderBg` #303030, `PopoverBg` #383838 — four flat greys, not one grey at four alphas
  over the game. A card at any transparency shows RimWorld's stone through the middle of
  a page, the options window still being vanilla's.
- **Controls are white over their surface.** A button is not a colour: it is the surface
  plus 10% white, 15% hovered, 30% pressed. One face is then correct on all four surfaces
  without being told which it is on. Text is the same trick — white at falling opacity,
  which is what Adwaita's own dim-label is.

Accent is #3584e4 and destructive #c01c28; both are solid, and neither carries a border.
Disabled is half opacity on the face and the label, never a darker face — these faces are
translucent white, and darkening one makes it lighter.

There is no relief anywhere: no sheen, no drop shadow, no button that moves when pressed.
A press is a face colour and nothing else.

Focus rings are drawn only where focus exists. `Field` and `Area` have real IMGUI focus,
tracked by control name, and get an accent ring outside the rect. Buttons and check boxes
have no keyboard focus in this UI at all, so they draw no ring — a ring that can never
appear is dead code, not a feature.
