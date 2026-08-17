# Player pawn

A user-controlled human colonist, separate from the agent system.

- **`PlayerPawn`** (`GameComponent`) spawns one colonist named "Player" with a
  random human look — no robot face, no metal skin, no agent outfit. Not
  registered in `AgentColony`, so it stays off the sidebar and out of the
  session reconcile.
- Walks to the camera centre. Every 15 frames the component reads
  `CameraDriver.CurrentViewRect.CenterCell`; when it moves 3+ cells a new
  `Goto` job at jog speed replaces whatever the pawn was doing.
- Press **1** (bare, no modifiers, terminal closed) to launch a `SlopFireball`
  projectile from the pawn toward the mouse cursor. The projectile is a flame
  bullet at speed 25; on impact **`Fireball`** (`Projectile` subclass) fires a
  2.9-radius flame explosion. Half-second cooldown between casts.
- The pawn reference is saved with the colony (`Scribe_References`); if it dies
  or is destroyed, `EnsurePawn` respawns it at map centre.
- Def: `mod/Defs/PlayerPawn.xml` (`SlopFireball`). `SlopDefOf.SlopFireball`
  holds the reference.
