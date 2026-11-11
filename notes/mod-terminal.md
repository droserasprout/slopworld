# Terminal ownership and input

`WorkspaceWindow` hosts controls; `TerminalSplit` holds distinct panels.
`TerminalPanel` owns rendering, history, subscriptions, caches, and input.
Retained frames, scroll positions, and history caches are pruned against session
inventory revisions even while a pane is hidden. Stopped sessions remain eligible
until removed from that inventory.
[Workspace](ui-dynamic-layout-architecture.md) owns geometry;
[panels](mod-workspace-panels.md) owns lifetime.

`TerminalInputController` separates workspace actions, history, and application input.
Map/pane callers share navigation but retain pawn/camera behavior. Visible terminals
bypass field focus scopes. Auto-resume gates terminal keys while workspace controls
remain usable. Bindings belong to [keyboard shortcuts](../docs/src/reference/keyboard-shortcuts.md).

Workspace navigation runs before WindowStack consumes events and inside GUI.Window
before sidebar fields draw. Both paths handle only unconsumed KeyDown events. Handled
navigation releases Search focus and ends the draw; modal windows and binding capture
retain input. `WorkspaceNavigation` remembers separate session and open-file targets.
Crossing groups with Alt+Z/X or Alt+A/S restores the last eligible target; further
presses cycle within that group. Removed or filtered targets fall back to the
requested end. Native reader headers participate alongside daemon readers.
Background reader replacements remap the remembered handle without creating a visit.
Native fields receive keys inside the callback. Shift bypasses Alt
navigation; terminal encoding forwards the unshifted Alt letter/digit.
Ctrl+Alt letters and control punctuation retain both modifiers; Shift does not add
a separate modifier to these character shortcuts. Ctrl+V remains reserved for paste.
Printable input, including punctuation, follows native character events. Polling
physical key state must not synthesize a second text stream.
Opening a terminal suppresses the opening native key stroke, including its text
callbacks and repeats across frames. A fresh press starts a new stroke. Focus reset
clears this ownership; beginning a frame only clears undispatched events.
Literal input flushes at the end of native keyboard dispatch, independently of
repaint. Keep each dispatch batch together so UTF-16 surrogate pairs survive;
named keys flush preceding text before being sent.
While a terminal workspace owns input, IMGUI replays accepted native keyboard
events in arrival order with event-time modifiers, including repeats. Frame key
polling must not collapse multiple presses, sort Enter ahead of later text, or
apply a later Shift/Ctrl/Alt state to an earlier event. Focus reset and a new native
input frame discard undispatched events; fields and modal windows retain native routing.

Wheel input updates local history and application input without preparing a displayed
history view or renderer. [Shared scrolling](mod-ui-scrolling.md) owns queue admission,
native coverage, and bounded application report counts across deferred delivery.
The hovered visible pane receives wheel input while its workspace host has input;
scrolling does not change the selected pane or keyboard focus.

Each panel negotiates its own bounds using the same snapped cell advance for drawing
and hits. Refresh cannot pass one pane's geometry to another. Client allocation limits
remain independent of daemon capabilities.
While reading history, a pane defers daemon resizing so reflow cannot invalidate its
row anchor. The retained grid clips to the new pane bounds; returning live negotiates
the latest bounds. This also applies to divider drags and restored history positions.

Flush literal input before clipboard/PRIMARY paste and on focus loss; release forwarded
mouse presses too. Rejected input is discarded. Mouse-reporting apps own live clicks;
Shift/history keeps local selection. Clipboard replies check pane/run identity after
round trips. CLIPBOARD, PRIMARY, and OSC writes stay separate; PRIMARY never sends the
image shortcut. Codex tries text before image paste; clients read
text from their own X11 desktop and upload local PNG clipboard bytes when no text
is available. Host panes accept text only. [Clipboard](daemon-clipboard.md) owns image imports.

Shift+Enter travels as a named key, not literal escape bytes. The daemon owns its
foreground-shell fallback and legacy application compatibility; prompt title capture
tracks it as a newline without submission.

Selection across missing rows leaves the clipboard unchanged. Gesture endpoints
clamp to the displayed row count, including the unused pixel strip below the final
row; that strip must not become a nonexistent selected row. Translate endpoints
and drag anchors with history growth so highlights stay on text. Edge scrolling keeps
the pointer-row endpoint, clamping only outside displayed rows. Closing/rebinding
must end the old draw before resources can be recreated. Session navigation preserves
pawn selection and avoids camera jumps during Eco.

Link activation shares input ownership. Probe file types through daemon browsing only
on activation, never hover/repaint; preserve line targets and restrict Reveal to session
projects. Native previews own selection/copy, while paste may use their backing terminal.
See [history](mod-terminal-history.md), [rendering](mod-terminal-rendering.md), and
[capture](daemon-terminal-capture.md).

Reader wheel steps follow the magnitude of Waylith's accumulated frame event
(three event units per notch), for both mouse reports and arrow-key fallback.
Counting one step per event makes fast scrolling depend on the frame rate.
