# C# tests

`make test` runs the daemon tests and the game-free mod tests. The mod is one `net472` assembly built
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

## First project

The lighter path needs no source restructure: `mod/Tests/` is a project that *links*
the specific pure source files (`<Compile Include="../Source/.../Fuzzy.cs" />`)
rather than referencing the mod DLL — the DLL only loads against a live install.
It targets net8 and uses a dependency-free executable runner; the linked files use
no game types. Its output stays under `mod/Tests/`, never in `mod/Assemblies/`,
which RimWorld loads wholesale. `make test` runs it beside `cargo test`. The cleaner path is
to first carve the pure logic into a `SlopWorld.Core` assembly both the mod and the
tests reference; do that only once the linked-file project proves the units worth
keeping.

Confirmed game-free today (no `Verse`/`UnityEngine`/`RimWorld` using-directive —
still eyeball each for transitive game types before adding): `Client/Json.cs`
(`JVal` round-trip), `Client/Toml.cs`, `Client/SlopConfig.cs`, `Client/Endpoint.cs`
(normalization), `UI/Fuzzy.cs` (match scoring/ranking), `UI/Pager.cs`, and the split
`Client/SessionHub/` DTOs — `DnsConfig.TryParseServers`, `NetworkModeText.Parse`,
`SessionLimits.FromJson`/`ToJson`, `SessionInfo`/`ScreenBuf` JSON parsing.

Start with `Fuzzy` and `JVal`: both are pure, both are dense with edge cases, and
both sit inside the big client SCC — locking their behavior is the safety net that
makes the harder `SessionHub` decoupling (see the dependency-cycle work) safe to
attempt. See [mod-client](mod-client.md), [mod-ui-search](mod-ui-search.md).
