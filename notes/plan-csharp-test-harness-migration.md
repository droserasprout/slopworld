# C# test harness library migration

Proposed: replace manual registration/execution in `mod/Tests/Program.cs` and assertions in
`AssertEx.cs` with [NUnit](https://github.com/nunit/nunit). Owner: [C# tests](test-csharp.md).
This is independent of production library migrations and adds no shipped mod dependency.

Keep linked production sources and narrow Unity/Verse/environment substitutes. Move test
discovery and result reporting to the framework without rewriting test behavior. Preserve
the benchmark runner through a separate entry point or equivalent make-owned invocation.

## Acceptance constraints

- Every existing case remains discoverable and executes; compare case identities and counts
  before and after rather than accepting a green run with fewer tests.
- Preserve deterministic ordering where shared static state requires it. Start without
  parallel execution; enable it only for cases proven isolated.
- `make test-mod`, its use from `make test`, quiet output, failure exit status and
  `make coverage-mod` remain functional. Keep coverage scoped to linked production code.
- `make bench-mod BUILD=release` retains its independent benchmark behavior and output.
- Test framework, adapters and outputs stay under the test project, never `mod/Assemblies/`.

Run the affected make test, coverage and benchmark targets after migration. Update
`test-csharp.md` to describe discovery and the new runner, then delete this plan.
