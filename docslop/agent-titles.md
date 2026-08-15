# Generated agent titles

Put automatic prompt summaries in `slopd`, not in each agent's extension system.
The daemon already sees input before tmux and OSC 0/2 titles after it, and already knows
how to read the OpenRouter key without exposing it inside an agent sandbox.

`[daemon] agent_titles` selects the Codex policy and defaults to `never`, because enabling it
sends prompt text to OpenRouter. `[daemon] host_titles` defaults to `true` and controls
summaries for commands entered in host terminals. `title_model` is the one model used by all
summaries; `openrouter_key_file`, or slopd's `OPENROUTER_API_KEY` when it is blank, supplies
the key.

The Summaries settings page exposes the Codex and Pi title policies, the host-command toggle,
and one shared model field. Its key is the Usage page's OpenRouter key file, which is editable
with credit polling off because title generation does not need polling. Pi defaults to `always`.

Both Codex and Pi use the daemon path. Explicit command lines such as `codex --yolo` and
`pi --model …` are recognized as well as named presets:

- `never`: do not generate a title; pass through the agent's OSC title.
- `once`: summarize the first real prompt in each conversation.
- `always`: summarize every real prompt, so the title follows the current task.

When `host_titles` is enabled, host terminals summarize every substantive command submission.
Host commands have no agent conversation boundary, so the toggle is on/off rather than
never/once/always.

The generated title is a runtime-only `Live` override separate from the emulator's OSC title.
The session view prefers the override, so an agent redraw cannot replace it. Persistence can
follow if titles should survive a daemon restart. Successful summaries are now cached in the
daemon config directory as `prompt-summaries.toml` (or beside the configured `SLOPD_CONFIG`),
with a stable prompt/model digest rather than prompt text. The cache keeps the newest 1024
entries and is written atomically with mode `0600`; a cache failure never prevents a title from
being applied.

## Input and conversation boundaries

Input is mirrored while being forwarded normally; the summary request never delays the agent
or host terminal.
Printable keys, paste, backspace/delete, cursor movement, common line kills and multiline
input need enough composer state to recover the submitted prompt. Common readline aliases are
handled too; an unsupported editing control clears the mirror and marks the capture uncertain,
so cancelled or history input cannot bleed into the next prompt. Empty input, slash commands,
approval answers and dialog selections are not prompts: accidentally sending auth or approval
input to OpenRouter is worse than missing a title. A substantive prompt is still captured if
the stale screen classification says waiting.

A tmux process can hold more than one conversation. Each live session has a conversation
epoch and the title adapter recognizes `/new`.
On a boundary, increment the epoch, clear the override and re-arm `once`; a supplied native
name such as `/new bug bash` is kept without calling OpenRouter. Unknown editing controls
make the capture uncertain and skip that submission.

Every summary request gets a generation number. A response applies only when both its
conversation epoch and generation are still current. This prevents late responses from an
older prompt or conversation replacing a newer title. A `once` policy counts its first request
even if OpenRouter fails, so later prompts cannot create more billable attempts. Requests emit
structured title outcomes under `slopd::titles`. Recognizable approval or dialog answers
submitted while the session is `waiting` are not sent; a substantive prompt is still named.

## Remaining adapters

Claude and OpenCode do not yet use the daemon path.

## Pi extension

`.pi/extensions/auto-name.ts` remains useful for Pi launched outside SlopWorld. Managed Pi
sessions set `SLOPWORLD_PI_TITLES=never`: the daemon owns their titles, avoiding Pi's
project-trust gate and keeping the OpenRouter key out of the sandbox.

Codex already has persistent `/rename`, a `thread` terminal-title item and app-server
`thread/name/set`, but ordinary lifecycle hooks cannot set either. Raw SQLite writes and a
Codex fork are therefore worse SlopWorld seams than the daemon-owned override.
