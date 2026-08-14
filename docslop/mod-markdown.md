# Markdown previews

Markdown rows in Files and Git open a native `IContentView`, rather than an agent pager.
The daemon owns the read through root-only `GET /api/read`; the response is valid UTF-8 and
bounded at 512 KiB. Markdig 0.18.3 is the `net40` assembly shipped beside the mod, chosen
because it has no extra runtime assembly dependencies under RimWorld's Mono loader.

The renderer consumes the AST directly, keeps unsupported HTML as literal faint text, and
handles a small native HTML subset: local `<img>` tags, `<br>`, semantic emphasis/code/strike
tags, `span` wrappers and links. Images honor left/right/center (including `middle`) and
`style="float: ..."`. This is deliberately a tag scanner, not an HTML parser;
CSS, scripts, forms, remote images and unknown tags are not interpreted. It supports headings,
emphasis, links, lists, quotes, fenced code, tables and task markers, and uses the existing
scheme, font and `SmoothScroll`. External links use terminal-style Ctrl+click and go back
through `/api/open`; relative links stay inside the owning project and open another native
preview or the existing pager. Rendered text supports drag, double-click word, triple-click
line, Ctrl+C, Ctrl+A and a right-click menu. Ctrl+V pastes to the agent pane behind the preview
when one exists; the preview itself remains read-only. Markdown's context menu retains an
explicit `View in pager` source-pager action. Local images resolve beside the Markdown file and
are bounded through `/api/image`; remote, data-URI and otherwise unsupported images remain
aligned unavailable stubs rather than disappearing from the document.
