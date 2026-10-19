# Mod project boundary

`UI/Views/Projects/` owns editing, temporary-directory preview state, and mount labels.
Client models retain editable `dir` and daemon-resolved `expanded_dir`; path operations
use the latter. Never expand paths using the game environment because daemon/sidecar
homes and variables can differ.

Temporary project root metadata belongs to daemon configuration, not `ProjectInfo`.
The daemon resolves temporary paths. Preview requests share unchanged-name work,
while disabling temporary mode invalidates pending replies. Project summary text
is shared by sidebar/Library rows and project pickers, not the project editor.
Daemon lifecycle belongs to [projects](daemon-projects.md), and user controls to
[Configuring agents](../docs/src/agents/configuring-agents.md).

`UI/Views/Projects/ProjectActions` owns project edit, duplication, and removal actions.
`UI/Terminal/HostTerminalActions` owns shared host-shell launch and removal.
`UI/Sessions/SessionActions` owns shared session controls, duplication, and agent shells.
Menu owners retain labels, ordering, project filtering, and availability guards;
worktree controls can retain local failure presentation. `SessionStore` resolves
project scope keys and worktree identity before launching.
