# Shared UI text

`UI/Text/` owns sprite-aware label measurement, truncation, and rendering. These
operations preserve catalog keys and plain text elements; drawing clips positioned
spans to the label box. Missing artwork must not change layout, and atlas drawing
preserves caller opacity without inheriting text tint.

Plain labels use the native text path. `PlainStatusLabel` and its height helper
share native wrapping; catalog keys remain literal text in these APIs.
Editable field ownership belongs to [focus](ui-focus.md), and control composition
to [shared chrome](mod-ui-chrome.md).

`tools/emoji_atlas.py` writes atlas artwork and `TextSpriteData.cs` together.
Sequence input is pinned to `assets/unicode/emoji-test.txt` (Unicode 17); regenerate
keys and artwork together without changing slot order. Normal builds use committed
assets. `just test-text-sprites` checks generated metadata and loading-tip literals.
