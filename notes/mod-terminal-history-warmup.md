# Terminal history

`TerminalHistory` indexes snapshots in live-bottom coordinates. Live growth shifts that
coordinate system. Storage uses wide coordinates; a shift beyond the entire retained
range clears and reseeds the cache without walking the reported shift.
Translate delayed replies using the captured and current history extents.

A TUI may rewrite its prompt before scrolling. Old live rows and a delayed capture's live
tail cannot prove historical content.
Fetch those rows again. Confirmed older history stays
cached. Real daemon extent outranks visual overlap guesses and bounds even the first gesture. The
advertised daemon history capacity bounds local cache coordinates.

Warmup is speculative and bounded.
It must not change the displayed scroll position. Missing
visible rows take priority over lookahead. Skip incompatible dimensions, known-empty history,
alternate screens and covered panes. Keep one request in flight per history owner.

Returning live keeps indexed rows. Reusing them after a tab switch requires matching run,
viewport, connection generation, and screen mode.
Pending requests do not remain valid through the tab switch.
The displayed scrollback snapshot freezes every row, including live-tail and fractional
overscan rows, while the indexed cache continues warming. Live growth translates its anchor.
Returning to the live view releases the snapshot.
Selection across missing rows must leave the clipboard unchanged rather than copy partial text.
