# Player pawn boundary

`Sim/`'s `PlayerPawn` is user-controlled and deliberately absent from `AgentColony`.
Do not let session reconcile, robot appearance, or agent work rules claim it.

Player action bindings live in the definitions and Keyboard page. Dispatch must respect
terminal/modifier ownership so typing into an agent cannot move or fire the player pawn.
Eco and cutscenes suppress player creation/input. The colony save controls pawn recall.
Recovery of a missing or dead player must not produce an agent session.
