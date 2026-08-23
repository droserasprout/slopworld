# Mod easter eggs

- Clicking any spawned `Capybara` activates the egg once for the loaded game.
  Every capybara currently on that map is removed from the custom plague and kept
  immune for the rest of the runtime; the clicked one shows a small, floating
  Uruguay flag. The latch and immunity are intentionally not saved, so loading
  the game starts a fresh opportunity.
- **`Snoop`** (`GameComponent`) spawns a pawn at 04:20 and 16:20 real time, once
  per window per day, stays 5–10 minutes and leaves. The pawn smokes a joint every
  600 ticks while present. Arrival avoids plague-reached cells; Grandma mode
  suppresses the whole component. State (`_lastDay4`, `_lastDay16`, `_leaveTick`,
  pawn ref) is scribed so the visitor survives a save/load mid-visit.
