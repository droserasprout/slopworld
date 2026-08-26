.PHONY: $(MAKECMDGOALS) mod
MAKEFLAGS += --no-print-directory
##
##  🤖 SlopWorld developer tools
##
##  RIMWORLD must point at a real install; the mod builds against the game's
##  own assemblies. PROFILE picks the save data folder `run` launches into.
##  BUILD is debug or release and every target follows it, install included:
##  `make BUILD=release install`.
##
GOGDL      ?= gogdl
GOGDL_AUTH ?= $(if $(XDG_CONFIG_HOME),$(XDG_CONFIG_HOME),$(HOME)/.config)/heroic/gog_store/auth.json
GOGDL_ID   ?= 1094900565
GOGDL_PATH ?= $(HOME)/GOG Games
GOGDL_LOGIN_URL ?= https://auth.gog.com/auth?client_id=46899977096215655&redirect_uri=https%3A%2F%2Fembed.gog.com%2Fon_login_success%3Forigin%3Dclient&response_type=code&layout=client2
RIMWORLD   ?= $(GOGDL_PATH)/RimWorld/game
MANAGED    ?= $(RIMWORLD)/RimWorldLinux_Data/Managed
MODS       ?= $(RIMWORLD)/Mods
BIN        ?= $(HOME)/.local/bin
UNITS      ?= $(HOME)/.config/systemd/user
LOG        ?= $(HOME)/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log
API        ?= http://127.0.0.1:7717
TOKEN      ?=
# Save data folder, defaults to `$XDG_DATA_HOME/slopworld/profile`.
PROFILE    ?=
# `run-slopcar` wiring: the sidecar's config dir holds the endpoint descriptor its daemon
# writes (url http://127.0.0.1:7718 + token, bind-mounted to the host), and the game runs into
# a profile kept wholly apart from the native one. Point SLOPCAR_CONFIG at whatever
# SLOPCAR_CONFIG_DIR the sidecar was started with; SLOPCAR_DATA keeps its session state separate
# from a native daemon.
SLOPCAR_CONFIG   ?= $(if $(XDG_CONFIG_HOME),$(XDG_CONFIG_HOME),$(HOME)/.config)/slopworld-car
SLOPCAR_ENDPOINT ?= $(SLOPCAR_CONFIG)/endpoint.toml
SLOPCAR_PORT     ?= 7718
SLOPCAR_DATA     ?= $(if $(XDG_DATA_HOME),$(XDG_DATA_HOME),$(HOME)/.local/share)/slopworld-car
SLOPCAR_CONTAINER ?= slopcar
SLOPCAR_PROFILE  ?= $(if $(XDG_DATA_HOME),$(XDG_DATA_HOME),$(HOME)/.local/share)/slopworld-car/profile
SLOPCAR_WORKSPACE ?= $(CURDIR)
SLOPCAR_START_ARGS ?= --workspace "$(SLOPCAR_WORKSPACE)"
# Native macOS RimWorld is an app bundle, so it needs its own references, mod destination and
# direct game executable. The Steam path is the useful default; every value is overridable for a
# standalone install or a friend whose home layout differs.
MAC_RIMWORLD    ?= $(HOME)/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app
MAC_GAME        ?= $(MAC_RIMWORLD)/Contents/MacOS/RimWorldMac
MAC_RESOURCES   ?= $(MAC_RIMWORLD)/Contents/Resources
MAC_MANAGED     ?= $(MAC_RESOURCES)/Data/Managed
MAC_MODS        ?= $(MAC_RIMWORLD)/Mods
MAC_PROFILE     ?= $(HOME)/Library/Application Support/SlopWorld/sidecar-profile
MAC_CSC         ?= csc
MAC_CSC_API     ?= $(if $(shell command -v brew >/dev/null 2>&1 && brew --prefix mono 2>/dev/null),$(shell brew --prefix mono 2>/dev/null)/lib/mono/4.7.2-api,$(CSC_API))
MAC_GAME_ARGS   ?=
# Which half of the split every build, install and run target follows.
BUILD      ?= debug
CARGOFLAGS  = $(if $(filter release,$(BUILD)),--release)
TARGET      = slopd/target/$(BUILD)
RUNNER      = $(TARGET)/slopworld
SLOPCTL     = $(TARGET)/slopctl
CSC         ?= csc
CSC_API     ?= /usr/lib/mono/4.7.2-api
CSC_SOURCES := $(shell find mod/Source/SlopWorld -type f -name '*.cs' -not -path '*/obj/*' -print | sort)
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


