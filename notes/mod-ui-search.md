# Search

`SearchView` owns drafts, selection, and reader actions; `SearchStore` owns submitted
queries and bounded requests. Submission captures ready checkout descriptors from
[browse scopes](mod-ui-browse-scopes.md). Delayed replies cannot cross query or scope
identity. Actions retain captured roots so same-named paths in different checkouts
stay distinct.

Search filters are independent of Files visibility. Disabled fields retain drafts
but are omitted from requests. Exclusions win; literal directories expand to descendants,
while globs retain ripgrep semantics. Includes apply before the cap without bypassing
ignore/hidden rules. The daemon ignores personal ripgrep configuration. Truncated
results must remain visibly incomplete.

Scope changes rerun submitted options without replacing drafts. They preserve readers,
including unpinned previews. Search has its own [reader collection](mod-file-readers.md);
leaving the tab releases field focus but keeps readers. Reentry and appearance changes
reattach them without changing intent.

Result folds use captured checkout IDs/relative paths. Preserve folds through query
changes, pruning only after results settle. Revealing opens ancestors; folding cannot
close readers. Execution belongs to `files/search.rs`; procedures to
[Files and search](../docs/src/workspace/files-and-search.md).
