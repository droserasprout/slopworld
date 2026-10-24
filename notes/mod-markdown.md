# Native Markdown

`UI/MarkdownPreview/` owns parsing, resources, layout, drawing, and selection for
Files and inline previews. Inline bodies need no file read, though highlighting may
still use the daemon. [Readers](mod-file-readers.md) owns replacement lifetime.

Local resources retain the captured project scope; the daemon enforces containment
and bounded reads/highlighter access. Remote/data images are not fetched. Replies
check document generation and viewport before applying. `BoundedImageDecoder` shares
Unity decoding with image readers: it cleans failed decodes; callers own successful
textures, including cleanup when memory-budget admission fails.

Markdig owns Markdown structure; AngleSharp repairs HTML blocks and tokenizes inline
HTML. Both supply decoded text/attributes to one display policy; do not decode twice.
Parsing has no resource loader or script integration. HTML support is display-only:
unrecognized markup stays faint text, comments disappear, and arbitrary classes do
not apply stylesheets. The book's `screenshot-stack` class uses separate image rows.

Preserve literal code whitespace through parsing and copy. Ordinary source newlines
become spaces; explicit breaks retain line boundaries. Floats belong to block flow;
quotes/lists contain their own, while slabs clear outer floats. Record line insets so
wrapping, drawing, selection, copy, and link hit testing share reflowed geometry.

Text pieces use the shared emoji atlas for catalog sequences. Sprite advances follow
line height; measurement, wrapping, drawing, and selection share that geometry.
Match catalog keys before Unicode text elements so joined emoji stay intact on Mono.
Clipboard text retains the original Unicode sequences.
