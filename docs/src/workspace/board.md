# Board

Open the chessboard beside Settings, click the chess table in the colony, or press **F10**. The chessboard also opens Board in SlopWeb before a colony is loaded. The picker switches between Root, projects, and archived projects. Type in **Search cards** to match titles and descriptions as you type, or choose an assignee beside the board picker. The two filters work together and reset when you switch boards.

## Organize work

Use **+ Column** to add a column. Use **+ Add card** at the foot of a column, type a title, and press Enter or **Add**. Open **···** to rename a column or remove an empty one. Drag a header to reorder columns. Click a card to open its details. Drag the card to an insertion line to reorder it or move it into another column. Hold near an edge to scroll. Escape or a release outside the destination cancels a drag.

On a narrow screen, swipe across columns or vertically through cards. Tap a card to open its popup and **×** to return. Use the visible grab handle to pick up a card or header on touchscreens. A mouse wheel scrolls the pointed column, and Shift+wheel moves across columns.

Edit the title or description in the popup and use **Save** beside the changed field. **Assigned to** immediately applies your choice of user, agent, worker, or **Unassigned**. The column dropdown moves the card within its board. Press Enter or **Post** to send a comment. Comments and activity share a newest-first conversation alongside the fields on wide screens and below them on narrow screens. Each entry shows its author, portrait or fallback icon, and recorded local time when available. Drag to select text across entries, double-click a word, or triple-click a line. Click an active agent’s name or portrait to open its terminal. Close the popup with **×** or Escape to return to the same board position. For a link, enter its kind and a target such as a project ID, branch name, worktree path, worker name, or URL. Use the **×** beside a link to detach it. Assignment records who owns the next step. Collaborators start workers and send messages separately. All columns, including Done, have the same behavior.

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
