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
RIMWORLD   ?= $(HOME)/GOG Games/RimWorld/game
MANAGED    ?= $(RIMWORLD)/RimWorldLinux_Data/Managed
MODS       ?= $(RIMWORLD)/Mods
BIN        ?= $(HOME)/.local/bin
UNITS      ?= $(HOME)/.config/systemd/user
LOG        ?= $(HOME)/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log
API        ?= http://127.0.0.1:7717
TOKEN      ?=
# Save data folder, defaults to `$XDG_DATA_HOME/slopworld/profile`.
PROFILE    ?=
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

appicon:           ## Regenerate the app icon (robot face + wilted rose)
	python3 tools/appicon.py

icons:             ## Rebake the action icons from a Nerd Font's Codicons
	python3 tools/icons.py

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

##
##-> Misc
##

logs:              ## Tail the game's Player.log
	@tail -f "$(LOG)"

check-reqs:        ## Print required and optional host requirements
	@RIMWORLD="$(RIMWORLD)" sh tools/check-reqs.sh

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
