# Project boards

The board store owns the SQLite transaction for each operation. A card belongs to its column and reaches its board through that column. Comments, activity, assignment and links follow the card ID. The store records activity in the same transaction as the card edit. Cross-column moves capture both column names at move time. Reordering within a column produces no transition. Insertions resolve the destination anchor under the write lock. Project IDs identify boards. Changing the prefix rekeys every card transactionally while relations continue to use the internal card ID. Each real key change replaces the card’s single lookup alias with its outgoing key. Prefix reservations and issuance counters survive archive deletion. Card creation advances the counter in the same transaction as insertion. Moves stay within the owning board. Task links reference assignment IDs and the task API owns their details.

Accepted project configuration owns project membership. The daemon materializes boards before project removal and reconciles the archive flag at startup, on project publication and on board access. Saved agent permissions combine a board access level with a dynamic project selection and explicit board IDs. The daemon scopes reads, writes, mutation replies, and reverse task lookups to that selection. A session lease stabilizes project membership through board commit even if a client disconnects.

The board and task views share ConversationReader for portraits, timestamp formatting, wrapping, and selection. The shared feed selects card transitions from board activity and takes task progress from the visible mailbox. Comments and activity carry daemon-recorded timestamps. Older entries retain an unknown time.

BoardView polls only while open. Mutations invalidate earlier reads. Detail selection follows the card ID across snapshots. SlopWeb forwards pointer device and capture cancellation into the native gesture owner. The browser’s visible viewport sets responsive canvas height when a keyboard is present.

See the [Board guide](../docs/src/workspace/board.md) and [API contract](../docs/src/reference/api.md#project-boards).
