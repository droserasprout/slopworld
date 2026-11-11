# Private mixed WASM AOT

This opt-in experiment consumes an **already final, patched** private prepared
Eco root. It does not prepare IL, install a toolchain, launch a browser/game, or
change launcher defaults. The installed launcher offers `--aot-sdk`;
`slopweb/aot.py` owns shared project generation and loader adaptation. Release
packaging ships only that helper and authored compiler inputs. Build success
does not establish runtime correctness or performance; use browser checks and comparative benchmarks.

Use absolute paths for an existing .NET SDK `dotnet` executable with matching
`wasm-tools`, the prepared root, and a fresh output directory **inside this
checkout's `experiments/browser/artifacts/`**:

```sh
just --justfile experiments/browser/aot/justfile prepare /private/sdk/dotnet /private/prepared-root /checkout/experiments/browser/artifacts/mixed-aot
just --justfile experiments/browser/aot/justfile publish /private/sdk/dotnet /checkout/experiments/browser/artifacts/mixed-aot
just --justfile experiments/browser/aot/justfile stage /checkout/experiments/browser/artifacts/mixed-aot
just --justfile experiments/browser/aot/justfile test
```

Validate the resulting private runtime with `just slopweb-host-check-browser OUTPUT/wwwroot`.
For workload comparisons, `just slopweb-host-workspace-bounded OUTPUT/wwwroot angle-gl`
retains a diagnostic browser under a two-core quota. Use the returned profile with
`browser-host-benchmark`, then `browser-host-close`. Compare the same explicit
rendering mode and reported GPU features against the interpreted prepared root;
backend selection itself is not an AOT speedup.

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
improvements require separate measurement. Retry failed or interrupted publishes
in the same output directory to reuse compiled cache. Attempt logs append to
`publish.log`; `publish.binlog` describes the latest attempt. A nonblocking Linux
file lock rejects overlapping publish/stage operations and releases on exception
or process exit (an orphaned compiler retains it until that compiler exits). Each
publish clears the completion marker first and restores it only on success.
Staging still refuses an existing `wwwroot`; use a fresh experiment for another
staged root. Nothing under the ignored output, including DLLs, generated sources,
assets or logs, may be committed, packaged or distributed. No release tooling
consumes this directory.
