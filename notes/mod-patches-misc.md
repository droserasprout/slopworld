# Other patch traps

`RunInBackground` must intercept the preference setter/application path, not just its getter.
Unity updates belong on the main thread. `FramePolicy` controls vSync and target FPS together.
vSync can make an FPS limit ineffective. Keep enough background frames for vanilla's tick-debt
accumulator.
Long frame gaps do not accumulate simulation time indefinitely.

The game uses loading tips before static startup constructors run. Replace the pool on first draw,
not in a later initializer. Background map generation requires an independent RNG.

Loading-wall caches depend on Grandma's visiting filtering and measured geometry.
Rebuild painted rows with the wrap and filter cache. Otherwise, excluded tips can remain visible.
Hide vanilla loading panels in layout and drawing because the engine centers their combined dimensions.
