# Daemon terminal capture

`session/manager/capture/` owns reader attachment, VT rendering, frames, scrollback,
input, and process-exit handling. `emu/` mirrors terminal geometry; `tmux/` owns the
transport. Readers and frames belong to a specific session run: stop/reset or
replacement prevents old work from republishing it.

Tmux answers terminal queries; the local mirror must not emit duplicate replies into
shell input. Live screens can coalesce, but terminal bytes and history replies remain
complete. Scroll replies retain capture sequence and history extent so clients can
translate delayed snapshots. History clear or screen-mode changes invalidate old
history, and scroll snapshots must not consume bells.

Title changes or newly latched bells publish a session snapshot even when screen
text is unchanged. Scheduling owns dirty-state consumption; rendering executes its
selected action. Clipboard write handoff is independent of reader lifetime.

A reader disconnect is not process exit. Confirmed live panes recover readers;
failed status queries do not declare exit. Missing pane status must be confirmed
against a checked session listing; a running tmux server with no sessions confirms
absence. Confirmed exit records evidence before removing the pane. Screen capture
can fall back to the last in-memory frame, and failed evidence writes preserve the
dead pane for inspection.

Startup seeding/repaint belongs to [redeploy](daemon-redeploy.md), process transitions
to [lifecycle](daemon-session-lifecycle.md), activity to [state](daemon-session-state.md),
and client input/history to [terminal](mod-terminal.md).

Temporary terminal reader intent, source identity, label, scope, and pin state recover
from tmux metadata into client views. Native previews have no tmux process to adopt.

The private tmux server and emulator agree on composing emoji modifiers and joined
cell widths. New panes default `LESSUTFCHARDEF` to retain modifiers/joiners/selectors;
custom environment/presets can override it, and existing panes keep launch settings
until restarted. These daemon rules determine client columns.

Reader cleanup owns cancellation and child reaping before tmux/session removal.
Dropping a task handle detaches work rather than aborting it; keep explicit ownership
of cancellation through teardown. Paste transport preserves the application's
bracketed-paste mode rather than inferring it from command names.
