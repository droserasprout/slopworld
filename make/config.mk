# Shared paths and tool settings. Targets live in the domain-specific files.

GOGDL      ?= gogdl
config_home := $(or $(XDG_CONFIG_HOME),$(HOME)/.config)
data_home   := $(or $(XDG_DATA_HOME),$(HOME)/.local/share)

GOGDL_AUTH ?= $(config_home)/heroic/gog_store/auth.json
GOGDL_ID   ?= 1094900565
GOGDL_PATH ?= $(HOME)/GOG Games
GOGDL_LOGIN_URL ?= https://auth.gog.com/auth?client_id=46899977096215655&redirect_uri=https%3A%2F%2Fembed.gog.com%2Fon_login_success%3Forigin%3Dclient&response_type=code&layout=client2
RIMWORLD   ?= $(GOGDL_PATH)/RimWorld/game
MANAGED    ?= $(RIMWORLD)/RimWorldLinux_Data/Managed
MODS       ?= $(RIMWORLD)/Mods
BIN        ?= $(HOME)/.local/bin
UNITS      ?= $(HOME)/.config/systemd/user
FONT_SOURCE ?= assets/fonts/clacon2.ttf
FONT_DIR   ?= $(data_home)/fonts
FONT_DEST  := $(FONT_DIR)/$(notdir $(FONT_SOURCE))
LOG        ?= $(HOME)/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log
# Save data folder, defaults to `$XDG_DATA_HOME/slopworld/profile`.
PROFILE    ?=

# `sidecar-run` wiring: the sidecar's config dir holds the endpoint descriptor its daemon
# writes (url http://127.0.0.1:7718 + token, bind-mounted to the host), and the game runs into
# a profile kept wholly apart from the native one. Point SLOPCAR_CONFIG at whatever
# SLOPCAR_CONFIG_DIR the sidecar was started with. SLOPCAR_DATA keeps its session state separate
# from a native daemon.
SLOPCAR_CONFIG   ?= $(config_home)/slopworld-car
SLOPCAR_ENDPOINT ?= $(SLOPCAR_CONFIG)/endpoint.toml
SLOPCAR_PORT     ?= 7718
SLOPCAR_DATA     ?= $(data_home)/slopworld-car
SLOPCAR_CONTAINER ?= slopcar
SLOPCAR_PROFILE  ?= $(data_home)/slopworld-car/profile
SLOPCAR_WORKSPACE ?= $(CURDIR)
SLOPCAR_START_ARGS ?= --workspace "$(SLOPCAR_WORKSPACE)"
SLOPCAR ?= slopcar/slopcar
SLOPCAR_ENV = \
	SLOPCAR_CONFIG_DIR="$(SLOPCAR_CONFIG)" \
	SLOPCAR_DATA_DIR="$(SLOPCAR_DATA)" \
	SLOPCAR_PORT="$(SLOPCAR_PORT)" \
	SLOPCAR_CONTAINER="$(SLOPCAR_CONTAINER)"

# Native macOS RimWorld is an app bundle, so it needs its own references, mod destination and
# direct game executable. The GOG path is the useful default. Every value is overridable for a
# Steam or standalone install, or a friend whose home layout differs.
MAC_RIMWORLD    ?= $(HOME)/Documents/RimWorld.app
MAC_GAME        ?= $(MAC_RIMWORLD)/Contents/MacOS/RimWorld by Ludeon Studios
MAC_RESOURCES   ?= $(MAC_RIMWORLD)/Contents/Resources
MAC_MANAGED     ?= $(MAC_RESOURCES)/Data/Managed
MAC_MODS        ?= $(MAC_RIMWORLD)/Mods
MAC_PROFILE     ?= $(HOME)/Library/Application Support/SlopWorld/sidecar-profile
MAC_GAME_ARGS   ?=

# Which half of the split every build, install and run target follows.
BUILD      ?= debug
ifneq ($(words $(BUILD)),1)
$(error BUILD must be exactly debug or release; got '$(BUILD)')
endif
ifneq ($(filter debug release,$(BUILD)),$(BUILD))
$(error BUILD must be exactly debug or release; got '$(BUILD)')
endif
CARGOFLAGS  = $(if $(filter release,$(BUILD)),--release)
TARGET      = slopd/target/$(BUILD)
RUNNER      = $(TARGET)/slopworld
SLOPCTL     = $(TARGET)/slopctl
CARGO       ?= cargo
DOTNET      ?= dotnet
PYTHON      ?= python3
MOD_PROJECT  := mod/Source/SlopWorld/SlopWorld.csproj
MOD_WARNINGS_AS_ERRORS ?= false
MOD_DLL      := mod/Assemblies/SlopWorld.dll
TEST_PROJECT := mod/Tests/SlopWorld.Tests.csproj
TEST_DLL     := mod/Tests/bin/Release/net8.0/SlopWorld.Tests.dll
COVERAGE_DIR := coverage
# Native llvm-cov file filter. Test modules live in adjacent excluded files.
RUST_COVERAGE_EXCLUDE ?= /tests/|/benches/|/benchmark/|/(tests|test_support|benchmark|[^/]*_tests)\.rs$$|/shared/(http_wire|protocol)\.rs$$|/target/|/rustc[^/]*/library/

# Settings passed to maintenance scripts.
export BUILD CARGO CARGOFLAGS COVERAGE_DIR DOTNET PYTHON
export RUST_COVERAGE_EXCLUDE
export GOGDL GOGDL_AUTH GOGDL_ID GOGDL_LOGIN_URL GOGDL_PATH RIMWORLD
export MAC_GAME MAC_MANAGED MAC_MODS TEST_PROJECT TEST_DLL
