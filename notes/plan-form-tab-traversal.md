# Form Tab traversal

## Outcome and scope

Make Tab and Shift+Tab move between enabled editable fields in Settings, shared
dialogs, and form content views. This addresses field traversal; buttons, checkboxes,
selectors, sidebar navigation, and inter-panel focus need a separate control-focus
extension. A form containing those controls will still require mouse interaction.

Start from [focus ownership](ui-focus.md). Existing restoration, field lifetimes,
exact native control-ID lookup, and nested scroll reveal should remain the owners.
The previous traversal handler was removed after runtime checking failed. The current
source does not establish that failure's cause; event ordering is a risk to investigate,
not a confirmed diagnosis.

## Interaction contract

- Tab advances and Shift+Tab reverses through rendered, enabled editable fields in
  logical form order, wrapping at either end. An unfocused form enters at the first
  field for Tab and the last for Shift+Tab. A single eligible field keeps focus.
- Skip disabled, read-only, and conditionally absent fields. Offscreen fields inside
  a scroll area remain eligible and are revealed when selected.
- Multiline form fields participate in traversal; Enter remains a newline. Audit
  instruction/code editors for an explicit need to insert literal tabs before rollout;
  any exception must be a declared field policy, not inferred from content.
- Ctrl/Alt/Command+Tab are outside this handler. Preserve function-key release,
  dialog accept/cancel, keybinding capture, and native text editing shortcuts.
- Only the active input-owning form handles traversal. Menus and modal children take
  precedence. Terminal Tab/BTab remains application input; Search and the command
  palette retain their separate input ownership.
- Moving focus does not change field text or force Select All. Preserve native editor
  caret/selection behavior and the existing visible focus treatment.

## Implementation sequence

### 1. Establish the event boundary

Trace `UiWindow`, `TerminalWindow`, and `ModOptions.TabState` scope entry through
`UiText` and `TextEntryController.Draw`. Account for native field consumption and
WindowStack's Used/rawType handling. Inspect prior traversal history if available.
Do not add a global Tab interception patch by default.

Define one form-owned traversal request containing direction and starting field
identity. Capture it before native controls can move focus or insert a tab, then
resolve it after the current pass has registered all eligible targets. Preserve
control allocation/draw order even when suppressing native input for that event.

Recover a Used event only when ownership proves it belongs to this form; rawType
alone must not replay a key consumed by a menu, binding capture, or another scope.
Ensure one physical event creates one transition, including duplicate IMGUI passes
and character events, while allowing deliberate key repeat. Empty forms must not
claim traversal. Validate first-pass behavior when no previous registration exists.

### 2. Add deterministic ordering

Extend `FieldFocusOrder` with pure next/previous resolution by stable field name.
Keep restoration separate from traversal: returning to a form restores memory,
whereas an explicit Tab chooses a neighbor. Resolve against current registrations,
deduplicate names, and define missing-current-field behavior as first/last entry.

Audit participating forms for stable unique names and logical registration order,
including conditional rows and responsive columns. If a scroll owner culls fields,
publish their semantic order independently so traversal can reach them.

### 3. Apply focus through the existing scope

Let `FieldFocusScope` resolve requests at the end of registration and map the chosen
name to this pass's exact control ID. Reuse its selection-release and nested-scroll
reveal path, update remembered identity, and prevent automatic restoration from
overwriting explicit traversal. Recheck lifetime and input ownership before applying.

Keep any deferred request bound to its originating lifetime; discard it on close,
tab replacement, or loss of ownership. Do not retain native IDs across passes.
Verify pending clipboard edits cannot migrate to the newly focused field.

### 4. Validate and document

Expand `mod/Tests/FieldFocusTests.cs` for forward/reverse wrap, empty/single fields,
unfocused entry, duplicates, reordered/removed fields, and independent owners.
Test event eligibility and one-transition semantics in a small pure policy helper
if extracted; pure tests alone cannot validate Unity focus behavior.

Run `make test-mod` and `make lint-mod`. The latter rebuilds the installed-source
assembly in Release; follow the [build guide](../docs/src/build.md).

Runtime acceptance requires a separately requested in-game check. Verify:

- Settings and a shared multi-field dialog, forward and reverse, with immediate
  typing landing in the destination and no inserted tab characters.
- Multiline fields, conditional/disabled fields, and nested scrolling in both
  directions; wrap and first-entry behavior.
- Mouse focus followed by Tab, held Tab, modal/menu open and close, category changes,
  and a clipboard reply arriving after focus moves.
- Keybinding capture can record Tab; function keys and Enter/Escape retain behavior.
- Terminal Tab and Shift+Tab reach the application, including with a split open.

Record any unperformed runtime checks explicitly; do not call traversal validated
solely because ordering tests pass. Once implemented and verified, update `ui-focus.md`
and the focused user keyboard documentation, then remove this unresolved-work note.
