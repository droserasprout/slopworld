# Daemon library

`config/library.rs` owns definitions/validation; `config/catalog.rs` prepares catalog
transactions. `application/library.rs` owns discovery, mutations, snapshots, and
publication. Session adapters supply guarded commits; API handlers own HTTP.
[Agent templates](daemon-agent-templates.md) have their own catalog.

## Launch and delivery

`application/launch.rs` resolves saved and explicit launches into `session/launch.rs`
requests. `application/launch_sessions.rs` prepares configuration and host records;
`session/errands.rs` reserves identities and publishes live state under the operation
guard. `application/errands.rs` owns copied settings and executable/label policy.
Validation precedes allocation. File-action scope belongs to
`application/file_actions.rs`; planning must not allocate sessions.

Editing supplied entries creates user overrides; deleting one reveals the supplied entry.
Prompts supply reusable text; errands also require a host or template.
Text-only Prompts need no project. File actions execute on the daemon host through
`files/actions/`.
`config/library.rs` owns the directory classification shared by catalog discovery and
storage target admission, including Timers. Catalog discovery also reads legacy
`breadcrumbs/` entries as Prompts and watches their revisions. The next Library
transaction writes canonical prompt files and retires legacy files in the same journal;
duplicate names fail validation without overwriting either entry.
`prompt/` owns Library lookup and contextual composition for manual, startup, and
scheduled input: inserted values are literal and never scanned again. Session adapters
resolve the target project/worktree before composition. Without client-supplied tips,
`random_tip` draws without replacement from the unmarked entries in the shared
`loading-tips.txt` catalog, including timer delivery and worker task composition.
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

## Timers

`config/timer_trigger.rs` owns trigger validation, named-zone calendar calculation,
and duration parsing.
`application/timers.rs` coordinates claims and delivery. Its `planning` module
computes occurrence eligibility, claim identity and watermark, deadline advancement,
and action snapshots without side effects. `runtime` owns activation anchors, `records` owns
durable claims, and `status` owns read-only views.
Maintenance checks timers without a game client. Configuration
commit/reload reconciles activation while retaining the session operation guard, so
rapid disable/enable edits cannot disappear between checks. The lock order is session
operation, then timer runtime gate; status projection takes only the timer gate.

Timers dispatch Prompt, Worker, and Shell actions through `timers/actions.rs`.
Prompt actions reference stable durable-agent identities; Worker actions use the
worker owner to create host-owned one-shot tasks from templates and live Library
Prompts. Shell actions use the launch owner and the Library item's saved location
and execution settings. Timer worker project references participate in project
rename/removal transactions. Admission freezes catalog and lifecycle identity
through claim and execution. `session/delivery.rs` rechecks readiness and uses
guarded input. Claims precede execution, missed ticks do not accumulate, and
uncertain actions are not replayed. Manual activation shares the claim path without
consuming the scheduled deadline. Relative timers re-arm on daemon restart; calendar
claims retain their wall-time watermark. The runtime gate is released before action
execution because worker/launch commits can reconcile timer activation; the session
operation remains held. Runtime status stays out of Library configuration. Invalid
disk schedules are skipped without blocking other Timers.

The mod resolves its existing time-zone preference to an explicit zone when creating a
calendar timer; daemon scheduling never depends on client display preferences. Named
zones use Jiff and the daemon's zone database, with a bundled fallback for minimal
sidecar images. Calendar DST gaps skip; folds use the
earlier instant once. Preview is daemon-owned and has no activation or persistence effects.
