# Shared UI chrome

Start with `UiTheme`, `UiText`, `UiButtons`, `UiControls`, `UiLayout`, and `Slab`.
New controls should use their measurement, styling, and hit-testing paths. `WorkspaceLayout` owns the geometry
snapshot shared by rendering, input, terminal size and Harmony hooks. See
[layout ownership](ui-dynamic-layout-architecture.md) and [focus](ui-focus.md).

Labels containing catalog sprites use the shared text layout/renderer in `UI/Text/`.
Measurement and truncation preserve catalog keys and plain text elements.
Drawing clips the positioned spans to the label box. The atlas preserves caller opacity without inheriting text
tint, and missing artwork cannot change layout. Plain labels retain the native text path.

IMGUI events must share stable geometry and control IDs. Measure/draw passes cannot mutate
form data differently. Cache layout by content and text metrics (including atlas/UI scale),
not color alone. `GameFont.Tiny` may render Small.
Use shared measurement helpers.

`SmoothScroll` owns fractional wheel input and terminal-style scrollbars. Consume precise input once.
A delayed Unity wheel event must not scroll a second time. Drawing and hit tests need the
same viewport clipping. Drag owners must respect `hotControl`, including replayed events.
Flat result lists route wheel-only passes through `HandleWheel` using their last measured
extent before model filtering, layout rebuilding or control allocation. The next normal pass
refreshes geometry.
Clicks and scrollbar drags retain normal control IDs. Do not skip nested
scroll owners. Flat owners can call `HandleWheel(outer)` to reuse their last `Begin` extent.
Resize requires new measurement. Keep native row controls clipped to visible rows on
ordinary passes too, retaining a focused read-only field while it is offscreen. `ScrollWheelRouter` wraps Settings pages outside their field-focus scope and
records the `SmoothScroll` tree on ordinary passes. Wheel passes replay only those regions,
with current parent translations and inner-first spending, so page measurement and row work
cannot amplify a touchpad backlog. Bounds/origin changes, failed captures and intervening
GUI groups use the ordinary path.
Page closure invalidates the snapshot. Other hosts can
retain a router around renderers whose wheel handling belongs entirely to `SmoothScroll`. X11 discovery and valuator queries run on one dedicated background sampler,
with at most one request in flight and no queued backlog. IMGUI only consumes the latest
completed snapshot and falls back to Unity input while sampling is pending. The native sampler
re-reads master-pointer axes on each query, retries transient absence/failure with a one-second
backoff, and marks source changes so recovery starts with a fresh baseline. The profile's
Smooth scrolling preference disables native sampling and uses logical wheel steps immediately.
Logical fallback invalidates older native motion so late replies cannot move the viewport twice.

`Window.Margin` translates the GUI group. It is not padding. Shared windows use zero margin
and explicit body padding. An absorbing window can consume MouseDown before controls see it.
Field replay must not start a second drag or apply a click to two overlapping targets.

Menus need independent window instances per submenu level. Vanilla same-type replacement
and promotion of a clicked outside window can destroy or bury a chain. Close menus before
promoting the fullscreen host. `FloatMenuOption.Disabled` is inferred from a null action,
so a submenu opener needs an action even if its work happens elsewhere.
Menus that replace their option list after an asynchronous catalog update must close the
old submenu and reset its row indices through `UiMenu.ReplaceOptions`.
`MenuRowGeometry` owns retained offsets for hit testing, keyboard reveal, and visible rows.
Option count, row height, or explicit replacement invalidates measurement; wheel passes
reuse the scroll extent. Menu width measurement stops at its cap. Font pages retain catalogs
for their lifetime. Settings measures category geometry once per frame and skips unrelated
wheel input and category drawing on wheel passes.

`WheelEventQueue` compacts compatible runs at the root GUI boundary while Settings or menus
are open, including Layout passes. Scan once per frame to avoid quadratic work on mixed
backlogs. Preserve pointer/modifier/display boundaries, direction reversals, and other event
order. `SmoothScroll` uses logical deltas during compaction frames.

The top bar has both map and terminal draw paths, but only one may handle input. It can lie
outside the active window: use its own rectangles rather than window-relative hover helpers.
Resource-owning helpers must restore `RenderTexture.active` and release temporary textures
on failure as well as success.

`TextEntryController` caches stripped native field/area styles by source style and UI metrics,
checking font identity/size/style as well. Do not clone `GUIStyle` or allocate `RectOffset`
per field per event: wheel fast paths do not remove ordinary Layout/repaint allocation costs.

See [Linux window state](ui-window-fullscreen.md) for native title and fullscreen ownership.
