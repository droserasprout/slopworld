# Session lifecycle

`session/lifecycle/` owns start, stop, adoption, and reconciliation.
`application/session_edits.rs` coordinates configured edits/private-state recovery;
`session/sessions.rs` supplies live facts. Validate complete candidates before tmux
identity changes, and retain guards through commit, rollback, publication, and cleanup
after caller cancellation. Creation and archived-agent restoration reserve names across
saved agents, saved host shells, live temporary identities, and surviving tmux panes
before changing records or moving archived state. Shared candidate validation also
rejects duplicate persisted agent/host names.

Agent deletion rejects running panes; retained dead panes do not block it. Host-tab
removal stops the shell first. Rename preserves durable identity. Failed persistence
restores the tmux name; failed rollback reports both errors and needs manual recovery.

Application resolves launch policy. `session/errands.rs` reserves identities without
publishing; the guard spans reservation through live publication. Dropping a reservation
leaves no live/temporary row. Reader context stays in `session/launch.rs`, separate from
saved definitions. `session/role.rs` owns retention: durable agents/saved shells survive
stop; temporary agents clean private state. Snapshot role/inputs before I/O.

Adoption uses tmux reader metadata rather than saved agent records. Worker metadata
wins over stale host identity. Adoption resolves a typed identity once for creation,
repair, and conflict checks. Worker option-query failures retain the pane for retry;
they cannot be interpreted as absent metadata or authorize another recovery role.
Failed worker identity queries abort checked reconciliation before startup task-loss
recovery. Successfully read but incomplete worker identity is rejected by worker
recovery. Ordinary malformed non-worker metadata remains available for inspection.
Temporary non-workers publish identity/project/worktree metadata before launch. Invalid metadata leaves panes untouched; [workers](daemon-workers.md)
owns worker rejection. Reconciliation must not reinterpret a host or different durable
identity as a same-name configured agent. Retire incompatible processes first; failure
keeps identities separate. Host refresh preserves live identity and input queues.

Identity/grants, terminal input, and persistence have separate boundaries so unrelated
lifecycle work need not block root input. Scoped work retains authorization; delayed
input checks run identity. See [stores](daemon-config-stores.md),
[capture](daemon-terminal-capture.md), and [redeploy](daemon-redeploy.md).

`session/lifecycle/resume.rs` gates automatic `/resume` on nonempty private transcript
files for supported commands (Codex, Claude, Pi). Missing, empty, or deleted history and
unknown commands start fresh. Discovery is bounded and ignores symlinks. Delivery waits
for queued resume input to drain, then checks readiness with a deadline; timeout dismisses
the picker for the same run, releases startup input, and records a warning. A queue fence
alone is not evidence that a conversation resumed. Manual Resume uses the same startup
sequence under lifecycle admission, requires a saved non-worker agent and supported
history, and overrides auto-resume only in the launch plan. Missing history rejects
the request before launch; the saved auto-resume preference remains unchanged.

`session/delivery.rs` keeps captured destination identity and readiness separate from
submission intent. Startup prompts capture titles; follow-ups and scheduled prompts
use guarded agent paste/Enter, with scheduled prompts reserving an idle turn.
Auto-resume is explicit startup control and does not enter title capture. Readiness
waits run outside session admission; admission rechecks the run and readiness before
queueing the complete sequence under the operation guard.
