# File preview boundary

`api/handlers/files.rs` owns text and image preview requests.
`api/handlers/preview_scope.rs` owns containment for their optional `root` scope.
A scoped preview cannot open a file outside its root, including through a symlink
or a concurrent symlink replacement.

These routes require root capability. With authentication enabled, this means the
root token. Omitting `root` allows unscoped text and image reads with that authority.
Directory browsing also requires root capability and does
not use the preview scope.

The [Files UI](mod-ui-files.md) owns captured preview roots and reader identity.