help:              ## Show this help (default)
	@grep -Fh "##" $(MAKEFILE_LIST) | grep -Fv grep -F | sed -e 's/\\$$//' | sed -e 's/##//'

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
	brew install git mono
	brew install --cask docker-desktop
	@echo "Open Docker Desktop once, accept its setup prompts, then run: make mac-check"

mac-docker-check: ## Check Docker Desktop on macOS
	@test "$$(uname -s)" = Darwin || { echo "macOS target requires Darwin" >&2; exit 1; }
	@command -v docker >/dev/null 2>&1 || { echo "missing docker; run make mac-setup" >&2; exit 1; }
	@docker info >/dev/null 2>&1 || { echo "Docker Desktop is not running; open Docker and retry" >&2; exit 1; }

mac-game-check:   ## Check the native macOS RimWorld and Mono paths
	@test "$$(uname -s)" = Darwin || { echo "macOS target requires Darwin" >&2; exit 1; }
	@command -v "$(MAC_CSC)" >/dev/null 2>&1 || { echo "missing $(MAC_CSC); run make mac-setup" >&2; exit 1; }
	@test -f "$(MAC_CSC_API)/mscorlib.dll" || { echo "missing Mono reference assemblies under $(MAC_CSC_API); override MAC_CSC_API" >&2; exit 1; }
	@test -x "$(MAC_GAME)" || { echo "missing native RimWorld executable: $(MAC_GAME); override MAC_RIMWORLD" >&2; exit 1; }
	@test -f "$(MAC_MANAGED)/Assembly-CSharp.dll" || { echo "missing RimWorld assemblies under $(MAC_MANAGED)" >&2; exit 1; }
	@test -d "$(MAC_MODS)" || { echo "missing RimWorld Mods directory: $(MAC_MODS); override MAC_RIMWORLD" >&2; exit 1; }

mac-check:        ## Check Docker, Mono and the native macOS RimWorld install
	$(MAKE) mac-docker-check mac-game-check


mac-sidecar-build: ## Build the Linux sidecar image on macOS
	$(MAKE) mac-docker-check
	$(MAKE) slopcar-build


mac-sidecar-doctor: ## Verify nested Bubblewrap, pasta and tmux on macOS
	$(MAKE) mac-sidecar-build
	$(MAKE) slopcar-doctor

mac-sidecar-start: ## Start the configured macOS sidecar, reusing its container
	$(MAKE) mac-docker-check
	@state="$$(docker container inspect --format '{{.State.Running}}' "$(SLOPCAR_CONTAINER)" 2>/dev/null || true)"; \
	if test "$$state" = true; then \
		echo "sidecar $(SLOPCAR_CONTAINER) is already running"; \
	elif docker container inspect "$(SLOPCAR_CONTAINER)" >/dev/null 2>&1; then \
		SLOPCAR_CONFIG_DIR="$(SLOPCAR_CONFIG)" \
		SLOPCAR_DATA_DIR="$(SLOPCAR_DATA)" \
		SLOPCAR_PORT="$(SLOPCAR_PORT)" \
		SLOPCAR_CONTAINER="$(SLOPCAR_CONTAINER)" \
		./slopcar/slopcar start; \
	else \
		SLOPCAR_CONFIG_DIR="$(SLOPCAR_CONFIG)" \
		SLOPCAR_DATA_DIR="$(SLOPCAR_DATA)" \
		SLOPCAR_PORT="$(SLOPCAR_PORT)" \
		SLOPCAR_CONTAINER="$(SLOPCAR_CONTAINER)" \
		./slopcar/slopcar start $(SLOPCAR_START_ARGS); \
	fi

