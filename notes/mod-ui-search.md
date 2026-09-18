# Search ownership

Search spans daemon `rg` execution and the sidebar's `SearchView`. Operation tokens reject
late replies after query replacement/clear. Bound daemon output while reading, not after
collecting it; UTF-8 truncation must preserve usable match context.

Query edits do not trigger a search until submission. Result geometry is fully measured but
only visible rows draw and accept clicks. Wheel-only passes update the offset without
visiting rows; movement is retained while intermediate row work is omitted.
Closing the tab releases field focus, not its
reader; replacing or explicitly dismissing the Search-owned pager releases that session.
Files and Git keep independent reader ownership.
