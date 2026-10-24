# Sidebar integration

`UI/Sidebar/` owns grouped layout and open-files split geometry. `SidebarInteraction`
retains gestures across passes; `Patches/Sidebar/` integrates Harmony, while colonist
bar dispatch stays in `Patches/ColonistBar/`. [Navigation](mod-sidebar-navigation.md)
owns tabs/history; [browse scopes](mod-ui-browse-scopes.md) owns filters.
Ordinary Edit actions in sidebar menus and session gizmos exclude host, ephemeral,
and worker sessions; worker identity belongs to its task.

Vanilla supplies pawn identities; the sidebar sorts indices and assigns geometry.
Hiding it suppresses portraits and hit tests. Check for a live world during teardown.
Park folded/filtered entries offscreen, and retain screen-space positions for external
hit tests after restoring vanilla geometry. Nested swaps and exceptions must restore
the right state. The back pass precedes vanilla input; the front adds labels/actions.
Indicators do not change vanilla idle classification.

Freeze split geometry per frame so both draw passes and hit tests agree. Sidebar
insets must also reach vanilla buttons, inspect panes, gizmos, and colonist hit tests;
changing drawing alone leaves invisible targets. Internal split changes must not
resize terminals. The tab and open-files pane scroll independently.

Worker hierarchy comes from daemon metadata; missing/filtered parents leave children
at top level. Host, temporary, and worker rows do not count as ordinary project agents.
Rename handoffs preserve membership until HTTP and WebSocket agree. Expand truncated
host cwd labels only when their suffix matches `Dir`; preserve application/fixed titles.

`RoutedSessionRows` owns reader membership. Open-files visibility is independent of
tabs and preserves readers/splitter state when hidden. Filtering alone cannot hide
it, but explicitly closing the last visible row can. Pending replacements keep it
alive; stopped sessions do not. Delayed completion cannot reverse a user's visibility
choice. [Readers](mod-file-readers.md) owns the inventory and lifecycle.

Rows and keyboard cycling apply both project and checkout filters, caching scope
revision. Empty checkout IDs mean main; identity does not wait for descriptors.
The task board retains filtered rows, direction counts, and live selection membership
until `TaskStore` replaces its snapshot or filters change; [tasks](agent-tasks.md) owns
semantics. Library rows follow published catalog and worktree revisions plus their own
filters. Both lists submit only viewport rows; scrolling and folding do not rebuild
the Library model. Catalog publication must advance `HubCatalog.Revision`, and accepted
worktree replies or explicit retries must advance `WorktreeInventory.Revision`.

Worker launch opens the terminal after session refresh. The hub returns detached
identity rather than having the dialog resolve mutable catalogs. Base previews reject
superseded replies, including repeated requests for the same revision, and retire on
close. See [client](mod-client.md) for handoffs.
