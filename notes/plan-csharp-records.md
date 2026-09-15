# C# data records migration

Proposed: reduce handwritten data-container boilerplate with C# records, including mutable
and short-lived data bundles. Immutability is optional; preserve each type's intended
mutation and ownership semantics. Owners: [mod source map](mod-source-layout.md),
[build](build-commands.md), and [wire contract](protocol-wire.md).

## Implementation sequence

1. Establish compiler compatibility before converting production types. `make mod` invokes
   Mono's `csc` directly with `-langversion:latest`; the SDK project's `net472` target and
   `LangVersion` do not establish what that compiler supports. Verify C# 9 records and C# 10
   record structs through the actual Makefile path. If a compiler upgrade is needed, make
   its selection reproducible across supported builds and update the build guide. Keep the
   RimWorld runtime target. Add a source-only `IsExternalInit` compatibility definition if
   needed for `init`, accounting for the tests' .NET 8 target without duplicate definitions.
2. Pilot value records with `SidebarViewLocation`. Replace handwritten equality and hashing
   with a `readonly record struct`, retaining factories, constructor accessibility, null
   normalization, and computed properties. Preserve default-value behavior. Then consider
   pure geometry bundles such as `TabbedFormGeometry` and `SandboxSplit`.
3. Migrate simple reference data bundles where generated value equality is appropriate.
   `CommandsPage.Choice` and `BinariesPage.BinarySpec` are initial immutable candidates.
   During the focused source audit, select a mutable temporary result/options type for a
   separate pilot; use a sealed record with explicit `get; set;` properties and existing
   defaults. Do not invent a new abstraction just to demonstrate mutable records.
4. Expand by owner in small changes after the pilots pass. Keep a class where object
   identity, lifecycle, or coordinated mutation is its purpose, including config drafts,
   session owners, and workspace panels. Review generated wire data separately through its
   generator; do not hand-edit generated declarations to adopt record syntax.

## Type selection and acceptance constraints

- Use positional record classes for simple immutable reference data, explicit mutable record
  classes for shared mutable data, and record structs for small values that should copy on
  assignment. Preserve existing readonly guarantees. Mutable positional record structs are
  suitable only where callers expect copies; do not replace classes with structs for brevity.
- Keep validation and restricted construction. A positional constructor or public `init`
  property must not bypass invariants through construction or `with`. For example,
  `PanelSize` currently clamps negative dimensions; a bare positional replacement would
  change that behavior.
- Audit equality users, dictionary keys, and hash-set membership before each conversion.
  Mutable records must not change equality-participating state while stored as hash keys.
  Generated equality compares stored fields, including property backing fields; collections
  retain their own equality behavior and are not automatically compared by contents.
- Treat `with` as a shallow copy. Lists and other mutable children remain shared unless
  copied explicitly. Do not imply that a record is deeply immutable or independently owned.
- Preserve observable field/property contracts: reflection, serialization, `ref`/`out` uses,
  defaults, and constructor call sites. Keep generated diagnostic strings from exposing
  secrets if record `ToString()` reaches logs. Preserve `BinaryResult.SetPath`'s coordinated
  update if that type is considered; public setters are not an equivalent replacement.

## Validation and completion

Run `make mod`, `make test-mod`, and `make lint-mod` for the implementation. Use existing
history and geometry tests, adding focused regressions only for changed semantic risks such
as default equality, normalization, or mutable aliasing. If wire types are migrated, verify
round trips and omitted/null behavior through the contract workflow as well.

The .NET 8 test build alone does not validate the production compiler or Unity Mono runtime.
Record any remaining runtime uncertainty; game execution requires an explicit user request.
Completion means the compatibility path and selected pilots work, remaining candidates have
been assessed by owner, and preserved ordinary classes have a semantic reason to stay.
Move lasting compatibility traps into the owning note and delete this plan when resolved.
