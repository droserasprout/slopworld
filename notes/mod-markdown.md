# Native Markdown

`UI/MarkdownPreview/` separates parsing, resources, layout, drawing and selection.
Files and inline Settings previews share the renderer; inline text skips daemon file reads.
Markdig's shipped version avoids extra runtime dependencies under RimWorld Mono.

The daemon bounds file/image/highlighter access. HTML support is a small scanner, not a
browser: unsupported markup remains text, and remote/data images do not become fetches.
Local resource paths must stay inside the owning project. Resource callbacks need document
identity checks so a replaced preview cannot receive stale images or highlighting.

Reflow depends on settled width. Probe scrollbar need on invalidation, not every repaint;
selection geometry and offscreen placements reuse that layout. Read-only previews still own
selection/copy, while paste may target the retained terminal behind them. Preserve that input
boundary when adding controls.

Selection copies blocks and complete table cells in document order; hit testing keeps a
separate spatial order. Soft wraps retain clipped whitespace without adding newlines.
Image state belongs to the document generation and is cleared on replacement or close.
