# Search ownership

Search spans daemon `rg` execution and the sidebar's `SearchView`. Operation tokens reject
late replies after query replacement/clear or scope changes. Each submission snapshots the
enabled ready checkouts from the shared scope catalog. At most four requests run at once.
Replacement queries keep that limit while old requests drain. Bound daemon output while reading, not after
collecting it.
UTF-8 truncation must preserve usable match context.

Query edits do not trigger a search until submission. Result geometry is fully measured but
only visible rows draw and accept clicks. Wheel-only passes update the offset without
visiting rows.
They preserve movement but omit intermediate row work.
Closing the tab releases field focus but keeps its reader.
Replacing or explicitly dismissing the Search-owned pager releases that session.
Files and Git keep independent reader ownership.

Filter changes rerun the last immutable submission, preserving draft fields and the reader.
Results and history use stable scope keys plus relative paths. Reader actions retain the root
from that result. Project and checkout headings separate identical paths and failed scopes.
