# Sidebar integration

`Patches/AgentSidebar/` owns the project-grouped sidebar and its colonist-bar integration.
View/navigation contracts are in [sidebar navigation](mod-sidebar-navigation.md).

The base game controls entry indices for reordering.
The sidebar controls their geometry. Filtered/folded
entries must be parked offscreen for both drawing and hit testing. Swap locations around
external `TryGetEntryAt` too: the normal draw finalizer has already restored vanilla positions.
Nested calls must not restore an outer swap early.

The back pass precedes base game input.
The front pass adds labels and actions. A finalizer must
clear temporary drawing state on exceptions. Portrait selection corners need the same crop,
scroll group and coordinate transform as the portrait, including multi-selection.

Layout caches cannot depend solely on daemon revisions: native previews and pending viewer
handoffs change routed rows locally. `RoutedSessionRows` caches scan/sort results using the
session version, project-filter revision, pager/editor command settings and explicit local reader
invalidation. Config-only command changes must reclassify routed membership. Pager session
handoffs, native preview identity/content changes and reader collection mutations invalidate it.
Frame geometry resets preserve routed membership; changing row height does not require a scan.
Routed headers and tree viewports must share clipping.
The shared draggable split for Files and Git retains independent scroll owners and a stable tree boundary when headers change. See [Files](mod-ui-files.md).

Worker hierarchy comes from explicit daemon metadata, never names. The plus menu's Worker action
opens the Spawn Worker dialog for a project, with template selection.
Project and agent context menus keep their worker actions. A project action uses the host session by default. The user can select an agent.
An agent action uses that agent as context. Host, ephemeral, and worker rows are not ordinary colonists or
part of project agent counts. A temporary session rename mapping preserves membership until the HTTP and WebSocket handoff completes.
See [client](mod-client.md).

Resizing must renegotiate each visible terminal's assigned slot. Moving navigation between
left and right changes geometry, not view identity, focus, or scroll ownership.
