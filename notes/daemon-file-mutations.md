# File mutation boundary

`api/handlers/files.rs` owns create, rename, and removal requests from the Files UI.
Mutation paths reject `.` and `..` segments.

- Rename never replaces an existing destination, including one created concurrently.
- Removal acts on a final symlink itself, preserving its target.

Read boundaries belong to [file previews](daemon-file-previews.md).
