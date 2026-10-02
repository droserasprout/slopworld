# Sidebar integration

`Patches/AgentSidebar/` owns the project-grouped sidebar and its colonist-bar integration.
The browse-scope catalog owns empty-state reasons (loading, failed, filtered, or unselected);
tree chrome only renders the reason supplied by its source. Catalog reads return detached
scope snapshots; callers cannot mutate retained identities. Bulk folding changes only the
supplied groups, preserving groups hidden by a filter.
View/navigation contracts are in [sidebar navigation](mod-sidebar-navigation.md).

The base game controls entry indices for reordering.
The sidebar controls their geometry. Filtered/folded
entries must be parked offscreen for both drawing and hit testing. Swap locations around
external `TryGetEntryAt` too: the normal draw finalizer has already restored vanilla positions.
Nested calls must not restore an outer swap early.

The back pass precedes base game input.
The front pass adds labels and actions. A finalizer must
clear temporary drawing state on exceptions. Compact portraits omit selection brackets and vanilla status overlays. Sidebar portrait
drawing and shared task-avatar requests live with the sidebar patches; daemon indicators
do not alter vanilla pawn idle classification. External bar hit tests translate scroll-content
locations by the body origin minus the scroll position and restore that exact offset.

Layout caches cannot depend solely on daemon revisions: native previews and pending viewer
handoffs change routed rows locally. `RoutedSessionRows` caches scan/sort results using the
session version, project-filter revision, pager/editor command settings and explicit local reader
invalidation. Config-only command changes must reclassify routed membership. Pager session
handoffs, native preview identity/content changes and reader collection mutations invalidate it.
Agent geometry also keys on pager/editor settings, since command changes can move a row
between Agents and Files/Git. Frame geometry resets preserve routed membership. Changing row height does not require a scan.
Routed headers and tree viewports must share clipping.
The shared draggable split for Files and Git retains independent scroll owners and a stable tree boundary when headers change. See [Files](mod-ui-files.md).

Worker hierarchy comes from explicit daemon metadata, never names. The plus menu's Worker action
opens the Spawn Worker dialog for a project, with template selection.
Manual agent start and restart failures open an OK dialog with the daemon error, including starts
from routed rows, gizmos, the session list, and the command palette.
Project and agent context menus keep their worker actions. A project action uses the host session by default. The user can select an agent.
An agent action uses that agent as context. Host, ephemeral, and worker rows are not ordinary colonists or
part of project agent counts. A temporary session rename mapping preserves membership until the HTTP and WebSocket handoff completes.
See [client](mod-client.md).

Files/Git terminal readers route by daemon intent. New reader names are opaque; snapshots carry
their label, source path, browse scope, key, line and pin. `FileReaders` reattaches surviving
pagers when Files or Git prepares routed rows. Search reattaches its own terminal reader
on entry or before appearance-restart discovery.
Appearance restarts and file refreshes preserve Search intent.
Legacy sessions still use command/name classification.

Resizing must renegotiate each visible terminal's assigned slot. Moving navigation between
left and right changes geometry, not view identity, focus, or scroll ownership.

Worker height counts use the actual nested lists grouped by the parent session’s project,
which also owns folding. Agent shortcuts in other tabs derive order from the colonist
inventory without depending on cleared Agents geometry. Resize-dependent pane updates
wait for the next frame’s workspace snapshot; Library reselection refreshes its catalog.
