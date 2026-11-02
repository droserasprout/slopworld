# Library items and errands

Use the Library to save prompts and commands you run repeatedly. Errands open
temporary sessions; prompts can also insert text into an existing terminal; file
actions run against a selected path. Timers send an existing Prompt to a durable agent on a schedule.

## Prompt errands

Use **+ > Library** to create a prompt named `Explain this project`. Enter:

> Read the README and summarize how to build and test this project. Do not edit files.

Choose a project and an agent template under **Run using**, then save. In Library,
right-click the entry and select **Run**. A temporary agent terminal opens with
that prompt. Read its response there. Set **Where it runs** to **Ask me every time**
to reuse one entry across projects.

Prompts can also use **Run using > Reusable text only**. These are editable text definitions
for the terminal Prompts menu, Timers and agent startup, without a temporary-agent Run action.

## Shell errands

Create a shell entry named `Working tree status` with command line `git status --short`.
Choose an existing Git project and **Run using > Host**, then save and **Run** it
from Library. The command runs in that project's directory in a temporary terminal.
Use the same pattern for a project's build or test command.

<a id="errand-execution"></a>

## Where errands run

Choose the project and execution settings before starting an errand. The project
supplies its working directory and mounts; a temporary project starts empty.

| Run using | Effect |
| --- | --- |
| **Host** | Runs outside the sandbox |
| **Agent template: [name]** | Copies the template's command, sandbox additions, network, DNS, limits, and private-state choices |

Shell errands keep their shell executable. Prompt errands use the template's
command unless you supply an override.

<a id="session-lifetime"></a>

Each run copies the current template once and gets a fresh private identity.
Later template edits or deletion do not change an existing errand. Errands do
not autostart or auto-resume.

## Prompts in terminals

Create a prompt with **Run using > Reusable text only** named `Review instructions` with text such as:

> Review the changes for correctness and missing tests. Report findings with file paths.

Save it, open an agent terminal, and choose it under **Prompts** in the terminal's
context menu. This pastes the text without pressing Enter, so you can edit it before
submitting. All prompts appear here, including prompts configured to run as errands.

## File actions

<a id="example"></a>

Create a file action named `Count lines` with command `wc -l {{ absolute_path }}`
and mode **Show result**. Save it, then right-click a text file in **Files** and
choose the entry under **File actions** to see its line count.

Actions also appear in Git and Search context menus. They run on the daemon
host, outside the agent's sandbox. Commands can use `{{ absolute_path }}` and
`{{ relative_path }}` for the selected path.

| Mode | Result |
| --- | --- |
| Omitted (**Ask**) | Choose on each invocation; the choice is not saved |
| `nothing` | Run without opening a result pane |
| `show_result` | Show captured output in an alert |
| `open_terminal` | Open an interactive temporary terminal |

## Library management

A user entry replaces the supplied entry with the same name. The Library also
stores [agent templates](../agents/configuring-agents.md#templates).
Manage [command presets](command-presets.md) in **Settings > Commands**.

## Timers

Create **+ > Library > Timer**, enable it, choose a target agent and a Library Prompt,
and enter a five-field UTC cron expression. `*/15 * * * *` checks every fifteen minutes;
`0 9 * * 1-5` checks at 09:00 UTC on weekdays. Fields support numbers, `*`, ranges,
comma-separated lists and positive steps. Weekdays use 0 for Sunday through 6 for Saturday.
When both day-of-month and weekday are restricted, either can match.

The daemon schedules Timers independently of the game. Busy, stopped, shell and unready
targets are skipped. Missed occurrences are not replayed after downtime. Multiple Timers
for the same agent can submit at most one prompt per minute. Prompt edits affect the next
occurrence; Timers do not copy text. A missing Prompt or target withholds delivery.

Named agents are bound by stable identity: renaming preserves the target, deleting and
recreating an agent with the same name does not. Timer details show the last
attempt, its outcome and the next scheduled occurrence in UTC. A recorded submission
means queued terminal input, not agent acceptance. An interrupted claim is not retried.

Edit a supplied Library item to create a personal override. Deleting that override restores
the supplied default. To pause a supplied Timer, disable it instead of deleting its override.
