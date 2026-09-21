#!/usr/bin/env bash
# Called by Make from the repository root; settings come from make/config.mk.
set -euo pipefail

case "${1:-}" in
daemon)
command -v cargo-llvm-cov >/dev/null || { echo "missing cargo-llvm-cov; install it with: cargo install cargo-llvm-cov --locked" >&2; exit 1; }
if ! command -v llvm-cov >/dev/null || ! command -v llvm-profdata >/dev/null; then
    echo "missing LLVM coverage tools" >&2; exit 1
fi
mkdir -p "${COVERAGE_DIR}"
(cd slopd && LLVM_COV="$(command -v llvm-cov)" LLVM_PROFDATA="$(command -v llvm-profdata)" \
	${CARGO} llvm-cov --cobertura --output-path "../${COVERAGE_DIR}/rust.cobertura.xml")
${PYTHON} tools/coverage_summary.py "${COVERAGE_DIR}/rust.cobertura.xml" Rust
;;
mod)
${DOTNET} tool restore
mkdir -p "${COVERAGE_DIR}"
${DOTNET} build "${TEST_PROJECT}" --configuration Release -p:Coverage=true
${DOTNET} tool run coverlet -- "${TEST_DLL}" \
	--target dotnet --targetargs "${TEST_DLL} --quiet" \
	--include-test-assembly --exclude-by-file '**/mod/Tests/**/*.cs' \
	--format cobertura --output "${COVERAGE_DIR}/csharp.cobertura.xml"
${PYTHON} tools/coverage_summary.py "${COVERAGE_DIR}/csharp.cobertura.xml" C\#
;;
*) echo "usage: $0 {daemon|mod}" >&2; exit 2 ;;
esac
