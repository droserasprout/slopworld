# Library and ephemeral errands

Library presents agent templates, prompts, shell errands, breadcrumbs, and file actions.
Start in `manager/library.rs`, `manager/errands.rs`, and `config.rs`; public usage belongs
in the book. Runnable entries choose explicit host execution or an agent template. Template
errands copy settings and snapshots once and use the selected project's mounts; missing
execution choices fail before allocating a session. Agent-shell requests can still clone a
source agent via `like`. File actions execute on the daemon host. They do not participate in
agent breadcrumb delivery.

`ephemeral` is not a persistence test: durable host terminals use that presentation flag
but retain config records and Down rows. Temporary errands disappear on stop/exit and own
cleanup of their private state. See [host terminals](daemon-host-terminals.md).

Errand creation returns before prompt delivery because agent startup can exceed the mod's
HTTP timeout. Ordered paste, gap and Enter must remain one queue sequence; later user input
cannot overtake it. Readiness timeout behavior differs from [auto-resume](agent-auto-resume.md).
Command-only errands should not wait for readiness to deliver empty text.

Builtin library records are a separate layer. A same-named user record shadows a builtin;
deleting the user record reveals the builtin again.

Personal library items are stored in the daemon-owned `config.toml`; agent templates are stored
in `agent-templates.toml` beside it. Presets use the daemon's user-level preset directory. None
of these catalogs inspect project checkouts, and instantiated templates still copy dependency
snapshots.