mac-sidecar-stop: ## Stop the macOS sidecar without removing its state
	$(MAKE) mac-docker-check
	SLOPCAR_CONFIG_DIR="$(SLOPCAR_CONFIG)" SLOPCAR_DATA_DIR="$(SLOPCAR_DATA)" \
	SLOPCAR_CONTAINER="$(SLOPCAR_CONTAINER)" ./slopcar/slopcar stop

mac-sidecar-status: ## Show macOS sidecar status
	$(MAKE) mac-docker-check
	SLOPCAR_CONFIG_DIR="$(SLOPCAR_CONFIG)" SLOPCAR_DATA_DIR="$(SLOPCAR_DATA)" \
	SLOPCAR_CONTAINER="$(SLOPCAR_CONTAINER)" ./slopcar/slopcar status

mac-sidecar-logs: ## Show macOS sidecar logs; pass LOG_ARGS='--tail 100'
	$(MAKE) mac-docker-check
	SLOPCAR_CONFIG_DIR="$(SLOPCAR_CONFIG)" SLOPCAR_DATA_DIR="$(SLOPCAR_DATA)" \
	SLOPCAR_CONTAINER="$(SLOPCAR_CONTAINER)" ./slopcar/slopcar logs $(LOG_ARGS)

##
##-> Build
##

all:               ## Build both halves
	$(MAKE) daemon mod

daemon:            ## Build the daemon and the launcher
	cd slopd && cargo build $(CARGOFLAGS)

mod:               ## Build the mod against the game's assemblies
	@test -f "$(CSC_API)/mscorlib.dll" || { echo "missing Mono reference assemblies under $(CSC_API)" >&2; exit 1; }
	@test -f "$(MANAGED)/Assembly-CSharp.dll" || { echo "missing RimWorld assemblies under $(MANAGED)" >&2; exit 1; }
	$(CSC) -nologo -noconfig -target:library -langversion:latest \
		-out:mod/Assemblies/SlopWorld.dll $(CSC_OPTIMIZE) $(CSC_WARNINGS) \
		$(CSC_REFS) $(CSC_SOURCES)

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

test:              ## Run the daemon and game-free mod tests
	cd slopd && cargo test
	dotnet run --project mod/Tests/SlopWorld.Tests.csproj --configuration Release
	$(MAKE) test-prose

coverage:          ## Measure Rust and game-free C# test coverage
	$(MAKE) coverage-daemon coverage-mod

coverage-daemon:   ## Write Rust coverage to coverage/rust.cobertura.xml
	@command -v cargo-llvm-cov >/dev/null || { echo "missing cargo-llvm-cov; install it with: cargo install cargo-llvm-cov --locked" >&2; exit 1; }
	@command -v llvm-cov >/dev/null && command -v llvm-profdata >/dev/null || { echo "missing LLVM coverage tools" >&2; exit 1; }
	@mkdir -p coverage
	cd slopd && LLVM_COV="$$(command -v llvm-cov)" LLVM_PROFDATA="$$(command -v llvm-profdata)" \
		cargo llvm-cov --cobertura --output-path ../coverage/rust.cobertura.xml
	python3 tools/coverage_summary.py coverage/rust.cobertura.xml Rust

