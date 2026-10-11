# Shared settings and script environment. Domain files own recipes.
# Export settings so recursive just calls retain command-line overrides.

export GOGDL := env("GOGDL", 'gogdl')
config_home := if env("XDG_CONFIG_HOME", "") != '' { env("XDG_CONFIG_HOME") } else { env("HOME") + '/.config' }
export GOGDL_AUTH := env("GOGDL_AUTH", config_home + '/heroic/gog_store/auth.json')
export GOGDL_ID := env("GOGDL_ID", '1094900565')
export GOGDL_PATH := env("GOGDL_PATH", env("HOME") + '/GOG Games')
export GOGDL_LOGIN_URL := env("GOGDL_LOGIN_URL", 'https://auth.gog.com/auth?client_id=46899977096215655&redirect_uri=https%3A%2F%2Fembed.gog.com%2Fon_login_success%3Forigin%3Dclient&response_type=code&layout=client2')
export RIMWORLD := env("RIMWORLD", GOGDL_PATH + '/RimWorld/game')
export MANAGED := env("MANAGED", RIMWORLD + '/RimWorldLinux_Data/Managed')
export MODS := env("MODS", RIMWORLD + '/Mods')
export BIN := env("BIN", env("HOME") + '/.local/bin')
export UNITS := env("UNITS", env("HOME") + '/.config/systemd/user')
# The source font is only used to regenerate the loading atlas; destination paths support
# uninstalling a per-user copy installed by an older SlopWorld release.
export FONT_SOURCE := env("FONT_SOURCE", 'assets/fonts/clacon2.ttf')
# Optional recipe overrides; binaries own default paths and mode selection.
export LOG := env("LOG", '')
export PROFILE := env("PROFILE", '')
export TRACE_LOG := env("TRACE_LOG", '')
export SLOPCAR_PORT := env("SLOPCAR_PORT", '7718')
export SLOPCAR_CONTAINER := env("SLOPCAR_CONTAINER", 'slopcar')
# Resolve from this shared file so the macOS justfile uses the same workspace.
export SLOPCAR_WORKSPACE := env("SLOPCAR_WORKSPACE", absolute_path(source_directory() / '..'))
export SLOPCAR_START_ARGS := env("SLOPCAR_START_ARGS", '--workspace "' + SLOPCAR_WORKSPACE + '"')

# Which half of the split every build, install and run target follows.
export BUILD := env("BUILD", 'debug')
build_mode := if BUILD =~ '^(debug|release)$' { BUILD } else { error('BUILD must be exactly debug or release; got ' + BUILD) }
export CARGOFLAGS := if build_mode == 'release' { '--release' } else { '' }
export SLOPCAR := env("SLOPCAR", 'slopcar/target/' + build_mode + '/slopcar')
TARGET := 'slopd/target/' + build_mode
export RUNNER := TARGET + '/slopworld'
SLOPCTL := TARGET + '/slopctl'
export CARGO := env("CARGO", 'cargo')
# Share eligible compiler results while retaining per-checkout Cargo build state.
# An explicit wrapper (including an empty value) takes precedence.
export RUSTC_WRAPPER := env("RUSTC_WRAPPER", `command -v sccache || true`)
export SCCACHE_DIR := env("SCCACHE_DIR", env("HOME") + '/.cache/sccache')
export DOTNET := env("DOTNET", 'dotnet')
export UV := env("UV", 'uv')
export PYTHON := env("PYTHON", UV + ' run --locked python')
MOD_PROJECT := 'mod/Source/SlopWorld/SlopWorld.csproj'
export MOD_WARNINGS_AS_ERRORS := env("MOD_WARNINGS_AS_ERRORS", 'true')
# Disable only when intentionally regenerating the runtime package lock.
export MOD_DEPS_LOCKED := env("MOD_DEPS_LOCKED", 'true')
MOD_DLL := 'mod/Assemblies/SlopWorld.dll'
export TEST_PROJECT := 'mod/Tests/SlopWorld.Tests.csproj'
export TEST_DLL := 'mod/Tests/bin/Release/net8.0/SlopWorld.Tests.dll'
export COVERAGE_DIR := 'coverage'
# Native llvm-cov file filter. Test modules live in adjacent excluded files.
export RUST_COVERAGE_EXCLUDE := env("RUST_COVERAGE_EXCLUDE", '/tests/|/benches/|/benchmark/|/(tests|test_support|test_http|http_fixture|benchmark|[^/]*_tests)\.rs$|/shared/(http_wire|protocol)\.rs$|/target/|/rustc[^/]*/library/')

# Settings passed to maintenance scripts.
export BENCH_BACKEND := env("BENCH_BACKEND", "")
export BENCH_BASELINE := env("BENCH_BASELINE", "")
export BENCH_FALLBACK_RUN := env("BENCH_FALLBACK_RUN", "")
export BENCH_FILL_HISTORY := env("BENCH_FILL_HISTORY", "")
export BENCH_MODE := env("BENCH_MODE", "")
export BENCH_MULTIPLIER := env("BENCH_MULTIPLIER", "")
export BENCH_PHASE := env("BENCH_PHASE", "")
export BENCH_PREPARE_SECONDS := env("BENCH_PREPARE_SECONDS", "")
export BENCH_REPORT_OUTPUT := env("BENCH_REPORT_OUTPUT", "")
export BENCH_RUN := env("BENCH_RUN", "")
export BENCH_TRACE_LOG := env("BENCH_TRACE_LOG", "")
export LOG_ARGS := env("LOG_ARGS", "")
export TEST_ARGS := env("TEST_ARGS", "")
export VERSION := env("VERSION", "")
export CONTAINER_BUILD_ARGS := env("CONTAINER_BUILD_ARGS", '')
export CONTAINER_RUN_ARGS := env("CONTAINER_RUN_ARGS", '')
export CONTAINER := env("CONTAINER", 'podman')
export GH := env("GH", 'gh')
export RELEASE_DIR := env("RELEASE_DIR", '')
export DEB_DIR := env("DEB_DIR", 'dist/debian')
export RPM_DIR := env("RPM_DIR", 'dist/fedora')
