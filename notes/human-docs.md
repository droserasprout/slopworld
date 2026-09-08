# Human documentation

`docs/` is the user-facing mdBook. Build with `make docs`, serve with
`make docs-serve`; `docs/book/` is ignored output. Write book prose when asked and
verify behavior against the implementation before publishing it.

Each fact has one canonical topical page. Tours introduce workflows; the FAQ
answers recurring questions. Both link to detailed guides and references instead
of repeating option tables, configuration, or troubleshooting. Give FAQ questions
explicit anchors so wording changes preserve links.

Keep operational instructions in the book and implementation constraints in
devnotes. The book takes precedence when they disagree; verify corrections against
the implementation. The README keeps a short Linux quickstart and links to
platform-specific guides.

Keep the API route inventory generated with `make api-docs`; describe contracts
that the inventory cannot express in the API reference and
[wire-protocol](wire-protocol.md). New or moved book pages belong in
`docs/src/SUMMARY.md`; devnotes belong in [index](index.md).
