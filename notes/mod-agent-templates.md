# Mod agent templates

`HubCatalog` owns the daemon-backed template list and refreshes it on connection and
when Library, the + menu, or agent/template editors open. Library lists templates alongside its
other entries. `EditSessionDialog` handles both agents and templates using the same tabs,
controls, snapshot-aware pickers, and preview. Template mode omits project, mounts, and
private-state actions; save targets the template catalog. The **+ > Agent** submenu offers
templates and **Custom**. Template selection happens before the editor opens; the form sends portable form overrides
to `SessionHub`. There is no template switch/reset in the form.

The existing agent editor's **Save as template** action uses a small naming dialog and the
daemon capture route. Catalog edits and deletes send the daemon-owned version; a failed
operation retains the open draft and offers explicit Reload to discard it and reconcile with
the newest catalog. Catalog request revisions suppress stale refresh responses while settling superseded page loads.
The form replaces the recipe, preserving unspecified choices: deep merging would retain
cleared fields. Template flags offer Session default; network/DNS are direct agent choices and
limits use No cap or Custom. Empty custom limits fail validation. Agent and template save
failures remain visible in the open editor. Project mounts are selected
in the project editor and are shown in the agent preview. The naming
capture dialog always saves the agent's persisted customizations, excluding project settings;
capture never includes project settings. Agent and template limits use
`ResourceLimitsForm`. Captured command and sandbox definitions take precedence over live catalog
entries. Writes disable the editor until they settle.
Shared sandbox pickers show missing references explicitly; direct references are removable,
while inherited references must be removed from their owning command or preset. No template
definitions are persisted in RimWorld profile settings.

Library refresh reloads the daemon's user-level template catalog through its API.

`DaemonSettingsPreview` renders the daemon's effective settings and contribution sources;
it does not resolve inheritance locally. Existing-agent editors fetch captured definitions
through the root-only preview endpoint, so their pickers also show the saved snapshots.
Preview requests are scoped to the draft and connection; Refresh handles external file edits.

The project editor owns editable From/To/mode mount rows and labels their next-start effect.
Add path appends a blank row; Add project copies current source and destination paths once. Library
breadcrumbs remain available through explicit terminal context-menu insertion; they are not part
of agent or template forms. `UiChoiceList` measures group headings inside the same scroll body
as the choices.
