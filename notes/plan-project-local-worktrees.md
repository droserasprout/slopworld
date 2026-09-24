# Project-local worktrees

Status: human approved
Approved by: user (requested committing the main-tree changes)

Default managed checkouts belong in `<project_path>/.worktrees/<name>` and start on
a branch with the same name. Explicit custom roots retain their project-name subdivision.
Project renames must leave local checkout paths intact; worktree renames still repair Git registration.

Validate named and generated branches, rejection before allocation, local and custom-root
rename/removal, and cache isolation. Move existing host storage once through `systemd-run`,
preserving conflicting and damaged checkouts. Do not add migration code.
