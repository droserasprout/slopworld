# Daemon library

`config/library.rs` owns definitions/validation; `config/catalog.rs` prepares catalog
transactions. `application/library.rs` owns discovery, mutations, snapshots, and
publication. Session adapters supply guarded commits; API handlers own HTTP.
[Agent templates](daemon-agent-templates.md) have their own catalog.

`application/launch.rs` resolves saved and explicit launches into `session/launch.rs`
requests. `application/launch_sessions.rs` prepares configuration and host records;
`session/errands.rs` reserves identities and publishes live state under the operation
guard. `application/errands.rs` owns copied settings and executable/label policy.
Validation precedes allocation. File-action scope belongs to
`application/file_actions.rs`; planning must not allocate sessions.

Editing supplied entries creates user overrides; deleting one reveals the supplied entry.
All Prompts supply reusable text; errands additionally require a host or template.
Text-only Prompts need no project. File actions execute on the daemon host through
`files/actions/`.
`config/library.rs` owns the directory classification shared by catalog discovery and
storage target admission, including Timers. Catalog discovery also reads legacy
`breadcrumbs/` entries as Prompts and watches their revisions. The next Library
transaction writes canonical prompt files and retires legacy files in the same journal;
duplicate names fail validation without overwriting either entry.
`prompt/` owns Library lookup and contextual composition for manual, startup, and
scheduled input: inserted values are literal and never scanned again. Session adapters
resolve the target project/worktree before composition; automated input has no client tips.
Manual insertion does not submit; launch callers compose errand/worker prompts before delivery.

`session/delivery.rs` owns readiness and ordered prompt input. Creation may return
before delivery; timeout withholds input. Temporary errands disappear on stop/exit
and clean private state. Reader handles are opaque and reserved against configured
and live names under the session boundary. Labels and intent are separate metadata.
Delivery rechecks readiness at admission. Codex startup banners can enable bracketed
paste before accepting input, so delivery also requires a cursor on the composer row.
[Saved host tabs](daemon-host-terminals.md) have a different persistence contract.

[Configuration stores](daemon-config-stores.md) owns revision checks and recovery.
Ordinary library operations exclude root settings and workspace records. Project
renames update library references in the shared journaled transaction, retaining
library revision checks. User procedures belong to
[Library items and errands](../docs/src/workspace/library.md).

`config/timer_schedule.rs` owns UTC cron validation. `application/timers.rs` owns durable
occurrence claims and diagnostics; maintenance drives it without a game client. Timers
reference live Library Prompts and stable session identities, with an explicitly selected
durable agent target. Claims precede input, missed ticks do not accumulate, and uncertain
submissions are not replayed. Session admission freezes catalog and lifecycle identity
through claims; `session/delivery.rs` rechecks idle/readiness and uses guarded agent input.
Timer status is a read-only projection, not persisted Library configuration.
Invalid schedules from disk edits are logged and skipped without blocking other Timers.
