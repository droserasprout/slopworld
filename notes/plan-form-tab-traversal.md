# Form Tab traversal

Status: proposed

## Outcome and scope

Make Tab and Shift+Tab move between enabled editable fields in Settings, shared
dialogs, and form content views. This addresses field traversal.
Buttons, checkboxes, selectors, sidebar navigation, and inter-panel focus need a separate control-focus extension. A form containing those controls will still require mouse interaction.

Start from [focus ownership](ui-focus.md). Existing restoration, field lifetimes,
exact native control-ID lookup, and nested scroll reveal should remain the owners.
Runtime checks showed that the previous traversal handler failed. The current
source does not establish the cause of that failure.
Event ordering is a risk to investigate, not a checked diagnosis.

## Interaction contract

- Tab advances and Shift+Tab reverses through rendered, enabled editable fields in
  logical form order, wrapping at either end. An unfocused form enters at the first
  field for Tab and the last for Shift+Tab. A single eligible field keeps focus.
- Skip disabled, read-only, and conditionally absent fields. Keep offscreen fields inside
  scroll areas eligible and scroll each selected field into view.
- Include multiline form fields in traversal. Enter remains a newline. Check
  instruction and code editors for a need to insert literal tabs before release.
  Record each exception as a field policy. Do not infer it from content.
- Ctrl/Alt/Command+Tab are outside this handler. Preserve function-key release,
  dialog accept/cancel, keybinding capture, and native text editing shortcuts.
- Only the active input-owning form handles traversal. Menus and modal children take
  precedence. Terminal Tab/BTab remains application input. Search and the command
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
resolve it after the current pass registers all eligible targets. Preserve
control allocation/draw order even when suppressing native input for that event.

Recover a Used event only when ownership proves it belongs to this form.
rawType alone must not replay a key consumed by a menu, binding capture, or another scope.
Ensure one physical event creates one transition, including duplicate IMGUI passes
and character events, while allowing deliberate key repeat. Empty forms must not
claim traversal. Check first-pass behavior when no previous registration exists.

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

Keep any deferred request bound to its originating lifetime.
Discard it on close, tab replacement, or loss of ownership. Do not retain native IDs across passes.
Check that pending clipboard edits cannot migrate to the newly focused field.

### 4. Validate and document

Expand `mod/Tests/FieldFocusTests.cs` for forward/reverse wrap, empty/single fields,
unfocused entry, duplicates, reordered/removed fields, and independent owners.
Test event eligibility and one-transition semantics in a small pure policy helper
if extracted.
Pure tests alone cannot validate Unity focus behavior.

Run `make test-mod` and `make lint-mod`. The latter rebuilds the installed-source
assembly in Release. Follow the [build guide](../docs/src/build.md).

Runtime acceptance requires a separately requested in-game check. Verify:

- Settings and a shared multi-field dialog, forward and reverse, with immediate
  typing landing in the destination and no inserted tab characters.
- Test multiline, conditional, and disabled fields. Test nested scrolling in both directions.
  Test wrapping and first-entry behavior.
- Test Tab after mouse focus and while Tab repeats. Test traversal while a modal or menu opens and closes.
  Test traversal after category changes. Confirm that a late clipboard reply does not edit the new field.
- Confirm that keybinding capture records Tab. Confirm that function keys, Enter, and Escape keep their behavior.
- Confirm that the terminal sends Tab and Shift+Tab to the application with and without a split.

Record any runtime checks that you did not perform.
Ordering tests alone do not validate traversal. Once traversal is implemented and verified,
update `ui-focus.md` and the focused user keyboard documentation. Then remove this unresolved-work note.
