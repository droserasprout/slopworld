# Library items and errands

Use the Library to save prompts and commands you run repeatedly. Errands open
temporary sessions; breadcrumbs insert text into an existing terminal; file
actions run against a selected path.

## Prompt errands

Use **+ > Library** to create a prompt named `Explain this project`. Enter:

> Read the README and summarize how to build and test this project. Do not edit files.

Choose a project and an agent template under **Run using**, then save. In Library,
right-click the entry and select **Run**. A temporary agent terminal opens with
that prompt. Read its response there. Set **Where it runs** to **Ask me every time**
to reuse one entry across projects.

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

## Breadcrumbs

Create a breadcrumb named `Review instructions` with text such as:

> Review the changes for correctness and missing tests. Report findings with file paths.

Save it, open an agent terminal, and insert the breadcrumb from the terminal's
context menu. It supplies reusable text to the current terminal rather than
creating an errand.

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
