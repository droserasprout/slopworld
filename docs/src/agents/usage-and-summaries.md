# Usage and summaries

SlopWorld can poll provider quotas and balances, and use OpenRouter to generate
short titles for agent prompts and tasks.

## Credentials

Set credential paths in **Settings > Integrations > Credentials**. These are files
on the daemon host.

| Provider | Credential |
| --- | --- |
| Anthropic | `~/.claude/.credentials.json`, re-read each poll |
| OpenAI / Codex | `~/.codex/auth.json` |
| OpenRouter | Configured key file, or the daemon's `OPENROUTER_API_KEY` when the path is blank |

OpenRouter usage polling and prompt summaries use the same key setting.
These settings do not control sandbox credential mounts. CLI presets can
separately share credential files with agents; enabling polling does not make
those mounts read-only. A summary key file is not automatically shared with
agents, though the Pi preset forwards `OPENROUTER_API_KEY` when set.

## Usage polling

Open **Settings > Integrations > Usage** to choose which quota windows and balances
to poll. Each row has a poll toggle and an optional interval; leave the interval
blank to use the global value, which defaults to 120 seconds. Provider minimum
intervals still apply. Polling starts disabled for every row. You can also change
the row's name and icon.

The global **Show Usage in statusbar** setting starts enabled. A row appears only
while polling is enabled and its provider has credentials. Turning off global
visibility hides the readout without stopping polling. Optional windows discovered
during a poll become available in Usage settings but start with polling disabled.

| Provider | Available usage | Default |
| --- | --- | --- |
| Anthropic | Session and weekly windows, Claude balance | Disabled |
| OpenAI / Codex | Account windows; free plans may expose only weekly usage | Disabled |
| OpenRouter | Balance | Disabled; enable `openrouter_balance` |

## Prompt summaries

Open **Settings > Agents > Summaries** to choose the OpenRouter model, minimum
prompt length, and when to generate titles:

- **Codex and Pi sessions:** `never`, `once`, or `always`.
- **Tasks:** `never` or `once`.

Prompts below the minimum length are skipped. For eligible prompts, the daemon
sends up to 2,000 characters to OpenRouter with the shared summary instruction
prepended. Tasks use the same model and summary settings.

For host shell titles and fixed labels, see [Host shells](../terminals/host-shells.md).
