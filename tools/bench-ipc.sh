#!/usr/bin/env bash
# Called by Make from the repository root; settings come from make/config.mk.
set -euo pipefail

configuration=Debug
[[ "$BUILD" != release ]] || configuration=Release

${DOTNET} build bench/ipc/csharp/IpcBench.csproj --configuration ${configuration} --verbosity quiet -p:RestoreLockedMode=true
mkdir -p bench/ipc/results
mono bench/ipc/csharp/bin/${configuration}/net472/IpcBench.exe bench/ipc/fixtures > bench/ipc/results/mono.csv
DOTNET_TieredCompilation=0 ${DOTNET} bench/ipc/csharp/bin/${configuration}/net8.0/IpcBench.dll bench/ipc/fixtures > bench/ipc/results/net8.csv
# CARGOFLAGS is a list of flags, including an empty list for debug builds.
# shellcheck disable=SC2086
(cd bench/ipc/rust && ${CARGO} run --quiet ${CARGOFLAGS} -- ../fixtures > ../results/rust.csv)
mono bench/ipc/csharp/bin/${configuration}/net472/IpcBench.exe --verify bench/ipc/fixtures
