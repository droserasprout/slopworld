# Sidebar integration

`UI/Sidebar/` owns grouped layout, rendering, and navigation.
`Patches/AgentSidebar/` owns Harmony label/portrait integration; colonist-bar dispatch
stays in `Patches/ColonistBar/`. Navigation and scope catalogs belong to
[sidebar navigation](mod-sidebar-navigation.md).

Hiding the sidebar suppresses the native colonist bar and its portrait hit tests on
both the map and terminal paths; vanilla portraits must not remain as fallback UI.

Vanilla supplies pawn entries and identities; the sidebar sorts indices within its
groups and assigns geometry. Filtered/folded entries must be parked offscreen for
drawing and hit testing. External hit tests need screen-space locations even after
the draw finalizer restores vanilla positions. Nested calls must not restore an
outer swap early, and exceptions must clear temporary drawing state.

The back pass precedes base-game input; the front pass adds labels/actions. Compact
portraits omit selection brackets and vanilla status overlays. Sidebar indicators
do not change vanilla idle classification.

Worker hierarchy uses explicit daemon metadata, never names. Workers nest under a
visible parent; root/host children and children with absent or filtered parents appear
at the top level. Host, ephemeral, and worker rows are excluded from ordinary
colonist/project agent counts. Rename handoffs preserve membership until HTTP and
WebSocket agree. Manual start/restart failures surface the daemon error.

`SidebarRowRenderer.RestoreHostPath` reconstructs truncated host cwd titles only
when their suffix matches the session view's `Dir`. Saved tabs back it with persisted
paths; temporary errands also carry it. Fixed labels bypass reconstruction, and
command/application titles remain intact. See [host tabs](daemon-host-terminals.md).

`RoutedSessionRows` owns routed-reader membership. [Files](mod-ui-files.md) owns
shared tree/reader panes and reveal behavior; [Git](mod-ui-git.md) owns Git actions;
[Search](mod-ui-search.md) owns its separate reader. [The client](mod-client.md)
owns daemon handoffs and [terminal](mod-terminal.md) owns pane sizing/input.
`TaskStore` supplies replacement snapshots to the host board, which lists and prunes
the same global mailbox set; see [task mailboxes](agent-tasks.md).

Files/Git routed headers and tree viewports share clipping, upper scroll position,
and a saved splitter fraction. Their draggable split has independent scroll owners
and a stable tree boundary as reader headers change. Shared lifetime belongs to
[file readers](mod-file-readers.md).

An empty project-filter set means all; unknown saved keys mean no match. Filtering
also governs keyboard cycling. Vanilla buttons, inspect panes, gizmos, and colonist
hit tests must all use the workspace inset; changing only sidebar drawing leaves
invisible old hit targets. Shared scopes belong to [browse scopes](mod-ui-browse-scopes.md).

The worker dialog submits caller context, project, template, task body, and durability
through SessionHub. It adds the returned task and starts task/session refreshes;
only session refresh is awaited before notifying the dialog to open the terminal.
