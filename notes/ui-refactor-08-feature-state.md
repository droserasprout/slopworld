# Step 8: Files/Git feature ownership

Status: complete. Dependency: [tree state](ui-refactor-06-trees.md).
[Shared validation](mod-refactoring-plan.md).

FilesView and GitView static partials couple loading, selection, viewer lifecycle, menus,
commands and rendering. Separate ownership after common tree state is extracted.

## Implementation slices

1. Map mutable fields and callers in both features. Identify application, project/repository,
   refresh and viewer lifetimes, including actual close/reset paths.
2. Move directory/cache/request state into a Files store and repository/status/count state
   into a Git store. Reuse operation validity from step 1 without unifying the node models.
3. Extract viewer controllers for selection/open/close state and action modules with explicit
   store/controller inputs. Keep endpoint construction in feature services.
4. Make rendering consume those owners. Retain thin static FilesView/GitView facades for
   sidebar compatibility during migration.
5. Assign subscriptions and pending operations an explicit owner. Invalidate at existing
   lifetime boundaries; switching tabs must not newly discard caches or selection.
6. Remove obsolete fields and forwarding after all callers migrate.

## Validation and completion

Add game-free lifecycle cases where production logic can be isolated: late results after
reset, selection across refresh, viewer close/reopen and status surviving count failure.
Run shared C# checks after each feature migration.

Done when mutable state has identifiable owners and rendering no longer coordinates loads.
A static compatibility facade is acceptable. Land Files and Git separately; update
mod-ui-files.md and mod-ui-git.md. Terminal-service restructuring is outside scope.

Implemented `FilesStore` for directory roots, focused-root state, browse queue/in-flight
counts and refresh lifetime, plus `FilesViewerController` for pager and Markdown preview
state. `GitStore` now owns repository snapshots and `GitViewerController` owns diff pager
state. Static Files/Git entry points remain compatibility facades; tree rendering consumes
the shared controller and feature-owned state while lazy browse and Git status/count work
remain separate. Existing async, tree-selection and pager lifecycle tests cover reset,
refresh, close/reopen and late-result behavior. `make test-mod` and `make lint-mod` pass.
