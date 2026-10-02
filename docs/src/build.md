# Build from source

## Prerequisites

- **Rust** stable toolchain builds the daemon and launcher using the 2024 edition.
- **.NET SDK**: install the version pinned in `global.json`. It builds the `net472`
  mod and runs C# tests and formatting. NuGet restores framework reference assemblies.
- **Protobuf compiler (`protoc`)**: Rust builds require it. Contract generation
  requires **36.1** to keep generated C# output stable. Install the matching binary
  archive from the [Protobuf v36.1 release](https://github.com/protocolbuffers/protobuf/releases/tag/v36.1)
  and put its `bin` directory on `PATH`. Distro packages are suitable when they
  provide that version; check with `protoc --version`.
- **GNU Make** runs the repository targets. On macOS, install it with
  `brew install make` and use `gmake`.
- **PyYAML** parses the shared wire contract during `make api-contract`.

Set `RIMWORLD` to the Linux game directory containing `RimWorldLinux`.
Building the mod requires the game's assemblies in `Managed/`. For native macOS,
follow the [macOS guide](guides/macos.md).

## Build and install

Run `make` to list targets:

```sh
make all       # daemon, launcher, and mod
make daemon    # daemon and launcher
make mod       # mod; requires game assemblies
```

`BUILD` is `debug` by default. Use `make BUILD=release all` for a release build.
Both modes produce `mod/Assemblies/SlopWorld.dll`; Git does not track this output.
`make clean` removes build output. Exact numeric `MAJOR.MINOR.PATCH` tags at HEAD, optionally prefixed by `v`, set
the release version. Untagged checkouts append the UTC build date and short hash
to the package version. Without Git data, builds use the package version alone.

`make install` installs the daemon, systemd unit, launcher, and mod.
See [Install](install.md) for the complete setup procedure.

## Checks

| Target | Purpose | Extra requirements |
| --- | --- | --- |
| `make format` | Format Rust and C# production, test, and benchmark sources | — |
| `make lint` | Rust formatting/Clippy and a Release mod build with C# formatting checks | Game assemblies |
| `make test` | All game-free Rust, C#, tool, and pager tests | `tmux`, `less` |
| `make ci` | Game-free tests with coverage, formatting, Rust lint, and generated-contract checks | `tmux`, `less`, coverage tools |

Use `test-daemon`, `test-mod`, `test-tools`, or `test-pager` to run a subset.
`make check-format-csharp` checks C# formatting without game assemblies.

`make coverage` writes `coverage/rust.cobertura.xml` and
`coverage/csharp.cobertura.xml`. Install `cargo-llvm-cov` with
`cargo install cargo-llvm-cov --locked` and the matching `llvm-cov` and
`llvm-profdata` binaries. Use `coverage-daemon` or `coverage-mod` for one component,
and `make coverage-summary` to summarize existing reports.

## More workflows

`make BUILD=release bench BENCH_RUN=<name>` measures the daemon, C#, and IPC
suites. Reports go to `bench/results/<name>/`. Use
`make bench-report BENCH_RUN=<name>` to regenerate a report from saved CSVs;
`BENCH_BASELINE=<older> BENCH_MODE=relative` compares runs. IPC benchmarks and
terminal-input HTTP regression require Mono.

- [Terminal latency measurements](guides/terminal-latency.md) covers desktop
  input benchmarks, tracing, and report interpretation.
- [IPC benchmark suite](../../bench/ipc/README.md) explains its scope.
- [Runtime package maintenance](../../mod/Dependencies/README.md) covers dependency updates.
- [Troubleshooting](reference/troubleshooting.md) covers logs and diagnosis.
- [Contributing](reference/contributing.md) covers repository and documentation workflows.

## Local development loop

`make devloop` lists Git worktrees in the terminal where you run it.
Use these controls:

- Press Enter to rebuild the previous selection.
- Enter a number to select a different checkout.
- Enter `q` to exit.

The game starts only after the install step succeeds.
The picker also lists worktrees that you created manually.
It uses each checkout's normal host build directories.
The sidecar devloop uses a separate script.
