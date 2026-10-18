# Search ownership

`SearchView` owns draft controls, selection, geometry, and reader actions.
Search uses the shared sidebar action row for fold/unfold-all and does not use Files visibility settings.
It includes dotfiles; its own `gitignd`, include, and exclude options control filtering.
Its underlined option labels toggle draft flags, all initially off. Selected
`gitignd` enables the submission's `IncludeIgnored` flag. `inc` and `exc` reveal
path-glob fields below the options strip. Disabling either retains its draft
text but omits it from submissions; exclusions take precedence over inclusions.
The daemon expands literal path filters to include directory descendants; a bare
name such as `notes` matches that directory at any depth. Explicit globs retain
ripgrep semantics.
Include filters apply to ripgrep's results before the result limit; they do not
override Git ignores or hidden-file policy. Exclusions still prune ripgrep's walk.
Toggling any option submits the current draft when the query is non-empty.
Enter submits from any visible Search field. Selection accents
the label and underline without painting a background.
`SearchStore` owns submitted queries, bounded requests, and grouped results through
an injected daemon transport. It consumes snapshots of the
[shared scope catalog](mod-sidebar-navigation.md); each submission captures the
enabled ready checkouts by copying their descriptors, so delayed replies retain
the submitted identity and root even if the catalog changes. Replies for a replaced query or
scope cannot update current results.
The daemon returns at most 1,000 matching lines per checkout and marks incomplete
results as truncated. Search draws only visible rows while retaining the result set.

Checkout filter changes rerun the last submitted query and options without replacing draft
controls. Search submissions, option changes, and checkout filter changes preserve the
open Search reader, including an unpinned preview.
Search owns a separate pager; Files and Git share [FileReaders](mod-file-readers.md).
Closing the Search tab releases field focus and preserves its reader.

Results retain their captured root for reader actions. Project and checkout groups
keep identical relative paths distinct. Scope and history identity belong to
[sidebar navigation](mod-sidebar-navigation.md), daemon execution to the
[daemon file API](daemon-files.md), and user workflow to the
[Files and search guide](../docs/src/workspace/files-and-search.md).

`SearchResultTree` owns the visible project, checkout, file, and match hierarchy.
Each heading folds independently using captured project/checkout IDs and relative
file paths. Fold state survives query replacement and scope refreshes; settled
results prune missing keys. Folding preserves the Search reader. Revealing a search
location opens its ancestors. Fold state is separate from Files and Git.
File headings use the match context menu, targeting the file's first match even
while folded. Project and checkout headings share the other trees' grey styling.

Search reattaches its reader on entry and before appearance-restart discovery;
appearance restarts preserve Search intent.
