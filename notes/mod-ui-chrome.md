# Shared UI chrome

`UiTheme`, `UiText`, `UiButtons`, `UiControls` and `UiLayout` own shared window/view chrome:
scheme colors, font-derived geometry, spacing, buttons, fields, headings and errors.
`UiWidgets` is an explicit compatibility facade while callers migrate. Color names resolve through
`UIScheme` ([mod-ui-identity](mod-ui-identity.md)); terminal colors stay in
`TerminalTheme`.

- Measure text with `LineHOf`, `Wide`, `RowLabel` and `StatusLabelHeight`. `GameFont.Tiny` may
  draw as Small, and wrapping changes its measurement. `RowLabel` supplies the middle anchor
  and disables wrapping; `StatusLabel` supplies scheme color, font, anchor and wrapping for
  ordinary status blocks. `RowLabel` has one geometry path for truncation, vertical placement,
  pixel snapping and widget-state restoration; italic preview labels only replace the final
  draw style. Use `Widgets.Label` directly only for custom Markdown, terminal,
  tooltip or selection rendering. `UiFont` gives every UI tier bottom safety space and overflow
  clipping so dynamic-font descenders survive tight label, tooltip and field rects. Shared gaps
  are `GapXS`, `GapS`, `GapM` and `GapL`.
- Single-line fields, menu rows and compact row buttons use `CompactH`; the field's
  hover/press wash is the same button wash, and `Slab` owns every text-entry background,
  edge and focus ring. `FieldFrame`/`BareField` are the composite-input escape hatch. Shared
  fields replay mouse-downs consumed by an absorbing window, expose Cut/Copy/Paste/Select all
  on right-click, and use the daemon's PRIMARY selection for middle-click paste.
  Text entries allocate a name-hinted control ID through `TextEntryController` and pass it to
  Unity's native text-field renderer; selection, read-only restoration, deferred clipboard edits
  and focus release use that controller and exact ID so one row cannot clamp another row's cursor
  to its text length.
- Listing fields and areas reserve `GapXS` after their captions and `GapS` after their control
  through the shared overloads. Pass `defaultValue` for a monochrome reset icon in the top
  right, with the complete default in its tooltip (`null` omits reset; `""` resets to empty).
  The icon has a reserved gutter and updates the focused native editor along with the form.
  Reset follows the caller's normal save/dirty behavior.
  Checkboxes and selectors use the same trailing `GapS`.
  Fixed-rectangle composite rows own their internal spacing.
  Caption gaps subtract the listing's automatic vertical spacing so fields and selectors
  have the same visible caption-to-control margin.
- `Slab` draws every control (fill, outline, focus ring and hairline). It is texture-free
  and snaps to the screen pixel grid; GUI-coordinate snapping seams at non-integer UI
  scales. `TerminalPanel.SyncSnap` uses the same arithmetic. Rules sit inside the
  control they close, on its last pixel.
- `UiButtons` owns button/background hit behavior, `UiControls` owns checkbox, selector and
  slider composition, and `UiLayout` owns window/page placement helpers. The old `UiWidgets`
  entry point forwards explicitly to those owners; it no longer inherits their static APIs.
- `SmoothScroll` is the mod's only scroll view and scrollbar. On X11 it reads XInput 2.1's
  fractional scroll valuator directly; Unity's logical wheel packet is the fallback. It
  draws the bar in `End` outside the scroll group and reserves `ScrollbarW`; `Reveal`
  jumps a selected row into view. Brief frame stalls preserve cumulative touchpad movement;
  a logical wheel packet is suppressed only after a precise sample was claimed, or when it
  is the matching delayed packet from the preceding frame. Vertical scroll only.
- `UiScrollBody.Measure` returns one geometry result with a named `Always` or `WhenNeeded`
  scrollbar reservation. The pure `ScrollableGeometry` math preserves content height and
  wrapping width at, below and above the viewport; each host still owns its `SmoothScroll`.
- `ScrollableListing` retains a `SmoothScroll` and frame-stable `SettingsContentHeight` for
  `Listing_Standard` forms. Its optional trailing callback is measured inside the same view;
  footers, overlays and page data remain with their hosts.
