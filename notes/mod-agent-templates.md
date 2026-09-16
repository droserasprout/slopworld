# Mod agent templates

`HubCatalog` owns the daemon-backed template list and refreshes it on connection and
when Library, the + menu, or agent/template editors open. Library lists templates alongside its
other entries. `EditSessionDialog` handles both agents and templates using the same tabs,
controls, snapshot-aware pickers, and preview. Template mode omits project, mounts, and
private-state actions; save targets the template catalog. The **+ > Agent** submenu offers
templates and **Custom**. Template selection happens before the editor opens; the form shows its origin as information and sends portable form overrides
to `SessionHub`. There is no template switch/reset in the form.

The existing agent editor's **Save as template** action uses a small naming dialog and the
daemon capture route. Catalog edits and deletes send the daemon-owned version; a failed
operation retains the open draft and offers explicit Reload to discard it and reconcile with
the newest catalog. Catalog request revisions suppress stale refresh responses while settling superseded page loads.
The form replaces the recipe, preserving unspecified choices: deep merging would retain
cleared overrides. Template flags offer Session default; network/DNS/limits explicitly choose
Project default or Custom. Empty custom limits fail validation; resetting removes the override. The naming
capture dialog always saves the agent's persisted customizations, excluding project settings;
full effective capture remains an advanced API option. Project, template and agent limits use
`ResourceLimitsForm`. Captured command, sandbox and prompt definitions take precedence over live catalog entries; prompt
selection order is delivery order. Writes disable the editor until they settle.
Shared sandbox pickers show missing references explicitly; direct references are removable,
while inherited references must be removed from their owning project or preset. No template
definitions are persisted in RimWorld profile settings.

Repository templates are read-only in the dialog; duplication creates a personal snapshot.
Library refresh reloads repository definitions through daemon APIs.

`DaemonSettingsPreview` renders the daemon's effective settings and contribution sources;
it does not resolve inheritance locally. Existing-agent editors fetch captured definitions
through the root-only preview endpoint, so their pickers also show the saved snapshots.
Preview requests are scoped to the draft and connection; Refresh handles external file edits.

Sandbox and breadcrumb pickers group project contributions separately from agent additions.
Overlapping explicit selections remain removable without removing the project contribution.
`UiChoiceList` measures group headings inside the same scroll body as the choices.
