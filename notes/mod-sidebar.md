# Sidebar integration

`UI/Sidebar/` owns grouped layout, rendering, and navigation, including shared
open-files split geometry. `Sidebar.LayoutState` retains row geometry and placement
cache inputs; `SidebarInteraction` owns scrolling and gestures across draw passes.
Overflowing sidebar labels, including Tasks, Search, Library, and shared content
trees, use `UiText.FadedRowLabel` to fade the right edge into the row background,
including hover and selection fills. Each view retains its own label rectangles
and space for metadata and actions.
Files, Git, and Search checkout headings mark the main checkout with the ginger-root
silhouette, tinted with the heading's grey; textual checkout identities remain unchanged.
`Patches/Sidebar/` owns Harmony label/portrait integration; colonist-bar dispatch
stays in `Patches/ColonistBar/`. Navigation and scope catalogs belong to
[sidebar navigation](mod-sidebar-navigation.md).

Hiding the sidebar suppresses the native colonist bar and its portrait hit tests on
both the map and terminal paths; vanilla portraits must not remain as fallback UI.
Visibility checks require a live world before reading its tile picker: scene teardown
can retain the playing state after the world is cleared.

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
`TaskFilters` owns independent status, direction, and agent selections. Each menu's
All checks every child; toggles can leave an empty selection. Selected agents match
either task participant, excluding the host identity. `FilterSelection` retains
All as a mode that includes future catalog entries until an individual toggle.

The shared open-files pane sits between the navigation row and the selected tab's
action row. Its profile visibility toggle is independent of tab selection and reader
membership; hiding it preserves readers, scroll position, and splitter fraction.
Closing the last reader hides the pane automatically. This follows the unfiltered
reader inventory, including native previews; changing filters does not hide it.
Explicitly dismissing the last visible row also hides the pane, even when other
readers remain outside the current filters. Dismissed session rows retire locally
before the daemon confirms their removal.
The toggle can still show an empty pane, and pending preview replacement keeps it open.
Stopped session metadata does not count as an open reader. Pending opens count toward
the pane's lifetime, so cancelling or failing the last open also hides the empty pane.
Alt+A/S reveal the pane only when a reader matches the current visibility filters.
The pane and selected tab have independent scroll owners. Sidebar split geometry
is frozen per frame so vanilla portrait placement, hit testing, and both draw passes
agree while visibility or the splitter changes. These internal pane changes do not
refresh workspace bounds or terminal dimensions. Shared lifetime belongs to
[file readers](mod-file-readers.md).

Project and checkout filtering belongs to [browse scopes](mod-ui-browse-scopes.md).
Session rows, including workers and routed readers, apply both filters and include
the scope revision in their caches. Empty session checkout IDs mean main;
visibility does not wait for checkout descriptors. Filtering also governs keyboard
cycling. Vanilla buttons, inspect panes, gizmos, and colonist hit tests must use the
workspace inset; changing only sidebar drawing leaves invisible old hit targets.

The worker dialog submits caller context, project, template, task body, and durability
through SessionHub. It adds the returned task and starts task/session refreshes;
only session refresh is awaited before notifying the dialog to open the terminal.
The hub supplies a detached worker result with its session name and refreshed
project/worktree identity so the dialog does not resolve it from mutable session state.
