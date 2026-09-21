.PHONY: mac mac-install mac-run mac-build mac-doctor mac-ready mac-start mac-stop \
	mac-status mac-logs mac-setup mac-docker-check mac-game-check mac-check \
	mac-mod mac-profile mac-mod-check

##

mac-install: mac-game-check mac-doctor install-mod ## Build the sidecar and install the mod into native macOS RimWorld
mac-install: override MANAGED := $(MAC_MANAGED)
mac-install: override MODS := $(MAC_MODS)

mac-run: mac-game-check mac-mod-check mac-ready ## Start the macOS sidecar and launch native RimWorld
	@echo "launching native macOS RimWorld against $(SLOPCAR_ENDPOINT)"
	SLOPD_ENDPOINT="$(SLOPCAR_ENDPOINT)" \
	SLOPCAR_PROFILE="$(MAC_PROFILE)" \
	"$(RUNNER)" --game-exe "$(MAC_GAME)" --working-dir "$(MAC_RESOURCES)" \
		--mods "$(MAC_MODS)" --profile "$(MAC_PROFILE)" --no-window-fix -- $(MAC_GAME_ARGS)

mac:               ## Install and run native macOS RimWorld with the sidecar
	MAKE_CMD="$(MAKE_BIN)" tools/mac.sh

mac-build: mac-docker-check sidecar-build ## Build the Linux sidecar image on macOS

mac-doctor: mac-build sidecar-doctor ## Verify nested Bubblewrap, pasta and tmux on macOS

mac-ready: mac-build
	$(SLOPCAR_ENV) tools/mac-sidecar-start.sh $(SLOPCAR_START_ARGS)

mac-start: mac-docker-check ## Start the configured macOS sidecar, reusing its container
	$(SLOPCAR_ENV) tools/mac-sidecar-start.sh $(SLOPCAR_START_ARGS)

mac-stop: mac-docker-check ## Stop the macOS sidecar without removing its state
	$(SLOPCAR_ENV) $(SLOPCAR) stop

mac-status: mac-docker-check ## Show macOS sidecar status
	$(SLOPCAR_ENV) $(SLOPCAR) status

mac-logs: mac-docker-check ## Show macOS sidecar logs; pass LOG_ARGS='--tail 100'
	$(SLOPCAR_ENV) $(SLOPCAR) logs $(LOG_ARGS)

mac-setup:        ## Install the macOS build tools and Docker Desktop with Homebrew
	@bash tools/mac-check.sh setup

mac-docker-check: ## Check Docker Desktop on macOS
	@bash tools/mac-check.sh docker-check

mac-game-check:   ## Check the native macOS RimWorld and .NET SDK paths
	@bash tools/mac-check.sh game-check

mac-check: mac-docker-check mac-game-check ## Check Docker, .NET SDK and the native macOS RimWorld install

mac-mod: mac-game-check mod ## Build SlopWorld.dll against native macOS RimWorld
mac-mod: override MANAGED := $(MAC_MANAGED)

mac-profile: daemon ## Create the isolated native macOS sidecar profile if it is absent
	"$(RUNNER)" --profile "$(MAC_PROFILE)" --init-profile --sidecar

mac-mod-check:
	@bash tools/mac-check.sh mod-check
