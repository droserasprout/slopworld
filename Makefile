.DEFAULT_GOAL := help

.PHONY: \
	help all daemon mod debug release daemon-debug daemon-release mod-debug mod-release \
	test test-daemon test-mod coverage coverage-daemon coverage-mod test-prose \
	appicon icons emoji-atlas reference scheme-report harmony clean \
	format format-daemon format-mod lint lint-daemon lint-mod lint-prose \
	install install-daemon install-runner install-mod mac-setup mac-mod mac-install mac-profile \
	uninstall uninstall-daemon uninstall-runner uninstall-mod \
	run run-slopcar mac-run mac install-mac run-mac \
	logs check-reqs slopcar-build slopcar-doctor mac-docker-check mac-game-check \
	mac-check mac-sidecar-build mac-sidecar-doctor mac-sidecar-start \
	mac-sidecar-ready mac-sidecar-stop mac-sidecar-status mac-sidecar-logs mac-mod-check \
	shot pkg-arch docs docs-serve devloop devloop-sidecar \
	gogdl-login gogdl-install gogdl-update
MAKEFLAGS += --no-print-directory
MAKE_BIN := $(MAKE)
##
##  🤖 SlopWorld developer tools
##
##  RIMWORLD must point at a real install; the mod builds against the game's
##  own assemblies. PROFILE picks the save data folder `run` launches into.
##  BUILD is debug or release and every target follows it, install included:
##  `make BUILD=release install`.
##
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
LOG        ?= $(HOME)/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log
# Save data folder, defaults to `$XDG_DATA_HOME/slopworld/profile`.
PROFILE    ?=
# `run-slopcar` wiring: the sidecar's config dir holds the endpoint descriptor its daemon
# writes (url http://127.0.0.1:7718 + token, bind-mounted to the host), and the game runs into
# a profile kept wholly apart from the native one. Point SLOPCAR_CONFIG at whatever
# SLOPCAR_CONFIG_DIR the sidecar was started with; SLOPCAR_DATA keeps its session state separate
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
# direct game executable. The GOG path is the useful default; every value is overridable for a
# Steam or standalone install, or a friend whose home layout differs.
MAC_RIMWORLD    ?= $(HOME)/Documents/RimWorld.app
MAC_GAME        ?= $(MAC_RIMWORLD)/Contents/MacOS/RimWorld by Ludeon Studios
MAC_RESOURCES   ?= $(MAC_RIMWORLD)/Contents/Resources
MAC_MANAGED     ?= $(MAC_RESOURCES)/Data/Managed
MAC_MODS        ?= $(MAC_RIMWORLD)/Mods
MAC_PROFILE     ?= $(HOME)/Library/Application Support/SlopWorld/sidecar-profile
MAC_CSC         ?= csc
MAC_MONO_PREFIX := $(shell command -v brew >/dev/null 2>&1 && brew --prefix mono 2>/dev/null)
MAC_CSC_API     ?= $(if $(MAC_MONO_PREFIX),$(MAC_MONO_PREFIX)/lib/mono/4.7.2-api,$(CSC_API))
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
CSC         ?= csc
CSC_API     ?= /usr/lib/mono/4.7.2-api
PACKAGE_VERSION := $(shell sed -n 's/^version = "\([^"]*\)"/\1/p' slopd/Cargo.toml | head -n1)
VERSION         := $(shell tools/version.sh "$(PACKAGE_VERSION)")
CSC_SOURCES = $(shell find mod/Source/SlopWorld -type f -name '*.cs' -not -path '*/obj/*' -print | sort)
CSC_REFS    = \
	-r:"$(CSC_API)/mscorlib.dll" \
	-r:"$(CSC_API)/Facades/netstandard.dll" \
	-r:"$(CSC_API)/System.dll" \
	-r:"$(CSC_API)/System.Core.dll" \
	-r:"$(CSC_API)/System.Xml.dll" \
	-r:mod/Assemblies/0Harmony.dll \
	-r:mod/Assemblies/Markdig.dll \
	-r:"$(MANAGED)/Assembly-CSharp.dll" \
	-r:"$(MANAGED)/UnityEngine.CoreModule.dll" \
	-r:"$(MANAGED)/UnityEngine.IMGUIModule.dll" \
	-r:"$(MANAGED)/UnityEngine.TextRenderingModule.dll" \
	-r:"$(MANAGED)/UnityEngine.InputLegacyModule.dll" \
	-r:"$(MANAGED)/UnityEngine.ImageConversionModule.dll"
