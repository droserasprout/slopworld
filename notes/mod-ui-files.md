# `FilesView` and its icons

`FilesView` is one of the sidebar's two project trees, alongside [Git](mod-ui-git.md).
It is drawn in the sidebar back pass and can overlay a pane. The daemon owns all
filesystem access because sessions have private mount namespaces; the mod uses
`/api/browse` and `/api/files`. A selected daemon-resolved private-state directory
can temporarily become the tree root.

- `Children == null` means not fetched. The browse reply also identifies returned directories that
  have no children, so an unopened empty directory does not get a disclosure arrow. Fetches begin in the
  draw pass; errors stop retries until the directory is reopened. While Files is visible,
  loaded directories in open branches are reread every two seconds and entries are merged by name/type,
  preserving expanded branches.
  Selecting Files also refreshes expanded paths immediately, even when already selected;
  a pending browse defers that refresh until it drains without consuming the polling deadline.
  Browse requests are limited to four at once; folding or manually opening a directory drops
  queued background work so the foreground path stays responsive.
- When gitignored entries are shown by the filter, the Files tree dims their icons and labels
  while leaving their row behavior unchanged.
- `Lines` is the post-layout hit-test table. `ContentTreeView` keeps the full height but paints
  only rows near the viewport, while `Screen` applies scroll offset and omits offscreen rows;
  do not hit-test against drawing-time geometry.
- `ContentTreeController` owns Files' project-heading folds, semantic selection key, group
  pruning and tree revision. A refresh can replace nodes without losing the selected path or
  surviving project folds; filtering and temporary storage roots preserve project fold state.
  Lazy loading, node expansion, browse invalidation and viewer storage remain Files-owned.
- `FilesStore` is the owner of roots, focused storage state, browse requests and refresh
  scheduling. `FilesViewerController` owns the replaceable/pinned pager set and Markdown
  preview records; the static `FilesView` methods are compatibility entry points for the
  sidebar and file-action callers.
- Empty directories remain right-clickable rows. Hover exposes view/edit/diff actions;
  diff is offered only for paths already present in Git's working-tree result.
- Context menus support copy paths, MIME-associated host applications for files and
  directories, the desktop portal's `Other...` chooser, `less -R`, `micro`,
  rename/remove, new file/folder and terminal here. Root-only Files mutations use create,
  one-component rename and recursive delete. View/edit and file actions run through the
  project sandbox; storage roots use disposable host errands.
- Project context menus also offer a host terminal. `fa` library items can show bounded output in the
  SlopWorld alert window (vanilla message toasts are hidden), open a temporary project terminal,
  run silently, or retain the per-invocation choice; the mode is set in the Library item editor.
  Completed captured actions refresh both sidebar trees; terminal actions refresh both when launched. Path
  markers are quoted and normalized by the daemon.

## Viewer

Preview errands receive the measured terminal columns/rows before process startup.
Resizing while LESSOPEN starts can strand leading `~` rows in less; `-c` also paints
short files from the top. Alternate-screen mode remains enabled for wheel routing.
`make test-pager` checks short-file wheel input followed by a full-height long preview
using an isolated tmux server, without running the game.

A Markdown-file click opens a native `MarkdownPreview` in the body; the daemon supplies
bounded UTF-8 text through `/api/read`, and Markdig provides the CommonMark/GFM parse tree.
Local HTML `<img>` tags resolve relative to the Markdown file through the bounded `/api/image`
route and support width/height plus right or center alignment.
Other text files use a replaceable `less -Rc --` preview pager above the tree; binary extensions
are excluded. The preview header is italic until its routed row or the previewed file row is
double-clicked, which pins that pager like an edit session. Opening another file replaces only the
unlocked preview; sidebar tab switches preserve readers. Clicking an open file reuses its
preview or pinned session regardless of tree selection. Pending clicks share one startup request.
Adding or removing routed headers adjusts tree scroll to preserve row screen positions,
within the available scroll range. The top bar shows the actual session name followed by
the project-relative file path (or an absolute path for storage readers). Markdown previews
are reused by project/path even when hidden or when tree selection changes. Markdown's context
menu exposes `View in pager` for the raw source when needed, and native Markdown previews use
the same preview/pin behavior.

## Icons

File icons are one PNG per slot, baked from the vendored MIT Material Icon Theme by
`tools/fileicons.py`. Lookup is filename, then longest matching extension, then a
generic page. The manifest and lookup table are maintained by hand; no atlas is used
because mipmapping can bleed between cells. Sidebar action icons come from the shared
[icon bake](mod-icons.md); the agents tab is a chip, not a robot faceplate.
