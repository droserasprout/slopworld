# Writing developer notes

Notes explain where to start, which component responsibilities to preserve, and
non-obvious constraints or failure reasons. Keep facts with their owner and link to
source, tests, or the main user-doc page.

Avoid duplicating inventories, defaults, control lists, or procedures readily found
in source or the book. Preserve cross-component requirements that would otherwise
be hard to discover, including sequences whose order matters. Prefer a few short
paragraphs. User-procedure ownership belongs to [human documentation](docs-human-docs.md).

Source comments stay beside the code they explain; see [house rules](core-house-rules.md).
Note cleanup and plan retention belong to the [note policy](README.md).

Prose is a liability: every sentence needs to earn its upkeep. Remove stale prose when
the behavior changes.
