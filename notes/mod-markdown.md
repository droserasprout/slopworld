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

Layout owns stable list gutters, explicit tight/loose list spacing, six-level heading metrics,
content-sized table columns and Markdig's column alignment. Inline-code padding and wrapped
code continuation marks are part of the text geometry, so selection and links must consume the
same offsets. Typography invalidation rebuilds Markdown styles and character-width caches before
reflow, including the independent terminal-font revision. Heading code keeps the terminal
face at the heading size, including game styles whose size is implicit in a baked font.
Mixed runs use font ascent.
Line bounds include shifted descenders and inline images.

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
or closes the document.
