#!/usr/bin/env bash
# Called by Make from the repository root. Settings come from make/config.mk.
set -euo pipefail

configuration=Debug
[[ "$BUILD" != release ]] || configuration=Release

if [[ "${1:-all}" != run ]]; then
${DOTNET} build bench/ipc/csharp/IpcBench.csproj --configuration ${configuration} --verbosity quiet -p:RestoreLockedMode=true
# shellcheck disable=SC2086
(cd bench/ipc/rust && ${CARGO} build --quiet ${CARGOFLAGS})
fi
[[ "${1:-all}" != build ]] || exit 0
output=${BENCH_IPC_OUTPUT:-bench/results/adhoc-ipc/raw/ipc}
mkdir -p "$output"
output=$(cd "$output" && pwd -P)
# Rust writes round-trip fixtures beside its inputs. Encode the tracked text
# fixtures into this run's scratch directory so the source tree stays untouched.
fixtures=${TMPDIR:-$output/tmp}/ipc-fixtures
mkdir -p "$fixtures"
fixtures=$(cd "$fixtures" && pwd -P)
for name in plain ansi unicode large; do
	protoc -I shared --encode=slopworld.Event shared/slopworld.proto \
		< "bench/ipc/fixtures/$name.textproto" > "$fixtures/$name.pb"
done
mono bench/ipc/csharp/bin/${configuration}/net472/IpcBench.exe "$fixtures" > "$output/mono.csv"
DOTNET_TieredCompilation=0 ${DOTNET} bench/ipc/csharp/bin/${configuration}/net8.0/IpcBench.dll "$fixtures" > "$output/net8.csv"
(cd bench/ipc/rust && "${CARGO_TARGET_DIR:-target}/${BUILD}/slopworld-ipc-bench" "$fixtures" > "$output/rust.csv")
mono bench/ipc/csharp/bin/${configuration}/net472/IpcBench.exe --verify "$fixtures"
