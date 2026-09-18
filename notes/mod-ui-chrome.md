# Shared UI chrome

Start with `UiTheme`, `UiText`, `UiButtons`, `UiControls`, `UiLayout` and `Slab`; new controls
should use their measurement, styling and hit paths. `WorkspaceLayout` owns the geometry
snapshot shared by rendering, input, terminal size and Harmony hooks. See
[layout ownership](ui-dynamic-layout-architecture.md) and [focus](ui-focus.md).

IMGUI events must share stable geometry and control IDs. Measure/draw passes cannot mutate
form data differently. Cache layout by content and text metrics (including atlas/UI scale),
not color alone. `GameFont.Tiny` may actually render Small; use shared measurement helpers.

`SmoothScroll` owns fractional wheel input and scrollbars. Consume precise input once;
a delayed Unity wheel event must not scroll a second time. Drawing and hit tests need the
same viewport clipping. Drag owners must respect `hotControl`, including replayed events.
Flat result lists skip row work on wheel-only passes while still entering/disposing the
scroll scope; it applies every delta and the next repaint uses the latest position. Do not
skip nested scroll owners or normal control passes. X11 sampling has a per-frame budget,
including at most one late-wheel refresh, so queued events cannot each block on X11.

`Window.Margin` translates the GUI group; it is not padding. Shared windows use zero margin
and explicit body padding. An absorbing window can consume MouseDown before controls see it;
field replay must not start a second drag or apply a click to two overlapping targets.

Menus need independent window instances per submenu level. Vanilla same-type replacement
and promotion of a clicked outside window can destroy or bury a chain. Close menus before
promoting the fullscreen host. `FloatMenuOption.Disabled` is inferred from a null action,
so a submenu opener needs an action even if its work happens elsewhere.

The top bar has both map and terminal draw paths, but only one may handle input. It can lie
outside the active window: use its own rectangles rather than window-relative hover helpers.
Resource-owning helpers must restore `RenderTexture.active` and release temporary textures
on failure as well as success.
