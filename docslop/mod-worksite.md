# `Worksite` - what a working agent builds

A working agent takes the nearest unreserved frame, or opens one at its position;
every finished frame emits plague (`Plague.Bloom`).

- The site cannot start inside its own dead ground. `Roam` is `Plague.Girth` plus
  `RoamMargin`; building grows the girth, and the distance check avoids scanning
  the plague's shape for every candidate.
- Leaving `Working` ends the job where it stands and `Frame.workDone` stays on the
  frame, so a monument is the sum of every burst.
- No stockpiles, no haulers, no economy, so a frame arrives with its stone in it
  (`Fill`).
- `Interval` is a quarter second: the errand a pawn is handed is the whole of what
  it does next, and a plate takes less than a tick to lay.
- `Wipe`, from `NextPlanet.Leave` - the site is the only thing here leaving
  permanent marks on the board.

## Runs (`Run`, `Lay`)

`Run` describes `Least`..`Most` items in `Lines` rows with `Gap` cells between
neighbours. Zeroes normalise to one item; paving is seven-by-seven with no gaps,
while graves form rows of five to ten with one-cell gaps.

- `Lay` is the only placement path. It reserves the complete run before the first
  item finishes, rolls facing once per run, and derives spacing from the rotated
  footprint (`Reach`).
- `_mine` marks the run's own frames so `Fits` applies the pad consistently.
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
- A round of darts finding nowhere to lay floor pauses the site for five seconds
  (`BlockedFor`), avoiding repeated `CanPlaceBlueprintAt` calls. Shaped items use
  a footprint and pad; one pawn finding no room does not stop other agents paving.
- `Sweep`, once a second: a frame with a plant grown into it or a chunk on it is
  one vanilla wants *cleared* first, which here means work no agent is allowed and
  a hauler that does not exist. It can never finish and counts against `MaxOpen`,
  so a site left alone fills its own quota with rubbish.
  `GenConstruct.FirstBlockingThing` is vanilla's own word for it.
- `Fits` reads `clearBuildingArea` and `forceMoveItemsBeforeConstruction` from the
  **blueprint**. Terrain blueprints set both false, so plates cross grass and slag;
  only a harvestable plant (`Rooted`) blocks them.
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
