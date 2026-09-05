# UI reuse and consistency plan

Status: implemented. Scope: `mod/Source/SlopWorld/UI/`.

The UI already has a useful shared foundation in `SlopTheme`, `SlopWidgets`,
`SlopWindow`, `DaemonConfigPage`, `MessageDialog`, `SlopListView`,
`ContentTreeView`, `RowActions`, and `RowChrome`. The next work should be
compositional and behavior-preserving. Do not introduce a large base class just
to make similar-looking screens share code.

## Guardrails

- Keep map-overlay rows and window-local rows on explicit `OverlayAware` and
  `Local` hover policies.
- Keep Markdown, terminal rendering, About, Tasks, Search, and the Files/Git
  domain wrappers specialized unless a shared contract becomes clear.
- Use `SlopTheme` geometry and colors instead of new literals.
- Public drawing helpers that change IMGUI state must use `WidgetState.Save()`.
- After each phase, compile the mod, run the existing checks, and run the strict
  duplication scan:

  ```text
  jscpd --min-lines 10 --min-tokens 80 --mode strict mod/Source/SlopWorld/UI
  ```

  The current UI baseline is 12 clone groups and 167 duplicated lines. A phase
  is complete only when behavior is unchanged, the scan does not regress, and
  the diff is smaller or clearer than the code it replaces.

## Phase 1: Share picker-window chrome

Targets:

- `UI/AppearancePage.cs` — cursor picker.
- `UI/UsagePage.cs` — usage-icon picker.

Both pages independently implement a centered and clamped `ImmediateWindow`,
title/close chrome, a fixed-cell grid, and a scroll view. Extract a small
`SlopPickerWindow` or equivalent helper that owns:

- popup placement and page-boundary clamping;
- title and close-button layout;
- grid width, row count, and scroll calculations;
- the shared cell size and icon inset tokens.

Leave choice lookup, selected-state rules, tooltips, and selection callbacks in
each caller. Verify that narrow and short pages still keep the popup on-screen,
and that both pickers retain local hover behavior and their existing window IDs.

## Phase 2: Share simple text-dialog layout

Targets:

- `UI/LabelDialog.cs`.
- `UI/GitCommitDialog.cs`.
- `UI/FileNameDialog.cs`.

Extract a narrow text-form dialog helper, not a domain-heavy dialog base. It
should support a title, optional explanatory text, one single-line field, one
error row, Cancel, and a primary action, with the field name and validation
remaining caller-owned. The label dialog may additionally expose its Remove
action.

Keep `DelegateTaskDialog` outside the first replacement because its recipient
selector and multiline body are materially different. Reuse only an error-row
or footer helper there if that reduces code without adding hooks.

Acceptance criteria: all three dialogs use the same spacing, footer geometry,
font/color scoping, Enter behavior, and disabled/error presentation; validation
messages and callbacks remain unchanged.

## Phase 3: Separate daemon request state from page layout

Targets:

- `UI/DaemonConfigPage.cs`.
- `UI/InstructionsPage.cs`.
- `UI/SandboxPage.cs`.
- `UI/StoragePage.cs`.

Introduce small state/service objects rather than another page superclass:

- a config state/client for load, parse, save, path, error, and post-save
  callbacks;
- a generic async-load state for waiting, error, reload, and empty results.

Use the config state in `InstructionsPage` while leaving its tabs and Markdown
preview local. Use the async-load state in `SandboxPage` and `StoragePage`.
Keep `ConfigWindow` on its own raw-TOML editor layout; share only the client if
its endpoint and payload contract match.

Acceptance criteria: retry, offline, save, success-message, and config-mirror
behavior are identical across existing pages, with no network calls from shared
rendering helpers.

## Phase 4: Consolidate option lists and interactive cells

Targets:

- `UI/PresetList.cs`.
- `UI/BreadcrumbList.cs`.
- ad hoc cells in `UsagePage`, `StoragePage`, `AppearancePage`, and
  `KeyBindingsPage`.

Extract a generic `SlopChoiceList<T>` for the shared framed list, inset, empty
state, scrollbar, row pitch, and checkbox rendering. Preserve preset dependency
and escape semantics, plus the special instructions entry in the breadcrumb
list, in adapters supplied by the callers.

Add small cell-level helpers where the existing primitives do not fit:

- `ToggleCell` for a checkbox-only column with tooltip and click sound;
- `IconPickerCell` for an icon preview with standard well, hover, hit testing,
  and tooltip behavior.

Use `RowChrome` for row painting and retain the explicit hover policy at every
call site. Either migrate callers to `RowChrome.Draw` or remove that currently
unused API after confirming no external caller depends on it.

## Phase 5: Finish scheme and IMGUI-state consistency

Audit the remaining custom drawing after the structural refactors.

- Decide whether `LibraryView` identity badge colors are intentionally fixed. If
  they should follow custom schemes, add named identity colors to `UIScheme` and
  expose them through `SlopTheme`; otherwise document the fixed-palette
  exception beside the scheme contract.
- Replace repeated `ArrowW`, inset, and `ContractedBy(4f)` literals with shared
  tokens where the value represents a common UI rule. Keep domain-specific tree
  indentation separate when it is intentional.
- Replace manual resets such as `Color.white`, `GameFont.Small`, and
  `TextAnchor.UpperLeft` with scoped `WidgetState.Save()` blocks in custom draw
  methods, especially `KeyBindingsPage` and tree/library renderers.
- Keep `TopBar` and sidebar map-pass rendering on their existing specialized
  input path; those controls do not have ordinary window-local hit-testing
  semantics.

Acceptance criteria: changing the active scheme updates every scheme-owned
  surface, nested drawing cannot leak font/anchor/color state, and map-overlay
  controls still receive input in the same layer.

## Phase 6: Extract only proven geometry helpers

After Phases 1–5, reassess repeated `PageBody`/`SmoothScroll`/`Listing_Standard`
hosts in `AppearancePage`, `TerminalPage`, `InstructionsPage`, and
`DaemonConfigPage`. If the remaining shapes are still materially identical,
extract a `SlopScrollBody` geometry helper. Do not create a page base class for
pages with different fixed previews, tab rails, or footer placement.

Reassessment: the shared `SlopScrollBody.View` geometry is extracted. The
scroll lifetimes and content measurement remain local because the four pages
have different previews, tabs, trailing fields, and footer contracts.

## Completion checklist

- All phases with clear call-site benefit are implemented or explicitly marked
  as intentionally separate.
- Strict duplication output is equal to or better than the baseline.
- Mod build and existing tests/checks pass.
- Visual checks cover light/dark/custom schemes, disabled controls, empty/error
  states, narrow windows, scroll boundaries, and map-overlay input.
- Any new shared helper has a short source comment describing its contract and
  hover/input policy.
