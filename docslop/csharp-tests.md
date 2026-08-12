# C# tests

`make test` runs only the daemon tests. The mod is one `net472` assembly built
against the exact RimWorld and Unity DLLs in a live install, and its runtime
code leans on `Find`, `Current`, `DefDatabase`, `Scribe`, IMGUI, frame timing,
and static singletons. A normal test runner cannot construct that game state;
mocking it would be a second, brittle RimWorld.

Harmony is not the main blocker. Patch registration and patch behavior are
integration concerns, especially when RimWorld changes a target. Runtime
failures appear in `Player.log`, not at compile time. Test assemblies also need
to stay out of the mod's `Assemblies/` directory because RimWorld loads every
DLL there.

The useful first layer is a separate C# test project for code that does not need
the game: `JVal`, `SlopConfig`, `Fuzzy`, SGR parsing, color/theme parsing,
endpoint normalization, and layout calculations. Keep those helpers separate
from Unity/game calls and run them in CI. Later, add a small RimWorld smoke
harness for patch binding and critical `GameComponent` behavior; leave pixel
layout and most Harmony details to in-game checks.
