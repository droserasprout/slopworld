# File preview boundary

`api/handlers/files/preview.rs` owns text and image preview requests and reader stamps.
`api/handlers/preview_scope.rs` owns containment for their optional `root` scope.
A scoped preview cannot open a file outside its root, including through a symlink
or a concurrent symlink replacement.

`api/handlers/highlighting.rs` owns syntax previews, theme discovery, and bounded
highlighter execution. Parsed arguments pass through styling and file substitution
without being converted back to command text.

File and syntax preview routes require root capability. With authentication enabled, this means the
root token. Omitting `root` allows unscoped text and image reads with that authority.
Directory browsing also requires root capability and does
not use the preview scope.

The [Files UI](mod-ui-files.md) owns captured preview roots and reader identity.
