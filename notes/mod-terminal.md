# Terminal ownership and input

`TerminalWindow` shows shared workspace controls. `TerminalSplit` holds one or two panels.
`TerminalPanel` owns terminal state, caches, subscriptions, and input.
When Settings covers a terminal, the panel stays open. Settings changes its visibility and focus.
When a user closes or switches panels during input, stop the old draw before it recreates released resources.

Each panel sets its size from its assigned bounds. Rendering, hit tests, and resize requests share a pixel-snapped cell advance.
If returned frames disagree, send another resize request. A redeploy can lose the request.
Do not send one grid size to both split panes. Do not restore a static size from the last session.
The app does not save split placement. Daemon capabilities report the supported dimension range and history capacity.
The client checks and limits these values before layout and cache allocation. The client also applies its own allocation limits.
Before the capability announcement arrives, the client uses local safety limits.

Escape belongs to the application. The workspace handles close and leave keys before it forwards input.
Send shifted navigation keys to the application on alternate screens.
On the primary screen, page keys scroll local history. Send Tab to the application.
Before adding another key binding, check `TerminalHotkeys` and the panel input code.

Clipboard handling depends on the target. Codex checks for text before it handles its image-paste shortcut.
Ctrl+V with plain text can cause a missing-image error.
Host panes accept only text. PRIMARY selection and explicit OSC clipboard writes use separate channels from ordinary CLIPBOARD.
Clicks in history stay local. Release forwarded drags even when Shift or focus changes.

Unity can lose semicolon character events. The named-key fallback runs once per frame.
It sends Shift+semicolon as a colon. Flush ordinary text before it sends semicolons as pasted text.

See [history](mod-terminal-history-warmup.md) for scroll-cache requirements.
See [rendering](mod-terminal-rendering.md) for damage, fonts, and links.
See [daemon capture](daemon-session-state.md) for terminal bytes and query responses.
