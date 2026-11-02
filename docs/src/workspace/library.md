# Library items and errands

Use the Library to save prompts and commands you run repeatedly. Errands open
temporary sessions; prompts can also insert text into an existing terminal; file
actions run against a selected path. Timers send prompts, create workers, or run shell scripts on a schedule.

## Prompt errands

Use **+ > Library** to create a prompt named `Explain this project`. Enter:

> Read the README and summarize how to build and test this project. Do not edit files.

Choose a project and an agent template under **Run using**, then save. In Library,
right-click the entry and select **Run**. A temporary agent terminal opens with
that prompt. Read its response there. Set **Where it runs** to **Ask me every time**
to reuse one entry across projects.

Choose **Run using > Reusable text only** for prompts used by terminals, Timers, or
agent startup without an errand's **Run** action.

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

Create **+ > Library > Timer** and choose an **Action**:

| Action | Settings |
| --- | --- |
| **Prompt** | A running agent and a Library Prompt to send |
| **Worker** | An agent template, project, and Library Prompt for the task |
| **Shell script** | A shell script from the Library |

Timer prompts resolve `{{ random_tip }}` from the shared loading-tip catalog even
while the game is closed. These unattended draws use unmarked tips.

Worker actions create a new one-shot, host-owned worker for each occurrence. The
selected Prompt supplies its task, with placeholders resolved for the new worker.
Shell actions use the selected item's execution settings and saved project or
temporary location. Set a saved location on scripts configured to **Ask** before
using them with a Timer.

Select **When**:

| Trigger | Schedule |
| --- | --- |
| **Calendar** | Daily or selected weekdays at a named-zone local time |
| **After daemon startup** | Once, a duration after slopd starts |
| **After activation** | Once, a duration after enabling the timer |
| **Repeat interval** | Repeatedly, a duration after activation and each interval boundary |

Durations accept `s`, `min`, `h`, and `d`, including combinations such as `1h 30min`.
Use a duration between one second and 366 days. The first interval occurs after a
full duration. Activation delays and intervals restart when you enable or change
the Timer, or restart the daemon. A startup timer enabled after its startup deadline
runs on the next scheduler check. One-shot timers remain enabled with status **Elapsed**
after their attempt; disable and enable them to arm them again.

For a Calendar timer, enter `*` for every day, `Mon..Fri` for weekdays, or a list
such as `Mon,Wed,Fri`, and a 24-hour time such as `09:00`. The time zone defaults to
your Settings selection, resolving Auto to your computer's named zone. The timer
saves that explicit zone, including its daylight-saving rules, so a remote daemon
or later Settings changes cannot shift the schedule. A nonexistent local time during
a daylight-saving transition is skipped; a repeated time fires only at its earlier
instant. **Preview schedule** shows upcoming triggers as if activated now, displayed
in your Settings time zone. It does not save or run the Timer.

Enable the Timer to schedule it. Its Library menu offers **Enable**, **Disable**, and
**Run now**. Run now executes the action even when disabled and leaves the scheduled
deadline unchanged. Details show whether the timer is Waiting, Elapsed, Disabled,
Activating, or Invalid, plus its last attempt, outcome, and next occurrence.

The daemon schedules Timers independently of the game, checking at approximately
one-second resolution. Calendar schedules use wall time; delays and intervals use
elapsed monotonic time, unaffected by system clock corrections. Prompt actions skip
busy, stopped, shell, and unready targets. Missed calendar minutes are not replayed after downtime;
relative timers start fresh after daemon restart. A delayed interval check makes at
most one attempt and preserves its original cadence. Multiple Timers due in one
scheduler pass submit at most one prompt to the same agent.

Prompt and shell script edits affect the next occurrence without restarting the Timer;
Timers reference live Library items. Missing prompts, templates, scripts, projects, or
targets withhold the action. Renaming an agent preserves its Timers; deleting and recreating it with the same name does not. Submission means
the input was queued, not accepted by the agent. Uncertain actions are not retried.

Edit a supplied Library item to create a personal override. Deleting that override
restores the supplied default. To pause a supplied Timer, disable it instead.
