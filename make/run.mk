.PHONY: run run-slopcar mac-sidecar-ready mac-run mac

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

mac-run: mac-game-check mac-mod-check mac-sidecar-ready ## Start the macOS sidecar and launch native RimWorld
	@echo "launching native macOS RimWorld against $(SLOPCAR_ENDPOINT)"
	SLOPD_ENDPOINT="$(SLOPCAR_ENDPOINT)" \
	SLOPCAR_PROFILE="$(MAC_PROFILE)" \
	"$(RUNNER)" --game-exe "$(MAC_GAME)" --working-dir "$(MAC_RESOURCES)" \
		--mods "$(MAC_MODS)" --profile "$(MAC_PROFILE)" --no-window-fix -- $(MAC_GAME_ARGS)

mac-mod-check:
	@test -f "$(MAC_MODS)/SlopWorld/About/About.xml" || { echo "missing SlopWorld mod; run gmake mac-install" >&2; exit 1; }

mac:               ## Install and run native macOS RimWorld with the sidecar
	MAKE_CMD="$(MAKE_BIN)" tools/mac.sh
