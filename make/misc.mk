.PHONY: logs check-reqs pkg-arch docs docs-serve devloop

##

logs:              ## Tail the game's Player.log
	@tail -f "$(LOG)"

.PHONY: trace-mod
TRACE_SECONDS ?= 30
TRACE_LABEL ?= current
TRACE_OUT ?= notes/trace-$(TRACE_LABEL).log
trace-mod:         ## Capture new mod perf lines; launch with SLOPWORLD_DEBUG=1 first
	@$(PYTHON) tools/trace-mod.py --log "$(LOG)" --seconds "$(TRACE_SECONDS)" --label "$(TRACE_LABEL)" --output "$(TRACE_OUT)"

.PHONY: trace-summary
TRACE_FILES ?= notes/trace-*.log
trace-summary:     ## Summarize captured windows by observed Eco/terminal/session/size state
	@$(PYTHON) tools/trace-summary.py $(TRACE_FILES)

check-reqs:        ## Print required and optional host requirements
	@RIMWORLD="$(RIMWORLD)" $(PYTHON) tools/check-reqs.py

pkg-arch:          ## Build and install Arch package
	cd packaging/arch && makepkg -p PKGBUILD.local -sif

docs: api-docs     ## Build human docs
	cd docs && mdbook build

docs-serve:        ## Serve human docs
	cd docs && mdbook serve

devloop:           ## Reinstall and relaunch after every game exit
	MAKE_CMD="$(MAKE_BIN)" tools/devloop.sh
