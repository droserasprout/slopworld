# Terminal history

`TerminalHistory` indexes rows relative to the live bottom. Live growth shifts those
coordinates. The live daemon extent bounds scrolling, and local capacity is the
smaller of the advertised capacity and the client limit. History clear, viewport or
screen-mode change, run/connection change, or a shift beyond retained range resets
cached history.

Translate delayed captures using captured and current extents. Old live rows and a
delayed live tail do not establish historical content; gaps require fresh capture.
Confirmed older history survives ordinary live growth.

Warmup runs during eligible visible-panel draws, is bounded, and never moves the
displayed position. Visible gaps precede lookahead. Offline, size-dirty, editor,
pending-request, mismatched-dimension, alternate-screen, and application-mouse panes
cannot warm. Each owner keeps one request in flight.

Returning live retains indexed rows. Tab reuse requires matching run, viewport,
connection, and screen mode; pending requests do not survive the switch. A pinned
scroll snapshot freezes its rows while the cache can warm and live growth translates
its anchor. Returning live releases it. Selection/copy belongs to
[terminal input](mod-terminal.md).
