# Shared UI chrome

`UI/Chrome/` owns controls and shared layout; `UI/Theme/` owns styling and icons;
`UI/Fonts/` owns shared font discovery, identity, selection labels, and UI font
application; `UI/Workspace/` owns host chrome. Features supply state and actions.

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
Child placement checks both sides against all open ancestors, preferring a clear
side when available; shared border seams do not count as overlap.
Selector width measurement includes menu checkbox columns and submenu arrows, so
matching the popup to its source control does not truncate those rows.
Stacked selectors measure the longest option up to the available form width,
with extra space for the control arrow; the standalone menu width cap does not apply.
`UiSelector` owns both stacked form selectors and single-row label/dropdown fields;
single-row fields keep their dropdowns aligned on the right as the form resizes.
`UiMenuInput` supplies inline creation fields. Focused fields retain editing keys;
menu accept dispatch submits them on Enter. Features own validation and draft guards,
while the menu cancels field lifetime on closure.
Font menus use `FontFamilyIdentity` to group patched faces with their installed
original family, sharing alias identity with terminal fallback selection. Menu
grouping preserves each face's installed name for selection and persistence.
`InstalledFonts` owns OS discovery and the catalog revision shared by font consumers.
Early bootstrap prewarms `NativeFonts` discovery on a worker using private managed
catalog snapshots. Registration remains on the presentation thread; rescans retire
pending snapshots so obsolete discovery cannot publish.
Rescanning reapplies UI fonts; terminal fonts and both menus observe the revision.
`FontSelection` labels Auto with its first installed candidate, explicitly as a
preference rather than a claim about Unity's native glyph resolution.

`TextEntryController` owns native fields and delayed clipboard edits. Replies arrive
on the UI thread but still need a live field owner. Resize grips run after the text
control on every enabled pass to preserve control IDs. Retire capture with the field.
See [focus](ui-focus.md) for field lifetime and restoration.
`NativeTextLayout` adapts Waylith text layout for tooltip lines, caret reveal,
and selection hit testing. It shares native field padding and restores both
selection endpoints after hit tests, including on failure.

`Slab` supplies snapped bounds and scale. Grain is an explicit control choice, drawn
before borders/text with face opacity. Appearance previews reuse these renderers.
Post-processing defaults to Off; saved explicit levels remain active.
`CrtOverlay` draws in physical pixels above windows/tooltips without input or terminal
cache dependencies. `Patch_CrtOverlay` draws after Waylith flushes queued GUI windows;
`GUI.depth` only orders behaviours and cannot place their draws above that later pass. A combined scanline/vignette texture uses one full-screen draw;
a scanline strip covers initial preparation. Keep the previous combined texture visible while
resize or UI-scale changes settle. Compute replacement pixels on workers, then upload in Update outside frame drawing.
Resize, scale and mode changes invalidate pending results; disabling or destroying
the overlay cancels workers and releases textures. Cursor dimming must preserve the
original hotspot.

`TooltipLayout` freezes wrapping because GUI transforms can change native line breaks.
Invalidate for content or metrics, not pointer motion. `Window.Margin` translates the
GUI group rather than adding padding. Native Tiny text can draw as Small; use shared
metrics. Only assets that must survive map changes need `DontUnloadUnusedAsset`.

[Workspace](ui-dynamic-layout-architecture.md), [dialogs](mod-ui-windows.md),
[text](mod-ui-text.md), and [theme identity](mod-ui-identity.md) own their focused rules.