coverage-mod:      ## Write game-free C# coverage to coverage/csharp.cobertura.xml
	dotnet tool restore
	@mkdir -p coverage
	dotnet build mod/Tests/SlopWorld.Tests.csproj --configuration Release -p:Coverage=true
	dotnet tool run coverlet -- mod/Tests/bin/Release/net8.0/SlopWorld.Tests.dll \
		--target dotnet --targetargs mod/Tests/bin/Release/net8.0/SlopWorld.Tests.dll \
		--include-test-assembly --exclude-by-file '**/mod/Tests/**/*.cs' \
		--format cobertura --output coverage/csharp.cobertura.xml
	python3 tools/coverage_summary.py coverage/csharp.cobertura.xml C\#

test-prose:        ## Test the prose linter
	python3 tools/test_prose_lint.py

appicon:           ## Regenerate the app icon (robot face + wilted rose)
	python3 tools/appicon.py

icons:             ## Rebake the action icons from a Nerd Font's Codicons
	python3 tools/icons.py

emoji-atlas:       ## Rebake the legacy terminal's emoji atlas with Pango
	python3 tools/emoji_atlas.py

reference:         ## Generate the environment/API/CLI reference
	python3 tools/reference.py

scheme-report:     ## Analyze the complete UI schemes and check Warm's luminance hierarchy
	python3 tools/analyze_ui_schemes.py --check-warm

harmony:           ## Fetch the latest Harmony release into the mod
	tools/fetch-harmony.sh

clean:             ## Drop build output
	cd slopd && cargo clean
	rm -f mod/Assemblies/SlopWorld.dll
	rm -rf mod/Source/SlopWorld/obj
	rm -rf coverage

##
##-> Format and lint
##

format:            ## Format both halves
	$(MAKE) format-daemon format-mod

format-daemon:     ## rustfmt the daemon
	cd slopd && cargo fmt

format-mod:        ## Format the mod's C# (needs the .NET SDK)
	dotnet format whitespace mod/Source/SlopWorld --folder --exclude obj

##

lint:              ## Lint both halves
	$(MAKE) lint-daemon lint-mod

lint-daemon:       ## Check the daemon's formatting, then clippy, warnings as errors
	cd slopd && cargo fmt --check
	cd slopd && cargo clippy --all-targets -- -D warnings

lint-mod:          ## Build the mod with warnings as errors, then check its formatting
	$(MAKE) BUILD=release CSC_WARNINGS=-warnaserror mod
	dotnet format whitespace mod/Source/SlopWorld --folder --exclude obj --verify-no-changes;

lint-prose:        ## Find LLM cliches in prose and source comments
	python3 tools/prose_lint.py $(PROSE_LINT_ARGS)

##
##-> Install
##

install:           ## Install all three
	$(MAKE) install-daemon install-runner install-mod

install-daemon:    ## Install the binary and the unit, restarting only when needed
	$(MAKE) daemon
	@restart=yes; \
	if systemctl --user is-active --quiet slopd.service; then \
		pid=$$(systemctl --user show --property=MainPID --value slopd.service); \
		if test "$$pid" -gt 0 2>/dev/null && cmp -s "$(TARGET)/slopd" "/proc/$$pid/exe"; then \
			restart=no; \
			echo "slopd already runs the latest $(BUILD) build; skipping restart"; \
		fi; \
	fi; \
	install -Dm755 $(TARGET)/slopd $(BIN)/slopd; \
	install -Dm755 $(SLOPCTL) $(BIN)/slopctl; \
	install -Dm644 slopd/slopd.service $(UNITS)/slopd.service; \
	systemctl --user daemon-reload; \
	systemctl --user enable --now slopd.service; \
	if test "$$restart" = yes; then systemctl --user restart slopd.service; fi; \
	systemctl --user --no-pager status slopd.service | head -3

install-runner:    ## Install the launcher beside the daemon
	$(MAKE) daemon
	install -Dm755 $(RUNNER) $(BIN)/slopworld
	@echo "installed to $(BIN)/slopworld"

