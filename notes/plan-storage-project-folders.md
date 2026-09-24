# Group storage by project
Status: proposed

Settings → Storage currently shows agent state and shared caches in one flat list. Grouping those entries by project will make each project's retained state easier to find.

## Completion criteria

- The daemon includes the owning project name for active and recoverable agent state, using the existing optional `StoredState.project` field.
- Settings → Storage renders entries in foldable project groups. Entries without a known project appear in an "Other storage" group.
- The existing storage actions and total size summary continue to work across folded and open groups.
- The focused storage ownership note describes the daemon and UI grouping boundary.
- Run the relevant formatting and build checks in this worktree.

## Decisions

- Group state and cache entries by project name. The stored-state wire type already carries the project name for cache entries. No new protocol field is needed.
- Keep fold state for the lifetime of the Storage page.
- If saved session metadata identifies the project, put recoverable state in that project group. Otherwise, put it in "Other storage".
