# Shared UI chrome

`UI/Display/CrtOverlay` owns the window-wide scanline and vignette overlay. Its
separate GUI behaviour draws above Root's windows and tooltips in physical pixels,
independently of terminal caches. Scanline thickness and spacing follow the game's
UI scale, with fractional coverage at line edges; the strip rebuilds when scale
or screen height changes. It creates no input controls. The
profile's `visualEffects` preference appears as Post-processing under Rendering, after
VSync, and offers Meh, Mild (default), Moderate, and Massive.
`VisualEffectsMode` owns scanline, vignette, grain, and cursor strengths for each preset; the overlay
owns coverage, falloff, and dithering. Changes rebuild textures immediately,
and Meh disables the overlay and releases them.
The hardware cursor bypasses the overlay. Its texture cache bakes a uniform RGB dim
and alpha reduction from `VisualEffectsMode` and rebuilds when the preset changes. Hotspots
are calculated before alpha changes so effect strength cannot move the click point.
Vignette dithering is baked at screen resolution. During resize it is hidden, then
rebuilt after dimensions remain stable for 200 ms to avoid repeated full-screen
CPU bakes and uploads. Enabling the overlay builds it immediately. Filtering
or scaling its noise would reintroduce bands or turn the noise into visible patches.

`UI/Chrome/` owns shared controls and layout helpers. `UI/Theme/` owns styling and
icon catalogs; `UI/Workspace/` owns workspace host chrome. Start with `UiText`,
`UiButtons`, `UiControls`, `UiLayout`, and `Slab` when adding a control. Feature
owners supply status, availability, empty messages, and actions.

Grained widget faces use `Slab.GrainedFill` or `Slab.Box` with `grain: true`.
`SurfaceGrain` owns their shared, static noise tile, drawn at physical-pixel scale
before borders, labels, and icons. Primary and danger buttons, checked indicators,
slider fills, settings tabs, selected rows, and interactive hover faces opt in
explicitly; color equality does not select textured surfaces. Slider knobs and
scrollbar thumbs opt in while hovered or dragged. Appearance previews reuse the
button and row renderers. Grain opacity follows face opacity, including disabled faces.
The post-processing selector also controls grain: Meh disables it, Mild keeps the
baseline, and Moderate/Massive increase its strength. Changes apply on the next
repaint without rebuilding the shared tile.

Measurement, drawing, and hit testing must share stable geometry and control IDs
across IMGUI passes. Measurement must not invoke controls, setters, or mutate form
data. Cache layout by content and text metrics; hover must not change bounds.

The workspace top bar keeps agents' names and runtime summaries, beside the Agents
sidebar header icon tinted by agent state. Readers use source
filenames or explicit diff targets. Viewers, editors, and native previews show
checkout · relative path as secondary context; main uses the sidebar's ginger-root
silhouette, sized consistently with checkout headings by `UiTheme.RootIconW`.
The top bar omits this mark and its separator when main is the project’s only
checkout enabled by the workspace filter; the relative path and tooltip remain. Reader icons share size, tint, and presentation with Open Files through
`ReaderRowChrome`. Project, full path, and daemon session identity stay in the tooltip.
Top-bar right-clicks reuse the corresponding sidebar reader or session menu,
including when the sidebar item is hidden or filtered. `PanelStatus` owns this presentation policy;
native previews supply their retained tab metadata through the file-reader owner.
Native preview identities are local handles, so their tooltips omit daemon session identity.

Tab rails and navigation rows activate on left mouse press through `NavigationPress`,
which consumes the event before content changes. Ordinary action buttons, menu
choices, and copy actions retain release activation.
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

Single-value choices use `UiSelector`: labelled controls in forms and `DrawInline`
for caller-owned table or toolbar rectangles. Both share the arrow, anchored menu,
and dismissal guard. Action menus keep ordinary buttons or contextual invocation.
Multi-select filters keep icon buttons and checkmarked options; their menus use
screen-space anchors and the same dismissal guard, retaining natural menu width.
Selector width must fit its text, arrow, and divider within the available form
width. The opened menu uses that final width. `UiMenu` owns menu chains;
`Patch_UiMenuWindowStack` closes them before a clicked non-menu window is raised.
Selectors use `NavigationPress` to consume the left mouse press before adding the menu.
Inline selectors build menu options only when opened. Forms with stable choice labels
can retain their label catalog for measurement and supply a separate menu factory;
measurement still uses current text metrics and the available form width.
Selector dismissal retains its source anchor so the same press cannot reopen it. Selectors observe physical mouse presses to retire an unclaimed
dismissal; repeated GUI passes in the same frame must preserve it. Other selectors
remain usable.
Menu levels need separate instances because vanilla same-type replacement can
remove a standing menu. Submenu openers need a non-null action because vanilla
treats null actions as disabled.
Menu highlights span the full row beneath the scrollbar. Row input and content
reserve only its mouse target; that target also supplies the trailing padding.

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
