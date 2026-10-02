# Search ownership

`SearchView` owns the sidebar search view and submits requests to the daemon file
API. It consumes the [shared scope catalog](mod-sidebar-navigation.md); each
submission captures the enabled ready checkouts. Replies for a replaced query or
scope cannot update current results.

Filter changes rerun the last submitted query and options without replacing draft
controls. They release an unpinned Search preview but preserve pinned Search readers.
Search owns a separate pager; Files and Git share [FileReaders](mod-file-readers.md).
Closing the Search tab releases field focus and preserves its reader.

Results retain their captured root for reader actions. Project and checkout groups
keep identical relative paths distinct. Scope and history identity belong to
[sidebar navigation](mod-sidebar-navigation.md), daemon execution to the
[daemon file API](daemon-files.md), and user workflow to the
[interface guide](../docs/src/tour/interface.md).

Search reattaches its reader on entry and before appearance-restart discovery;
appearance restarts preserve Search intent.
