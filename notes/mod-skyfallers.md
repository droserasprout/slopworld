# Dropping a thing out of the sky

`IntroDirector.Fall` uses this to land the persona core. Use the installed RimWorld 1.6
defs and `ikdasm Assembly-CSharp.dll` to verify carrier behavior.

```csharp
GenSpawn.Spawn(SkyfallerMaker.MakeSkyfaller(ThingDefOf.ShipChunkIncoming, thing),
               cell, map);
```

- `Skyfaller.GetThingForGraphic` returns the payload whenever the skyfaller's own
  def has no `graphicData`. So a carrier def with no graphic draws whatever it is
  holding: the thing you dropped is the thing that falls.
- `Skyfaller.SpawnThings` walks `innerContainer` and calls
  `GenPlace.TryPlaceThing(..., ThingPlaceMode.Near, ...)` on each. `spawnThing` in
  `SkyfallerProperties` is *not* used there, so a carrier never adds anything of
  its own to what you handed it.
- `Skyfaller.Impact` explodes only if `SkyfallerProperties.CausesExplosion`, which
  needs `explosionDamage` non-null *and* `explosionRadius > 0`. The default radius
  is 3 but the default damage is null, so a def that says nothing does not blow up.
- Which carrier: `ShipChunkIncoming` lands harmlessly - vanilla keeps the
  explosive variant as a separate def, `ShipChunkIncoming_SmallExplosion`.
- Pawns go down in a pod instead: `DropPodUtility.DropThingsNear(cell, map,
  things, openDelay, canInstaDropDuringInit, leaveSlag, canRoofPunch, forbid,
  allowFogged, faction)`. `openDelay` is in ticks and defaults to 110; the pod
  also spends its `ticksToImpactRange` (120-200 by default) falling first, so
  budget three to five seconds between sending one and the pawn walking.

`SkyfallerProperties` defaults, from its ctor: `hitRoof` true,
`ticksToImpactRange` (120, 200), `explosionRadius` 3, `explosionDamage` null,
`speed` 1, `shadow` SkyfallerShadowCircle, `shadowSize` (1,1), `motesPerCell` 3,
`movementType` Accelerate (the enum's zero).
