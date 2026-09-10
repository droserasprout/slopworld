# Step 5: Sidebar tab handlers

Status: complete. Dependency: none. [Shared validation](mod-refactoring-plan.md).

CurrentTab switches in `Patches/AgentSidebar/` distribute tab behavior across drawing,
interaction, folding, refresh and serialization.

## Implementation slices

1. Inventory switches in AgentSidebar.Views, Rendering, Interaction, Chrome and Menus.
   Record each persisted name, capability, action and activation effect.
2. Add a SidebarTabDefinition registry beside SidebarTab. Store metadata and delegates for
   draw, click, refresh, folds, activation and closing; retain the static feature views.
3. Route parsing/serialization and capability queries through definitions. Preserve persisted
   strings and unknown-name fallback to Agents.
4. Route action dispatch through definitions. Keep Files/Git's routed tree drawing as an
   explicit callback or capability without forcing all tabs into a tree interface.
5. Centralize activation with distinct switch and reselection paths. Close menus before both.
   Preserve Git/Tasks refresh on reselection, Files.Entered on reselection, and Git.Entered
   when switching into Files to initialize missing status data.

## Validation and completion

Extract enough pure dispatch policy to test with recorded delegates. Cover persisted names,
unknown fallback, switching versus reselection, menu-close ordering, Files/Git initialization
and fold availability. Run shared C# checks.

Done when metadata and dispatch actions live in one definition per tab. Feature-specific
rendering conditionals may remain. Land metadata before dispatch; update mod-sidebar.md.

Implemented `SidebarTabDefinition`/`SidebarTabRegistry` with per-tab draw, click, action,
refresh, fold, filter, close and activation delegates. Activation keeps menu closure first,
separates switching from reselection, preserves Files/Git/Tasks refresh behavior, and keeps
the Files entry path initializing Git state. Pure tests cover names, fallback, capabilities
and dispatch ordering. `make test-mod` and `make lint-mod` pass.
