# Building and running

Use `make` for project commands (`gmake` on macOS). Run it without arguments for
primary target help. Toolchain, build modes, tests, coverage, and release output are
in [Build from source](../docs/src/build.md); platform workflows live in the
book's installation guides.

For a GOG Linux install, use `make gogdl-login`, then `make gogdl-install`;
`make gogdl-update` updates `RIMWORLD`. These include owned DLCs. `GOGDL_PATH`
selects the download directory and `GOGDL_AUTH` overrides the Heroic token path.

## Build and install constraints

`RIMWORLD` must point to a real install: the mod links against its assemblies.
Both `BUILD=debug` and `BUILD=release` write `mod/Assemblies/SlopWorld.dll`;
`lint-mod` always rebuilds in Release.

`install-mod` uses the tested host command `slopworld mod install`. It stages the
shipped directories beside the destination, replaces only `Mods/SlopWorld`, and
refuses filesystem roots or destinations inside the source tree. Update
`slopd/src/bin/slopworld/mod_install.rs` when adding a shipped top-level directory.

`install-font` refreshes fontconfig when available. `FONT_DIR` and `FONT_SOURCE`
override its destination and source.

`sidecar-devloop` passes `SLOPCAR_CONFIG_DIR` to `slopcar start` so the container
writes the endpoint descriptor where the game launcher expects it.

See [build-tools](build-tools.md) for auxiliary tools and
[diagnostics](ops-diagnostics.md) for runtime checks.

`make bench-report` runs the full benchmark suite three times, averages each reported metric,
and writes a dated result note under `notes/` with the current commit hash.
