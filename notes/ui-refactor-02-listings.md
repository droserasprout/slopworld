# Step 2: Scrollable listings

Status: complete. Dependency: none. [Shared validation](mod-refactoring-plan.md).

SettingsForm, DaemonConfigPage.DrawFieldsBody and InstructionsPage repeat scroll ownership,
frame-stable height measurement, Listing_Standard setup and cleanup.

## Implementation

1. Compare `UI/Settings/SettingsPageLayout.cs`, `DaemonConfigPage.cs`, and
   `InstructionsPage.cs`. Identify trailing-content and overlay boundaries.
2. Extract a retained ScrollableListing owner in shared chrome for SmoothScroll and measured
   height. Accept a listing callback and optional trailing-content callback returning the
   final content bottom. Keep footer, overlay, and load/save state outside it.
3. Preserve existing scrollbar reservation, font setup, frame-stable measurement,
   maxOneColumn, and the oversized listing rectangle. Pair listing.End, scroll scopes and
   WidgetState restoration on normal and exception paths.
4. Make SettingsForm a thin wrapper or remove it if it contributes no policy. Migrate daemon
   fields and Instructions to the same owner without recreating it per draw.
5. Preserve scroll and field identity across resizing, Instructions editing, and daemon
   trailing fields and overlays. Reuse the owner for compatible dialog tabs in step 4.

## Validation and completion

Extend SettingsLayout tests only if pure measurement behavior changes: frame-boundary height
updates and short-view bounds. Review cleanup paths directly; avoid Unity mocks.
Run shared C# checks.

Done when the three hosts use one listing lifecycle and retain independent data state.
Land extraction and migrations together; update mod-ui-chrome.md.
