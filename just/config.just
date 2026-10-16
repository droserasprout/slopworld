# Shared settings and script environment. Domain files own recipes.
# Export user settings to component recipes and workflow scripts.

export RIMWORLD := env("RIMWORLD", GOGDL_PATH + '/RimWorld/game')
export MANAGED := env("MANAGED", RIMWORLD + '/RimWorldLinux_Data/Managed')
export MODS := env("MODS", RIMWORLD + '/Mods')
export BIN := env("BIN", env("HOME") + '/.local/bin')
export UNITS := env("UNITS", env("HOME") + '/.config/systemd/user')
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
# Share eligible compiler results while retaining per-checkout Cargo build state.
# An explicit wrapper (including an empty value) takes precedence.
export RUSTC_WRAPPER := env("RUSTC_WRAPPER", `command -v sccache || true`)
export SCCACHE_DIR := env("SCCACHE_DIR", env("HOME") + '/.cache/sccache')
export COVERAGE_DIR := env("COVERAGE_DIR", 'coverage')
# Shared version override.
export VERSION := env("VERSION", "")

# Workflow scripts invoke Just through this executable.
export JUST_CMD := just_executable()
