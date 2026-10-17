# Shared UI chrome

`UI/Chrome/` owns shared controls and layout helpers. `UI/Theme/` owns styling and
icon catalogs; `UI/Workspace/` owns workspace host chrome. Start with `UiText`,
`UiButtons`, `UiControls`, `UiLayout`, and `Slab` when adding a control. Feature
owners supply status, availability, empty messages, and actions.

Measurement, drawing, and hit testing must share stable geometry and control IDs
across IMGUI passes. Measurement must not invoke controls, setters, or mutate form
data. Cache layout by content and text metrics; hover must not change bounds.
Shared chrome owns spacing and metrics. Chrome colors resolve on read, so textures
that bake them must invalidate when the theme changes.
`ActionStrip` shares full-row-height targets between drawing, tooltips, and click
dispatch for file and group actions. Its width includes padding around the glyphs
so labels stop before the action targets.

`TextEntryController` owns native field invocation and delayed clipboard edits.
`IUiClipboard` providers return on the UI thread; fields must still check their
lifetime before applying delayed edits. `DaemonClipboard` owns transport and
native fallback; [focus](ui-focus.md) owns focus memory.

Resizable text areas retain a form-owned `UiAreaResize`. Measurement and drawing
must include the same padding and scrollbar gutter. The native grip takes Unity
mouse capture: the adapter restores its capture and invokes the grip after the
text control on every enabled IMGUI pass to keep control allocation stable.
Retire capture with the field lifetime.

Selector width must fit its text, arrow, and divider within the available form
width. The opened menu uses that final width. `UiMenu` owns menu chains;
`Patch_UiMenuWindowStack` closes them before a clicked non-menu window is raised.
Menu levels need separate instances because vanilla same-type replacement can
remove a standing menu. Submenu openers need a non-null action because vanilla
treats null actions as disabled.

`TooltipLayout` shares frozen line breaks and a no-wrap style between sizing and
drawing: native wrapping can change with the GUI transform even at integer scales.
Invalidate on content, font, scale, or atlas changes, not cursor movement. Its
weak-key cache follows `ActiveTip` lifetime.

`Window.Margin` translates the GUI group rather than adding padding; shared hosts
use explicit body padding. `GameFont.Tiny` may draw as Small, so use shared metrics.
Use an explicit one-column listing instead of relying on a short vanilla listing
rectangle. Only runtime assets that must survive map changes need
`DontUnloadUnusedAsset`.

See [workspace layout](ui-dynamic-layout-architecture.md) for host geometry,
[Settings](ui-settings.md) for responsive previews, [dialogs](mod-ui-windows.md)
for window lifetimes, [theme identity](mod-ui-identity.md) for catalogs,
[text](mod-ui-text.md) for text rendering, and [scrolling](mod-ui-scrolling.md)
for scroll geometry and input.
