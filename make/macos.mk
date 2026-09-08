.PHONY: mac-setup mac-docker-check mac-game-check mac-check \
	mac-sidecar-build mac-sidecar-doctor mac-sidecar-start mac-sidecar-stop \
	mac-sidecar-status mac-sidecar-logs

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

mac-sidecar-build: mac-docker-check slopcar-build ## Build the Linux sidecar image on macOS

mac-sidecar-doctor: mac-sidecar-build slopcar-doctor ## Verify nested Bubblewrap, pasta and tmux on macOS

mac-sidecar-start: mac-docker-check ## Start the configured macOS sidecar, reusing its container
	$(SLOPCAR_ENV) tools/mac-sidecar-start.sh $(SLOPCAR_START_ARGS)

mac-sidecar-stop: mac-docker-check ## Stop the macOS sidecar without removing its state
	$(SLOPCAR_ENV) $(SLOPCAR) stop

mac-sidecar-status: mac-docker-check ## Show macOS sidecar status
	$(SLOPCAR_ENV) $(SLOPCAR) status

mac-sidecar-logs: mac-docker-check ## Show macOS sidecar logs; pass LOG_ARGS='--tail 100'
	$(SLOPCAR_ENV) $(SLOPCAR) logs $(LOG_ARGS)
