# Project boards

The board store owns the SQLite transaction for each operation. A card belongs to its column and reaches its board through that column. Comments, activity, assignment and links follow the card ID. The store records activity in the same transaction as the card edit. Insertions resolve the destination anchor under the write lock. Project IDs also identify boards and stay reserved while an archive exists.

Accepted project configuration owns project membership. The daemon materializes boards before project removal and reconciles the archive flag at startup, on project publication and on board access. A session lease stabilizes project membership through board commit even if a client disconnects.

The board and task views share ConversationReader for portraits, timestamp formatting, wrapping, and selection. Comments and activity carry daemon-recorded timestamps. Older entries retain an unknown time.

BoardView polls only while open. Mutations invalidate earlier reads. Detail selection follows the card ID across snapshots. SlopWeb forwards pointer device and capture cancellation into the native gesture owner. The browser’s visible viewport sets responsive canvas height when a keyboard is present.

See the [Board guide](../docs/src/workspace/board.md) and [API contract](../docs/src/reference/api.md#project-boards).
