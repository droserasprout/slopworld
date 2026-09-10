# Step 4: Editable dialog identity and layout

Status: complete. Dependency: [scrollable listings](ui-refactor-02-listings.md).
[Shared validation](mod-refactoring-plan.md).

Agent, project and library editors duplicate create/edit/copy identity. Agent and project
editors also repeat tab rails, footer geometry and per-tab scroll/height state.

## Implementation slices

1. Add a small EditIdentity value object under `UI/Dialogs/`: mode, original name, copy
   source and title construction. Reuse NameTools for unique copy names. Preserve empty-name
   conventions used by existing save calls.
2. Migrate EditSessionDialog, EditLibraryItemDialog and the project editor in
   `UI/Views/Projects/ProjectsView.cs`. Keep cloning, validation, persistence, agent rename
   effects and private-state reset in each feature.
3. Extract TabbedFormLayout for rail/body/footer rectangles. Dialogs retain tab enums and
   dispatch. Reuse retained per-tab listing owners from step 2 where applicable.
4. Migrate agent then project tabs. Keep specialized preset, breadcrumb and preview scroll
   owners. Preserve cached heights and footer enablement across tab changes.
5. Share Cancel/Save placement only where geometry matches; keep action policy local.

## Validation and completion

Add pure tests for new/edit/copy identity and bounded rail/body/footer geometry in short and
narrow viewports. Reuse NameTools coverage for copy-name collisions. Review save arguments
and rename/reset branches. Run shared C# checks.

Done when identity bookkeeping and tab geometry each have one implementation while save
behavior stays feature-owned. Land identity before layout; update mod-ui-windows.md.