CSC_OPTIMIZE = $(if $(filter release,$(BUILD)),-optimize+,)
CSC_WARNINGS ?=
MOD_DLL      := mod/Assemblies/SlopWorld.dll
MOD_ASSEMBLY_INFO := mod/Source/SlopWorld/obj/AssemblyInfo.cs
MOD_INSTALL  = $(MODS)/SlopWorld
MOD_DIRS     := About Defs Patches Sounds Textures Assemblies
TEST_PROJECT := mod/Tests/SlopWorld.Tests.csproj
TEST_DLL     := mod/Tests/bin/Release/net8.0/SlopWorld.Tests.dll
COVERAGE_DIR := coverage


help:              ## Show this help (default)
	@awk 'BEGIN { tag = "#" "#" } index($$0, tag) == 1 { if (substr($$0, 3, 2) == "->") print "->" substr($$0, 5); else if (substr($$0, 3) != "") print substr($$0, 3); next } /^[[:alnum:]_.-]+:/ && index($$0, tag) { target = $$0; sub(/:.*/, "", target); desc = substr($$0, index($$0, tag) + 2); sub(/^[[:space:]]+/, "", desc); printf "%-18s %s\n", target ":", desc }' $(MAKEFILE_LIST)

##
##-> RimWorld
##

gogdl-login:       ## Log into GOG interactively and save the gogdl token
	@mkdir -p "$(dir $(GOGDL_AUTH))"; \
	if command -v xdg-open >/dev/null 2>&1; then \
		xdg-open "$(GOGDL_LOGIN_URL)" >/dev/null 2>&1 || true; \
	fi; \
	echo "Log into GOG in your browser, then paste the authorization code here."; \
	echo "If the browser did not open, visit: $(GOGDL_LOGIN_URL)"; \
	printf "Authorization code: " >&2; \
	read -r code; \
	test -n "$$code" || { echo "authorization code is empty" >&2; exit 1; }; \
	$(GOGDL) --auth-config-path "$(GOGDL_AUTH)" auth --code "$$code"; \
	test -s "$(GOGDL_AUTH)" || { echo "gogdl did not save credentials to $(GOGDL_AUTH)" >&2; exit 1; }

gogdl-install:     ## Install RimWorld's native Linux build with gogdl
	@test -s "$(GOGDL_AUTH)" || { echo "missing gogdl login at $(GOGDL_AUTH); run make gogdl-login" >&2; exit 1; }
	@mkdir -p "$(GOGDL_PATH)" "$(dir $(GOGDL_AUTH))"
	$(GOGDL) --auth-config-path "$(GOGDL_AUTH)" download "$(GOGDL_ID)" \
		--path "$(GOGDL_PATH)" --platform linux --with-dlcs

gogdl-update:      ## Update the local RimWorld copy with gogdl
	@test -x "$(RIMWORLD)/RimWorldLinux" || { echo "missing RimWorld at $(RIMWORLD); run make gogdl-install or set RIMWORLD" >&2; exit 1; }
	@test -s "$(GOGDL_AUTH)" || { echo "missing gogdl login at $(GOGDL_AUTH); run make gogdl-login" >&2; exit 1; }
	@mkdir -p "$(dir $(GOGDL_AUTH))"
	$(GOGDL) --auth-config-path "$(GOGDL_AUTH)" update "$(GOGDL_ID)" \
		--path "$(RIMWORLD)" --platform linux --with-dlcs

##
##-> macOS sidecar
##

mac-setup:        ## Install the macOS build tools and Docker Desktop with Homebrew
	@test "$$(uname -s)" = Darwin || { echo "mac-setup must run on macOS" >&2; exit 1; }
	@command -v brew >/dev/null 2>&1 || { echo "missing Homebrew" >&2; exit 1; }
	brew install git make mono
	brew install --cask docker-desktop
	@echo "Use GNU Make as gmake on macOS. Open Docker Desktop once, then run: gmake mac-check"

mac-docker-check: ## Check Docker Desktop on macOS
	@test "$$(uname -s)" = Darwin || { echo "macOS target requires Darwin" >&2; exit 1; }
	@command -v docker >/dev/null 2>&1 || { echo "missing docker; run gmake mac-setup" >&2; exit 1; }
	@docker info >/dev/null 2>&1 || { echo "Docker Desktop is not running; open Docker and retry" >&2; exit 1; }

