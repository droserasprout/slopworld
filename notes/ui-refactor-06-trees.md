# Step 6: Content tree state and capabilities

Status: complete. Dependencies: [async operations](ui-refactor-01-async.md) and
[sidebar handlers](ui-refactor-05-sidebar.md). [Shared validation](mod-refactoring-plan.md).

ContentTreeSource has many optional no-op methods. Files and Git repeat collapsed groups,
revisions, selection/viewer plumbing and forwarding.

## Implementation slices

1. Compare `UI/Views/Shared/ContentTreeView.cs`, FilesView*.cs and GitView*.cs.
   Identify shared state and the existing owner of each invalidation.
2. Extract pure state for collapsed group keys, revision changes and fold-all policy.
   Preserve group identity and selection keys across refresh.
3. Add a ContentTreeController for shared group construction and forwarding only where the
   policies match. Keep lazy directory loading in Files and repository status/counts in Git.
4. Separate the required source contract from optional loading, row actions, group bodies
   and selection capabilities. Add only capabilities supported by real callers, migrate both
   adapters, then remove replaced no-op methods.
5. Preserve the measured tree index, visible-row rendering, scroll-event shortcut and routed
   session rows. Keep viewer storage feature-owned until step 8.

## Validation and completion

Add pure state tests for folding, revision invalidation, selection identity after refresh and
group removal. Reuse tree/index coverage where available. Review asynchronous lazy loading
and partial Git counts. Run shared C# checks per slice.

Done when common tree state has one owner and adapters expose supported capabilities.
Land state before contract narrowing; update mod-ui-files.md and mod-ui-git.md.

Implemented `ContentTreeState` and `ContentTreeController` for semantic selection keys,
collapsed group keys, group pruning, fold-all policy and revisions. Files and Git now share
that controller while keeping lazy browse, repository status/counts, node folds and viewer
storage in their adapters. `ContentTreeSource` now has a required structural contract;
loading, row actions, group extras and double-click selection are explicit capabilities.
Pure state tests cover folding, invalidation, selection retention and removed groups.
`make test-mod` and `make lint-mod` pass.
