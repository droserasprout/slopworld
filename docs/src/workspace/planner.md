# Planner

Planner is your manual todo.txt backlog. Agent [Tasks](../agents/agent-collaboration.md#task-mailboxes)
carry assignments and progress reports; Library [Timers](library.md#timers) schedule
work. Planner entries can link to Tasks and Workers, but due dates alone do not
start work.

## Add and edit tasks

Open **Planner** with the chess table button beside Settings or the chess table on the map.
Use **Add a task** for quick entry, check a task to complete it, and select a row to
edit its details. Quick-add and checkboxes save
immediately. The details pane uses **Save changes** and **Discard edits**.

Use projects (`+project`) and contexts (`@context`) to group work for filtering
and search. The **Projects** dropdown accepts multiple TODO or configured projects. Smaller windows
use filter menus and open details separately.

Use the Markdown field beneath the title for notes, checklists, and code blocks.
**Preview** renders the draft. **Save changes** saves the task and all edited descriptions;
failed saves retain drafts for retry. **Discard** restores the last saved values.
Drafts survive task and view changes within the mod session; save before quitting.

**Tasks | Raw** switches between the task list and its underlying
[todo.txt text](https://github.com/todotxt/todo.txt). Field edits preserve unknown metadata.
**Refresh** reloads external TODO edits and links; **Reload description** reads external
Markdown edits. On a conflict, copy the affected draft before discarding and reloading.

## Link tasks and workers

**Links** lists associated Tasks and Workers, with live status and progress. Click a
Task to open its details or a Worker to focus its terminal. **Link Task…** and
**Link Worker…** attach existing items; **Remove** detaches them without deleting,
canceling, or stopping them. Worker outcomes do not change the Planner checkbox.

Save edits before choosing **Spawn worker…**. Select a project, then use the worker
dialog, prefilled with the task title and saved description. Spawning links the new
Task and Worker even if startup fails. You can remove old links and spawn another worker.
To cancel an unfinished task, open its link and choose **Cancel**. Cancellation prevents
further task updates but leaves the worker process running.

## Storage and recovery

The daemon stores the backlog in `TODO.txt` (up to 64 KiB) in its data directory,
normally `~/.local/share/slopworld/`. Each saved row has a stable `sw-id:`.
Keep that ID when renaming or moving a row in **Raw**. Give a copied row a new
ID by removing its `sw-id:` before saving.

The ID connects the row to two files:

- `planner/descriptions/<sw-id>.md` holds its Markdown description (up to 1 MiB).
- `planner/links/<sw-id>.json` lists its linked Tasks and Workers.

Removing a row leaves its description, links, and worker tasks in place.
Restoring the row with its original ID reconnects it to those files.

Include these files in your [backup](../maintenance/backup-and-recovery.md).
