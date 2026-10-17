# Search ownership

`SearchView` owns draft controls, selection, geometry, and reader actions.
Its underlined option labels toggle draft flags, all initially off. Selected
`gitignd` enables the submission's `IncludeIgnored` flag. `inc` and `exc` reveal
path-glob fields between the query and options. Disabling either retains its draft
text but omits it from submissions; exclusions take precedence over inclusions.
The daemon expands literal path filters to include directory descendants; a bare
name such as `notes` matches that directory at any depth. Explicit globs retain
ripgrep semantics.
Toggling either path filter submits the current draft when the query is non-empty.
Enter submits from any visible Search field. Selection accents
the label and underline without painting a background.
`SearchStore` owns submitted queries, bounded requests, and grouped results through
an injected daemon transport. It consumes snapshots of the
[shared scope catalog](mod-sidebar-navigation.md); each submission captures the
enabled ready checkouts by copying their descriptors, so delayed replies retain
the submitted identity and root even if the catalog changes. Replies for a replaced query or
scope cannot update current results.

Checkout filter changes rerun the last submitted query and options without replacing draft
controls. They release an unpinned Search preview but preserve pinned Search readers.
Search owns a separate pager; Files and Git share [FileReaders](mod-file-readers.md).
Closing the Search tab releases field focus and preserves its reader.

Results retain their captured root for reader actions. Project and checkout groups
keep identical relative paths distinct. Scope and history identity belong to
[sidebar navigation](mod-sidebar-navigation.md), daemon execution to the
[daemon file API](daemon-files.md), and user workflow to the
[Files and search guide](../docs/src/workspace/files-and-search.md).

Search reattaches its reader on entry and before appearance-restart discovery;
appearance restarts preserve Search intent.
