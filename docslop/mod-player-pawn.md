# Player pawn

A user-controlled human colonist, separate from the agent system.

- **`PlayerPawn`** (`GameComponent`) spawns one colonist named "Player" with a
  random human look — no robot face, no metal skin, no agent outfit. Not
  registered in `AgentColony`, so it stays off the sidebar and out of the
  session reconcile.
- Walks to the camera centre. Every 15 frames the component reads
  `CameraDriver.CurrentViewRect.CenterCell`; when it moves 3+ cells a new
  `Goto` job at jog speed replaces whatever the pawn was doing.
  After a center path finishes, autopilot waits 10 seconds of no camera movement
  or player action before taking control again.
- Press **1** (bare, no modifiers, terminal closed) to launch a `SlopFireball`
  projectile from the pawn toward the mouse cursor. The projectile is a flame
  bullet at speed 25; on impact **`Fireball`** (`Projectile` subclass) fires a
  circular 2.5-radius flame explosion. Press **2** under the same conditions to
  launch a `SlopWaterBall`, a restorative impact with no explosion. It heals nearby
  pawns, revives plants, clears local plague records, and permanently turns barren
  natural ground into rich soil. One in five impacts instead turns the eligible patch
  into shallow freshwater. Both actions keep the direct arrow-to-mouse behavior and
  share a half-second cooldown.
- Press **3** under the same bare-key conditions to teleport the player pawn to the
  cursor, falling back to the nearest standable cell and cancelling its current job.
- The pawn reference is saved with the colony (`Scribe_References`); if it dies
  or is destroyed, `EnsurePawn` respawns it at map centre.
- Defs: `mod/Defs/PlayerPawn.xml` (`SlopFireball`, `SlopWaterBall`).
  `SlopDefOf` holds both references.
