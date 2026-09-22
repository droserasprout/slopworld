# Sidebar navigation

`SidebarTabRegistry` is the ordered definition table; `SidebarViewHistory` stores semantic
back/forward targets. Persist stable tab IDs, not enum positions or reconstructed row objects.
Unknown tabs fall back to Agents; stale history targets may select a tab without a row.
Library targets distinguish templates from ordinary entries even when names match.
Library selection resolves catalog identity after refresh; row clicks select, while details
own execution/editing. Its list viewport excludes search and details for both scrolling and
hit testing. Global Library definitions remain visible under project filters. The Library
action strip provides quick access to Projects, Worktrees, Sandbox presets and App presets.
Project and worktree choices follow the shared project filter; project creation is also
available there.

Tab changes close menus and view-local input state, but preserve preview readers. Reselection
refreshes without repeating close/enter lifetime changes. Files also primes Git's shared
change lookup. Reader ownership: [Files](mod-ui-files.md), [Git](mod-ui-git.md),
[Search](mod-ui-search.md).

An empty project-filter set means all, while unknown saved keys mean no match. Filtering is
part of keyboard order as well as drawing: excluded agents must not reappear in cycling.

Vanilla main buttons, inspect panes, gizmos and colonist hit tests use different coordinate
paths. `ChromeShift` and the colonist-bar patches must all consume the workspace inset;
changing the drawn sidebar alone leaves invisible hit targets in old positions.
