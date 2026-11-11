# Private mixed WASM AOT

This opt-in experiment consumes an **already final, patched** private prepared
Eco root. It does not prepare IL, install a toolchain, launch a browser/game, or
change launcher defaults or release packaging. The parent owns runtime correctness
and comparative performance measurements.

Use absolute paths for an existing .NET SDK `dotnet` executable with matching
`wasm-tools`, the prepared root, and a fresh output directory **inside this
checkout's `experiments/browser/artifacts/`**:

```sh
just --justfile experiments/browser/aot/justfile prepare /private/sdk/dotnet /private/prepared-root /checkout/experiments/browser/artifacts/mixed-aot
just --justfile experiments/browser/aot/justfile publish /private/sdk/dotnet /checkout/experiments/browser/artifacts/mixed-aot
just --justfile experiments/browser/aot/justfile stage /checkout/experiments/browser/artifacts/mixed-aot
just --justfile experiments/browser/aot/justfile test
```

`prepare` snapshots the full private root, including fonts, portraits, background
and appearance assets. It copies final top-level root/game/mod DLLs into flat
binary references: game/mod/adapter dependencies form cycles. Duplicate assembly
names and symlinks are rejected. The project captures each declared Probe source
once and adds a private `LoadPublishedAssembly` export to its Program copy; no
production source is modified. Prepared roots must not change during capture.

`publish` writes `publish.log` and `publish.binlog`. It requires Release trimming,
roots Probe and every captured/app dependency assembly to retain metadata and IL,
keeps `WasmStripILAfterAOT=false`, and enables reflection JSON. The four absent
dependencies are pinned in the generator to the prototype's reviewed versions.
The custom CLR owners Waylith, Waylith.Injection and
RimWorldWaylith.PrepatcherCompat are forced interpreted; other eligible assemblies
use the runtime's mixed AOT/interpreter mode. Roots do not prove arbitrary
framework reflection compatibility, and IL preservation does not enable runtime
Harmony detours or Reflection.Emit. Inspect linker/AOT warnings; they are not
blanket-suppressed. Compression is disabled for diagnostic iteration.

`stage` creates a fresh `wwwroot` with the entire published framework and boot
resources together, retaining private assets and logical startup order. Its loader
resolves published identities through `Assembly.Load(name)` instead of loading
another copy from bytes. The alternate `skipLogger` assembly variant is disabled
because published code must match the captured final IL. Never patch DLLs after
publishing; generate a new snapshot for any managed code or hook change.

The SDK, Emscripten and native AOT pipeline require substantial disk, compile time
and peak memory; compilation can take many minutes. Runtime memory and workload
improvements require separate measurement. Failed or repeated publishes require a
new output directory; retained logs prevent overlapping writes and preserve failure
evidence. Nothing under the ignored output, including DLLs, generated sources,
assets or logs, may be committed, packaged or distributed. No release tooling
consumes this directory.
