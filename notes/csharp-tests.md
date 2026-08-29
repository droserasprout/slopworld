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
from Unity/game calls and run them in CI. Patch binding, critical `GameComponent`
behavior, pixel layout, and most Harmony details remain in-game checks.

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

Covered today, all linked into `mod/Tests/`: `Client/Json.cs` (`JVal` round-trip),
`Client/Toml.cs`, `Client/SlopConfig.cs`, `Client/Endpoint.cs` (normalization),
`UI/Fuzzy.cs` (match scoring/ranking), the split `Client/SessionHub/` DTOs
(`DnsConfig.TryParseServers`, `NetworkModeText.Parse`, `SessionLimits.FromJson`/`ToJson`,
`SessionInfo`/`ScreenBuf` JSON parsing), and — carved out to make them game-free —
`UI/UrlScan.cs` and `UI/PagerCommands.cs`.

`Pager.cs` and `Sgr.cs` were *not* game-free as first guessed: `Pager` reaches
`SessionHub.Instance`/`TerminalWindow`/`SlopWidgets`, and `Sgr` uses `UnityEngine.Color`
and `TerminalTheme.Current`. The pure logic in each was extracted into a sibling class
the game-bound original delegates to — `UI/PagerCommands.cs` (quoting, argv templating,
pager/editor command shapes; config passed in) and `UI/UrlScan.cs` (URL/scheme scanning,
trailing-punctuation trim, OSC 8 parsing). Both are linked and tested; the game-bound
`Pager`/`Sgr` shells keep their public API as thin wrappers.

`Fuzzy` and `JVal` were the first units locked: both pure, both dense with edge cases,
both inside the big client SCC — the safety net that makes the harder `SessionHub`
decoupling (see the dependency-cycle work) safe to attempt. See
[mod-client](mod-client.md), [mod-ui-search](mod-ui-search.md).
