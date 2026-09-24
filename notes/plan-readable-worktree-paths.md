# Readable managed worktree paths

Status: human approved
Approved by: user (requested landing on `main`)
Revision: bfec8656

Managed checkouts currently use project and worktree UUIDs plus a `checkout` suffix. Paths are difficult to recognize in terminals, external editors, and devloop.

New managed checkouts should use `<managed-root>/<project-name>/<worktree-name>`. Project and worktree renames should move the checkout on disk, repair Git's linked-worktree registration, and update the durable catalog. Existing UUID paths should stay valid until explicitly renamed. Refuse a move while sessions or host terminals remain attached. Preserve checkout contents and Git state on failures.

Review should cover named creation, both rename routes, legacy migration, collision handling, and removal after a rename.
