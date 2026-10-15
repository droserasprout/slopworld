# Game-free C# tests

Run `just mod-test`, also included in `just test`. Build and coverage commands belong
to [Build from source](../docs/src/development/build.md).

The .NET 10/CoreCLR project explicitly links production sources and supplies substitutes
for game/Unity dependencies. NUnitLite uses a non-parallel fixture; the harness finds
test types whose names end in `Tests` and invokes qualifying public static methods.
Tests cover pure policies and game-free client/transport integration. Individual
tests own the detailed behavior inventory.

Linux idle-inhibition integration tests require `dbus-daemon`.
They start an isolated private bus and fake desktop services; they never acquire
an inhibitor on the developer's desktop. Other platforms skip those native tests.
X11 fallback selection and periodic worker cleanup use fake leases; these tests
never open the host display or change its screensaver state.

The same project links the production `DaemonHttpTransport` for bounded loopback
HTTP tests of protocol handling, deadlines, response limits, and host-pumped callbacks.
Service tests keep a recording `DaemonClient` facade; transport tests own isolated
transport instances and exercise the real endpoint resolver.

The Harmony runtime fixture patches and restores a managed method using the shipped
DLL. Substitutes do not verify game target binding or native drawing/input. Test output,
including `TestResult.xml`, belongs in the executable output directory, not the
shipped `Assemblies/` tree. RimWorld must not load test/substitute assemblies.
Game checks, when requested, use [build diagnostics](../docs/src/development/build.md).

`just check mod-build` also runs `GameBindings` against the installed game and built mod
in an isolated assembly context. It resolves native text targets and field geometry,
and applies the swapchain rewrite to the game's actual IL. It does not start the game
or validate rendering. Keep this check separate from the game-free coverage denominator.

Benchmark workflow belongs to [build commands](build-commands.md) and
[IPC benchmarks](../bench/ipc/README.md).
