# Game-free C# tests

Run `make test-mod`, also included in `make test`. Build and coverage commands belong
to [Build from source](../docs/src/build.md).

The .NET 8/CoreCLR project explicitly links production sources and supplies substitutes
for game/Unity dependencies. NUnitLite uses a non-parallel fixture; the harness finds
test types whose names end in `Tests` and invokes qualifying public static methods.
Tests cover pure policies and game-free client/transport integration. Individual
tests own the detailed behavior inventory.

Substitutes do not verify Unity drawing/input or Harmony target binding. Test output,
including `TestResult.xml`, belongs in the executable output directory, not the
shipped `Assemblies/` tree. RimWorld must not load test/substitute assemblies.
Game checks, when requested, use [build diagnostics](../docs/src/build.md).

Benchmark workflow belongs to [build commands](build-commands.md) and
[IPC benchmarks](../bench/ipc/README.md).
