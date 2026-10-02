# Files and readers

`FilesStore` owns roots, browse requests and refresh. `FileReaders` owns the pager collection
shared with Git.
`FilesViewerController` controls native Markdown and JPG/PNG reader lifetimes.
`ContentTreeController` owns semantic selection/folds. Static `FilesView` methods are entry
points, not another state owner. All filesystem reads use daemon APIs. Viewers, editors and file actions run on the daemon
host without private agent state. The selected registered worktree validates action paths and supplies
the working directory. Host reader sessions are disposable, not saved host-shell tabs.

Roots use the shared browsing scope key. Relative paths identify selections and history.
Scope keys use daemon project IDs, or a `name:` key until a project has an ID. Assigning an ID
does not migrate saved choices from the name key.
Project and checkout folds are independent. A checkout rename relocates cached nodes without
forgetting expansion. Refresh merges by path/type to preserve expansion and selection. Bound concurrency so
background refresh cannot starve foreground opens. Use layout geometry for both hit tests
and scrolling. Loaded directories keep expandability from their own filtered listing; the
parent empty-directory check does not account for gitignored entries.
Clipped rows must never receive clicks outside their pane.

A viewer has one replaceable preview and independently pinned readers. Reopening the same
path, including a line-targeted open, reuses its reader even while hidden.
Concurrent opens share a pending request.
Tab changes preserve readers. Explicit dismissal and validated file removal may close a
pinned reader. Ordinary focus changes must not close one. While Files or Git is visible, serialized
`/api/files/stat` probes reconcile source readers independently of tree filters, folds and
listing caps. Failed probes preserve readers. The controller ignores replies for replaced readers.
After the first successful probe establishes a baseline, changed file stamps refresh source-file
pagers on the five-second polling cycle. A replacement starts before the old session stops;
only panes still showing that reader rebind, preserving pins, split selection, and focus.
Refresh failures retain the old reader for retry. Pager scroll/search state resets; line-targeted
opens retain their original launch line. Editors and Git diff commands are excluded.
The daemon's opaque stamp includes file identity, size, and modification/change timestamps;
empty stamps from older daemons leave previews unchanged.
Create/rename/remove refresh both trees, and rename/remove dismiss affected source readers.
Files supplies view/edit and Git supplies diffs without changing the active tree.
Both tabs show the same routed headers, upper scroll position and saved splitter fraction.
Pager acquisition releases an unpinned native preview.
Native preview creation releases the shared pager preview. Diff keys use a mode prefix so source and diff readers can coexist.

Supply terminal dimensions before pager startup: resizing during LESSOPEN can strand leading
padding in less. Preserve alternate-screen behavior for wheel routing.
Keep short files open.
`make test-pager` validates this without the game.

Markdown uses [native rendering](mod-markdown.md). JPG/PNG files use a native content
reader backed by the daemon's bounded image route. Native readers share one replaceable
preview and independently pinned headers; opening one releases the pager preview.
Links from Markdown retain their captured root and use bounded native readers for text and images.
They do not reuse an unscoped reader or launch a pager that could reopen a changed symlink.
The file icon tool bakes icons from the
vendored Material Icon Theme.
Filename matches take precedence over the longest extension match. Update the manifest and C# lookup together.
Action icons use the separate [shared bake](mod-icons.md).

Wheel bursts reuse the tree's measured extent before refresh, group or layout work. Refresh
and pending selection reveals resume on normal GUI passes. Routed reader headers use an
indexed visible-row range rather than scanning every open reader.

FilesView adds a “Choose an application” submenu. It lists associated apps and offers
“Choose another application.” The daemon opens GTK’s native chooser when its host has
Python 3, PyGObject, and GTK 3. The chooser lists all installed apps. Otherwise, the daemon
opens the desktop portal. The chooser runs outside the timed file action, so the user's wait
does not cause a timeout.

Reader identity retains its source scope independently of filter choices. Native Markdown
readers snapshot their root and origin label. Terminal readers carry the worktree ID in the
run request. Refreshing and folding never release pinned readers.
