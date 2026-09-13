# The baked background

`UI/MenuBackground/MenuBackground.cs` bakes the menu and loading-screen frames from whatever
background this install shows, and caches them under
`$XDG_CACHE_HOME/slopworld/bg/<key>` as q10 JPEGs — the encoder's 8x8 blocking is
most of the look. Nothing here ships art. `Patch_MenuBackgroundRot` hooks the draw,
not `Init`, because the loading screen bypasses `Init` and [eco](mod-eco.md) draws
the same frames a third way.

## Shape

```
[0, Onset)        the ramp:   frame 0 is the game's own picture, untouched
[Onset, Total)    the grid:   Depths rungs x Phases redraws
```

The ramp is load-bearing: the picture on screen the frame before ours is
RimWorld's, so every preset opens by arriving out of it. Played once on a
smoothstepped clock, never returned to.

The grid is the steady state. Depth is how far into the preset the picture sits;
phase is a redraw of whatever moves in it. Two axes and not one ladder, so the
picture can breathe without re-rolling the flames — one index could not do both.

## Presets

`Preset(name, depths, phases, closed)`, chosen by `grandmaMode` and named in the
cache key so both survive on disk.

- **`rot`** (5 x 8) darkens, drains, tints toward `SlopPlagueGas`'s violet and burns
  the lit face. Open: phases are unrelated fBm draws, so playback picks one.
- **`glow`** (1 x 24) lays a rainbow sheen and a blinking constellation over it,
  nothing taken away. Closed: sheen and blinks are continuous in the phase and the
  set is baked so the last meets the first, so playback walks it in order. Nothing
  to breathe, hence one rung.

A third preset is a field, a branch in `Prep` and a branch in `Stage`.

## Walk

Depth is an Ornstein-Uhlenbeck walk pulled toward the middle of
`[BreatheLow, BreatheHigh]`; a sine has a period a menu is up long enough to learn.
`WalkPull`/`WalkJitter` decide whether a rung is worth baking — tuned so a minute
reaches every one. Phase is redrawn every `PhaseSecs`, drawn from the others so a
redraw always redraws. A closed preset ignores both and runs `t / LoopSecs`.

Both axes step off absolute times, so a frame that asks twice gets one answer.

## Cache

Key is `<art>-<w>x<h>-<preset><total>-<tuning>-v<Version>`. `Version` covers the
arithmetic; `Tuning` is an FNV hash of every constant the pixels depend on, because
under `Version` alone a tuning change that forgot to bump it rendered the old
numbers.

Frames load non-readable, dropping the CPU-side copy Unity keeps beside the GPU
one — half the set's resident cost, and what paid for the second axis. `Sweep`
drops directories nothing has loaded from in `KeepDays`; `Load` touches the write
time, so two sets a player toggles between both count as in use.

The resident frame set owns its generated textures. Replacements are built before the
old set is destroyed; partial loads and bakes release their allocations on failure.
The source texture remains owned by vanilla or its content pack. Failed replacements
keep the resident set and wait 60 seconds before retrying the same cache key.
