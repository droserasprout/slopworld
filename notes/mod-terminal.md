# Terminal ownership and input

`TerminalWindow` owns workspace chrome; `TerminalSplit` retains one or two panels.
`TerminalPanel` owns terminal state, caches, subscriptions and input. Covering a terminal
with Settings changes visibility/focus, not lifetime. Closing or switching during input must
stop the old draw before it recreates released resources.

Sizes come from each panel's assigned bounds, with pixel-snapped cell advance shared by
rendering, hit tests and resize negotiation. Retry while returned frames disagree: redeploy
can lose a resize. Do not broadcast one grid to both split panes or resurrect a static
last-used size. Split placement is not persisted. Daemon capabilities advertise the supported
dimension bounds and history capacity; the client validates and clamps those values before layout
and cache work, while independent client allocation caps remain in force. Older daemons use the
local safety baseline only when they omit that capability.

Chrome and application key ownership differ. Escape belongs to the application; workspace
close/leave keys are handled before forwarding. Shifted navigation is forwarded on alternate
screens, while primary-screen page movement uses local history. Tab remains application input.
Check `TerminalHotkeys` and panel input code before adding another binding path.

Clipboard handling depends on the target. Codex needs text-only probing before forwarding
its image-paste shortcut; forwarding Ctrl+V for plain text can trigger a missing-image error.
Host panes accept text only. PRIMARY selection and explicit OSC clipboard writes are separate
from ordinary CLIPBOARD. Historical clicks remain local; forwarded drags must receive release
even if Shift or focus changes.

Unity can lose semicolon character events. The named-key fallback is frame-deduplicated,
suppressed for colon, and uses byte-preserving paste after flushing ordinary text.

For scroll-cache invariants see [history](mod-terminal-history-warmup.md); for damage, fonts
and links see [rendering](mod-terminal-rendering.md). Terminal bytes and query responses are
covered in [daemon capture](daemon-session-state.md).
