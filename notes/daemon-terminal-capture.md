# Terminal capture

`session/capture/` owns readers, frames, history, input, and exit handling. `emu/`
mirrors geometry; `tmux/` owns transport. Work belongs to a specific run; stop/reset
or replacement prevents stale publication. Cleanup retains cancellation and child
reaping through teardown: dropping a task handle only detaches it.

Tmux answers terminal queries; the mirror must not send duplicate replies to shell
input. Screens may coalesce, but bytes and history replies stay complete. History
replies retain sequence/extent for delayed translation. Clear and screen-mode changes
invalidate history; scroll snapshots cannot consume bells. Titles and newly latched
bells publish even without changed text. Scheduling consumes dirty state; rendering
executes the selected action. Clipboard handoff has an independent lifetime.

Reader disconnect is not process exit. Failed status queries cannot declare exit;
missing panes require a checked session listing. Confirmed exit records evidence
before pane removal, falling back to the last frame if needed. Failed evidence writes
retain the dead pane for inspection. Inspection/attachment failures schedule bounded
monotonic maintenance retries, independently of configuration reconciliation. Recheck
pending identity under the session boundary; reset or success retires retries.

Tmux metadata restores temporary-reader intent and source identity. Native previews
have no process to adopt. Startup seeding/repaint belongs to [redeploy](daemon-redeploy.md);
role transitions to [lifecycle](daemon-session-lifecycle.md), activity to
[state](daemon-session-state.md), and client history to [terminal](mod-terminal.md).

The private tmux server and emulator must agree on emoji cell widths. New panes set
`LESSUTFCHARDEF` to retain modifiers/joiners/selectors; overrides and existing panes
keep their launch settings. CI and sidecar use the same pinned Debian forky tmux;
older packages can lack required width ranges. Preserve application-negotiated
bracketed paste rather than guessing from command names.

Accepted live resizes publish immediately without waiting for output. Compare frame
dimensions and reject captures from an older size. A stopped mirror resize must not
activate the session.
