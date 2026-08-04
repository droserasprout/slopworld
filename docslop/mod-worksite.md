# `Worksite` - what a working agent builds

A working agent takes the nearest unreserved frame; with none free it opens one
where it stands. Everything finished emits plague (`Plague.Bloom`).

- The site cannot be held *inside* the dead ground: ground only the site makes
  cannot also be ground it needs to start. `Roam` is `Plague.Girth` plus
  `RoamMargin` and a pawn past it is aimed back in - building blooms, blooming
  grows the girth, and the girth is what the leash is measured off. A distance
  rather than the plague's own shape, because "within a few cells of somewhere
  dead" asked of every candidate is hundreds of lookups where this is one.
- Leaving `Working` ends the job where it stands and `Frame.workDone` stays on the
  frame, so a monument is the sum of every burst.
- No stockpiles, no haulers, no economy, so a frame arrives with its stone in it
  (`Fill`).
- `Interval` is a quarter second: the errand a pawn is handed is the whole of what
  it does next, and a plate takes less than a tick to lay.
- `Wipe`, from `NextPlanet.Leave` - the site is the only thing here leaving
  permanent marks on the board.

## Runs (`Run`, `Lay`)

How many of a thing go down together and in what shape: a line of `Least`..`Most`,
`Lines` of those side by side, `Gap` cells between neighbours. Zeroes mean one
thing on its own, which is what most errands are, so `Add` normalises and an
errand wanting nothing special says nothing. Paving is seven-by-seven with no
gaps; graves are a row of five to ten with one.

- `Lay` is the **only** placement path - a single is a run of one. The whole
  sequence is pitched in one pass and a frame is a `Building`, so the strip is
  reserved before the first is finished; laid one at a time, a row of graves grows
  a stele through the middle of it.
- Facing is rolled once *for the run* and the line goes across it, so a row of
  graves is a row rather than a queue. Spacing is read off the thing's own rotated
  footprint (`Reach`), so the table never states a figure that has to be kept in
  step with a def.
- `_mine` is the run's own frames, and `Fits` lets them through its pad. Read as
  strangers, the pad refuses the second grave of every row and every row on the
  map comes out one grave long.
- A member that does not fit is **skipped rather than ending the run** - a grave
  wants `Diggable` ground and the agents pave, so a row through finished ground is
  meant to come out with holes in it.

## Work sheet

`Allow` puts every work type at zero and Construction at three; `Stop` puts that
back. Both halves are load-bearing: on the vanilla work sheet a colonist finds
vanilla jobs and this loop overrides them a quarter second later, so the pawn
turns round every few steps; with Construction left on while idle, the work giver
hands it the nearest frame and the site stops saying which processes are busy.

## Costs and tuning

The errand table states costs in *seconds of an agent's working time*;
`Patch_ErrandWork` lands that on `Frame.WorkToBuild`. `*Odds` is the whole of the
tuning: whole numbers summing to a hundred, each the share of the finished site.
Nothing enforces the sum - `Pick` normalises whatever it is handed, and must,
because it weighs only what the pawn in front of it could finish. `*Bloom` is kept
roughly flat per second of working time, so the map dies at the speed the sessions
are busy.

## Placement guards

- A frame is handed out only if `GenConstruct.CanConstruct` says yes - the
  driver's own fail condition asked one tick early; otherwise an unreachable frame
  is handed out, fails, and is handed back forever.
- A round of darts finding nowhere to lay *floor* sits the site down for five
  seconds (`BlockedFor`), or every agent asking four times a second is five
  hundred `CanPlaceBlueprintAt` calls a second against ground that will not
  change. Anything with a shape asks for a footprint and a pad, and finding no
  room near one pawn is not grounds for stopping agents who could pave.
- `Sweep`, once a second: a frame with a plant grown into it or a chunk on it is
  one vanilla wants *cleared* first, which here means work no agent is allowed and
  a hauler that does not exist. It can never finish and counts against `MaxOpen`,
  so a site left alone fills its own quota with rubbish.
  `GenConstruct.FirstBlockingThing` is vanilla's own word for it.
- `Fits` reads `clearBuildingArea` and `forceMoveItemsBeforeConstruction` off the
  thing's **blueprint** rather than the thing: for a floor the two disagree and
  only the blueprint's is the answer the game will give. `NewBlueprintDef_Terrain`
  sets both false, so a plate goes over grass and slag and only a plant worth
  harvesting blocks one (`Rooted`). Read off the `TerrainDef`, where
  `clearBuildingArea` defaults true, every cell with a blade of grass was refused
  as a paving site.
- `Patch_HideFloorFrames` - floor is queued a square at a time, so its corner
  brackets are a grid over most of the map saying nothing anybody can act on.
  Anything with a shape keeps its frame.

## XML

- `Patches/PavingHands.xml` takes steel tile's inherited
  `constructionSkillPrerequisite` of three off the paving terrain, or a colonist
  rolled a two never lays a plate and half the table is silently off its sheet,
  and `Sweep` destroys floor a skilled clanker queued once nobody has the hands.
  Same ground as `SteadyHands` and `Patch_AgentsCanBuild`
  ([mod-patches-agents](mod-patches-agents.md)).
- `Patches/AncientBuildings.xml` is all that stands between here and a server
  rack: `BuildableDef.BuildableByPlayer` is literally `designationCategory !=
  null`, and a frame is generated for nothing else. Those defs cost nothing and
  ask no skill, so a frame is workable the tick it is placed. `AncientLamp` is a
  `CompGlower` with neither a power nor a fuel comp, the one light in the game
  that simply burns. `AncientMachine` needs
  `disableImpassableShotOverConfigError` in the same patch: vanilla calls
  impassable-and-half-filling an error the moment a def becomes player-buildable.
  Sculptures are absent because they are bench work in this game, and steles carry
  the same `CompArt` anyway.