- `TextureReadback.ReadBack` owns the temporary ARGB32 render target and CPU copy used by
  cursor and menu-background baking. It restores `RenderTexture.active` before releasing the
  target and destroys the copy on both success and failure.
- Sidebar resizing, scrollbars and the terminal divider honor Unity's `hotControl` capture.
  Replaying a consumed mouse-down cannot start a second drag; hiding the sidebar releases
  its resize capture.
- `TerminalPanel` uses `SmoothScroll.BeginInput`/`EndInput` without a translated GUI group.
  It spends the claimed input before choosing a frame, then `TerminalHistory` assembles the
  fractional viewport from overlapping daemon snapshots and an overscan row.
- `ContentTreeView` keeps complete row geometry for scrolling but paints and hit-tests only
  rows near the viewport; scroll-event passes reuse the measured height, update the offset and
  skip repainting the tree.
- `UiWindow` supplies the frame, border and close corner. `Margin` is zero because
  vanilla translates contents into a group instead of providing padding; bodies use
  `Pad`.
- `UiTheme.Wide` and `TruncateText` use bounded caches keyed by text metrics, UI scale and
  dynamic-font atlas revision. Labels still run on every IMGUI event; direct icon and ThingIcon
  draws in shared buttons and top-bar doors are Repaint-only while their hit paths remain live.
- `UiMenu` replaces `FloatMenu` and reads only the label, action, `Disabled` and
  right-side extra text from `FloatMenuOption`; rows touch the frame vertically and
  use the darker `PopoverBg`. Up/Down, Home/End and PageUp/PageDown move a keyboard
  selection; Enter opens or chooses it, and Left/Right move through submenu levels.
- `UiSubmenu` keeps one window per level and builds its rows when opened. Hover opens
  after `OpenDelay`; the child follows a scrolling/clamped parent, chooses the roomier
  side without covering it, and shares its border. Selecting a row or pressing Escape
  closes the chain before the action. Unhandled keys dismiss it via `rawType` in the
  window body because high-priority input may consume the event first; the event remains
  unused so interface shortcuts can still act on the same press.
- `MenuRowH` and `PaletteRowH` are separate; the latter is one gap step taller for
  keyboard navigation.
- `ActiveTip.DrawInner` is patched once per tooltip. `TooltipHandler.TipRegion` still
  owns delay, placement, stacking and size; only the final surface/text draw changes.
- `WorkspaceLayout` is the source of the immutable navigation/top-bar/content geometry
  snapshot. `UiLayout` keeps compatibility inset helpers; `Hidden` is separate from layout
  selection so screenshot-filtered chrome does not move other UI. Navigation may be left or
  right, and the default/compact `UiMetrics` preset keeps font-derived minimum control heights.
  Metrics are captured once per IMGUI frame; density, typography/atlas and UI-scale revisions
  invalidate layout separately while scheme colors only repaint. `UiComposition` provides the
  pure row/column measure-and-arrange math; Appearance caches its arranged form/preview bounds
  and switches to one scrollable column when the viewport is short.

## Usage and top bar

- `TopBarMapComponent` draws the bar from the map layer while `UsageReadout` supplies daemon-
  reported quota, leading with remaining or spent values from the global Usage setting. Icon
  choices are stable per key and overrideable through `Settings.usageIcons`; enabled sources
  keep missing rows as placeholders. Freshness uses the last successful poll, and a failed
  source dims only its own rows. The clock is independent of usage health, and is right-aligned
  beside colony doors by default, or reserved at the bar centre / omitted by the Appearance
  setting. The selected session's status shows its state marker, name and task title.
- Quota rows and counts cache by usage snapshot, mutable poll flags, display mode and culture.
  Clock/expiry text refreshes each second; measured widths invalidate on font, atlas or UI scale
  changes. Hidden clocks skip formatting and measurement; hover registration remains per draw.
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
- `UiListView<T>` shares the agents/projects/Library window structure.
- `UiTable` shares the fixed-column settings-table chrome between Usage and Summaries;
  flexible columns absorb remaining width and fixed columns shrink together when needed.
- `CoreTip` draws the persona-core hint over the map or an opaque terminal.
- `MenuBackground` bakes and caches menu/loading frames; its patch hooks drawing because
  the loading screen bypasses main-menu initialization ([mod-background](mod-background.md)).
