# Player pawn boundary

`Sim/`'s `PlayerPawn` is user-controlled and deliberately absent from `AgentColony`.
Do not let session reconcile, robot appearance, or agent work rules claim it.

Player action bindings live in the definitions and Keyboard page. Dispatch must respect
terminal/modifier ownership so typing into an agent cannot move or fire the player pawn.
Eco and cutscenes suppress player creation/input. Pawn recall belongs to the colony save;
missing/dead player recovery must not produce an agent session.
