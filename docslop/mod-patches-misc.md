# Odds and ends in `Patches/`

- **`RunInBackground`** - the **setter** is forced, not the getter, because what
  reaches Unity is `PrefsData.Apply` reading the field. Enforced once at startup
  through `LongEventHandler.ExecuteWhenFinished`, `Apply` being a no-op off the
  main thread.
- **`RealTimePatches`** - every duration the game prints, in real time.
- **`LoadingScreen`** - the tip pool is cached on the first draw into a static
  nothing rebuilds, and that draw is before any `StaticConstructorOnStartup`, so
  writing the cache is the one move that lands; `currentTipIndex` goes back with
  it. The scroll is `ScrollChance` flipped against `Tick` rather than a delay of
  its own, so the pace never reads as machine load. Zalgo goes on the joined
  frame, or the noise travels with the words. Dice are `System.Random`, because
  this screen is up during map generation. `Patch_LoadingLayout` writes
  `GameplayTipWindow.WindowSize` before reading it, with `Lines` counted by probe;
  both patches stand down if the wall could not be built. The mods/DLC panel is
  patched to **zero size** as well as no draw, because `LongEventHandler` centres
  the stack on the total.
