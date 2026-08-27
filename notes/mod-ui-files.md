# `FilesView` and its icons

`FilesView` is one of the sidebar's two project trees, alongside [Git](mod-ui-git.md).
It is drawn in the sidebar back pass and can overlay a pane. The daemon owns all
filesystem access because sessions have private mount namespaces; the mod uses
`/api/browse` and `/api/files`. A selected daemon-resolved private-state directory
can temporarily become the tree root.

- `Kids == null` means not fetched. Fetches begin in the draw pass; errors stop retries
  until the directory is reopened. While Files is visible, loaded directories are reread every
  two seconds and entries are merged by name/type, preserving expanded branches.
- `Lines` is the post-layout hit-test table. `Screen` applies scroll offset and omits
  offscreen rows; do not hit-test against drawing-time geometry.
- Empty directories remain right-clickable rows. Hover exposes view/edit/diff actions;
  diff is offered only for paths already present in Git's working-tree result.
- Context menus support copy paths, MIME-associated host applications for files and
  directories, the desktop portal's `Other...` chooser, `less -R`, `micro`,
  rename/remove, new file/folder and terminal here. Root-only Files mutations use create,
  one-component rename and recursive delete. View/edit and file actions run through the
  project sandbox; storage roots use disposable host errands.
- Project headings also offer a host terminal. `fa` shortcuts show bounded output in the
  SlopWorld alert window (vanilla message toasts are hidden) or open a temporary project
  terminal; path markers are quoted and normalized by the daemon.

## Viewer

A Markdown-file click opens a native `MarkdownPreview` in the body; the daemon supplies
bounded UTF-8 text through `/api/read`, and Markdig provides the CommonMark/GFM parse tree.
Local HTML `<img>` tags resolve relative to the Markdown file through the bounded `/api/image`
route and support width/height plus right or center alignment.
Other text files still start or reopen one tracked `less -R --` pager above the tree.
Binary extensions are excluded. Markdown's context menu exposes `View in pager` for the raw
source when needed.
Changing file, directory, project, view, terminal or pane closes the reader; Files owns
view/edit sessions, while Git owns diffs opened from here.

## Icons

File icons are one PNG per slot, baked from the vendored MIT Material Icon Theme by
`tools/fileicons.py`. Lookup is filename, then longest matching extension, then a
generic page. The manifest and lookup table are maintained by hand; no atlas is used
because mipmapping can bleed between cells. Sidebar action icons come from the shared
[icon bake](mod-icons.md); the agents tab is a chip, not a robot faceplate.
