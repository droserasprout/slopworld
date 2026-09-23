.PHONY: sidecar-run sidecar-build sidecar-doctor sidecar-devloop

## Linux sidecar

sidecar-run: daemon ## Run a separate profile against the running sidecar daemon (start the sidecar first)
	SLOPD_ENDPOINT="$(SLOPCAR_ENDPOINT)" \
	SLOPCAR_PROFILE="$(SLOPCAR_PROFILE)" \
	$(RUNNER) --game "$(RIMWORLD)"

sidecar-build:     ## Build the Linux sidecar image
	$(SLOPCAR) build

sidecar-doctor:    ## Check nested Bubblewrap, pasta, and tmux in the sidecar
	$(SLOPCAR) doctor

sidecar-devloop:   ## Rebuild and redeploy the sidecar before each game launch
	MAKE_CMD="$(MAKE_BIN)" $(SLOPCAR_ENV) tools/devloop-sidecar.sh $(SLOPCAR_START_ARGS)