mac-game-check:   ## Check the native macOS RimWorld and Mono paths
	@test "$$(uname -s)" = Darwin || { echo "macOS target requires Darwin" >&2; exit 1; }
	@command -v "$(MAC_CSC)" >/dev/null 2>&1 || { echo "missing $(MAC_CSC); run gmake mac-setup" >&2; exit 1; }
	@test -f "$(MAC_CSC_API)/mscorlib.dll" || { echo "missing Mono reference assemblies under $(MAC_CSC_API); override MAC_CSC_API" >&2; exit 1; }
	@test -x "$(MAC_GAME)" || { echo "missing native RimWorld executable: $(MAC_GAME); override MAC_RIMWORLD or MAC_GAME" >&2; exit 1; }
	@test -f "$(MAC_MANAGED)/Assembly-CSharp.dll" || { echo "missing RimWorld assemblies under $(MAC_MANAGED)" >&2; exit 1; }
	@test -d "$(MAC_MODS)" || { echo "missing RimWorld Mods directory: $(MAC_MODS); override MAC_RIMWORLD" >&2; exit 1; }

mac-check: mac-docker-check mac-game-check ## Check Docker, Mono and the native macOS RimWorld install


mac-sidecar-build: mac-docker-check ## Build the Linux sidecar image on macOS
	$(SLOPCAR) build


mac-sidecar-doctor: mac-sidecar-build ## Verify nested Bubblewrap, pasta and tmux on macOS
	$(SLOPCAR) doctor

mac-sidecar-start: mac-docker-check ## Start the configured macOS sidecar, reusing its container
	$(SLOPCAR_ENV) tools/mac-sidecar-start.sh $(SLOPCAR_START_ARGS)

mac-sidecar-stop: mac-docker-check ## Stop the macOS sidecar without removing its state
	$(SLOPCAR_ENV) $(SLOPCAR) stop

mac-sidecar-status: mac-docker-check ## Show macOS sidecar status
	$(SLOPCAR_ENV) $(SLOPCAR) status

mac-sidecar-logs: mac-docker-check ## Show macOS sidecar logs; pass LOG_ARGS='--tail 100'
	$(SLOPCAR_ENV) $(SLOPCAR) logs $(LOG_ARGS)

##
##-> Build
##

all: daemon mod   ## Build both halves

daemon:            ## Build the daemon and the launcher
	cd slopd && SLOPWORLD_BUILD_VERSION="$(VERSION)" $(CARGO) build $(CARGOFLAGS)

mod:               ## Build the mod against the game's assemblies
	@test -f "$(CSC_API)/mscorlib.dll" || { echo "missing Mono reference assemblies under $(CSC_API)" >&2; exit 1; }
	@test -f "$(MANAGED)/Assembly-CSharp.dll" || { echo "missing RimWorld assemblies under $(MANAGED)" >&2; exit 1; }
	@mkdir -p "$(dir $(MOD_ASSEMBLY_INFO))"
	@{ \
		printf '%s\n' \
			'using System.Reflection;' \
			'[assembly: AssemblyInformationalVersion("$(VERSION)")]'; \
	} > "$(MOD_ASSEMBLY_INFO)"
	$(CSC) -nologo -noconfig -target:library -langversion:latest \
		-out:"$(MOD_DLL)" $(CSC_OPTIMIZE) $(CSC_WARNINGS) \
		$(CSC_REFS) "$(MOD_ASSEMBLY_INFO)" $(CSC_SOURCES)

##

debug:             ## Alias for BUILD=debug all
	$(MAKE) BUILD=debug all

release:           ## Alias for BUILD=release all
	$(MAKE) BUILD=release all

daemon-debug:      ## Alias for BUILD=debug daemon
	$(MAKE) BUILD=debug daemon

daemon-release:    ## Alias for BUILD=release daemon
	$(MAKE) BUILD=release daemon

mod-debug:         ## Alias for BUILD=debug mod
	$(MAKE) BUILD=debug mod

mod-release:       ## Alias for BUILD=release mod
	$(MAKE) BUILD=release mod

test: test-daemon test-mod test-prose ## Run the daemon and game-free mod tests

test-daemon:
	cd slopd && $(CARGO) test

test-mod:
	$(DOTNET) run --project "$(TEST_PROJECT)" --configuration Release

coverage: coverage-daemon coverage-mod ## Measure Rust and game-free C# test coverage

coverage-daemon:   ## Write Rust coverage to coverage/rust.cobertura.xml
	@command -v cargo-llvm-cov >/dev/null || { echo "missing cargo-llvm-cov; install it with: cargo install cargo-llvm-cov --locked" >&2; exit 1; }
	@command -v llvm-cov >/dev/null && command -v llvm-profdata >/dev/null || { echo "missing LLVM coverage tools" >&2; exit 1; }
	@mkdir -p "$(COVERAGE_DIR)"
	cd slopd && LLVM_COV="$$(command -v llvm-cov)" LLVM_PROFDATA="$$(command -v llvm-profdata)" \
		$(CARGO) llvm-cov --cobertura --output-path "../$(COVERAGE_DIR)/rust.cobertura.xml"
	$(PYTHON) tools/coverage_summary.py "$(COVERAGE_DIR)/rust.cobertura.xml" Rust

