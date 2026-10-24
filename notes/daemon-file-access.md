# Host file access

`files/mutations.rs` owns create/rename/removal policy and effects;
`files/preview.rs` owns bounded text/image reads and reader stamps;
`files/preview_scope.rs` owns scoped containment. API handlers decode requests and
project results. These operations use literal filesystem identities: preserve
whitespace and do not expand environment variables or home expressions.

Mutation paths reject `.` and `..` segments. Rename must not replace a destination,
even one created concurrently. Removal acts on a final symlink, preserving its target.
A scoped preview cannot escape its root through a symlink or concurrent replacement.

`files/highlighting.rs` owns syntax previews and bounded highlighter execution.
Parsed arguments retain their structure through styling/path substitution rather than
being rebuilt as command text. The HTTP handler selects configured or request-local
commands.

`process.rs` owns a separate process group for bounded host commands. Timeout,
I/O failure, and request cancellation terminate that group, including child commands;
commands that deliberately create a new session or process group escape this boundary.

Previews, highlighting, and directory browsing require root capability. Omitting a
preview root permits unscoped reads; browsing does not use preview scopes.
[Files UI](mod-ui-files.md) owns captured roots and reader identity.
