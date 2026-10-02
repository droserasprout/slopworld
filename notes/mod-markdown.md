# Native Markdown

`UI/MarkdownPreview/` separates parsing, resources, layout, drawing and selection.
Files and inline Settings previews share the renderer.
Local JPG/PNG links open the Files native image reader.
Inline text skips daemon file reads.
The supplied Markdig version avoids extra runtime dependencies under RimWorld Mono.

The daemon bounds file, image, and highlighter access.
A small scanner handles HTML without browser behavior.
Unsupported markup remains text. The scanner does not fetch remote or data images.
Local resource paths must stay inside the owning project. Resource callbacks need document
identity checks so a replaced preview cannot receive stale images or highlighting.
The client checks paths lexically; scoped read/image requests carry the captured root to the daemon.
The daemon resolves symlinks and opens resolved components without following replacement links.
Linked text uses the scoped native reader, including non-Markdown text, so no pager reopens an unchecked path.
Raw HTML attributes decode once; Markdig AST text and URLs are already normalized.

Layout owns stable list gutters, explicit tight/loose list spacing, six-level heading metrics,
content-sized table columns and Markdig's column alignment. Inline-code padding and wrapped
code continuation marks are part of the text geometry, so selection and links must consume the
same offsets. Typography invalidation rebuilds Markdown styles and character-width caches before
reflow, including the independent terminal-font revision. Heading code keeps the terminal
face at the heading size, including game styles whose size is implicit in a baked font.
Mixed runs use font ascent.
Line bounds include shifted descenders and inline images. Wrapping preserves Unicode text elements
while logical copy offsets remain UTF-16 offsets. Consumed link clicks must skip selection dispatch.

Table drawing clips each cell when even one glyph cannot fit. Translate clipped link regions
back to document coordinates and constrain selection highlights without truncating copied text.
Tests without the game check production styles and block layout with variable font metrics.
Runtime appearance still needs checks in the game.

Reflow depends on settled width. Check whether a scrollbar is necessary on invalidation, not on every repaint.
Selection geometry and offscreen placements reuse that layout. Read-only previews still own
selection/copy, while paste may target the retained terminal behind them. Preserve that input
boundary when adding controls.

Selection copies blocks and complete table cells in document order.
Hit testing keeps a separate spatial order. Soft wraps retain clipped whitespace without adding newlines.
The preview ties image state to the document generation. It clears that state when it replaces
or closes the document. Decoded inline images have a per-image limit and share a document budget,
including reservations for pending decodes. Only viewport-adjacent images load; visible images take
priority. Eviction retains natural dimensions so scrolling cannot change document geometry.
Image and render visibility indexes rebuild with the layout generation, including after clear.
