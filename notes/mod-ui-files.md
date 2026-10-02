# Files browser and previews

`FilesView` is the facade; `FilesBrowser` coordinates navigation and tree draw/input.
`FilesStore` owns roots, directory focus, browse requests, and refresh.
`FilesViewerController` owns native reader tabs, preview handoffs, and probes.
`FilesActions` owns path menus and mutations, shared with Git and terminal file menus.
Shared tree ownership lives in `UI/Browsing/`.

Browsing, stat, and native text/image previews use daemon APIs. Source pagers and
editors run as host commands and read paths directly, without private agent state.
The selected registered checkout validates action paths and supplies the working
directory. Scope and history identity belong to [navigation](mod-sidebar-navigation.md).

Refresh preserves semantic selection and expansion. Reveal waits for active listings
and probes exact paths when a capped listing omits them; missing checkouts cannot
select or reopen a reader. Mutation actions refresh both trees and rename/removal
can dismiss affected source readers.

Native Markdown/image policy belongs to [Markdown](mod-markdown.md), shared preview
and pinned-reader lifetime to [readers](mod-file-readers.md), diff dispatch to
[Git](mod-ui-git.md), artwork to [icons](mod-icons.md), and pane geometry to
[sidebar](mod-sidebar.md). Application-picker usage belongs to
[keyboard/menu reference](../docs/src/reference/keyboard-shortcuts.md).

Files consumes Git’s shared status cache for Diff availability.

Bounded filesystem results remain partial: clients must expose truncation rather
than imply that a capped listing includes every path.

Markdown links retain their captured root and use bounded scoped native text/image
readers, including non-Markdown text. They do not reuse unscoped readers or launch
a pager that could reopen an unchecked replacement symlink.
