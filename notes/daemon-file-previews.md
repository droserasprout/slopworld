# File preview boundary

`files/preview.rs` owns bounded text/image reads and reader stamps.
`files/preview_scope.rs` owns containment for their optional `root` scope.
`api/handlers/files/preview.rs` owns request decoding and wire projection.
Preview and stat paths, including scope roots, are literal filesystem identities:
preserve whitespace and do not expand environment variables or home expressions.
A scoped preview cannot open a file outside its root, including through a symlink
or a concurrent symlink replacement.

`files/highlighting.rs` owns syntax previews, theme discovery, and bounded
highlighter execution. Its HTTP handler selects request-local or configured commands. Parsed arguments pass through styling and file substitution
without being converted back to command text.

File and syntax preview routes require root capability. With authentication enabled, this means the
root token. Omitting `root` allows unscoped text and image reads with that authority.
Directory browsing also requires root capability and does
not use the preview scope.

The [Files UI](mod-ui-files.md) owns captured preview roots and reader identity.
