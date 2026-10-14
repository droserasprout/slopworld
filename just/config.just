# Shared settings and script environment. Domain files own recipes.
# Export settings so recursive just calls retain command-line overrides.

export RIMWORLD := env("RIMWORLD", GOGDL_PATH + '/RimWorld/game')
export MANAGED := env("MANAGED", RIMWORLD + '/RimWorldLinux_Data/Managed')
export MODS := env("MODS", RIMWORLD + '/Mods')
export BIN := env("BIN", env("HOME") + '/.local/bin')
export UNITS := env("UNITS", env("HOME") + '/.config/systemd/user')
# The source font is only used by the explicit loading atlas generator.
export FONT_SOURCE := env("FONT_SOURCE", 'assets/fonts/clacon2.ttf')
# Optional recipe overrides; binaries own default paths and mode selection.
export LOG := env("LOG", '')
export PROFILE := env("PROFILE", '')
export SLOPCAR_PORT := env("SLOPCAR_PORT", '7718')
export SLOPCAR_CONTAINER := env("SLOPCAR_CONTAINER", 'slopcar')
# Resolve from this shared file for every platform namespace.
export SLOPCAR_WORKSPACE := env("SLOPCAR_WORKSPACE", absolute_path(source_directory() / '..'))

# Which half of the split every build, install and run target follows.
export BUILD := env("BUILD", 'debug')
build_mode := if BUILD =~ '^(debug|release)$' { BUILD } else { error('BUILD must be exactly debug or release; got ' + BUILD) }
export CARGOFLAGS := if build_mode == 'release' { '--release' } else { '' }
export SLOPCAR := env("SLOPCAR", 'slopcar/target/' + build_mode + '/slopcar')
export TARGET := 'slopd/target/' + build_mode
export RUNNER := TARGET + '/slopworld'
export SLOPCTL := TARGET + '/slopctl'
export CARGO := env("CARGO", 'cargo')
# Share eligible compiler results while retaining per-checkout Cargo build state.
# An explicit wrapper (including an empty value) takes precedence.
export RUSTC_WRAPPER := env("RUSTC_WRAPPER", `command -v sccache || true`)
export SCCACHE_DIR := env("SCCACHE_DIR", env("HOME") + '/.cache/sccache')
export DOTNET := env("DOTNET", 'dotnet')
export UV := env("UV", 'uv')
export PYTHON := env("PYTHON", UV + ' run --locked python')
export MOD_PROJECT := env("MOD_PROJECT", 'mod/Source/SlopWorld/SlopWorld.csproj')
export MOD_WARNINGS_AS_ERRORS := env("MOD_WARNINGS_AS_ERRORS", 'true')
# Disable only when intentionally regenerating the runtime package lock.
export MOD_DEPS_LOCKED := env("MOD_DEPS_LOCKED", 'true')
export MOD_DLL := env("MOD_DLL", 'mod/Assemblies/SlopWorld.dll')
export TEST_PROJECT := env("TEST_PROJECT", 'mod/Tests/SlopWorld.Tests.csproj')
export TEST_DLL := env("TEST_DLL", 'mod/Tests/bin/Release/net8.0/SlopWorld.Tests.dll')
export COVERAGE_DIR := env("COVERAGE_DIR", 'coverage')
# Native llvm-cov file filter. Test modules live in adjacent excluded files.
export RUST_COVERAGE_EXCLUDE := env("RUST_COVERAGE_EXCLUDE", '/tests/|/benches/|/benchmark/|/(tests|test_support|test_http|http_fixture|benchmark|[^/]*_tests)\.rs$|/shared/(http_wire|protocol)\.rs$|/target/|/rustc[^/]*/library/')

# Shared test and version overrides.
export TEST_ARGS := env("TEST_ARGS", "")
export VERSION := env("VERSION", "")

# Root exports are inherited by module recipe processes, including CLI overrides.
export JUST_CMD := just_executable()
export JUST_ROOT := justfile_directory() / 'justfile'
export CSHARP_FORMAT_FOLDERS := env("CSHARP_FORMAT_FOLDERS", 'mod/Source/SlopWorld mod/Tests bench/ipc/csharp bench/terminal-input/tests/http_transport')
export PYTHON_TOOLS := env("PYTHON_TOOLS", 'tools bench/runner.py bench/test_runner.py mac slopcar/*.py')
