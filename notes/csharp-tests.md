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
the game: `JVal`, `DaemonConfig`, `Fuzzy`, SGR parsing, color/theme parsing,
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

The project covers, all linked into `mod/Tests/`: `Client/Json.cs` (`JVal` round-trip),
`Client/Toml.cs`, `Client/DaemonConfig.cs`, `Client/Endpoint.cs` (normalization),
`UI/Fuzzy.cs` (match scoring/ranking), the split `Client/SessionHub/` DTOs
(`DnsConfig.TryParseServers`, `NetworkModeText.Parse`, `SessionLimits.FromJson`/`ToJson`,
`SessionInfo`/`ScreenBuf` JSON parsing), and — carved out to make them game-free —
`UI/UrlScan.cs` and `UI/PagerCommands.cs`.

`Pager` and `Sgr` remain game-bound shells. Their game-free logic lives in sibling
classes: `UI/PagerCommands.cs` handles quoting, argv templating, and pager/editor
command shapes; `UI/UrlScan.cs` handles URL/scheme scanning, trailing-punctuation
trimming, and OSC 8 parsing. Both are linked and tested while the shells keep their
public APIs as thin wrappers. See [mod-client](mod-client.md) and
[mod-ui-search](mod-ui-search.md).

`RepaintTests` links the tree/list viewport geometry, revision/reveal index, project session
counts, routed-row preparation, and terminal repaint policy/key. It checks large-tree lookup
cost, interval boundaries, cache invalidation, and selective versus full repaint decisions
without constructing game state. It does not exercise Unity drawing or event dispatch.