install-mod:       ## Install the mod into the game's Mods folder
	$(MAKE) mod
	@test -n "$(MODS)" || { echo "MODS is empty, refusing to remove anything"; exit 1; }
	rm -rf "$(MODS)/SlopWorld"
	mkdir -p "$(MODS)/SlopWorld"
	cp -r mod/About mod/Defs mod/Patches mod/Sounds mod/Textures mod/Assemblies "$(MODS)/SlopWorld/"
	@echo "installed to $(MODS)/SlopWorld"

mac-mod:          ## Build SlopWorld.dll against native macOS RimWorld
	$(MAKE) mac-game-check
	$(MAKE) BUILD="$(BUILD)" CSC="$(MAC_CSC)" CSC_API="$(MAC_CSC_API)" MANAGED="$(MAC_MANAGED)" mod

mac-install:      ## Build the sidecar and install the mod into native macOS RimWorld
	$(MAKE) mac-game-check
	$(MAKE) mac-sidecar-doctor
	$(MAKE) BUILD="$(BUILD)" CSC="$(MAC_CSC)" CSC_API="$(MAC_CSC_API)" \
		MANAGED="$(MAC_MANAGED)" MODS="$(MAC_MODS)" install-mod

mac-profile:      ## Create the isolated native macOS sidecar profile if it is absent
	@case "$(MAC_PROFILE)" in *"="*) echo "MAC_PROFILE cannot contain '=': $(MAC_PROFILE)" >&2; exit 1;; esac
	@mkdir -p "$(MAC_PROFILE)/Config"
	@test -e "$(MAC_PROFILE)/slopworld.profile" || printf '%s\n' \
		'This is a SlopWorld profile.' \
		'The marker keeps the mod out of ordinary RimWorld saves.' \
		> "$(MAC_PROFILE)/slopworld.profile"
	@test -e "$(MAC_PROFILE)/Config/SlopWorld.toml" || printf '%s\n' \
		'uiScheme = "slopworld-warm"' \
		> "$(MAC_PROFILE)/Config/SlopWorld.toml"
	@test -e "$(MAC_PROFILE)/Config/ModsConfig.xml" || printf '%s\n' \
		'<?xml version="1.0" encoding="utf-8"?>' \
		'<ModsConfigData>' \
		'  <activeMods>' \
		'    <li>ludeon.rimworld</li>' \
		'    <li>drsr.slopworld</li>' \
		'  </activeMods>' \
		'  <knownExpansions>' \
		'    <li>ludeon.rimworld.royalty</li>' \
		'    <li>ludeon.rimworld.ideology</li>' \
		'    <li>ludeon.rimworld.biotech</li>' \
		'    <li>ludeon.rimworld.anomaly</li>' \
		'    <li>ludeon.rimworld.odyssey</li>' \
		'  </knownExpansions>' \
		'</ModsConfigData>' \
		> "$(MAC_PROFILE)/Config/ModsConfig.xml"
	@echo "macOS sidecar profile: $(MAC_PROFILE)"

##

uninstall:         ## Remove all three, keeping config and saves
	$(MAKE) uninstall-daemon uninstall-runner uninstall-mod
	@echo "left alone: ~/.config/slopworld, the profile (saves), any tmux server"
	@echo "under the slopworld socket; \`tmux -L slopworld kill-server\` ends the agents."

uninstall-daemon:  ## Stop the service, remove the binary and the unit
	-systemctl --user disable --now slopd.service
	rm -f $(UNITS)/slopd.service
	rm -f $(BIN)/slopd
	rm -f $(BIN)/slopctl
	systemctl --user daemon-reload
	@echo "removed $(BIN)/slopd, $(BIN)/slopctl and $(UNITS)/slopd.service"

uninstall-runner:  ## Remove the launcher
	rm -f $(BIN)/slopworld
	@echo "removed $(BIN)/slopworld"

uninstall-mod:     ## Remove the installed mod folder
	@test -n "$(MODS)" || { echo "MODS is empty, refusing to remove anything"; exit 1; }
	rm -rf "$(MODS)/SlopWorld"
	@echo "removed $(MODS)/SlopWorld"