coverage-mod:      ## Write game-free C# coverage to coverage/csharp.cobertura.xml
	$(DOTNET) tool restore
	@mkdir -p "$(COVERAGE_DIR)"
	$(DOTNET) build "$(TEST_PROJECT)" --configuration Release -p:Coverage=true
	$(DOTNET) tool run coverlet -- "$(TEST_DLL)" \
		--target dotnet --targetargs "$(TEST_DLL)" \
		--include-test-assembly --exclude-by-file '**/mod/Tests/**/*.cs' \
		--format cobertura --output "$(COVERAGE_DIR)/csharp.cobertura.xml"
	$(PYTHON) tools/coverage_summary.py "$(COVERAGE_DIR)/csharp.cobertura.xml" C\#

test-prose:        ## Test the prose linter
	$(PYTHON) tools/test_prose_lint.py

appicon:           ## Regenerate the app icon (robot face + wilted rose)
	$(PYTHON) tools/appicon.py

icons:             ## Rebake the action icons from a Nerd Font's Codicons
	$(PYTHON) tools/icons.py

emoji-atlas:       ## Rebake the legacy terminal's emoji atlas with Pango
	$(PYTHON) tools/emoji_atlas.py

reference:         ## Generate the environment/API/CLI reference
	$(PYTHON) tools/reference.py

scheme-report:     ## Analyze the complete UI schemes and check Warm's luminance hierarchy
	$(PYTHON) tools/analyze_ui_schemes.py --check-warm

harmony:           ## Fetch the latest Harmony release into the mod
	tools/fetch-harmony.sh

clean:             ## Drop build output
	cd slopd && $(CARGO) clean
	rm -f "$(MOD_DLL)"
	rm -rf mod/Source/SlopWorld/obj
	rm -rf "$(COVERAGE_DIR)"

##
##-> Format and lint
##

format: format-daemon format-mod ## Format both halves

format-daemon:     ## rustfmt the daemon
	cd slopd && $(CARGO) fmt

format-mod:        ## Format the mod's C# (needs the .NET SDK)
	$(DOTNET) format whitespace mod/Source/SlopWorld --folder --exclude obj

##

lint: lint-daemon lint-mod ## Lint both halves

lint-daemon:       ## Check the daemon's formatting, then clippy, warnings as errors
	cd slopd && $(CARGO) fmt --check
	cd slopd && $(CARGO) clippy --all-targets -- -D warnings

lint-mod: override BUILD := release
lint-mod: override CSC_WARNINGS := -warnaserror
lint-mod: mod       ## Build the mod with warnings as errors, then check its formatting
	$(DOTNET) format whitespace mod/Source/SlopWorld --folder --exclude obj --verify-no-changes

lint-prose:        ## Find LLM cliches in prose and source comments
	$(PYTHON) tools/prose_lint.py $(PROSE_LINT_ARGS)

##
##-> Install
##

install: install-daemon install-runner install-mod ## Install all three

install-daemon: daemon ## Install the binary and the unit, restarting only when needed
	TARGET="$(TARGET)" BIN="$(BIN)" UNITS="$(UNITS)" BUILD="$(BUILD)" \
		tools/install-daemon.sh

install-runner: daemon ## Install the launcher beside the daemon
	install -Dm755 "$(RUNNER)" "$(BIN)/slopworld"
	@echo "installed to $(BIN)/slopworld"

install-mod: mod       ## Install the mod into the game's Mods folder
	@test -n "$(MODS)" && test "$(MODS)" != / || { echo "MODS is empty or unsafe, refusing to remove anything"; exit 1; }
	rm -rf "$(MOD_INSTALL)"
	mkdir -p "$(MOD_INSTALL)"
	cp -r $(MOD_DIRS:%=mod/%) "$(MOD_INSTALL)/"
	@echo "installed to $(MOD_INSTALL)"

mac-mod: mac-game-check mod ## Build SlopWorld.dll against native macOS RimWorld
mac-mod: override CSC := $(MAC_CSC)
mac-mod: override CSC_API := $(MAC_CSC_API)
mac-mod: override MANAGED := $(MAC_MANAGED)

mac-install: mac-game-check mac-sidecar-doctor install-mod ## Build the sidecar and install the mod into native macOS RimWorld
mac-install: override CSC := $(MAC_CSC)
mac-install: override CSC_API := $(MAC_CSC_API)
mac-install: override MANAGED := $(MAC_MANAGED)
mac-install: override MODS := $(MAC_MODS)

