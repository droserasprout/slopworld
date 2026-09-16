# Library and ephemeral errands

Library presents agent templates, prompts, shell errands, breadcrumbs, and file actions.
Start in `manager/library.rs`, `manager/errands.rs`, and `config.rs`; public usage belongs
in the book. File actions use the selected project's sandbox, except explicit host errands
for storage paths. They do not participate in agent breadcrumb delivery.

`ephemeral` is not a persistence test: durable host terminals use that presentation flag
but retain config records and Down rows. Temporary errands disappear on stop/exit and own
cleanup of their private state. See [host terminals](daemon-host-terminals.md).

Errand creation returns before prompt delivery because agent startup can exceed the mod's
HTTP timeout. Ordered paste, gap and Enter must remain one queue sequence; later user input
cannot overtake it. Readiness timeout behavior differs from [auto-resume](agent-auto-resume.md).
Command-only errands should not wait for readiness to deliver empty text.

Builtin library records are a separate layer. A same-named user record shadows a builtin;
deleting the user record reveals the builtin again.

`config/project_library.rs` discovers read-only definitions under each registered checkout's
`.slopworld/library/` and `.slopworld/templates/`. Qualified `project::name` identities keep
projects independent without implicit shadowing. Library lookups and template catalog reads
see file edits; discovery does not execute or attach anything. File ownership stays out of
machine config, and instantiated templates still copy dependency snapshots. Layout and examples:
[repository Library](../docs/src/guides/repository-library.md).
