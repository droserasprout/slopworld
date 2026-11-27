# Board

Open the chessboard beside Settings, click the chess table in the colony, or press **F10**. The chessboard also opens Board in SlopWeb before a colony is loaded. The picker switches between Root, projects, and archived projects. Each board has its own columns and cards.

## Organize work

Use **+ Column** to add a column. Open **···** on a column to add a card, rename the column, or remove an empty column. Drag a header to reorder columns. Click a card to open its details. Drag the card to an insertion line to reorder it or move it into another column. Hold near an edge to scroll. Escape or a release outside the destination cancels a drag.

On a narrow screen, swipe across columns or vertically through cards. Tap a card to open its details and **Back** to return. Use the visible grab handle to pick up a card or header on touchscreens. A mouse wheel scrolls the pointed column, and Shift+wheel moves across columns.

Card details include the description, one assignee, comments, and resource links. For a link, enter its kind and a target such as a project ID, branch name, worktree path, worker name, or URL. Assignment records who owns the next step. Collaborators start workers and send messages separately. All columns, including Done, have the same behavior.

## Work with an agent

A human can add a Review column and create a card. The agent can then run the following commands, substituting the IDs from the first two replies.

```sh
slopctl board list
slopctl board show BOARD
slopctl board assign BOARD CARD
slopctl board link BOARD CARD worktree /path/to/checkout
slopctl board comment BOARD CARD "Ready for review"
```

The open board refreshes automatically. Read the comment and drag the card into Review. An API client can read the committed order and card details with a single GET. See the [API contract](../reference/api.md#project-boards).

## Move and archive

In card details, **Move to board…** selects a destination board and then a column. The card keeps its ID, comments, links, and assignment. Removing a project archives its board. Use the picker to browse an archive and move cards out. **Delete archive…** asks for separate confirmation and permanently deletes the archived board and any cards it still holds.

The daemon stores active and archived boards in one SQLite database at
`<DATA>/boards.sqlite3`. For a consistent online backup, use the SQLite backup API.
