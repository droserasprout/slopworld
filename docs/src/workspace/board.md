# Board

Open the chessboard beside Settings, click the chess table in the colony, or press **F10**. The chessboard also opens Board in SlopWeb before a colony is loaded. The picker switches between Root, projects, and archived projects. Type in **Search cards** to match keys, titles and descriptions as you type, or choose an assignee beside the board picker. The two filters work together and reset when you switch boards.

## Organize work

Use **+ Column** to add a column. Use **+ Add card** at the foot of a column, type a title, and press Enter. Open **···** to rename a column or remove an empty one. Drag a header to reorder columns. Click a card to open its details. Drag the card to an insertion line to reorder it or move it into another column. Hold near an edge to scroll. Escape or a release outside the destination cancels a drag.

On a narrow screen, swipe across columns or vertically through cards. Tap a card to open its popup and **×** to return. Use the visible grab handle to pick up a card or header on touchscreens. A mouse wheel scrolls the pointed column, and Shift+wheel moves across columns.

Card details appear beside **Comments and activity**, or above them on narrow screens. Press Enter or **Post** to send a comment. Drag to select text, double-click a word, or triple-click a line. Click an active agent’s name or portrait to open its terminal. Select **Show activity** to see edits and expand a repeated edit to inspect or remove an individual event. **Remove comment…** lets you choose a comment by its author, text, and entry ID.

Under **Tasks**, you can attach an assignment by ID, spawn a worker, or delegate to an agent. Select an assignment to read its history and result. Back returns to the card. From **Task: Open**, use **Open card** to follow a link in the other direction. If several cards link to the same task, choose one from the picker.

Edit the title or description and use **Save** beside the changed field. **Assigned to** applies your choice of user, agent, worker, or **Unassigned**. To attach another resource, enter its kind and a target such as a branch name, worktree path, or URL. Use **×** beside a link to detach it. The column dropdown moves the card within its board. Moving a card to Done leaves the linked assignments under their workers’ control. Close the popup with **×** or Escape to return to the same board position.

## Work with an agent

A human can add a Review column and create a card. The agent can then run the following commands, substituting the IDs from the first two replies.

```sh
slopctl board list
slopctl board show BOARD
slopctl board assign BOARD CARD
slopctl board link BOARD CARD worktree /path/to/checkout
slopctl board set-prefix BOARD SW
slopctl board link BOARD CARD task TASK_ID
slopctl task delegate --card SW-42 AGENT review the change
slopctl worker spawn --card SW-42 --project PROJECT --template TEMPLATE review the change
slopctl board comment BOARD CARD "Ready for review"
```

New project boards use the project name as their key prefix. Spaces and unsupported characters become separators, and a reserved name gets a numeric suffix. Root uses `root`. Click the current prefix beside **+ Column** to edit it in place. Enter applies it to every card on the board, keeping each card’s number. Escape cancels. The previous key still opens the card until the next prefix change. The internal card ID always works. The agent’s **Permissions** template defaults to **Write** on **Agent’s project**. Under **Visible boards**, check Root or individual projects to grant access to those boards too. The agent’s project checkbox follows the agent. Named selections follow their boards across renames and archival. An agent without a project sees only explicitly selected boards.

**Off** disables board access and preserves the selections. **Read** allows browsing. **Write** adds card and column edits, comments, and task linking. **Admin** also allows deleting cards, columns and discussion entries, changing key prefixes, and deleting archives. Task visibility, delegation, and worker spawning use their existing permissions.

The open board refreshes automatically. Read the comment and drag the card into Review. An API client can read the committed order and card details with a single GET. See the [API contract](../reference/api.md#project-boards).

## Move and archive

Move cards between columns on the same board. Each card retains its public key and internal ID. Removing a project archives its board for browsing. **Delete archive…** asks for separate confirmation and permanently deletes the archived board and any cards it still holds.

The daemon stores active and archived boards in one SQLite database at
`<DATA>/boards.sqlite3`. For a consistent online backup, use the SQLite backup API.
