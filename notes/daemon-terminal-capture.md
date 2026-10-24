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
executes the selected action. OSC 52 publication validates the reader identity and hands off to subscribed clients
without desktop I/O; [clipboard](daemon-clipboard.md) owns the client boundary.
Watched viewport clears settle for one display frame of quiet after the latest output.
Managed pagers have no absolute redraw deadline, so slow split redraws retain the
previous page while replacement rows arrive. Other terminals cap settling at 32 ms
from the first clear, including repeated clears, so continuous output cannot freeze
publication. Ordinary output keeps its normal draw cadence.
Managed file, diff, and search pagers can finish settling earlier when their final
terminal operation clears the tail of the bottom prompt row. Completed pages publish
on the normal display beat before the next queued clear, keeping repeated scrolling
responsive without publishing the erased page. Other terminal output retains the
quiet-interval fallback; later operations invalidate a prompt hint.
Before a due draw, the reader consumes a bounded batch of queued control lines,
including lines buffered in the current transport chunk, and rechecks the deadline.

The mirror bounds each cell's combining tail. Discarded scalars still advance cluster
parsing so standalone text after an oversized cluster keeps its columns.

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

Literal keys and clipboard paste preserve control bytes. tmux 3.8 requires
`paste-buffer -S` for this; detect support from the connected server's command
list so tmux 3.7 retains its raw-paste behavior.

Named Shift+Enter uses tmux's negotiated key mode when present. In legacy mode,
foreground shells receive Enter while other applications retain CSI-u for multiline
input. Sandboxed agents cannot probe the host tmux socket, so negotiation alone is
not sufficient for compatibility. Literal text and clipboard bytes bypass this policy.

Native packages require tmux 3.7 or newer: 3.6 introduced `codepoint-widths`,
but our skin-tone override uses range syntax introduced in 3.7. Pane recovery
also reads `bracket_paste_flag`, introduced in 3.7. Debian 13's stock 3.5a does
not meet this contract, even though release binaries compile on Debian 13.

Accepted live resizes publish immediately without waiting for output. Compare frame
dimensions and reject captures from an older size. A stopped mirror resize must not
activate the session.