##
##-> Run
##

run:               ## Launch the game through the runner
	$(MAKE) daemon
	$(RUNNER) --game "$(RIMWORLD)" $(if $(PROFILE),--profile "$(PROFILE)")

run-slopcar:       ## Run a separate profile against the running slopcar daemon (start the sidecar first)
	$(MAKE) daemon
	SLOPD_ENDPOINT="$(SLOPCAR_ENDPOINT)" \
	SLOPCAR_PROFILE="$(SLOPCAR_PROFILE)" \
	$(RUNNER) --game "$(RIMWORLD)"

mac-run:           ## Start the macOS sidecar and launch native RimWorld
	$(MAKE) mac-game-check
	$(MAKE) mac-profile
	@test -f "$(MAC_MODS)/SlopWorld/About/About.xml" || { echo "missing SlopWorld mod; run make mac-install" >&2; exit 1; }
	$(MAKE) mac-sidecar-build
	$(MAKE) mac-sidecar-start
	@test -f "$(SLOPCAR_ENDPOINT)" || { echo "missing sidecar endpoint: $(SLOPCAR_ENDPOINT)" >&2; exit 1; }
	@echo "launching native macOS RimWorld against $(SLOPCAR_ENDPOINT)"
	@cd "$(MAC_RESOURCES)" && \
		SLOPD_ENDPOINT="$(SLOPCAR_ENDPOINT)" \
		"$(MAC_GAME)" "-savedatafolder=$(MAC_PROFILE)" $(MAC_GAME_ARGS)

mac:               ## Install and run native macOS RimWorld with the sidecar
	$(MAKE) mac-install
	$(MAKE) mac-run
install-mac:       ## Alias for mac-install
	$(MAKE) mac-install
run-mac:           ## Alias for mac-run
	$(MAKE) mac-run

##
##-> Misc
##

logs:              ## Tail the game's Player.log
	@tail -f "$(LOG)"

check-reqs:        ## Print required and optional host requirements
	@RIMWORLD="$(RIMWORLD)" python3 tools/check-reqs.py

slopcar-build:     ## Build the macOS Linux sidecar image
	slopcar/slopcar build

slopcar-doctor:    ## Prove nested bwrap, pasta and tmux in the sidecar
	slopcar/slopcar doctor

# Needs the `x11` preset on this project's sandbox; see tools/shot.sh.
shot:              ## Screenshot the game window into OUT
	@tools/shot.sh $(OUT)

pkg-arch:          ## Build and install Arch package
	cd packaging/arch && makepkg -p PKGBUILD.local -sif

docs:              ## Build human docs
	cd docs && mdbook build

docs-serve:        ## Serve human docs
	cd docs && mdbook serve

devloop:
	sh -c 'while true; do make install run; sleep 1; done;'

devloop-sidecar:  ## Rebuild and redeploy the sidecar before each game launch
	sh -c 'while true; do \
		make slopcar-build; \
		SLOPCAR_CONFIG_DIR="$(SLOPCAR_CONFIG)" \
		SLOPCAR_DATA_DIR="$(SLOPCAR_DATA)" \
		SLOPCAR_CONTAINER="$(SLOPCAR_CONTAINER)" \
		slopcar/slopcar rm >/dev/null 2>&1 || true; \
		SLOPCAR_CONFIG_DIR="$(SLOPCAR_CONFIG)" \
		SLOPCAR_DATA_DIR="$(SLOPCAR_DATA)" \
		SLOPCAR_PORT="$(SLOPCAR_PORT)" \
		SLOPCAR_CONTAINER="$(SLOPCAR_CONTAINER)" \
		slopcar/slopcar start $(SLOPCAR_START_ARGS); \
		make run-slopcar; \
		sleep 1; \
	done;'
