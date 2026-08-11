# Shared UI chrome

`SlopWidgets` owns chrome shared by windows and embedded views: colours, font-derived
heights, spacing, buttons, fields, headings, and error messages. Terminal colours remain
in `TerminalTheme`.

Text geometry must go through `LineHOf`, `Wide`, and `RowLabel`. Verse may promote
`GameFont.Tiny` to Small, and its measurements change when word wrapping is enabled.
Hard-coded heights have previously clipped glyphs. Shared gaps are `GapXS`, `GapS`,
`GapM`, and `GapL`.

`Slab` draws every control: fill, outline, box, focus ring, and hairline. Square, and no
texture — see [mod-ui-identity](mod-ui-identity.md). It snaps geometry to the screen pixel grid;
GUI-coordinate snapping produces seams at non-integer UI scales. `TerminalWindow.SyncSnap`
uses the same arithmetic. A rule sits *inside* the thing it closes, on its last pixel: a
strip that ruled below itself would not contain its own line, and two strips of the same
height would meet with a jog in the boundary.

`SmoothScroll` is the only scroll view in the mod, and it draws the only scrollbar. It
consumes wheel events before IMGUI and eases toward a target using unscaled time; it must
consume the event or nested scroll views apply the same wheel input again. The bar is drawn
in `End`, outside the scroll group, which is the only place the outer rect means what it
says. Callers reserve `ScrollbarW` off the view width for it. Vertical only. `Reveal` puts a
keyboard-selected row in view without easing to it.

`SlopWindow` is the frame under every dialog: own background, border and close corner.
`Margin` is nought because vanilla's margin translates the contents into a group rather
than padding them, so the body is inset by `Pad` here instead. Existing form geometry is
unchanged by that trade.

`SlopMenu` replaces `FloatMenu` as the dropdown, taking the same `List<FloatMenuOption>`.
It reads the label, the action, `Disabled` and the right-justified extra part, and ignores
everything vanilla's pawn-order menus use.

A `SlopSubmenu` row carries a list where an option carries an action. The pointer opens it
beside its own row once it has rested there `OpenDelay`, so a pointer crossing the menu leaves
no trail of opened lists — one window per level, chained parent to child, marked with the arrow
a folded sidebar head wears — and the list is built when the pointer arrives, so rows that mark
what is playing or selected are current. A submenu's rect is worked out every frame, not kept
from the moment it opened: the parent scrolls under a resting pointer and is itself shoved near
a screen edge. It never covers its parent — the room either side is measured and it takes a
side rather than sliding across one, narrowing itself where neither side can hold it — and the
two frames share one border, level with the row that opened it. Picking a row anywhere
closes the whole chain before its action runs, and Escape closes the chain rather than one
level of it: the pointer is the only thing that walks back up a menu opened on hover. The
chrome's own F-keys call `SlopMenu.CloseAll` where they are read — in `HandleFunctionKey` and
again in `TerminalHotkeys` for the map layer — since each of them replaces what the menu was
standing over. Nesting
by closing one menu and opening another is what left a parent standing after a value two levels
down was picked.

`MenuRowH` is the dropdown's row and `PaletteRowH` the command palette's, a gap step taller —
one is a block read at a glance, the other a list stepped through with the keyboard.

`ActiveTip.DrawInner` is patched once for every hover bubble. Registrations remain ordinary
`TooltipHandler.TipRegion` calls, so vanilla still owns delay, placement, stacking and size;
only the final atlas and text draw are replaced with the popover surface and border.

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

- `TickBox` is the checkbox, on its own: the box `Checkbox` wears, drawn rather than
  clicked, because every other caller has a row that is the hit target already. It goes
  before the label everywhere — `MenuToggle` hangs one on a float-menu row without abusing
  `Disabled`, and the palette's sub list draws one for options that carry a state — so a
  tick reads the same in a settings page, a menu and the palette.
- `SlopListWindow<T>` holds the common agents/projects/shortcuts window structure.
- `CoreTip` draws the persona-core hint over either the map or an opaque terminal.
- `MenuBackground` bakes and caches the menu/loading-screen frames — see
  [mod-background](mod-background.md). Its patch hooks drawing, because the loading
  screen bypasses the main menu's initialization path.
