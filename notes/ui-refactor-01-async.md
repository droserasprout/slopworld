# Step 1: Operation-aware async loads

Status: complete. Dependency: none. [Shared validation](mod-refactoring-plan.md).

AsyncLoadState accepts every callback. Search, Browse, Binaries, DaemonConfigState and Git
independently track generations. Centralize operation validity while keeping request
construction and aggregation feature-owned.

## Implementation slices

1. Inventory AsyncLoadState callers and their old-value policies on start, failure and reset.
   Add a pure operation gate beside `UI/Views/Shared/AsyncLoadState.cs` with Begin,
   IsCurrent and Invalidate. One token covers a logical operation, including chained requests.
2. Integrate the gate into AsyncLoadState. Guard value/error/loading mutations and the loaded
   callback. Support invalidation without starting another request. Preserve each caller's
   retained-versus-cleared value policy explicitly.
3. Migrate `UI/Dialogs/BrowseDialog.cs` and `UI/Settings/DaemonConfigState.cs` first.
   Closing Browse invalidates pending work. Preserve existing main-thread dispatch.
4. Migrate SearchView and BinariesPage. Clearing Search invalidates its operation; fan-out
   pending counts, partial errors, and binary resolution stay feature-owned.
5. Migrate GitView.State with one gate per repository. Status and counts share a token;
   failed counts must leave status usable. Audit Files' draining generations before adopting
   the helper there; do not mechanically replace its loading policy.

## Validation and completion

Add captured-callback tests for A then B with late A success/error, invalidation without a
replacement, stale completion side effects, multiple callbacks within one operation, and
old-value policies. Exercise Git's status/counts failure policy where pure logic permits.
Run shared C# checks per slice.

Done when migrated callers share token validity and stale results cannot alter their state.
Land the helper with simple callers, then fan-out callers. Endpoint semantics remain local.
