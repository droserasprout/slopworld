# Shared settings and script environment. Domain files own recipes.
# Export settings so recursive just calls retain command-line overrides.

export GOGDL := env("GOGDL", 'gogdl')
config_home := if env("XDG_CONFIG_HOME", "") != '' { env("XDG_CONFIG_HOME") } else { env("HOME") + '/.config' }
data_home := if env("XDG_DATA_HOME", "") != '' { env("XDG_DATA_HOME") } else { env("HOME") + '/.local/share' }

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
export FONT_DIR := env("FONT_DIR", data_home + '/fonts')
FONT_DEST := FONT_DIR + '/' + file_name(FONT_SOURCE)
export LOG := env("LOG", env("HOME") + '/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log')
# Save data folder, defaults to `$XDG_DATA_HOME/slopworld/profile`.
export PROFILE := env("PROFILE", '')
# Capture launcher environment overrides before the sidecar just defaults below.
trace_profile_env := if env("SLOPCAR_PROFILE", "") != "" { env("SLOPCAR_PROFILE") } else { env("SLOPWORLD_PROFILE", "") }
export TRACE_LOG := env("TRACE_LOG", (if PROFILE != '' { PROFILE } else if trace_profile_env != '' { trace_profile_env } else { data_home + '/slopworld/profile' }) + '/SlopWorld-trace.log')

# `sidecar-run` wiring: the sidecar's config dir holds the endpoint descriptor its daemon
# writes (url http://127.0.0.1:7718 + token, bind-mounted to the host), and the game runs into
# a profile kept wholly apart from the native one. Point SLOPCAR_CONFIG at whatever
# SLOPCAR_CONFIG_DIR the sidecar was started with. SLOPCAR_DATA keeps its session state separate
# from a native daemon.
export SLOPCAR_CONFIG := env("SLOPCAR_CONFIG", config_home + '/slopworld-car')
export SLOPCAR_ENDPOINT := env("SLOPCAR_ENDPOINT", SLOPCAR_CONFIG + '/endpoint.toml')
export SLOPCAR_PORT := env("SLOPCAR_PORT", '7718')
export SLOPCAR_DATA := env("SLOPCAR_DATA", data_home + '/slopworld-car')
export SLOPCAR_CONTAINER := env("SLOPCAR_CONTAINER", 'slopcar')
# Keep the default out of native launcher environments. Sidecar recipes set it explicitly.
SLOPCAR_PROFILE := env("SLOPCAR_PROFILE", data_home + '/slopworld-car/profile')
export SLOPCAR_WORKSPACE := env("SLOPCAR_WORKSPACE", justfile_directory())
export SLOPCAR_START_ARGS := env("SLOPCAR_START_ARGS", '--workspace "' + SLOPCAR_WORKSPACE + '"')
export SLOPCAR := env("SLOPCAR", 'slopcar/slopcar')

# Native macOS RimWorld is an app bundle, so it needs its own references, mod destination and
# direct game executable. The GOG path is the useful default. Every value is overridable for a
# Steam or standalone install, or a friend whose home layout differs.
export MAC_RIMWORLD := env("MAC_RIMWORLD", env("HOME") + '/Documents/RimWorld.app')
export MAC_GAME := env("MAC_GAME", MAC_RIMWORLD + '/Contents/MacOS/RimWorld by Ludeon Studios')
export MAC_RESOURCES := env("MAC_RESOURCES", MAC_RIMWORLD + '/Contents/Resources')
export MAC_MANAGED := env("MAC_MANAGED", MAC_RESOURCES + '/Data/Managed')
export MAC_MODS := env("MAC_MODS", MAC_RIMWORLD + '/Mods')
export MAC_PROFILE := env("MAC_PROFILE", env("HOME") + '/Library/Application Support/SlopWorld/sidecar-profile')
export MAC_GAME_ARGS := env("MAC_GAME_ARGS", '')

# Which half of the split every build, install and run target follows.
export BUILD := env("BUILD", 'debug')
build_mode := if BUILD =~ '^(debug|release)$' { BUILD } else { error('BUILD must be exactly debug or release; got ' + BUILD) }
export CARGOFLAGS := if build_mode == 'release' { '--release' } else { '' }
TARGET := 'slopd/target/' + build_mode
RUNNER := TARGET + '/slopworld'
SLOPCTL := TARGET + '/slopctl'
export CARGO := env("CARGO", 'cargo')
export DOTNET := env("DOTNET", 'dotnet')
export PYTHON := env("PYTHON", 'python3')
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
SLOPCAR_ENV := 'SLOPCAR_CONFIG_DIR=' + quote(SLOPCAR_CONFIG) + ' SLOPCAR_DATA_DIR=' + quote(SLOPCAR_DATA) + ' SLOPCAR_PORT=' + quote(SLOPCAR_PORT) + ' SLOPCAR_CONTAINER=' + quote(SLOPCAR_CONTAINER)
