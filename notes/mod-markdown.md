# Markdown previews

Markdown rows in Files and Git open a native `IContentView`, rather than an agent pager.
The daemon owns the read through root-only `GET /api/read`; the response is valid UTF-8 and
bounded at 512 KiB. Markdig 0.18.3 is the `net40` assembly shipped beside the mod, chosen
because it has no extra runtime assembly dependencies under RimWorld's Mono loader.

The renderer also accepts inline text for the Settings > Integrations > Instructions preview;
that path skips daemon file reads while reusing the same AST, layout, selection, and scroll
behavior. The renderer consumes the AST directly, keeps unsupported HTML as literal faint text, and
handles a small native HTML subset: local `<img>` tags, `<br>`, semantic emphasis/code/strike
tags, `span` wrappers and links. Images honor left/right/center (including `middle`) and
`style="float: ..."`. This is deliberately a tag scanner, not an HTML parser;
CSS, scripts, forms, remote images and unknown tags are not interpreted. It supports headings,
emphasis, links, lists, quotes, fenced code, tables and task markers (drawn as native
read-only checkboxes), and uses the existing
scheme, font and `SmoothScroll`. Fenced code is sent to the daemon's configured host syntax
highlighter and its ANSI colors are rendered natively; unavailable highlighting stays plain.
External links use terminal-style Ctrl+click and call
Unity's `Application.OpenURL`; relative links stay inside the owning project and open another native
preview or the existing pager. Rendered text supports drag, double-click word, triple-click
line, Ctrl+C, Ctrl+A and a right-click menu. Ctrl+V pastes to the agent pane behind the preview
when one exists; the preview itself remains read-only. Markdown's context menu retains an
explicit `View in pager` source-pager action. Local images resolve beside the Markdown file and
are bounded through `/api/image`; remote, data-URI and otherwise unsupported images remain
aligned unavailable stubs rather than disappearing from the document.

Layout is cached at the settled content width. A full-width scrollbar probe runs only when the
document is invalidated or the viewport changes; ordinary repaints and wheel movement reuse the
existing placements and selection geometry.
