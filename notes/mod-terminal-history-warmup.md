# Terminal history

`TerminalHistory` indexes snapshots in live-bottom coordinates. Live growth shifts that
coordinate system; delayed replies must be translated by captured versus current history
extent. Request-time movement is only a fallback for frames lacking metadata.

A TUI may rewrite its prompt before scrolling. Old live rows and a delayed capture's live
tail cannot prove historical content; fetch those rows again. Confirmed older history stays
cached. Real daemon extent outranks visual overlap guesses and bounds even the first gesture.

Warmup is speculative and bounded; it must not change displayed scroll position. Missing
visible rows take priority over lookahead. Skip incompatible dimensions, known-empty history,
alternate screens and covered panes. Keep one request in flight per history owner.

Returning live keeps indexed rows. Reusing them after a tab switch requires matching run,
viewport, connection generation and screen mode; pending requests do not survive the gap.
The displayed scrollback snapshot freezes every row, including live-tail and fractional
overscan rows, while the indexed cache continues warming. Live growth translates its anchor;
returning live releases it.
Selection across missing rows must leave the clipboard unchanged rather than copy partial text.
