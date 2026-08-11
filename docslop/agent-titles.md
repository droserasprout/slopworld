# Generated agent titles (planned)

Put automatic prompt summaries in `slopd`, not in each agent's extension system.
The daemon already sees input before tmux and OSC 0/2 titles after it, and already knows
how to read the OpenRouter key without exposing it inside an agent sandbox.

The policy should apply to every supported agent:

- `never`: do not generate a title; pass through the agent's OSC title.
- `once`: summarize the first real prompt in each conversation.
- `always`: summarize every real prompt, so the title follows the current task.

Keep the generated title as a `Live` override separate from the emulator's OSC title.
The session and screen views prefer the override when present, so an agent redraw cannot
replace it. Runtime-only state is enough initially; persistence beside private session state
can follow if titles should survive a daemon restart.

## Input and conversation boundaries

Mirror input while forwarding it normally; never delay the agent on the summary request.
Printable keys, paste, backspace/delete, cursor movement, common line kills and multiline
input need enough composer state to recover the submitted prompt. Unrecognised editing marks
the capture uncertain and skips naming. Empty input, slash commands, approval answers and
dialog selections are not prompts: accidentally sending auth or approval input to OpenRouter
is worse than missing a title.

A tmux process can hold more than one conversation. Give each live session a conversation
epoch and use a small adapter per agent to recognize boundaries such as Codex `/new`.
On a boundary, increment the epoch, clear the override and re-arm `once`; a supplied native
name such as `/new bug bash` can be kept without calling OpenRouter. Unknown agent controls
are conservative no-ops.

Every summary request also gets a generation number. Apply a response only when both its
conversation epoch and generation are still current. This prevents late responses from an
older prompt or conversation replacing a newer title. A failed `once` request may re-arm the
next real prompt.

## Existing Pi experiment

`.pi/extensions/auto-name.ts` is the prototype: it summarizes through a cheap OpenRouter
model, persists Pi's session name and sets the exact UI title. It is intended to run once
because it checks `pi.getSessionName()`, although observed repeated updates mean its guard or
async race needs verifying. A local pending/named state would close that race. Remove the
extension once the daemon implementation has parity, rather than retaining two title owners.

Codex already has persistent `/rename`, a `thread` terminal-title item and app-server
`thread/name/set`, but ordinary lifecycle hooks cannot set either. Raw SQLite writes and a
Codex fork are therefore worse SlopWorld seams than the daemon-owned override.
