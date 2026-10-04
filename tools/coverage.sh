#!/usr/bin/env bash
# Called by just from the repository root. Settings come from just/config.just.
set -euo pipefail

case "${1:-}" in
daemon)
command -v cargo-llvm-cov >/dev/null || { echo "Missing cargo-llvm-cov. Install it with: cargo install cargo-llvm-cov --locked" >&2; exit 1; }
if ! command -v llvm-cov >/dev/null || ! command -v llvm-profdata >/dev/null; then
    echo "Missing LLVM coverage tools." >&2; exit 1
fi
mkdir -p "${COVERAGE_DIR}"
(
    cd slopd
    export LLVM_COV="$(command -v llvm-cov)" LLVM_PROFDATA="$(command -v llvm-profdata)"
    ${CARGO} llvm-cov clean --profraw-only
    ${CARGO} llvm-cov --no-report
    ${CARGO} llvm-cov report --cobertura --output-path "../${COVERAGE_DIR}/rust.cobertura.xml"
    ${CARGO} llvm-cov report --ignore-filename-regex "${RUST_COVERAGE_EXCLUDE}" \
        --cobertura --output-path "../${COVERAGE_DIR}/rust.filtered.cobertura.xml"
    ${CARGO} llvm-cov report --ignore-filename-regex "${RUST_COVERAGE_EXCLUDE}" \
        > "../${COVERAGE_DIR}/rust.files.txt"
)
${PYTHON} tools/coverage_summary.py "${COVERAGE_DIR}/rust.cobertura.xml" 'Rust (default scope)'
${PYTHON} tools/coverage_summary.py "${COVERAGE_DIR}/rust.filtered.cobertura.xml" 'Rust (excluding test/generated/benchmark files)'
echo "Per-file coverage: ${COVERAGE_DIR}/rust.files.txt"
;;
mod)
${DOTNET} tool restore
mkdir -p "${COVERAGE_DIR}"
${DOTNET} build "${TEST_PROJECT}" --configuration Release -p:Coverage=true
# Measure handwritten behavior, not protoc's generated serialization machinery.
${DOTNET} tool run coverlet -- "${TEST_DLL}" \
	--target dotnet --targetargs "${TEST_DLL} --quiet" \
	--include-test-assembly --exclude-by-file '**/mod/Tests/**/*.cs' \
	--exclude-by-file '**/Client/Generated/Slopworld.cs' \
	--format cobertura --output "${COVERAGE_DIR}/csharp.cobertura.xml"
${PYTHON} tools/coverage_summary.py "${COVERAGE_DIR}/csharp.cobertura.xml" C\#
;;
*) echo "usage: $0 {daemon|mod}" >&2; exit 2 ;;
esac
