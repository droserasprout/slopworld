# Terminal and input bug review

Reviewed in source, 2026-09-08. Five fixes remain; the submenu change was reverted
after manual testing reported broken navigation. The original submenu finding is
unconfirmed. The game was not launched by the agent.

## Submenu navigation processes a key multiple times — unconfirmed; fix reverted

`mod/Source/SlopWorld/UI/UiMenu.Input.cs`

The parent recursively calls its child's keyboard handler. The child calls it
again during its own window draw. Because the handler reads `rawType`, consuming
the event does not prevent this second dispatch. Open a submenu and press Down:
one press can skip rows, with more dispatches at greater nesting depth.

The attempted root-only dispatch broke submenu navigation in manual testing and
was reverted. The source review assumed both parent and child received the same
keyboard event; this needs runtime evidence before another dispatch change.

## Copying a scrolling selection omits offscreen rows — fixed

`mod/Source/SlopWorld/UI/TerminalWindow/TerminalWindow.Selection.cs`

Drag a selection beyond one viewport using edge scrolling, then copy. Selection
endpoints track the scroll, but extraction clamps both endpoints to the currently
displayed buffer. Selected rows outside that buffer are silently omitted.

Extraction now uses displayed rows and retained `TerminalHistory` rows for the full
range. Reverse drags and partial endpoint columns are preserved. If a row is no
longer cached, copy leaves the clipboard unchanged rather than truncating the range.

## Clicking history forwards a click to the live application — fixed

`mod/Source/SlopWorld/UI/TerminalMouseInput.cs`

Enter history with Shift+PageUp while a primary-screen application reports mouse
input, then click historical text. Unlike wheel routing, `ShouldForwardMouse`
ignores `ScrollOffset`. The forwarding handler jumps to live and sends a press at
the historical click's screen coordinates, potentially activating an unrelated
live control.

New forwarded gestures now require the live view; historical selection stays local.

## Changing Shift during a mouse gesture loses the release — fixed

`mod/Source/SlopWorld/UI/TerminalMouseInput.cs`

Press the left button in a mouse-enabled application, hold Shift, then release.
The Shift gate bypasses forwarding, so the application never receives its matching
release and `_mouseFwd` remains set.

Forwarded presses now own their button's continuation before local-input gates,
regardless of modifier changes. Local selections also retain their continuation
when Shift is released. Click-only apps retain their drag-to-selection handoff.

## Punctuation control chords are swallowed — fixed

`mod/Source/SlopWorld/UI/TerminalMouseInput.cs`

Ctrl+Space, Ctrl+[, Ctrl+backslash, and Ctrl+] have no dispatch/mapping path.
The printable fallback rejects Control-modified input. This prevents standard
terminal shortcuts, including Ctrl+[ for Escape and Ctrl+] for a telnet escape.

Dispatch and mappings now cover Ctrl+Space/@, Ctrl+[, Ctrl+backslash, Ctrl+],
Ctrl+^, and Ctrl+_, including shifted 2/6/minus key codes.

## Horizontal scrolling sends downward input — fixed

`mod/Source/SlopWorld/UI/TerminalMouseInput.cs`

In an alternate-screen or mouse-enabled application, a horizontal-only wheel
packet has `delta.y == 0`. The minimum step is nevertheless clamped to one, and
the direction defaults downward. Each packet sends Down or `wheeldown`.

Mouse-reporting apps ignore packets without vertical movement. Alternate-screen
apps without mouse reporting now map the dominant wheel axis to Left/Right or
Up/Down, allowing sideways scrolling without accidental downward input.

## Validation

`make test-mod` passes 173 tests, including new multi-viewport copy regressions for
reverse selection, live shifts, missing rows, and displayed text with ANSI styling.
`make lint-mod` builds with warnings as errors and checks whitespace.
Menu and mouse event behavior still needs in-game verification.
