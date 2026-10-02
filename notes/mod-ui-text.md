# Shared UI text

`UI/Text/` owns sprite-aware label measurement, truncation, and rendering. These
operations preserve catalog keys and plain text elements; drawing clips positioned
spans to the label box. Missing artwork must not change layout, and atlas drawing
preserves caller opacity without inheriting text tint.

Plain labels use the native text path. `PlainStatusLabel` and its height helper
share native wrapping; catalog keys remain literal text in these APIs.
Editable field ownership belongs to [focus](ui-focus.md), and control composition
to [shared chrome](mod-ui-chrome.md).
