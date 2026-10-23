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

Each panel negotiates its own bounds using the same snapped cell advance for drawing
and hits. Refresh cannot pass one pane's geometry to another. Client allocation limits
remain independent of daemon capabilities.

Flush literal input before clipboard/PRIMARY paste and on focus loss; release forwarded
mouse presses too. Rejected input is discarded. Mouse-reporting apps own live clicks;
Shift/history keeps local selection. Clipboard replies check pane/run identity after
round trips. CLIPBOARD, PRIMARY, and OSC writes stay separate; PRIMARY never sends the
image shortcut. Codex tries text before forwarding its image shortcut; host panes
accept text only. [Clipboard](daemon-clipboard.md) owns host image imports.

Shift+Enter travels as a named key, not literal escape bytes. The daemon owns its
foreground-shell fallback and legacy application compatibility; prompt title capture
tracks it as a newline without submission.

Selection across missing rows leaves the clipboard unchanged. Translate endpoints
and drag anchors with history growth so highlights stay on text. Edge scrolling keeps
the pointer-row endpoint, clamping only outside displayed rows. Closing/rebinding
must end the old draw before resources can be recreated. Session navigation preserves
pawn selection and avoids camera jumps during Eco.

Link activation shares input ownership. Probe file types through daemon browsing only
on activation, never hover/repaint; preserve line targets and restrict Reveal to session
projects. Native previews own selection/copy, while paste may use their backing terminal.
See [history](mod-terminal-history.md), [rendering](mod-terminal-rendering.md), and
[capture](daemon-terminal-capture.md).
