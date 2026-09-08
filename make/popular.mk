.PHONY: all format lint test install run

##

all: daemon mod   ## Build both halves

format: format-daemon format-mod ## Format both halves

lint: lint-daemon lint-mod ## Lint both halves

test: test-daemon test-mod test-prose ## Run the daemon and game-free mod tests

install: install-daemon install-runner install-mod install-font ## Install the daemon, runner, mod and bundled font

run: daemon        ## Launch the game through the runner
	$(RUNNER) --game "$(RIMWORLD)" $(if $(PROFILE),--profile "$(PROFILE)")
