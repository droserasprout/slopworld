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
CONFIG      = $(if $(filter release,$(BUILD)),Release,Debug)
TARGET      = slopd/target/$(BUILD)
RUNNER      = $(TARGET)/slopworld
SLOPCTL     = $(TARGET)/slopctl
CSPROJ      = mod/Source/SlopWorld/SlopWorld.csproj


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
	cd mod/Source/SlopWorld && msbuild -restore -v:minimal -p:Configuration=$(CONFIG) \
		-p:RimWorldManaged="$(MANAGED)" SlopWorld.csproj

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

test:              ## Run the daemon's tests; the mod needs the game
	cd slopd && cargo test

appicon:           ## Regenerate the app icon (robot face + wilted rose)
	python3 tools/appicon.py

icons:             ## Rebake the action icons from a Nerd Font's Codicons
	python3 tools/icons.py

reference:         ## Generate the environment/API/CLI reference
	python3 tools/reference.py

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
	cd mod/Source/SlopWorld && msbuild -restore -v:minimal -t:Rebuild \
		-p:Configuration=Release -p:RimWorldManaged="$(MANAGED)" \
		-p:TreatWarningsAsErrors=true SlopWorld.csproj
	dotnet format whitespace mod/Source/SlopWorld --folder --exclude obj --verify-no-changes;

##
##-> Install
##

install:           ## Install all three
	$(MAKE) install-daemon install-runner install-mod

install-daemon:    ## Install the binary and the unit, restart the service
	$(MAKE) daemon
	install -Dm755 $(TARGET)/slopd $(BIN)/slopd
	install -Dm755 $(SLOPCTL) $(BIN)/slopctl
	install -Dm644 slopd/slopd.service $(UNITS)/slopd.service
	systemctl --user daemon-reload
	systemctl --user enable --now slopd.service
	systemctl --user restart slopd.service
	@systemctl --user --no-pager status slopd.service | head -3

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
	$(RUNNER) --game "$(RIMWORLD)" $(if $(PROFILE),--profile "$(PROFILE)") \
		-popupwindow -force-opengl

##
##-> Misc
##

redeploy:          ## Install both halves, then bounce the game
	$(MAKE) install
	@curl -fsS -X POST "$(API)/api/game/restart" \
		-H "x-slop-token: $(TOKEN)" -H "content-type: application/json" \
		-d '{"delay_ms":4000}' >/dev/null \
		&& echo "game restart requested" \
		|| echo "game not restarted (is daemon.game_cmd set, and the game running?)"

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
