# File mutation boundary

`files/mutations.rs` owns create, rename, and removal policy and filesystem effects.
`api/handlers/files/mutations.rs` decodes Files UI requests and maps results to HTTP.
Mutation paths reject `.` and `..` segments.
Browser paths are literal filesystem identities; environment and home expansion
belong to configuration inputs, not mutation requests.

- Rename never replaces an existing destination, including one created concurrently.
- Removal acts on a final symlink itself, preserving its target.

Read boundaries belong to [file previews](daemon-file-previews.md).
