#!/usr/bin/env bash
# Make supplies the tool settings. Build once, then run the same binaries each time.
set -euo pipefail
configuration=Debug
[[ "$BUILD" != release ]] || configuration=Release
case "${1:-}" in
build)
    # CARGOFLAGS is empty in debug builds.
    # shellcheck disable=SC2086
    (cd slopd && ${CARGO} build --quiet --bin slopd ${CARGOFLAGS})
    ${DOTNET} build "${TEST_PROJECT}" --configuration "$configuration" --verbosity quiet
    bash tools/bench-ipc.sh build
    ;;
run)
    (cd slopd && "${CARGO_TARGET_DIR:-target}/${BUILD}/slopd" --perf-bench)
    DOTNET_TieredCompilation=0 ${DOTNET} "mod/Tests/bin/$configuration/net8.0/SlopWorld.Tests.dll" --perf-bench
    bash tools/bench-ipc.sh run
    ;;
*) echo "usage: $0 {build|run}" >&2; exit 2 ;;
esac
