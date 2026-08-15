# Warm startup

Warm startup is the launcher, the engine, Core and mod content, `SlopWorldBootstrap`,
one menu frame, and the save `Sim/AutoResume.cs` loads on it. None of what follows is
implemented. The mod has no timing instrumentation, so the ranking is read off file
sizes rather than a profile; add one before acting on it. Per-frame work is
[cpu-optimization](cpu-optimization.md).

## What a save costs

An 8.52 MB autosave of a 200x200 map, by thing class:

| | count | bytes |
|---|---|---|
| `Plant` | 11,824 | 4.44 MB |
| `<world>` | | 1.94 MB |
| `Filth` | 2,787 | 1.00 MB |
| `Pawn` | 52 | 636 KB |

15,246 `<thing>` elements are deserialized and spawned on every warm start.

## Candidates

- `QuickStart.MapSize` is 200, so 40,000 cells. Plants, filth, grids and region
  building scale with that; 150 is 22,500 cells.
- `QuickStart.PlanetCoverage` is 0.3. `Patch_NoLeavingTheMap` forbids leaving the map,
  so the only reader is `LandingSite.Choose`, which needs a tropical or temperate
  forest tile at flat or small hills. A smaller planet reaches that tile's warning
  more often.
- Nothing removes filth. `Aura` clears it near the cat and fire keeps making ash, so a
  save grows for as long as its colony runs.
- Plants never grow, because the sim is stripped and eco holds the clock.
  `saveCompressible` on the plant defs moves them into `MapFileCompressor`'s per-cell
  grid, but a compressed thing is rebuilt with `ThingMaker.MakeThing(def)` and loses
  its growth, so trees come back as saplings without a post-load pass.
- `MenuBackground.Load` decodes a whole set inside the first menu draw, 54 frames for
  the rotting preset. Only the ramp is drawn during `OnsetSecs`; see
  [mod-background](mod-background.md).
- `Prefs.TextureCompression` makes `ModContentLoader.LoadTexture` call
  `Texture2D.Compress(true)` on every texture Core loads. Enforcing it off trades VRAM
  for load time, as `Patch_RunInBackground` already enforces a pref.

## The OST is not a startup cost

`ModContentLoader.ShouldStreamAudioClipFromFile` returns true for a filesystem file
over 300 KB, so the four OST tracks load as streaming clips and are never decoded.
Taking them out of `Sounds/` would save only the per-file `UnityWebRequest` that
`LoadItem` busy-waits on at `Thread.Sleep(1)` granularity.

Taking them out is possible for other reasons. The daemon opens the directory by path
rather than through RimWorld's content system, and the `SlopWorld_` `SongDef`s have no
C# reader; see [mod-jukebox](mod-jukebox.md). `Root_Play.Update` calls `Root.Update`
before `MusicManagerPlay.MusicUpdate`, so `Radio` sets `disabled` first and
`MusicUpdate` returns. With no appropriate song `ChooseNextSong` logs an error and
picks a random `SongDef` instead of throwing, so an empty `Songs.xml` is loud rather
than fatal.

## Daemon restart

`Manager::sync_from_config` rebuilds sessions one after another, and each
`spawn_reader` spawns two tmux processes and feeds up to the fixed 10,000-line scrollback
through the emulator. The sessions are independent of each other. One `list-panes -a` answers
the per-session size and cursor queries together, and the mod shows one pane at a
time, so full scrollback can wait for a subscribe. What the rebuild has to restore is
[daemon-redeploy](daemon-redeploy.md).
