# Player pawn

A user-controlled human colonist, separate from the agent system.

- **`PlayerPawn`** (`GameComponent`) spawns one colonist named "Player" with a
  random human look; robot faces, metal skin, and agent outfits are excluded. It is not
  registered in `AgentColony`, so it stays off the sidebar and out of the
  session reconcile.
- Camera movement does not move the pawn. Press **1** (bare, no modifiers,
  terminal closed) to start a jog-speed `Goto` job to the cursor, using the
  nearest standable cell when the cursor is over impassable terrain.
- Press **2** under the same conditions to launch a `SlopFireball`
  projectile from the pawn toward the mouse cursor. The projectile is a flame
  bullet at speed 25; on impact **`Fireball`** (`Projectile` subclass) fires a
  circular 2.5-radius flame explosion. Press **3** under the same conditions to
  launch a `SlopWaterBall`, a restorative impact with no explosion. It heals nearby
  pawns, revives plants, clears local plague records, and permanently turns barren
  natural ground into rich soil. One in five impacts instead turns the eligible patch
  into shallow freshwater. Both projectile actions keep the direct arrow-to-mouse
  behavior and share a half-second cooldown.
- Press **4** under the same bare-key conditions to teleport the player pawn to the
  cursor, falling back to the nearest standable cell and cancelling its current job.
- Press **5** for Cat Whistle. The cat makes its species call and starts a
  jog-speed `Goto` job to the cursor, falling back to the nearest standable cell.
  All five actions are rebindable under Settings > Keyboard > Player; their
  defaults are **1** through **5** in the order above.
- The pawn reference is saved with the colony (`Scribe_References`); if it dies
  or is destroyed, `EnsurePawn` respawns it at map centre.
- Defs: `mod/Defs/PlayerPawn.xml` (`SlopFireball`, `SlopWaterBall`).
  `SlopDefOf` holds both references.
