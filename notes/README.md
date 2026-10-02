# Developer notes

Notes capture ownership and hard-to-find constraints. [Writing developer notes](docs-prose-guide.md)
owns content guidance; [human documentation](docs-human-docs.md) owns the book boundary.

Start filenames with the subject area and keep one main subject per note.
Keep plans in `priv/notes/` with a `plan-` filename for changes that have not completed
review and merge. Each plan requires one `Status:` line near the title: `proposed`,
`approved`, `wip`, `implemented`, `reviewed`, or `human approved`.
Record the problem and requirements for completion.

Keep plans through review and merge. Then move lasting ownership guidance to focused
notes and delete the plan in a worktree. Delete stale/duplicate notes, migration
instructions, and session history once they no longer explain current constraints;
do not archive them. Dated measurements describe past results, not current behavior.
