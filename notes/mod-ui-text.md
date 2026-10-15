# Shared UI text

`UI/Text/` owns sprite-aware label measurement, truncation, and rendering. These
operations preserve catalog keys and plain text elements; drawing clips positioned
spans to the label box. Missing artwork must not change layout, and atlas drawing
preserves caller opacity without inheriting text tint.
Catalog matching leaves text-default symbols on the font path unless an emoji
selector or a supported longer sequence requests artwork. VS15 requests font
presentation. The pinned scalar-plus-VS16 keys identify text-default symbols;
bare artwork slots remain in place to preserve atlas indexing.
Terminal rendering prefers the terminal font for supported BMP text-default symbols.
`TextSymbolRenderer` supplies missing BMP glyphs and supplementary symbols that
legacy Unity cannot reliably query as whole scalars. Its atlas covers 207 symbols.
`just generate-symbol-atlas` selects glyphs from pinned Noto Sans Symbols 2, then
Noto Sans Symbols, then monochrome Noto Emoji for remaining coverage. It queries
the vendored files directly, independently of host font configuration. The first
two cover 191 symbols; Noto Emoji supplies the remaining 16. `assets/fonts/noto-symbols/`
and `assets/fonts/noto-emoji-mono/` own source checksums; the corresponding
`licenses/` directories own attribution. Generated source indices retain glyph provenance. Emoji
presentation continues to use the color atlas. `TextAtlas` owns shared texture loading
and draw-state restoration; renderers own presentation, geometry, and tint.
Monochrome atlas metadata preserves original raster size and baseline-relative
glyph bounds separately from packed UV bounds; terminal font metrics determine
display size. Packing must not normalize the displayed size of each symbol.

`NativeFonts` owns installed family discovery and GUI backend registration. `InstalledFontNames`
reads OpenType family, style, and fixed-pitch metadata. Family selection prefers an upright,
normal-width, regular-weight face, including within collections. Font pickers do not register
faces to measure monospace eligibility. Registration names include import size because the backend deduplicates
by name and byte length. Collection entries retain their face index; registration
extracts that face to standalone SFNT bytes because native metrics reject TTC headers.
The installed collection stays unchanged. Font wrappers may be released; registered faces
remain backend-owned.
OS font creation and family enumeration in Unity's compatibility API are stubs.

Shared text rendering applies the style foreground through GUI tint because native
`GUI.Label` forwards tint without reading the style state's text color. Each label
restores the caller tint, including on failure; atlas artwork keeps its own colors.

`NativeTextScope` carries literal/rich text policy across the backend API, which
omits `GUIStyle.richText`. The chrome Harmony hooks scope style measurement and
drawing together and suppress native tag recognition only for literal text.
Field drawing, hit testing, and cursor geometry always use literal text; tooltip
cursor geometry follows its label style. Scopes restore thread-local policy on
exception and nested calls without rewriting strings or selection offsets.

Plain labels use the native text path. `PlainStatusLabel` and its height helper
share native wrapping; catalog keys remain literal text in these APIs.
`UiText.Paragraph` owns plain paragraph drawing and spacing; `SizedStyle` owns
explicit-size native font styles, falling back to the native size when unspecified.
Editable field ownership belongs to [focus](ui-focus.md), and control composition
to [shared chrome](mod-ui-chrome.md).

`tools/assets/emoji_atlas.py` writes atlas artwork and `TextSpriteData.cs` together
through `just generate-emoji-atlas`. Rendering and artwork fitting belong to the
shared owners described in [UI icons](mod-icons.md).
Sequence input is pinned to `assets/unicode/emoji-test.txt` (Unicode 17); regenerate
keys and artwork together without changing slot order. Normal builds use committed
assets. `just tools-test tools/assets/test_text_sprites.py` checks generated metadata and loading-tip literals.

`assets/fonts/noto-emoji/` owns the bundled font and installed-source checksum;
`licenses/noto-emoji/` owns its license. `tools/assets/emoji_font.py` selects it through a process-local Fontconfig
configuration shared by emoji, atlas, and application-icon bakers. Character
enumeration and rendering use the same file; no installed emoji font is required.

`DocumentSelection` owns gestures, mouse capture, hit testing, and highlights for
Markdown and task readers. Markdown supplies styled logical lines and table copy
ordering; tasks supply Unicode boundaries and source offsets that survive reflow.
Each adapter retains its clipboard policy: Markdown publishes PRIMARY on mouse
selection and copies on Select all; tasks copy only on an explicit Copy command.
Terminal cell selection remains a separate owner.
