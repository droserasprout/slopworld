# Other patch traps

`RunInBackground` must intercept the preference setter/application path, not just its getter.
Unity updates belong on the main thread. `FramePolicy` owns vSync and target FPS together:
vSync can make an FPS cap ineffective. Keep enough background frames for vanilla's tick-debt
accumulator; long frame gaps do not accumulate simulation time indefinitely.

Loading tips are consumed before static startup constructors. Replace the pool on first draw,
not in a later initializer. Background map generation requires an independent RNG.

Loading-wall caches depend on Grandma filtering and measured geometry. Rebuild painted rows
with the wrap/filter cache or excluded tips can remain visible. Hide vanilla loading panels
in layout as well as drawing, because the engine centers their combined dimensions.
