# Shared UI chrome

`SlopWidgets` owns shared window/view chrome: scheme colors, font-derived geometry,
spacing, buttons, fields, headings and errors. Color names resolve through
`UIScheme` ([mod-ui-identity](mod-ui-identity.md)); terminal colors stay in
`TerminalTheme`.

- Measure text with `LineHOf`, `Wide` and `RowLabel`. `GameFont.Tiny` may draw as
  Small, and wrapping changes its measurement. `RowLabel` supplies the middle anchor
  and disables wrapping; use `Widgets.Label` directly only for wrapped or top-aligned
  blocks. Shared gaps are `GapXS`, `GapS`, `GapM` and `GapL`.
- Single-line fields, menu rows and compact row buttons use `CompactH`; the field's
  hover/press wash is the same button wash, and `Slab` owns every text-entry background,
  edge and focus ring. `FieldFrame`/`BareField` are the composite-input escape hatch.
- `Slab` draws every control (fill, outline, focus ring and hairline). It is texture-free
  and snaps to the screen pixel grid; GUI-coordinate snapping seams at non-integer UI
  scales. `TerminalWindow.SyncSnap` uses the same arithmetic. Rules sit inside the
  control they close, on its last pixel.
- `SmoothScroll` is the mod's only scroll view and scrollbar. It consumes wheel events
  before IMGUI, eases with unscaled time, draws the bar in `End` outside the scroll
  group, and reserves `ScrollbarW`; `Reveal` jumps a selected row into view. Vertical
  scroll only.
- `SlopWindow` supplies the frame, border and close corner. `Margin` is zero because
  vanilla translates contents into a group instead of providing padding; bodies use
  `Pad`.
- `SlopMenu` replaces `FloatMenu` and reads only the label, action, `Disabled` and
  right-side extra text from `FloatMenuOption`; rows touch the frame vertically and
  use the darker `PopoverBg`.
- `SlopSubmenu` keeps one window per level and builds its rows when opened. Hover opens
  after `OpenDelay`; the child follows a scrolling/clamped parent, chooses the roomier
  side without covering it, and shares its border. Selecting a row or pressing Escape
  closes the chain before the action. Any key dismisses it via `rawType` in the window
  body because high-priority input may consume the event first; the event remains unused
  so F1 can still open the palette.
- `MenuRowH` and `PaletteRowH` are separate; the latter is one gap step taller for
  keyboard navigation.
- `ActiveTip.DrawInner` is patched once per tooltip. `TooltipHandler.TipRegion` still
  owns delay, placement, stacking and size; only the final surface/text draw changes.
- `SlopLayout` is the source of sidebar/top-bar insets. `Hidden` is separate from layout
  selection so hidden chrome does not move other UI.

## Usage and top bar

- `TopBarMapComponent` draws the bar from the map layer while `UsageReadout` supplies daemon-
  reported quota, leading with remaining or spent values from the global Usage setting. Icon
  choices are stable per key and overrideable through `Settings.usageIcons`; enabled sources
  keep missing rows as placeholders. Freshness uses the last successful poll. The clock is
  right-aligned beside colony doors by default, or reserved at the bar centre / omitted by the
  Appearance setting.
- `TopBar` draws from a map component and, over a terminal, from `TerminalWindow`; only
  one copy handles input. Appearance settings can hide Usage, place or hide Clock, or hide
  the Jukebox or Computer Core (`GM`) independently. Doors lay out right-to-left before
  quota, so transient usage rows do not move them. The top bar keeps the settings gear and
  map-object doors; its right-most hamburger is gone. Jukebox/core doors exist only when
  their objects do.
- Top-bar hit tests use door rects, not `Mouse.IsOver`: the bar can be outside the active
  window and remains interactive over options. Map-click absorption keeps its stricter
  interaction gate.

## Other pieces

- `TickBox` draws the checkbox while the surrounding row handles the click. It precedes
  labels in settings, menus and palette sublists.
- `SlopListWindow<T>` shares the agents/projects/shortcuts window structure.
- `CoreTip` draws the persona-core hint over the map or an opaque terminal.
- `MenuBackground` bakes and caches menu/loading frames; its patch hooks drawing because
  the loading screen bypasses main-menu initialization ([mod-background](mod-background.md)).
