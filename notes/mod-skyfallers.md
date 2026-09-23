# Dropping a thing out of the sky

`IntroDirector.Fall` uses this to land the persona core. Use the installed RimWorld 1.6
defs and `ikdasm Assembly-CSharp.dll` to verify carrier behavior.

```csharp
GenSpawn.Spawn(SkyfallerMaker.MakeSkyfaller(ThingDefOf.ShipChunkIncoming, thing),
               cell, map);
```

- `Skyfaller.GetThingForGraphic` returns the payload whenever the skyfaller's own
  def has no `graphicData`.
  A carrier def without a graphic therefore draws the thing that it carries.
- `Skyfaller.SpawnThings` walks `innerContainer` and calls
  `GenPlace.TryPlaceThing(..., ThingPlaceMode.Near, ...)` on each. `spawnThing` in
  `SkyfallerProperties` is *not* used there, so a carrier never adds anything of
  its own to the supplied contents.
- `Skyfaller.Impact` explodes only if `SkyfallerProperties.CausesExplosion`, which
  needs `explosionDamage` non-null *and* `explosionRadius > 0`. The default radius
  is 3. The default damage is null, so a def without these settings does not explode.
- `ShipChunkIncoming` lands without damage.
  The base game uses a separate def for the explosive variant: `ShipChunkIncoming_SmallExplosion`.
- Pawns go down in a pod instead: `DropPodUtility.DropThingsNear(cell, map,
  things, openDelay, canInstaDropDuringInit, leaveSlag, canRoofPunch, forbid,
  allowFogged, faction)`. `openDelay` is in ticks and defaults to 110.
  The pod first falls for its `ticksToImpactRange` (120-200 by default).
  Allow three to five seconds between sending a pod and the pawn walking.

`SkyfallerProperties` defaults, from its ctor: `hitRoof` true,
`ticksToImpactRange` (120, 200), `explosionRadius` 3, `explosionDamage` null,
`speed` 1, `shadow` SkyfallerShadowCircle, `shadowSize` (1,1), `motesPerCell` 3,
`movementType` Accelerate (the enum's zero).
