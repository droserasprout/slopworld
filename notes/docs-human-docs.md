# Human documentation

`docs/` is the mdBook for users.
Build it with `make docs`.
Serve it with `make docs-serve`.
Git ignores the output in `docs/book/`.

Write book prose when asked.
Before publication, check that behavior descriptions agree with the implementation.

Put each fact on one main page.
Tours introduce workflows. The FAQ answers recurring questions.
Link both to detailed guides and reference pages.
Do not repeat option tables, configuration steps, or troubleshooting instructions in tours or
the FAQ.
Give each FAQ question an explicit anchor. This keeps its link when the wording changes.

Put operating instructions in the book.
Put implementation constraints in developer notes.
When book instructions conflict with developer notes, follow the book.
Check book corrections against the implementation.
The README keeps a short Linux quickstart and links to platform-specific guides.

Generate the API route inventory with `make api-docs`.
Describe contracts that the inventory cannot express in the API reference and
[wire protocol](protocol-wire.md).
Add new or moved book pages to `docs/src/SUMMARY.md`.
