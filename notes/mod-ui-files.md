# Files browser and previews

`FilesBrowser` coordinates navigation and tree input; `FilesStore` owns browse state
and requests. `FilesViewerController` owns native reader handoffs; `FilesActions`
shares path menus and mutations with Git and terminals. Files and Git reader menu
actions share type checks and viewer callbacks in `FilesActions`; callers own
directory/existence checks, and Git also permits editing non-text files. Shared trees live in
`UI/Browsing/`. User procedures belong to
[Files and search](../docs/src/workspace/files-and-search.md).

Browsing and native previews use daemon APIs. Pagers and editors run host commands
and read paths directly, without private agent state. Registered checkout identity
supplies the working directory and validates actions. Use native image previews for
PNG/JPEG, even with a line target. Captured preview roots require scoped native
readers, including when following Markdown links or switching to plain source; a
pager could reopen an unchecked replacement symlink.

Refresh preserves semantic selection and expansion. Reveal waits for current listings
and probes paths omitted by caps. Missing checkouts cannot select or reopen readers.
Rename/removal refresh both trees and can dismiss affected source readers. Files
reads Git's shared status cache for Diff availability without owning Git refresh.

Bounded listings must remain visibly partial. `TerminalPathProbe` checks omitted paths
before offering actions. Browsing applies ignore rules before the visible-entry cap.
Non-UTF-8 names fail rather than producing lossy identities. File-action substitution
scans only the original template, never inserted paths.

[Readers](mod-file-readers.md) owns preview/pin lifetime, [Markdown](mod-markdown.md)
owns scoped resources, and [daemon previews](daemon-file-access.md) owns containment.
