# Shared UI chrome

`UI/Chrome/` owns controls and shared layout; `UI/Theme/` owns styling and icons;
`UI/Workspace/` owns host chrome. Features supply state and actions.

Measurement, drawing, and hit testing share geometry and control IDs across IMGUI
passes. Measurement must not invoke controls, setters, or menu construction. Cache
by content and text metrics; hover cannot change bounds. Theme colors resolve on
read, so textures that bake colors must invalidate when the theme changes.

Navigation activates on press; ordinary actions activate on release. `RowActions`
uses the same clipped, scroll-adjusted geometry for drawing and dispatch. Test actions
before the row fallback so one press cannot trigger both. Hover may truncate labels
but must not move targets. [Scrolling](mod-ui-scrolling.md) owns scrollbar hit insets.

`UiMenu` retains the dismissed source anchor across GUI passes so the same press
cannot reopen it. Close menus before raising another window. Vanilla replaces
same-type windows, so each menu level needs a separate instance; submenu openers need a
non-null action to remain enabled.

`TextEntryController` owns native fields and delayed clipboard edits. Replies arrive
on the UI thread but still need a live field owner. Resize grips run after the text
control on every enabled pass to preserve control IDs. Retire capture with the field.
See [focus](ui-focus.md) for field lifetime and restoration.

`Slab` supplies snapped bounds and scale. Grain is an explicit control choice, drawn
before borders/text with face opacity. Appearance previews reuse these renderers.
`CrtOverlay` draws in physical pixels above windows/tooltips without input or terminal
cache dependencies. A combined scanline/vignette texture uses one full-screen draw;
a scanline strip covers initial preparation. Keep the previous combined texture visible while
resize or UI-scale changes settle. Bake replacements in Update outside frame drawing and
release textures when disabled. Cursor dimming must preserve the
original hotspot.

`TooltipLayout` freezes wrapping because GUI transforms can change native line breaks.
Invalidate for content or metrics, not pointer motion. `Window.Margin` translates the
GUI group rather than adding padding. Native Tiny text can draw as Small; use shared
metrics. Only assets that must survive map changes need `DontUnloadUnusedAsset`.

[Workspace](ui-dynamic-layout-architecture.md), [dialogs](mod-ui-windows.md),
[text](mod-ui-text.md), and [theme identity](mod-ui-identity.md) own their focused rules.
