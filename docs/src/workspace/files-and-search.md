# Files and search

Use **Files** to browse a checkout and **Search** to find text across checkouts.
Both use the shared **Project** filter. Enable the Main checkout or worktrees you
want to inspect; see [Browse several checkouts](project-worktrees.md#browse-several-checkouts).
Changing this filter does not move an agent to another checkout.

## Browse and open files

1. Open **Files**, expand a project and checkout, then expand its folders.
2. Right-click a file and choose **View** to read it or **Edit** to open a text editor.
3. For Markdown, use **View** for the rendered reader or **View in pager** for source.

Images open in the native reader. Text pagers and editors run on the daemon host;
they do not use an agent's private state. Configure reader tools under
[Settings > Appearance > Code](../customization/settings.md#applying-changes).

Where desktop integration is available, **Open in** lists associated applications.
Choose **Other** to select another application. See
[Keyboard and mouse](../reference/keyboard-shortcuts.md) for reader navigation.

Rendered Markdown can display local resources within the project scope, but
does not load remote images or images embedded as data URLs.

## Preview tabs

Opening a file uses a replaceable preview. Double-click its sidebar header to
pin it before opening another file; middle-click the header to close it.
Files and Git share readers, while Search has its own. Source readers refresh
when file changes are detected, including pinned readers. For updated diffs,
see [Change review](review-changes.md#change-review).

## Manage files

A folder's context menu offers **New file**, **New folder**, and **Shell here**.
File and folder menus offer **Rename**, **Remove**, and path-copying actions.
These actions affect the selected checkout. Open **Git** afterward to review
changes to tracked files.

For reusable commands on a selected path, create a
[Library file action](library.md#file-actions).

## Search workspace text

1. Enable the checkouts you want in the Project filter and open **Search**.
2. Enter a query. Select **Case** for case-sensitive matching, **Word** for whole
   words, **Regex** for a regular expression, or **Include ignored** to search
   files ignored by Git.
3. Press Enter in the query field or click **›** to submit.
4. Expand the project, checkout, and file groups, then select a match to read it.
   Use its context menu to edit the file.

Editing the query does not search until you submit it. Changing the checkout
filter reruns the last submitted query and options. The groups distinguish files
with the same relative path in different checkouts.

## Incomplete results

Directory listings and search results are bounded. If the view reports truncation,
the visible entries are only part of the result. Narrow the search query or select
fewer checkouts. Use a [host shell](../terminals/host-shells.md) when you need a complete
listing or a more specialized search.