mac-profile:      ## Create the isolated native macOS sidecar profile if it is absent
	MAC_PROFILE="$(MAC_PROFILE)" tools/mac-profile.sh

##

uninstall: uninstall-daemon uninstall-runner uninstall-mod ## Remove all three, keeping config and saves
	@echo "left alone: ~/.config/slopworld, the profile (saves), any tmux server"
	@echo "under the slopworld socket; \`tmux -L slopworld kill-server\` ends the agents."

uninstall-daemon:  ## Stop the service, remove the binary and the unit
	-systemctl --user disable --now slopd.service
	rm -f "$(UNITS)/slopd.service"
	rm -f "$(BIN)/slopd"
	rm -f "$(BIN)/slopctl"
	systemctl --user daemon-reload
	@echo "removed $(BIN)/slopd, $(BIN)/slopctl and $(UNITS)/slopd.service"

uninstall-runner:  ## Remove the launcher
	rm -f "$(BIN)/slopworld"
	@echo "removed $(BIN)/slopworld"

uninstall-mod:     ## Remove the installed mod folder
	@test -n "$(MODS)" && test "$(MODS)" != / || { echo "MODS is empty or unsafe, refusing to remove anything"; exit 1; }
	rm -rf "$(MOD_INSTALL)"
	@echo "removed $(MOD_INSTALL)"

##
##-> Run
##

run: daemon        ## Launch the game through the runner
	$(RUNNER) --game "$(RIMWORLD)" $(if $(PROFILE),--profile "$(PROFILE)")

run-slopcar: daemon ## Run a separate profile against the running slopcar daemon (start the sidecar first)
	SLOPD_ENDPOINT="$(SLOPCAR_ENDPOINT)" \
	SLOPCAR_PROFILE="$(SLOPCAR_PROFILE)" \
	$(RUNNER) --game "$(RIMWORLD)"

mac-sidecar-ready: mac-sidecar-build
	$(SLOPCAR_ENV) tools/mac-sidecar-start.sh $(SLOPCAR_START_ARGS)

mac-run: mac-game-check mac-profile mac-mod-check mac-sidecar-ready ## Start the macOS sidecar and launch native RimWorld
	@test -f "$(SLOPCAR_ENDPOINT)" || { echo "missing sidecar endpoint: $(SLOPCAR_ENDPOINT)" >&2; exit 1; }
	@echo "launching native macOS RimWorld against $(SLOPCAR_ENDPOINT)"
	@cd "$(MAC_RESOURCES)" && \
		SLOPD_ENDPOINT="$(SLOPCAR_ENDPOINT)" \
		"$(MAC_GAME)" "-savedatafolder=$(MAC_PROFILE)" $(MAC_GAME_ARGS)

mac-mod-check:
	@test -f "$(MAC_MODS)/SlopWorld/About/About.xml" || { echo "missing SlopWorld mod; run gmake mac-install" >&2; exit 1; }

mac:               ## Install and run native macOS RimWorld with the sidecar
	MAKE_CMD="$(MAKE_BIN)" tools/mac.sh
install-mac: mac-install ## Alias for mac-install
run-mac: mac-run     ## Alias for mac-run

##
##-> Misc
##

logs:              ## Tail the game's Player.log
	@tail -f "$(LOG)"

check-reqs:        ## Print required and optional host requirements
	@RIMWORLD="$(RIMWORLD)" $(PYTHON) tools/check-reqs.py

slopcar-build:     ## Build the macOS Linux sidecar image
	$(SLOPCAR) build

slopcar-doctor:    ## Prove nested bwrap, pasta and tmux in the sidecar
	$(SLOPCAR) doctor

# Needs the `x11` preset on this project's sandbox; see tools/shot.sh.
shot:              ## Screenshot the game window into OUT
	@tools/shot.sh $(OUT)

pkg-arch:          ## Build and install Arch package
	cd packaging/arch && makepkg -p PKGBUILD.local -sif

docs:              ## Build human docs
	cd docs && mdbook build

docs-serve:        ## Serve human docs
	cd docs && mdbook serve

devloop:           ## Reinstall and relaunch after every game exit
	MAKE_CMD="$(MAKE_BIN)" tools/devloop.sh

devloop-sidecar:  ## Rebuild and redeploy the sidecar before each game launch
	MAKE_CMD="$(MAKE_BIN)" $(SLOPCAR_ENV) tools/devloop-sidecar.sh $(SLOPCAR_START_ARGS)
