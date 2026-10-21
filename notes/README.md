# Developer notes

Notes explain ownership, boundaries between components, and constraints that are hard
to discover in code. Start with the maps in [AGENTS.md](../AGENTS.md), then follow
focused links to source and tests. The [book](../docs/src/SUMMARY.md) owns user
instructions; [human documentation](docs-human-docs.md) explains that boundary.

Keep one subject per note and start filenames with the subject area. Prefer short
paragraphs. Link to source or the book instead of copying file inventories, defaults,
controls, or procedures. Keep local implementation explanations beside the code.
Describe current behavior; Git owns history. Delete stale or duplicate notes rather
than archiving them.

Keep unfinished plans in `priv/notes/` with a `plan-` filename. Each needs the problem,
completion requirements, and one `Status:` line near the title: `proposed`, `approved`,
`wip`, `implemented`, `reviewed`, or `human approved`. Retain plans through review and
merge, then move lasting guidance to focused notes and delete the plan in a worktree.
Dated measurements describe past results, not current behavior.
