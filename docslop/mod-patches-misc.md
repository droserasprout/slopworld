# Odds and ends in `Patches/`

- **`RunInBackground`** - the **setter** is forced, not the getter, because what
  reaches Unity is `PrefsData.Apply` reading the field. Enforced once at startup
  through `LongEventHandler.ExecuteWhenFinished`, `Apply` being a no-op off the
  main thread. The frame cap beside it is what makes running in the background
  affordable, and the sim does not slow with the frames: `TickManagerUpdate` banks
  `Time.deltaTime` and spends up to 45ms a frame paying it back, so fifteen frames
  a second is four ticks a frame at exact pace. Below ten it stops banking - the
  accumulator is *assigned* rather than added once `deltaTime` reaches 0.1 - hence
  the clearance. vSync comes off with it or the cap does nothing, Unity ignoring
  `targetFrameRate` while `vSyncCount` is set; both go back as found. Eco mode
  asks the same class for a cap of its own - see [mod-eco](mod-eco.md).
- **`RealTimePatches`** - every duration the game prints, in real time.
- **`LoadingScreen`** - the tip pool is cached on the first draw into a static
  nothing rebuilds, and that draw is before any `StaticConstructorOnStartup`, so
  writing the cache is the one move that lands; `currentTipIndex` goes back with
  it. The scroll is `ScrollChance` flipped against `Tick` rather than a delay of
  its own, so the pace never reads as machine load. A tip may end in a marker
  naming who it is for, and the marker is a face: ` (` goes when grandma is
  visiting, ` )` is shown only then, unmarked either way, and `Strip` takes it off
  whichever way it went.

  The wall is the tips wrapped into **one list of rows** (`Wall`) and `_top` is a
  position in it, not a block per scroll position - it used to materialise the
  whole stream again for every row of height. The rows on screen are seasoned
  individually and kept in `_rows`; a scroll shifts that ring and seasons the one
  row arriving at the bottom, and `Flicker` others are rolled again per tick. Zalgo
  goes on a row rather than the joined block, or the noise travels with the words -
  and the whole block seasoned fourteen times a second was `StringBuilder` churn
  during map generation, which is when the collector is least welcome.

  `Wall` is keyed on `grandmaMode` and on `Generation`: the tip filter runs once at
  the build, so turning the setting on has to rebuild, and the wrap is measured
  against a box, so a screen that changed size has to. `_rows` and `_painted` are
  reset with it - `_painted` is what `DrawContents` draws, so a stale one keeps the
  dropped tips on screen. Dice are `System.Random`, because this screen is up
  during map generation. `Patch_LoadingLayout` writes `GameplayTipWindow.WindowSize`
  before reading it, once per `Generation`, with `Lines` counted by probe; both
  patches stand down if the wall could not be built. The mods/DLC panel is patched
  to **zero size** as well as no draw, because `LongEventHandler` centres the stack
  on the total.
