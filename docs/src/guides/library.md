# Library items and errands

A library item stores a prompt or shell command for an agent. Prompt errands paste and submit
text. Shell errands run a command line.
A breadcrumb is a saved guidance block. Insert it from a terminal's context menu.

File-sidebar actions appear in the Files, Git, and Find context menus.
Their `command` runs against the selected path, with `{{ absolute_path }}` and
`{{ relative_path }}` available as substitutions. Set `mode` to `"nothing"`,
`"show_result"`, or `"open_terminal"` to choose what happens after selection.
If you omit `mode`, the default is **Ask**, offering a choice on every invocation.
The choice does not become a saved default. `nothing` runs without opening a result pane. The other
modes show captured output in an alert or open an interactive temporary terminal.

Manage prompts, errands, breadcrumbs, and file actions in the Library.
Manage command presets in **Settings > Commands**.
A user entry replaces the supplied entry with the same name.

Prompts and shell commands create temporary sessions.
Their Library editor has these execution choices:

- **Run using > Host** runs the session outside the sandbox.
- **Agent template: [name]** copies the template's command, sandbox additions, network, DNS,
limits, and private-state choices.
The selected project supplies the working directory and mounts.
A temporary project starts with an empty workspace.
Shell errands retain their shell executable.
Prompt errands use the template's command unless you supply a command override.

Each run copies the current template once and gets a fresh private identity. Editing or deleting
the template does not change an errand that already exists. Errands do not autostart or auto-resume.
Choose how to run an entry before you start it. File actions run on the daemon host.
