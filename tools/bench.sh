#!/usr/bin/env bash
# just supplies the tool settings. Build once, then run the same binaries each time.
set -euo pipefail
configuration=Debug
[[ "$BUILD" != release ]] || configuration=Release
suite=${2:-gamefree}
case "${1:-}" in
build)
    # CARGOFLAGS is empty in debug builds.
    # shellcheck disable=SC2086
    (cd slopd && ${CARGO} build --quiet --bin slopd ${CARGOFLAGS})
    ${DOTNET} build "${TEST_PROJECT}" --configuration "$configuration" --verbosity quiet
    bash tools/bench-ipc.sh build
    ;;
run)
    if [[ "$suite" == gamefree || "$suite" == daemon ]]; then
        (cd slopd && "${CARGO_TARGET_DIR:-target}/${BUILD}/slopd" --perf-bench)
    fi
    if [[ "$suite" == gamefree || "$suite" == mod ]]; then
        DOTNET_TieredCompilation=0 ${DOTNET} "mod/Tests/bin/$configuration/net8.0/SlopWorld.Tests.dll" --perf-bench
    fi
    if [[ "$suite" == gamefree || "$suite" == ipc ]]; then
        bash tools/bench-ipc.sh run
    fi
    ;;
*) echo "usage: $0 {build|run} [gamefree|daemon|mod|ipc]" >&2; exit 2 ;;
esac
