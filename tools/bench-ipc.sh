#!/usr/bin/env bash
# Called by Make from the repository root; settings come from make/config.mk.
set -euo pipefail

configuration=Debug
[[ "$BUILD" != release ]] || configuration=Release

if [[ "${1:-all}" != run ]]; then
${DOTNET} build bench/ipc/csharp/IpcBench.csproj --configuration ${configuration} --verbosity quiet -p:RestoreLockedMode=true
# shellcheck disable=SC2086
(cd bench/ipc/rust && ${CARGO} build --quiet ${CARGOFLAGS})
fi
[[ "${1:-all}" != build ]] || exit 0
mkdir -p bench/ipc/results
mono bench/ipc/csharp/bin/${configuration}/net472/IpcBench.exe bench/ipc/fixtures > bench/ipc/results/mono.csv
DOTNET_TieredCompilation=0 ${DOTNET} bench/ipc/csharp/bin/${configuration}/net8.0/IpcBench.dll bench/ipc/fixtures > bench/ipc/results/net8.csv
(cd bench/ipc/rust && "${CARGO_TARGET_DIR:-target}/${BUILD}/slopworld-ipc-bench" ../fixtures > ../results/rust.csv)
mono bench/ipc/csharp/bin/${configuration}/net472/IpcBench.exe --verify bench/ipc/fixtures
