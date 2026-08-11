# Search view

The sidebar's Search tab asks `GET /api/search` once per project and groups the returned
matches under project and file headings. Enter runs the query; Case, Word, and Regex alter the next
run rather than searching on every keystroke. A generation number drops replies from an
older query that arrive after a newer one. The result list measures every row for scrolling but
only draws rows in the viewport, since a common term can fill the per-project cap many times over.

The daemon spawns `rg` directly in the project directory and reads `--json` a line at a
time. It kills the process at 200 matches, excludes `.git`, and returns relative path,
line, byte column, and a UTF-8-safe line preview of at most 1,000 bytes, centred on the first
match when a generated line is longer. No query or path passes through a shell.

Clicking a match opens a tracked `less` at that line in the project's sandbox. Search owns
that pager, as Files owns file viewers and Git owns diffs, so leaving the tab or closing
the pane stops only the reader Search created.
