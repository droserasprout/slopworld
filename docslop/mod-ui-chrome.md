# Shared UI chrome

`SlopWidgets` owns chrome shared by windows and embedded views: colours, font-derived
heights, spacing, buttons, fields, headings, and error messages. Terminal colours remain
in `TerminalTheme`.

Text geometry must go through `LineHOf`, `Wide`, and `RowLabel`. Verse may promote
`GameFont.Tiny` to Small, and its measurements change when word wrapping is enabled.
Hard-coded heights have previously clipped glyphs. Shared gaps are `GapXS`, `GapS`,
`GapM`, and `GapL`.

`Slab` draws every control: fill, outline, box, focus ring, hairline. Square, and no
texture — see [mod-adwaita](mod-adwaita.md). It snaps geometry to the screen pixel grid;
GUI-coordinate snapping produces seams at non-integer UI scales. `TerminalWindow.SyncSnap`
uses the same arithmetic.

`SmoothScroll` is the only scroll view in the mod, and it draws the only scrollbar. It
consumes wheel events before IMGUI and eases toward a target using unscaled time; it must
consume the event or nested scroll views apply the same wheel input again. The bar is drawn
in `End`, outside the scroll group, which is the only place the outer rect means what it
says. Callers reserve 18px off the view width for it. Vertical only. `Reveal` puts a
keyboard-selected row in view without easing to it.

`SlopWindow` is the frame under every dialog: own background, border and close corner.
`Margin` is nought because vanilla's margin translates the contents into a group rather
than padding them, so the body is inset by `Pad` here instead. Existing form geometry is
unchanged by that trade.

`SlopMenu` replaces `FloatMenu` as the dropdown, taking the same `List<FloatMenuOption>`.
It reads the label, the action, `Disabled` and the right-justified extra part, and ignores
everything vanilla's pawn-order menus use.

`SlopLayout` is the single source of sidebar/top-bar insets. `Hidden` is intentionally
separate from layout selection: hiding chrome must not move the rest of the UI.

## Usage and top bar

`UsageReadout` renders remaining quota reported by the daemon. Icon assignments are stable
per key and may be overridden by `Settings.usageIcons`. Missing rows expected from an
enabled source remain as placeholders so an expired login does not look like the resource
was removed. Freshness comes from the snapshot's successful-poll timestamp, not message
arrival time. The real clock is the strip's final resource, immediately before the colony
doors, rather than a centred divider.

`TopBar` is drawn from the map component and, over a terminal, by `TerminalWindow`; only
one copy may register input. Doors are laid out right-to-left before quota so transient
usage rows do not move them. The jukebox and persona-core doors exist only while their
things exist.

Top-bar input cannot use `Mouse.IsOver`: the bar may be drawn outside the active window,
and the options view deliberately leaves it interactive. Door hit tests use their rects
directly, while map-click absorption retains the stricter interaction gate.

## Other pieces

- `MenuToggle` supplies checked float-menu rows without abusing `Disabled`.
- `SlopListWindow<T>` holds the common agents/projects/shortcuts window structure.
- `CoreTip` draws the persona-core hint over either the map or an opaque terminal.
- `MenuBackground` bakes and caches the menu/loading-screen frames — see
  [mod-background](mod-background.md). Its patch hooks drawing, because the loading
  screen bypasses the main menu's initialization path.
