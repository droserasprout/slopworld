# Refactor: MenuBackground bake vs draw

Owns: `mod/Source/SlopWorld/UI/MenuBackground.cs` (new siblings under `UI/` are
fine). See [mod-background](mod-background.md).

1077 lines, flagged as a god class — but most "fields" are `static readonly`
tuning constants + palette colors + inner-type fields (`Preset`, `LayoutState`,
`AnimationState`, `Shared`, `Spark`). The real smell is **two lifetimes in one
file**: the offline **bake pipeline** (cache dir, downsample, prep, parallel bake,
JPG IO) and the runtime **draw/animation**.

Steps:

- Extract the bake pipeline — `Root`/`Dir`/cache pruning, `ReadBack`/`Downsample`/
  `Prep`, the `ParallelOptions` bake, JPG load — into `UI/MenuBackgroundBake.cs`.
- Keep the per-frame draw, `AnimationState`, the walk, and `HasFrames`/`_frames`
  in `MenuBackground.cs`.
- Palette colors and tuning consts can move to a `MenuBackgroundTuning.cs` static.

This is a file-split, not a rewrite — the baking already sits in cohesive helpers.

Done when: the draw path and the bake path are in separate files, and
`MenuBackground.cs` is the runtime side only.
