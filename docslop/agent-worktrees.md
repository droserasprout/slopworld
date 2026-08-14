# Agent worktree mode (proposed)

Worktree mode is a project workspace policy: the project remains the source repository, while each agent gets a Git worktree as its working directory. The worktree is created once from the exact `HEAD` commit at agent creation and survives agent restarts.

The first version stays deliberately small:

- Existing `shared` workspaces remain the default; worktree projects require a Git repository.
- There is no automatic sync. A worktree is a snapshot; users merge or rebase from the agent terminal.
- There is no blanket `git worktree prune`. SlopWorld only audits worktrees it created.
- A missing worktree is broken until the user explicitly recreates it. Clean worktrees may be removed; dirty ones require an explicit discard or keep decision.

This isolates source files and indexes, not Git metadata: a linked worktree still needs access to the repository's shared `.git` data. Full repository isolation would be a clone-sized follow-up, not part of worktree mode.
