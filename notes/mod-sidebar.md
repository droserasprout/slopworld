# Sidebar integration

`Patches/AgentSidebar/` owns the project-grouped sidebar and its colonist-bar integration.
View/navigation contracts are in [sidebar navigation](mod-sidebar-navigation.md).

Vanilla owns entry indices for reordering; the sidebar owns their geometry. Filtered/folded
entries must be parked offscreen for both drawing and hit testing. Swap locations around
external `TryGetEntryAt` too: the normal draw finalizer has already restored vanilla positions.
Nested calls must not restore an outer swap early.

The back pass precedes vanilla input; the front pass adds labels/actions. A finalizer must
clear temporary drawing state on exceptions. Portrait selection corners need the same crop,
scroll group and coordinate transform as the portrait, including multi-selection.

Layout caches cannot depend solely on daemon revisions: native previews and pending viewer
handoffs change routed rows locally. Routed headers and tree viewports must share clipping;
Files/Git's shared draggable split retains independent scroll owners and a stable tree boundary when
headers change. See [Files](mod-ui-files.md).

Worker hierarchy comes from explicit daemon metadata, never names. The plus menu's Worker action
opens the template-based Spawn Worker dialog by project; project and agent context menus retain
their targeted worker actions. Project actions default to the host caller and can select an agent,
while agent actions supply that context. Host/ephemeral/worker rows are not ordinary colonists or
part of project agent counts. A short-lived session rename
mapping preserves membership until the HTTP/socket handoff settles; see [client](mod-client.md).

Resizing must renegotiate each visible terminal's assigned slot. Moving navigation between
left and right changes geometry, not view identity, focus, or scroll ownership.
