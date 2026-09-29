# File mutation boundary

`api/handlers/files.rs` owns create, rename, and removal requests from the Files UI.
It rejects `.` and `..` path segments before any mutation. A final symlink remains
a symlink for removal; do not resolve it into its target.

Rename uses Linux `renameat2` with `RENAME_NOREPLACE`. The earlier existence check
only improves the error message; it cannot protect a destination created concurrently.
