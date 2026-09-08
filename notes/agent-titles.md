# Generated agent titles

Put automatic prompt and task summaries in `slopd`, not in each agent's extension system.
The daemon already sees input before tmux and OSC 0/2 titles after it, and already knows
how to read the OpenRouter key without exposing it inside an agent sandbox.

`[daemon] agent_titles` selects the Codex policy and defaults to `never`, because enabling it
sends prompt text to OpenRouter. `title_model` is shared by the agent title policies;
`openrouter_key_file`, or slopd's `OPENROUTER_API_KEY` when it is blank, supplies the key.
`title_min_chars` defaults to 20 Unicode characters and skips shorter prompts before they
consume a `once` attempt or make a request.

The Summaries settings page exposes the Codex and Pi title policies, one task-summary policy,
minimum prompt length, and one shared model field. Its key is the Usage page's OpenRouter key
file, which is editable with credit polling off because title generation does not need polling.
Pi defaults to `always`; task summaries default to `never` and support `never` or `once`, one
summary per delegated task.

Both Codex and Pi use the daemon path. Explicit command lines such as `codex --yolo` and
`pi --model …` are recognized as well as named presets:

- `never`: do not generate a title; pass through the agent's OSC title.
- `once`: summarize the first real prompt in each conversation.
- `always`: summarize every real prompt, so the title follows the current task.

Host terminals do not send shell commands to the title service. Their sidebar title is the
terminal application's OSC title unless the user sets a fixed label.

The generated title is a `Live` override separate from the emulator's OSC title. The session
view prefers the override, so an agent redraw cannot replace it. Successful summaries are cached
in the daemon config directory as `prompt-summaries.toml` (or beside the configured
`SLOPD_CONFIG`), with a stable prompt/model digest rather than prompt text. The cache also keeps
the latest title for each durable session, restoring it after a daemon restart and clearing it
when the agent stops, starts a new conversation, or is removed as an ephemeral session. It keeps
the newest 1024 prompt summaries and is written atomically with mode `0600`; a cache failure
never prevents a title from being applied.

## Input and conversation boundaries

Input is mirrored while being forwarded normally; the summary request never delays the agent.
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
