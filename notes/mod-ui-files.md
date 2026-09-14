# Files and readers

`FilesStore` owns roots, browse requests and refresh; `FilesViewerController` owns pager and
Markdown lifetimes. `ContentTreeController` owns semantic selection/folds. Static `FilesView`
methods are entry points, not another state owner. All filesystem reads use daemon APIs.

Refresh merges by path/type to preserve expansion and selection. Bound concurrency so
background refresh cannot starve foreground opens. Use layout geometry for both hit tests
and scrolling; clipped rows must never catch clicks outside their pane.

A viewer has one replaceable preview and independently pinned readers. Reopening the same
path reuses its reader, including while hidden; concurrent opens share a pending request.
Tab changes preserve readers. Explicit dismissal may close a pinned reader; ordinary focus
changes may not. Files owns view/edit, Git owns diffs, even when launched from Files.

Supply terminal dimensions before pager startup: resizing during LESSOPEN can strand leading
padding in less. Keep alternate-screen behavior for wheel routing and short files open;
`make test-pager` exercises this without the game.

Markdown uses [native rendering](mod-markdown.md). File icons are baked from the vendored
Material Icon Theme; filename precedes longest extension. Manifest and C# lookup are maintained
together. Action icons use the separate [shared bake](mod-icons.md).
